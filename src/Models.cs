using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace RemoConsole
{
    class Host
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "";
        public string Protocol = "RDP";      // "RDP" | "VNC"
        public string Address = "";
        public int Port = 3389;
        public string Username = "";
        public string Domain = "";
        public string Pwd = "";               // DPAPI-protected, base64
        public string Group = "";
        public string Notes = "";
        public bool Pinned = false;
        public string CardMode = "Default";   // Default | Live | Static
        public long LastConnectedUtc;

        // RDP
        public int Width = 1280;
        public int Height = 720;
        public int ColorDepth = 32;
        public bool Nla = true;
        public bool RedirectClipboard = true;
        public bool RedirectDrives = false;
        public bool RedirectPrinters = false;
        public bool Audio = true;

        // VNC
        public bool ViewOnly = false;

        [ScriptIgnore]
        public string Password
        {
            get { return Secret.Unprotect(Pwd); }
            set { Pwd = Secret.Protect(value); }
        }

        [ScriptIgnore]
        public bool IsRdp { get { return Protocol == "RDP"; } }

        public Host Clone()
        {
            var h = (Host)MemberwiseClone();
            return h;
        }

        public void Sanitize()
        {
            if (string.IsNullOrWhiteSpace(Id)) Id = Guid.NewGuid().ToString("N");
            Address = (Address ?? "").Trim();
            Name = string.IsNullOrWhiteSpace(Name) ? Address : Name.Trim();
            if (Protocol != "RDP" && Protocol != "VNC") Protocol = "RDP";
            if (Port < 1 || Port > 65535) Port = IsRdp ? 3389 : 5900;
            Username = (Username ?? "").Trim();
            Domain = (Domain ?? "").Trim();
            Pwd = Pwd ?? "";
            Group = (Group ?? "").Trim();
            Notes = Notes ?? "";
            if (CardMode != "Live" && CardMode != "Static") CardMode = "Default";
            Width = Math.Max(200, Math.Min(8192, Width));
            Height = Math.Max(200, Math.Min(8192, Height));
            if (ColorDepth != 15 && ColorDepth != 16 && ColorDepth != 24 && ColorDepth != 32) ColorDepth = 32;
        }

        public override string ToString() { return Name; }
    }

    class Settings
    {
        public string DefaultCardMode = "Static"; // Live | Static
        public int CardWidth = 320;
        public string ViewMode = "Grid";          // Grid | Tabs
        public string GroupFilter = "";           // "" (all) | "__PINNED__" | "__UNGROUPED__" | <group name>
        public string ProtocolFilter = "";        // "" (all) | "RDP" | "VNC"
        public string SortBy = "Name";            // Name | Recent | Protocol | Status
        public List<string> CollapsedGroups = new List<string>();
        public int WinX = -1, WinY = -1, WinW = 1400, WinH = 850;
        public bool Maximized = false;
        public int RetrySeconds = 20;
        public bool ProbeStatic = true;           // TCP reachability check for static cards

        public void Sanitize()
        {
            if (DefaultCardMode != "Live" && DefaultCardMode != "Static") DefaultCardMode = "Static";
            CardWidth = Math.Max(240, Math.Min(760, CardWidth));
            if (ViewMode != "Grid" && ViewMode != "Tabs") ViewMode = "Grid";
            GroupFilter = GroupFilter ?? "";
            if (ProtocolFilter != "RDP" && ProtocolFilter != "VNC") ProtocolFilter = "";
            if (SortBy != "Name" && SortBy != "Recent" && SortBy != "Protocol" && SortBy != "Status") SortBy = "Name";
            if (CollapsedGroups == null) CollapsedGroups = new List<string>();
            else CollapsedGroups.RemoveAll(g => g == null);
            if (WinW < 400) WinW = 1400;
            if (WinH < 300) WinH = 850;
            RetrySeconds = Math.Max(5, Math.Min(3600, RetrySeconds));
        }
    }

    class Db
    {
        public int Version = 1;
        public List<Host> Hosts = new List<Host>();
        public Settings Settings = new Settings();
    }
}
