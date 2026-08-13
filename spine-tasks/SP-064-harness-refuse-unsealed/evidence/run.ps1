# SP-064 Step 3 real-process evidence (four bounded runs, T-11):
#   (a) class-1 flag, CCP_DATA_ROOT UNSET  -> refuse: exit 3, stderr names the variable, no window
#   (b) real %APPDATA%\CcpClient byte-identical across (a), both-directions diff + positive controls
#   (c) same flag SEALED (CCP_DATA_ROOT=scratch) -> NOT refused, override line + m2test signal, auto-close bound
#   (d) plain launch UNSEALED, no flags -> NOT refused, window rect-verified, exit 0, profile delta reported
# Manifest/diff/drive helpers copied verbatim from SP-057's evidence (same path-hashed privacy
# discipline; only manifests/logs/pngs are committed, never the scratch root itself).
$ErrorActionPreference = "Stop"
$root = "C:\Code\Conditioning-Control-Panel---CSharp-WPF\.worktrees\spine-20260813T010705\lane-1"
$ev = "$root\spine-tasks\SP-064-harness-refuse-unsealed\evidence"
$exe = "$root\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe"
$real = "$env:APPDATA\CcpClient"
$scratch = Join-Path $env:TEMP ("ccp-sp064-sealed-" + [Guid]::NewGuid().ToString("N"))
$tx = "$ev\run-transcript.log"
"" | Out-File $tx
$verdict = 0
function Note($m) { $m | Out-File $tx -Append }

# Screens (owner DISPLAY3 convention; loud fallback per SP-057's amendment).
Add-Type -AssemblyName System.Windows.Forms
$screens = [System.Windows.Forms.Screen]::AllScreens
Note ("attached screens: " + (($screens | ForEach-Object { $_.DeviceName + ' ' + $_.Bounds.ToString() }) -join '; '))
$placeX = -2576; $placeY = 1091
if (-not ($screens | Where-Object { $_.Bounds.Contains(-2576, 1091) })) {
  $placeX = 100; $placeY = 100
  Note "DISPLAY3 ABSENT in this session — window placement falls back to DISPLAY1 (100,100); named in record.md"
}

# ---------- (b-pre) PRE manifest of the REAL profile (app not running) ----------
pwsh -NoProfile -File "$ev\manifest.ps1" -Root $real -Out "$ev\pre-manifest.json" -DisplayRoot "%APPDATA%\CcpClient (path-hashed for privacy — SP-064)" *>&1 | Out-File $tx -Append

# ---------- (a) REFUSAL, real process, unsealed ----------
Remove-Item Env:CCP_DATA_ROOT -ErrorAction SilentlyContinue
"CCP_DATA_ROOT at (a) launch: [$env:CCP_DATA_ROOT]" | Out-File $tx -Append
$pa = Start-Process $exe -PassThru -ArgumentList "--dtrh-m2test" `
  -RedirectStandardError "$ev\run-a-refusal.stderr.log" -RedirectStandardOutput "$ev\run-a-refusal.stdout.log"
$exited = $pa.WaitForExit(15000)
$pa.Refresh()
Note "(a) exited within 15s: $exited; EXIT=$($pa.ExitCode)"
if (-not $exited) { $pa.Kill(); Note "(a) FAIL: process did not exit — no refusal happened"; $verdict = 1 }
if ($pa.ExitCode -ne 3) { Note "(a) FAIL: exit code $($pa.ExitCode), expected 3"; $verdict = 1 }
$errA = Get-Content "$ev\run-a-refusal.stderr.log" -Raw
foreach ($needle in @("refusing to start", "CCP_DATA_ROOT", "--dtrh-m2test", "HARNESS-ONLY")) {
  $hit = $errA.Contains($needle)
  Note "positive control [$hit]: (a) stderr contains '$needle'"
  if (-not $hit) { $verdict = 1 }
}
$stillUp = Get-Process CcpClient.Desktop -ErrorAction SilentlyContinue
Note "positive control [$($null -eq $stillUp)]: no CcpClient.Desktop process/window survives the refusal"
if ($stillUp) { $verdict = 1 }

# ---------- (b) POST manifest + both-directions diff ----------
pwsh -NoProfile -File "$ev\manifest.ps1" -Root $real -Out "$ev\post-refusal-manifest.json" -DisplayRoot "%APPDATA%\CcpClient (path-hashed for privacy — SP-064)" *>&1 | Out-File $tx -Append
pwsh -NoProfile -File "$ev\diff.ps1" -Pre "$ev\pre-manifest.json" -Post "$ev\post-refusal-manifest.json" *>&1 | Tee-Object -FilePath "$ev\diff-refusal-verdict.txt" | Out-File $tx -Append
if ($LASTEXITCODE -ne 0) { Note "(b) FAIL: real profile changed across the refusal run"; $verdict = 1 }

# ---------- (c) SEALED harness run still works ----------
$env:CCP_DATA_ROOT = $scratch
$pc = Start-Process $exe -PassThru -ArgumentList "--dtrh-demo --dtrh-quick --dtrh-m2test --dtrh-auto-close 60" `
  -RedirectStandardError "$ev\run-c-sealed.stderr.log" -RedirectStandardOutput "$ev\run-c-sealed.stdout.log"
Note "(c) launched pid=$($pc.Id) with CCP_DATA_ROOT=$scratch"
$seen = $false
for ($i = 0; $i -lt 20; $i++) {
  Start-Sleep -Seconds 2
  if ((Test-Path "$ev\run-c-sealed.stderr.log") -and (Get-Content "$ev\run-c-sealed.stderr.log" -Raw).Contains("M2 TEST MODE")) { $seen = $true; break }
}
$pc.WaitForExit(90000) | Out-Null
$pc.Refresh()
Note "(c) EXIT=$($pc.ExitCode); M2 TEST MODE observed: $seen"
$errC = Get-Content "$ev\run-c-sealed.stderr.log" -Raw
foreach ($needle in @("data-root override active: CCP_DATA_ROOT", "M2 TEST MODE")) {
  $hit = $errC.Contains($needle)
  Note "positive control [$hit]: (c) stderr contains '$needle'"
  if (-not $hit) { $verdict = 1 }
}
if ($errC.Contains("refusing to start")) { Note "(c) FAIL: sealed run was refused"; $verdict = 1 }
pwsh -NoProfile -File "$ev\manifest.ps1" -Root $scratch -Out "$ev\sealed-root-manifest.json" -PlainPaths *>&1 | Out-File $tx -Append
# SP-057's established control set: slot index + dtrh data dir + wv2-profile*. settings.json
# is deliberately NOT a control — a run that never mutates DemoSettings never saves it
# (SP-010: even a fresh plain launch creates no settings.json); absence is expected, named
# in record.md, and the sealed root's 300+ files are the persistence proof.
foreach ($ctl in @("$scratch\dtrh_slots.json", "$scratch\dtrh")) {
  $ok = Test-Path $ctl
  Note "positive control [$ok]: sealed root populated: $(Split-Path $ctl -Leaf)"
  if (-not $ok) { $verdict = 1 }
}
$wv2 = Get-ChildItem "$scratch\dtrh" -Directory -Filter "wv2-profile*" -ErrorAction SilentlyContinue
Note "positive control [$($wv2.Count -gt 0)]: wv2-profile* present in sealed root ($($wv2.Count) dirs)"
if ($wv2.Count -eq 0) { $verdict = 1 }
Note "informational: settings.json present in sealed root: $(Test-Path "$scratch\settings.json") (expected False — no settings mutation; SP-010)"
Remove-Item Env:CCP_DATA_ROOT

# ---------- (d) PLAIN launch, unsealed — normal-launch non-regression ----------
$pd = Start-Process $exe -PassThru `
  -RedirectStandardError "$ev\run-d-plain.stderr.log" -RedirectStandardOutput "$ev\run-d-plain.stdout.log"
Note "(d) launched plain pid=$($pd.Id), CCP_DATA_ROOT=[$env:CCP_DATA_ROOT]"
Start-Sleep -Seconds 10
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\run-d-plain-window.png" -TitleLike "CCP Client" -X $placeX -Y $placeY *>&1 | Out-File $tx -Append
if ($LASTEXITCODE -ne 0) { Note "(d) FAIL: no rect-verified 'CCP Client' window"; $verdict = 1 }
$errDsofar = Get-Content "$ev\run-d-plain.stderr.log" -Raw
if ($errDsofar.Contains("refusing to start")) { Note "(d) FAIL: plain launch was refused"; $verdict = 1 }
$pd.CloseMainWindow() | Out-Null
$closed = $pd.WaitForExit(15000)
$pd.Refresh()
Note "(d) CloseMainWindow -> exited: $closed; EXIT=$($pd.ExitCode)"
if (-not $closed) { $pd.Kill(); Note "(d) FAIL: window close did not exit the app"; $verdict = 1 }
if ($pd.ExitCode -ne 0) { Note "(d) FAIL: exit $($pd.ExitCode), expected 0"; $verdict = 1 }

# ---------- (d-delta) honest profile delta across the plain launch ----------
pwsh -NoProfile -File "$ev\manifest.ps1" -Root $real -Out "$ev\post-plain-manifest.json" -DisplayRoot "%APPDATA%\CcpClient (path-hashed for privacy — SP-064)" *>&1 | Out-File $tx -Append
pwsh -NoProfile -File "$ev\diff.ps1" -Pre "$ev\post-refusal-manifest.json" -Post "$ev\post-plain-manifest.json" *>&1 | Tee-Object -FilePath "$ev\diff-plain-verdict.txt" | Out-File $tx -Append
# Not a failure either way — reported HONESTLY (SP-010: a plain launch is expected to write
# nothing; if something changed it is named in the record, never suppressed).
Note "(d) plain-launch profile delta verdict above (exit $LASTEXITCODE; informational — named in record.md)"

Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
Note "OVERALL VERDICT EXIT=$verdict (0 = refuse-unsealed proven + profile byte-identical + sealed works + plain unaffected)"
Write-Output "OVERALL VERDICT EXIT=$verdict"
exit $verdict
