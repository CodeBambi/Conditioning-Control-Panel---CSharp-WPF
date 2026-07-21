$exe = Join-Path (Get-Location) 'client/src/CcpClient.Desktop/bin/Debug/net10.0/CcpClient.Desktop.exe'
Write-Output "exe=$exe exists=$(Test-Path $exe)"
$outFile = Join-Path $env:TEMP 'sd-out.txt'
$errFile = Join-Path $env:TEMP 'sd-err.txt'
$p = Start-Process -FilePath $exe -ArgumentList '--avatar-strip-decode','--capture','spine-tasks/SP-015-avatartube-animation/evidence/cap-g1-1784542702871.bmp' -NoNewWindow -Wait -RedirectStandardOutput $outFile -RedirectStandardError $errFile -PassThru
Write-Output "exit=$($p.ExitCode)"
Write-Output "stdout: $(Get-Content $outFile -Raw)"
Write-Output "stderr: $(Get-Content $errFile -Raw)"
