param([string]$Payload)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
function P($i, $pct, $msg) { Write-Output "@step|$i|$pct|$msg" }
function Fail($msg) {
    L "FAIL $msg"
    try {
        if ($script:m -and (Test-Path "$script:m\Windows")) { dism /Unmount-Wim /MountDir:$script:m /Discard /Quiet | Out-Null }
        if ($script:np) {
            Get-Partition -DiskNumber $script:np.DiskNumber -PartitionNumber $script:np.PartitionNumber -EA 0 | Remove-Partition -Confirm:$false -EA 0
            Resize-Partition -DriveLetter C -Size (Get-PartitionSupportedSize -DriveLetter C).SizeMax -EA 0
            L 'rolled back installer partition'
        }
        if ($picks.iso) { Dismount-DiskImage -ImagePath $picks.iso -EA 0 | Out-Null }
    } catch { L "rollback: $($_.Exception.Message)" }
    Write-Output "@error|$msg"; exit 1
}
trap { Fail "Unexpected error: $($_.Exception.Message)" }
function L($m) { Write-Output "$(Get-Date -f HH:mm:ss) $m" }

$picks = Get-Content "$Payload\picks.json" -Raw | ConvertFrom-Json
$whatos = $picks.win -eq 'whatos'
$dl = $whatos -and -not $picks.iso
$usb = $picks.method -eq 'usb'
$SA = if ($dl) { 4 } else { 3 }
L "picks $($picks | ConvertTo-Json -Compress -Depth 5)"

function Get-File($url, $dst, $size, $step) {
    $buf = New-Object byte[] (1MB); $last = -1
    for ($try = 0; $try -lt 20; $try++) {
        $have = if (Test-Path $dst) { (Get-Item $dst).Length } else { 0 }
        if ($have -ge $size) { return }
        try {
            $rq = [Net.HttpWebRequest]::Create($url); $rq.Timeout = 30000; $rq.ReadWriteTimeout = 60000
            if ($have) { $rq.AddRange([long]$have) }
            $rs = $rq.GetResponse(); $in = $rs.GetResponseStream()
            $out = [IO.File]::Open($dst, [IO.FileMode]::Append)
            $t0 = Get-Date; $b0 = $have
            try {
                while (($n = $in.Read($buf, 0, $buf.Length)) -gt 0) {
                    $out.Write($buf, 0, $n); $have += $n
                    $pct = [int](100 * $have / $size)
                    if ($pct -ne $last) {
                        $last = $pct; $sec = ((Get-Date) - $t0).TotalSeconds
                        $mbs = if ($sec -gt 1) { ($have - $b0) / 1MB / $sec } else { 0 }
                        P $step $pct ("Downloading Windows - {0:N1} of {1:N1} GB - {2:N0} MB/s" -f ($have / 1GB), ($size / 1GB), $mbs)
                    }
                }
            } finally { $out.Close(); $in.Close(); $rs.Close() }
        } catch { L "download try $try failed: $($_.Exception.Message)"; Start-Sleep 5 }
    }
    Fail 'The download kept failing. Check the internet connection and try again.'
}

P 0 0 'Checking this PC'
if ($usb) {
    $ud = Get-Disk -Number ([int]$picks.usb) -EA 0
    if (-not $ud -or $ud.BusType -ne 'USB') { Fail 'The USB stick was not found. Plug it in and try again.' }
    if ($ud.IsBoot -or $ud.IsSystem) { Fail 'That USB drive runs this Windows. Pick another one.' }
} else {
    if ((Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')) { Fail 'Windows is waiting to finish an update. Restart your PC first, then open WhatOS again.' }
    $bios = $env:firmware_type -ne 'UEFI'
    $c = Get-Partition -DriveLetter C
    $disk = Get-Disk -Number $c.DiskNumber
    if ($bios) {
        if ($disk.PartitionStyle -ne 'MBR') { Fail 'This PC starts in legacy BIOS mode but the Windows disk is not MBR. Use a USB stick instead.' }
        $efi = Get-Partition -DiskNumber $disk.Number | ? { $_.IsActive } | select -First 1
        if (-not $efi) { Fail 'No active system partition found on the Windows disk.' }
        if ($efi.DiskNumber -ne $c.DiskNumber) { Fail 'Windows boots from another disk. Use a USB stick instead.' }
        if (@(Get-Partition -DiskNumber $disk.Number).Count -ge 4) { Fail 'The Windows disk already has 4 partitions. Use a USB stick instead.' }
    } else {
        if ($disk.PartitionStyle -ne 'GPT') { Fail 'The Windows disk is not GPT. Use a USB stick instead.' }
        $efi = Get-Partition -DiskNumber $disk.Number | ? { $_.GptType -eq '{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}' } | select -First 1
        if (-not $efi) { Fail 'No EFI system partition found on the Windows disk.' }
    }
    $old = Get-Volume | ? { $_.FileSystemLabel -eq 'WhatOS Installer' }
    if ($old) {
        L 'old installer found, removing it'
        & powershell -NoProfile -ExecutionPolicy Bypass -File "$Payload\finish.ps1" -Action cancel | Out-Null
        if (Get-Volume | ? { $_.FileSystemLabel -eq 'WhatOS Installer' }) { Fail 'An old WhatOS installer is on this PC and could not be removed.' }
    }
}
if ($dl) {
    P 0 30 'Finding the latest Windows 11'
    $cab = "$Payload\products.cab"
    try { (New-Object Net.WebClient).DownloadFile('https://go.microsoft.com/fwlink/?LinkId=2156292', $cab) } catch { Fail 'Could not reach Microsoft. Check the internet connection.' }
    tar.exe -xf $cab -C "$Payload" products.xml 2>&1 | Out-Null
    [xml]$px = Get-Content "$Payload\products.xml" -Raw
    $esd = $px.SelectNodes('//File') | ? { $_.LanguageCode -eq 'en-us' -and $_.Architecture -eq 'x64' -and $_.Edition -eq 'Professional' -and $_.FileName -match 'CLIENTCONSUMER' } | select -First 1
    if (-not $esd) { Fail 'Microsoft did not list a Windows 11 download.' }
    $esdSize = [long]$esd.Size
    L "esd $($esd.FileName) $esdSize sha1 $($esd.Sha1)"
    $need = $esdSize + 1536MB
} else {
    if (-not (Test-Path $picks.iso)) { Fail 'The ISO file was not found.' }
    $need = (Get-Item $picks.iso).Length + 1GB
}
P 0 60 "Saving this PC's drivers"
$drv = "$env:TEMP\whatos-drivers"
Remove-Item $drv -Recurse -Force -EA 0
New-Item -ItemType Directory "$drv\d", "$drv\wifi" -Force | Out-Null
try { Export-WindowsDriver -Online -Destination "$drv\d" -EA Stop | Out-Null } catch { L "driver export: $($_.Exception.Message)" }
netsh wlan export profile key=clear folder="$drv\wifi" 2>&1 | Out-Null
Set-Content "$drv\uuid.txt" (Get-CimInstance Win32_ComputerSystemProduct).UUID -Encoding ascii
$drvSize = [long](Get-ChildItem $drv -Recurse -File -EA 0 | Measure-Object Length -Sum).Sum
L "drivers $([math]::Round($drvSize / 1MB)) MB, wifi profiles $(@(Get-ChildItem "$drv\wifi" -EA 0).Count), uuid $(Get-Content "$drv\uuid.txt")"
$need += $drvSize + 64MB
if ($usb) {
    if ($ud.Size -lt $need + 2.5GB) { Fail ("The USB stick is too small. It needs {0:N0} GB or more." -f (($need + 2.5GB) / 1GB)) }
    L "usb disk $($ud.Number) $($ud.FriendlyName) $($ud.Size)"
    P 0 100 'Ready'
    P 1 0 'Erasing the USB stick'
    Get-Volume | ? { $_.FileSystemLabel -in 'WHATOSBOOT', 'WHATOSUSB' } | % { L "old usb label on $($_.DriveLetter):" }
    $used = @([IO.DriveInfo]::GetDrives() | % { $_.Name[0] })
    $free = @([char[]]'RSUVYQPONMLKJ' | ? { $used -notcontains $_ })
    $B = "$($free[0]):"; $W = "$($free[1]):"
    @("select disk $($ud.Number)", 'clean', 'convert mbr', 'create partition primary size=2048', 'format fs=fat32 quick label=WHATOSBOOT', 'active', "assign letter=$($free[0])", 'create partition primary', 'format fs=ntfs quick label=WHATOSUSB', "assign letter=$($free[1])") | Set-Content "$Payload\usb.txt" -Encoding ascii
    P 1 30 'Making the USB parts'
    $ErrorActionPreference = 'Continue'
    $dp = diskpart /s "$Payload\usb.txt" 2>&1 | Out-String
    $ErrorActionPreference = 'Stop'
    L ($dp -split "`n" | ? { $_ -match 'successfully|rror' } | % { $_.Trim() }) -join ' / '
    Start-Sleep 3
    if (-not (Test-Path "$B\") -or -not (Test-Path "$W\")) { Fail 'Windows did not give the USB parts a drive letter. Unplug the stick, plug it back in and try again.' }
    L "usb boot $B data $W"
    P 1 100 "USB stick ready ($B $W)"
} else {
    $sup = Get-PartitionSupportedSize -DriveLetter C
    if ($c.Size - $sup.SizeMin -lt $need + 2GB) {
        if ((Get-Volume C).SizeRemaining -gt $need + 2GB) { Fail ("Windows can only make {0:N1} GB free on C: right now because of files it can't move (restore points, page file). Restart the PC and try again, or use a USB stick." -f (($c.Size - $sup.SizeMin) / 1GB)) }
        Fail ("Not enough free space on C:. Free up {0:N1} GB and try again." -f (($need + 2GB - ($c.Size - $sup.SizeMin)) / 1GB))
    }
    $serial = (Get-PhysicalDisk | ? { $_.DeviceId -eq "$($disk.Number)" }).SerialNumber
    if (-not $serial) { $serial = $disk.SerialNumber }
    L "disk $($disk.Number) serial '$serial' C part $($c.PartitionNumber) efi $($efi.PartitionNumber)"
    P 0 100 'This PC is ready'
    P 1 0 'Shrinking C:'
    $size = [math]::Ceiling($need / 1MB) * 1MB
    Resize-Partition -DriveLetter C -Size ($c.Size - $size)
    P 1 60 'Making the installer partition'
    $script:np = $np = New-Partition -DiskNumber $disk.Number -UseMaximumSize -AssignDriveLetter
    $np | Format-Volume -FileSystem NTFS -NewFileSystemLabel 'WhatOS Installer' -Confirm:$false | Out-Null
    $np = Get-Partition -DiskNumber $disk.Number -PartitionNumber $np.PartitionNumber
    $W = "$($np.DriveLetter):"
    L "installer $W part $($np.PartitionNumber)"
    P 1 100 "Installer space ready ($W)"
}

if ($dl) {
    New-Item -ItemType Directory "$W\sources" -Force | Out-Null
    $img = "$W\sources\install.esd"
    Get-File $esd.FilePath $img $esdSize 2
    P 2 100 'Checking the download'
    $h = (Get-FileHash $img -Algorithm SHA1).Hash
    if ($esd.Sha1 -and $h -ne $esd.Sha1.ToUpper()) { Remove-Item $img -Force; Fail 'The download was damaged. Try again.' }
    P 2 100 'Windows downloaded'
    P 3 5 'Unpacking setup files'
    $ErrorActionPreference = 'Continue'
    dism /Apply-Image /ImageFile:$img /Index:1 /ApplyDir:"$W\" /Quiet
    if ($LASTEXITCODE) { Fail "Could not unpack setup files (dism $LASTEXITCODE)." }
    P 3 40 'Building the installer'
    dism /Export-Image /SourceImageFile:$img /SourceIndex:2 /DestinationImageFile:"$W\sources\boot.wim" /Compress:max /Quiet
    if ($LASTEXITCODE) { Fail "Could not build boot.wim (dism $LASTEXITCODE)." }
    P 3 70 'Building the installer'
    dism /Export-Image /SourceImageFile:$img /SourceIndex:3 /DestinationImageFile:"$W\sources\boot.wim" /Compress:max /Bootable /Quiet
    if ($LASTEXITCODE) { Fail "Could not build boot.wim (dism $LASTEXITCODE)." }
    $ErrorActionPreference = 'Stop'
    P 3 100 'Installer built'
} else {
    P 2 0 'Opening the ISO'
    $im = Mount-DiskImage -ImagePath $picks.iso -PassThru
    $src = ($im | Get-Volume).DriveLetter + ':'
    $img = if (Test-Path "$src\sources\install.wim") { "$W\sources\install.wim" } elseif (Test-Path "$src\sources\install.esd") { "$W\sources\install.esd" } else { $null }
    if (-not $img) { Dismount-DiskImage -ImagePath $picks.iso | Out-Null; Fail 'This ISO has no Windows image (sources\install.wim or install.esd).' }
    $files = Get-ChildItem $src -Recurse -File
    $total = ($files | Measure-Object Length -Sum).Sum
    $done = 0; $last = -1
    $buf = New-Object byte[] (4MB)
    foreach ($f in $files) {
        $dst = "$W$($f.FullName.Substring($src.Length))"
        $dir = Split-Path $dst
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory $dir -Force | Out-Null }
        $in = [IO.File]::OpenRead($f.FullName); $out = [IO.File]::Create($dst)
        try {
            while (($n = $in.Read($buf, 0, $buf.Length)) -gt 0) {
                $out.Write($buf, 0, $n); $done += $n
                $pct = [int](100 * $done / $total)
                if ($pct -ne $last) { $last = $pct; P 2 $pct 'Copying Windows' }
            }
        } finally { $in.Close(); $out.Close() }
    }
    attrib -r "$W\*.*" /s /d | Out-Null
    Dismount-DiskImage -ImagePath $picks.iso | Out-Null
    P 2 100 'Windows copied'
}
$BW = "$W\sources\boot.wim"
if ($usb) {
    foreach ($x in 'efi', 'boot') { if (Test-Path "$W\$x") { Copy-Item "$W\$x" "$B\" -Recurse -Force } }
    foreach ($x in 'bootmgr', 'bootmgr.efi') { if (Test-Path "$W\$x") { Copy-Item "$W\$x" "$B\" -Force } }
    New-Item -ItemType Directory "$B\sources" -Force | Out-Null
    Move-Item $BW "$B\sources\boot.wim" -Force
    $bs = "$B\boot\bootsect.exe"
    if (Test-Path $bs) { & $bs /nt60 $B /force /mbr | Out-Null; L "bootsect $LASTEXITCODE" } else { L 'no bootsect.exe: USB boots on UEFI only' }
    $BW = "$B\sources\boot.wim"
    Set-Content "$B\whatos-boot.txt" 'WhatOS USB boot part. Windows files are on the WhatOS USB part.'
}
$imgs = Get-WindowsImage -ImagePath $img
$ed = $picks.ed
if (-not $whatos -or -not $ed -or $ed -eq 'auto') { $ed = if ((Get-CimInstance Win32_OperatingSystem).Caption -match 'Pro') { 'pro' } else { 'home' } }
$want = if ($ed -eq 'pro') { ' Pro(fessional)?$' } else { ' Home$' }
$pick = $imgs | ? { $_.ImageName -match $want -and $_.ImageName -notmatch ' N$| N |Single|Education' } | select -First 1
if (-not $pick) { $pick = $imgs | ? { $_.ImageName -match 'Windows' } | select -Last 1 }
if (-not $pick) { Fail 'No Windows edition found in this ISO.' }
$info = Get-WindowsImage -ImagePath $img -Index $pick.ImageIndex
if ($info.Architecture -ne 9) { Fail "This ISO is not 64-bit (x64) Windows ($($info.ImageName))." }
if ($whatos -and ($info.InstallationType -ne 'Client' -or $info.Build -lt 22000)) { Fail "WhatOS needs a Windows 11 ISO ($($info.ImageName), build $($info.Build)). To install this ISO as-is, turn WhatOS off." }
L "image index $($pick.ImageIndex) $($pick.ImageName) build $($info.Build)"

P $SA 0 'Adding WhatOS'
$H = "$W\whatos"
New-Item -ItemType Directory $H -Force | Out-Null
Set-Content "$W\whatos.tag" 'whatos'
Copy-Item "$Payload\post.ps1", "$Payload\picks.json" $H
Copy-Item $drv "$H\drivers" -Recurse -Force
Remove-Item $drv -Recurse -Force -EA 0
if ($picks.report) { Set-Content "$H\report.url" $picks.report -Encoding ascii }
if ($whatos) {
    Copy-Item "$Payload\pe.xml", "$Payload\offline.cmd", "$Payload\whatos-prep.ps1", "$Payload\edge.ps1", "$Payload\updates.ps1", "$Payload\update-check.ps1", "$Payload\tweaks.json", "$Payload\logo.png", "$Payload\whatos-wallpaper.jpg", "$Payload\whatos.theme" $H
    if (Test-Path "$Payload\WhatOS.exe") { Copy-Item "$Payload\WhatOS.exe", "$Payload\WhatOSLink.exe", "$Payload\WhatOSRam.exe", "$Payload\WhatOSGame.exe" $H }
    $b = { param($v) if ($v -eq 'remove') { '0' } else { '1' } }
    $keep = @()
    $map = @{ 'Photos' = 'Photos'; 'Notepad' = 'Notepad'; 'Snipping Tool' = 'ScreenSketch'; 'Terminal' = 'WindowsTerminal'; 'Calculator' = 'WindowsCalculator'; 'Paint' = 'Paint'; 'Camera' = 'WindowsCamera' }
    foreach ($a in @($picks.apps.winapps)) { if ($map[$a]) { $keep += $map[$a] } }
    $edge = if (@($picks.apps.browser) -contains 'Edge') { '1' } else { '0' }
    $ids = @{ 'Chrome' = 'Google.Chrome|admin'; 'Firefox' = 'Mozilla.Firefox|admin'; 'Brave' = 'Brave.Brave|admin'; 'Opera GX' = 'Opera.OperaGX|admin'
        '7-Zip' = '7zip.7zip|admin'; 'WinRAR' = 'RARLab.WinRAR|admin'; 'NanaZip' = 'M2Team.NanaZip|admin'
        'Steam' = 'Valve.Steam|admin'; 'Epic' = 'EpicGames.EpicGamesLauncher|admin'; 'EA app' = 'ElectronicArts.EADesktop|admin'; 'Ubisoft' = 'Ubisoft.Connect|admin'; 'Battle.net' = 'Blizzard.BattleNet|admin'; 'Xbox' = '9MV0B5HZVK9Z|admin'
        'Discord' = 'https://discord.com/api/downloads/distributions/app/installers/latest?channel=stable&platform=win&arch=x64|user|Discord|-s'; 'Telegram' = 'Telegram.TelegramDesktop|admin'; 'WhatsApp' = '9NKSQGP7F2NH|admin'; 'LINE' = 'https://desktop.line-scdn.net/win/new/LineInst.exe|user|LINE|RUNONCE'
        'VLC' = 'VideoLAN.VLC|admin'; 'Spotify' = 'https://download.scdn.co/SpotifySetup.exe|user|Spotify|/silent'; 'OBS' = 'OBSProject.OBSStudio|admin' }
    $al = foreach ($k in 'browser', 'zip', 'games', 'chat', 'media') { foreach ($a in @($picks.apps.$k)) { $v = $ids[$a]; if ($v -match '^https?://') { $v } elseif ($v) { "$v|$a" } } }
    Set-Content "$H\apps.txt" -Value @($al) -Encoding ascii; Copy-Item "$Payload\apps.ps1" $H; L "apps $($al -join ', ')"
    @("set MITIG=$(& $b $picks.mitig)", "set DEF=$(& $b $picks.def)", "set XBOX=$(& $b $picks.xbox)", "set STORE=$(& $b $picks.store)", "set KEEPAPPS=$($keep -join ' ')", "set EDGE=$edge") | Set-Content "$H\choices.cmd" -Encoding ascii
}
if ($picks.test -and $whatos) {
    Copy-Item "$Payload\pe-test.xml" "$H\pe.xml" -Force
    New-Item -ItemType Directory "$H\ssh" -Force | Out-Null
    (New-Object Net.WebClient).DownloadFile('https://github.com/PowerShell/Win32-OpenSSH/releases/download/10.4.0.0p1-Preview/OpenSSH-Win64.zip', "$H\ssh\OpenSSH-Win64.zip")
    if ($picks.testkey) { Set-Content "$H\ssh\authorized_keys" $picks.testkey -Encoding ascii }
    L "test mode: auto account test/test, sshd"
}
$applied = if ($whatos) { @($pick.ImageName; $(if ($picks.def -eq 'remove') { 'Defender removed' } else { 'Defender kept' }); 'WhatOS power plan') -join ' · ' } else { "$($pick.ImageName) · as-is" }
if ($usb) { $ini = @('[whatpe]', "name=$($pick.ImageName)", 'pick=1', 'usb=1', "index=$($pick.ImageIndex)", 'fw=UEFI', "whatos=$(if($whatos){1}else{0})", "applied=$applied") }
else { $ini = @('[whatpe]', "name=$($pick.ImageName)", "serial=$serial", "part=$($c.PartitionNumber)", "efi=$($efi.PartitionNumber)", "index=$($pick.ImageIndex)", "fw=$(if ($bios) { 'BIOS' } else { 'UEFI' })", "whatos=$(if($whatos){1}else{0})", "applied=$applied") }
$ini | Set-Content "$H\whatpe.ini" -Encoding UTF8
P $SA 20 'Adding WhatOS'
$script:m = $m = Join-Path $env:TEMP 'WhatOS\mnt'
if (Test-Path $m) { dism /Unmount-Wim /MountDir:$m /Discard /Quiet | Out-Null; Remove-Item $m -Recurse -Force -EA 0 }
New-Item -ItemType Directory $m -Force | Out-Null
$bi = if ((Get-WindowsImage -ImagePath $BW).Count -ge 2) { 2 } else { 1 }
dism /Mount-Wim /WimFile:"$BW" /Index:$bi /MountDir:$m /Quiet
if ($LASTEXITCODE) { Fail "Could not open boot.wim (dism $LASTEXITCODE)." }
P $SA 60 'Adding WhatOS'
Copy-Item "$Payload\whatpe.exe" "$m\Windows\System32\" -Force
Copy-Item "$env:windir\System32\findstr.exe" "$m\Windows\System32\" -Force
Copy-Item "$env:windir\System32\en-US\findstr.exe.mui" "$m\Windows\System32\en-US\" -Force -EA 0
Copy-Item "$env:windir\Fonts\segoeui.ttf" "$m\Windows\Fonts\" -Force -EA 0
"[LaunchApps]`r`n%SYSTEMDRIVE%\Windows\System32\whatpe.exe`r`n" | Set-Content "$m\Windows\System32\winpeshl.ini" -Encoding ascii
reg load HKLM\WHATPE "$m\Windows\System32\config\SYSTEM" | Out-Null
foreach ($v in 'BypassTPMCheck', 'BypassSecureBootCheck', 'BypassRAMCheck', 'BypassCPUCheck') { reg add HKLM\WHATPE\Setup\LabConfig /v $v /t REG_DWORD /d 1 /f | Out-Null }
[gc]::Collect(); Start-Sleep 1
reg unload HKLM\WHATPE | Out-Null
P $SA 80 'Saving the installer'
dism /Unmount-Wim /MountDir:$m /Commit /Quiet
if ($LASTEXITCODE) { Fail "Could not save boot.wim (dism $LASTEXITCODE)." }
Remove-Item $m -Recurse -Force -EA 0

if ($usb) {
    P $SA 100 'USB stick ready'
    Write-Output "@done|usb"
    exit 0
}
P $SA 90 'Adding the boot entry'
$ErrorActionPreference = 'Continue'
$ro = bcdedit /create '{ramdiskoptions}' /d 'WhatOS Installer' 2>&1
if ($LASTEXITCODE) { bcdedit /set '{ramdiskoptions}' description 'WhatOS Installer' | Out-Null }
bcdedit /set '{ramdiskoptions}' ramdisksdidevice "partition=$W" | Out-Null
bcdedit /set '{ramdiskoptions}' ramdisksdipath \boot\boot.sdi | Out-Null
$o = bcdedit /create /d 'WhatOS Installer' /application osloader
$g = [regex]::Match(($o -join ' '), '\{[0-9a-fA-F-]+\}').Value
if (-not $g) { Fail 'Could not add the boot entry.' }
foreach ($k in 'device', 'osdevice') { bcdedit /set $g $k "ramdisk=[$W]\sources\boot.wim,{ramdiskoptions}" | Out-Null }
bcdedit /set $g path "\windows\system32\boot\winload.$(if ($bios) { 'exe' } else { 'efi' })" | Out-Null
bcdedit /set $g systemroot \windows | Out-Null
bcdedit /set $g winpe yes | Out-Null
bcdedit /set $g detecthal yes | Out-Null
bcdedit /displayorder $g /addlast | Out-Null
Set-Content "$H\bcd.id" $g
L "bcd $g"
P $SA 100 'WhatOS added'
Write-Output "@done|$W"
