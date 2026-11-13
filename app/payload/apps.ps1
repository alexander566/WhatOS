param([string]$Mode = 'admin')
if ($env:USERNAME -like 'defaultuser*') { exit }
$d = 'C:\Users\Public\WhatOS'
New-Item -ItemType Directory $d -Force | Out-Null
$log = "$d\apps-$Mode.log"
function W($m) { "$(Get-Date -f HH:mm:ss) $m" | Add-Content $log }
function St($m) { Set-Content "$d\apps.status" $m }
W "start as $(whoami)"
St 'Waiting for the internet'
for ($n = 0; $n -lt 43200; $n++) { try { $tc = New-Object Net.Sockets.TcpClient; $tc.Connect('1.1.1.1', 443); $tc.Close(); break } catch { Start-Sleep 2 } }
W 'online'
$list = @(Get-Content C:\Windows\WhatOS\apps.txt | ? { $_ -match "\|$Mode\|" })
$wg = $null
$direct = @($list | ? { $_ -match '^https?://' })
$list = @($list | ? { $_ -notmatch '^https?://' })
$i = 0
foreach ($l in $direct) {
    $i++
    $url, $m, $name, $args2 = $l -split '\|'
    St "Installing $name ($i of $($direct.Count))"
    try {
        $f = "$env:TEMP\whatos-$i.exe"
        if ($args2 -eq 'RUNONCE') {
            $f = "C:\Users\Public\WhatOS\$($name -replace '[^A-Za-z0-9]','')-setup.exe"
            (New-Object Net.WebClient).DownloadFile($url, $f)
            New-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce' -Force | Out-Null
            Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce' "WhatOS$($name -replace '[^A-Za-z0-9]','')" "`"$f`""
            W "$name installer opens at next sign-in"
            continue
        }
        (New-Object Net.WebClient).DownloadFile($url, $f)
        $p = Start-Process $f -ArgumentList $args2 -PassThru
        $null = $p.Handle
        $done = @{ 'Discord' = "$env:LOCALAPPDATA\Discord\app-*\Discord.exe" }[$name]
        for ($w = 0; $w -lt 84 -and -not $p.HasExited; $w++) { if ($done -and (Test-Path $done) -and (Select-String -Path (Join-Path (Split-Path (Split-Path $done)) 'SquirrelSetup.log') -Pattern 'Updating shortcut' -Quiet -EA 0)) { Start-Sleep 5; break }; Start-Sleep 5 }
        if (-not $p.HasExited -and $done -and (Test-Path $done)) { W "$name (direct) installed after $($w*5) s" }
        elseif (-not $p.WaitForExit(1000)) { $p | Stop-Process -Force -EA 0; Get-Process | ? { $_.Path -like "$env:TEMP\whatos-*" } | Stop-Process -Force -EA 0; W "$name timed out" } else { W "$name (direct) exit $($p.ExitCode)" }
    } catch { W "$name direct failed: $($_.Exception.Message)" }
}
for ($k = 0; $k -lt 120 -and -not $wg -and $list.Count; $k++) {
    $c = @((Get-Command winget.exe -EA 0).Source) + @(Get-ChildItem "$env:ProgramFiles\WindowsApps\Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe\winget.exe" -EA 0 | sort FullName -Descending | % FullName)
    foreach ($x in $c) { if ($x) { & $x --version 2>&1 | Out-Null; if ($LASTEXITCODE -eq 0) { $wg = $x; break } } }
    if ($wg) { break }
    if ($k -eq 0 -or $k % 12 -eq 0) { try { Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe -EA Stop } catch { W "register: $($_.Exception.Message)" } }
    if ($k -eq 12) {
        try {
            $t = "$env:TEMP\whatos-wg"; New-Item -ItemType Directory $t -Force | Out-Null
            $wc = New-Object Net.WebClient
            $wc.DownloadFile('https://github.com/microsoft/winget-cli/releases/latest/download/DesktopAppInstaller_Dependencies.zip', "$t\dep.zip")
            $wc.DownloadFile('https://aka.ms/getwinget', "$t\wg.msixbundle")
            Expand-Archive "$t\dep.zip" "$t\dep" -Force
            Get-ChildItem "$t\dep" -Recurse -Filter *.appx | ? { $_.FullName -match 'x64' } | % { try { Add-AppxPackage $_.FullName -EA Stop } catch { W "dep $($_.Exception.Message)" } }
            Add-AppxPackage "$t\wg.msixbundle" -EA Stop
            W 'winget installed from GitHub'
        } catch { W "winget download: $($_.Exception.Message)" }
    }
    St 'Getting the app installer ready'
    Start-Sleep 5
}
W "winget $wg"
if ($wg -and $list.Count) {
    & $wg source update 2>&1 | Out-Null
    $i = 0
    foreach ($l in $list) {
        $i++
        $id, $m, $name = $l -split '\|'
        St "Installing $name ($i of $($list.Count))"
        $src = if ($id -match '^[0-9A-Z]{12}$') { 'msstore' } else { 'winget' }
        $o = "$d\wg-$i.txt"
        for ($try = 1; $try -le 2; $try++) {
            $p = Start-Process $wg -ArgumentList "install --id $id -e --source $src --silent --accept-package-agreements --accept-source-agreements --disable-interactivity" -PassThru -WindowStyle Hidden -RedirectStandardOutput $o
            $null = $p.Handle
            if (-not $p.WaitForExit(900000)) { $p | Stop-Process -Force -EA 0; W "$name ($id) timed out after 15 min"; break }
            W "$name ($id) exit $($p.ExitCode)"
            if (-not $p.ExitCode) { break }
            W ((Get-Content $o -EA 0 | ? { $_.Trim() } | select -Last 3) -join ' | ')
            if ($try -eq 1) { & $wg source reset --force 2>&1 | Out-Null; & $wg source update 2>&1 | Out-Null; W 'source reset, retry' }
        }
    }
} elseif ($list.Count) { W 'winget not available, apps skipped' }
if ($Mode -eq 'admin') {
    Set-Content "$d\apps.done" (Get-Date)
    Unregister-ScheduledTask -TaskName WhatOSApps -Confirm:$false -EA 0
} else { Set-Content "$d\apps-user.done" (Get-Date) }
St 'Apps done'
W 'end'
