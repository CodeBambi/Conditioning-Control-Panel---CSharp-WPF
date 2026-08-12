# SP-057 byte-identity run: a REAL headed DTRH host run (incl. --dtrh-m2test) under
# CCP_DATA_ROOT, bracketed by pre/post manifests of the REAL user data directory.
# Claim under test: the real %APPDATA%\CcpClient is BYTE-IDENTICAL after the run and the
# override root is demonstrably populated. The negative case is NOT executed against the
# live profile (reasoned: trap proof + unset-env unit test) — PROMPT Step 3.
# Owner DISPLAY3 convention: the window is moved to (-2576,1091) and rect-verified
# before capture (rect line appended into the committed transcript, SP-026 binding).
$ErrorActionPreference = "Stop"
$root = "C:\Code\Conditioning-Control-Panel---CSharp-WPF\.worktrees\spine-20260812T053518\lane-1"
$ev = "$root\spine-tasks\SP-057-profile-isolation-seam\evidence"
$exe = "$root\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe"
$real = "$env:APPDATA\CcpClient"
$sandbox = "$ev\override-root"
$tx = "$ev\run-drive.log"
"" | Out-File $tx

# 0. Clean slate for the override root (fresh sandbox; committed evidence is manifests+logs).
if (Test-Path $sandbox) { Remove-Item $sandbox -Recurse -Force }

# 1. PRE manifest of the REAL user data directory (app not running). Paths are
# sha256-hashed inside the manifest (owner file names never enter git).
pwsh -NoProfile -File "$ev\manifest.ps1" -Root $real -Out "$ev\pre-manifest.json" -DisplayRoot "%APPDATA%\CcpClient (path-hashed for privacy — SP-057)" *>&1 | Out-File $tx -Append

# 2. Headed run under the override: DTRH host + m2test (the SP-052 hazard class).
$env:CCP_DATA_ROOT = $sandbox
$proc = Start-Process $exe -PassThru -ArgumentList "--dtrh-demo --dtrh-quick --dtrh-m2test --dtrh-auto-close 80" `
  -RedirectStandardError "$ev\run.log" -RedirectStandardOutput "$ev\run.out.log"
"launched pid=$($proc.Id) with CCP_DATA_ROOT=$sandbox" | Out-File $tx -Append
Start-Sleep -Seconds 8

# 3. Display placement + verification, then a capture (headed-ness proof). Owner
# convention is DISPLAY3 (-2576,1091); when the session has no DISPLAY3 (2026-08-12:
# only DISPLAY1 2880x1800 attached) the window is placed at a visible DISPLAY1 point
# and the fallback is recorded in the transcript — an off-screen "verified" rect is
# NOT evidence (the window manager accepts any coordinates).
Add-Type -AssemblyName System.Windows.Forms
$screens = [System.Windows.Forms.Screen]::AllScreens
"attached screens: $($screens | ForEach-Object { $_.DeviceName + ' ' + $_.Bounds.ToString() })" | Out-File $tx -Append
$placeX = -2576; $placeY = 1091
if (-not ($screens | Where-Object { $_.Bounds.Contains(-2576, 1091) })) {
  $placeX = 100; $placeY = 100
  "DISPLAY3 ABSENT in this session — falling back to DISPLAY1 (100,100); the DISPLAY3 convention gate is named in record.md" | Out-File $tx -Append
}
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\run-host-live.png" -TitleLike "Down the Rabbit Hole" -X $placeX -Y $placeY *>&1 | Out-File $tx -Append

# 4. Poll for M2TEST DONE (m2test timeline: ~3s settle + ~16s payloads + meta walk + payout).
$done = $false
for ($i = 0; $i -lt 24; $i++) {
  Start-Sleep -Seconds 2
  if ((Get-Content "$ev\run.log" -Raw) -match "M2TEST DONE") { $done = $true; break }
}
"m2test done observed: $done" | Out-File $tx -Append
pwsh -NoProfile -File "$ev\drive.ps1" -Action capture -Arg "$ev\run-after-m2test.png" -TitleLike "Down the Rabbit Hole" -NoMove *>&1 | Out-File $tx -Append

$proc.WaitForExit(120000) | Out-Null
$proc.Refresh()
"EXIT=$($proc.ExitCode)" | Out-File $tx -Append
$env:CCP_DATA_ROOT = $null

# 5. POST manifest + both-directions diff (app exited — no in-flight writer).
pwsh -NoProfile -File "$ev\manifest.ps1" -Root $real -Out "$ev\post-manifest.json" -DisplayRoot "%APPDATA%\CcpClient (path-hashed for privacy — SP-057)" *>&1 | Out-File $tx -Append
pwsh -NoProfile -File "$ev\diff.ps1" -Pre "$ev\pre-manifest.json" -Post "$ev\post-manifest.json" *>&1 | Tee-Object -FilePath "$ev\diff-verdict.txt" | Out-File $tx -Append
$diffExit = $LASTEXITCODE

# 6. Positive controls (consult c hole 2 — a crash-at-startup also leaves the profile
# untouched; byte-identity is vacuous without proof the run really persisted somewhere).
pwsh -NoProfile -File "$ev\manifest.ps1" -Root $sandbox -Out "$ev\override-manifest.json" -PlainPaths *>&1 | Out-File $tx -Append
$controls = @(
  @{ path = "$sandbox\dtrh_slots.json"; why = "slot INDEX (the file SP-052 Run A clobbered)" },
  @{ path = "$sandbox\dtrh"; why = "dtrh data dir" }
)
foreach ($c in $controls) {
  $ok = Test-Path $c.path
  "positive control [$($ok)]: $($c.path) — $($c.why)" | Out-File $tx -Append
  if (-not $ok) { $diffExit = 1 }
}
$wv2 = Get-ChildItem "$sandbox\dtrh" -Directory -Filter "wv2-profile*" -ErrorAction SilentlyContinue
"positive control [$($wv2.Count -gt 0)]: wv2-profile* present ($($wv2.Count) dirs)" | Out-File $tx -Append
if ($wv2.Count -eq 0) { $diffExit = 1 }
$logRaw = Get-Content "$ev\run.log" -Raw
foreach ($needle in @("data-root override active: CCP_DATA_ROOT", "M2 TEST MODE", "meta engine bound to slot")) {
  $hit = $logRaw.Contains($needle)
  "positive control [$hit]: run.log contains '$needle'" | Out-File $tx -Append
  if (-not $hit) { $diffExit = 1 }
}
# m2test baseline pin (SP-057 pre-completion consult pin 2): the accepted outcome on a
# FRESH declared fixture is EXACTLY 7/8 with the single FAIL being meta-commands rev +19
# (the 19th bump is page-originated narrative traffic; the engine applies exactly the 18
# modeled ops — pinned by M2TestOpSequence_OffFixture_AppliesExactlyTheModeledEighteen).
# Anything else fails this script: a second failing check, a different rev delta, 6/8 —
# or a green 8/8 (which would mean the fixture stopped being fresh).
$m2Fails = [regex]::Matches($logRaw, "M2TEST FAIL ([^ ]+) ([^\r\n]+)")
$baselineOk = $logRaw.Contains("M2TEST DONE: FAILURES PRESENT (7/8)") `
  -and $m2Fails.Count -eq 1 `
  -and $m2Fails[0].Groups[1].Value -eq "meta-commands" `
  -and $m2Fails[0].Groups[2].Value -match "^rev \+19 \(expected 18" `
  -and -not $logRaw.Contains("M2TEST DONE: ALL PASS")
"positive control [$baselineOk]: m2test baseline is exactly 7/8 with the single explained meta-commands +19 FAIL ($($m2Fails.Count) FAIL lines)" | Out-File $tx -Append
if (-not $baselineOk) { $diffExit = 1 }
"OVERALL VERDICT EXIT=$diffExit (0 = byte-identical real profile + populated override root)" | Out-File $tx -Append
Write-Output "OVERALL VERDICT EXIT=$diffExit"
exit $diffExit
