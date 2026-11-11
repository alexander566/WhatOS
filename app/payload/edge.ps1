$d='C:\Windows\WhatOS'; $l="$d\edge.log"
function L($m){ "$(Get-Date -f HH:mm:ss) $m" | Add-Content $l }
L "start $(whoami)"
$wv0="${env:ProgramFiles(x86)}\Microsoft\EdgeWebView\Application"; $n=Test-Path "$wv0\*\msedgewebview2.exe"; for($i=0;$i -lt 180 -and -not $n;$i++){ $n=(Test-NetConnection 1.1.1.1 -Port 443 -WarningAction 0 -InformationLevel Quiet); if(-not $n){Start-Sleep 5} }
L "net $n"
$wv="${env:ProgramFiles(x86)}\Microsoft\EdgeWebView\Application"
foreach($v in "edgeupdate","edgeupdatem"){ Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\$v" Start 3 -EA 0 }
if($n -and -not (Test-Path "$wv\*\msedgewebview2.exe")){
  $b="$env:TEMP\wv2.exe"
  try{ Invoke-WebRequest 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $b -UseBasicParsing; L "wv2 dl $((Get-Item $b).Length)"; Start-Process $b '/silent /install' -Wait; L "wv2 installer done" }catch{ L "wv2 dl fail $_" }
}
$ok=Test-Path "$wv\*\msedgewebview2.exe"
L "webview2 standalone $ok"
if($ok){
  New-Item 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdateDev' -Force | Out-Null
  Set-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdateDev' AllowUninstall ''
  $j='C:\Windows\System32\IntegratedServicesRegionPolicySet.json'
  takeown /f $j | Out-Null; icacls $j /grant '*S-1-5-18:F' | Out-Null
  $t=Get-Content $j -Raw; $o=$t | ConvertFrom-Json
  foreach($q in $o.policies){ if($q.guid -eq '{1bca278a-5d11-4acf-ad2f-f9ab6d7f93a6}'){ $q.defaultState='enabled' } }
  $o | ConvertTo-Json -Depth 20 | Set-Content $j -Encoding utf8; L "region policy edge-uninstallable enabled"
  $s=Get-ChildItem "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\*\Installer\setup.exe" -EA 0 | Select-Object -First 1
  if($s){
    $dr='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion'; $old=(Get-ItemProperty $dr -EA 0).DeviceRegion
    if(Test-Path $dr){ Set-ItemProperty $dr DeviceRegion 177 -Type DWord; L "device region $old -> 177 (NO) for uninstall" }
    $g='HKCU:\Control Panel\International\Geo'; $gn=(Get-ItemProperty $g -EA 0).Name; $gi=(Get-ItemProperty $g -EA 0).Nation; New-Item $g -Force | Out-Null; Set-ItemProperty $g Name 'NO'; Set-ItemProperty $g Nation '177'; L "geo $gn/$gi -> NO"
    $ss="$d\SystemSettings.exe"; Copy-Item "$env:windir\System32\cmd.exe" $ss -Force
    $a='/c ""'+$s.FullName+'" --uninstall --msedge --channel=stable --system-level --verbose-logging --force-uninstall"'
    $r=Start-Process $ss $a -Wait -PassThru -WindowStyle Hidden; L "edge uninstall exit $($r.ExitCode) (parent SystemSettings.exe)"
    Remove-Item $ss -Force -EA 0
    if($gn){ Set-ItemProperty $g Name $gn; Set-ItemProperty $g Nation $gi }
    if($null -ne $old){ Set-ItemProperty $dr DeviceRegion $old -Type DWord; L "device region restored $old" }
  } else { L "edge setup.exe not found" }
  $pol='HKLM:\SOFTWARE\Policies\Microsoft\EdgeUpdate'; New-Item $pol -Force | Out-Null
  Set-ItemProperty $pol 'Install{56EB18F8-B008-4CBD-B6D2-8C97FE7E9062}' 0 -Type DWord
  Set-ItemProperty $pol 'Install{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}' 1 -Type DWord
  Set-ItemProperty $pol 'Update{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}' 1 -Type DWord
  New-Item 'HKLM:\SOFTWARE\Microsoft\EdgeUpdate' -Force | Out-Null
  Set-ItemProperty 'HKLM:\SOFTWARE\Microsoft\EdgeUpdate' DoNotUpdateToEdgeWithChromium 1 -Type DWord
  Remove-Item 'C:\Users\Public\Desktop\Microsoft Edge.lnk','C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Microsoft Edge.lnk' -Force -EA 0
  $ea="${env:ProgramFiles(x86)}\Microsoft\Edge"
  if(-not (Test-Path "$ea\Application\msedge.exe") -and (Test-Path $ea)){
    Get-Process pwahelper,msedge -EA 0 | Stop-Process -Force -EA 0
    takeown /f $ea /r /d y 2>&1 | Out-Null; icacls $ea /grant '*S-1-5-32-544:F' /t /c /q 2>&1 | Out-Null
    Remove-Item $ea -Recurse -Force -EA 0; L "edge leftovers removed $(-not (Test-Path $ea))"
  }
  Get-ChildItem 'C:\Users\*\AppData\Roaming\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\Microsoft Edge.lnk','C:\Users\*\Desktop\Microsoft Edge.lnk' -EA 0 | Remove-Item -Force -EA 0
  L "edge app left $(Test-Path "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe") webview2 still $(Test-Path "$wv\*\msedgewebview2.exe")"
}
foreach($v in "edgeupdate","edgeupdatem"){ Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\$v" Start 4 -EA 0 }
Set-Content "$d\edge.done" (Get-Date)
schtasks /delete /tn WhatOSEdge /f | Out-Null
L "end"
