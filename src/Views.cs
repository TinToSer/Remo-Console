using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace RemoConsole
{
    enum ViewState { Idle, Connecting, Connected, Failed }

    /// <summary>One remote session (RDP or VNC) as a WinForms control. Can live in a card (preview) or a tab (interactive).</summary>
    abstract class RemoteView : Panel
    {
        public Host Host;
        public ViewState State = ViewState.Idle;
        public string Error = "";
        public bool NeedAuth;
        public DateTime NextRetryUtc = DateTime.MinValue;
        bool interactive;

        public event EventHandler StateChanged;
        public event EventHandler PreviewActivated;   // double-click while non-interactive

        protected RemoteView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Black;
        }

        public bool Interactive
        {
            get { return interactive; }
            set { interactive = value; OnInteractiveChanged(); }
        }
        protected virtual void OnInteractiveChanged() { }

        protected void RaiseActivated() { var h = PreviewActivated; if (h != null) h(this, EventArgs.Empty); }

        protected void SetState(ViewState s, string err, bool auth = false)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)(() => SetState(s, err, auth))); } catch { }
                return;
            }
            State = s; Error = err ?? ""; NeedAuth = auth;
            var h = StateChanged; if (h != null) h(this, EventArgs.Empty);
            Invalidate();
        }

        public abstract void Start(string password);
        public abstract void Stop();
        /// <summary>Scaled copy of the current remote screen, or null if unavailable.</summary>
        public abstract Bitmap Snapshot(int maxWidth);
        public virtual void SendCtrlAltDel() { }
        public virtual void FocusInput() { Focus(); }

        protected void PaintStatus(Graphics g)
        {
            g.Clear(Theme.Input);
            string t;
            switch (State)
            {
                case ViewState.Connecting: t = "Connecting…"; break;
                case ViewState.Failed: t = string.IsNullOrEmpty(Error) ? "Disconnected" : Error; break;
                default: t = "Not connected"; break;
            }
            bool big = Width > Theme.S(500);
            Color c = State == ViewState.Failed ? Theme.Bad : State == ViewState.Connecting ? Theme.Warn : Theme.Dim;
            int iconH = big ? Theme.S(52) : Theme.S(26);
            var iconFont = big ? Theme.IconBig : Theme.Icon;
            int total = iconH + Theme.S(8) + (big ? Theme.S(60) : Theme.S(36));
            int top = Math.Max(0, (Height - total) / 2);
            Theme.DrawIcon(g, State == ViewState.Failed ? Glyph.Warning : Glyph.Monitor, new Rectangle(0, top, Width, iconH), c, iconFont);
            var tr = new Rectangle(Theme.S(16), top + iconH + Theme.S(8), Width - Theme.S(32), Height - top - iconH - Theme.S(8));
            TextRenderer.DrawText(g, t, big ? Theme.Ui : Theme.Small, tr, State == ViewState.Failed ? Color.FromArgb(235, 150, 150) : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    // =====================================================================
    //  VNC
    // =====================================================================
    class VncView : RemoteView
    {
        RfbClient c;
        Bitmap bmp; GCHandle pin; byte[] pinned;
        readonly object bmpLock = new object();
        volatile bool dirty;
        readonly Timer tick = new Timer();
        int tickCount;
        int buttons;
        string lastClip = "";
        readonly Dictionary<int, uint> down = new Dictionary<int, uint>();

        public VncView()
        {
            tick.Interval = 33;
            tick.Tick += delegate
            {
                tickCount++;
                if (dirty && (Interactive || tickCount % 8 == 0)) { dirty = false; Invalidate(); }
            };
            tick.Start();
            TabStop = true;
        }

        public override void Start(string password)
        {
            Stop();
            SetState(ViewState.Connecting, "");
            c = new RfbClient { HostName = Host.Address, Port = Host.Port, Password = password };
            var mine = c;
            ApplyStreamMode();
            c.Connected += delegate { if (c == mine) SetState(ViewState.Connected, ""); };
            c.Updated += delegate { dirty = true; };
            c.Resized += delegate { dirty = true; };
            c.CursorShape += delegate(byte[] px, byte[] mask, int w, int h, int hx, int hy)
            {
                if (IsDisposed) return;
                try { BeginInvoke((Action)(() => { if (c == mine) SetRemoteCursor(px, mask, w, h, hx, hy); })); } catch { }
            };
            c.CutText += delegate(string t)
            {
                if (IsDisposed || !Interactive || Host.ViewOnly) return;
                try { BeginInvoke((Action)(() => { try { lastClip = t; if (t.Length > 0) Clipboard.SetText(t); } catch { } })); } catch { }
            };
            c.Closed += delegate(string msg, bool auth)
            {
                if (c != mine) return;
                SetState(msg == null ? ViewState.Idle : ViewState.Failed, msg ?? "", auth);
            };
            c.Start();
        }

        public override void Stop()
        {
            var old = c; c = null;
            if (old != null) old.Stop();
            ReleaseAllKeys();
            if (State != ViewState.Failed) State = ViewState.Idle;
            lock (bmpLock) { FreeBmp(); }
            ClearRemoteCursor();
            Invalidate();
        }

        /// <summary>Previews: ~3 fps and server-drawn cursor. Interactive: full rate and a local (lag-free) cursor.</summary>
        void ApplyStreamMode()
        {
            var cl = c; if (cl == null) return;
            bool live = Interactive && !Host.ViewOnly;
            cl.MinFrameMs = Interactive ? 0 : 300;
            cl.LocalCursor = live;
            if (!live) ClearRemoteCursor();
        }

        protected override void OnInteractiveChanged() { ApplyStreamMode(); }

        // ---- remote cursor shape -> real Windows cursor ----
        [StructLayout(LayoutKind.Sequential)]
        struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }
        [DllImport("user32.dll")] static extern IntPtr CreateIconIndirect(ref ICONINFO info);
        [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO info);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] static extern bool DestroyCursor(IntPtr h);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
        IntPtr hCursor; Cursor remoteCursor;
        byte[] curPx, curMask; int curW, curH, curHx, curHy; float curScale;

        void SetRemoteCursor(byte[] px, byte[] mask, int w, int h, int hx, int hy)
        {
            if (w == 0 || h == 0) { px = new byte[4]; mask = new byte[1]; w = h = 1; hx = hy = 0; } // server hides the cursor
            curPx = px; curMask = mask; curW = w; curH = h; curHx = hx; curHy = hy; curScale = 0;
            BuildCursor();
        }

        void BuildCursor()
        {
            var cl = c;
            if (curPx == null || cl == null || cl.W <= 0 || !Interactive || Host.ViewOnly) return;
            float s = Math.Max(0.5f, Math.Min(2f, DestRect(cl).Width / (float)cl.W));
            if (Math.Abs(s - curScale) < 0.01f && remoteCursor != null) return;
            curScale = s;
            int stride = (curW + 7) / 8;
            if (curPx.Length < curW * curH * 4 || curMask == null || curMask.Length < stride * curH) return;
            IntPtr old = hCursor; hCursor = IntPtr.Zero;
            var oldCur = remoteCursor; remoteCursor = null;
            using (var src = new Bitmap(curW, curH, PixelFormat.Format32bppArgb))
            {
                for (int y = 0; y < curH; y++)
                    for (int x = 0; x < curW; x++)
                    {
                        int o = (y * curW + x) * 4;
                        bool on = (curMask[y * stride + x / 8] & (0x80 >> (x % 8))) != 0;
                        src.SetPixel(x, y, on ? Color.FromArgb(255, curPx[o + 2], curPx[o + 1], curPx[o]) : Color.Transparent);
                    }
                int dw = Math.Max(1, (int)Math.Round(curW * s)), dh = Math.Max(1, (int)Math.Round(curH * s));
                using (var dst = new Bitmap(dw, dh, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(dst))
                    {
                        g.InterpolationMode = s < 1 ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(src, new Rectangle(0, 0, dw, dh), 0, 0, curW, curH, GraphicsUnit.Pixel);
                    }
                    IntPtr icon = dst.GetHicon();
                    ICONINFO ii;
                    if (GetIconInfo(icon, out ii))
                    {
                        ii.fIcon = false;
                        ii.xHotspot = Math.Max(0, Math.Min(dw - 1, (int)(curHx * s)));
                        ii.yHotspot = Math.Max(0, Math.Min(dh - 1, (int)(curHy * s)));
                        hCursor = CreateIconIndirect(ref ii);
                        if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
                        if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
                    }
                    DestroyIcon(icon);
                }
            }
            remoteCursor = hCursor != IntPtr.Zero ? new Cursor(hCursor) : null;
            Cursor = remoteCursor ?? Cursors.Default;
            if (oldCur != null) oldCur.Dispose();
            if (old != IntPtr.Zero) DestroyCursor(old);
        }

        void ClearRemoteCursor()
        {
            curPx = null;
            var oldCur = remoteCursor; remoteCursor = null;
            if (!IsDisposed) Cursor = Cursors.Default;
            if (oldCur != null) oldCur.Dispose();
            if (hCursor != IntPtr.Zero) { DestroyCursor(hCursor); hCursor = IntPtr.Zero; }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); if (curPx != null) BuildCursor(); }

        void FreeBmp()
        {
            if (bmp != null) { bmp.Dispose(); bmp = null; }
            if (pin.IsAllocated) pin.Free();
            pinned = null;
        }

        /// <summary>Must hold c.FbLock.</summary>
        bool EnsureBmp(RfbClient cl)
        {
            if (cl.W <= 0 || cl.H <= 0) return false;
            if (bmp == null || pinned != cl.Fb)
            {
                FreeBmp();
                pinned = cl.Fb;
                pin = GCHandle.Alloc(pinned, GCHandleType.Pinned);
                bmp = new Bitmap(cl.W, cl.H, cl.W * 4, PixelFormat.Format32bppRgb, pin.AddrOfPinnedObject());
            }
            return true;
        }

        Rectangle DestRect(RfbClient cl)
        {
            if (cl == null || cl.W <= 0 || cl.H <= 0 || Width <= 0 || Height <= 0) return new Rectangle(0, 0, 1, 1);
            float s = Math.Min((float)Width / cl.W, (float)Height / cl.H);
            int w = Math.Max(1, (int)(cl.W * s)), h = Math.Max(1, (int)(cl.H * s));
            return new Rectangle((Width - w) / 2, (Height - h) / 2, w, h);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }
        [DllImport("gdi32.dll")] static extern int StretchDIBits(IntPtr hdc, int xDst, int yDst, int wDst, int hDst, int xSrc, int ySrc, int wSrc, int hSrc, IntPtr bits, ref BITMAPINFOHEADER bmi, uint usage, uint rop);
        [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll")] static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr old);
        bool gdiFailed;

        protected override void OnPaint(PaintEventArgs e)
        {
            var cl = c;
            if (cl == null || State != ViewState.Connected) { PaintStatus(e.Graphics); return; }
            lock (cl.FbLock)
            {
                if (!EnsureBmp(cl)) { PaintStatus(e.Graphics); return; }
                var g = e.Graphics;
                var d = DestRect(cl);
                using (var bg = new Region(ClientRectangle)) { bg.Exclude(d); g.FillRegion(Brushes.Black, bg); }

                // Fast path: GDI StretchDIBits straight from the pinned framebuffer. HALFTONE gives smooth
                // downscaling for previews at a fraction of GDI+'s HighQualityBilinear cost.
                if (!gdiFailed)
                {
                    var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = cl.W, biHeight = -cl.H, biPlanes = 1, biBitCount = 32 };
                    IntPtr hdc = g.GetHdc();
                    try
                    {
                        SetStretchBltMode(hdc, d.Width < cl.W ? 4 /*HALFTONE*/ : 3 /*COLORONCOLOR*/);
                        SetBrushOrgEx(hdc, 0, 0, IntPtr.Zero);
                        if (StretchDIBits(hdc, d.X, d.Y, d.Width, d.Height, 0, 0, cl.W, cl.H, pin.AddrOfPinnedObject(), ref bi, 0, 0x00CC0020) != 0) return;
                    }
                    finally { g.ReleaseHdc(hdc); }
                    gdiFailed = true;
                }
                g.InterpolationMode = d.Width < cl.W ? (Interactive ? InterpolationMode.HighQualityBilinear : InterpolationMode.Bilinear) : InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(bmp, d, 0, 0, cl.W, cl.H, GraphicsUnit.Pixel);
            }
        }

        public override Bitmap Snapshot(int maxWidth)
        {
            var cl = c;
            if (cl == null || State != ViewState.Connected) return null;
            lock (cl.FbLock)
            {
                if (!EnsureBmp(cl)) return null;
                int w = Math.Min(maxWidth, cl.W), h = Math.Max(1, cl.H * w / cl.W);
                var o = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(o))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(bmp, new Rectangle(0, 0, w, h), 0, 0, cl.W, cl.H, GraphicsUnit.Pixel);
                }
                return o;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { tick.Stop(); tick.Dispose(); Stop(); }
            base.Dispose(disposing);
        }

        // ---- input ----
        bool CanSend { get { return c != null && State == ViewState.Connected && Interactive && !Host.ViewOnly; } }

        bool MapPoint(MouseEventArgs e, out int x, out int y)
        {
            x = y = 0;
            var cl = c; if (cl == null || cl.W <= 0 || cl.H <= 0 || Width <= 0 || Height <= 0) return false;
            var d = DestRect(cl);
            x = (int)((e.X - d.X) * (double)cl.W / d.Width);
            y = (int)((e.Y - d.Y) * (double)cl.H / d.Height);
            x = Math.Max(0, Math.Min(cl.W - 1, x)); y = Math.Max(0, Math.Min(cl.H - 1, y));
            return true;
        }

        void SendPointer(MouseEventArgs e)
        {
            int x, y;
            if (!CanSend || !MapPoint(e, out x, out y)) return;
            c.Pointer(x, y, buttons);
        }

        static int Bit(MouseButtons b)
        {
            if (b == MouseButtons.Left) return 1;
            if (b == MouseButtons.Middle) return 2;
            if (b == MouseButtons.Right) return 4;
            return 0;
        }

        protected override void OnMouseDown(MouseEventArgs e) { Focus(); buttons |= Bit(e.Button); SendPointer(e); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { buttons &= ~Bit(e.Button); SendPointer(e); base.OnMouseUp(e); }
        protected override void OnMouseMove(MouseEventArgs e) { SendPointer(e); base.OnMouseMove(e); }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (!Interactive) RaiseActivated();
            base.OnMouseDoubleClick(e);
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int x, y;
            if (!CanSend || !MapPoint(e, out x, out y)) return;
            int notches = Math.Max(1, Math.Abs(e.Delta) / 120);
            int bit = e.Delta > 0 ? 8 : 16;
            for (int i = 0; i < notches; i++) { c.Pointer(x, y, buttons | bit); c.Pointer(x, y, buttons); }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            try
            {
                if (CanSend && Clipboard.ContainsText())
                {
                    string t = Clipboard.GetText();
                    if (t != lastClip && t.Length < 1000000) { lastClip = t; c.SendClipboard(t); }
                }
            }
            catch { }
        }
        protected override void OnLostFocus(EventArgs e) { ReleaseAllKeys(); base.OnLostFocus(e); }

        protected override bool IsInputKey(Keys keyData) { return Interactive; }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (Interactive && Focused && CanSend)
            {
                // WM_KEYDOWN(0x100)/WM_SYSKEYDOWN(0x104): handle ourselves so Alt/Tab combos reach the remote
                if (msg.Msg == 0x100 || msg.Msg == 0x104) { HandleKey((int)msg.WParam, true); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void WndProc(ref Message m)
        {
            // ProcessCmdKey only receives key-down; handle WM_KEYUP(0x101)/WM_SYSKEYUP(0x105) here so modifiers don't stick
            if ((m.Msg == 0x101 || m.Msg == 0x105) && Interactive && Focused && CanSend)
            {
                HandleKey((int)m.WParam, false);
                return;
            }
            base.WndProc(ref m);
        }

        void ReleaseAllKeys()
        {
            var cl = c;
            foreach (var kv in new List<KeyValuePair<int, uint>>(down))
                if (cl != null) cl.Key(kv.Value, false);
            down.Clear();
            buttons = 0;
        }

        public override void SendCtrlAltDel()
        {
            if (!CanSend) return;
            uint[] k = { 0xFFE3, 0xFFE9, 0xFFFF };
            foreach (uint s in k) c.Key(s, true);
            for (int i = k.Length - 1; i >= 0; i--) c.Key(k[i], false);
        }

        void HandleKey(int vk, bool isDown)
        {
            if (isDown)
            {
                uint sym;
                if (down.TryGetValue(vk, out sym)) { c.Key(sym, true); return; } // autorepeat
                sym = KeySym(vk);
                if (sym == 0) return;
                down[vk] = sym;
                c.Key(sym, true);
            }
            else
            {
                uint sym;
                if (down.TryGetValue(vk, out sym)) { down.Remove(vk); c.Key(sym, false); }
            }
        }

        [DllImport("user32.dll")] static extern bool GetKeyboardState(byte[] state);
        [DllImport("user32.dll")] static extern int ToUnicode(uint vk, uint sc, byte[] state, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder buf, int size, uint flags);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);

        static uint KeySym(int vk)
        {
            Keys k = (Keys)vk;
            switch (k)
            {
                case Keys.Back: return 0xFF08; case Keys.Tab: return 0xFF09; case Keys.Return: return 0xFF0D;
                case Keys.Escape: return 0xFF1B; case Keys.Insert: return 0xFF63; case Keys.Delete: return 0xFFFF;
                case Keys.Home: return 0xFF50; case Keys.Left: return 0xFF51; case Keys.Up: return 0xFF52;
                case Keys.Right: return 0xFF53; case Keys.Down: return 0xFF54; case Keys.PageUp: return 0xFF55;
                case Keys.PageDown: return 0xFF56; case Keys.End: return 0xFF57;
                case Keys.ShiftKey: case Keys.LShiftKey: return 0xFFE1; case Keys.RShiftKey: return 0xFFE2;
                case Keys.ControlKey: case Keys.LControlKey: return 0xFFE3; case Keys.RControlKey: return 0xFFE4;
                case Keys.Menu: case Keys.LMenu: return 0xFFE9; case Keys.RMenu: return 0xFFEA;
                case Keys.LWin: return 0xFFEB; case Keys.RWin: return 0xFFEC;
                case Keys.Capital: return 0xFFE5; case Keys.NumLock: return 0xFF7F; case Keys.Scroll: return 0xFF14;
                case Keys.Pause: return 0xFF13; case Keys.PrintScreen: return 0xFF61; case Keys.Apps: return 0xFF67;
                case Keys.Multiply: return 0xFFAA; case Keys.Add: return 0xFFAB; case Keys.Subtract: return 0xFFAD;
                case Keys.Decimal: return 0xFFAE; case Keys.Divide: return 0xFFAF;
            }
            if (vk >= (int)Keys.F1 && vk <= (int)Keys.F12) return (uint)(0xFFBE + vk - (int)Keys.F1);
            if (vk >= (int)Keys.NumPad0 && vk <= (int)Keys.NumPad9) return (uint)(0xFFB0 + vk - (int)Keys.NumPad0);

            byte[] st = new byte[256];
            if (!GetKeyboardState(st)) return 0;
            bool ctrl = (st[0x11] & 0x80) != 0, alt = (st[0x12] & 0x80) != 0;
            if (!(ctrl && alt)) { st[0x11] = st[0xA2] = st[0xA3] = 0; st[0x12] = st[0xA4] = st[0xA5] = 0; }
            var sb = new StringBuilder(8);
            int n = ToUnicode((uint)vk, MapVirtualKey((uint)vk, 0), st, sb, sb.Capacity, 4);
            if (n >= 1)
            {
                int cp = sb[0];
                if (n >= 2 && char.IsHighSurrogate(sb[0])) cp = char.ConvertToUtf32(sb[0], sb[1]);
                if (cp < 0x20) return 0;
                return cp < 0x100 ? (uint)cp : (uint)(0x01000000 + cp);
            }
            return 0;
        }
    }

    // =====================================================================
    //  RDP (native Microsoft RDP ActiveX control - the same engine as mstsc)
    // =====================================================================
    class RdpAx : AxHost
    {
        public RdpAx() : base("7cacbd7b-0d99-468f-ac33-22e495c0afe5") { } // MsRdpClient9NotSafeForScripting
        public object Ocx { get { return GetOcx(); } }
    }

    [ComImport, Guid("c1e6743a-41c1-4a74-832a-0dd06c1c7a0e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMsTscNonScriptable
    {
        void put_ClearTextPassword([MarshalAs(UnmanagedType.BStr)] string pw);
    }

    /// <summary>Verified vtable layout from mstscax.dll for controlling CredSSP and credential prompts.</summary>
    [ComImport, Guid("4f6996d5-d7b1-412c-b0ff-063718566907"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMsRdpClientNonScriptable5
    {
        // IMsTscNonScriptable (10)
        void put_ClearTextPassword([MarshalAs(UnmanagedType.BStr)] string pw);
        void _s1(); void _s2(); void _s3(); void _s4(); void _s5(); void _s6(); void _s7(); void _s8(); void _s9();
        // IMsRdpClientNonScriptable (2)
        void _s10(); void _s11();
        // IMsRdpClientNonScriptable2 (2)
        void _s12(); void _s13();
        // IMsRdpClientNonScriptable3 (20)
        void put_ShowRedirectionWarningDialog([MarshalAs(UnmanagedType.VariantBool)] bool v);
        [return: MarshalAs(UnmanagedType.VariantBool)] bool get_ShowRedirectionWarningDialog();
        void put_PromptForCredentials([MarshalAs(UnmanagedType.VariantBool)] bool v);
        [return: MarshalAs(UnmanagedType.VariantBool)] bool get_PromptForCredentials();
        void put_NegotiateSecurityLayer([MarshalAs(UnmanagedType.VariantBool)] bool v);
        [return: MarshalAs(UnmanagedType.VariantBool)] bool get_NegotiateSecurityLayer();
        void put_EnableCredSspSupport([MarshalAs(UnmanagedType.VariantBool)] bool v);
        [return: MarshalAs(UnmanagedType.VariantBool)] bool get_EnableCredSspSupport();
        void _s22(); void _s23(); void _s24(); void _s25(); void _s26(); void _s27();
        void put_WarnAboutSendingCredentials([MarshalAs(UnmanagedType.VariantBool)] bool v);
        [return: MarshalAs(UnmanagedType.VariantBool)] bool get_WarnAboutSendingCredentials();
        void _s30(); void _s31(); void _s32(); void _s33();
        // IMsRdpClientNonScriptable4 (16)
        void _s34(); void _s35(); void _s36(); void _s37(); void _s38(); void _s39(); void _s40(); void _s41();
        void put_AllowCredentialSaving([MarshalAs(UnmanagedType.VariantBool)] bool v);
        [return: MarshalAs(UnmanagedType.VariantBool)] bool get_AllowCredentialSaving();
        void put_PromptForCredsOnClient([MarshalAs(UnmanagedType.VariantBool)] bool v);
        [return: MarshalAs(UnmanagedType.VariantBool)] bool get_PromptForCredsOnClient();
        void _s46(); void _s47(); void _s48(); void _s49();
        // IMsRdpClientNonScriptable5 (12)
        void _s50(); void _s51(); void _s52(); void _s53(); void _s54();
        void put_DisableConnectionBar([MarshalAs(UnmanagedType.VariantBool)] bool v);
        void _s56(); void _s57(); void _s58(); void _s59();
        void put_AllowPromptingForCredentials([MarshalAs(UnmanagedType.VariantBool)] bool v);
        [return: MarshalAs(UnmanagedType.VariantBool)] bool get_AllowPromptingForCredentials();
    }

    /// <summary>The subset of the RDP control's event dispinterface we listen to (DISPIDs from msrdp.idl).</summary>
    [ComImport, Guid("336d5562-efa8-482e-8cb3-c5c0fc7a7db6"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    public interface IMsTscAxEvents
    {
        [DispId(4)] void OnDisconnected(int discReason);
        [DispId(10)] void OnFatalError(int errorCode);
    }

    [ClassInterface(ClassInterfaceType.None), ComVisible(true)]
    public class RdpEventSink : IMsTscAxEvents
    {
        public volatile int Disconnected = -1, Fatal = -1;
        public void OnDisconnected(int discReason) { Disconnected = discReason; }
        public void OnFatalError(int errorCode) { Fatal = errorCode; }
    }

    class RdpView : RemoteView
    {
        RdpAx ax;
        readonly Timer poll = new Timer();
        int lastConn = 0;
        bool sawActivity;
        RdpEventSink sink; AxHost.ConnectionPointCookie cookie;
        DateTime startedUtc;
        int timeoutSec = 60;   // longer when the control may be showing its own credential prompt

        public RdpView()
        {
            poll.Interval = 400;
            poll.Tick += delegate { Poll(); };
        }

        static void Try(Action a) { try { a(); } catch (Exception ex) { Log.Write("RDP setting skipped: " + ex.Message); } }

        public override void Start(string password)
        {
            Stop();
            SetState(ViewState.Connecting, "");
            try
            {
                ax = new RdpAx { Dock = DockStyle.Fill, Enabled = Interactive };
                ((ISupportInitialize)ax).BeginInit();
                Controls.Add(ax);
                ((ISupportInitialize)ax).EndInit();
                IntPtr h = ax.Handle; // force creation of the control
                dynamic o = ax.Ocx;
                Host hs = Host;

                o.Server = (hs.Address ?? "").Trim();
                string rdpUser = (hs.Username ?? "").Trim();
                string rdpDomain = (hs.Domain ?? "").Trim();
                if (string.IsNullOrEmpty(rdpDomain))
                {
                    int slash = rdpUser.IndexOf('\\');
                    if (slash > 0 && slash < rdpUser.Length - 1)
                    {
                        rdpDomain = rdpUser.Substring(0, slash);
                        rdpUser = rdpUser.Substring(slash + 1);
                    }
                }
                Try(() => { if (!string.IsNullOrEmpty(rdpUser)) o.UserName = rdpUser; });
                Try(() => { if (!string.IsNullOrEmpty(rdpDomain)) o.Domain = rdpDomain; });
                Try(() => o.DesktopWidth = Math.Max(200, Math.Min(8192, hs.Width)));
                Try(() => o.DesktopHeight = Math.Max(200, Math.Min(8192, hs.Height)));
                Try(() => o.ColorDepth = hs.ColorDepth);
                dynamic a = null;
                try { a = o.AdvancedSettings9; } catch { try { a = o.AdvancedSettings8; } catch { try { a = o.AdvancedSettings7; } catch { } } }
                if (a != null)
                {
                    Try(() => a.RDPPort = hs.Port);
                    Try(() => a.SmartSizing = true);
                    Try(() => a.EnableCredSspSupport = hs.Nla);
                    Try(() => a.NegotiateSecurityLayer = true);
                    Try(() => a.AuthenticationLevel = 0);
                    Try(() => a.PublicMode = false);
                    Try(() => a.RedirectClipboard = hs.RedirectClipboard);
                    Try(() => a.RedirectDrives = hs.RedirectDrives);
                    Try(() => a.RedirectPrinters = hs.RedirectPrinters);
                    Try(() => a.BitmapPeristence = 1);
                    Try(() => a.Compress = 1);
                    Try(() => a.DisplayConnectionBar = false);
                    Try(() => a.PinConnectionBar = false);
                    Try(() => a.ConnectionBarShowMinimizeButton = false);
                    if (!string.IsNullOrEmpty(password))
                        Try(() => a.ClearTextPassword = password);
                }
                Try(() => o.SecuredSettings2.AudioRedirectionMode = hs.Audio ? 0 : 2);
                Try(() => o.SecuredSettings2.KeyboardHookMode = 1);
                bool hasPwd = !string.IsNullOrEmpty(password);
                var ns5 = ax.Ocx as IMsRdpClientNonScriptable5;
                if (ns5 != null)
                {
                    Try(() => ns5.put_EnableCredSspSupport(hs.Nla));
                    Try(() => ns5.put_NegotiateSecurityLayer(true));
                    Try(() => ns5.put_ShowRedirectionWarningDialog(false));
                    Try(() => ns5.put_WarnAboutSendingCredentials(false));
                    Try(() => ns5.put_AllowCredentialSaving(false));
                    Try(() => ns5.put_PromptForCredentials(!hasPwd));
                    Try(() => ns5.put_PromptForCredsOnClient(!hasPwd));
                    Try(() => ns5.put_AllowPromptingForCredentials(!hasPwd));
                    if (hasPwd) Try(() => ns5.put_ClearTextPassword(password));
                }
                else if (hasPwd)
                {
                    Try(() => ((IMsTscNonScriptable)ax.Ocx).put_ClearTextPassword(password));
                }

                lastConn = 0; sawActivity = false; startedUtc = DateTime.UtcNow;
                timeoutSec = string.IsNullOrEmpty(password) ? 300 : 60;
                sink = new RdpEventSink();
                try { cookie = new AxHost.ConnectionPointCookie(ax.Ocx, sink, typeof(IMsTscAxEvents)); }
                catch (Exception ex) { cookie = null; Log.Write("RDP event sink unavailable (falling back to polling): " + ex.Message); }
                o.Connect();
                ax.Visible = false; // the control paints white until connected; our themed status screen shows instead
                poll.Start();
            }
            catch (Exception ex)
            {
                Log.Write("RDP start failed: " + ex);
                CleanupAx();
                SetState(ViewState.Failed, "RDP control error: " + ex.Message);
            }
        }

        void Poll()
        {
            if (ax == null || IsDisposed) return;
            int conn;
            try { dynamic o = ax.Ocx; conn = Convert.ToInt32(o.Connected); }
            catch { return; }
            int disc = sink != null ? sink.Disconnected : -1, fatal = sink != null ? sink.Fatal : -1;
            if (fatal >= 0) { Fail("RDP fatal error " + fatal + "."); return; }
            if (conn == 1 && State != ViewState.Connected) { sawActivity = true; ax.Visible = true; SetState(ViewState.Connected, ""); }
            else if (conn == 2) { sawActivity = true; if (State != ViewState.Connecting) SetState(ViewState.Connecting, ""); }
            else if (conn == 0 && (disc >= 0 || (sawActivity && lastConn != 0)))
            {
                Fail(DisconnectText(disc));
                return;
            }
            else if (conn != 1 && State != ViewState.Connected && (DateTime.UtcNow - startedUtc).TotalSeconds > timeoutSec)
            {
                Fail("Timed out after " + timeoutSec + " s. Check the address, port and that Remote Desktop is enabled on the target.");
                return;
            }
            lastConn = conn;
        }

        void Fail(string why)
        {
            poll.Stop();
            CleanupAx();
            SetState(ViewState.Failed, why);
        }

        /// <summary>Uses the control's own wording (same text mstsc shows) when it can.</summary>
        string DisconnectText(int disc)
        {
            const string generic = "Disconnected. Check address, credentials, network level authentication and that Remote Desktop is enabled on the target.";
            int ext = 0;
            try { dynamic o = ax.Ocx; ext = Convert.ToInt32(o.ExtendedDisconnectReason); } catch { }
            if (disc == 1 || disc == 2 || disc == 3) return ext != 0 ? ExtText(ext) ?? "The remote session ended." : "The remote session ended.";
            if (disc > 0)
            {
                try
                {
                    dynamic o = ax.Ocx;
                    string s = Convert.ToString(o.GetErrorDescription((uint)disc, (uint)ext));
                    if (!string.IsNullOrWhiteSpace(s)) return s.Trim() + " (code " + disc + ")";
                }
                catch { }
                return generic + " (code " + disc + (ext != 0 ? ", ext " + ext : "") + ")";
            }
            return ExtText(ext) ?? generic;
        }

        static string ExtText(int ext)
        {
            switch (ext)
            {
                case 0: return null;
                case 1: return "Disconnected by an administrator or another tool.";
                case 2: return "Logged off by an administrator or another tool.";
                case 3: return "Disconnected: idle session limit reached.";
                case 4: return "Disconnected: session time limit reached.";
                case 5: return "Another user connected to this session.";
                case 6: return "The server is out of memory.";
                case 7: return "The server denied the connection.";
                case 9: return "This user account is not allowed to connect remotely.";
                case 11: return "Signed out from inside the remote session.";
                case 12: return "Logged off from inside the remote session.";
                default: return "Disconnected (extended reason " + ext + ").";
            }
        }

        void CleanupAx()
        {
            poll.Stop();
            if (cookie != null) { try { cookie.Disconnect(); } catch { } cookie = null; }
            var a = ax; ax = null;
            if (a == null) return;
            try { dynamic o = a.Ocx; if (Convert.ToInt32(o.Connected) != 0) o.Disconnect(); } catch { }
            try { Controls.Remove(a); a.Dispose(); } catch { }
        }

        public override void Stop()
        {
            CleanupAx();
            if (State != ViewState.Failed) State = ViewState.Idle;
            Invalidate();
        }

        protected override void OnInteractiveChanged()
        {
            if (ax != null) { try { ax.Enabled = Interactive; } catch { } }
        }

        public override void FocusInput() { if (ax != null) { try { ax.Focus(); } catch { } } }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (ax == null || State != ViewState.Connected) PaintStatus(e.Graphics);
        }

        public override Bitmap Snapshot(int maxWidth)
        {
            try
            {
                var f = FindForm();
                if (ax == null || State != ViewState.Connected || !Visible || f == null || f.WindowState == FormWindowState.Minimized || Form.ActiveForm != f) return null;
                var r = RectangleToScreen(ClientRectangle);
                if (r.Width < 40 || r.Height < 40) return null;
                using (var full = new Bitmap(r.Width, r.Height, PixelFormat.Format24bppRgb))
                {
                    using (var g = Graphics.FromImage(full)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                    int w = Math.Min(maxWidth, r.Width), h = Math.Max(1, r.Height * w / r.Width);
                    var o = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                    using (var g = Graphics.FromImage(o))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(full, new Rectangle(0, 0, w, h));
                    }
                    return o;
                }
            }
            catch { return null; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { CleanupAx(); poll.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
