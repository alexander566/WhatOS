if($env:USERNAME -like 'defaultuser*'){ exit }
$d='C:\Windows\WhatOS'; $st='C:\Users\Public\WhatOS'
New-Item -ItemType Directory $st -Force -EA 0 | Out-Null
$l="$st\prep.log"; "prep start $(whoami) $(Get-Date)" | Add-Content $l
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$bg=[Drawing.Color]::FromArgb(11,15,20); $ink=[Drawing.Color]::FromArgb(233,238,245); $mut=[Drawing.Color]::FromArgb(134,150,171)
$acc=[Drawing.Color]::FromArgb(76,141,255); $ok=[Drawing.Color]::FromArgb(63,203,138); $line=[Drawing.Color]::FromArgb(38,49,66)
$f=New-Object Windows.Forms.Form; $f.FormBorderStyle='None'; $f.WindowState='Maximized'; $f.TopMost=$true; $f.BackColor=$bg; $f.ShowInTaskbar=$false
$sw=[Windows.Forms.Screen]::PrimaryScreen.Bounds.Width; $sh=[Windows.Forms.Screen]::PrimaryScreen.Bounds.Height; $k=[math]::Max(0.8,$sh/900)
function F($sz,$b){ New-Object Drawing.Font($(if($b){'Segoe UI Semibold'}else{'Segoe UI'}),[single]($sz*$k),[Drawing.FontStyle]::Regular,[Drawing.GraphicsUnit]::Pixel) }
function Lb($txt,$x,$y,$font,$col){ $o=New-Object Windows.Forms.Label; $o.Text=$txt; $o.ForeColor=$col; $o.BackColor=$bg; $o.Font=$font; $o.AutoSize=$true; $o.Location=New-Object Drawing.Point([int]$x,[int]$y); $f.Controls.Add($o); $o }
if(Test-Path "$d\logo.png"){ $pb=New-Object Windows.Forms.PictureBox; $pb.Image=[Drawing.Image]::FromFile("$d\logo.png"); $pb.SizeMode='Zoom'; $pb.Size=New-Object Drawing.Size([int](28*$k),[int](28*$k)); $pb.Location=New-Object Drawing.Point([int](28*$k),[int](28*$k)); $f.Controls.Add($pb) }
[void](Lb 'WhatOS' (62*$k) (29*$k) (F 15 1) $ink)
$cw=560*$k; $cx=($sw-$cw)/2; $y=$sh/2-190*$k
[void](Lb 'Getting your PC ready' $cx $y (F 30 1) $ink)
[void](Lb 'This runs once. Your PC restarts when it is done.' ($cx+2) ($y+52*$k) (F 16 0) $mut)
$steps=@('Connect to the internet')
if(Test-Path "$d\edge.want"){ $steps+='Remove Edge' }
if(Test-Path "$d\apps.txt"){ $steps+='Install your apps' }
$steps+='Finish'
$dots=@(); $names=@(); $y+=110*$k
foreach($s in $steps){ $dots+=Lb ([char]0x25CF) $cx ($y-2*$k) (F 18 0) $line; $names+=Lb $s ($cx+34*$k) $y (F 18 0) $mut; $y+=46*$k }
$status=Lb '' ($cx+34*$k) ($y+4*$k) (F 15 0) $mut
[void](Lb "Don't turn off the PC" (28*$k) ($sh-48*$k) (F 14 0) $mut)
Add-Type -Namespace W -Name E -MemberDefinition '[DllImport("kernel32.dll",SetLastError=true)] public static extern System.IntPtr OpenEvent(uint a,bool i,string n); [DllImport("kernel32.dll")] public static extern bool SetEvent(System.IntPtr h);'
$h=[W.E]::OpenEvent(2,$false,'ShellDesktopSwitchEvent'); "switchevent $h $([W.E]::SetEvent($h))" | Add-Content $l
$f.Show(); $f.Activate()
function Pump($ms){ $t=[Environment]::TickCount; do { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 100 } while([Environment]::TickCount-$t -lt $ms) }
function Step($name){ $i=[array]::IndexOf($steps,$name); for($j=0;$j -lt $steps.Count;$j++){ if($j -lt $i){ $dots[$j].ForeColor=$ok; $names[$j].ForeColor=$ink } elseif($j -eq $i){ $dots[$j].ForeColor=$acc; $names[$j].ForeColor=$ink } }; $status.Text=''; "step $name" | Add-Content $l; Pump 50 }
function Say($m){ if($status.Text -ne $m){ $status.Text=$m; "$m" | Add-Content $l }; Pump 50 }

Step 'Connect to the internet'
$n=$false; for($i=0;$i -lt 60 -and -not $n;$i++){ $n=(Test-NetConnection 1.1.1.1 -Port 443 -WarningAction 0).TcpTestSucceeded; if(-not $n){ Say 'Waiting for a network connection. Plug in a cable or connect to Wi-Fi.'; Pump 5000 } }
"net $n" | Add-Content $l
if(Test-Path "$d\edge.want"){
 Step 'Remove Edge'
 for($i=0;$i -lt 180 -and -not (Test-Path "$d\edge.done");$i++){ Pump 5000 }
 "edge done $(Test-Path "$d\edge.done")" | Add-Content $l
}
if(Test-Path "$d\apps.txt"){
 Step 'Install your apps'
 for($i=0;$i -lt 2400 -and -not (Test-Path "$st\apps.done");$i++){ $m=Get-Content "$st\apps.status" -EA 0 | select -First 1; if($m){ Say $m }; Pump 1000 }
 "apps admin done $(Test-Path "$st\apps.done")" | Add-Content $l
 if((Get-Content "$d\apps.txt") -match '\|user\|'){
  $u=Start-Process powershell -ArgumentList "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File $d\apps.ps1 -Mode user" -PassThru
  while(-not $u.HasExited){ $m=Get-Content "$st\apps.status" -EA 0 | select -First 1; if($m){ Say $m }; Pump 1000 }
  "apps user done" | Add-Content $l
 }
}
Step 'Finish'
Say 'Restarting'
$o="$st\verify.txt"
"procs $((Get-Process).Count) svchost $((Get-Process svchost).Count) svcrun $((Get-Service|?{$_.Status -eq 'Running'}).Count) ramMB $([int]((Get-CimInstance Win32_OperatingSystem|%{$_.TotalVisibleMemorySize-$_.FreePhysicalMemory})/1KB))" | Set-Content $o
"defdir $(Test-Path 'C:\Program Files\Windows Defender') edge $(Test-Path "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe") webview2 $(Test-Path "${env:ProgramFiles(x86)}\Microsoft\EdgeWebView")" | Add-Content $o
Get-Content "$st\apps-*.log" -EA 0 | Add-Content $o
if(Test-Path "$d\report.url"){ try { $u=(Get-Content "$d\report.url" | select -First 1).Trim(); $b=(Get-Content $o, $l, "$st\apps-*.log", "$d\edge.log", C:\whatos-post.txt -EA 0 | Out-String); Invoke-WebRequest $u -Method Post -Body $b -UseBasicParsing -TimeoutSec 20 | Out-Null } catch {} }
Pump 2000
shutdown /r /t 3
