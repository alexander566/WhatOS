$st='C:\Users\Public\WhatOS'
New-Item -ItemType Directory $st -Force | Out-Null
$log="$st\updates.log"
function St($m){ Set-Content "$st\wu.status" $m; $m | Add-Content $log }
"start $(whoami) $(Get-Date)" | Add-Content $log
St 'Waiting for the internet'
for($i=0;$i -lt 86400;$i++){ try { $c=New-Object Net.Sockets.TcpClient; $c.Connect('1.1.1.1',443); $c.Close(); break } catch { Start-Sleep 2 } }
try {
 $us=New-Object -ComObject Microsoft.Update.Session; $us.ClientApplicationID='WhatOS'
 for($round=1;$round -le 3;$round++){
  St 'Looking for updates and drivers'
  $sr=$us.CreateUpdateSearcher(); $sr.Online=$true
  $res=$sr.Search("IsInstalled=0 and IsHidden=0 and BrowseOnly=0")
  $col=New-Object -ComObject Microsoft.Update.UpdateColl
  foreach($u in $res.Updates){ if($u.Title -notmatch 'Preview|Defender Antivirus|Security Intelligence' -or $u.Type -eq 2){ if(-not $u.EulaAccepted){ $u.AcceptEula() }; [void]$col.Add($u) } }
  "wu round $round found $($col.Count)" | Add-Content $log
  if($col.Count -eq 0){ break }
  for($i=0;$i -lt $col.Count;$i++){
   $one=New-Object -ComObject Microsoft.Update.UpdateColl; [void]$one.Add($col.Item($i))
   $t=$col.Item($i).Title; if($t.Length -gt 70){ $t=$t.Substring(0,70)+'...' }
   St "Downloading $($i+1) of $($col.Count): $t"
   $dl=$us.CreateUpdateDownloader(); $dl.Updates=$one; [void]$dl.Download()
   St "Installing $($i+1) of $($col.Count): $t"
   $in=$us.CreateUpdateInstaller(); $in.Updates=$one; $r=$in.Install()
   "wu $($r.ResultCode) $($col.Item($i).Title)" | Add-Content $log
  }
 }
} catch { "wu error $($_.Exception.Message)" | Add-Content $log }
Set-Content "$st\wu.done" (Get-Date)
Unregister-ScheduledTask -TaskName WhatOSUpdate -Confirm:$false -EA 0
"end $(Get-Date)" | Add-Content $log
