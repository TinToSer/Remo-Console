using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RemoConsole
{
    /// <summary>
    /// Responsive tile matrix. The window width and the requested tile width decide the number of columns;
    /// tiles are stretched so every row fills the window exactly. Layout changes and scrolling are eased.
    /// </summary>
    class GridPanel : Panel, IMessageFilter
    {
        public int TargetWidth = 300;
        public readonly List<Control> Items = new List<Control>();
        public int Cols, Rows;
        public event EventHandler LayoutChanged;

        class St { public float X, Y, W; public Rectangle T; }
        class Plan { public List<Rectangle> R = new List<Rectangle>(); public int Cols, Rows, Height, TileW; }

        readonly Dictionary<Control, St> st = new Dictionary<Control, St>();
        readonly Timer timer = new Timer { Interval = 15 };
        float scrollCur, scrollTarget;
        int contentH;
        bool applying;

        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);

        public GridPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            AutoScroll = true;
            timer.Tick += delegate { Tick(); };
        }

        int Gap { get { return Theme.S(16); } }
        int PadX { get { return Theme.S(22); } }
        int PadY { get { return Theme.S(16); } }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Application.AddMessageFilter(this); }
        protected override void OnHandleDestroyed(EventArgs e) { Application.RemoveMessageFilter(this); base.OnHandleDestroyed(e); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { Application.RemoveMessageFilter(this); timer.Dispose(); }
            base.Dispose(disposing);
        }

        // ---------------------------------------------------------------- planning
        Plan MakePlan(int target, int viewW)
        {
            var p = new Plan();
            int avail = Math.Max(Theme.S(200), viewW - 2 * PadX);
            target = Theme.S(target);   // TargetWidth is expressed in 96-dpi units
            int cols = Math.Max(1, (int)Math.Round((avail + Gap) / (double)(target + Gap)));
            int tw = (avail - Gap * (cols - 1)) / cols;
            int ch = CardControl.HeightForWidth(tw);
            p.Cols = cols; p.TileW = tw;
            int x = PadX, y = PadY, col = 0, rows = 0;
            foreach (var it in Items)
            {
                if (it is GroupHeader)
                {
                    if (col > 0) { y += ch + Gap; col = 0; x = PadX; }
                    if (y > PadY) y += Gap / 2;
                    p.R.Add(new Rectangle(PadX, y, avail, it.Height));
                    y += it.Height + Gap;
                }
                else
                {
                    if (col == 0) rows++;
                    p.R.Add(new Rectangle(x, y, tw, ch));
                    col++; x += tw + Gap;
                    if (col == cols) { col = 0; x = PadX; y += ch + Gap; }
                }
            }
            if (col > 0) y += ch + Gap;
            p.Rows = rows;
            p.Height = y - Gap + PadY;
            return p;
        }

        Plan CurrentPlan(int target)
        {
            var p = MakePlan(target, Width);
            if (p.Height > ClientSize.Height && ClientSize.Height > 0)
                p = MakePlan(target, Width - SystemInformation.VerticalScrollBarWidth);
            return p;
        }

        /// <summary>Largest tile width at which every tile fits in the window without scrolling (never below min).</summary>
        public int FitWidth(int min, int max)
        {
            for (int w = max; w > min; w -= 4)
            {
                var p = MakePlan(w, Width);
                if (p.Height <= ClientSize.Height) return w;
            }
            return min;
        }

        // ---------------------------------------------------------------- layout
        public void Relayout(bool animate)
        {
            if (Width <= 0 || Height <= 0 || applying) return;
            var plan = CurrentPlan(TargetWidth);

            var keep = new HashSet<Control>(Items);
            foreach (Control c in Controls) if (!keep.Contains(c)) c.Visible = false;
            var stale = new List<Control>();
            foreach (var k in st.Keys) if (!keep.Contains(k)) stale.Add(k);
            foreach (var k in stale) st.Remove(k);

            for (int i = 0; i < Items.Count; i++)
            {
                var c = Items[i]; var r = plan.R[i];
                if (c.Parent != this) Controls.Add(c);
                c.Visible = true;
                St s;
                if (!st.TryGetValue(c, out s)) { s = new St { X = r.X, Y = r.Y, W = r.Width }; st[c] = s; }
                s.T = r;
                if (!animate) { s.X = r.X; s.Y = r.Y; s.W = r.Width; }
            }
            Cols = plan.Cols; Rows = plan.Rows; contentH = plan.Height;
            AutoScrollMinSize = new Size(0, contentH);
            float max = Math.Max(0, contentH - ClientSize.Height);
            scrollTarget = Math.Max(0, Math.Min(max, scrollTarget));
            if (!animate) scrollCur = scrollTarget;

            ApplyAll();
            if (animate) timer.Start();
            var h = LayoutChanged; if (h != null) h(this, EventArgs.Empty);
        }

        public void ResetScroll() { scrollCur = scrollTarget = 0; ApplyAll(); }

        void ApplyAll()
        {
            applying = true;
            try
            {
                int off = (int)Math.Round(scrollCur);
                if (-AutoScrollPosition.Y != off) AutoScrollPosition = new Point(0, off);
                foreach (var c in Items)
                {
                    St s; if (!st.TryGetValue(c, out s)) continue;
                    var card = c as CardControl;
                    int w = (int)Math.Round(s.W);
                    if (card != null) { if (card.Width != w) card.SetWidth(w); c.Location = new Point((int)Math.Round(s.X), (int)Math.Round(s.Y) - off); }
                    else c.SetBounds((int)Math.Round(s.X), (int)Math.Round(s.Y) - off, w, c.Height);
                }
            }
            finally { applying = false; }
        }

        void Tick()
        {
            bool moving = false;
            foreach (var c in Items)
            {
                St s; if (!st.TryGetValue(c, out s)) continue;
                moving |= Ease(ref s.X, s.T.X); moving |= Ease(ref s.Y, s.T.Y); moving |= Ease(ref s.W, s.T.Width);
            }
            moving |= Ease(ref scrollCur, scrollTarget);
            ApplyAll();
            if (!moving) timer.Stop();
        }

        static bool Ease(ref float cur, float target)
        {
            float d = target - cur;
            if (Math.Abs(d) < 0.6f) { cur = target; return false; }
            cur += d * 0.26f;
            return true;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!IsHandleCreated || applying) return;
            var plan = CurrentPlan(TargetWidth);
            Relayout(plan.Cols != Cols);   // animate only when the matrix actually reshapes
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            if (applying) return;
            scrollCur = scrollTarget = -AutoScrollPosition.Y;
            ApplyAll();
        }

        // ---------------------------------------------------------------- smooth wheel
        public void WheelBy(int delta)
        {
            float max = Math.Max(0, contentH - ClientSize.Height);
            if (max <= 0) return;
            scrollTarget = Math.Max(0, Math.Min(max, scrollTarget - delta / 120f * Theme.S(120)));
            timer.Start();
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != 0x020A || !IsHandleCreated || !Visible) return false;
            try
            {
                var c = Control.FromChildHandle(WindowFromPoint(Cursor.Position));
                bool inside = false;
                for (var p = c; p != null; p = p.Parent)
                {
                    var rv = p as RemoteView;
                    if (rv != null && rv.Interactive) return false;
                    if (p == this) { inside = true; break; }
                }
                if (!inside) return false;
                WheelBy((short)(((long)m.WParam >> 16) & 0xFFFF));
                return true;
            }
            catch { return false; }
        }
    }
}
