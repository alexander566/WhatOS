using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace WhatOS
{
    static class Program
    {
        public static readonly string Work = Path.Combine(Path.GetTempPath(), "WhatOS");

        public static int StartTab;

        public static string[] Args = new string[0];
        public static Version Ver => Assembly.GetExecutingAssembly().GetName().Version;
        public static string VerText => Ver.Major == 0 && Ver.Minor == 1 && Ver.Build == 0 ? "dev" : "v" + Ver.Major + "." + Ver.Minor + "." + Ver.Build;

        public const string InstDir = @"C:\Program Files\WhatOS";
        static string InstExe => Path.Combine(InstDir, "WhatOS.exe");

        static bool Install()
        {
            if (VerText == "dev" || Args.Contains("--portable")) return false;
            var self = Application.ExecutablePath;
            if (string.Equals(Path.GetFullPath(self), InstExe, StringComparison.OrdinalIgnoreCase)) { Register(false); return false; }
            try
            {
                Directory.CreateDirectory(InstDir);
                Version have = null;
                try { if (File.Exists(InstExe)) have = new Version(FileVersionInfo.GetVersionInfo(InstExe).FileVersion); } catch { }
                if (have == null || have < new Version(Ver.Major, Ver.Minor, Ver.Build, 0))
                {
                    foreach (var p in Process.GetProcessesByName("WhatOS")) try { if (p.Id != Process.GetCurrentProcess().Id && string.Equals(p.MainModule.FileName, InstExe, StringComparison.OrdinalIgnoreCase)) { p.Kill(); p.WaitForExit(5000); } } catch { }
                    File.Copy(self, InstExe, true);
                    Register(true);
                }
                Process.Start(new ProcessStartInfo(InstExe, string.Join(" ", Args.Select(x => "\"" + x + "\""))) { UseShellExecute = false });
                return true;
            }
            catch { return false; }
        }

        static void Register(bool fresh)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WhatOS"))
                {
                    k.SetValue("DisplayName", "WhatOS");
                    k.SetValue("Publisher", "WhatOS");
                    k.SetValue("DisplayVersion", VerText.TrimStart('v'));
                    k.SetValue("DisplayIcon", InstExe);
                    k.SetValue("InstallLocation", InstDir);
                    k.SetValue("UninstallString", "\"" + InstExe + "\" --uninstall");
                    k.SetValue("URLInfoAbout", "https://github.com/alexander566/WhatOS");
                    k.SetValue("EstimatedSize", (int)(new FileInfo(InstExe).Length / 1024), Microsoft.Win32.RegistryValueKind.DWord);
                    k.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);
                }
                var start = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "WhatOS.lnk");
                if (fresh || !File.Exists(start)) Shortcut(start);
                if (fresh) Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "WhatOS.lnk"));
            }
            catch { }
        }

        static void Shortcut(string path)
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            var sh = Activator.CreateInstance(t);
            var lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { path });
            var lt = lnk.GetType();
            lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { InstExe });
            lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { InstDir });
            lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "WhatOS Tweaker" });
            lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
        }

        public static bool ApplyPending()
        {
            var exe = Application.ExecutablePath;
            try { File.Delete(exe + ".old"); } catch { }
            var n = exe + ".new";
            try
            {
                if (!File.Exists(n)) return false;
                if (new FileInfo(n).Length < 500000) { File.Delete(n); return false; }
                File.Move(exe, exe + ".old");
                File.Move(n, exe);
                Process.Start(new ProcessStartInfo(exe, string.Join(" ", Args.Select(x => "\"" + x + "\"")) + " --after " + Process.GetCurrentProcess().Id) { UseShellExecute = false });
                return true;
            }
            catch { try { if (!File.Exists(exe) && File.Exists(exe + ".old")) File.Move(exe + ".old", exe); } catch { } return false; }
        }

        public static string FetchUpdate()
        {
            var exe = Application.ExecutablePath;
            if (VerText == "dev") return null;
            try
            {
                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                var rq = (System.Net.HttpWebRequest)System.Net.WebRequest.Create("https://api.github.com/repos/alexander566/WhatOS/releases/latest");
                rq.UserAgent = "WhatOS"; rq.Timeout = 10000;
                string body;
                using (var rs = rq.GetResponse()) using (var sr = new StreamReader(rs.GetResponseStream())) body = sr.ReadToEnd();
                var d = (Dictionary<string, object>)new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(body);
                var tag = (string)d["tag_name"];
                if (!Version.TryParse(tag.TrimStart('v'), out var latest) || latest <= new Version(Ver.Major, Ver.Minor, Ver.Build)) return null;
                string url = null;
                foreach (Dictionary<string, object> x in (object[])d["assets"]) if ((string)x["name"] == "WhatOS.exe") url = (string)x["browser_download_url"];
                if (url == null) return null;
                using (var w = new System.Net.WebClient()) { w.Headers.Add("User-Agent", "WhatOS"); w.DownloadFile(url, exe + ".part"); }
                if (new FileInfo(exe + ".part").Length < 500000) { File.Delete(exe + ".part"); return null; }
                try { File.Delete(exe + ".new"); } catch { }
                File.Move(exe + ".part", exe + ".new");
                return tag;
            }
            catch { try { File.Delete(exe + ".part"); } catch { } return null; }
        }

        [STAThread]
        static void Main(string[] args)
        {
            var a = string.Join(" ", args).ToLowerInvariant();
            StartTab = a.Contains("updates") ? 1 : a.Contains("--uninstall") ? 3 : 0;
            Directory.CreateDirectory(Work);
            var ai = Array.IndexOf(args, "--after");
            if (ai >= 0)
            {
                try { if (ai + 1 < args.Length) using (var p = Process.GetProcessById(int.Parse(args[ai + 1]))) p.WaitForExit(10000); } catch { }
                args = args.Where((x, i) => i != ai && i != ai + 1).ToArray();
            }
            Args = args;
            var ci = Array.IndexOf(args, "--clean");
            if (ci >= 0) { Cleaner.Clean((ci + 1 < args.Length ? args[ci + 1] : "temp,shader").Split(',')); return; }
            if (ApplyPending()) return;
            if (Install()) return;
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                var n = new AssemblyName(e.Name).Name + ".dll";
                using (var r = Res("lib." + n))
                {
                    if (r == null) return null;
                    var b = new byte[r.Length];
                    r.Read(b, 0, b.Length);
                    return Assembly.Load(b);
                }
            };
            Extract("lib.WebView2Loader.dll", Path.Combine(Work, "WebView2Loader.dll"));
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        public static Stream Res(string name) => Assembly.GetExecutingAssembly().GetManifestResourceStream(name);

        public static void Extract(string name, string path)
        {
            using (var r = Res(name))
            {
                if (File.Exists(path) && new FileInfo(path).Length == r.Length) return;
                for (var i = 0; ; i++)
                {
                    try { using (var f = File.Create(path)) r.CopyTo(f); return; }
                    catch (IOException) { if (i >= 20) { if (File.Exists(path)) return; throw; } r.Position = 0; System.Threading.Thread.Sleep(250); }
                }
            }
        }

        public static string Text(string name)
        {
            using (var r = Res(name))
            using (var s = new StreamReader(r))
                return s.ReadToEnd();
        }
    }

    class MainForm : Form
    {
        Microsoft.Web.WebView2.WinForms.WebView2 web;
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        Process job;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct WINDOWPOS { public IntPtr hwnd, after; public int x, y, cx, cy; public uint flags; }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct MINMAXINFO { public int ResX, ResY, MaxSizeX, MaxSizeY, MaxPosX, MaxPosY, MinTrackX, MinTrackY, MaxTrackX, MaxTrackY; }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x24)
            {
                var mm = (MINMAXINFO)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(MINMAXINFO));
                mm.MinTrackX = MinimumSize.Width; mm.MinTrackY = MinimumSize.Height;
                System.Runtime.InteropServices.Marshal.StructureToPtr(mm, m.LParam, false);
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == 0x46 && IsZoomed(Handle))
            {
                var wp = (WINDOWPOS)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(WINDOWPOS));
                var wa = ((wp.flags & 3) == 3 ? Screen.FromHandle(Handle) : Screen.FromRectangle(new Rectangle(wp.x, wp.y, Math.Max(1, wp.cx), Math.Max(1, wp.cy)))).WorkingArea;
                wp.x = wa.X; wp.y = wa.Y; wp.cx = wa.Width; wp.cy = wa.Height; wp.flags &= ~3u;
                System.Runtime.InteropServices.Marshal.StructureToPtr(wp, m.LParam, false);
            }
            if (m.Msg == 0x86) { m.LParam = new IntPtr(-1); base.WndProc(ref m); return; }
            if (m.Msg == 0x83 && m.WParam != IntPtr.Zero)
            {
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
            if (m.Msg != 0x84 || WindowState == FormWindowState.Maximized) return;
            var p = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
            int g = 6, w = ClientSize.Width, h = ClientSize.Height;
            bool l = p.X < g, r = p.X >= w - g, t = p.Y < g, b = p.Y >= h - g;
            if (t && l) m.Result = (IntPtr)13; else if (t && r) m.Result = (IntPtr)14; else if (b && l) m.Result = (IntPtr)16; else if (b && r) m.Result = (IntPtr)17;
            else if (l) m.Result = (IntPtr)10; else if (r) m.Result = (IntPtr)11; else if (t) m.Result = (IntPtr)12; else if (b) m.Result = (IntPtr)15;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);

        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ClassStyle |= 0x20000; p.Style |= 0x20000 | 0x40000 | 0x10000 | 0x80000; return p; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int v = 2;
            try { DwmSetWindowAttribute(Handle, 33, ref v, 4); } catch { }
            int bc = 0x00362A20;
            try { DwmSetWindowAttribute(Handle, 34, ref bc, 4); } catch { }
        }

        public MainForm()
        {
            Text = "WhatOS";
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(18, 24, 33);
            var wa = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(wa.Width, Math.Max(1180, wa.Width * 4 / 5)), Math.Min(wa.Height, Math.Max(760, wa.Height * 7 / 8)));
            MinimumSize = new Size(860, 560);
            Padding = new Padding(5);
            Resize += (s, e) => Padding = new Padding(WindowState == FormWindowState.Maximized ? 0 : 5);
            StartPosition = FormStartPosition.CenterScreen;
            var wf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WhatOS", "window.txt");
            try
            {
                var v = File.ReadAllText(wf).Split(',').Select(int.Parse).ToArray();
                var r = new Rectangle(v[0], v[1], v[2], v[3]);
                if (Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(r)) && r.Width >= 860 && r.Height >= 560) { StartPosition = FormStartPosition.Manual; Bounds = r; if (v[4] == 1) WindowState = FormWindowState.Maximized; }
            }
            catch { }
            FormClosing += (s, e) => { try { var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds; Directory.CreateDirectory(Path.GetDirectoryName(wf)); File.WriteAllText(wf, b.X + "," + b.Y + "," + b.Width + "," + b.Height + "," + (WindowState == FormWindowState.Maximized ? 1 : 0)); } catch { } };
            using (var r = Program.Res("ui.logo.png"))
            using (var b = new Bitmap(r))
                Icon = Icon.FromHandle(b.GetHicon());
            Load += async (s, e) => await Init();
            FormClosing += (s, e) =>
            {
                if (job != null && !job.HasExited && MessageBox.Show(this, "WhatOS is still preparing. Stop and close?", "WhatOS", MessageBoxButtons.YesNo) != DialogResult.Yes) e.Cancel = true;
                else if (job != null && !job.HasExited) try { job.Kill(); } catch { }
            };
        }

        static bool HasRuntime()
        {
            try { return !string.IsNullOrEmpty(Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString()); }
            catch { return false; }
        }

        async Task<bool> InstallRuntime()
        {
            var l = new Label { Text = "Getting WebView2 ready…", ForeColor = Color.FromArgb(233, 238, 245), Font = new Font("Segoe UI", 14), AutoSize = true, Location = new Point(40, 40) };
            Controls.Add(l);
            try
            {
                var f = Path.Combine(Program.Work, "wv2setup.exe");
                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                using (var w = new System.Net.WebClient())
                    await w.DownloadFileTaskAsync("https://go.microsoft.com/fwlink/p/?LinkId=2124703", f);
                var p = Process.Start(new ProcessStartInfo(f, "/silent /install") { UseShellExecute = false, CreateNoWindow = true });
                await Task.Run(() => p.WaitForExit());
                Controls.Remove(l);
                return HasRuntime();
            }
            catch (Exception ex)
            {
                l.Text = "WhatOS needs WebView2 and could not install it.\n" + ex.Message + "\nConnect to the internet and start WhatOS again.";
                return false;
            }
        }

        async Task Init()
        {
            Microsoft.Web.WebView2.Core.CoreWebView2Environment.SetLoaderDllFolderPath(Program.Work);
            if (!HasRuntime() && !await InstallRuntime()) return;
            web = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(18, 24, 33) };
            Controls.Add(web);
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, Path.Combine(Program.Work, "wv2"));
            await web.EnsureCoreWebView2Async(env);
            var c = web.CoreWebView2;
            c.Settings.AreDevToolsEnabled = Environment.GetEnvironmentVariable("WHATOS_DEV") == "1";
            c.Settings.AreDefaultContextMenusEnabled = false;
            c.Settings.IsZoomControlEnabled = false;
            c.Settings.IsStatusBarEnabled = false;
            c.WebMessageReceived += (s, e) => OnMessage(e.WebMessageAsJson);
            var html = Program.Text("ui.index.html");
            using (var r = Program.Res("ui.logo.png"))
            using (var m = new MemoryStream())
            {
                r.CopyTo(m);
                html = html.Replace("LOGO_PNG", "data:image/png;base64," + Convert.ToBase64String(m.ToArray()));
            }
            html = html.Replace("<span>Tweaker</span>", "<span>Tweaker</span><span class=\"ver\">" + Program.VerText + "</span><span id=\"upd\"></span>");
            html = html.Replace("const M={tab:0,", "const M={tab:" + Program.StartTab + ",");
            c.NavigateToString(html);
        }

        void Send(object o)
        {
            var s = json.Serialize(o);
            if (InvokeRequired) BeginInvoke((Action)(() => web.CoreWebView2.PostWebMessageAsJson(s)));
            else web.CoreWebView2.PostWebMessageAsJson(s);
        }

        void OnMessage(string raw)
        {
            var m = json.Deserialize<Dictionary<string, object>>(raw);
            var cmd = m["cmd"] as string;
            try
            {
                switch (cmd)
                {
                    case "freevss": Task.Run(() => { try { Ps("vssadmin delete shadows /for=C: /all /quiet | Out-Null"); Send(new { type = "info", info = Info() }); } catch (Exception ex) { Send(new { type = "error", msg = "Could not delete restore points: " + ex.Message }); } }); break;
                    case "info": Task.Run(() => { try { var lp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WhatOS", "last.json"); if (File.Exists(lp)) Send(new { type = "last", picks = json.DeserializeObject(File.ReadAllText(lp)) }); Send(new { type = "info", info = Info() }); } catch (Exception ex) { Send(new { type = "error", msg = "Could not read this PC: " + ex.Message }); } }); break;
                    case "browseIso": BrowseIso(); break;
                    case "prepare": Prepare(m["picks"] as Dictionary<string, object>); break;
                    case "restart": RunPs("finish.ps1", "-Action restart", false); break;
                    case "cancel": RunPs("finish.ps1", "-Action cancel", true); break;
                    case "selfupd": Task.Run(() =>
                    {
                        var vf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WhatOS", "ver.txt");
                        try { var prev = File.Exists(vf) ? File.ReadAllText(vf).Trim() : null; Directory.CreateDirectory(Path.GetDirectoryName(vf)); File.WriteAllText(vf, Program.VerText); if (prev != null && prev != Program.VerText) Send(new { type = "selfupd", updated = Program.VerText }); } catch { }
                        var t = Program.FetchUpdate(); if (t != null) Send(new { type = "selfupd", ready = t });
                    }); break;
                    case "selfrestart": if (Program.ApplyPending()) Close(); break;
                    case "gm": { var act = m["act"] as string; var gc = m.ContainsKey("cfg") ? m["cfg"] : null; if (act == "add") { AddGame(); break; } Task.Run(() => { try { Gm(act, gc); } catch (Exception ex) { Send(new { type = "gm", err = ex.Message }); } }); break; }
                    case "fav":
                        {
                            var ff = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WhatOS", "fav.json");
                            try
                            {
                                if (m.ContainsKey("ids")) { Directory.CreateDirectory(Path.GetDirectoryName(ff)); File.WriteAllText(ff, json.Serialize(m["ids"])); }
                                else Send(new { type = "fav", ids = File.Exists(ff) ? json.DeserializeObject(File.ReadAllText(ff)) : new object[0] });
                            }
                            catch { }
                            break;
                        }
                    case "apps": { var act = m["act"] as string; Task.Run(() => { try { Apps(act, m); } catch (Exception ex) { Send(new { type = "apps", act, err = ex.Message }); } }); break; }
                    case "clean":
                        {
                            var act = m["act"] as string;
                            Task.Run(() =>
                            {
                                try
                                {
                                    if (act == "scan") Send(new { type = "clean", act, sizes = Cleaner.Scan(), sched = Cleaner.Schedule() });
                                    else if (act == "run") { var ids = ((System.Collections.IEnumerable)m["ids"]).Cast<object>().Select(x => x.ToString()).ToArray(); var freed = Cleaner.Clean(ids); Send(new { type = "clean", act, freed, sizes = Cleaner.Scan(), sched = Cleaner.Schedule() }); }
                                    else if (act == "sched") { Cleaner.SetSchedule(m["when"] as string, Application.ExecutablePath); Send(new { type = "clean", act = "scan", sizes = Cleaner.Scan(), sched = Cleaner.Schedule() }); }
                                }
                                catch (Exception ex) { Send(new { type = "clean", act, err = ex.Message }); }
                            });
                            break;
                        }
                    case "power": { var act = m["act"] as string; Task.Run(() => { try { Send(new { type = "power", data = PowerPlans.Do(act, m) }); } catch (Exception ex) { Send(new { type = "power", err = ex.Message }); } }); break; }
                    case "stats": if (statsBusy || WindowState == FormWindowState.Minimized) break; statsBusy = true; Task.Run(() => { try { try { Send(new { type = "stats", data = Stats() }); } catch (Exception ex) { Send(new { type = "stats", err = ex.Message }); } } finally { statsBusy = false; } }); break;
                    case "sysinfo": Task.Run(() => SendCached("sysinfo", SysInfo)); break;
                    case "health": Task.Run(() => SendCached("health", Health)); break;
                    case "clearram": Task.Run(() => { try { var r = @"C:\Program Files\WhatOS\WhatOSRam.exe"; if (!File.Exists(r)) { r = Path.Combine(Program.Work, "WhatOSRam.exe"); Program.Extract("payload.WhatOSRam.exe", r); } if (File.Exists(r)) Process.Start(r)?.WaitForExit(15000); Send(new { type = "stats", data = Stats() }); } catch { } }); break;
                    case "manage": { var a = m["action"] as string; var id = m.ContainsKey("id") ? m["id"] as string : ""; if (a == "status") { Task.Run(() => SendCached("manage", () => Manage("-Action status"))); break; } Task.Run(() => { var o = Manage("-Action " + a + " -Id \"" + id + "\""); Send(o != null ? (object)new { type = "manage", data = o } : new { type = "manage", err = "WhatOS could not change this setting." }); }); break; }
                    case "updcheck": Task.Run(() => { try { var rf = Path.Combine(Program.Work, "remote.json"); using (var w = new System.Net.WebClient()) { System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12; w.DownloadFile(ManifestUrl, rf); } var o = Manage("-Action diff -Manifest \"" + rf + "\""); Send(new { type = "diff", data = o }); } catch (Exception ex) { Send(new { type = "diff", err = "Could not check for updates: " + ex.Message }); } }); break;
                    case "updapply": Task.Run(() => { var o = Manage("-Action update -Manifest \"" + Path.Combine(Program.Work, "remote.json") + "\""); Send(new { type = "updated", data = o }); }); break;
                    case "uninstall": Task.Run(() => { Manage("-Action restore"); Ps(@"
Unregister-ScheduledTask -TaskName WhatOSUpdateCheck -Confirm:$false -EA 0
Remove-Item 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WhatOS' -Recurse -Force -EA 0
Remove-Item 'HKLM:\SOFTWARE\Classes\whatos', 'HKLM:\SOFTWARE\Classes\AppUserModelId\WhatOS', 'HKLM:\SOFTWARE\Classes\DesktopBackground\Shell\WhatOSClearRAM' -Recurse -Force -EA 0
Unregister-ScheduledTask -TaskName WhatOSClearRAM -Confirm:$false -EA 0
Unregister-ScheduledTask -TaskName WhatOSGameMode -Confirm:$false -EA 0
Get-Process WhatOSGame -EA 0 | Stop-Process -Force
Remove-Item 'HKLM:\SOFTWARE\Classes\DesktopBackground\Shell\WhatOSGameMode' -Recurse -Force -EA 0
Remove-Item ""$env:PUBLIC\Desktop\WhatOS.lnk"" -Force -EA 0
Remove-Item ""$env:ProgramData\Microsoft\Windows\Start Menu\Programs\WhatOS.lnk"" -Force -EA 0
Remove-Item 'C:\ProgramData\WhatOS', 'C:\Windows\WhatOS' -Recurse -Force -EA 0
Start-Process powershell -WindowStyle Hidden -ArgumentList '-NoProfile','-Command','for($i=0;$i -lt 150;$i++){ Remove-Item ""C:\Program Files\WhatOS"" -Recurse -Force -EA 0; if(-not (Test-Path ""C:\Program Files\WhatOS"")){break}; Start-Sleep 2 }'
"); Send(new { type = "uninstalled" }); }); break;
                    case "reboot": Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false }); break;
                    case "close": Close(); break;
                    case "min": WindowState = FormWindowState.Minimized; break;
                    case "max": WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; break;
                    case "drag": ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); break;
                }
            }
            catch (Exception ex)
            {
                Send(new { type = "error", msg = ex.Message });
            }
        }

        const string ManifestUrl = "https://raw.githubusercontent.com/alexander566/WhatOS/main/app/payload/tweaks.json";

        object Manage(string args)
        {
            var pay = Path.Combine(Program.Work, "payload");
            Directory.CreateDirectory(pay);
            Program.Extract("payload.manage.ps1", Path.Combine(pay, "manage.ps1"));
            Program.Extract("payload.tweaks.json", Path.Combine(pay, "tweaks.json"));
            var p = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(pay, "manage.ps1") + "\" " + args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8
            });
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            File.AppendAllText(Path.Combine(Program.Work, "manage.log"), DateTime.Now + " " + args + "\r\n" + o + "\r\n");
            var last = o.Split('\n').Select(x => x.Trim()).LastOrDefault(x => x.StartsWith("{"));
            return last == null ? null : json.DeserializeObject(last);
        }

        void SendCached(string type, Func<object> f)
        {
            var cf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WhatOS", type + ".json");
            try { if (File.Exists(cf)) Send(new { type, cached = true, data = json.DeserializeObject(File.ReadAllText(cf)) }); } catch { }
            object d = null;
            try { d = f(); } catch (Exception ex) { Send(new { type, err = ex.Message }); return; }
            Send(d != null ? (object)new { type, data = d } : new { type, err = "WhatOS could not read this." });
            try { if (d != null) { Directory.CreateDirectory(Path.GetDirectoryName(cf)); File.WriteAllText(cf, json.Serialize(d)); } } catch { }
        }

        const string GmExe = @"C:\Program Files\WhatOS\WhatOSGame.exe";
        static string GmFile(string n) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WhatOS", n);
        object GmRead(string n) { try { var f = GmFile(n); return File.Exists(f) ? json.DeserializeObject(File.ReadAllText(f)) : null; } catch { return null; } }
        static void GmSignal(string e) { try { using (var h = System.Threading.EventWaitHandle.OpenExisting(@"Global\WhatOSGame" + e)) h.Set(); } catch { } }

        void Gm(string act, object cfg)
        {
            if (act == "install")
            {
                var src = Path.Combine(Program.Work, "WhatOSGame.exe");
                Program.Extract("payload.WhatOSGame.exe", src);
                Ps(@"
$e='C:\Program Files\WhatOS\WhatOSGame.exe'
New-Item -ItemType Directory 'C:\Program Files\WhatOS' -Force | Out-Null
Get-Process WhatOSGame -EA 0 | Stop-Process -Force
Copy-Item '" + src + @"' $e -Force
$s=New-ScheduledTaskSettingsSet -ExecutionTimeLimit 0 -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -Priority 4
Register-ScheduledTask WhatOSGameMode -Action (New-ScheduledTaskAction -Execute $e) -Trigger (New-ScheduledTaskTrigger -AtLogOn) -Settings $s -Principal (New-ScheduledTaskPrincipal -GroupId 'BUILTIN\Administrators' -RunLevel Highest) -Force | Out-Null
Start-ScheduledTask WhatOSGameMode
$k='HKLM:\SOFTWARE\Classes\DesktopBackground\Shell\WhatOSGameMode'
New-Item ""$k\shell\on\command"", ""$k\shell\off\command"" -Force | Out-Null
Set-ItemProperty $k MUIVerb 'Game Mode'; Set-ItemProperty $k SubCommands ''; Set-ItemProperty $k Icon ""$e,0""
Set-ItemProperty ""$k\shell\on"" MUIVerb 'Turn on'; Set-ItemProperty ""$k\shell\off"" MUIVerb 'Turn off'
Set-ItemProperty ""$k\shell\on\command"" '(default)' ""`""$e`"" --on""; Set-ItemProperty ""$k\shell\off\command"" '(default)' ""`""$e`"" --off""
");
                System.Threading.Thread.Sleep(1500);
            }
            else if (act == "remove")
            {
                GmSignal("Off");
                System.Threading.Thread.Sleep(500);
                Ps(@"
Unregister-ScheduledTask -TaskName WhatOSGameMode -Confirm:$false -EA 0
Get-Process WhatOSGame -EA 0 | Stop-Process -Force
Remove-Item 'HKLM:\SOFTWARE\Classes\DesktopBackground\Shell\WhatOSGameMode' -Recurse -Force -EA 0
");
            }
            else if (act == "cfg" && cfg != null) { Directory.CreateDirectory(Path.GetDirectoryName(GmFile("gm.json"))); File.WriteAllText(GmFile("gm.json"), json.Serialize(cfg)); GmSignal("Reload"); System.Threading.Thread.Sleep(300); }
            else if (act == "scan") { using (var p = Process.Start(new ProcessStartInfo(GmExe, "--scan") { UseShellExecute = false, CreateNoWindow = true })) p.WaitForExit(60000); GmSignal("Reload"); System.Threading.Thread.Sleep(300); }
            else if (act == "on" || act == "off") { GmSignal(act == "on" ? "On" : "Off"); System.Threading.Thread.Sleep(800); }
            var st = GmRead("gm-state.json") as Dictionary<string, object>;
            var running = false;
            try { if (st != null && st.TryGetValue("pid", out var pid) && pid != null) using (var p = Process.GetProcessById(Convert.ToInt32(pid))) running = p.ProcessName.Equals("WhatOSGame", StringComparison.OrdinalIgnoreCase); } catch { }
            Send(new { type = "gm", data = new { installed = File.Exists(GmExe), running, state = running ? st : null, cfg = GmRead("gm.json") ?? new Dictionary<string, object>(), games = GmRead("gm-games.json") ?? new object[0] } });
        }

        void AddGame()
        {
            using (var d = new OpenFileDialog { Filter = "Game (*.exe)|*.exe", Title = "Pick the game's .exe" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) { Task.Run(() => Gm("status", null)); return; }
                var file = d.FileName;
                Task.Run(() =>
                {
                    var c = GmRead("gm.json") as Dictionary<string, object> ?? new Dictionary<string, object>();
                    var extra = (c.TryGetValue("extra", out var x) && x is object[] a ? a.ToList() : new List<object>());
                    if (!extra.OfType<Dictionary<string, object>>().Any(e => string.Equals(e["exe"] as string, file, StringComparison.OrdinalIgnoreCase)))
                        extra.Add(new Dictionary<string, object> { ["name"] = Path.GetFileNameWithoutExtension(file), ["exe"] = file });
                    c["extra"] = extra;
                    Directory.CreateDirectory(Path.GetDirectoryName(GmFile("gm.json")));
                    File.WriteAllText(GmFile("gm.json"), json.Serialize(c));
                    Gm("scan", null);
                });
            }
        }

        static string Winget()
        {
            var a = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");
            if (File.Exists(a)) return a;
            try { var d = Directory.GetDirectories(@"C:\Program Files\WindowsApps", "Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe").OrderByDescending(x => x).Select(x => Path.Combine(x, "winget.exe")).FirstOrDefault(File.Exists); if (d != null) return d; } catch { }
            return null;
        }
        static int Run(string exe, string args, out string output, int ms = 600000)
        {
            using (var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 }))
            {
                var t = p.StandardOutput.ReadToEndAsync(); p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(ms)) { try { p.Kill(); } catch { } output = "timeout"; return -1; }
                output = t.Result; return p.ExitCode;
            }
        }
        static readonly object appsLock = new object();
        static HashSet<string> wgInstalled;

        void Apps(string act, Dictionary<string, object> m)
        {
            if (act == "catalog")
            {
                var cat = json.DeserializeObject(Program.Text("payload.apps-catalog.json"));
                Send(new { type = "apps", act, catalog = cat, installed = wgInstalled, winget = Winget() != null });
                var wg = Winget();
                if (wg == null) return;
                var f = Path.Combine(Program.Work, "wg-export.json");
                try { File.Delete(f); } catch { }
                Run(wg, "export -o \"" + f + "\" --accept-source-agreements --disable-interactivity", out _, 120000);
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var d = (Dictionary<string, object>)json.DeserializeObject(File.ReadAllText(f));
                    foreach (Dictionary<string, object> src in (object[])d["Sources"]) foreach (Dictionary<string, object> pk in (object[])src["Packages"]) ids.Add((string)pk["PackageIdentifier"]);
                }
                catch { }
                wgInstalled = ids;
                Send(new { type = "apps", act, catalog = cat, installed = ids, winget = true });
            }
            else if (act == "install")
            {
                var wg = Winget();
                if (wg == null) { Send(new { type = "apps", act, err = "winget is missing. Install App Installer from the Microsoft Store." }); return; }
                lock (appsLock)
                    foreach (var id in ((System.Collections.IEnumerable)m["ids"]).Cast<object>().Select(x => x.ToString()))
                    {
                        Send(new { type = "appsprog", id, st = "busy" });
                        var rc = Run(wg, "install --id \"" + id + "\" -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity", out var o, 1200000);
                        var ok = rc == 0 || rc == -1978335189 || rc == -1978335135;
                        if (ok) (wgInstalled ?? (wgInstalled = new HashSet<string>(StringComparer.OrdinalIgnoreCase))).Add(id);
                        Send(new { type = "appsprog", id, st = ok ? "done" : "fail", msg = ok ? null : "winget error " + rc });
                    }
                Send(new { type = "appsprog", id = (string)null, st = "end" });
            }
            else if (act == "installed") Send(new { type = "apps", act, list = InstalledApps() });
            else if (act == "remove")
            {
                var key = m["key"] as string;
                var a = InstalledApps().FirstOrDefault(x => (string)x["key"] == key);
                if (a == null) { Send(new { type = "apps", act, err = "That app is already gone." }); return; }
                var cmd = (a["quiet"] as string) ?? (a["un"] as string) ?? "";
                var mm = System.Text.RegularExpressions.Regex.Match(cmd, @"(?i)msiexec(\.exe)?\s+/[ix]\s*(\{[0-9A-F-]+\})");
                string exe, args;
                if (mm.Success) { exe = "msiexec.exe"; args = "/x " + mm.Groups[2].Value + " /qb /norestart"; }
                else if (cmd.StartsWith("\"")) { var e = cmd.IndexOf('"', 1); exe = cmd.Substring(1, e - 1); args = cmd.Substring(e + 1).Trim(); }
                else { var e = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); exe = e > 0 ? cmd.Substring(0, e + 4) : cmd; args = e > 0 ? cmd.Substring(e + 4).Trim() : ""; }
                Send(new { type = "apps", act = "removing", key });
                var wg = Winget();
                string wo = null; var rc = wg == null ? -1 : Run(wg, "uninstall --name \"" + ((string)a["name"]).Replace("\"", "") + "\" --exact --silent --accept-source-agreements --disable-interactivity", out wo, 900000);
                try { File.AppendAllText(Path.Combine(Program.Work, "apps.log"), DateTime.Now + " uninstall " + a["name"] + " rc=" + rc + "\r\n" + wo + "\r\n"); } catch { }
                var quiet = false;
                if (rc == 0) for (var i = 0; i < 15 && !(quiet = !InstalledApps().Any(x => (string)x["key"] == key)); i++) System.Threading.Thread.Sleep(2000);
                if (!quiet) try { using (var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true })) p.WaitForExit(900000); } catch (Exception ex) { Send(new { type = "apps", act, err = "Could not start the uninstaller: " + ex.Message }); }
                System.Threading.Thread.Sleep(1500);
                Send(new { type = "apps", act = "installed", list = InstalledApps() });
            }
            else if (act == "startup") Send(new { type = "apps", act, list = StartupItems() });
            else if (act == "toggle")
            {
                var id = (m["id"] as string).Split('|');
                var on = (bool)m["on"];
                var hive = id[0] == "HKLM" ? Microsoft.Win32.Registry.LocalMachine : Microsoft.Win32.Registry.CurrentUser;
                using (var k = hive.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\" + id[1]))
                {
                    var b = new byte[12]; b[0] = (byte)(on ? 2 : 3);
                    if (!on) BitConverter.GetBytes(DateTime.Now.ToFileTime()).CopyTo(b, 4);
                    k.SetValue(id[2], b, Microsoft.Win32.RegistryValueKind.Binary);
                }
                Send(new { type = "apps", act = "startup", list = StartupItems() });
            }
        }

        static List<Dictionary<string, object>> InstalledApps()
        {
            var list = new List<Dictionary<string, object>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (hive, hn, path) in new[] { (Microsoft.Win32.Registry.LocalMachine, "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"), (Microsoft.Win32.Registry.LocalMachine, "HKLM", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"), (Microsoft.Win32.Registry.CurrentUser, "HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall") })
                using (var k = hive.OpenSubKey(path))
                {
                    if (k == null) continue;
                    foreach (var n in k.GetSubKeyNames())
                        using (var a = k.OpenSubKey(n))
                        {
                            var name = a?.GetValue("DisplayName") as string;
                            var un = a?.GetValue("UninstallString") as string;
                            if (string.IsNullOrWhiteSpace(name) || un == null || Convert.ToInt32(a.GetValue("SystemComponent") ?? 0) == 1 || a.GetValue("ParentKeyName") != null || n == "WhatOS" || !seen.Add(name)) continue;
                            list.Add(new Dictionary<string, object> { ["key"] = hn + "|" + path + "|" + n, ["name"] = name.Trim(), ["pub"] = a.GetValue("Publisher") as string, ["ver"] = a.GetValue("DisplayVersion") as string, ["size"] = Convert.ToDouble(a.GetValue("EstimatedSize") ?? 0) * 1024, ["un"] = un, ["quiet"] = a.GetValue("QuietUninstallString") as string });
                        }
                }
            return list.OrderBy(x => (string)x["name"], StringComparer.OrdinalIgnoreCase).ToList();
        }

        static List<Dictionary<string, object>> StartupItems()
        {
            var list = new List<Dictionary<string, object>>();
            bool On(Microsoft.Win32.RegistryKey hive, string ap, string name)
            {
                using (var k = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\" + ap))
                    return !(k?.GetValue(name) is byte[] b && b.Length > 0 && (b[0] & 1) == 1);
            }
            foreach (var (hive, hn, path, ap) in new[] { (Microsoft.Win32.Registry.CurrentUser, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Run", "Run"), (Microsoft.Win32.Registry.LocalMachine, "HKLM", @"Software\Microsoft\Windows\CurrentVersion\Run", "Run"), (Microsoft.Win32.Registry.LocalMachine, "HKLM", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Run32") })
                using (var k = hive.OpenSubKey(path))
                    if (k != null) foreach (var n in k.GetValueNames()) if (n.Length > 0) list.Add(new Dictionary<string, object> { ["id"] = hn + "|" + ap + "|" + n, ["name"] = n, ["cmd"] = k.GetValue(n)?.ToString(), ["on"] = On(hive, ap, n), ["all"] = hn == "HKLM" });
            foreach (var (dir, hive, hn) in new[] { (Environment.GetFolderPath(Environment.SpecialFolder.Startup), Microsoft.Win32.Registry.CurrentUser, "HKCU"), (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Microsoft.Win32.Registry.LocalMachine, "HKLM") })
                try { foreach (var f in Directory.GetFiles(dir).Where(x => !x.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase))) list.Add(new Dictionary<string, object> { ["id"] = hn + "|StartupFolder|" + Path.GetFileName(f), ["name"] = Path.GetFileNameWithoutExtension(f), ["cmd"] = f, ["on"] = On(hive, "StartupFolder", Path.GetFileName(f)), ["all"] = hn == "HKLM" }); } catch { }
            return list.OrderBy(x => (string)x["name"], StringComparer.OrdinalIgnoreCase).ToList();
        }

        static PerformanceCounter cpuC;
        static volatile bool statsBusy;
        static PerformanceCounter diskC, procC, perfC, rdC, wrC;
        static float freqMhz;
        static List<PerformanceCounter[]> netC;
        static int netAge;
        static DateTime diskTAt = DateTime.MinValue;
        static double? diskT;
        static bool netFresh;
        static List<PerformanceCounter> thermC;
        static bool thermNone;
        static PerformanceCounterCategory vramCat;
        static readonly HashSet<string> gpuLuids = new HashSet<string>();
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct KOPEN { public uint Lo; public int Hi; public uint h; }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct KQAI { public uint h; public int Type; public IntPtr p; public uint Size; }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct KPERF { public uint Index; public ulong MemFreq, MaxMemFreq, MaxMemFreqOC, MemBw, PcieBw; public uint Fan, Power, Temp; public byte Pso; }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct KCLOSE { public uint h; }
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern int D3DKMTOpenAdapterFromLuid(ref KOPEN o);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern int D3DKMTQueryAdapterInfo(ref KQAI q);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern int D3DKMTCloseAdapter(ref KCLOSE c);
        static double GpuTemp(string luid)
        {
            try
            {
                var m = System.Text.RegularExpressions.Regex.Match(luid, "luid_0x([0-9a-fA-F]+)_0x([0-9a-fA-F]+)");
                if (!m.Success) return 0;
                var o = new KOPEN { Hi = Convert.ToInt32(m.Groups[1].Value, 16), Lo = Convert.ToUInt32(m.Groups[2].Value, 16) };
                if (D3DKMTOpenAdapterFromLuid(ref o) != 0) return 0;
                var size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(KPERF));
                var buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
                try
                {
                    System.Runtime.InteropServices.Marshal.StructureToPtr(new KPERF(), buf, false);
                    var q = new KQAI { h = o.h, Type = 62, p = buf, Size = (uint)size };
                    if (D3DKMTQueryAdapterInfo(ref q) != 0) return 0;
                    var r = (KPERF)System.Runtime.InteropServices.Marshal.PtrToStructure(buf, typeof(KPERF));
                    return r.Temp > 0 && r.Temp < 1500 ? Math.Round(r.Temp / 10.0) : 0;
                }
                finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buf); var c = new KCLOSE { h = o.h }; D3DKMTCloseAdapter(ref c); }
            }
            catch { return 0; }
        }
        static PerformanceCounterCategory gpuCat;
        static InstanceDataCollection gpuPrev;
        object Stats()
        {
            var init = cpuC == null;
            if (init)
            {
                cpuC = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total"); cpuC.NextValue();
                diskC = new PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total"); diskC.NextValue();
                procC = new PerformanceCounter("System", "Processes");
                try { perfC = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total"); perfC.NextValue(); freqMhz = new PerformanceCounter("Processor Information", "Processor Frequency", "_Total").NextValue(); } catch { }
                try { rdC = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total"); wrC = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total"); rdC.NextValue(); wrC.NextValue(); } catch { }
                Task.Run(() => { try { var g = new PerformanceCounterCategory("GPU Engine"); gpuPrev = g.ReadCategory()["utilization percentage"]; gpuCat = g; } catch { } });
                System.Threading.Thread.Sleep(300);
            }
            double cpu = Math.Min(100, cpuC.NextValue());
            var ms = new MEMORYSTATUSEX(); ms.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(ms); GlobalMemoryStatusEx(ref ms);
            double? gpu = null;
            if (gpuCat != null) try
            {
                var a = gpuPrev;
                var b = gpuCat.ReadCategory()["utilization percentage"];
                gpuPrev = b;
                var per = new Dictionary<string, double>();
                foreach (InstanceData x in b.Values)
                {
                    if (a == null || !a.Contains(x.InstanceName)) continue;
                    var n = x.InstanceName; int li = n.IndexOf("luid_"), ei = n.IndexOf("engtype_");
                    if (li < 0 || ei < 0) continue;
                    var lk = n.Substring(li, n.IndexOf("_phys", li) > 0 ? n.IndexOf("_phys", li) - li : 20);
                    gpuLuids.Add(lk);
                    var key = lk + "|" + n.Substring(ei);
                    var v = CounterSample.Calculate(a[n].Sample, x.Sample);
                    per[key] = (per.TryGetValue(key, out var o) ? o : 0) + v;
                }
                gpu = per.Count > 0 ? per.Values.Max() : 0;
            }
            catch { }
            double disk = Math.Max(0, Math.Min(100, 100 - diskC.NextValue()));
            double ghz = 0, rd = 0, wr = 0, rx = 0, tx = 0, vram = 0; double? ctemp = null, gtemp = null;
            try { if (perfC != null && freqMhz > 0) ghz = Math.Round(perfC.NextValue() / 100 * freqMhz / 1000, 2); } catch { }
            try { if (rdC != null) { rd = rdC.NextValue(); wr = wrC.NextValue(); } } catch { }
            try
            {
                if (netC == null || ++netAge > 30)
                {
                    netAge = 0;
                    var names = new PerformanceCounterCategory("Network Interface").GetInstanceNames();
                    if (netC == null || !names.OrderBy(x => x).SequenceEqual(netC.Select(c => c[0].InstanceName).OrderBy(x => x)))
                    {
                        netC = names.Select(i => new[] { new PerformanceCounter("Network Interface", "Bytes Received/sec", i), new PerformanceCounter("Network Interface", "Bytes Sent/sec", i) }).ToList();
                        foreach (var c in netC) { c[0].NextValue(); c[1].NextValue(); }
                        netFresh = true;
                    }
                }
                if (!netFresh) foreach (var c in netC) { rx += c[0].NextValue(); tx += c[1].NextValue(); }
                netFresh = false;
            }
            catch { netC = null; }
            try
            {
                if (thermC == null && !thermNone)
                {
                    var cat = new PerformanceCounterCategory("Thermal Zone Information");
                    thermC = cat.GetInstanceNames().Select(i => new PerformanceCounter("Thermal Zone Information", "Temperature", i)).ToList();
                    if (thermC.Count == 0) thermNone = true;
                }
                if (thermC != null) { var k = thermC.Max(c => c.NextValue()); if (k > 200) ctemp = Math.Round(k - 273.15); }
            }
            catch { thermNone = true; thermC = null; }
            if ((DateTime.Now - diskTAt).TotalSeconds > 30)
            {
                diskTAt = DateTime.Now;
                Task.Run(() =>
                {
                    try
                    {
                        double mx = 0;
                        using (var q = new System.Management.ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT * FROM MSFT_PhysicalDisk"))
                            foreach (System.Management.ManagementObject d0 in q.Get())
                                foreach (System.Management.ManagementObject r in d0.GetRelated("MSFT_StorageReliabilityCounter"))
                                    mx = Math.Max(mx, Convert.ToDouble(r["Temperature"] ?? 0));
                        diskT = mx > 0 ? (double?)mx : null;
                    }
                    catch { diskT = null; }
                });
            }
            foreach (var l in gpuLuids.ToArray())
            {
                var t = GpuTemp(l);
                if (t > 0 && (gtemp == null || t > gtemp)) gtemp = t;
            }
            try
            {
                if (vramCat == null) vramCat = new PerformanceCounterCategory("GPU Adapter Memory");
                foreach (InstanceData x in vramCat.ReadCategory()["dedicated usage"].Values) vram = Math.Max(vram, x.RawValue);
            }
            catch { }
            var d = new DriveInfo("C");
            return new
            {
                cpu = Math.Round(cpu), gpu = gpu.HasValue ? (object)Math.Round(Math.Min(100, gpu.Value)) : null,
                ramUsed = (double)(ms.ullTotalPhys - ms.ullAvailPhys), ramTotal = (double)ms.ullTotalPhys,
                diskUsed = (double)(d.TotalSize - d.AvailableFreeSpace), diskTotal = (double)d.TotalSize,
                disk = Math.Round(disk), procs = (int)procC.NextValue(), uptime = GetTickCount64() / 1000,
                ghz, rd = Math.Round(rd), wr = Math.Round(wr), rx = Math.Round(rx), tx = Math.Round(tx), vram, ctemp, gtemp, dtemp = diskT
            };
        }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern ulong GetTickCount64();

        object SysInfo() => json.DeserializeObject(Ps(@"
$os=Get-CimInstance Win32_OperatingSystem; $cpu=Get-CimInstance Win32_Processor | select -First 1
$g=@(Get-CimInstance Win32_VideoController | ? { $_.PNPDeviceID -match '^PCI' } | % { [pscustomobject]@{ name=$_.Name; drv=$_.DriverVersion } })
$dk=Get-PhysicalDisk | ? { $_.DeviceId -eq ((Get-Partition -DriveLetter C).DiskNumber) } | select -First 1
$pm=@(Get-CimInstance Win32_PhysicalMemory); $mt=@{20='DDR';21='DDR2';24='DDR3';26='DDR4';30='LPDDR4';34='DDR5';35='LPDDR5'}[[int]($pm[0].SMBIOSMemoryType)]; $sp=($pm | % { if($_.ConfiguredClockSpeed){$_.ConfiguredClockSpeed}else{$_.Speed} } | measure -Maximum).Maximum; $mf=(@($pm | % { ($_.Manufacturer -replace '\s+$','') } | ? { $_ -and $_ -notmatch '^(Unknown|Undefined|0+)$' } | select -Unique) -join '/'); $pn=(@($pm | % { ""$($_.PartNumber)"".Trim() } | ? { $_ } | select -Unique) -join '/')
$mem=(@($mt, $(if($sp){""$sp MT/s""}), ""$($pm.Count)x$([math]::Round(($pm | select -First 1).Capacity/1GB)) GB"", (""$mf $pn"").Trim()) | ? { $_ }) -join ' · '
$sb=try{Confirm-SecureBootUEFI -EA Stop}catch{$null}; $tpm=try{(Get-CimInstance -Namespace root\cimv2\security\microsofttpm Win32_Tpm -EA Stop).SpecVersion.Split(',')[0]}catch{''}
[pscustomobject]@{ host=$env:COMPUTERNAME; os=($os.Caption -replace 'Microsoft ',''); ver=(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').DisplayVersion; build=""$($os.BuildNumber).$((Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').UBR)""
 cpu=($cpu.Name -replace '\s+',' ').Trim(); cores=$cpu.NumberOfCores; threads=$cpu.NumberOfLogicalProcessors; gpus=$g; ram=[math]::Round($os.TotalVisibleMemorySize/1MB)
 mem=$mem; disk=$dk.FriendlyName; diskType=[string]$dk.MediaType; fw=$env:firmware_type; secureBoot=$sb; tpm=$tpm } | ConvertTo-Json -Compress -Depth 3"));

        object Health() => json.DeserializeObject(Ps(@"
$r=@()
function A($ok,$n,$d,$v,$fix){ $script:r+=[pscustomobject]@{ ok=$ok; name=$n; desc=$d; res=$v; fix=$fix } }
$v=Get-CimInstance Win32_VideoController | ? { $_.PNPDeviceID -match 'VEN_10DE' } | select -First 1
if($v){ try{
 $want=$v.Name -replace '^NVIDIA ',''; $x=[xml](Invoke-WebRequest 'https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3' -UseBasicParsing -UserAgent 'Mozilla/5.0' -TimeoutSec 20).Content
 $c=@($x.LookupValueSearch.LookupValues.LookupValue | ? { ($_.Name -replace '^NVIDIA ','') -eq $want })[0]
 $l=(Invoke-RestMethod ""https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php?func=DriverManualLookup&psid=$($c.ParentID)&pfid=$($c.Value)&osID=57&languageCode=1033&isWHQL=1&dch=1&sort1=0&numberOfResults=1"" -UserAgent 'Mozilla/5.0' -TimeoutSec 20).IDS[0].downloadInfo.Version
 $h=(($v.DriverVersion -split '\.')[2..3] -join '') -replace '^.*(\d{5})$','$1'; $hv=$h.Substring(0,3)+'.'+$h.Substring(3)
 if([double]$l -gt [double]$hv){ A $false 'Graphics driver' ""$($v.Name): $hv installed, $l is out"" 'Update available' 'nvidia' } else { A $true 'Graphics driver' ""$($v.Name) $hv is the newest"" 'Up to date' '' } } catch { A $true 'Graphics driver' $v.Name 'Could not check' '' } }
$p=(Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')
if($p){ A $false 'Restart needed' 'Windows finished an update and wants a restart' 'Pending' 'restart' } else { A $true 'Restart needed' 'Nothing is waiting for a restart' 'No' '' }
$sb=try{Confirm-SecureBootUEFI -EA Stop}catch{$null}; A ([bool]$sb) 'Secure Boot' 'Some anti-cheat games need it' $(if($sb){'On'}elseif($null -eq $sb){'Not supported'}else{'Off'}) ''
$t=try{Get-Tpm -EA Stop}catch{$null}; A ([bool]$t.TpmReady) 'TPM' 'Needed by Windows 11 and some games' $(if($t.TpmPresent){'Ready'}else{'Not found'}) ''
$dk=Get-PhysicalDisk | ? { $_.DeviceId -eq ((Get-Partition -DriveLetter C).DiskNumber) } | select -First 1; $w=($dk | Get-StorageReliabilityCounter -EA 0).Wear
A ($dk.HealthStatus -eq 'Healthy') 'Disk health' ""$($dk.FriendlyName)$(if($w -ne $null){"" · $(100-$w)% life left""})"" ([string]$dk.HealthStatus) ''
$act=@(Get-CimInstance SoftwareLicensingProduct -Filter ""ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' and PartialProductKey is not null"" -EA 0 | ? LicenseStatus -eq 1).Count -gt 0
A $act 'Windows activation' (Get-CimInstance Win32_OperatingSystem).Caption $(if($act){'Activated'}else{'Not activated'}) ''
$pg=try{(Test-Connection 1.1.1.1 -Count 2 -EA Stop | measure ResponseTime -Average).Average}catch{$null}
A ($null -ne $pg) 'Internet' 'Ping to 1.1.1.1' $(if($null -ne $pg){""$([int]$pg) ms""}else{'No connection'}) ''
$f=(Get-Volume C).SizeRemaining; A ($f -gt 20GB) 'Free space on C:' ""$([math]::Round($f/1GB)) GB free"" $(if($f -gt 20GB){'Good'}else{'Low'}) 'clean'
@($r) | ConvertTo-Json -Compress -Depth 3"));

        static string Ps(string script)
        {
            var f = Path.Combine(Program.Work, "q" + Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(f, "[Console]::OutputEncoding=[Text.Encoding]::UTF8\r\n" + script, new UTF8Encoding(true));
            var p = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + f + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8
            });
            try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            var o = p.StandardOutput.ReadToEnd();
            try { File.Delete(f); } catch { }
            p.WaitForExit();
            return o.Trim();
        }

        object Info()
        {
            var o = Ps(@"
$os=Get-CimInstance Win32_OperatingSystem
$c=Get-Partition -DriveLetter C
$d=Get-Disk -Number $c.DiskNumber
$v=Get-Volume -DriveLetter C
$bl=$false; try { $bl=(Get-BitLockerVolume -MountPoint C: -EA Stop).ProtectionStatus -eq 'On' } catch {}
$max=(Get-PartitionSupportedSize -DriveLetter C).SizeMin
$usbs=@(Get-Disk | ?{ $_.BusType -eq 'USB' -and -not $_.IsBoot -and -not $_.IsSystem } | %{ $l=(Get-Partition -DiskNumber $_.Number -EA 0 | ?{ $_.DriveLetter } | %{ ""$($_.DriveLetter):"" }) -join ' '; [pscustomobject]@{n=$_.Number; name=$_.FriendlyName; size=$_.Size; letters=$l} })
$parts=@(Get-Partition -DiskNumber $d.Number | ?{ $_.Type -in 'Basic','IFS' } | %{ $vv=$null; if($_.DriveLetter){ $vv=Get-Volume -DriveLetter $_.DriveLetter -EA 0 }; [pscustomobject]@{l=[string]$_.DriveLetter; size=$_.Size; label=[string]$vv.FileSystemLabel} })
[pscustomobject]@{
 caption=$os.Caption; build=$os.BuildNumber
 edition=$(if($os.Caption -match 'Pro'){'Pro'}else{'Home'})
 uefi=($env:firmware_type -eq 'UEFI'); gpt=($d.PartitionStyle -eq 'GPT')
 disk=$d.FriendlyName; diskSize=$d.Size
 cSize=$c.Size; cFree=$v.SizeRemaining; cMin=$max; vss=@(Get-CimInstance Win32_ShadowCopy -EA 0).Count
 bitlocker=$bl; parts=$parts; usbs=$usbs
 whatos=((Test-Path 'C:\ProgramData\WhatOS\state.json') -or (Test-Path 'C:\Windows\WhatOS\tweaks.json'))
} | ConvertTo-Json -Compress -Depth 4");
            return json.DeserializeObject(o);
        }

        void BrowseIso()
        {
            using (var d = new OpenFileDialog { Filter = "Windows ISO (*.iso)|*.iso", Title = "Pick a Windows ISO" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) { Send(new { type = "iso", path = (string)null }); return; }
                var f = new FileInfo(d.FileName);
                Send(new { type = "iso", path = f.FullName, name = f.Name, size = f.Length });
            }
        }

        void Prepare(Dictionary<string, object> picks)
        {
            var pay = Path.Combine(Program.Work, "payload");
            Directory.CreateDirectory(pay);
            foreach (var n in Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(n => n.StartsWith("payload.")))
                Program.Extract(n, Path.Combine(pay, n.Substring(8)));
            try { File.Copy(Application.ExecutablePath, Path.Combine(pay, "WhatOS.exe"), true); } catch { }
            var over = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "pe.xml");
            if (File.Exists(over)) File.Copy(over, Path.Combine(pay, "pe.xml"), true);
            File.WriteAllText(Path.Combine(pay, "picks.json"), json.Serialize(picks), new UTF8Encoding(false));
            try
            {
                var ld = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WhatOS");
                Directory.CreateDirectory(ld);
                File.WriteAllText(Path.Combine(ld, "last.json"), json.Serialize(picks), new UTF8Encoding(false));
            }
            catch { }
            RunPs("prepare.ps1", "-Payload \"" + pay + "\"", true);
        }

        void RunPs(string script, string args, bool stream)
        {
            var pay = Path.Combine(Program.Work, "payload");
            Directory.CreateDirectory(pay);
            var path = Path.Combine(pay, script);
            Program.Extract("payload." + script, path);
            var log = Path.Combine(Program.Work, "prepare.log");
            job = new Process
            {
                StartInfo = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + path + "\" " + args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8
                },
                EnableRaisingEvents = true
            };
            job.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                File.AppendAllText(log, e.Data + "\r\n");
                if (!stream) return;
                if (e.Data.StartsWith("@")) Send(new { type = "ps", line = e.Data.Substring(1) });
            };
            job.ErrorDataReceived += (s, e) => { if (e.Data != null) File.AppendAllText(log, "ERR " + e.Data + "\r\n"); };
            job.Exited += (s, e) => Send(new { type = "exit", script, code = job.ExitCode });
            job.Start();
            job.BeginOutputReadLine();
            job.BeginErrorReadLine();
        }
    }
}

static class Cleaner
{
    static string E(string p) => Environment.ExpandEnvironmentVariables(p);
    public static readonly Dictionary<string, string[]> Targets = new Dictionary<string, string[]>
    {
        ["temp"] = new[] { "%TEMP%", @"%SystemRoot%\Temp" },
        ["shader"] = new[] { @"%LOCALAPPDATA%\D3DSCache", @"%LOCALAPPDATA%\NVIDIA\DXCache", @"%LOCALAPPDATA%\NVIDIA\GLCache", @"%LOCALAPPDATA%\NVIDIA Corporation\NV_Cache", @"%ProgramData%\NVIDIA Corporation\NV_Cache", @"%LOCALAPPDATA%\AMD\DxCache", @"%LOCALAPPDATA%\AMD\DxcCache", @"%LOCALAPPDATA%\AMD\VkCache", @"%LOCALAPPDATA%\AMD\GLCache", @"%USERPROFILE%\AppData\LocalLow\Intel\ShaderCache" },
        ["wu"] = new[] { @"%SystemRoot%\SoftwareDistribution\Download" },
        ["dorepo"] = new[] { @"%SystemRoot%\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache" },
        ["dumps"] = new[] { @"%SystemRoot%\Minidump", @"%LOCALAPPDATA%\CrashDumps", @"%ProgramData%\Microsoft\Windows\WER\ReportArchive", @"%ProgramData%\Microsoft\Windows\WER\ReportQueue", @"%LOCALAPPDATA%\Microsoft\Windows\WER\ReportArchive" },
        ["thumbs"] = new[] { @"%LOCALAPPDATA%\Microsoft\Windows\Explorer" },
    };
    static bool Match(string id, string f) => id != "thumbs" || Path.GetFileName(f).StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f).StartsWith("iconcache_", StringComparison.OrdinalIgnoreCase);
    static IEnumerable<string> Files(string dir)
    {
        var st = new Stack<string>(); st.Push(dir);
        while (st.Count > 0)
        {
            var d = st.Pop(); string[] fs = null, ds = null;
            try { fs = Directory.GetFiles(d); ds = Directory.GetDirectories(d); } catch { continue; }
            foreach (var f in fs) yield return f;
            foreach (var x in ds) try { if ((File.GetAttributes(x) & FileAttributes.ReparsePoint) == 0) st.Push(x); } catch { }
        }
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)] struct RBI { public int cbSize; public long Size; public long Items; }
    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern int SHQueryRecycleBin(string root, ref RBI i);
    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern int SHEmptyRecycleBin(IntPtr h, string root, int flags);
    public static Dictionary<string, double> Scan()
    {
        var r = new Dictionary<string, double>();
        foreach (var t in Targets)
        {
            long n = 0;
            foreach (var d in t.Value.Select(E).Where(Directory.Exists)) foreach (var f in Files(d)) if (Match(t.Key, f)) try { n += new FileInfo(f).Length; } catch { }
            if (t.Key == "dumps") try { var md = E(@"%SystemRoot%\MEMORY.DMP"); if (File.Exists(md)) n += new FileInfo(md).Length; } catch { }
            r[t.Key] = n;
        }
        var rb = new RBI { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(RBI)) };
        r["recycle"] = SHQueryRecycleBin(null, ref rb) == 0 ? rb.Size : 0;
        return r;
    }
    public static Dictionary<string, double> Clean(string[] ids)
    {
        var before = Scan();
        foreach (var id in ids)
        {
            if (id == "recycle") { try { SHEmptyRecycleBin(IntPtr.Zero, null, 7); } catch { } continue; }
            if (!Targets.TryGetValue(id, out var dirs)) continue;
            if (id == "wu" || id == "dorepo") foreach (var sv in new[] { "wuauserv", "bits", "dosvc" }) try { using (var sc = new System.ServiceProcess.ServiceController(sv)) if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running) { sc.Stop(); sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20)); } } catch { }
            foreach (var d in dirs.Select(E).Where(Directory.Exists))
            {
                foreach (var f in Files(d).ToList()) if (Match(id, f)) try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch { }
                if (id != "thumbs") foreach (var x in Directory.GetDirectories(d, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length)) try { Directory.Delete(x); } catch { }
            }
            if (id == "dumps") try { File.Delete(E(@"%SystemRoot%\MEMORY.DMP")); } catch { }
        }
        var after = Scan();
        return ids.Where(after.ContainsKey).ToDictionary(i => i, i => Math.Max(0, before[i] - after[i]));
    }
    public static string Schedule()
    {
        try { using (var p = Process.Start(new ProcessStartInfo("schtasks.exe", "/query /tn WhatOSClean /xml") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })) { var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(); return p.ExitCode != 0 ? "off" : o.Contains("<ScheduleByMonth>") ? "monthly" : o.Contains("<ScheduleByWeek>") ? "weekly" : "daily"; } }
        catch { return "off"; }
    }
    public static void SetSchedule(string when, string exe)
    {
        var inst = Path.Combine(WhatOS.Program.InstDir, "WhatOS.exe");
        if (File.Exists(inst)) exe = inst;
        string a = when == "off" ? "/delete /tn WhatOSClean /f" : "/create /tn WhatOSClean /f /rl highest /tr \"\\\"" + exe + "\\\" --clean temp,shader,dumps\" " + (when == "monthly" ? "/sc monthly /d 1 /st 12:00" : when == "daily" ? "/sc daily /st 12:00" : "/sc weekly /d SUN /st 12:00");
        using (var p = Process.Start(new ProcessStartInfo("schtasks.exe", a) { UseShellExecute = false, CreateNoWindow = true })) p.WaitForExit();
        if (when != "off") using (var p = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -Command \"$s=Get-ScheduledTask WhatOSClean; $s.Settings.StartWhenAvailable=$true; $s.Settings.DisallowStartIfOnBatteries=$false; $s | Set-ScheduledTask | Out-Null\"") { UseShellExecute = false, CreateNoWindow = true })) p.WaitForExit();
    }
}

static class PowerPlans
{
    static readonly string[][] Opts =
    {
        new[] { "usb", "USB never sleeps", "Mouse, keyboard and headset stay awake. Fixes USB devices cutting out", "2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", "0", "1", "" },
        new[] { "pcie", "PCIe power saving off", "Graphics card and NVMe drive stay at full link speed", "501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", "0", "1", "" },
        new[] { "sleep", "Never sleep when plugged in", "The PC stays on until you turn it off", "238c9fa8-0aad-41ed-83f4-97be242c8f20", "29f6c1db-86da-48c5-9fdb-f2b67b1f44da", "0", "1800", "" },
        new[] { "disk", "Drives never turn off", "No wait for a hard drive to spin up again", "0012ee47-9041-4b5d-9b77-535fba8b1442", "6738e2c4-e8a5-4a42-b16a-e040e769756e", "0", "1200", "" },
        new[] { "display", "Screen never turns off when plugged in", "Good for watching or long downloads", "7516b95f-f776-4464-8c53-06167f40cc99", "3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e", "0", "600", "" },
        new[] { "lid", "Keep running when the lid closes", "Plugged in only. Handy with an external monitor", "4f971e89-eebd-4455-a8de-9e59040e7347", "5ca83367-6e45-459f-a27b-476b1d01c936", "0", "1", "laptop" },
    };
    static string Pc(string a) { using (var p = Process.Start(new ProcessStartInfo("powercfg.exe", a) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })) { var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(); return o; } }
    static bool Laptop => SystemInformation.PowerStatus.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery;
    public static object Do(string act, Dictionary<string, object> m)
    {
        if (act == "use") Pc("/setactive " + m["id"]);
        else if (act == "add")
        {
            var src = (string)m["id"] == "ultimate" ? "e9a42b02-d5df-448d-aa00-03f14749eb61" : "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
            var o = Pc("/duplicatescheme " + src);
            var g = System.Text.RegularExpressions.Regex.Match(o, "[0-9a-f]{8}-[0-9a-f-]{27}").Value;
            if (g.Length > 0) Pc("/setactive " + g);
        }
        else if (act == "delete") Pc("/delete " + m["id"]);
        else if (act == "set")
        {
            var o = Opts.First(x => x[0] == (string)m["id"]);
            Pc("/setacvalueindex SCHEME_CURRENT " + o[3] + " " + o[4] + " " + ((bool)m["on"] ? o[5] : o[6]));
            Pc("/setactive SCHEME_CURRENT");
        }
        var plans = new List<object>();
        foreach (System.Text.RegularExpressions.Match x in System.Text.RegularExpressions.Regex.Matches(Pc("/list"), @"([0-9a-f]{8}-[0-9a-f-]{27})\s+\((.+?)\)(\s*\*)?"))
            plans.Add(new { id = x.Groups[1].Value, name = x.Groups[2].Value, active = x.Groups[3].Success && x.Groups[3].Value.Contains("*"), builtin = new[] { "381b4222-f694-41f0-9685-ff5bb260df2e", "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", "a1841308-3541-4fab-bc81-f71556f20b4a" }.Contains(x.Groups[1].Value) });
        var opts = new List<object>();
        foreach (var o in Opts)
        {
            if (o[7] == "laptop" && !Laptop) continue;
            var q = System.Text.RegularExpressions.Regex.Match(Pc("/q SCHEME_CURRENT " + o[3] + " " + o[4]), @"Current AC Power Setting Index:\s*0x([0-9a-f]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!q.Success) continue;
            opts.Add(new { id = o[0], name = o[1], desc = o[2], on = Convert.ToInt64(q.Groups[1].Value, 16).ToString() == o[5] });
        }
        return new { plans, opts, laptop = Laptop };
    }
}
