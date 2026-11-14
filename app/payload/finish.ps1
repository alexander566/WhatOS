param([string]$Action)
$v = Get-Volume | ? { $_.FileSystemLabel -eq 'WhatOS Installer' } | select -First 1
if (-not $v) { Write-Output '@error|No WhatOS installer found on this PC.'; exit 1 }
$W = "$($v.DriveLetter):"
$g = (Get-Content "$W\whatos\bcd.id" -EA 0 | select -First 1)
if ($Action -eq 'restart') {
    bcdedit /bootsequence $g | Out-Null
    try { if ((Get-BitLockerVolume -MountPoint C: -EA Stop).ProtectionStatus -eq 'On') { Suspend-BitLocker -MountPoint C: -RebootCount 1 | Out-Null } } catch {}
    shutdown /r /t 3 /c 'WhatOS: restarting into the installer'
    Write-Output '@done|restart'
    exit 0
}
foreach ($b in (bcdedit /enum all | Out-String) -split "`r?`n`r?`n") {
    if ($b -match 'WhatOS Installer') { $id = [regex]::Match($b, '\{[0-9a-f-]+\}').Value; if ($id) { bcdedit /delete $id /f | Out-Null } }
}
Get-Partition -DriveLetter $v.DriveLetter | Remove-Partition -Confirm:$false
$m = (Get-PartitionSupportedSize -DriveLetter C).SizeMax
Resize-Partition -DriveLetter C -Size $m -EA 0
Write-Output '@done|cancel'
