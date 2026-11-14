New-Item -ItemType Directory C:\ProgramData\WhatOS -Force | Out-Null
if((Test-Path C:\Windows\WhatOS\tweaks.json) -and -not (Test-Path C:\ProgramData\WhatOS\tweaks.json)){ Copy-Item C:\Windows\WhatOS\tweaks.json C:\ProgramData\WhatOS\ }
if(Test-Path 'C:\Program Files\WhatOS\WhatOS.exe'){
 $sh=(New-Object -ComObject WScript.Shell).CreateShortcut("$env:ProgramData\Microsoft\Windows\Start Menu\Programs\WhatOS.lnk"); $sh.TargetPath='C:\Program Files\WhatOS\WhatOS.exe'; $sh.Save()
 $a=New-ScheduledTaskAction -Execute powershell.exe -Argument '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File C:\Windows\WhatOS\update-check.ps1'
 $tr=@((New-ScheduledTaskTrigger -Daily -At 12:00), (New-ScheduledTaskTrigger -AtLogOn)); $tr[1].Delay='PT10M'
 Register-ScheduledTask -TaskName WhatOSUpdateCheck -Action $a -Trigger $tr -Principal (New-ScheduledTaskPrincipal -GroupId 'BUILTIN\Users') -Settings (New-ScheduledTaskSettingsSet -StartWhenAvailable) -Force | Out-Null
 "update check task $([bool](Get-ScheduledTask -TaskName WhatOSUpdateCheck -EA 0))" | Add-Content C:\whatos-post.txt
 if(Test-Path 'C:\Program Files\WhatOS\WhatOSRam.exe'){
  Register-ScheduledTask -TaskName WhatOSClearRAM -Action (New-ScheduledTaskAction -Execute 'C:\Program Files\WhatOS\WhatOSRam.exe') -Principal (New-ScheduledTaskPrincipal -GroupId 'BUILTIN\Administrators' -RunLevel Highest) -Settings (New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 2)) -Force | Out-Null
  $ts=New-Object -ComObject Schedule.Service; $ts.Connect(); $ts.GetFolder('\').GetTask('WhatOSClearRAM').SetSecurityDescriptor('D:(A;;FA;;;BA)(A;;FA;;;SY)(A;;GRGX;;;AU)',0)
  "clear ram task $([bool](Get-ScheduledTask -TaskName WhatOSClearRAM -EA 0))" | Add-Content C:\whatos-post.txt
 }
}
$wf='C:\Windows\Setup\Scripts\wifi'
if(Test-Path $wf){ Get-ChildItem $wf -Filter *.xml | % { netsh wlan add profile filename="$($_.FullName)" user=all | Out-Null }; Remove-Item $wf -Recurse -Force }
$p=Get-Partition | Where-Object { (Get-Volume -Partition $_ -EA 0).FileSystemLabel -eq "WhatOS Installer" };
if($p){ $p | Remove-Partition -Confirm:$false };
$m=(Get-PartitionSupportedSize -DriveLetter C).SizeMax;
Resize-Partition -DriveLetter C -Size $m -EA 0;
foreach($b in (bcdedit /enum all | Out-String) -split "`r?`n`r?`n"){ if($b -match "WhatOS Installer"){ $g=[regex]::Match($b,"\{[0-9a-f-]+\}").Value;
if($g){ bcdedit /delete $g /f } } };
"done $(Get-Date)" | Set-Content C:\whatos-post.txt
Get-Partition | Format-Table PartitionNumber,DriveLetter,Size,Type -a | Out-String | Add-Content C:\whatos-post.txt
bcdedit /enum all | Select-String description | Out-String | Add-Content C:\whatos-post.txt
if ((Test-Path C:\Windows\WhatOS\updates.ps1) -and -not (Test-Path C:\Users\Public\WhatOS\wu.done) -and -not (Get-ScheduledTask -TaskName WhatOSUpdate -EA 0)) {
    New-Item -ItemType Directory C:\Users\Public\WhatOS -Force | Out-Null
    icacls C:\Users\Public\WhatOS /grant '*S-1-5-32-545:(OI)(CI)M' | Out-Null
    $a = New-ScheduledTaskAction -Execute powershell.exe -Argument '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File C:\Windows\WhatOS\updates.ps1'
    Register-ScheduledTask -TaskName WhatOSUpdate -Action $a -Trigger (New-ScheduledTaskTrigger -AtLogOn) -User 'NT AUTHORITY\SYSTEM' -RunLevel Highest -Force | Out-Null
    "update task $([bool](Get-ScheduledTask -TaskName WhatOSUpdate -EA 0))" | Add-Content C:\whatos-post.txt
}
if ((Test-Path C:\Windows\WhatOS\apps.txt) -and -not (Test-Path C:\Users\Public\WhatOS\apps.done) -and -not (Get-ScheduledTask -TaskName WhatOSApps -EA 0)) {
    New-Item -ItemType Directory C:\Users\Public\WhatOS -Force | Out-Null
    icacls C:\Users\Public\WhatOS /grant '*S-1-5-32-545:(OI)(CI)M' | Out-Null
    $a = New-ScheduledTaskAction -Execute powershell.exe -Argument '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File C:\Windows\WhatOS\apps.ps1 -Mode admin'
    $p = New-ScheduledTaskPrincipal -GroupId 'BUILTIN\Administrators' -RunLevel Highest
    Register-ScheduledTask -TaskName WhatOSApps -Action $a -Trigger (New-ScheduledTaskTrigger -AtLogOn) -Principal $p -Force | Out-Null
    "apps task $([bool](Get-ScheduledTask -TaskName WhatOSApps -EA 0))" | Add-Content C:\whatos-post.txt
}
if ((Test-Path C:\Windows\WhatOS\edge.want) -and -not (Test-Path C:\Windows\WhatOS\edge.done)) {
    "edge from setupcomplete" | Add-Content C:\whatos-post.txt
    & powershell -NoProfile -ExecutionPolicy Bypass -File C:\Windows\WhatOS\edge.ps1
    "edge done $(Test-Path C:\Windows\WhatOS\edge.done)" | Add-Content C:\whatos-post.txt
}
if ((Test-Path C:\Windows\WhatOS\ssh\OpenSSH-Win64.zip) -and -not (Get-Service sshd -EA 0)) {
    Expand-Archive C:\Windows\WhatOS\ssh\OpenSSH-Win64.zip 'C:\Program Files' -Force
    & 'C:\Program Files\OpenSSH-Win64\install-sshd.ps1' | Out-Null
    Start-Service sshd -EA 0
    if (Test-Path C:\Windows\WhatOS\ssh\authorized_keys) {
        Copy-Item C:\Windows\WhatOS\ssh\authorized_keys C:\ProgramData\ssh\administrators_authorized_keys -Force
        icacls C:\ProgramData\ssh\administrators_authorized_keys /inheritance:r /grant '*S-1-5-32-544:F' /grant '*S-1-5-18:F' | Out-Null
    }
    New-NetFirewallRule -Name WhatOSssh -DisplayName 'WhatOS test ssh' -Protocol TCP -LocalPort 22 -Action Allow -EA 0 | Out-Null
    Set-Service sshd -StartupType Automatic
    Restart-Service sshd -EA 0
    "sshd $((Get-Service sshd -EA 0).Status)" | Add-Content C:\whatos-post.txt
}
