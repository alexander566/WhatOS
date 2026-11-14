param([string]$Action = 'status', [string]$Id = '', [string]$Manifest = '')
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$dir = 'C:\ProgramData\WhatOS'
New-Item -ItemType Directory $dir -Force | Out-Null
$sf = "$dir\state.json"
$mf = if ($Manifest) { $Manifest } else {
    $b = Join-Path $PSScriptRoot 'tweaks.json'; $i = "$dir\tweaks.json"
    if ((Test-Path $i) -and (-not (Test-Path $b) -or [int](Get-Content $i -Raw | ConvertFrom-Json).version -ge [int](Get-Content $b -Raw | ConvertFrom-Json).version)) { $i } else { $b }
}
$M = Get-Content $mf -Raw | ConvertFrom-Json
$hpScheme = '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'
$balanced = '381b4222-f694-41f0-9685-ff5bb260df2e'
$pw = @(
    @('54533251-82be-4824-96c1-47b60b740d00', '7b224883-b3cc-4d79-819f-8374152cbe7c', 90),
    @('48672f38-7a9a-4bb2-8bf8-3d85be19de4e', '2bfc24f9-5ea2-4801-8213-3dbae01aa39d', 6),
    @('54533251-82be-4824-96c1-47b60b740d00', '6c2993b0-8f48-481f-bcc6-00dd2742aa06', 0),
    @('54533251-82be-4824-96c1-47b60b740d00', '4e4450b3-6179-4e91-b8f1-5bb9938f81a1', 0),
    @('54533251-82be-4824-96c1-47b60b740d00', '36687f9e-e3a5-4dbf-b1dc-15eb381c6863', 0),
    @('54533251-82be-4824-96c1-47b60b740d00', '36687f9e-e3a5-4dbf-b1dc-15eb381c6864', 0),
    @('7516b95f-f776-4464-8c53-06167f40cc99', 'aded5e82-b909-4619-9949-f5d71dac0bcb', 0),
    @('7516b95f-f776-4464-8c53-06167f40cc99', 'f1fbfde2-a960-4165-9f88-50667911ce96', 0))

$classic = 'HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32'
$ramMenu = 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\DesktopBackground\Shell\WhatOSClearRAM'
$ifk = 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces'
function NicKeys { @(Get-ChildItem $ifk -EA 0 | ? { $p = Get-ItemProperty $_.PSPath -EA 0; $p.DhcpIPAddress -or ($p.IPAddress -and "$($p.IPAddress)".Trim()) }) }
function RegPath($r) { 'Registry::' + ($r -replace '^HKLM\\', 'HKEY_LOCAL_MACHINE\' -replace '^HKCU\\', 'HKEY_CURRENT_USER\') }
function Norm($v) { if ($null -eq $v) { return $null }; if ($v -is [int] -or $v -is [long] -or $v -is [uint32]) { $x = [long]$v; if ($x -lt 0) { $x += 4294967296 }; return [string]$x }; [string]$v }
function Get-Op($o) {
    if ($o.r) { $v = $null; try { $v = Get-ItemPropertyValue (RegPath $o.r) $o.n -EA Stop } catch {}; return (Norm $v) }
    if ($o.s) { $k = "HKLM:\SYSTEM\CurrentControlSet\Services\$($o.s)"; if (-not (Test-Path $k)) { return 'n/a' }; return (Norm (Get-ItemPropertyValue $k Start -EA 0)) }
    switch ($o.ps) {
        'power' { if ((powercfg /getactivescheme) -match $hpScheme) { 'on' } else { 'off' } }
        'dyntick' { if ((bcdedit /enum '{current}' | Out-String) -match 'disabledynamictick\s+Yes') { 'on' } else { 'off' } }
        'hiber' { if ((Get-ItemPropertyValue HKLM:\SYSTEM\CurrentControlSet\Control\Power HibernateEnabled -EA 0) -eq 0) { 'on' } else { 'off' } }
        'reserved' { if ((Get-ItemPropertyValue HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\ReserveManager ShippedWithReserves -EA 0) -eq 0) { 'on' } else { 'off' } }
        'classic' { if (Test-Path $classic) { 'on' } else { 'off' } }
        'clearram' { if (-not (Test-Path 'C:\Program Files\WhatOS\WhatOSRam.exe')) { 'n/a' } elseif (Test-Path $ramMenu) { 'on' } else { 'off' } }
        'memcomp' { if ((Get-MMAgent).MemoryCompression) { 'off' } else { 'on' } }
        'nagle' { $k = NicKeys; if (-not $k) { 'n/a' } elseif (@($k | ? { (Get-ItemProperty $_.PSPath -EA 0).TcpAckFrequency -ne 1 }).Count -eq 0) { 'on' } else { 'off' } }
    }
}
function Target($o, $on) {
    if ($o.ps) { return $(if ($on) { 'on' } else { 'off' }) }
    if ($on) { return (Norm $o.v) }
    if ($o.PSObject.Properties.Name -contains 'd') { return (Norm $o.d) }
    $null
}
function Set-Op($o, $on) {
    if ($o.r) {
        $p = RegPath $o.r
        $want = if ($on) { $o.v } elseif ($o.PSObject.Properties.Name -contains 'd') { $o.d } else { $null }
        if ($null -eq $want) { Remove-ItemProperty $p $o.n -EA 0; return }
        if (-not (Test-Path $p)) { New-Item $p -Force | Out-Null }
        $t = if ($o.t) { $o.t } else { 'DWord' }
        if ($t -eq 'DWord') { $x = [long]$want; if ($x -gt 2147483647) { $x -= 4294967296 }; $want = [int]$x }
        New-ItemProperty $p $o.n -PropertyType $t -Value $want -Force | Out-Null
        return
    }
    if ($o.s) {
        if (-not (Test-Path "HKLM:\SYSTEM\CurrentControlSet\Services\$($o.s)")) { return }
        $v = if ($on) { $o.v } else { $o.d }
        $m = @{ 2 = 'auto'; 3 = 'demand'; 4 = 'disabled' }[[int]$v]
        sc.exe config $o.s start= $m | Out-Null
        if ($LASTEXITCODE) { Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\$($o.s)" Start ([int]$v) -EA 0 }
        if ($on -and $v -eq 4) { Stop-Service $o.s -Force -EA 0 }
        return
    }
    switch ($o.ps) {
        'power' {
            if ($on) {
                powercfg /setactive $hpScheme | Out-Null
                if ($LASTEXITCODE) { powercfg /duplicatescheme $hpScheme $hpScheme | Out-Null; powercfg /setactive $hpScheme | Out-Null }
                foreach ($s in $pw) { powercfg /setacvalueindex $hpScheme $s[0] $s[1] $s[2] | Out-Null }
                powercfg /setactive $hpScheme | Out-Null
            } else { powercfg /setactive $balanced | Out-Null }
        }
        'dyntick' { if ($on) { bcdedit /set disabledynamictick yes | Out-Null } else { bcdedit /deletevalue disabledynamictick | Out-Null } }
        'hiber' { if ($on) { powercfg /h off } else { powercfg /h on } }
        'reserved' { Set-ItemProperty HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\ReserveManager ShippedWithReserves $(if ($on) { 0 } else { 1 }) -Type DWord -EA 0; Start-Process dism.exe "/Online /Set-ReservedStorageState /State:$(if ($on) { 'Disabled' } else { 'Enabled' })" -WindowStyle Hidden }
        'classic' { if ($on) { New-Item $classic -Force | Out-Null; Set-ItemProperty $classic '(default)' '' } else { Remove-Item (Split-Path $classic) -Recurse -Force -EA 0 } }
        'clearram' { if ($on) { New-Item "$ramMenu\command" -Force | Out-Null; Set-ItemProperty $ramMenu MUIVerb 'Clear RAM'; Set-ItemProperty $ramMenu Icon 'C:\Program Files\WhatOS\WhatOSRam.exe,0'; Set-ItemProperty "$ramMenu\command" '(default)' '"C:\Program Files\WhatOS\WhatOSRam.exe"' } else { Remove-Item $ramMenu -Recurse -Force -EA 0 } }
        'memcomp' { if ($on) { Disable-MMAgent -MemoryCompression -EA 0 } else { Enable-MMAgent -MemoryCompression -EA 0 } }
        'nagle' { foreach ($k in NicKeys) { if ($on) { Set-ItemProperty $k.PSPath TcpAckFrequency 1 -Type DWord; Set-ItemProperty $k.PSPath TCPNoDelay 1 -Type DWord } else { Remove-ItemProperty $k.PSPath TcpAckFrequency, TCPNoDelay -EA 0 } } }
    }
}
function GState($g) {
    $on = 0; $off = 0; $n = 0
    foreach ($o in $g.ops) {
        $c = Get-Op $o
        if ($c -eq 'n/a') { continue }
        $n++
        if ($c -eq (Target $o $true)) { $on++ } elseif ($c -eq (Target $o $false)) { $off++ }
    }
    if ($n -eq 0) { 'n/a' } elseif ($on -eq $n) { 'on' } elseif ($off -eq $n) { 'off' } else { 'mixed' }
}
function Load {
    if (Test-Path $sf) { $s = Get-Content $sf -Raw | ConvertFrom-Json } else { $s = $null }
    if (-not $s) {
        $s = [pscustomobject]@{ version = 0; applied = @(); off = @(); history = @() }
        if (Test-Path C:\Windows\WhatOS) { $s.applied = @($M.groups | ? { (GState $_) -eq 'on' } | % id); $s.version = $M.version }
        Save $s
    }
    if (-not ($s.PSObject.Properties.Name -contains 'off')) { $s | Add-Member off @() }
    $s.applied = @($s.applied); $s.off = @($s.off); $s.history = @($s.history)
    $s
}
function Save($s) { $s | ConvertTo-Json -Depth 6 | Set-Content $sf -Encoding UTF8 }
function Snap($g) { @($g.ops | % { Get-Op $_ }) }
function Log($s, $a, $g, $before) { $s.history = @([pscustomobject]@{ t = (Get-Date).ToString('yyyy-MM-dd HH:mm'); a = $a; id = $g.id; name = $g.name; before = $before }) + @($s.history) | select -First 100 }
function Do-Set($s, $g, $on, $why) {
    $before = Snap $g
    foreach ($o in $g.ops) { Set-Op $o $on }
    if ($on) { $s.applied = @(@($s.applied) + $g.id | select -Unique); $s.off = @($s.off | ? { $_ -ne $g.id }) } else { $s.applied = @($s.applied | ? { $_ -ne $g.id }); if ($why -eq 'off') { $s.off = @(@($s.off) + $g.id | select -Unique) } }
    Log $s $why $g $before
}
function Status($s) {
    $list = foreach ($g in $M.groups) {
        $st = GState $g
        $mine = $s.applied -contains $g.id
        if ($mine -and $st -ne 'on' -and $st -ne 'n/a') { $st = 'yours' }
        [pscustomobject]@{ id = $g.id; name = $g.name; desc = $g.desc; cat = $g.cat; page = $g.page; level = $g.level; rec = [bool]$g.rec; expert = [bool]$g.expert; state = $st }
    }
    [pscustomobject]@{ version = $s.version; manifest = $M.version; groups = @($list); history = @($s.history | select -First 30 | select t, a, id, name) }
}

$S = Load
switch ($Action) {
    'status' { }
    'apply' { $g = $M.groups | ? id -eq $Id; if ($g) { Do-Set $S $g $true 'on' } }
    'revert' { $g = $M.groups | ? id -eq $Id; if ($g) { Do-Set $S $g $false 'off' } }
    'applyset' { foreach ($i in ($Id -split ',')) { $g = $M.groups | ? id -eq $i; if ($g) { Do-Set $S $g $true 'on' } } }
    'undo' {
        $h = @($S.history)[0]
        $g = $M.groups | ? id -eq $h.id
        if ($h -and $g) {
            $ops = @($g.ops)
            for ($i = 0; $i -lt $ops.Count; $i++) {
                $o = $ops[$i]; $b = @($h.before)[$i]
                if ($b -eq 'n/a') { continue }
                if ($o.ps) { Set-Op $o ($b -eq 'on') }
                elseif ($b -eq (Target $o $true)) { Set-Op $o $true }
                elseif ($b -eq (Target $o $false)) { Set-Op $o $false }
                elseif ($o.r) { $p = RegPath $o.r; if ($null -eq $b) { Remove-ItemProperty $p $o.n -EA 0 } else { $t = if ($o.t) { $o.t } else { 'DWord' }; $v = if ($t -eq 'DWord') { $x = [long]$b; if ($x -gt 2147483647) { $x -= 4294967296 }; [int]$x } else { $b }; New-ItemProperty $p $o.n -PropertyType $t -Value $v -Force | Out-Null } }
                elseif ($o.s) { Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\$($o.s)" Start ([int]$b) -EA 0 }
            }
            if ($h.a -eq 'on') { $S.applied = @($S.applied | ? { $_ -ne $g.id }) } elseif ($h.a -eq 'off') { $S.applied = @(@($S.applied) + $g.id | select -Unique) }
            $S.history = @($S.history | select -Skip 1)
        }
    }
    'update' {
        foreach ($g in $M.groups) {
            if ($g.expert) { continue }
            $st = GState $g
            if ($S.off -contains $g.id -or ($S.applied -contains $g.id -and $st -eq 'mixed')) { continue }
            if ($S.applied -contains $g.id -or $st -eq 'off') { if ($st -ne 'on') { Do-Set $S $g $true 'update' } }
        }
        $S.version = $M.version
        if ($Manifest) { Copy-Item $Manifest "$dir\tweaks.json" -Force }
    }
    'diff' {
        $old = @{}; if (Test-Path "$dir\tweaks.json") { (Get-Content "$dir\tweaks.json" -Raw | ConvertFrom-Json).groups | % { $old[$_.id] = ($_.ops | ConvertTo-Json -Compress -Depth 4) } }
        $ch = @($M.groups | ? { -not $_.expert -and $S.off -notcontains $_.id -and (-not $old.ContainsKey($_.id) -or $old[$_.id] -ne ($_.ops | ConvertTo-Json -Compress -Depth 4)) } | % { [pscustomobject]@{ id = $_.id; name = $_.name; desc = $_.desc; new = -not $old.ContainsKey($_.id) } })
        [pscustomobject]@{ version = $M.version; current = $S.version; changes = $ch } | ConvertTo-Json -Depth 4 -Compress
        exit
    }
    'restore' {
        foreach ($g in $M.groups) { if ((GState $g) -ne 'off') { Do-Set $S $g $false 'restore' } }
        Unregister-ScheduledTask -TaskName WhatOSUpdateCheck -Confirm:$false -EA 0
    }
}
Save $S
Status $S | ConvertTo-Json -Depth 5 -Compress
