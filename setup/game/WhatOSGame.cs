using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

static class G
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WhatOS");
    static string F(string n) => Path.Combine(Dir, n);
    const string High = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    static readonly JavaScriptSerializer J = new JavaScriptSerializer();
    static readonly Regex Helper = new Regex(@"crash|report|launcher|setup|install|redist|unins|update|patch|helper|anticheat|beservice|battleye|webhelper|cefprocess|dxsetup|prereq|touchup|^(leagueclient|riotclient)|server\.exe$", RegexOptions.IgnoreCase);
    static readonly string[] Bg = { "discord", "steam", "steamwebhelper", "epicgameslauncher", "epicwebhelper", "chrome", "msedge", "msedgewebview2", "firefox", "brave", "opera", "opera_gx", "vivaldi", "spotify", "onedrive", "teams", "ms-teams", "battle.net", "eadesktop", "eabackgroundservice", "upc", "ubisoftconnect", "galaxyclient", "overwolf", "obs64", "whatsapp", "telegram", "slack", "zoom", "searchhost", "phoneexperiencehost", "widgets", "msedgeupdate", "googleupdate" };
    static readonly string[] Events = { "On", "Off", "Toggle", "Reload" };

    static Dictionary<string, object> cfg = new Dictionary<string, object>();
    static List<Dictionary<string, object>> games = new List<Dictionary<string, object>>();
    static readonly object L = new object();
    static readonly HashSet<int> gamePids = new HashSet<int>();
    static readonly Dictionary<int, ProcessPriorityClass> lowered = new Dictionary<int, ProcessPriorityClass>();
    static bool boosted, manual, lowerApps, paused;
    static string gameName, prevPlan;
    static DateTime since, lastExit;

    [STAThread]
    static int Main(string[] a)
    {
        Directory.CreateDirectory(Dir);
        var c = a.Length > 0 ? a[0].ToLowerInvariant() : "";
        if (c == "--scan") { Scan(); return 0; }
        foreach (var e in Events) if (c == "--" + e.ToLowerInvariant()) return Signal(e) ? 0 : 1;
        using (var m = new Mutex(true, @"Global\WhatOSGameWatcher", out var first)) { if (first) Run(); }
        return 0;
    }

    static bool Signal(string e)
    {
        try { using (var h = EventWaitHandle.OpenExisting(@"Global\WhatOSGame" + e)) h.Set(); return true; } catch { return false; }
    }

    static void Log(string s)
    {
        try { var f = F("gm.log"); if (File.Exists(f) && new FileInfo(f).Length > 200000) File.Delete(f); File.AppendAllText(f, DateTime.Now.ToString("MM-dd HH:mm:ss ") + s + "\r\n"); } catch { }
    }

    static T Get<T>(Dictionary<string, object> d, string k, T def) => d != null && d.TryGetValue(k, out var v) && v is T t ? t : def;

    static void Load()
    {
        try { cfg = File.Exists(F("gm.json")) ? J.Deserialize<Dictionary<string, object>>(File.ReadAllText(F("gm.json"))) : new Dictionary<string, object>(); } catch { cfg = new Dictionary<string, object>(); }
        if (!File.Exists(F("gm-games.json")) || File.GetLastWriteTime(F("gm-games.json")) < DateTime.Now.AddHours(-12)) Scan();
        try { games = J.Deserialize<List<Dictionary<string, object>>>(File.ReadAllText(F("gm-games.json"))); } catch { games = new List<Dictionary<string, object>>(); }
    }

    static void Run()
    {
        var sec = new EventWaitHandleSecurity();
        sec.AddAccessRule(new EventWaitHandleAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize, AccessControlType.Allow));
        sec.AddAccessRule(new EventWaitHandleAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), EventWaitHandleRights.FullControl, AccessControlType.Allow));
        var ev = Events.Select(e => new EventWaitHandle(false, EventResetMode.AutoReset, @"Global\WhatOSGame" + e, out _, sec)).ToArray();
        try
        {
            var old = J.Deserialize<Dictionary<string, object>>(File.ReadAllText(F("gm-state.json")));
            var pp = Get<string>(old, "prevPlan", null);
            if (Get(old, "on", false) && pp != null) PowerCfg("/setactive " + pp);
        }
        catch { }
        Load();
        Save();
        ManagementEventWatcher ws = null, wx = null;
        try
        {
            ws = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
            ws.EventArrived += (s, e) => Started(Convert.ToInt32(e.NewEvent["ProcessID"]));
            ws.Start();
            wx = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace"));
            wx.EventArrived += (s, e) => Stopped(Convert.ToInt32(e.NewEvent["ProcessID"]));
            wx.Start();
        }
        catch (Exception ex) { Log("trace failed, polling: " + ex.Message); ws = null; }
        foreach (var p in Process.GetProcesses()) Started(p.Id);
        var known = new HashSet<int>();
        var tick = 0;
        while (true)
        {
            var i = WaitHandle.WaitAny(ev, 5000);
            lock (L)
            {
                if (i == 0 || (i == 2 && !boosted)) { manual = true; paused = false; Boost(gameName ?? "Manual"); }
                else if (i == 1 || i == 2) { manual = false; paused = gamePids.Count > 0; Unboost(); }
                else if (i == 3) { Load(); Save(); }
                else
                {
                    if (gamePids.RemoveWhere(pid => !Alive(pid)) > 0) lastExit = DateTime.Now;
                    if (paused && gamePids.Count == 0) { paused = false; Save(); }
                    if (boosted && !manual && gamePids.Count == 0 && DateTime.Now - lastExit > TimeSpan.FromSeconds(10)) Unboost();
                    if (boosted && lowerApps && ++tick % 6 == 0) LowerApps();
                }
            }
            if (ws == null && i == WaitHandle.WaitTimeout)
            {
                var now = new HashSet<int>(Process.GetProcesses().Select(p => p.Id));
                foreach (var pid in now) if (!known.Contains(pid)) Started(pid);
                known = now;
            }
        }
    }

    static bool Alive(int pid) { try { using (var p = Process.GetProcessById(pid)) return !p.HasExited; } catch { return false; } }

    static void Started(int pid)
    {
        if (!Get(cfg, "auto", true)) return;
        var path = ImagePath(pid);
        if (path == null) return;
        var g = games.FirstOrDefault(x => Get(x, "exe", "") is string ex && ex.Length > 0 ? string.Equals(ex, path, StringComparison.OrdinalIgnoreCase) : path.StartsWith(Get(x, "dir", "\0") + "\\", StringComparison.OrdinalIgnoreCase) && !Helper.IsMatch(Path.GetFileName(path)));
        if (g == null) return;
        var o = Over(g);
        if (Get(o, "off", false)) return;
        lock (L)
        {
            gamePids.Add(pid);
            if (paused) return;
            var pr = Get(o, "priority", Get(cfg, "priority", "High"));
            try { using (var p = Process.GetProcessById(pid)) p.PriorityClass = pr == "Normal" ? ProcessPriorityClass.Normal : pr == "AboveNormal" ? ProcessPriorityClass.AboveNormal : ProcessPriorityClass.High; } catch { }
            lowerApps = Get(o, "lowerApps", Get(cfg, "lowerApps", true));
            if (!boosted) Log("game " + Get(g, "name", "") + " " + path);
            Boost(Get(g, "name", "Game"));
        }
    }

    static Dictionary<string, object> Over(Dictionary<string, object> g)
    {
        var all = Get<Dictionary<string, object>>(cfg, "games", null);
        return all != null && all.TryGetValue(Get(g, "id", ""), out var v) ? v as Dictionary<string, object> : null;
    }

    static void Stopped(int pid)
    {
        lock (L) { if (gamePids.Remove(pid)) lastExit = DateTime.Now; lowered.Remove(pid); }
    }

    static void Boost(string name)
    {
        gameName = name;
        if (!boosted)
        {
            boosted = true; since = DateTime.Now;
            if (manual) lowerApps = Get(cfg, "lowerApps", true);
            if (Get(cfg, "power", true)) SetPlan();
            if (Get(cfg, "clearRam", true)) ClearStandby();
        }
        if (lowerApps) LowerApps();
        Save();
    }

    static void Unboost()
    {
        if (!boosted) return;
        foreach (var kv in lowered) try { using (var p = Process.GetProcessById(kv.Key)) p.PriorityClass = kv.Value; } catch { }
        lowered.Clear();
        foreach (var id in gamePids) try { using (var p = Process.GetProcessById(id)) p.PriorityClass = ProcessPriorityClass.Normal; } catch { }
        if (prevPlan != null) PowerCfg("/setactive " + prevPlan);
        Log("off after " + (int)(DateTime.Now - since).TotalMinutes + " min");
        boosted = false; prevPlan = null; gameName = null;
        Save();
    }

    static void LowerApps()
    {
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (lowered.ContainsKey(p.Id) || gamePids.Contains(p.Id) || !Bg.Contains(p.ProcessName.ToLowerInvariant())) continue;
                var pc = p.PriorityClass;
                if (pc == ProcessPriorityClass.Idle || pc == ProcessPriorityClass.BelowNormal) continue;
                p.PriorityClass = ProcessPriorityClass.BelowNormal;
                lowered[p.Id] = pc;
            }
            catch { }
            finally { p.Dispose(); }
        }
    }

    static void SetPlan()
    {
        var cur = Regex.Match(PowerCfg("/getactivescheme"), "[0-9a-f]{8}-[0-9a-f-]{27}").Value;
        if (cur == High) return;
        if (!PowerCfg("/list").Contains(High)) PowerCfg("/duplicatescheme " + High + " " + High);
        prevPlan = cur.Length > 0 ? cur : null;
        PowerCfg("/setactive " + High);
    }

    static string PowerCfg(string args)
    {
        try
        {
            using (var p = Process.Start(new ProcessStartInfo("powercfg.exe", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }))
            { var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(); return o; }
        }
        catch { return ""; }
    }

    static void Save()
    {
        try
        {
            File.WriteAllText(F("gm-state.json"), J.Serialize(new Dictionary<string, object>
            {
                ["on"] = boosted, ["manual"] = manual, ["paused"] = paused, ["game"] = gameName, ["since"] = boosted ? since.ToString("o") : null,
                ["prevPlan"] = prevPlan, ["lowered"] = lowered.Count, ["pid"] = Process.GetCurrentProcess().Id
            }));
        }
        catch { }
    }

    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(int a, bool i, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int f, StringBuilder s, ref int n);
    static string ImagePath(int pid)
    {
        var h = OpenProcess(0x1000, false, pid);
        if (h == IntPtr.Zero) return null;
        try { var sb = new StringBuilder(1024); int n = sb.Capacity; return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : null; }
        finally { CloseHandle(h); }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)] struct TP { public int Count; public long Luid; public int Attr; }
    [DllImport("advapi32.dll")] static extern bool OpenProcessToken(IntPtr p, int a, out IntPtr t);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern bool LookupPrivilegeValue(string s, string n, out long l);
    [DllImport("advapi32.dll")] static extern bool AdjustTokenPrivileges(IntPtr t, bool d, ref TP n, int l, IntPtr p, IntPtr r);
    [DllImport("ntdll.dll")] static extern int NtSetSystemInformation(int c, ref int i, int l);
    static void ClearStandby()
    {
        try
        {
            OpenProcessToken(Process.GetCurrentProcess().Handle, 0x28, out var t);
            LookupPrivilegeValue(null, "SeProfileSingleProcessPrivilege", out var luid);
            var tp = new TP { Count = 1, Luid = luid, Attr = 2 };
            AdjustTokenPrivileges(t, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            CloseHandle(t);
            int cmd = 4;
            var r = NtSetSystemInformation(80, ref cmd, 4);
            if (r != 0) Log("standby clear " + r.ToString("X"));
        }
        catch { }
    }

    static void Scan()
    {
        var list = new List<Dictionary<string, object>>();
        void Add(string name, string dir, string src, string exe = null)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            try { dir = Path.GetFullPath(dir.Replace('/', '\\')).TrimEnd('\\'); } catch { return; }
            if (dir.Length <= 3 || !Directory.Exists(dir) || list.Any(x => string.Equals((string)x["dir"], dir, StringComparison.OrdinalIgnoreCase))) return;
            list.Add(new Dictionary<string, object> { ["id"] = (src + ":" + Path.GetFileName(dir)).ToLowerInvariant(), ["name"] = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(dir) : name.Trim(), ["dir"] = dir, ["src"] = src, ["exe"] = exe ?? "" });
        }
        try
        {
            var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
            if (steam != null)
            {
                var libs = new List<string> { steam };
                var vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
                if (File.Exists(vdf)) foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"(.+?)\"")) libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
                foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var sa = Path.Combine(lib, "steamapps");
                    if (!Directory.Exists(sa)) continue;
                    foreach (var acf in Directory.GetFiles(sa, "appmanifest_*.acf"))
                    {
                        var t = File.ReadAllText(acf);
                        var name = Regex.Match(t, "\"name\"\\s+\"(.+?)\"").Groups[1].Value;
                        var idir = Regex.Match(t, "\"installdir\"\\s+\"(.+?)\"").Groups[1].Value;
                        if (idir.Length == 0 || Regex.IsMatch(name, "Redistributable|Proton|Steam Linux Runtime|Steamworks", RegexOptions.IgnoreCase)) continue;
                        Add(name, Path.Combine(sa, "common", idir), "steam");
                    }
                }
            }
        }
        catch (Exception ex) { Log("steam scan: " + ex.Message); }
        try
        {
            var man = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
            if (Directory.Exists(man)) foreach (var f in Directory.GetFiles(man, "*.item"))
                {
                    var d = J.Deserialize<Dictionary<string, object>>(File.ReadAllText(f));
                    Add(Get(d, "DisplayName", ""), Get(d, "InstallLocation", ""), "epic");
                }
        }
        catch (Exception ex) { Log("epic scan: " + ex.Message); }
        try
        {
            var meta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Riot Games\Metadata");
            if (Directory.Exists(meta)) foreach (var f in Directory.GetFiles(meta, "*.product_settings.yaml", SearchOption.AllDirectories))
                {
                    var p = Regex.Match(File.ReadAllText(f), "product_install_full_path:\\s*\"?(.+?)\"?\\s*$", RegexOptions.Multiline).Groups[1].Value.Replace('/', '\\').TrimEnd('\\');
                    if (p.Length == 0) continue;
                    var n = Path.GetFileName(p);
                    Add(n.Equals("live", StringComparison.OrdinalIgnoreCase) || n.Equals("pbe", StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(Path.GetDirectoryName(p)) : n, p, "riot");
                }
        }
        catch (Exception ex) { Log("riot scan: " + ex.Message); }
        try
        {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games"))
                if (k != null) foreach (var s in k.GetSubKeyNames()) using (var g = k.OpenSubKey(s)) Add(g?.GetValue("gameName") as string, g?.GetValue("path") as string, "gog");
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs"))
                if (k != null) foreach (var s in k.GetSubKeyNames()) using (var g = k.OpenSubKey(s)) { var d = g?.GetValue("InstallDir") as string; Add(d == null ? null : Path.GetFileName(d.TrimEnd('/', '\\')), d, "ubisoft"); }
        }
        catch (Exception ex) { Log("gog/ubi scan: " + ex.Message); }
        try
        {
            var pubs = new Regex("^(Blizzard Entertainment|Electronic Arts|Rockstar Games|Activision|Bethesda Softworks|Riot Games)", RegexOptions.IgnoreCase);
            var skip = new Regex("Battle\\.net|EA app|Launcher|Ubisoft Connect|Riot Client|Vanguard|Social Club", RegexOptions.IgnoreCase);
            foreach (var root in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
                using (var k = Registry.LocalMachine.OpenSubKey(root))
                    if (k != null) foreach (var s in k.GetSubKeyNames()) using (var g = k.OpenSubKey(s))
                            {
                                var pub = g?.GetValue("Publisher") as string; var n = g?.GetValue("DisplayName") as string;
                                if (pub == null || n == null || !pubs.IsMatch(pub) || skip.IsMatch(n)) continue;
                                Add(n, g.GetValue("InstallLocation") as string, "store");
                            }
        }
        catch (Exception ex) { Log("uninstall scan: " + ex.Message); }
        try
        {
            foreach (var d in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Fixed && x.IsReady))
            {
                var xg = Path.Combine(d.RootDirectory.FullName, "XboxGames");
                if (Directory.Exists(xg)) foreach (var g in Directory.GetDirectories(xg)) if (!Path.GetFileName(g).Equals("GameSave", StringComparison.OrdinalIgnoreCase)) Add(Path.GetFileName(g), g, "xbox");
            }
        }
        catch (Exception ex) { Log("xbox scan: " + ex.Message); }
        try
        {
            var c = File.Exists(F("gm.json")) ? J.Deserialize<Dictionary<string, object>>(File.ReadAllText(F("gm.json"))) : null;
            if (c != null && c.TryGetValue("extra", out var ex) && ex is System.Collections.IEnumerable arr)
                foreach (Dictionary<string, object> x in arr) { var exe = Get(x, "exe", ""); if (File.Exists(exe)) Add(Get(x, "name", Path.GetFileNameWithoutExtension(exe)), Path.GetDirectoryName(exe), "manual", exe); }
        }
        catch (Exception ex) { Log("manual list: " + ex.Message); }
        list.Sort((a, b) => string.Compare((string)a["name"], (string)b["name"], StringComparison.OrdinalIgnoreCase));
        File.WriteAllText(F("gm-games.json"), J.Serialize(list));
    }
}
