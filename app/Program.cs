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

        static bool SelfUpdate(string[] args) => Task.Run(() => SelfUpdateCore(args)).Result;

        static bool SelfUpdateCore(string[] args)
        {
            var exe = Application.ExecutablePath;
            try { File.Delete(exe + ".old"); } catch { }
            var cur = Assembly.GetExecutingAssembly().GetName().Version;
            if (cur.Major == 0 && cur.Minor == 1 && cur.Build == 0) return false;
            try
            {
                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                var rq = (System.Net.HttpWebRequest)System.Net.WebRequest.Create("https://api.github.com/repos/alexander566/WhatOS/releases/latest");
                rq.UserAgent = "WhatOS"; rq.Timeout = 5000;
                string body;
                using (var rs = rq.GetResponse()) using (var sr = new StreamReader(rs.GetResponseStream())) body = sr.ReadToEnd();
                var d = (Dictionary<string, object>)new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(body);
                if (!Version.TryParse(((string)d["tag_name"]).TrimStart('v'), out var latest) || latest <= new Version(cur.Major, cur.Minor, cur.Build)) return false;
                string url = null;
                foreach (Dictionary<string, object> x in (object[])d["assets"]) if ((string)x["name"] == "WhatOS.exe") url = (string)x["browser_download_url"];
                if (url == null) return false;
                using (var w = new System.Net.WebClient()) { w.Headers.Add("User-Agent", "WhatOS"); w.DownloadFile(url, exe + ".new"); }
                if (new FileInfo(exe + ".new").Length < 500000) { File.Delete(exe + ".new"); return false; }
                File.Move(exe, exe + ".old");
                File.Move(exe + ".new", exe);
                Process.Start(new ProcessStartInfo(exe, string.Join(" ", args.Select(x => "\"" + x + "\""))) { UseShellExecute = false });
                return true;
            }
            catch { try { if (!File.Exists(exe) && File.Exists(exe + ".old")) File.Move(exe + ".old", exe); } catch { } return false; }
        }

        [STAThread]
        static void Main(string[] args)
        {
            var a = string.Join(" ", args).ToLowerInvariant();
            StartTab = a.Contains("updates") ? 1 : a.Contains("--uninstall") ? 3 : 0;
            Directory.CreateDirectory(Work);
            if (SelfUpdate(args)) return;
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
            using (var f = File.Create(path))
                r.CopyTo(f);
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

        protected override void WndProc(ref Message m)
        {
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
            get { var p = base.CreateParams; p.ClassStyle |= 0x20000; p.Style |= 0x20000; return p; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int v = 2;
            try { DwmSetWindowAttribute(Handle, 33, ref v, 4); } catch { }
        }

        public MainForm()
        {
            Text = "WhatOS Installer";
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(18, 24, 33);
            ClientSize = new Size(1000, 640);
            MinimumSize = new Size(860, 560);
            Padding = new Padding(5);
            Resize += (s, e) => Padding = new Padding(WindowState == FormWindowState.Maximized ? 0 : 5);
            StartPosition = FormStartPosition.CenterScreen;
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
                    case "manage": { var a = m["action"] as string; var id = m.ContainsKey("id") ? m["id"] as string : ""; Task.Run(() => { var o = Manage("-Action " + a + " -Id \"" + id + "\""); Send(o != null ? (object)new { type = "manage", data = o } : new { type = "manage", err = "WhatOS could not change this setting." }); }); break; }
                    case "updcheck": Task.Run(() => { try { var rf = Path.Combine(Program.Work, "remote.json"); using (var w = new System.Net.WebClient()) { System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12; w.DownloadFile(ManifestUrl, rf); } var o = Manage("-Action diff -Manifest \"" + rf + "\""); Send(new { type = "diff", data = o }); } catch (Exception ex) { Send(new { type = "diff", err = "Could not check for updates: " + ex.Message }); } }); break;
                    case "updapply": Task.Run(() => { var o = Manage("-Action update -Manifest \"" + Path.Combine(Program.Work, "remote.json") + "\""); Send(new { type = "updated", data = o }); }); break;
                    case "uninstall": Task.Run(() => { Manage("-Action restore"); Ps(@"
Unregister-ScheduledTask -TaskName WhatOSUpdateCheck -Confirm:$false -EA 0
Remove-Item 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WhatOS' -Recurse -Force -EA 0
Remove-Item 'HKLM:\SOFTWARE\Classes\whatos', 'HKLM:\SOFTWARE\Classes\AppUserModelId\WhatOS', 'HKLM:\SOFTWARE\Classes\DesktopBackground\Shell\WhatOSClearRAM' -Recurse -Force -EA 0
Unregister-ScheduledTask -TaskName WhatOSClearRAM -Confirm:$false -EA 0
Remove-Item ""$env:ProgramData\Microsoft\Windows\Start Menu\Programs\WhatOS.lnk"" -Force -EA 0
Remove-Item 'C:\ProgramData\WhatOS', 'C:\Windows\WhatOS' -Recurse -Force -EA 0
Start-Process cmd -ArgumentList '/c ping -n 4 127.0.0.1 >nul & rd /s /q ""C:\Program Files\WhatOS""' -WindowStyle Hidden
"); Send(new { type = "uninstalled" }); }); break;
                    case "reboot": Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false }); break;
                    case "close": Close(); break;
                    case "min": WindowState = FormWindowState.Minimized; break;
                    case "max": MaximizedBounds = Screen.FromControl(this).WorkingArea; WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; break;
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
