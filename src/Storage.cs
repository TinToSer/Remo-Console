using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace RemoConsole
{
    /// <summary>
    /// Crash-safe file storage. Every write:
    ///  1. serialises to "RCSTORE1 &lt;sha256&gt;\n&lt;body&gt;"
    ///  2. writes to a temp file with WriteThrough and flushes to disk
    ///  3. read-back verifies the temp file
    ///  4. atomically swaps it in with File.Replace (previous good copy becomes .bak)
    ///  5. keeps timestamped rolling snapshots in backups\
    /// Reads verify the checksum and fall back .bak -> snapshots. A corrupt file is
    /// never deleted: it is quarantined as *.corrupt-&lt;time&gt;.
    /// </summary>
    static class AtomicFile
    {
        const string Magic = "RCSTORE1 ";
        const int KeepSnapshots = 30;
        static readonly TimeSpan SnapshotEvery = TimeSpan.FromMinutes(10);

        static string Hash(byte[] body)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(body)).Replace("-", "").ToLowerInvariant();
        }

        static byte[] Wrap(byte[] body)
        {
            byte[] head = Encoding.ASCII.GetBytes(Magic + Hash(body) + "\n");
            byte[] all = new byte[head.Length + body.Length];
            Buffer.BlockCopy(head, 0, all, 0, head.Length);
            Buffer.BlockCopy(body, 0, all, head.Length, body.Length);
            return all;
        }

        /// <summary>Returns body if file is intact, else null.</summary>
        public static byte[] TryVerify(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                byte[] all = File.ReadAllBytes(path);
                int nl = Array.IndexOf(all, (byte)'\n');
                if (nl < 0 || nl > 100) return null;
                string head = Encoding.ASCII.GetString(all, 0, nl);
                if (!head.StartsWith(Magic, StringComparison.Ordinal)) return null;
                string want = head.Substring(Magic.Length);
                byte[] body = new byte[all.Length - nl - 1];
                Buffer.BlockCopy(all, nl + 1, body, 0, body.Length);
                return Hash(body) == want ? body : null;
            }
            catch { return null; }
        }

        public static string BackupDir(string path)
        {
            return Path.Combine(Path.GetDirectoryName(path), "backups");
        }

        public static void Write(string path, byte[] body)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            byte[] all = Wrap(body);

            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 8192, FileOptions.WriteThrough))
            {
                fs.Write(all, 0, all.Length);
                fs.Flush(true); // force to physical disk
            }
            byte[] check = TryVerify(tmp);
            if (check == null || check.Length != body.Length)
            {
                try { File.Delete(tmp); } catch { }
                throw new IOException("Write verification failed; existing data left untouched.");
            }

            if (File.Exists(path))
            {
                // Only promote the current file to .bak if it is itself intact.
                if (TryVerify(path) != null)
                    File.Replace(tmp, path, path + ".bak", true);
                else
                    File.Replace(tmp, path, null, true);
            }
            else
            {
                File.Move(tmp, path);
            }
            Snapshot(path);
        }

        static void Snapshot(string path)
        {
            try
            {
                string dir = BackupDir(path);
                Directory.CreateDirectory(dir);
                string name = Path.GetFileName(path);
                var existing = new DirectoryInfo(dir).GetFiles(name + ".*.bak").OrderByDescending(f => f.Name).ToList();
                if (existing.Count == 0 || DateTime.UtcNow - existing[0].LastWriteTimeUtc > SnapshotEvery)
                {
                    string snap = Path.Combine(dir, name + "." + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".bak");
                    File.Copy(path, snap, true);
                    existing.Insert(0, new FileInfo(snap));
                }
                foreach (var old in existing.Skip(KeepSnapshots))
                    try { old.Delete(); } catch { }
            }
            catch { /* backups are best-effort; never fail the save */ }
        }

        /// <summary>Loads newest intact copy. 'source' says where it came from. Null if nothing usable.</summary>
        public static byte[] Read(string path, out string source)
        {
            source = null;
            if (!File.Exists(path) && !File.Exists(path + ".bak") && !Directory.Exists(BackupDir(path))) return null;

            var candidates = new List<string>();
            candidates.Add(path);
            candidates.Add(path + ".bak");
            try
            {
                string dir = BackupDir(path);
                if (Directory.Exists(dir))
                    candidates.AddRange(new DirectoryInfo(dir).GetFiles(Path.GetFileName(path) + ".*.bak")
                        .OrderByDescending(f => f.Name).Select(f => f.FullName));
            }
            catch { }

            for (int i = 0; i < candidates.Count; i++)
            {
                byte[] body = TryVerify(candidates[i]);
                if (body == null) continue;
                source = candidates[i];
                if (i > 0 && File.Exists(path))
                    Quarantine(path);
                return body;
            }
            if (File.Exists(path)) Quarantine(path); // exists but nothing verifies anywhere
            return null;
        }

        static void Quarantine(string path)
        {
            try
            {
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                string dest = path + ".corrupt-" + stamp;
                for (int n = 1; File.Exists(dest); n++) dest = path + ".corrupt-" + stamp + "-" + n;
                File.Move(path, dest);
                Log.Write("Quarantined unreadable data file " + path);
            }
            catch { }
        }
    }

    /// <summary>Windows DPAPI (current user) for passwords at rest.</summary>
    static class Secret
    {
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RemoConsole.v1");

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            byte[] enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(enc);
        }

        public static string Unprotect(string blob)
        {
            if (string.IsNullOrEmpty(blob)) return "";
            try
            {
                byte[] dec = ProtectedData.Unprotect(Convert.FromBase64String(blob), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(dec);
            }
            catch { return ""; }
        }
    }

    static class Log
    {
        static readonly object L = new object();
        public static string Path_;
        public static void Write(string msg)
        {
            try
            {
                lock (L)
                {
                    if (Path_ == null) return;
                    var fi = new FileInfo(Path_);
                    if (fi.Exists && fi.Length > 1024 * 1024) { try { File.Copy(Path_, Path_ + ".old", true); File.Delete(Path_); } catch { } }
                    File.AppendAllText(Path_, DateTime.Now.ToString("s") + " " + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }

    /// <summary>The persisted database: hosts + settings. Debounced, thread-safe saving.</summary>
    class Store
    {
        public readonly string Dir;
        public readonly string DataFile;
        public readonly string ThumbDir;
        public Db Data = new Db();
        public string LoadNote; // non-null if recovery happened
        public DateTime LastSaved = DateTime.MinValue;
        public bool SaveFailed;
        public bool IsDirty { get { lock (saveLock) return dirty; } }
        bool dirty;
        readonly object saveLock = new object();
        static JavaScriptSerializer Ser()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
        }

        public Store(string dir)
        {
            Dir = dir;
            DataFile = Path.Combine(dir, "data.rc");
            ThumbDir = Path.Combine(dir, "thumbs");
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(ThumbDir);
            Log.Path_ = Path.Combine(dir, "remo-console.log");
        }

        public void Load()
        {
            bool hadExisting = File.Exists(DataFile) || File.Exists(DataFile + ".bak") || Directory.Exists(AtomicFile.BackupDir(DataFile));
            string src;
            byte[] body = AtomicFile.Read(DataFile, out src);
            if (body == null)
            {
                if (hadExisting)
                    LoadNote = "Your data file was unreadable and no valid backup was found. The damaged file was preserved next to it (*.corrupt-*). Starting empty.";
                Data = new Db();
                return;
            }
            try
            {
                Data = Ser().Deserialize<Db>(Encoding.UTF8.GetString(body)) ?? new Db();
            }
            catch (Exception ex)
            {
                Log.Write("Deserialize failed: " + ex);
                Data = new Db();
                LoadNote = "Data could not be parsed (" + ex.Message + "). Starting empty; backups are in the backups folder.";
            }
            if (Data.Hosts == null) Data.Hosts = new List<Host>();
            if (Data.Settings == null) Data.Settings = new Settings();
            SanitizeLoaded();
            if (src != DataFile && LoadNote == null)
                LoadNote = "Main data file was damaged; restored from " + Path.GetFileName(src) + ".";
        }

        void SanitizeLoaded()
        {
            Data.Settings.Sanitize();
            var seen = new HashSet<string>();
            Data.Hosts.RemoveAll(h => h == null);
            foreach (var h in Data.Hosts)
            {
                h.Sanitize();
                if (!seen.Add(h.Id)) h.Id = Guid.NewGuid().ToString("N");
            }
        }

        public void MarkDirty() { lock (saveLock) dirty = true; }

        /// <summary>Writes if dirty. Returns false if the write failed (data stays dirty and is retried).</summary>
        public bool Flush(bool force = false)
        {
            lock (saveLock)
            {
                if (!dirty && !force) return true;
                try
                {
                    byte[] body = Encoding.UTF8.GetBytes(Ser().Serialize(Data));
                    AtomicFile.Write(DataFile, body);
                    dirty = false;
                    SaveFailed = false; LastSaved = DateTime.Now;
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Write("Save failed: " + ex);
                    SaveFailed = true;
                    return false;
                }
            }
        }

        public string ThumbPath(string hostId)
        {
            var sb = new StringBuilder(32);
            foreach (char ch in (hostId ?? ""))
                if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_')
                    sb.Append(ch);
            string safe = sb.Length > 0 ? sb.ToString() : "unknown";
            return Path.Combine(ThumbDir, safe + ".png");
        }
    }
}
