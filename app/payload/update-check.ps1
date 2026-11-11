$ErrorActionPreference = 'SilentlyContinue'
$d = 'C:\ProgramData\WhatOS'
$cur = 0
if (Test-Path "$d\state.json") { $cur = [int](Get-Content "$d\state.json" -Raw | ConvertFrom-Json).version }
if (-not $cur -and (Test-Path "$d\tweaks.json")) { $cur = [int](Get-Content "$d\tweaks.json" -Raw | ConvertFrom-Json).version }
[Net.ServicePointManager]::SecurityProtocol = 'Tls12'
try { $r = Invoke-RestMethod 'https://raw.githubusercontent.com/alexander566/WhatOS/main/app/payload/tweaks.json' -TimeoutSec 20 } catch { exit }
if ([int]$r.version -le $cur) { exit }
if (Test-Path "$d\toast-$($r.version).txt") { exit }
Add-Type -Namespace W -Name N -MemberDefinition '[DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int s);'
$s = 0; [void][W.N]::SHQueryUserNotificationState([ref]$s)
if ($s -ne 5) { exit }
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null
$x = New-Object Windows.Data.Xml.Dom.XmlDocument
$x.LoadXml("<toast activationType=`"protocol`" launch=`"whatos:updates`"><visual><binding template=`"ToastGeneric`"><text>WhatOS update</text><text>New tweaks are ready. Your own changes stay as they are.</text></binding></visual><actions><action content=`"Update`" activationType=`"protocol`" arguments=`"whatos:updates`"/><action content=`"Later`" activationType=`"system`" arguments=`"dismiss`"/></actions></toast>")
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe').Show([Windows.UI.Notifications.ToastNotification]::new($x))
Set-Content "$env:LOCALAPPDATA\Temp\whatos-toast.txt" (Get-Date)
New-Item -ItemType File "$d\toast-$($r.version).txt" -Force | Out-Null
