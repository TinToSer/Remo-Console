using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace RemoConsole
{
    /// <summary>App logo + wordmark.</summary>
    class BrandMark : Control
    {
        public BrandMark()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Size = new Size(Theme.S(36) + TextRenderer.MeasureText("Remo-Console", Theme.Big).Width + Theme.S(8), Theme.S(34));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Parent != null ? Parent.BackColor : Theme.Surface); Theme.Smooth(g);
            int d = Theme.S(30);
            Theme.FillRound(g, Theme.Accent, null, new Rectangle(0, (Height - d) / 2, d, d), Theme.S(8));
            Theme.DrawIcon(g, Glyph.Monitor, new Rectangle(0, (Height - d) / 2, d, d), Color.White, Theme.Icon);
            Theme.Txt(g, "Remo-Console", Theme.Big, new Rectangle(d + Theme.S(10), 0, Width - d, Height), Theme.Text);
        }
    }

    class MainForm : Form
    {
        readonly Store store;
        Settings S { get { return store.Data.Settings; } }
        List<Host> Hosts { get { return store.Data.Hosts; } }

        readonly Dictionary<string, CardControl> cards = new Dictionary<string, CardControl>();
        readonly Dictionary<string, RemoteView> views = new Dictionary<string, RemoteView>();
        readonly Dictionary<string, Panel> tabsByHost = new Dictionary<string, Panel>();
        readonly Dictionary<string, Panel> sessionBars = new Dictionary<string, Panel>();
        readonly Dictionary<string, DateTime> nextThumb = new Dictionary<string, DateTime>();
        readonly Dictionary<string, string> sessionPwd = new Dictionary<string, string>();
        readonly Dictionary<string, DateTime> nextProbe = new Dictionary<string, DateTime>();
        int probesInFlight;
        readonly List<GroupHeader> headers = new List<GroupHeader>();

        Panel header, divider, content, dashPage, emptyPanel, statusBar;
        Field search; FlatButton btnGroup, btnAdd, btnMenu, emptyAdd; Segmented allMode; Slider size;
        GridPanel grid; TabStrip strip;
        Segmented viewMode; Label matrixLbl; FlatButton btnFit;
        readonly Dictionary<string, FlatButton> closeBtns = new Dictionary<string, FlatButton>();
        string emptyTitle = "", emptySub = "";
        readonly Timer maint = new Timer();
        bool fullscreen; FormWindowState preFs; FormBorderStyle preBorder;
        bool updatingUi;

        public MainForm(Store st)
        {
            store = st;
            Text = "Remo-Console";
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Ui;
            MinimumSize = new Size(Theme.S(900), Theme.S(560));
            StartPosition = FormStartPosition.Manual;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Native.DarkTitle(this);
            BuildUi();
            RestoreBounds_();
            foreach (var h in Hosts) EnsureCard(h);
            ApplyViewMode();
            RefreshLayout();
            maint.Interval = 1000;
            maint.Tick += delegate { try { Maintain(); } catch (Exception ex) { Log.Write("Maintain: " + ex); } };
            maint.Start();
            Shown += delegate
            {
                grid.ResetScroll();
                if (store.LoadNote != null) MessageBox.Show(this, store.LoadNote, "Remo-Console data recovery", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
        }

        // ------------------------------------------------------------ UI
        FlowLayoutPanel Strip(DockStyle dock, Padding pad)
        {
            return new FlowLayoutPanel { Dock = dock, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.Surface, Padding = pad };
        }

        void BuildUi()
        {
            // ---- header
            header = new Panel { Dock = DockStyle.Top, Height = Theme.S(64), BackColor = Theme.Surface };
            var left = Strip(DockStyle.Left, new Padding(Theme.S(20), Theme.S(15), 0, 0));
            var brand = new BrandMark { Margin = new Padding(0, 0, Theme.S(28), 0) };
            search = new Field("Search  (Ctrl+F)", Glyph.Search) { Width = Theme.S(280), Margin = new Padding(0, 0, Theme.S(10), 0) };
            search.TextChanged += delegate { if (!updatingUi) RefreshLayout(); };
            btnGroup = new FlatButton(Glyph.Filter, "All groups", BtnKind.Secondary) { Margin = new Padding(0, 0, Theme.S(10), 0) };
            btnGroup.Click += delegate { ShowGroupMenu(); };
            left.Controls.AddRange(new Control[] { brand, search, btnGroup });

            var right = Strip(DockStyle.Right, new Padding(0, Theme.S(15), Theme.S(20), 0));
            lbl = new Label { Text = "All cards", AutoSize = true, ForeColor = Theme.Muted, BackColor = Theme.Surface, Margin = new Padding(0, Theme.S(8), Theme.S(8), 0), Font = Theme.Ui };
            viewMode = new Segmented("Grid", "Tabs") { Margin = new Padding(0, 0, Theme.S(18), 0) };
            viewMode.SelectedText = S.ViewMode == "Tabs" ? "Tabs" : "Grid";
            viewMode.Changed += delegate { S.ViewMode = viewMode.SelectedText; store.MarkDirty(); ApplyViewMode(); };
            allMode = new Segmented("Live", "Static") { Tints = new[] { Theme.Ok, Theme.Muted }, Margin = new Padding(0, 0, Theme.S(18), 0) };
            allMode.Changed += delegate { SetAllMode(allMode.SelectedText); };
            szIcon = new Label { Text = Glyph.Grid, Font = Theme.Icon, AutoSize = true, ForeColor = Theme.Muted, BackColor = Theme.Surface, Margin = new Padding(0, Theme.S(8), 2, 0) };
            size = new Slider { Min = 240, Max = 760, Width = Theme.S(130), Margin = new Padding(0, 0, Theme.S(4), 0) };
            size.Value = S.CardWidth = Math.Max(size.Min, Math.Min(size.Max, S.CardWidth));
            size.ValueChanged += delegate { S.CardWidth = size.Value; store.MarkDirty(); grid.TargetWidth = size.Value; grid.Relayout(false); };
            matrixLbl = new Label { AutoSize = false, Width = Theme.S(52), Height = Theme.S(34), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Muted, BackColor = Theme.Surface, Font = Theme.Small, Margin = new Padding(0, 0, Theme.S(4), 0) };
            btnFit = new FlatButton(null, "Fit", BtnKind.Ghost) { Margin = new Padding(0, 0, Theme.S(14), 0) };
            btnFit.Click += delegate { int w = grid.FitWidth(size.Min, size.Max); size.Value = w; S.CardWidth = w; store.MarkDirty(); grid.TargetWidth = w; grid.Relayout(true); };
            btnAdd = new FlatButton(Glyph.Add, "Add connection", BtnKind.Primary) { Margin = new Padding(0, 0, Theme.S(10), 0) };
            btnAdd.Click += delegate { AddHost(); };
            btnMenu = new FlatButton(Glyph.Menu, null, BtnKind.Secondary);
            btnMenu.Click += delegate { ShowMainMenu(); };
            right.Controls.AddRange(new Control[] { viewMode, lbl, allMode, szIcon, size, matrixLbl, btnFit, btnAdd, btnMenu });
            header.Controls.Add(right);
            header.Controls.Add(left);
            header.Resize += delegate { LayoutHeader(); };
            divider = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border };

            strip = new TabStrip();
            strip.SelectedChanged += ShowPage;
            strip.CloseRequested += delegate(int i) { if (i > 0 && i < strip.Items.Count) CloseTab(strip.Items[i].Id); };

            // ---- content
            content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            dashPage = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            grid = new GridPanel { Dock = DockStyle.Fill, TargetWidth = S.CardWidth };
            grid.LayoutChanged += delegate { matrixLbl.Text = grid.Items.Count(i => i is CardControl) == 0 ? "" : grid.Cols + " × " + grid.Rows; };
            Native.DarkScroll(grid);
            emptyPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            emptyPanel.Paint += PaintEmpty;
            emptyPanel.Resize += delegate { PlaceEmptyButton(); };
            emptyAdd = new FlatButton(Glyph.Add, "Add connection", BtnKind.Primary);
            emptyAdd.Click += delegate { AddHost(); };
            emptyPanel.Controls.Add(emptyAdd);
            dashPage.Controls.Add(grid);
            dashPage.Controls.Add(emptyPanel);
            content.Controls.Add(dashPage);

            // ---- status bar
            statusBar = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(28), BackColor = Theme.Surface };
            statusBar.Paint += PaintStatusBar;

            Controls.Add(content);
            Controls.Add(statusBar);
            Controls.Add(strip);
            Controls.Add(divider);
            Controls.Add(header);
        }

        Label lbl, szIcon;

        /// <summary>Responsive header: drop optional controls first, then shrink search, so nothing overlaps.</summary>
        void LayoutHeader()
        {
            int w = header.Width;
            Func<int, bool> atLeast = n => w >= Theme.S(n);
            lbl.Visible = szIcon.Visible = atLeast(1480);
            matrixLbl.Visible = btnFit.Visible = atLeast(1060);
            size.Visible = atLeast(900);
            bool wide = atLeast(1340);
            search.Width = atLeast(1480) ? Theme.S(280) : wide ? Theme.S(220) : Theme.S(150);
            btnAdd.Label = wide ? "Add connection" : "Add"; btnAdd.AutoFit();
            string g = S.GroupFilter ?? "";
            btnGroup.Label = wide ? (g == "" ? "All groups" : g) : (g == "" ? "" : (g.Length > 10 ? g.Substring(0, 9) + "…" : g));
            btnGroup.AutoFit(); btnGroup.Invalidate();
        }

        /// <summary>Grid: sessions open as a focused view over the grid ("Back to grid"). Tabs: classic tab strip.</summary>
        void ApplyViewMode()
        {
            bool tabs = S.ViewMode == "Tabs";
            strip.Visible = tabs && !fullscreen;
            foreach (var b in closeBtns.Values)
            {
                b.Label = tabs ? "Close" : "Back to grid";
                b.GlyphText = tabs ? Glyph.Close : Glyph.Grid;
                b.Kind = tabs ? BtnKind.Danger : BtnKind.Secondary;
                b.AutoFit(); b.Invalidate();
            }
        }

        void PlaceEmptyButton()
        {
            emptyAdd.Visible = Hosts.Count == 0;
            emptyAdd.Left = (emptyPanel.Width - emptyAdd.Width) / 2;
            emptyAdd.Top = emptyPanel.Height / 2 + Theme.S(52);
            emptyPanel.Invalidate();
        }

        void PaintEmpty(object s, PaintEventArgs e)
        {
            var g = e.Graphics; var p = emptyPanel;
            int cy = p.Height / 2 - Theme.S(70);
            Theme.DrawIcon(g, Glyph.Monitor, new Rectangle(0, cy, p.Width, Theme.S(60)), Theme.BorderStrong, Theme.IconBig);
            TextRenderer.DrawText(g, emptyTitle, Theme.Title, new Rectangle(0, cy + Theme.S(66), p.Width, Theme.S(30)), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, emptySub, Theme.Ui, new Rectangle(0, cy + Theme.S(98), p.Width, Theme.S(24)), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        void PaintStatusBar(object s, PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Smooth(g);
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, 0, statusBar.Width, 0);
            int connected = views.Values.Count(v => v.State == ViewState.Connected);
            int connecting = views.Values.Count(v => v.State == ViewState.Connecting);
            int failed = views.Values.Count(v => v.State == ViewState.Failed);
            int x = Theme.S(20);
            Action<string, Color?> seg = (t, dot) =>
            {
                if (dot.HasValue) { using (var b = new SolidBrush(dot.Value)) g.FillEllipse(b, x, statusBar.Height / 2 - Theme.S(3) + 1, Theme.S(7), Theme.S(7)); x += Theme.S(12); }
                int w = TextRenderer.MeasureText(t, Theme.Small, Size.Empty, TextFormatFlags.NoPadding).Width;
                Theme.Txt(g, t, Theme.Small, new Rectangle(x, 1, w + 4, statusBar.Height), Theme.Muted);
                x += w + Theme.S(18);
            };
            seg(Hosts.Count + (Hosts.Count == 1 ? " connection" : " connections"), null);
            seg(connected + " connected", Theme.Ok);
            if (connecting > 0) seg(connecting + " connecting", Theme.Warn);
            if (failed > 0) seg(failed + " failed", Theme.Bad);

            string r; Color rc;
            if (store.SaveFailed) { r = "⚠ Save failed — will retry (see remo-console.log)"; rc = Theme.Bad; }
            else if (store.IsDirty) { r = "Saving…"; rc = Theme.Muted; }
            else if (store.LastSaved == DateTime.MinValue) { r = "All data stored safely"; rc = Theme.Muted; }
            else { r = "✓ Saved " + store.LastSaved.ToString("HH:mm:ss"); rc = Theme.Muted; }
            int rw = TextRenderer.MeasureText(r, Theme.Small, Size.Empty, TextFormatFlags.NoPadding).Width;
            Theme.Txt(g, r, Theme.Small, new Rectangle(statusBar.Width - rw - Theme.S(24), 1, rw + 6, statusBar.Height), rc);
        }

        void ShowGroupMenu()
        {
            var m = Theme.Menu();
            m.Closed += delegate { BeginInvoke((Action)(() => m.Dispose())); };
            string cur = S.GroupFilter ?? "";
            Action<string, string> add = (label, value) =>
                Theme.Item(m, (cur == value ? "✓  " : "     ") + label, delegate { S.GroupFilter = value; store.MarkDirty(); RefreshLayout(); });
            add("All groups", "");
            foreach (var g in Hosts.Select(h => h.Group).Where(g => !string.IsNullOrEmpty(g)).Distinct().OrderBy(g => g)) add(g, g);
            m.Show(btnGroup, new Point(0, btnGroup.Height + 4));
        }

        void ShowMainMenu()
        {
            var m = Theme.Menu();
            m.Closed += delegate { BeginInvoke((Action)(() => m.Dispose())); };
            Theme.Item(m, "Open data folder", delegate
            {
                try { Process.Start("explorer.exe", "\"" + store.Dir + "\""); }
                catch (Exception ex) { MessageBox.Show(this, "Could not open data folder: " + ex.Message, "Remo-Console", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            });
            Theme.Item(m, "Back up now", delegate
            {
                MessageBox.Show(this, store.Flush(true) ? "Saved and snapshotted to:\r\n" + AtomicFile.BackupDir(store.DataFile) : "Save failed - see remo-console.log", "Backup");
            });
            Theme.Sep(m);
            Theme.Item(m, "Export connections (no passwords)…", delegate { ExportHosts(); });
            Theme.Item(m, "Import connections…", delegate { ImportHosts(); });
            Theme.Sep(m);
            Theme.Item(m, (S.ProbeStatic ? "✓  " : "     ") + "Check if static hosts are reachable", delegate
            {
                S.ProbeStatic = !S.ProbeStatic; store.MarkDirty(); nextProbe.Clear();
                foreach (var c in cards.Values) c.SetReach(CardControl.ReachUnknown);
            });
            Theme.Sep(m);
            Theme.Item(m, "Shortcuts: Ctrl+N add · Ctrl+F search · F11 fullscreen", null).Enabled = false;
            Theme.Item(m, "Remo-Console v" + Program.AppVersion, null).Enabled = false;
            m.Show(btnMenu, new Point(btnMenu.Width - Theme.S(300), btnMenu.Height + 4));
        }

        // ------------------------------------------------------------ window state
        void RestoreBounds_()
        {
            var bounds = new Rectangle(S.WinX, S.WinY, S.WinW, S.WinH);
            bool visible = S.WinX != -1 && Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(bounds));
            if (visible) Bounds = bounds;
            else { Size = new Size(Math.Max(Theme.S(1100), S.WinW), Math.Max(Theme.S(700), S.WinH)); StartPosition = FormStartPosition.CenterScreen; }
            if (S.Maximized) WindowState = FormWindowState.Maximized;
        }

        void SaveBounds()
        {
            var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            S.WinX = b.X; S.WinY = b.Y; S.WinW = b.Width; S.WinH = b.Height;
            S.Maximized = WindowState == FormWindowState.Maximized;
            store.MarkDirty();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && views.Values.Any(v => tabsByHost.ContainsKey(v.Host.Id) && v.State == ViewState.Connected))
            {
                if (MessageBox.Show(this, "You have open remote sessions. Close Remo-Console and disconnect them?", "Confirm exit", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                { e.Cancel = true; return; }
            }
            maint.Stop();
            SaveBounds();
            foreach (var id in views.Keys.ToList())
            {
                var v = views[id];
                try { SaveThumb(v); } catch { }
                try { v.Stop(); } catch { }
            }
            if (!store.Flush(true))
            {
                if (MessageBox.Show(this, "Could not save your data (see remo-console.log). Exit anyway?", "Save failed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                { e.Cancel = true; maint.Start(); return; }
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) maint.Dispose();
            base.Dispose(disposing);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.N) && strip.Selected == 0) { AddHost(); return true; }
            if (keyData == (Keys.Control | Keys.F) && strip.Selected == 0) { search.Focus(); search.SelectAllText(); return true; }
            if (keyData == Keys.F11) { ToggleFullscreen(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ------------------------------------------------------------ cards
        string Mode(Host h)
        {
            return (h.CardMode == "Live" || h.CardMode == "Static") ? h.CardMode : S.DefaultCardMode;
        }

        CardControl EnsureCard(Host h)
        {
            CardControl c;
            if (cards.TryGetValue(h.Id, out c)) return c;
            c = new CardControl(h);
            DateTime when;
            var img = CardControl.LoadThumb(store.ThumbPath(h.Id), out when);
            if (img != null) c.SetThumb(img, when);
            c.SetWidth(S.CardWidth);
            grid.Controls.Add(c);
            c.OpenRequested += delegate { OpenTab(c.Host); };
            c.ContextMenuStrip = BuildCardMenu(c);
            cards[h.Id] = c;
            return c;
        }

        void ToggleMode(Host h)
        {
            h.CardMode = Mode(h) == "Live" ? "Static" : "Live";
            store.MarkDirty(); ApplyMode(h); SyncAllMode();
        }

        ContextMenuStrip BuildCardMenu(CardControl c)
        {
            var m = Theme.Menu();
            Theme.Item(m, "Open session", delegate { OpenTab(c.Host); });
            var mstsc = Theme.Item(m, "Open in Remote Desktop (mstsc)", delegate { OpenInMstsc(c.Host); });
            var tog = Theme.Item(m, "Make static", delegate { ToggleMode(c.Host); });
            Theme.Item(m, "Refresh preview", delegate { RestartView(c.Host); });
            Theme.Sep(m);
            Theme.Item(m, "Edit…", delegate { EditHost(c.Host); });
            Theme.Item(m, "Duplicate", delegate { DuplicateHost(c.Host); });
            Theme.Item(m, "Copy address", delegate { try { Clipboard.SetText(c.Host.Address); } catch { } });
            Theme.Sep(m);
            Theme.Item(m, "Delete…", delegate { DeleteHost(c.Host); }, true);
            m.Opening += delegate
            {
                tog.Text = Mode(c.Host) == "Live" ? "Make static  (stop live preview)" : "Make live  (show running session)";
                mstsc.Visible = c.Host.IsRdp;
            };
            return m;
        }

        /// <summary>Fallback to the stock client (multi-monitor, RD Gateway, smart cards…). Credentials come from Windows.</summary>
        void OpenInMstsc(Host h)
        {
            string raw = (h.Address ?? "").Trim();
            if (string.IsNullOrEmpty(raw) || raw.Any(ch => char.IsWhiteSpace(ch) || char.IsControl(ch) || ch == '"' || ch == '/'))
            {
                MessageBox.Show(this, "Invalid host address for mstsc.", "Remo-Console", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string addr = raw.Contains(":") && !raw.StartsWith("[") ? "[" + raw + "]" : raw;
            try { Process.Start("mstsc.exe", "/v:" + addr + (h.Port != 3389 ? ":" + h.Port : "")); }
            catch (Exception ex) { MessageBox.Show(this, "Could not start mstsc: " + ex.Message, "Remo-Console"); }
        }

        void SetAllMode(string mode)
        {
            if (mode != "Live" && mode != "Static") return;
            S.DefaultCardMode = mode;
            foreach (var h in Hosts) h.CardMode = "Default";
            store.MarkDirty();
            foreach (var h in Hosts.ToList()) ApplyMode(h);
        }

        void SyncAllMode()
        {
            int n = Hosts.Count(h => Mode(h) == "Live");
            updatingUi = true;
            allMode.Selected = Hosts.Count == 0 ? -1 : (n == Hosts.Count ? 0 : (n == 0 ? 1 : -1));
            updatingUi = false;
        }

        bool Match(Host h, string want, string q)
        {
            return (want == "" || h.Group == want) &&
                (q == "" || (h.Name + " " + h.Address + " " + h.Group + " " + h.Notes).ToLowerInvariant().Contains(q));
        }

        bool laidOutOnce;

        void RefreshLayout()
        {
            string want = S.GroupFilter ?? "";
            var groups = Hosts.Select(h => h.Group).Where(g => !string.IsNullOrEmpty(g)).Distinct().ToList();
            if (want != "" && !groups.Contains(want)) want = S.GroupFilter = "";
            LayoutHeader();

            string q = (search.Text ?? "").Trim().ToLowerInvariant();
            var ordered = Hosts.OrderBy(h => string.IsNullOrEmpty(h.Group) ? 1 : 0).ThenBy(h => h.Group ?? "", StringComparer.CurrentCultureIgnoreCase)
                               .ThenBy(h => h.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            bool anyGroups = groups.Count > 0;

            foreach (var hd in headers) { grid.Controls.Remove(hd); hd.Dispose(); }
            headers.Clear();
            var items = new List<Control>();
            int shown = 0; string lastG = null;
            foreach (var h in ordered)
            {
                bool match = Match(h, want, q);
                string g = h.Group ?? "";
                if (anyGroups && match && lastG != g)
                {
                    int count = ordered.Count(x => (x.Group ?? "") == g && Match(x, want, q));
                    var hd = new GroupHeader(g == "" ? "Ungrouped" : g, count);
                    headers.Add(hd); items.Add(hd);
                    lastG = g;
                }
                var c = EnsureCard(h);
                if (match) { items.Add(c); shown++; }
                ApplyMode(h);
            }
            grid.Items.Clear(); grid.Items.AddRange(items);
            grid.TargetWidth = S.CardWidth;
            grid.Relayout(laidOutOnce && IsHandleCreated);
            laidOutOnce = true;

            bool none = shown == 0;
            if (Hosts.Count == 0) { emptyTitle = "No connections yet"; emptySub = "Add an RDP or VNC host to get started."; }
            else { emptyTitle = "No matches"; emptySub = "Nothing matches your search or group filter."; }
            emptyPanel.Visible = none; grid.Visible = !none;
            PlaceEmptyButton();
            SyncAllMode();
            statusBar.Invalidate();
        }

        // ------------------------------------------------------------ host CRUD
        void AddHost()
        {
            using (var d = new HostDialog(null))
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    Hosts.Add(d.Result);
                    store.MarkDirty(); store.Flush();
                    RefreshLayout();
                }
        }

        void EditHost(Host h)
        {
            using (var d = new HostDialog(h))
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    int i = Hosts.IndexOf(h);
                    if (i < 0) return;
                    var nh = d.Result;
                    Hosts[i] = nh;
                    sessionPwd.Remove(h.Id);
                    CardControl card;
                    if (cards.TryGetValue(h.Id, out card))
                    {
                        card.Host = nh;
                        nextProbe.Remove(h.Id); card.SetReach(CardControl.ReachUnknown);
                    }
                    var tab = strip.Items.FirstOrDefault(t => t.Id == h.Id);
                    if (tab != null) { tab.Title = nh.Name; strip.Invalidate(); }
                    Panel sb; if (sessionBars.TryGetValue(h.Id, out sb)) sb.Invalidate();
                    RemoteView v;
                    if (views.TryGetValue(h.Id, out v))
                    {
                        v.Host = nh;
                        if (!tabsByHost.ContainsKey(h.Id)) DestroyView(nh.Id);
                        else MessageBox.Show(this, "Changes will apply the next time you reconnect this session.", "Saved");
                    }
                    store.MarkDirty(); store.Flush();
                    RefreshLayout();
                    if (card != null) card.Invalidate();
                }
        }

        void DuplicateHost(Host h)
        {
            var c = h.Clone();
            c.Id = Guid.NewGuid().ToString("N");
            c.Name = h.Name + " (copy)";
            Hosts.Add(c);
            store.MarkDirty(); store.Flush();
            RefreshLayout();
        }

        void DeleteHost(Host h)
        {
            if (MessageBox.Show(this, "Delete “" + h.Name + "”? This removes its saved settings and password.", "Delete connection", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            CloseTab(h.Id, true);
            DestroyView(h.Id);
            sessionPwd.Remove(h.Id);
            nextProbe.Remove(h.Id);
            nextThumb.Remove(h.Id);
            CardControl c;
            if (cards.TryGetValue(h.Id, out c)) { grid.Items.Remove(c); grid.Controls.Remove(c); c.Dispose(); cards.Remove(h.Id); }
            try { File.Delete(store.ThumbPath(h.Id)); } catch { }
            Hosts.Remove(h);
            store.MarkDirty(); store.Flush();
            RefreshLayout();
        }

        void ExportHosts()
        {
            using (var sfd = new SaveFileDialog { Filter = "Remo-Console connections (*.json)|*.json", FileName = "remo-console-connections.json" })
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        var copy = Hosts.Select(h => { var c = h.Clone(); c.Pwd = ""; return c; }).ToList();
                        var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                        File.WriteAllText(sfd.FileName, ser.Serialize(copy));
                    }
                    catch (Exception ex) { MessageBox.Show(this, "Export failed: " + ex.Message, "Remo-Console", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }
        }

        void ImportHosts()
        {
            using (var ofd = new OpenFileDialog { Filter = "Remo-Console connections (*.json)|*.json" })
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                        var list = ser.Deserialize<List<Host>>(File.ReadAllText(ofd.FileName)) ?? new List<Host>();
                        int added = 0, skipped = 0;
                        foreach (var h in list)
                        {
                            if (h == null || string.IsNullOrWhiteSpace(h.Address)) { skipped++; continue; }
                            h.Id = Guid.NewGuid().ToString("N"); h.Pwd = "";
                            h.Address = h.Address.Trim();
                            if (h.Protocol != "RDP" && h.Protocol != "VNC") h.Protocol = "RDP";
                            if (h.Port < 1 || h.Port > 65535) h.Port = h.IsRdp ? 3389 : 5900;
                            if (h.CardMode != "Live" && h.CardMode != "Static") h.CardMode = "Default";
                            h.Width = Math.Max(200, Math.Min(8192, h.Width)); h.Height = Math.Max(200, Math.Min(8192, h.Height));
                            if (string.IsNullOrEmpty(h.Name)) h.Name = h.Address;
                            Hosts.Add(h); added++;
                        }
                        store.MarkDirty(); store.Flush(); RefreshLayout();
                        MessageBox.Show(this, "Imported " + added + " connection(s)." + (skipped > 0 ? "\r\nSkipped " + skipped + " entr" + (skipped == 1 ? "y" : "ies") + " without an address." : ""));
                    }
                    catch (Exception ex) { MessageBox.Show(this, "Import failed: " + ex.Message); }
                }
        }

        // ------------------------------------------------------------ views / live-static
        RemoteView CreateView(Host h)
        {
            RemoteView v = h.IsRdp ? (RemoteView)new RdpView() : new VncView();
            v.Host = h;
            v.StateChanged += ViewStateChanged;
            v.PreviewActivated += delegate { OpenTab(v.Host); };
            views[h.Id] = v;
            return v;
        }

        void DestroyView(string id)
        {
            RemoteView v;
            if (!views.TryGetValue(id, out v)) return;
            views.Remove(id);
            v.StateChanged -= ViewStateChanged;
            CardControl c;
            if (cards.TryGetValue(id, out c)) { c.DetachView(); c.Status = ViewState.Idle; c.ErrorText = ""; c.IsLive = Mode(c.Host) == "Live"; c.Invalidate(); }
            try { v.Stop(); } catch { }
            v.Dispose();
        }

        void RestartView(Host h)
        {
            RemoteView v;
            if (views.TryGetValue(h.Id, out v)) { v.NextRetryUtc = DateTime.MinValue; StartView(v); }
            else if (Mode(h) == "Live") ApplyMode(h);
            else OpenTab(h);
        }

        void StartView(RemoteView v)
        {
            try
            {
                v.NextRetryUtc = DateTime.UtcNow.AddSeconds(Math.Max(5, S.RetrySeconds));
                string pw = v.Host.Password, tmp;
                if (string.IsNullOrEmpty(pw) && sessionPwd.TryGetValue(v.Host.Id, out tmp)) pw = tmp;
                v.Start(pw);
            }
            catch (Exception ex) { Log.Write("StartView: " + ex); }
        }

        /// <summary>Make the card match its effective live/static mode.</summary>
        void ApplyMode(Host h)
        {
            CardControl card;
            if (!cards.TryGetValue(h.Id, out card)) return;
            bool live = Mode(h) == "Live";
            card.IsLive = live;
            bool inTab = tabsByHost.ContainsKey(h.Id);
            RemoteView v; views.TryGetValue(h.Id, out v);
            if (live)
            {
                if (v == null) { v = CreateView(h); v.NextRetryUtc = DateTime.MinValue; }
                if (!inTab && card.View != v) card.AttachView(v);
                card.Status = v.State; card.ErrorText = v.Error;
            }
            else if (v != null && !inTab)
            {
                try { SaveThumb(v); } catch { }
                DestroyView(h.Id);
            }
            card.Invalidate();
            statusBar.Invalidate();
        }

        void ViewStateChanged(object sender, EventArgs e)
        {
            var v = (RemoteView)sender;
            CardControl c;
            if (cards.TryGetValue(v.Host.Id, out c)) { c.Status = v.State; c.ErrorText = v.Error; c.Invalidate(); }
            var item = strip.Items.FirstOrDefault(t => t.Id == v.Host.Id);
            if (item != null) { item.State = v.State; strip.Invalidate(); }
            Panel bar; if (sessionBars.TryGetValue(v.Host.Id, out bar)) bar.Invalidate();
            statusBar.Invalidate();
            if (v.State == ViewState.Connected)
            {
                v.Host.LastConnectedUtc = DateTime.UtcNow.Ticks; store.MarkDirty();
                nextThumb[v.Host.Id] = DateTime.UtcNow.AddSeconds(5);
            }
            else if (v.State == ViewState.Failed)
            {
                v.NextRetryUtc = v.NeedAuth ? DateTime.MaxValue : DateTime.UtcNow.AddSeconds(Math.Max(5, S.RetrySeconds));
                if (v.NeedAuth && tabsByHost.ContainsKey(v.Host.Id))
                    BeginInvoke((Action)(() =>
                    {
                        if (!IsDisposed && v.State == ViewState.Failed && v.NeedAuth && AskPassword(v.Host)) { v.NextRetryUtc = DateTime.MinValue; StartView(v); }
                    }));
            }
        }

        void SaveThumb(RemoteView v)
        {
            if (v == null || v.State != ViewState.Connected) return;
            using (var bmp = v.Snapshot(520))
            {
                if (bmp == null) return;
                string path = store.ThumbPath(v.Host.Id), tmp = path + ".tmp";
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 8192, FileOptions.WriteThrough))
                { bmp.Save(fs, ImageFormat.Png); fs.Flush(true); }
                if (File.Exists(path)) File.Replace(tmp, path, null, true); else File.Move(tmp, path);
                CardControl c;
                if (cards.TryGetValue(v.Host.Id, out c)) c.SetThumb(new Bitmap(bmp), DateTime.Now);
            }
        }

        // ------------------------------------------------------------ maintenance loop (1 Hz)
        void Maintain()
        {
            store.Flush(); // debounced: no-op unless dirty
            int connecting = views.Values.Count(x => x.State == ViewState.Connecting);
            var now = DateTime.UtcNow;
            foreach (var kv in views.ToList())
            {
                var v = kv.Value; var id = kv.Key;
                bool inTab = tabsByHost.ContainsKey(id);
                if (!inTab && Mode(v.Host) == "Live" && (v.State == ViewState.Idle || v.State == ViewState.Failed) && now >= v.NextRetryUtc && connecting < 2)
                {
                    StartView(v); connecting++;
                }
                DateTime due;
                if (v.State == ViewState.Connected && nextThumb.TryGetValue(id, out due) && now >= due)
                {
                    try { SaveThumb(v); } catch (Exception ex) { Log.Write("thumb: " + ex.Message); }
                    nextThumb[id] = now.AddSeconds(90);
                }
            }
            foreach (var c in cards.Values) if (!c.IsLive && c.Thumb != null) c.Invalidate(); // refresh "last seen x ago"
            ProbeStatic(now);
            statusBar.Invalidate();
        }

        // ------------------------------------------------------------ reachability of static cards
        /// <summary>Plain TCP connect to the RDP/VNC port (no login) once a minute, max 6 at a time, off the UI thread.</summary>
        void ProbeStatic(DateTime now)
        {
            if (!S.ProbeStatic || WindowState == FormWindowState.Minimized) return;
            foreach (var c in cards.Values)
            {
                if (probesInFlight >= 6) break;
                var h = c.Host;
                if (c.IsLive || !c.Visible || tabsByHost.ContainsKey(h.Id) || string.IsNullOrWhiteSpace(h.Address)) continue;
                DateTime due;
                if (nextProbe.TryGetValue(h.Id, out due) && now < due) continue;
                nextProbe[h.Id] = now.AddSeconds(60);
                System.Threading.Interlocked.Increment(ref probesInFlight);
                string id = h.Id, addr = h.Address.Trim(); int port = h.Port;
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    int ms = TcpProbe(addr, port, 3000);
                    try
                    {
                        if (IsDisposed || !IsHandleCreated) return;
                        BeginInvoke((Action)(() =>
                        {
                            CardControl cc;
                            if (S.ProbeStatic && cards.TryGetValue(id, out cc) && !cc.IsLive && cc.Host.Address.Trim() == addr && cc.Host.Port == port) cc.SetReach(ms);
                        }));
                    }
                    catch { }
                    finally { System.Threading.Interlocked.Decrement(ref probesInFlight); }
                });
            }
        }

        /// <summary>Connect time in ms, or -1 if the port is not reachable within the timeout.</summary>
        static int TcpProbe(string addr, int port, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            using (var t = new System.Net.Sockets.TcpClient())
            {
                try
                {
                    var ar = t.BeginConnect(addr, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) return -1;
                    t.EndConnect(ar);
                    return (int)sw.ElapsedMilliseconds;
                }
                catch { return -1; }
            }
        }

        // ------------------------------------------------------------ session tabs
        void ShowPage(int idx)
        {
            dashPage.Visible = idx == 0;
            for (int i = 1; i < strip.Items.Count; i++)
            {
                Panel pg;
                if (tabsByHost.TryGetValue(strip.Items[i].Id, out pg)) pg.Visible = i == idx;
            }
            if (idx > 0)
            {
                RemoteView v;
                if (views.TryGetValue(strip.Items[idx].Id, out v)) v.FocusInput();
            }
        }

        void OpenTab(Host h)
        {
            Panel existing;
            if (tabsByHost.TryGetValue(h.Id, out existing))
            {
                int i = strip.Items.FindIndex(t => t.Id == h.Id);
                if (i > 0) strip.Selected = i;
                return;
            }

            RemoteView v;
            if (!views.TryGetValue(h.Id, out v)) v = CreateView(h);
            if (v.NeedAuth && !AskPassword(h)) return;

            CardControl card;
            if (cards.TryGetValue(h.Id, out card)) card.DetachView();

            var page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Visible = false };
            var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
            var bar = BuildSessionBar(h, v);
            v.Parent = body; v.Dock = DockStyle.Fill; v.Visible = true;
            v.Interactive = true;
            page.Controls.Add(body);
            page.Controls.Add(bar);
            body.BringToFront();
            content.Controls.Add(page);
            tabsByHost[h.Id] = page; sessionBars[h.Id] = bar;
            ApplyViewMode();
            strip.Items.Add(new TabStrip.Tab { Id = h.Id, Title = h.Name, Proto = h.Protocol, State = v.State });
            strip.Selected = strip.Items.Count - 1;

            if (v.State == ViewState.Idle || v.State == ViewState.Failed) { v.NextRetryUtc = DateTime.MinValue; StartView(v); }
            v.FocusInput();
        }

        bool AskPassword(Host h)
        {
            using (var p = new PasswordPrompt("Password for " + h.Name, "This server requires a password."))
            {
                if (p.ShowDialog(this) != DialogResult.OK) return false;
                if (p.Remember) { h.Password = p.Value; sessionPwd.Remove(h.Id); store.MarkDirty(); store.Flush(); }
                else sessionPwd[h.Id] = p.Value; // memory only, never written to disk
                return true;
            }
        }

        Panel BuildSessionBar(Host h, RemoteView v)
        {
            var bar = new Panel { Dock = DockStyle.Top, Height = Theme.S(52), BackColor = Theme.Surface };
            var btns = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Theme.Surface, Padding = new Padding(0, Theme.S(9), Theme.S(14), 0) };
            Action<string, string, BtnKind, EventHandler> add = (g, t, k, c) =>
            {
                var b = new FlatButton(g, t, k) { Margin = new Padding(Theme.S(6), 0, 0, 0) }; b.Click += c; btns.Controls.Add(b);
            };
            add(Glyph.Refresh, "Reconnect", BtnKind.Secondary, delegate
            {
                if (v.NeedAuth && !AskPassword(h)) return;
                v.NextRetryUtc = DateTime.MinValue; StartView(v); v.FocusInput();
            });
            if (!h.IsRdp) add(Glyph.Key, "Ctrl+Alt+Del", BtnKind.Secondary, delegate { v.SendCtrlAltDel(); v.FocusInput(); });
            add(Glyph.Camera, "Screenshot", BtnKind.Ghost, delegate { Screenshot(v); });
            add(Glyph.Full, "Fullscreen", BtnKind.Ghost, delegate { ToggleFullscreen(); v.FocusInput(); });
            var closeBtn = new FlatButton(Glyph.Close, "Close", BtnKind.Danger) { Margin = new Padding(Theme.S(6), 0, 0, 0) };
            closeBtn.Click += delegate { CloseTab(h.Id); };
            btns.Controls.Add(closeBtn); closeBtns[h.Id] = closeBtn;
            bar.Controls.Add(btns);
            bar.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border });

            bar.Paint += (s, e) =>
            {
                var g = e.Graphics; Theme.Smooth(g);
                h = v.Host ?? h;   // follows edits made while the session is open
                Color pc = h.IsRdp ? Theme.Rdp : Theme.Vnc;
                var chip = new Rectangle(Theme.S(20), (bar.Height - Theme.S(22)) / 2, Theme.S(40), Theme.S(22));
                Theme.FillRound(g, Color.FromArgb(40, pc), null, chip, Theme.S(5));
                TextRenderer.DrawText(g, h.Protocol, Theme.SmallBold, chip, pc, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                int x = chip.Right + Theme.S(12);
                int nw = TextRenderer.MeasureText(h.Name, Theme.UiSemi, Size.Empty, TextFormatFlags.NoPadding).Width;
                Theme.Txt(g, h.Name, Theme.UiSemi, new Rectangle(x, 0, nw + 4, bar.Height), Theme.Text);
                x += nw + Theme.S(14);
                string addr = h.Address + ":" + h.Port;
                int aw = TextRenderer.MeasureText(addr, Theme.Small, Size.Empty, TextFormatFlags.NoPadding).Width;
                Theme.Txt(g, addr, Theme.Small, new Rectangle(x, 0, aw + 4, bar.Height), Theme.Muted);
                x += aw + Theme.S(18);
                Color sc = v.State == ViewState.Connected ? Theme.Ok : v.State == ViewState.Connecting ? Theme.Warn : v.State == ViewState.Failed ? Theme.Bad : Theme.Dim;
                string st = v.State == ViewState.Connected ? "Connected" : v.State == ViewState.Connecting ? "Connecting…" : v.State == ViewState.Failed ? "Disconnected" : "Idle";
                int sw = TextRenderer.MeasureText(st, Theme.Small, Size.Empty, TextFormatFlags.NoPadding).Width;
                var pill = new Rectangle(x, (bar.Height - Theme.S(24)) / 2, sw + Theme.S(32), Theme.S(24));
                Theme.FillRound(g, Color.FromArgb(34, sc), null, pill, pill.Height / 2);
                using (var b = new SolidBrush(sc)) g.FillEllipse(b, pill.X + Theme.S(10), pill.Y + pill.Height / 2 - Theme.S(3), Theme.S(7), Theme.S(7));
                Theme.Txt(g, st, Theme.Small, new Rectangle(pill.X + Theme.S(22), pill.Y, sw + 6, pill.Height), sc);
                if (!h.IsRdp) return;
                int hx = pill.Right + Theme.S(16);
                if (hx + Theme.S(200) < btns.Left) Theme.Txt(g, "Ctrl+Alt+End = Ctrl+Alt+Del", Theme.Small, new Rectangle(hx, 0, Theme.S(200), bar.Height), Theme.Dim);
            };
            bar.Resize += delegate { bar.Invalidate(); };
            return bar;
        }

        static string SafeFileName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "screenshot";
            foreach (char ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
            return s.Trim();
        }

        void Screenshot(RemoteView v)
        {
            using (var bmp = v.Snapshot(4096))
            {
                if (bmp == null) { MessageBox.Show(this, "No screen available to capture yet."); return; }
                using (var sfd = new SaveFileDialog { Filter = "PNG image|*.png", FileName = SafeFileName(v.Host.Name) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png" })
                    if (sfd.ShowDialog(this) == DialogResult.OK)
                    {
                        try { bmp.Save(sfd.FileName, ImageFormat.Png); }
                        catch (Exception ex) { MessageBox.Show(this, "Could not save screenshot: " + ex.Message, "Remo-Console", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                    }
            }
        }

        void ToggleFullscreen()
        {
            if (!fullscreen)
            {
                if (strip.Selected == 0) return; // only for sessions
                preFs = WindowState; preBorder = FormBorderStyle;
                header.Visible = divider.Visible = strip.Visible = statusBar.Visible = false;
                FormBorderStyle = FormBorderStyle.None; WindowState = FormWindowState.Normal; WindowState = FormWindowState.Maximized;
                fullscreen = true;
            }
            else
            {
                FormBorderStyle = preBorder; WindowState = preFs;
                header.Visible = divider.Visible = statusBar.Visible = true;
                fullscreen = false;
                ApplyViewMode();
            }
        }

        void CloseTab(string id, bool deleting = false)
        {
            if (id == null) return;
            Panel page;
            if (!tabsByHost.TryGetValue(id, out page)) return;
            RemoteView v; views.TryGetValue(id, out v);
            if (v != null)
            {
                try { SaveThumb(v); } catch { }
                v.Interactive = false;
                if (v.Parent != null) v.Parent.Controls.Remove(v); // keep the view alive; page disposal must not take it down
            }
            var cur = strip.Selected < strip.Items.Count ? strip.Items[strip.Selected] : null;
            var item = strip.Items.FirstOrDefault(t => t.Id == id);
            tabsByHost.Remove(id); sessionBars.Remove(id); closeBtns.Remove(id);
            content.Controls.Remove(page);
            page.Dispose();
            if (item != null) strip.Items.Remove(item);
            strip.Selected = (cur == null || cur == item) ? 0 : Math.Max(0, strip.Items.IndexOf(cur));
            if (fullscreen) ToggleFullscreen();
            if (deleting) return;

            Host h = Hosts.FirstOrDefault(x => x.Id == id);
            if (h != null) ApplyMode(h);   // live: returns to card; static: connection is dropped
        }
    }
}
