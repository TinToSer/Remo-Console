using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RemoConsole
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        public static string AppVersion
        {
            get
            {
                var attr = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                    Assembly.GetExecutingAssembly(), typeof(AssemblyInformationalVersionAttribute));
                if (attr != null && !string.IsNullOrEmpty(attr.InformationalVersion)) return attr.InformationalVersion;
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v != null ? v.ToString(3) : "1.0.0";
            }
        }

        static string MutexKey(string dir)
        {
            string norm = Path.GetFullPath(dir).TrimEnd('\\', '/').ToLowerInvariant();
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(norm));
                return "Local\\RemoConsole.SingleInstance." + BitConverter.ToString(h, 0, 8).Replace("-", "").ToLowerInvariant();
            }
        }

        [STAThread]
        static int Main(string[] args)
        {
            if (Array.IndexOf(args, "--version") >= 0 || Array.IndexOf(args, "-v") >= 0)
            {
                Console.WriteLine("Remo-Console " + AppVersion);
                return 0;
            }
            if (Array.IndexOf(args, "--help") >= 0 || Array.IndexOf(args, "-h") >= 0)
            {
                Console.WriteLine("Remo-Console " + AppVersion + " - Remote-control console for Windows (RDP + VNC)");
                Console.WriteLine("Usage: Remo-Console.exe [--data <dir>] [--selftest] [--version] [--help]");
                return 0;
            }

            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Remo-Console");
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--data") dir = args[i + 1];

            if (Array.IndexOf(args, "--selftest") >= 0) return SelfTest();

            bool created;
            using (var mutex = new Mutex(true, MutexKey(dir), out created))
            {
                if (!created)
                {
                    MessageBox.Show("Remo-Console is already running.", "Remo-Console");
                    return 1;
                }
                try { SetProcessDPIAware(); } catch { }
                Theme.InitScale();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

                Store store;
                try
                {
                    store = new Store(dir);
                    store.Load();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Cannot open data folder:\r\n" + dir + "\r\n\r\n" + ex.Message, "Remo-Console", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 2;
                }

                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Log.Write("UI exception: " + e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    Log.Write("Fatal: " + e.ExceptionObject);
                    try { store.Flush(true); } catch { }
                };
                Microsoft.Win32.SessionEndingEventHandler onEnd = delegate { try { store.Flush(true); } catch { } };
                Microsoft.Win32.SystemEvents.SessionEnding += onEnd;

                try
                {
                    Application.Run(new MainForm(store));
                }
                finally
                {
                    Microsoft.Win32.SystemEvents.SessionEnding -= onEnd;
                    try { store.Flush(true); } catch { }
                }
            }
            return 0;
        }

        // Headless checks: remo-console.exe --selftest
        static int SelfTest()
        {
            int fail = 0;
            Action<string, bool> check = (name, ok) => { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name); if (!ok) fail++; };

            check("DES known-answer vector", Des.SelfTest());

            string tmp = Path.Combine(Path.GetTempPath(), "remo-console-selftest-" + Guid.NewGuid().ToString("N"));
            try
            {
                string f = Path.Combine(tmp, "t.rc");
                string src;
                AtomicFile.Write(f, Encoding.UTF8.GetBytes("one"));
                AtomicFile.Write(f, Encoding.UTF8.GetBytes("two"));
                check("read returns newest", Encoding.UTF8.GetString(AtomicFile.Read(f, out src)) == "two");

                // flip a byte in the main file -> must fall back to previous intact copy, and keep the damaged file
                byte[] raw = File.ReadAllBytes(f); raw[raw.Length - 1] ^= 0x55; File.WriteAllBytes(f, raw);
                byte[] rec = AtomicFile.Read(f, out src);
                check("bit-flip detected, recovered from backup", rec != null && Encoding.UTF8.GetString(rec) == "one");
                check("damaged file quarantined, not deleted", Directory.GetFiles(tmp, "t.rc.corrupt-*").Length == 1);

                // truncated file
                AtomicFile.Write(f, Encoding.UTF8.GetBytes("three"));
                AtomicFile.Write(f, Encoding.UTF8.GetBytes("four"));
                File.WriteAllBytes(f, new byte[] { 1, 2, 3 });
                rec = AtomicFile.Read(f, out src);
                check("truncated/garbage main file recovered", rec != null);

                // stale half-written temp file must never be picked up
                File.WriteAllBytes(f + ".tmp", Encoding.UTF8.GetBytes("RCSTORE1 deadbeef\npartial"));
                AtomicFile.Write(f, Encoding.UTF8.GetBytes("five"));
                check("stale .tmp ignored", Encoding.UTF8.GetString(AtomicFile.Read(f, out src)) == "five");

                // store round trip incl. DPAPI password
                var st = new Store(Path.Combine(tmp, "s"));
                var h = new Host { Name = "x", Address = "10.0.0.1" }; h.Password = "s3cret!";
                st.Data.Hosts.Add(h); st.MarkDirty();
                check("store flush ok", st.Flush());
                string raw2 = File.ReadAllText(st.DataFile);
                check("password not stored in clear text", !raw2.Contains("s3cret!"));
                var st2 = new Store(Path.Combine(tmp, "s")); st2.Load();
                check("store reload + password decrypt", st2.Data.Hosts.Count == 1 && st2.Data.Hosts[0].Password == "s3cret!");

                // empty folder -> clean start, no note
                var st3 = new Store(Path.Combine(tmp, "empty")); st3.Load();
                check("fresh start is clean", st3.LoadNote == null && st3.Data.Hosts.Count == 0);

                // unrecoverable single file (no backups) must quarantine AND notify user
                string badDir = Path.Combine(tmp, "unrec");
                Directory.CreateDirectory(badDir);
                File.WriteAllBytes(Path.Combine(badDir, "data.rc"), new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
                var st4 = new Store(badDir); st4.Load();
                check("unrecoverable file sets LoadNote and quarantines", st4.LoadNote != null && Directory.GetFiles(badDir, "data.rc.corrupt-*").Length == 1);

                // sanitization of malformed host fields on load
                var st5 = new Store(Path.Combine(tmp, "san"));
                st5.Data.Hosts.Add(new Host { Id = "dup", Name = "", Address = " 10.0.0.2 ", Protocol = "BAD", Port = 99999, Width = 10, Height = 99999 });
                st5.Data.Hosts.Add(new Host { Id = "dup", Name = "two", Address = "10.0.0.3", Protocol = "VNC", Port = 0 });
                st5.MarkDirty(); st5.Flush();
                var st5b = new Store(Path.Combine(tmp, "san")); st5b.Load();
                check("loaded hosts sanitized (ids, ports, protocol, bounds)",
                    st5b.Data.Hosts.Count == 2
                    && st5b.Data.Hosts[0].Id != st5b.Data.Hosts[1].Id
                    && st5b.Data.Hosts[0].Name == "10.0.0.2"
                    && st5b.Data.Hosts[0].Protocol == "RDP"
                    && st5b.Data.Hosts[0].Port == 3389
                    && st5b.Data.Hosts[0].Width == 200 && st5b.Data.Hosts[0].Height == 8192
                    && st5b.Data.Hosts[1].Port == 5900);
            }
            catch (Exception ex) { Console.WriteLine("EXC   " + ex); fail++; }
            finally { try { Directory.Delete(tmp, true); } catch { } }

            try { check("RFB loopback (auth + raw/rre/hextile/zrle/pseudo/throttle)", RfbLoopback()); }
            catch (Exception ex) { Console.WriteLine("EXC   " + ex); fail++; }

            Console.WriteLine(fail == 0 ? "ALL PASSED" : fail + " FAILED");
            return fail == 0 ? 0 : 1;
        }

        static void Rx(System.IO.Stream s, int n) { byte[] b = new byte[n]; int o = 0; while (o < n) { int r = s.Read(b, o, n - o); if (r <= 0) throw new EndOfStreamException(); o += r; } }
        static byte[] RxB(System.IO.Stream s, int n) { byte[] b = new byte[n]; int o = 0; while (o < n) { int r = s.Read(b, o, n - o); if (r <= 0) throw new EndOfStreamException(); o += r; } return b; }

        static bool RfbLoopback()
        {
            var lst = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            lst.Start();
            int port = ((System.Net.IPEndPoint)lst.LocalEndpoint).Port;
            bool authOk = false;
            long throttleMs = -1;
            var srv = new Thread(() =>
            {
                try
                {
                    using (var cl = lst.AcceptTcpClient())
                {
                    var s = cl.GetStream();
                    Action<byte[]> tx = b => s.Write(b, 0, b.Length);
                    tx(Encoding.ASCII.GetBytes("RFB 003.008\n"));
                    RxB(s, 12);
                    tx(new byte[] { 1, 2 });
                    RxB(s, 1);
                    byte[] ch = new byte[16]; for (int i = 0; i < 16; i++) ch[i] = (byte)(i * 7 + 1);
                    tx(ch);
                    byte[] resp = RxB(s, 16);
                    byte[] exp = Des.VncResponse("pw", ch);
                    authOk = true; for (int i = 0; i < 16; i++) if (resp[i] != exp[i]) authOk = false;
                    tx(new byte[] { 0, 0, 0, 0 });
                    RxB(s, 1); // ClientInit
                    var si = new System.IO.MemoryStream();
                    si.Write(new byte[] { 0, 64, 0, 32 }, 0, 4);
                    si.Write(new byte[16], 0, 16);
                    si.Write(new byte[] { 0, 0, 0, 1, (byte)'t' }, 0, 5);
                    tx(si.ToArray());
                    RxB(s, 20); RxB(s, 4 + 4 * 7); RxB(s, 10);

                    var u = new System.IO.MemoryStream();
                    Action<int[]> hdr = a => { foreach (int v in a) { u.WriteByte((byte)(v >> 8)); u.WriteByte((byte)v); } };
                    Action<int> be32 = v => { u.WriteByte((byte)(v >> 24)); u.WriteByte((byte)(v >> 16)); u.WriteByte((byte)(v >> 8)); u.WriteByte((byte)v); };
                    // zlib stream continued across rects: non-final "stored" deflate blocks
                    Func<byte[], bool, byte[]> stored = (data, first) =>
                    {
                        var m = new System.IO.MemoryStream();
                        if (first) { m.WriteByte(0x78); m.WriteByte(0x01); }
                        m.WriteByte(0); int n = data.Length;
                        m.WriteByte((byte)n); m.WriteByte((byte)(n >> 8)); m.WriteByte((byte)~n); m.WriteByte((byte)(~n >> 8));
                        m.Write(data, 0, n); return m.ToArray();
                    };
                    u.Write(new byte[] { 0, 0, 0, 7 }, 0, 4);
                    // Raw 2x1 at (0,0): red, green (BGRX)
                    hdr(new[] { 0, 0, 2, 1 }); u.Write(new byte[] { 0, 0, 0, 0 }, 0, 4);
                    u.Write(new byte[] { 0, 0, 255, 0, 0, 255, 0, 0 }, 0, 8);
                    // RRE 4x4 at (4,0): bg blue, one white 2x2 subrect at (1,1)
                    hdr(new[] { 4, 0, 4, 4 }); u.Write(new byte[] { 0, 0, 0, 2 }, 0, 4);
                    u.Write(new byte[] { 0, 0, 0, 1 }, 0, 4);
                    u.Write(new byte[] { 255, 0, 0, 0 }, 0, 4);
                    u.Write(new byte[] { 255, 255, 255, 0 }, 0, 4);
                    hdr(new[] { 1, 1, 2, 2 });
                    // Hextile 16x16 at (16,0): bg=yellow, fg=cyan, one subrect (3,3) 2x2
                    hdr(new[] { 16, 0, 16, 16 }); u.Write(new byte[] { 0, 0, 0, 5 }, 0, 4);
                    u.WriteByte(2 | 4 | 8);
                    u.Write(new byte[] { 0, 255, 255, 0 }, 0, 4);
                    u.Write(new byte[] { 255, 255, 0, 0 }, 0, 4);
                    u.WriteByte(1); u.WriteByte(0x33); u.WriteByte(0x11);
                    // ZRLE 4x2 at (40,0): raw tile, pixel i = R 3i, G 2i, B i
                    var za = new System.IO.MemoryStream(); za.WriteByte(0);
                    for (int i = 0; i < 8; i++) { za.WriteByte((byte)i); za.WriteByte((byte)(2 * i)); za.WriteByte((byte)(3 * i)); }
                    byte[] pa = stored(za.ToArray(), true);
                    hdr(new[] { 40, 0, 4, 2 }); be32(16); be32(pa.Length); u.Write(pa, 0, pa.Length);
                    // ZRLE 6x2 at (48,0): palette RLE, red x5 then blue x7
                    byte[] pb = stored(new byte[] { 0x82, 0, 0, 255, 255, 0, 0, 0x80, 4, 0x81, 6 }, false);
                    hdr(new[] { 48, 0, 6, 2 }); be32(16); be32(pb.Length); u.Write(pb, 0, pb.Length);
                    // Cursor pseudo-rect: 2x2, hotspot (1,1)
                    hdr(new[] { 1, 1, 2, 2 }); be32(-239); u.Write(new byte[16], 0, 16); u.WriteByte(0xC0); u.WriteByte(0x40);
                    // DesktopName pseudo-rect
                    hdr(new[] { 0, 0, 0, 0 }); be32(-307); be32(3); u.Write(Encoding.ASCII.GetBytes("new"), 0, 3);
                    tx(u.ToArray());
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    RxB(s, 10); // incremental request, delayed by the preview throttle
                    throttleMs = sw.ElapsedMilliseconds;
                    Thread.Sleep(200);
                }
                }
                catch { }
            });
            srv.IsBackground = true; srv.Start();

            var done = new ManualResetEvent(false);
            var c = new RfbClient { HostName = "127.0.0.1", Port = port, Password = "pw", MinFrameMs = 300 };
            c.Updated += delegate { done.Set(); };
            int curW = 0, curHx = 0;
            c.CursorShape += (cpx, cmask, w, h, hx, hy) => { curW = w; curHx = hx; };
            string closed = null;
            c.Closed += (m, a) => closed = m;
            c.Start();
            if (!done.WaitOne(5000)) { Console.WriteLine("      no update received, closed=" + closed); return false; }
            Func<int, int, string> px = (x, y) => { int o = (y * c.W + x) * 4; return c.Fb[o + 2] + "," + c.Fb[o + 1] + "," + c.Fb[o]; };
            bool ok = authOk && c.W == 64 && c.H == 32
                && px(0, 0) == "255,0,0" && px(1, 0) == "0,255,0"          // raw
                && px(4, 0) == "0,0,255" && px(5, 1) == "255,255,255" && px(4, 3) == "0,0,255"   // rre
                && px(16, 0) == "255,255,0" && px(19, 3) == "0,255,255" && px(21, 3) == "255,255,0"; // hextile
            bool zrle = px(40, 0) == "0,0,0" && px(41, 1) == "15,10,5" && px(43, 1) == "21,14,7"
                && px(52, 0) == "255,0,0" && px(53, 0) == "0,0,255" && px(48, 1) == "0,0,255";
            bool pseudo = curW == 2 && curHx == 1 && c.DesktopName == "new";
            Thread.Sleep(600);
            bool throttled = throttleMs >= 200;
            Console.WriteLine((zrle ? "PASS  " : "FAIL  ") + "RFB ZRLE raw + palette-RLE across one zlib stream");
            Console.WriteLine((pseudo ? "PASS  " : "FAIL  ") + "RFB cursor shape + desktop name pseudo-encodings");
            Console.WriteLine((throttled ? "PASS  " : "FAIL  ") + "RFB preview throttle delays next request (" + throttleMs + " ms)");
            if (!ok) Console.WriteLine("      authOk=" + authOk + " px0=" + px(0, 0) + " px1=" + px(1, 0) + " rre=" + px(4, 0) + "/" + px(5, 1) + " hex=" + px(16, 0) + "/" + px(19, 3));
            if (!zrle) Console.WriteLine("      zrle " + px(40, 0) + " " + px(41, 1) + " " + px(43, 1) + " " + px(52, 0) + " " + px(53, 0) + " " + px(48, 1) + " closed=" + closed);
            c.Stop(); lst.Stop();
            return ok && zrle && pseudo && throttled;
        }
    }
}
