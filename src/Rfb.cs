using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace RemoConsole
{
    class AuthRequiredException : Exception
    {
        public AuthRequiredException(string m) : base(m) { }
    }

    /// <summary>Feeds the persistent ZRLE inflate stream with the compressed bytes of each rectangle.</summary>
    class FeedStream : Stream
    {
        byte[] buf = new byte[1 << 16];
        int pos, len;
        public void Append(byte[] d, int off, int n)
        {
            if (pos > 0) { Buffer.BlockCopy(buf, pos, buf, 0, len - pos); len -= pos; pos = 0; }
            if (len + n > buf.Length) Array.Resize(ref buf, Math.Max(buf.Length * 2, len + n));
            Buffer.BlockCopy(d, off, buf, len, n);
            len += n;
        }
        public override int Read(byte[] b, int off, int count)
        {
            int n = Math.Min(count, len - pos);
            if (n <= 0) return 0;
            Buffer.BlockCopy(buf, pos, b, off, n);
            pos += n;
            return n;
        }
        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) { throw new NotSupportedException(); }
        public override void SetLength(long v) { throw new NotSupportedException(); }
        public override void Write(byte[] b, int o, int c) { throw new NotSupportedException(); }
    }

    /// <summary>
    /// RFB (VNC) client written from scratch. Protocol 3.3/3.7/3.8, security None + VNC Auth,
    /// encodings Raw, CopyRect, RRE, Hextile, ZRLE, DesktopSize. Framebuffer is 32bpp BGRX.
    /// </summary>
    class RfbClient
    {
        public string HostName; public int Port; public string Password;
        public int W, H;
        public byte[] Fb = new byte[4];
        public readonly object FbLock = new object();
        public string DesktopName = "";

        public event Action Connected;
        public event Action Updated;
        public event Action Resized;
        public event Action<string> CutText;
        public event Action<string, bool> Closed;   // message, authRequired
        /// <summary>Server cursor shape: BGRX pixels, 1bpp mask (row-padded), w, h, hotspot x, y. w/h 0 = hidden.</summary>
        public event Action<byte[], byte[], int, int, int, int> CursorShape;

        /// <summary>Minimum time between framebuffer update requests (0 = as fast as the server sends). Used to throttle previews.</summary>
        public int MinFrameMs { get { return minFrameMs; } set { minFrameMs = Math.Max(0, value); try { wake.Set(); } catch { } } }
        volatile int minFrameMs;
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly ManualResetEvent stopEv = new ManualResetEvent(false);
        volatile bool localCursor;

        TcpClient tcp; NetworkStream ns; Stream ins; Thread th; volatile bool stop;
        readonly object sendLock = new object();
        readonly byte[] sc = new byte[4];           // scratch for small reads (reader thread only)
        FeedStream zfeed; DeflateStream zr; int zHeaderBytesLeft;
        readonly byte[] zbuf = new byte[1 << 16]; int zpos, zlen;
        uint[] ztile = new uint[64 * 64], zpal = new uint[128];
        byte[] zraw = new byte[64 * 64 * 4], zout = new byte[64 * 64 * 4];

        public void Start()
        {
            th = new Thread(Run) { IsBackground = true, Name = "rfb-" + HostName };
            th.Start();
        }

        public void Stop()
        {
            stop = true;
            try { stopEv.Set(); } catch { }
            try { wake.Set(); } catch { }
            try { if (tcp != null) tcp.Close(); } catch { }
        }

        /// <summary>
        /// true: ask the server for the cursor shape (drawn locally by Windows, no lag).
        /// false: the server paints the cursor into the framebuffer (visible in previews / view-only).
        /// </summary>
        public bool LocalCursor
        {
            get { return localCursor; }
            set
            {
                if (localCursor == value) return;
                localCursor = value;
                if (ns != null) try { SendEncodings(); } catch { }
            }
        }

        void SendEncodings()
        {
            var list = new List<int> { 16, 5, 1, 2, 0, -223, -307 };   // ZRLE, Hextile, CopyRect, RRE, Raw, DesktopSize, DesktopName
            if (localCursor) list.Add(-239);                           // Cursor pseudo-encoding
            byte[] se = new byte[4 + 4 * list.Count];
            se[0] = 2; P16(se, 2, list.Count);
            for (int i = 0; i < list.Count; i++) P32(se, 4 + 4 * i, unchecked((uint)list[i]));
            Send(se);
        }

        void Run()
        {
            string err = null; bool auth = false;
            try
            {
                tcp = new TcpClient();
                var ar = tcp.BeginConnect(HostName, Port, null, null);
                int idx = WaitHandle.WaitAny(new WaitHandle[] { ar.AsyncWaitHandle, stopEv }, 8000);
                if (idx != 0) { try { tcp.Close(); } catch { } if (stop) return; throw new IOException("Connection timed out"); }
                tcp.EndConnect(ar);
                if (stop) return;
                tcp.NoDelay = true;
                tcp.ReceiveBufferSize = 1 << 20;
                ns = tcp.GetStream();
                ins = new BufferedStream(ns, 1 << 16); // reads only; writes go straight to ns
                Handshake();
                var h = Connected; if (h != null) h();
                Loop();
            }
            catch (AuthRequiredException ex) { err = ex.Message; auth = true; }
            catch (Exception ex) { if (!stop) err = ex.Message; }
            finally
            {
                try { if (zr != null) zr.Dispose(); } catch { }
                try { if (zfeed != null) zfeed.Dispose(); } catch { }
                try { if (ins != null) ins.Dispose(); } catch { }
                try { if (ns != null) ns.Dispose(); } catch { }
                try { if (tcp != null) tcp.Close(); } catch { }
                var c = Closed; if (c != null) c(err, auth);
            }
        }

        // ---------- IO helpers ----------
        static void ReadExact(Stream s, byte[] b, int off, int n)
        {
            while (n > 0)
            {
                int r = s.Read(b, off, n);
                if (r <= 0) throw new EndOfStreamException("Connection closed");
                off += r; n -= r;
            }
        }
        byte[] Rd(int n) { byte[] b = new byte[n]; ReadExact(ins, b, 0, n); return b; }
        void RdInto(byte[] b, int n) { ReadExact(ins, b, 0, n); }
        void Skip(int n) { byte[] t = new byte[Math.Min(n, 8192)]; while (n > 0) { int k = Math.Min(n, t.Length); ReadExact(ins, t, 0, k); n -= k; } }
        int U8() { int b = ins.ReadByte(); if (b < 0) throw new EndOfStreamException("Connection closed"); return b; }
        int U16() { ReadExact(ins, sc, 0, 2); return (sc[0] << 8) | sc[1]; }
        uint U32() { ReadExact(ins, sc, 0, 4); return ((uint)sc[0] << 24) | ((uint)sc[1] << 16) | ((uint)sc[2] << 8) | sc[3]; }
        void Send(byte[] b) { lock (sendLock) { if (stop || ns == null) return; ns.Write(b, 0, b.Length); } }
        static void P16(byte[] b, int o, int v) { b[o] = (byte)(v >> 8); b[o + 1] = (byte)v; }
        static void P32(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

        // ---------- handshake ----------
        void Handshake()
        {
            string ver = Encoding.ASCII.GetString(Rd(12));
            if (!ver.StartsWith("RFB ")) throw new IOException("Not a VNC server (bad banner)");
            int major = 3, minor = 3;
            int.TryParse(ver.Substring(4, 3), out major);
            int.TryParse(ver.Substring(8, 3), out minor);
            int use = (major > 3 || minor >= 8) ? 8 : (minor >= 7 ? 7 : 3);
            Send(Encoding.ASCII.GetBytes("RFB 003.00" + use + "\n"));

            int sec = 0;
            if (use == 3)
            {
                sec = (int)U32();
                if (sec == 0) throw new IOException(ReadReason());
            }
            else
            {
                int n = U8();
                if (n == 0) throw new IOException(ReadReason());
                byte[] types = Rd(n);
                bool none = Array.IndexOf(types, (byte)1) >= 0, vnc = Array.IndexOf(types, (byte)2) >= 0;
                if (vnc && (!none || !string.IsNullOrEmpty(Password))) sec = 2;
                else if (none) sec = 1;
                else
                {
                    var sb = new StringBuilder();
                    foreach (byte t in types) sb.Append(t).Append(' ');
                    throw new IOException("Unsupported VNC security type(s): " + sb.ToString().Trim() + " (only None and VNC password are supported)");
                }
                Send(new byte[] { (byte)sec });
            }

            if (sec == 2)
            {
                if (string.IsNullOrEmpty(Password)) { throw new AuthRequiredException("Password required"); }
                byte[] ch = Rd(16);
                Send(Des.VncResponse(Password, ch));
            }
            else if (sec != 1) throw new IOException("Unsupported VNC security type " + sec);

            if (sec == 2 || use == 8)
            {
                uint res = U32();
                if (res != 0)
                {
                    string why = use == 8 ? ReadReason() : "Authentication failed";
                    throw new AuthRequiredException(string.IsNullOrEmpty(why) ? "Authentication failed" : why);
                }
            }

            Send(new byte[] { 1 }); // ClientInit: shared
            int w = U16(), h = U16();
            Rd(16); // server pixel format, we override
            int nl = (int)U32();
            if (nl < 0 || nl > 4096) throw new IOException("Bad desktop name length");
            DesktopName = Encoding.UTF8.GetString(Rd(nl));
            Resize(w, h);

            // SetPixelFormat: 32bpp depth 24 little-endian true colour, shifts R16 G8 B0
            byte[] pf = new byte[20];
            pf[0] = 0; pf[4] = 32; pf[5] = 24; pf[6] = 0; pf[7] = 1;
            P16(pf, 8, 255); P16(pf, 10, 255); P16(pf, 12, 255);
            pf[14] = 16; pf[15] = 8; pf[16] = 0;
            Send(pf);
            SendEncodings();
            RequestUpdate(false);
        }

        string ReadReason()
        {
            int n = (int)U32();
            if (n < 0 || n > 65536) return "Server refused connection";
            return Encoding.UTF8.GetString(Rd(n));
        }

        void Resize(int w, int h)
        {
            if (w <= 0 || h <= 0 || (long)w * h > 100000000L) throw new IOException("Bad desktop size " + w + "x" + h);
            lock (FbLock) { W = w; H = h; Fb = new byte[w * h * 4]; }
            var r = Resized; if (r != null) r();
        }

        // ---------- main loop ----------
        void RequestUpdate(bool inc)
        {
            byte[] b = new byte[10];
            b[0] = 3; b[1] = (byte)(inc ? 1 : 0);
            P16(b, 6, W); P16(b, 8, H);
            Send(b);
        }

        void Loop()
        {
            while (!stop)
            {
                int t = U8();
                switch (t)
                {
                    case 0: FramebufferUpdate(); break;
                    case 1: // colour map: unsupported for true colour, skip
                        Rd(1); U16(); { int n = U16(); Rd(n * 6); }
                        break;
                    case 2: break; // bell
                    case 3:
                        Rd(3);
                        int len = (int)U32();
                        if (len < 0 || len > 16 * 1024 * 1024) throw new IOException("Bad clipboard length");
                        string text = Encoding.GetEncoding(28591).GetString(Rd(len));
                        var ct = CutText; if (ct != null) ct(text);
                        break;
                    default: throw new IOException("Unknown server message " + t);
                }
            }
        }

        void FramebufferUpdate()
        {
            U8();
            int n = U16();
            bool resized = false;
            for (int i = 0; i < n; i++)
            {
                int x = U16(), y = U16(), w = U16(), h = U16();
                int enc = (int)U32();
                if (enc == -223) { Resize(w, h); resized = true; continue; }
                if (enc == -239) { DecCursor(x, y, w, h); continue; }
                if (enc == -307)
                {
                    int nl = (int)U32();
                    if (nl < 0 || nl > 65536) throw new IOException("Bad desktop name length");
                    DesktopName = Encoding.UTF8.GetString(Rd(nl));
                    continue;
                }
                if (x + w > W || y + h > H) throw new IOException("Rectangle outside framebuffer");
                switch (enc)
                {
                    case 0: DecRaw(x, y, w, h); break;
                    case 1: DecCopy(x, y, w, h); break;
                    case 2: DecRre(x, y, w, h); break;
                    case 5: DecHextile(x, y, w, h); break;
                    case 16: DecZrle(x, y, w, h); break;
                    default: throw new IOException("Unsupported encoding " + enc);
                }
            }
            var u = Updated; if (u != null) u();

            // Throttle (previews): wait before asking for the next frame. Changing MinFrameMs wakes us early.
            int min = minFrameMs;
            if (min > 0 && !resized)
            {
                var until = DateTime.UtcNow.AddMilliseconds(min);
                while (!stop && minFrameMs > 0)
                {
                    int left = (int)(until - DateTime.UtcNow).TotalMilliseconds;
                    if (left <= 0) break;
                    wake.WaitOne(left);
                }
            }
            if (stop) return;
            RequestUpdate(!resized);
        }

        void DecCursor(int hx, int hy, int w, int h)
        {
            if (w > 256 || h > 256) throw new IOException("Cursor too large");
            byte[] px = w * h > 0 ? Rd(w * h * 4) : new byte[0];
            byte[] mask = w * h > 0 ? Rd(((w + 7) / 8) * h) : new byte[0];
            var cs = CursorShape; if (cs != null) cs(px, mask, w, h, hx, hy);
        }

        // ---------- pixel helpers ----------
        void Blit(int x, int y, int w, int h, byte[] src, int stride)
        {
            for (int row = 0; row < h; row++)
                Buffer.BlockCopy(src, row * stride, Fb, ((y + row) * W + x) * 4, w * 4);
        }

        void Fill(int x, int y, int w, int h, byte b, byte g, byte r)
        {
            if (w <= 0 || h <= 0) return;
            byte[] fb = Fb;
            int first = (y * W + x) * 4;
            for (int i = 0, o = first; i < w; i++, o += 4) { fb[o] = b; fb[o + 1] = g; fb[o + 2] = r; fb[o + 3] = 255; }
            for (int yy = 1; yy < h; yy++)
                Buffer.BlockCopy(fb, first, fb, ((y + yy) * W + x) * 4, w * 4);
        }

        void DecRaw(int x, int y, int w, int h)
        {
            if (w == 0 || h == 0) return;
            byte[] fb = Fb;   // read straight into the framebuffer, row by row: no temp buffer
            for (int row = 0; row < h; row++) ReadExact(ins, fb, ((y + row) * W + x) * 4, w * 4);
        }

        void DecCopy(int x, int y, int w, int h)
        {
            int sx = U16(), sy = U16();
            if (sx + w > W || sy + h > H) throw new IOException("CopyRect source outside framebuffer");
            byte[] tmp = new byte[w * h * 4];
            for (int r = 0; r < h; r++) Buffer.BlockCopy(Fb, ((sy + r) * W + sx) * 4, tmp, r * w * 4, w * 4);
            Blit(x, y, w, h, tmp, w * 4);
        }

        void DecRre(int x, int y, int w, int h)
        {
            uint n = U32();
            if (n > 1000000) throw new IOException("Too many RRE subrects");
            byte[] bg = Rd(4), p = new byte[4];
            Fill(x, y, w, h, bg[0], bg[1], bg[2]);
            for (uint i = 0; i < n; i++)
            {
                RdInto(p, 4);
                int sx = U16(), sy = U16(), sw = U16(), sh = U16();
                if (sx + sw > w || sy + sh > h) throw new IOException("RRE subrect outside rect");
                Fill(x + sx, y + sy, sw, sh, p[0], p[1], p[2]);
            }
        }

        void DecHextile(int x, int y, int w, int h)
        {
            byte[] bg = new byte[4], fg = new byte[4], sub4 = new byte[4];
            for (int ty = 0; ty < h; ty += 16)
                for (int tx = 0; tx < w; tx += 16)
                {
                    int tw = Math.Min(16, w - tx), th = Math.Min(16, h - ty);
                    int sub = U8();
                    if ((sub & 1) != 0)
                    {
                        byte[] raw = Rd(tw * th * 4);
                        Blit(x + tx, y + ty, tw, th, raw, tw * 4);
                        continue;
                    }
                    if ((sub & 2) != 0) RdInto(bg, 4);
                    if ((sub & 4) != 0) RdInto(fg, 4);
                    Fill(x + tx, y + ty, tw, th, bg[0], bg[1], bg[2]);
                    if ((sub & 8) != 0)
                    {
                        int ns_ = U8();
                        bool colored = (sub & 16) != 0;
                        for (int i = 0; i < ns_; i++)
                        {
                            byte[] c = fg;
                            if (colored) { RdInto(sub4, 4); c = sub4; }
                            int xy = U8(), wh = U8();
                            int sx = xy >> 4, sy = xy & 15, sw = (wh >> 4) + 1, sh = (wh & 15) + 1;
                            if (sx + sw > tw || sy + sh > th) throw new IOException("Hextile subrect outside tile");
                            Fill(x + tx + sx, y + ty + sy, sw, sh, c[0], c[1], c[2]);
                        }
                    }
                }
        }

        // ---------- ZRLE ----------
        // Inflated bytes are pulled through a local buffer: DeflateStream.ReadByte allocates per call.
        bool ZFill()
        {
            zpos = 0; zlen = zr.Read(zbuf, 0, zbuf.Length);
            return zlen > 0;
        }
        int ZByte()
        {
            if (zpos >= zlen && !ZFill()) throw new IOException("ZRLE stream ended unexpectedly");
            return zbuf[zpos++];
        }
        void ZRead(byte[] b, int n)
        {
            int off = 0;
            while (n > 0)
            {
                if (zpos >= zlen && !ZFill()) throw new IOException("ZRLE stream ended unexpectedly");
                int k = Math.Min(n, zlen - zpos);
                Buffer.BlockCopy(zbuf, zpos, b, off, k);
                zpos += k; off += k; n -= k;
            }
        }

        void DecZrle(int x, int y, int w, int h)
        {
            int len = (int)U32();
            if (len < 0 || len > 64 * 1024 * 1024) throw new IOException("Bad ZRLE length");
            byte[] comp = Rd(len);
            if (zfeed == null) { zfeed = new FeedStream(); zr = new DeflateStream(zfeed, CompressionMode.Decompress); zHeaderBytesLeft = 2; zpos = zlen = 0; }
            int skip = Math.Min(zHeaderBytesLeft, len);
            zHeaderBytesLeft -= skip;
            if (len > skip) zfeed.Append(comp, skip, len - skip);

            uint[] tile = ztile, pal = zpal;
            byte[] cp = new byte[3];
            byte[] outbuf = zout;

            for (int ty = 0; ty < h; ty += 64)
                for (int tx = 0; tx < w; tx += 64)
                {
                    int tw = Math.Min(64, w - tx), th = Math.Min(64, h - ty), total = tw * th;
                    int sub = ZByte();
                    bool rle = (sub & 128) != 0;
                    int ps = sub & 127;

                    if (!rle)
                    {
                        if (ps == 0)
                        {
                            ZRead(zraw, total * 3);   // whole raw tile in one go
                            for (int i = 0, o = 0; i < total; i++, o += 3) tile[i] = (uint)(zraw[o] | (zraw[o + 1] << 8) | (zraw[o + 2] << 16));
                        }
                        else if (ps == 1)
                        {
                            uint c = ZPixel(cp);
                            for (int i = 0; i < total; i++) tile[i] = c;
                        }
                        else if (ps <= 16)
                        {
                            for (int i = 0; i < ps; i++) pal[i] = ZPixel(cp);
                            int bpp = ps <= 2 ? 1 : (ps <= 4 ? 2 : 4);
                            int mask = (1 << bpp) - 1;
                            for (int r = 0; r < th; r++)
                            {
                                int bits = 0, have = 0;
                                for (int c = 0; c < tw; c++)
                                {
                                    if (have == 0) { bits = ZByte(); have = 8; }
                                    have -= bpp;
                                    int idx = (bits >> have) & mask;
                                    if (idx >= ps) throw new IOException("ZRLE palette index out of range");
                                    tile[r * tw + c] = pal[idx];
                                }
                            }
                        }
                        else throw new IOException("Bad ZRLE subencoding " + sub);
                    }
                    else
                    {
                        int pos = 0;
                        if (ps == 0)
                        {
                            while (pos < total)
                            {
                                uint c = ZPixel(cp);
                                int run = 1, b;
                                do { b = ZByte(); run += b; if (pos + run > total) throw new IOException("ZRLE run overflow"); } while (b == 255);
                                for (int i = 0; i < run; i++) tile[pos++] = c;
                            }
                        }
                        else
                        {
                            if (ps == 1 || ps > 127) throw new IOException("Bad ZRLE palette size " + ps);
                            for (int i = 0; i < ps; i++) pal[i] = ZPixel(cp);
                            while (pos < total)
                            {
                                int idx = ZByte();
                                int run = 1;
                                if ((idx & 128) != 0)
                                {
                                    idx &= 127;
                                    int b;
                                    do { b = ZByte(); run += b; if (pos + run > total) throw new IOException("ZRLE run overflow"); } while (b == 255);
                                }
                                if (idx >= ps) throw new IOException("ZRLE palette index out of range");
                                if (pos + run > total) throw new IOException("ZRLE run overflow");
                                uint c = pal[idx];
                                for (int i = 0; i < run; i++) tile[pos++] = c;
                            }
                        }
                    }

                    for (int i = 0; i < total; i++)
                    {
                        uint c = tile[i];
                        outbuf[i * 4] = (byte)c; outbuf[i * 4 + 1] = (byte)(c >> 8); outbuf[i * 4 + 2] = (byte)(c >> 16); outbuf[i * 4 + 3] = 255;
                    }
                    Blit(x + tx, y + ty, tw, th, outbuf, tw * 4);
                }
        }

        uint ZPixel(byte[] cp)
        {
            ZRead(cp, 3); // CPIXEL: B,G,R
            return (uint)(cp[0] | (cp[1] << 8) | (cp[2] << 16));
        }

        // ---------- input ----------
        public void Pointer(int x, int y, int mask)
        {
            if (ns == null) return;
            byte[] b = new byte[6];
            b[0] = 5; b[1] = (byte)mask; P16(b, 2, Math.Max(0, Math.Min(65535, x))); P16(b, 4, Math.Max(0, Math.Min(65535, y)));
            try { Send(b); } catch { }
        }

        public void Key(uint keysym, bool down)
        {
            if (ns == null) return;
            byte[] b = new byte[8];
            b[0] = 4; b[1] = (byte)(down ? 1 : 0); P32(b, 4, keysym);
            try { Send(b); } catch { }
        }

        public void SendClipboard(string text)
        {
            if (ns == null || text == null) return;
            byte[] data = Encoding.GetEncoding(28591).GetBytes(text);
            byte[] b = new byte[8 + data.Length];
            b[0] = 6; P32(b, 4, (uint)data.Length);
            Buffer.BlockCopy(data, 0, b, 8, data.Length);
            try { Send(b); } catch { }
        }
    }
}
