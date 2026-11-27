using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RemoConsole
{
    /// <summary>Base for all modal dialogs: dark title bar, themed footer with Cancel / primary action.</summary>
    class DialogBase : Form
    {
        protected readonly Panel body = new Panel();
        protected readonly FlatButton ok, cancel;
        protected readonly Label note;
        protected int y;
        protected int PadX { get { return Theme.S(24); } }
        protected int InnerW { get { return ClientSize.Width - 2 * PadX; } }

        public DialogBase(string title, string subtitle, string okText, int width, int height)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Ui;
            ClientSize = new Size(Theme.S(width), Theme.S(height));
            Native.DarkTitle(this);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var footer = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(64), BackColor = Theme.Surface };
            footer.Paint += (s, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, 0, footer.Width, 0); };
            ok = new FlatButton(null, okText, BtnKind.Primary); ok.Width = Math.Max(ok.Width, Theme.S(100));
            cancel = new FlatButton(null, "Cancel", BtnKind.Secondary); cancel.Width = Math.Max(cancel.Width, Theme.S(90));
            ok.Top = cancel.Top = (footer.Height - ok.Height) / 2;
            ok.Left = footer.Width - PadX - ok.Width; cancel.Left = ok.Left - cancel.Width - Theme.S(10);
            ok.Anchor = cancel.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            note = new Label { ForeColor = Theme.Bad, Font = Theme.Ui, AutoSize = false, Left = PadX, Top = 0, Height = footer.Height, Width = cancel.Left - PadX - Theme.S(10), TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.Surface };
            footer.Controls.Add(note);
            footer.Controls.Add(ok); footer.Controls.Add(cancel);
            Controls.Add(footer);
            body.Dock = DockStyle.Fill; body.BackColor = Theme.Bg;
            Controls.Add(body);
            body.BringToFront();

            var t = new Label { Text = title, Font = Theme.Title, ForeColor = Theme.Text, AutoSize = true, Left = PadX, Top = Theme.S(20), BackColor = Theme.Bg };
            body.Controls.Add(t);
            y = Theme.S(20) + Theme.S(28);
            if (!string.IsNullOrEmpty(subtitle))
            {
                body.Controls.Add(new Label { Text = subtitle, Font = Theme.Ui, ForeColor = Theme.Muted, AutoSize = false, Left = PadX, Top = y, Width = InnerW, Height = Theme.S(36), BackColor = Theme.Bg });
                y += Theme.S(40);
            }
            else y += Theme.S(8);

            AcceptButton = null; // Field handles Enter by clicking ok; set below
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
        }

        protected void Section(string text)
        {
            y += Theme.S(6);
            body.Controls.Add(new Label { Text = text.ToUpperInvariant(), Font = Theme.SmallBold, ForeColor = Theme.Muted, AutoSize = true, Left = PadX, Top = y, BackColor = Theme.Bg });
            var line = new Panel { Left = PadX + TextRenderer.MeasureText(text.ToUpperInvariant(), Theme.SmallBold).Width + Theme.S(10), Top = y + Theme.S(8), Height = 1, BackColor = Theme.Border };
            line.Width = InnerW - (line.Left - PadX);
            body.Controls.Add(line);
            y += Theme.S(24);
        }

        /// <summary>Places labelled controls side by side across the dialog width. weights sum is arbitrary.</summary>
        protected Control[] Row(string[] labels, Control[] cs, int[] weights = null)
        {
            int gap = Theme.S(14), total = 0, n = cs.Length;
            if (weights == null) { weights = new int[n]; for (int i = 0; i < n; i++) weights[i] = 1; }
            foreach (int w in weights) total += w;
            int avail = InnerW - gap * (n - 1), x = PadX, rowH = 0;
            for (int i = 0; i < n; i++)
            {
                int w = avail * weights[i] / total;
                if (labels[i] != null)
                    body.Controls.Add(new Label { Text = labels[i], Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false, Left = x, Top = y, Width = w, Height = Theme.S(18), BackColor = Theme.Bg });
                cs[i].Left = x; cs[i].Top = y + (labels[i] != null ? Theme.S(20) : 0); cs[i].Width = Math.Max(w, 10);
                body.Controls.Add(cs[i]);
                rowH = Math.Max(rowH, (labels[i] != null ? Theme.S(20) : 0) + cs[i].Height);
                x += w + gap;
            }
            y += rowH + Theme.S(12);
            return cs;
        }
    }

    class HostDialog : DialogBase
    {
        public Host Result;
        readonly Host src;
        readonly Field name, addr, user, domain, pwd, group, notes;
        readonly NumField port, w, h;
        readonly Segmented proto, mode, depth;
        readonly Toggle nla, clip, drives, printers, audio, viewOnly, clearPwd, pinned;
        readonly Label pwdNote;
        readonly Control[] rdpOnly, vncOnly;

        public HostDialog(Host existing, IEnumerable<string> existingGroups = null) : base(existing == null ? "Add connection" : "Edit connection",
            null, "Save", 620, 738)
        {
            src = existing;
            AcceptButton = ok;

            name = new Field("e.g. Production DB server");
            proto = new Segmented("RDP", "VNC") { Tints = new[] { Theme.Rdp, Theme.Vnc } };
            addr = new Field("Hostname or IP address");
            port = new NumField(1, 65535);
            Section("Connection");
            Row(new[] { "Name" }, new Control[] { name });
            Row(new[] { "Protocol", "Address", "Port" }, new Control[] { proto, addr, port }, new[] { 3, 6, 2 });

            Section("Sign-in");
            user = new Field(); domain = new Field("optional"); pwd = new Field(); pwd.Password = true;
            clearPwd = new Toggle("Remove saved password");
            pwdNote = new Label { Font = Theme.Small, ForeColor = Theme.Ok, AutoSize = false, Height = Theme.S(18), BackColor = Theme.Bg };
            Row(new[] { "Username", "Domain", "Password" }, new Control[] { user, domain, pwd }, new[] { 3, 2, 3 });
            y -= Theme.S(6);
            pwdNote.Left = PadX; pwdNote.Top = y; pwdNote.Width = InnerW / 2; body.Controls.Add(pwdNote);
            clearPwd.Left = PadX + InnerW / 2; clearPwd.Top = y - Theme.S(4); clearPwd.Width = InnerW / 2; body.Controls.Add(clearPwd);
            y += Theme.S(26);

            Section("Organise");
            group = new Field("e.g. Production");
            if (existingGroups != null) group.SetSuggestions(existingGroups);
            mode = new Segmented("Default", "Live", "Static") { Tints = new[] { Theme.Accent, Theme.Ok, Theme.Muted } };
            Row(new[] { "Group", "Dashboard card" }, new Control[] { group, mode }, new[] { 1, 1 });
            pinned = new Toggle("Pin to top (favorites)");
            pinned.Left = PadX; pinned.Top = y - Theme.S(4); body.Controls.Add(pinned);
            y += pinned.Height + Theme.S(6);
            notes = new Field("Notes", null, true); notes.Height = Theme.S(48);
            Row(new[] { "Notes" }, new Control[] { notes });

            Section("Session");
            w = new NumField(200, 8192); h = new NumField(200, 8192);
            depth = new Segmented("32", "24", "16", "15");
            Row(new[] { "Width", "Height", "Colour depth (bits)" }, new Control[] { w, h, depth }, new[] { 2, 2, 4 });
            nla = new Toggle("Network Level Auth"); clip = new Toggle("Clipboard"); drives = new Toggle("Drives"); printers = new Toggle("Printers"); audio = new Toggle("Audio");
            int ty = y - Theme.S(4);
            Control[] ts = { nla, clip, drives, printers, audio };
            int tx = PadX;
            foreach (var t in ts) { t.Left = tx; t.Top = ty; body.Controls.Add(t); tx += t.Width + Theme.S(8); }
            rdpOnly = new Control[] { w, h, depth, nla, clip, drives, printers, audio };
            // labels for rdp-only row are siblings; track them so they hide with the row
            viewOnly = new Toggle("View only (no mouse / keyboard)");
            viewOnly.Left = PadX; viewOnly.Top = ty; body.Controls.Add(viewOnly);
            vncOnly = new Control[] { viewOnly };


            proto.Changed += delegate { ProtoChanged(true); };
            ok.Click += delegate { if (Commit()) DialogResult = DialogResult.OK; };

            Host s = existing ?? new Host();
            name.Text = s.Name; addr.Text = s.Address; port.Value = s.Port;
            proto.SelectedText = s.Protocol; user.Text = s.Username; domain.Text = s.Domain;
            group.Text = s.Group; notes.Text = s.Notes; mode.SelectedText = s.CardMode; pinned.Checked = s.Pinned;
            w.Value = s.Width; h.Value = s.Height;
            depth.SelectedText = s.ColorDepth.ToString(); if (depth.Selected < 0) depth.Selected = 0;
            nla.Checked = s.Nla; clip.Checked = s.RedirectClipboard; drives.Checked = s.RedirectDrives; printers.Checked = s.RedirectPrinters; audio.Checked = s.Audio;
            viewOnly.Checked = s.ViewOnly;
            bool has = !string.IsNullOrEmpty(s.Pwd);
            bool canDecrypt = has && !string.IsNullOrEmpty(s.Password);
            if (has && !canDecrypt)
            {
                pwdNote.Text = "⚠ Saved password could not be decrypted on this PC/user. Enter a new password.";
                pwdNote.ForeColor = Theme.Warn;
            }
            else
            {
                pwdNote.Text = has ? "✓ A password is saved. Type a new one to replace it." : "Passwords are stored encrypted (Windows DPAPI), readable only by you on this PC.";
                if (!has) pwdNote.ForeColor = Theme.Muted;
            }
            pwdNote.Width = has ? InnerW / 2 : InnerW;
            clearPwd.Visible = has;
            if (proto.Selected < 0) proto.Selected = 0;
            ProtoChanged(false);
            Shown += delegate { name.Focus(); };
        }

        void ProtoChanged(bool changedByUser)
        {
            bool rdp = proto.SelectedText == "RDP";
            if (changedByUser && (port.Value == 3389 || port.Value == 5900)) port.Value = rdp ? 3389 : 5900;
            foreach (Control c in rdpOnly) c.Visible = rdp;
            foreach (Control c in vncOnly) c.Visible = !rdp;
            user.Enabled = domain.Enabled = rdp;
            // hide / show the labels that belong to the RDP-only row
            foreach (Control c in body.Controls)
            {
                var l = c as Label;
                if (l != null && (l.Text == "Width" || l.Text == "Height" || l.Text == "Colour depth (bits)")) l.Visible = rdp;
            }
        }

        bool Commit()
        {
            note.Text = "";
            string trimmedAddr = (addr.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(trimmedAddr)) { note.Text = "⚠ Address is required."; addr.Focus(); return false; }
            foreach (char ch in trimmedAddr)
            {
                if (char.IsWhiteSpace(ch) || char.IsControl(ch))
                {
                    note.Text = "⚠ Address cannot contain spaces or control characters.";
                    addr.Focus();
                    return false;
                }
            }
            var r = src != null ? src.Clone() : new Host();
            r.Address = trimmedAddr;
            r.Name = string.IsNullOrWhiteSpace(name.Text) ? r.Address : name.Text.Trim();
            r.Protocol = proto.SelectedText; r.Port = port.Value;
            r.Username = user.Text.Trim(); r.Domain = domain.Text.Trim();
            r.Group = group.Text.Trim(); r.Notes = notes.Text; r.Pinned = pinned.Checked; r.CardMode = mode.SelectedText == "" ? "Default" : mode.SelectedText;
            r.Width = w.Value; r.Height = h.Value;
            int d; r.ColorDepth = int.TryParse(depth.SelectedText, out d) ? d : 32;
            r.Nla = nla.Checked; r.RedirectClipboard = clip.Checked; r.RedirectDrives = drives.Checked;
            r.RedirectPrinters = printers.Checked; r.Audio = audio.Checked; r.ViewOnly = viewOnly.Checked;
            if (pwd.Text.Length > 0) r.Password = pwd.Text;
            else if (clearPwd.Visible && clearPwd.Checked) r.Pwd = "";
            Result = r;
            return true;
        }
    }

    class PasswordPrompt : DialogBase
    {
        readonly Field box;
        readonly Toggle remember;
        public string Value { get { return box.Text; } }
        public bool Remember { get { return remember.Checked; } }

        public PasswordPrompt(string title, string message) : base(title, message, "Connect", 440, 270)
        {
            AcceptButton = ok;
            box = new Field("Password", Glyph.Lock); box.Password = true;
            Row(new string[] { null }, new Control[] { box });
            remember = new Toggle("Remember (encrypted)") { Checked = true };
            Row(new string[] { null }, new Control[] { remember });
            ok.Click += delegate { DialogResult = DialogResult.OK; };
            Shown += delegate { box.Focus(); };
        }
    }

    class InputDialog : DialogBase
    {
        readonly Field box;
        public string Value { get { return (box.Text ?? "").Trim(); } }

        public InputDialog(string title, string subtitle, string placeholder, string initialValue, string okText)
            : base(title, subtitle, okText, 440, 240)
        {
            AcceptButton = ok;
            box = new Field(placeholder);
            box.Text = initialValue ?? "";
            Row(new string[] { null }, new Control[] { box });
            ok.Click += delegate
            {
                note.Text = "";
                if (string.IsNullOrWhiteSpace(box.Text)) { note.Text = "⚠ Value is required."; box.Focus(); return; }
                DialogResult = DialogResult.OK;
            };
            Shown += delegate { box.Focus(); box.SelectAllText(); };
        }
    }
}
