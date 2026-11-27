using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace RemoConsole
{
    /// <summary>A host tile. Live: hosts the running session view as a preview. Static: shows the last saved thumbnail.</summary>
    class CardControl : Control
    {
        public Host Host;
        public bool IsLive;
        public Image Thumb;
        public DateTime ThumbTime;
        public RemoteView View;
        public ViewState Status = ViewState.Idle;
        public string ErrorText = "";
        public const int ReachUnknown = -2;
        /// <summary>Static cards: TCP connect time in ms, -1 unreachable, ReachUnknown not checked.</summary>
        public int ReachMs = ReachUnknown;

        public void SetReach(int ms)
        {
            if (ms == ReachMs) return;
            bool changed = (ms >= 0) != (ReachMs >= 0) || ms < 0 || ReachMs < 0;
            ReachMs = ms;
            if (changed) Invalidate();   // latency jitter alone does not need a repaint
        }
        bool hover; int hoverBtn; // 1 live pill, 2 open, 3 menu

        public event EventHandler OpenRequested;
        readonly ToolTip tip = new ToolTip { InitialDelay = 400 };

        public CardControl(Host h)
        {
            Host = h;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Margin = new Padding(Theme.S(8));
        }

        // The session window uses the top of the card; name, menu, address, status and Open live in the footer.
        int HeaderH { get { return Theme.S(10); } }
        int FooterH { get { return Theme.S(52); } }
        int SidePad { get { return Theme.S(10); } }

        public static int HeightForWidth(int w)
        {
            return Theme.S(10) + (int)((w - 2 * Theme.S(10)) * 9.0 / 16.0) + Theme.S(52);
        }

        public void SetWidth(int w)
        {
            Size = new Size(w, HeightForWidth(w));
            LayoutView();
        }

        Rectangle BodyRect { get { return new Rectangle(SidePad, HeaderH, Width - 2 * SidePad, Height - HeaderH - FooterH); } }
        Rectangle OpenRect { get { int w = Theme.S(70), h = Theme.S(30); return new Rectangle(Width - SidePad - Theme.S(2) - w, Height - FooterH + (FooterH - h) / 2 - Theme.S(1), w, h); } }
        Rectangle MenuRect { get { int d = Theme.S(28); var o = OpenRect; return new Rectangle(o.X - Theme.S(6) - d, Height - FooterH + (FooterH - d) / 2 - Theme.S(1), d, d); } }
        bool ShowChip { get { return Width >= Theme.S(330); } }
        Rectangle NameRect { get { int right = MenuRect.X - Theme.S(6) - (ShowChip ? Theme.S(46) : 0); return new Rectangle(SidePad + Theme.S(4), Height - FooterH, Math.Max(10, right - SidePad - Theme.S(4)), FooterH - Theme.S(2)); } }

        public void AttachView(RemoteView v)
        {
            View = v;
            v.Interactive = false;
            v.Parent = this;
            v.Dock = DockStyle.None;
            v.ContextMenuStrip = ContextMenuStrip;
            LayoutView();
            v.Visible = true;
            v.BringToFront();
            Invalidate();
        }

        public void DetachView()
        {
            if (View != null && View.Parent == this) Controls.Remove(View);
            View = null;
            Invalidate();
        }

        void LayoutView() { if (View != null && View.Parent == this) View.Bounds = BodyRect; }
        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutView(); }

        public void SetThumb(Image img, DateTime when)
        {
            var old = Thumb; Thumb = img; ThumbTime = when;
            if (old != null) old.Dispose();
            Invalidate();
        }

        public static Image LoadThumb(string path, out DateTime when)
        {
            when = DateTime.MinValue;
            try
            {
                if (!File.Exists(path)) return null;
                byte[] b = File.ReadAllBytes(path);
                when = File.GetLastWriteTime(path);
                using (var ms = new MemoryStream(b))
                using (var tmp = Image.FromStream(ms))
                    return new Bitmap(tmp);
            }
            catch { return null; }
        }

        static string Ago(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalSeconds < 90) return "just now";
            if (d.TotalMinutes < 90) return (int)d.TotalMinutes + " min ago";
            if (d.TotalHours < 36) return (int)d.TotalHours + " h ago";
            return (int)d.TotalDays + " d ago";
        }

        string InfoText()
        {
            string addr = Host.Address + ":" + Host.Port;
            string s = (Host.Pinned ? "★ " : "") + Host.Name + "\n" + Host.Protocol + " · " + addr + "\n" + StatusLabel();
            if (!string.IsNullOrEmpty(Host.Group)) s += "\nGroup: " + Host.Group;
            if (IsLive && Status == ViewState.Failed && !string.IsNullOrEmpty(ErrorText)) s += "\n" + ErrorText;
            return s;
        }

        Color StatusColor()
        {
            if (!IsLive)
            {
                if (ReachMs >= 0) return Color.FromArgb(150, Theme.Ok);
                if (ReachMs == -1) return Color.FromArgb(170, Theme.Bad);
                return Theme.Dim;
            }
            switch (Status)
            {
                case ViewState.Connected: return Theme.Ok;
                case ViewState.Connecting: return Theme.Warn;
                case ViewState.Failed: return Theme.Bad;
                default: return Theme.Dim;
            }
        }

        string StatusLabel()
        {
            if (!IsLive)
            {
                string r = ReachMs >= 0 ? "Static · host reachable (" + ReachMs + " ms)" : ReachMs == -1 ? "Static · host unreachable" : "Static";
                return Thumb != null ? r + " · preview " + Ago(ThumbTime) : r;
            }
            switch (Status)
            {
                case ViewState.Connected: return "Connected";
                case ViewState.Connecting: return "Connecting…";
                case ViewState.Failed: return "Connection failed";
                default: return "Starting…";
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            g.Clear(Theme.Bg);
            var card = new Rectangle(0, 0, Width - 1, Height - 1);
            // live cards get a green frame (red if the session failed); static cards a neutral one
            Color frame = Theme.Border;
            if (IsLive) frame = Status == ViewState.Failed ? Color.FromArgb(200, Theme.Bad) : Theme.Ok;
            else if (hover) frame = Color.FromArgb(120, Theme.Accent);
            Theme.FillRound(g, hover ? Theme.CardHover : Theme.Card, frame, card, Theme.S(10));
            if (IsLive)
                using (var pen = new Pen(Color.FromArgb(hover ? 255 : 220, frame), Theme.S(2)))
                using (var path = Theme.Round(new Rectangle(1, 1, Width - 3, Height - 3), Theme.S(10)))
                    g.DrawPath(pen, path);

            Color pc = Host.IsRdp ? Theme.Rdp : Theme.Vnc;

            // ---- body
            var body = BodyRect;
            using (var path = Theme.Round(body, Theme.S(6)))
            {
                var oldClip = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                using (var br = new SolidBrush(Theme.Input)) g.FillRectangle(br, body);
                if (Thumb != null && Thumb.Width > 0 && Thumb.Height > 0)
                {
                    float s = Math.Min((float)body.Width / Thumb.Width, (float)body.Height / Thumb.Height);
                    int w = (int)(Thumb.Width * s), h = (int)(Thumb.Height * s);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(Thumb, body.X + (body.Width - w) / 2, body.Y + (body.Height - h) / 2, w, h);
                    if (!IsLive)
                    {
                        using (var br = new LinearGradientBrush(new Rectangle(body.X, body.Bottom - Theme.S(30), body.Width, Theme.S(30)), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(190, 0, 0, 0), 90f))
                            g.FillRectangle(br, body.X, body.Bottom - Theme.S(30), body.Width, Theme.S(30));
                        Theme.Txt(g, "Last seen " + Ago(ThumbTime), Theme.Small, new Rectangle(body.X + Theme.S(10), body.Bottom - Theme.S(24), body.Width - Theme.S(20), Theme.S(20)), Color.FromArgb(220, 225, 235));
                    }
                }
                else
                {
                    Theme.DrawIcon(g, Glyph.Monitor, new Rectangle(body.X, body.Y + body.Height / 2 - Theme.S(34), body.Width, Theme.S(40)), Theme.BorderStrong, Theme.IconBig);
                    string msg = IsLive ? (Status == ViewState.Failed ? "" : "Waiting for live view…") : "No preview yet — Open once to capture";
                    Theme.Txt(g, msg, Theme.Small, new Rectangle(body.X, body.Y + body.Height / 2 + Theme.S(10), body.Width, Theme.S(22)), Theme.Dim, TextFormatFlags.HorizontalCenter);
                }
                g.Clip = oldClip;
            }

            // ---- footer
            // one row:  ● [★] Name  [RDP]  ⋯  [Open]      (address + status are in the tooltip)
            int fy = Height - FooterH, cy = fy + FooterH / 2 - Theme.S(1);
            using (var b = new SolidBrush(StatusColor())) g.FillEllipse(b, SidePad + Theme.S(5), cy - Theme.S(4), Theme.S(8), Theme.S(8));
            var nr = NameRect;
            int nameLeft = SidePad + Theme.S(20);
            if (Host.Pinned)
            {
                Theme.DrawIcon(g, Glyph.Star, new Rectangle(nameLeft - Theme.S(2), fy, Theme.S(14), FooterH - Theme.S(2)), Theme.Warn, Theme.IconSmall);
                nameLeft += Theme.S(15);
            }
            Theme.Txt(g, Host.Name, Theme.UiSemi, new Rectangle(nameLeft, fy, Math.Max(10, nr.Right - nameLeft), FooterH - Theme.S(2)), Theme.Text);
            if (ShowChip)
            {
                var chip = new Rectangle(MenuRect.X - Theme.S(6) - Theme.S(38), cy - Theme.S(9), Theme.S(38), Theme.S(18));
                Theme.FillRound(g, Color.FromArgb(40, pc), null, chip, Theme.S(5));
                TextRenderer.DrawText(g, Host.Protocol, Theme.SmallBold, chip, pc, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            var mr = MenuRect;
            if (hoverBtn == 3) Theme.FillRound(g, Theme.BorderStrong, null, mr, Theme.S(6));
            Theme.DrawIcon(g, Glyph.More, mr, hoverBtn == 3 ? Theme.Text : Theme.Muted);
            var or = OpenRect;
            Theme.DrawButton(g, new Rectangle(or.X, or.Y, or.Width + 1, or.Height + 1), Glyph.Open, "Open", BtnKind.Primary, hoverBtn == 2, false, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e)
        {
            if (!ClientRectangle.Contains(PointToClient(Cursor.Position))) { hover = false; hoverBtn = 0; Invalidate(); }
            base.OnMouseLeave(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int b = OpenRect.Contains(e.Location) ? 2 : MenuRect.Contains(e.Location) ? 3 : NameRect.Contains(e.Location) ? 4 : 0;
            if (b != hoverBtn)
            {
                hoverBtn = b; Cursor = (b == 2 || b == 3) ? Cursors.Hand : Cursors.Default; Invalidate();
                tip.SetToolTip(this, b == 3 ? "More actions (make live / static, edit, delete…)" : b == 4 ? InfoText() : null);
            }
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (OpenRect.Contains(e.Location)) { var h = OpenRequested; if (h != null) h(this, EventArgs.Empty); }
                else if (MenuRect.Contains(e.Location) && ContextMenuStrip != null) ContextMenuStrip.Show(this, new Point(MenuRect.Left, MenuRect.Bottom + 2));
            }
            base.OnMouseUp(e);
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (e.Y < Height - FooterH && !MenuRect.Contains(e.Location)) { var h = OpenRequested; if (h != null) h(this, EventArgs.Empty); }
            base.OnMouseDoubleClick(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tip.Dispose();
                if (Thumb != null) { Thumb.Dispose(); Thumb = null; }
                if (ContextMenuStrip != null) { var m = ContextMenuStrip; ContextMenuStrip = null; m.Dispose(); }
            }
            base.Dispose(disposing);
        }
    }
}
