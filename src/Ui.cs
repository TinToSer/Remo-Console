using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RemoConsole
{
    static class Native
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string idlist);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        public static void DarkScroll(Control c)
        {
            Action a = () => { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } };
            if (c.IsHandleCreated) a(); else c.HandleCreated += delegate { a(); };
        }
        public static void DarkTitle(Form f)
        {
            Action a = () => { try { int v = 1; if (DwmSetWindowAttribute(f.Handle, 20, ref v, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref v, 4); } catch { } };
            if (f.IsHandleCreated) a(); else f.HandleCreated += delegate { a(); };
        }
        public static void Cue(TextBox t, string s) { try { SendMessage(t.Handle, 0x1501, (IntPtr)1, s); } catch { } }
    }

    enum BtnKind { Primary, Secondary, Ghost, Danger }

    static class Glyph
    {
        public const string Add = "\uE710", Search = "\uE721", More = "\uE712", Close = "\uE711", Refresh = "\uE72C",
            Camera = "\uE722", Full = "\uE740", Restore = "\uE73F", Edit = "\uE70F", Delete = "\uE74D", Copy = "\uE8C8",
            Folder = "\uE8B7", Menu = "\uE700", Grid = "\uE80A", Monitor = "\uE7F4", Warning = "\uE7BA", Filter = "\uE71C",
            Check = "\uE73E", Key = "\uE765", Open = "\uE8A7", Down = "\uE70D", Right = "\uE76C", Star = "\uE735", Save = "\uE74E", Lock = "\uE72E";
    }

    static class Theme
    {
        public static float Scale = 1f;
        public static int S(int v) { return (int)Math.Round(v * Scale); }
        public static void InitScale() { try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f; } catch { Scale = 1f; } }

        public static readonly Color Bg = Color.FromArgb(17, 19, 25);
        public static readonly Color Surface = Color.FromArgb(23, 26, 33);
        public static readonly Color Input = Color.FromArgb(14, 16, 21);
        public static readonly Color Card = Color.FromArgb(27, 31, 40);
        public static readonly Color CardHover = Color.FromArgb(34, 39, 50);
        public static readonly Color Border = Color.FromArgb(42, 48, 61);
        public static readonly Color BorderStrong = Color.FromArgb(63, 71, 89);
        public static readonly Color Accent = Color.FromArgb(77, 138, 255);
        public static readonly Color AccentHover = Color.FromArgb(104, 158, 255);
        public static readonly Color Text = Color.FromArgb(233, 237, 245);
        public static readonly Color Muted = Color.FromArgb(139, 148, 166);
        public static readonly Color Dim = Color.FromArgb(94, 103, 122);
        public static readonly Color Ok = Color.FromArgb(52, 199, 123);
        public static readonly Color Warn = Color.FromArgb(245, 176, 65);
        public static readonly Color Bad = Color.FromArgb(240, 97, 97);
        public static readonly Color Rdp = Color.FromArgb(88, 148, 255);
        public static readonly Color Vnc = Color.FromArgb(52, 194, 135);

        public static readonly Font Ui = new Font("Segoe UI", 9f);
        public static readonly Font UiSemi = new Font("Segoe UI Semibold", 9.5f);
        public static readonly Font Small = new Font("Segoe UI", 8.25f);
        public static readonly Font SmallBold = new Font("Segoe UI Semibold", 7.75f);
        public static readonly Font Title = new Font("Segoe UI Semibold", 13f);
        public static readonly Font Big = new Font("Segoe UI Semibold", 15f);
        public static readonly Font Icon = new Font("Segoe MDL2 Assets", 10f);
        public static readonly Font IconSmall = new Font("Segoe MDL2 Assets", 7.5f);
        public static readonly Font IconBig = new Font("Segoe MDL2 Assets", 28f);

        public const TextFormatFlags Flat = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;

        public static GraphicsPath Round(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0) return p;
            int d = Math.Max(1, Math.Min(rad * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color fill, Color? border, Rectangle r, int rad)
        {
            using (var p = Round(r, rad))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, p);
                if (border.HasValue) using (var pen = new Pen(border.Value)) g.DrawPath(pen, p);
            }
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        public static void Txt(Graphics g, string t, Font f, Rectangle r, Color c, TextFormatFlags extra = 0)
        {
            TextRenderer.DrawText(g, t, f, r, c, Flat | extra);
        }

        public static void DrawIcon(Graphics g, string glyph, Rectangle r, Color c, Font f = null)
        {
            TextRenderer.DrawText(g, glyph, f ?? Icon, r, c, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        public static void DrawButton(Graphics g, Rectangle r, string glyph, string text, BtnKind k, bool hot, bool down, bool enabled)
        {
            Color bg, fg, border = Color.Empty;
            switch (k)
            {
                case BtnKind.Primary: bg = down ? Color.FromArgb(58, 112, 220) : (hot ? AccentHover : Accent); fg = Color.White; break;
                case BtnKind.Danger: bg = hot ? Color.FromArgb(60, 240, 97, 97) : Color.Transparent; fg = Bad; border = Color.FromArgb(120, 240, 97, 97); break;
                case BtnKind.Ghost: bg = hot ? CardHover : Color.Transparent; fg = hot ? Text : Muted; break;
                default: bg = hot ? CardHover : Card; fg = Text; border = hot ? BorderStrong : Border; break;
            }
            if (!enabled) { fg = Dim; if (k == BtnKind.Primary) bg = Color.FromArgb(50, 66, 100); }
            if (bg != Color.Transparent || border != Color.Empty)
                FillRound(g, bg, border == Color.Empty ? (Color?)null : border, new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), S(6));
            bool hasG = !string.IsNullOrEmpty(glyph), hasT = !string.IsNullOrEmpty(text);
            int iw = hasG ? S(16) : 0, gap = (hasG && hasT) ? S(8) : 0;
            int tw = hasT ? TextRenderer.MeasureText(text, UiSemi, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width : 0;
            int x = r.X + (r.Width - (iw + gap + tw)) / 2;
            if (hasG) { DrawIcon(g, glyph, new Rectangle(x, r.Y, iw, r.Height), fg); x += iw + gap; }
            if (hasT) TextRenderer.DrawText(g, text, UiSemi, new Rectangle(x, r.Y, tw + 2, r.Height), fg, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }

        // ---- dark context menus ----
        public static ContextMenuStrip Menu()
        {
            return new ContextMenuStrip { Renderer = new DarkRenderer(), Font = Ui, ShowImageMargin = false, BackColor = Card, ForeColor = Text };
        }
        public static ToolStripMenuItem Item(ToolStripDropDown m, string text, EventHandler click, bool danger = false)
        {
            var i = new ToolStripMenuItem(text) { Padding = new Padding(S(6), S(5), S(6), S(5)), ForeColor = danger ? Bad : Text, BackColor = Card };
            if (click != null) i.Click += click;
            m.Items.Add(i);
            return i;
        }
        public static void Sep(ToolStripDropDown m) { m.Items.Add(new ToolStripSeparator { BackColor = Card, ForeColor = Border }); }
    }

    class DarkColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return Theme.CardHover; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.CardHover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.CardHover; } }
        public override Color MenuItemBorder { get { return Theme.CardHover; } }
        public override Color MenuBorder { get { return Theme.BorderStrong; } }
        public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Border; } }
    }

    class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? e.Item.ForeColor : Theme.Dim;
            base.OnRenderItemText(e);
        }
    }

    // =====================================================================
    class FlatButton : Control, IButtonControl
    {
        DialogResult dialogResult = DialogResult.None;
        public DialogResult DialogResult { get { return dialogResult; } set { dialogResult = value; } }
        public void NotifyDefault(bool value) { }
        public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }
        public BtnKind Kind = BtnKind.Secondary;
        public string GlyphText, Label;
        bool hot, down;

        public FlatButton(string glyph, string label, BtnKind kind)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            GlyphText = glyph; Label = label; Kind = kind;
            Cursor = Cursors.Hand; BackColor = Color.Transparent;
            AutoFit();
        }

        public void AutoFit()
        {
            int tw = string.IsNullOrEmpty(Label) ? 0 : TextRenderer.MeasureText(Label, Theme.UiSemi, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
            int iw = string.IsNullOrEmpty(GlyphText) ? 0 : Theme.S(16);
            int gap = (tw > 0 && iw > 0) ? Theme.S(8) : 0;
            int pad = (tw == 0) ? Theme.S(10) : Theme.S(14);
            Size = new Size(pad * 2 + iw + gap + tw, Theme.S(34));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Parent != null) { using (var b = new SolidBrush(Parent.BackColor)) e.Graphics.FillRectangle(b, ClientRectangle); }
            Theme.Smooth(e.Graphics);
            Theme.DrawButton(e.Graphics, ClientRectangle, GlyphText, Label, Kind, hot, down, Enabled);
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Enter || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; } base.OnKeyDown(e); }
    }

    /// <summary>Rounded text input with optional icon and placeholder.</summary>
    class Field : Control
    {
        protected readonly TextBox tb = new TextBox();
        public string GlyphText;
        bool focused;

        public Field(string placeholder = null, string glyph = null, bool multiline = false)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            GlyphText = glyph;
            tb.BorderStyle = BorderStyle.None; tb.BackColor = Theme.Input; tb.ForeColor = Theme.Text; tb.Font = Theme.Ui;
            tb.Multiline = multiline;
            Controls.Add(tb);
            Height = multiline ? Theme.S(80) : Theme.S(34);
            tb.Enter += delegate { focused = true; Invalidate(); };
            tb.Leave += delegate { focused = false; Invalidate(); OnLeaveField(); };
            tb.TextChanged += delegate { OnTextChanged(EventArgs.Empty); };
            tb.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter && !tb.Multiline) { e.SuppressKeyPress = true; var f = FindForm(); if (f != null && f.AcceptButton != null) f.AcceptButton.PerformClick(); } };
            if (placeholder != null) tb.HandleCreated += delegate { Native.Cue(tb, placeholder); };
            DoLayout();
        }

        protected virtual void OnLeaveField() { }
        public bool Password { get { return tb.UseSystemPasswordChar; } set { tb.UseSystemPasswordChar = value; } }
        public override string Text { get { return tb.Text; } set { tb.Text = value; } }
        public void SelectAllText() { tb.SelectAll(); }
        public new bool Focus() { return tb.Focus(); }
        public void SetSuggestions(IEnumerable<string> items)
        {
            if (items == null) return;
            var col = new AutoCompleteStringCollection();
            foreach (var it in items) if (!string.IsNullOrWhiteSpace(it)) col.Add(it);
            tb.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            tb.AutoCompleteSource = AutoCompleteSource.CustomSource;
            tb.AutoCompleteCustomSource = col;
        }

        void DoLayout()
        {
            int left = Theme.S(10) + (GlyphText != null ? Theme.S(22) : 0);
            if (tb.Multiline) { tb.SetBounds(left, Theme.S(8), Math.Max(10, Width - left - Theme.S(10)), Math.Max(10, Height - Theme.S(16))); }
            else { tb.Width = Math.Max(10, Width - left - Theme.S(10)); tb.Location = new Point(left, (Height - tb.Height) / 2); }
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); DoLayout(); }
        protected override void OnMouseDown(MouseEventArgs e) { tb.Focus(); base.OnMouseDown(e); }
        protected override void OnEnabledChanged(EventArgs e) { tb.Enabled = Enabled; tb.ForeColor = Enabled ? Theme.Text : Theme.Dim; Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (Parent != null) using (var b = new SolidBrush(Parent.BackColor)) g.FillRectangle(b, ClientRectangle);
            Theme.Smooth(g);
            Theme.FillRound(g, Theme.Input, focused ? Theme.Accent : Theme.Border, new Rectangle(0, 0, Width - 1, Height - 1), Theme.S(6));
            if (GlyphText != null) Theme.DrawIcon(g, GlyphText, new Rectangle(Theme.S(8), 0, Theme.S(20), Height), focused ? Theme.Accent : Theme.Dim);
        }
    }

    class NumField : Field
    {
        public int Min = 1, Max = 65535;
        public NumField(int min, int max) : base() { Min = min; Max = max; tb.KeyPress += delegate(object s, KeyPressEventArgs e) { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; }; }
        public int Value
        {
            get { int v; if (!int.TryParse(Text, out v)) v = Min; return Math.Max(Min, Math.Min(Max, v)); }
            set { Text = Math.Max(Min, Math.Min(Max, value)).ToString(); }
        }
        protected override void OnLeaveField() { Value = Value; }
    }

    class Segmented : Control
    {
        public string[] Options = new string[0];
        public Color[] Tints;
        int selected = -1, hover = -1;
        public event EventHandler Changed;

        public Segmented(params string[] options)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Options = options; Cursor = Cursors.Hand;
            int tw = 0; foreach (var o in options) tw += TextRenderer.MeasureText(o, Theme.UiSemi).Width + Theme.S(22);
            Size = new Size(tw + Theme.S(8), Theme.S(34));
        }
        public int Selected { get { return selected; } set { if (selected != value) { selected = value; Invalidate(); } } }
        public string SelectedText { get { return selected >= 0 && selected < Options.Length ? Options[selected] : ""; } set { Selected = Array.IndexOf(Options, value); } }

        Rectangle Seg(int i)
        {
            int n = Math.Max(1, Options.Length);
            int inner = Width - Theme.S(8);
            return new Rectangle(Theme.S(4) + inner * i / n, Theme.S(4), inner / n, Height - Theme.S(8));
        }
        int Hit(Point p) { for (int i = 0; i < Options.Length; i++) if (Seg(i).Contains(p)) return i; return -1; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (Parent != null) using (var b = new SolidBrush(Parent.BackColor)) g.FillRectangle(b, ClientRectangle);
            Theme.Smooth(g);
            Theme.FillRound(g, Theme.Input, Theme.Border, new Rectangle(0, 0, Width - 1, Height - 1), Theme.S(7));
            for (int i = 0; i < Options.Length; i++)
            {
                var r = Seg(i);
                bool sel = i == selected;
                Color tint = (Tints != null && i < Tints.Length) ? Tints[i] : Theme.Accent;
                if (sel) Theme.FillRound(g, Color.FromArgb(52, tint), Color.FromArgb(110, tint), r, Theme.S(5));
                else if (i == hover) Theme.FillRound(g, Theme.CardHover, null, r, Theme.S(5));
                TextRenderer.DrawText(g, Options[i], Theme.UiSemi, r, sel ? (tint == Theme.Muted ? Theme.Text : tint) : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
        }
        protected override void OnMouseMove(MouseEventArgs e) { int h = Hit(e.Location); if (h != hover) { hover = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            int h = Hit(e.Location);
            if (h >= 0 && h != selected) { selected = h; Invalidate(); var c = Changed; if (c != null) c(this, EventArgs.Empty); }
            base.OnMouseUp(e);
        }
    }

    class Toggle : Control
    {
        bool on, hot;
        public string Label;
        public event EventHandler Changed;
        public Toggle(string label)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Label = label; Cursor = Cursors.Hand;
            Size = new Size(Theme.S(44) + TextRenderer.MeasureText(label, Theme.Ui, Size.Empty, TextFormatFlags.NoPadding).Width + Theme.S(4), Theme.S(26));
        }
        public bool Checked { get { return on; } set { if (on != value) { on = value; Invalidate(); } } }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (Parent != null) using (var b = new SolidBrush(Parent.BackColor)) g.FillRectangle(b, ClientRectangle);
            Theme.Smooth(g);
            int w = Theme.S(34), h = Theme.S(18), y = (Height - h) / 2;
            Theme.FillRound(g, on ? Theme.Accent : Theme.BorderStrong, null, new Rectangle(0, y, w, h), h / 2);
            int d = h - Theme.S(6);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, on ? w - d - Theme.S(3) : Theme.S(3), y + Theme.S(3), d, d);
            Theme.Txt(g, Label, Theme.Ui, new Rectangle(w + Theme.S(10), 0, Width - w - Theme.S(10), Height), hot ? Theme.Text : Color.FromArgb(205, 211, 224));
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e) { on = !on; Invalidate(); var c = Changed; if (c != null) c(this, EventArgs.Empty); base.OnMouseUp(e); }
    }

    class Slider : Control
    {
        public int Min = 0, Max = 100; int val; bool drag;
        public event EventHandler ValueChanged;
        public Slider() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); Size = new Size(Theme.S(120), Theme.S(34)); Cursor = Cursors.Hand; }
        public int Value { get { return val; } set { val = Math.Max(Min, Math.Min(Max, value)); Invalidate(); } }
        int Pad { get { return Theme.S(9); } }
        void SetFrom(int x)
        {
            int nv = Min + (int)Math.Round((double)(x - Pad) / Math.Max(1, Width - 2 * Pad) * (Max - Min));
            nv = Math.Max(Min, Math.Min(Max, nv));
            if (nv != val) { val = nv; Invalidate(); var c = ValueChanged; if (c != null) c(this, EventArgs.Empty); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (Parent != null) using (var b = new SolidBrush(Parent.BackColor)) g.FillRectangle(b, ClientRectangle);
            Theme.Smooth(g);
            int th = Theme.S(4), y = Height / 2 - th / 2;
            int x = Pad + (int)((double)(val - Min) / Math.Max(1, Max - Min) * (Width - 2 * Pad));
            Theme.FillRound(g, Theme.BorderStrong, null, new Rectangle(Pad, y, Width - 2 * Pad, th), th / 2);
            if (x > Pad) Theme.FillRound(g, Theme.Accent, null, new Rectangle(Pad, y, x - Pad, th), th / 2);
            int d = Theme.S(14);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, x - d / 2, Height / 2 - d / 2, d, d);
        }
        protected override void OnMouseDown(MouseEventArgs e) { drag = true; Capture = true; SetFrom(e.X); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) SetFrom(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Capture = false; base.OnMouseUp(e); }
    }

    /// <summary>Browser-style tab strip: fixed Dashboard tab + closable session tabs.</summary>
    class TabStrip : Control
    {
        public class Tab { public string Id, Title, Proto; public ViewState State; }
        public readonly List<Tab> Items = new List<Tab>();
        int selected, hover = -1; bool hoverClose;
        public event Action<int> SelectedChanged;
        public event Action<int> CloseRequested;

        public TabStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(40); Dock = DockStyle.Top;
            Items.Add(new Tab { Id = null, Title = "Dashboard" });
        }
        public int Selected { get { return selected; } set { selected = value; Invalidate(); var c = SelectedChanged; if (c != null) c(value); } }

        Rectangle TabRect(int i)
        {
            int dashW = Theme.S(140), pad = Theme.S(10);
            if (i == 0) return new Rectangle(pad, Theme.S(6), dashW, Height - Theme.S(6));
            int n = Items.Count - 1;
            int avail = Width - pad - dashW - Theme.S(12) - Theme.S(6) * n;
            int w = Math.Max(Theme.S(110), Math.Min(Theme.S(220), n > 0 ? avail / n : Theme.S(220)));
            return new Rectangle(pad + dashW + Theme.S(6) + (i - 1) * (w + Theme.S(6)), Theme.S(6), w, Height - Theme.S(6));
        }
        Rectangle CloseRect(Rectangle t) { int d = Theme.S(22); return new Rectangle(t.Right - d - Theme.S(8), t.Y + (t.Height - d) / 2, d, d); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Smooth(g);
            g.Clear(Theme.Surface);
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            for (int i = 0; i < Items.Count; i++)
            {
                var t = TabRect(i); var it = Items[i];
                bool sel = i == selected, hov = i == hover;
                var tr = new Rectangle(t.X, t.Y, t.Width, t.Height + Theme.S(2));
                if (sel)
                {
                    using (var path = Theme.Round(tr, Theme.S(8))) using (var b = new SolidBrush(Theme.Bg)) g.FillPath(b, path);
                    using (var pen = new Pen(Theme.Border)) { using (var path = Theme.Round(tr, Theme.S(8))) g.DrawPath(pen, path); }
                    using (var b = new SolidBrush(Theme.Bg)) g.FillRectangle(b, t.X + 1, Height - 2, t.Width - 1, 3);
                    using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, t.X + Theme.S(10), t.Y, t.Width - Theme.S(20), Theme.S(2));
                }
                else if (hov) Theme.FillRound(g, Theme.CardHover, null, new Rectangle(t.X, t.Y + Theme.S(3), t.Width, t.Height - Theme.S(4)), Theme.S(7));

                Color fg = sel ? Theme.Text : Theme.Muted;
                if (i == 0)
                {
                    Theme.DrawIcon(g, Glyph.Grid, new Rectangle(t.X + Theme.S(10), t.Y, Theme.S(20), t.Height), sel ? Theme.Accent : Theme.Muted);
                    Theme.Txt(g, "Dashboard", Theme.UiSemi, new Rectangle(t.X + Theme.S(36), t.Y, t.Width - Theme.S(44), t.Height), fg);
                }
                else
                {
                    Color dot = it.State == ViewState.Connected ? Theme.Ok : it.State == ViewState.Connecting ? Theme.Warn : it.State == ViewState.Failed ? Theme.Bad : Theme.Dim;
                    using (var b = new SolidBrush(dot)) g.FillEllipse(b, t.X + Theme.S(12), t.Y + t.Height / 2 - Theme.S(4), Theme.S(8), Theme.S(8));
                    Theme.Txt(g, it.Title, Theme.UiSemi, new Rectangle(t.X + Theme.S(28), t.Y, t.Width - Theme.S(28) - Theme.S(36), t.Height), fg);
                    if (sel || hov)
                    {
                        var cr = CloseRect(t);
                        if (hov && hoverClose) Theme.FillRound(g, Color.FromArgb(70, 255, 255, 255), null, cr, Theme.S(5));
                        Theme.DrawIcon(g, Glyph.Close, cr, hoverClose && hov ? Theme.Text : Theme.Muted, Theme.IconSmall);
                    }
                }
            }
        }

        int Hit(Point p) { for (int i = 0; i < Items.Count; i++) if (TabRect(i).Contains(p)) return i; return -1; }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = Hit(e.Location); bool hc = h > 0 && CloseRect(TabRect(h)).Contains(e.Location);
            if (h != hover || hc != hoverClose) { hover = h; hoverClose = hc; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; hoverClose = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            int h = Hit(e.Location);
            if (h < 0) return;
            if (h > 0 && (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && CloseRect(TabRect(h)).Contains(e.Location))))
            { var c = CloseRequested; if (c != null) c(h); return; }
            if (e.Button == MouseButtons.Left && h != selected) Selected = h;
            base.OnMouseDown(e);
        }
    }

    /// <summary>Interactive collapsible section heading used on the dashboard for host groups and pinned hosts.</summary>
    class GroupHeader : Control
    {
        public string GroupKey;
        public string Caption;
        public int Count;
        public int LiveCount;
        public bool Collapsed;
        public bool IsPinnedSection;
        public event EventHandler ToggleRequested;
        bool hover, hoverMenu;

        public GroupHeader(string groupKey, string caption, int count, int liveCount, bool collapsed, bool isPinnedSection = false)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            GroupKey = groupKey; Caption = caption; Count = count; LiveCount = liveCount; Collapsed = collapsed; IsPinnedSection = isPinnedSection;
            Height = Theme.S(36);
            Margin = new Padding(Theme.S(8), Theme.S(8), Theme.S(8), 0);
            Cursor = Cursors.Hand;
        }

        Rectangle MenuRect { get { int d = Theme.S(26); return new Rectangle(Width - d - Theme.S(2), (Height - Theme.S(4) - d) / 2, d, d); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            Theme.Smooth(g);
            int midH = Height - Theme.S(4);
            if (hover)
                Theme.FillRound(g, Color.FromArgb(18, 255, 255, 255), null, new Rectangle(0, Theme.S(2), Width - 1, midH - Theme.S(4)), Theme.S(6));

            int x = Theme.S(4);
            Theme.DrawIcon(g, Collapsed ? Glyph.Right : Glyph.Down, new Rectangle(x, 0, Theme.S(18), midH), hover ? Theme.Text : Theme.Muted, Theme.IconSmall);
            x += Theme.S(22);
            if (IsPinnedSection)
            {
                Theme.DrawIcon(g, Glyph.Star, new Rectangle(x, 0, Theme.S(16), midH), Theme.Warn, Theme.IconSmall);
                x += Theme.S(20);
            }
            string cap = Caption.ToUpperInvariant();
            int cw = TextRenderer.MeasureText(cap, Theme.SmallBold, Size.Empty, TextFormatFlags.NoPadding).Width;
            Theme.Txt(g, cap, Theme.SmallBold, new Rectangle(x, 0, cw + 4, midH), IsPinnedSection ? Theme.Text : (hover ? Theme.Text : Theme.Muted));
            x += cw + Theme.S(10);

            string cnt = Count.ToString();
            int nw = TextRenderer.MeasureText(cnt, Theme.SmallBold, Size.Empty, TextFormatFlags.NoPadding).Width;
            var pill = new Rectangle(x, (midH - Theme.S(18)) / 2, Math.Max(Theme.S(22), nw + Theme.S(12)), Theme.S(18));
            Theme.FillRound(g, Theme.Card, Theme.Border, pill, pill.Height / 2);
            TextRenderer.DrawText(g, cnt, Theme.SmallBold, pill, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x = pill.Right + Theme.S(8);

            if (LiveCount > 0)
            {
                string lt = LiveCount + " live";
                int lw = TextRenderer.MeasureText(lt, Theme.SmallBold, Size.Empty, TextFormatFlags.NoPadding).Width;
                var lpill = new Rectangle(x, (midH - Theme.S(18)) / 2, lw + Theme.S(18), Theme.S(18));
                Theme.FillRound(g, Color.FromArgb(30, Theme.Ok), null, lpill, lpill.Height / 2);
                using (var b = new SolidBrush(Theme.Ok)) g.FillEllipse(b, lpill.X + Theme.S(6), lpill.Y + lpill.Height / 2 - Theme.S(2), Theme.S(5), Theme.S(5));
                Theme.Txt(g, lt, Theme.SmallBold, new Rectangle(lpill.X + Theme.S(14), lpill.Y, lw + 4, lpill.Height), Theme.Ok);
                x = lpill.Right + Theme.S(8);
            }

            if (ContextMenuStrip != null)
            {
                var mr = MenuRect;
                if (hoverMenu) Theme.FillRound(g, Theme.BorderStrong, null, mr, Theme.S(6));
                if (hover || hoverMenu) Theme.DrawIcon(g, Glyph.More, mr, hoverMenu ? Theme.Text : Theme.Muted);
            }

            using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, Height - Theme.S(2), Width, Height - Theme.S(2));
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; hoverMenu = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool hm = ContextMenuStrip != null && MenuRect.Contains(e.Location);
            if (hm != hoverMenu) { hoverMenu = hm; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (ContextMenuStrip != null && MenuRect.Contains(e.Location))
                    ContextMenuStrip.Show(this, new Point(MenuRect.Left - Theme.S(130), MenuRect.Bottom + 2));
                else
                {
                    var h = ToggleRequested;
                    if (h != null) h(this, EventArgs.Empty);
                }
            }
            base.OnMouseUp(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && ContextMenuStrip != null) { var m = ContextMenuStrip; ContextMenuStrip = null; m.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
