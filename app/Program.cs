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

        [STAThread]
        static void Main()
        {
            Directory.CreateDirectory(Work);
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
                    case "info": Task.Run(() => { try { var lp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WhatOS", "last.json"); if (File.Exists(lp)) Send(new { type = "last", picks = json.DeserializeObject(File.ReadAllText(lp)) }); Send(new { type = "info", info = Info() }); } catch (Exception ex) { Send(new { type = "error", msg = "Could not read this PC: " + ex.Message }); } }); break;
                    case "browseIso": BrowseIso(); break;
                    case "prepare": Prepare(m["picks"] as Dictionary<string, object>); break;
                    case "restart": RunPs("finish.ps1", "-Action restart", false); break;
                    case "cancel": RunPs("finish.ps1", "-Action cancel", true); break;
                    case "close": Close(); break;
                    case "min": WindowState = FormWindowState.Minimized; break;
                    case "drag": ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); break;
                }
            }
            catch (Exception ex)
            {
                Send(new { type = "error", msg = ex.Message });
            }
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
 cSize=$c.Size; cFree=$v.SizeRemaining; cMin=$max
 bitlocker=$bl; parts=$parts; usbs=$usbs
} | ConvertTo-Json -Compress -Depth 4");
            return json.DeserializeObject(o);
        }

        void BrowseIso()
        {
            using (var d = new OpenFileDialog { Filter = "Windows ISO (*.iso)|*.iso", Title = "Pick a Windows 11 ISO" })
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
