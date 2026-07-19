# SP-011 failure-injection case 1: kill the WebView2 renderer processes belonging to the
# spike app (identified by the unique UserDataFolder under the spike scratch dir).
$marker = "CcpSpike.WebView\scratch\wv2-profile"
$procs = Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" |
  Where-Object { $_.CommandLine -like "*$marker*" }
if (-not $procs) { Write-Output "no matching msedgewebview2 processes"; exit 1 }
foreach ($p in $procs) {
  Write-Output "killing pid=$($p.ProcessId)"
  Stop-Process -Id $p.ProcessId -Force
}
Write-Output "killed $($procs.Count) renderer process(es)"
