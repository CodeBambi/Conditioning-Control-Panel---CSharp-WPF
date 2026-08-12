$exe = (Resolve-Path 'client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe').Path
$env:CCP_DATA_ROOT = (Resolve-Path 'spine-tasks\SP-061-chaos-tunnel-backdrop\scratch\evidence\wh\sandbox-root').Path
$psi = New-Object System.Diagnostics.ProcessStartInfo($exe, '--tunnel-demo --tunnel-auto-close 8')
$psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.UseShellExecute = $false
$p = [System.Diagnostics.Process]::Start($psi)
$ok = $p.WaitForExit(40000)
$err = $p.StandardError.ReadToEnd()
Write-Output ('wait=' + $ok + ' ExitCode=' + $p.ExitCode)
Write-Output '--- err tail ---'
$err -split "\r?\n" | Select-Object -Last 10
