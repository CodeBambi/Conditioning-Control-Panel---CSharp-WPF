# SP-058 byte-identity run: a REAL headed Graded Intake host run (--intake-demo +
# --intake-drive) under CCP_DATA_ROOT, bracketed by pre/post manifests of the REAL user
# data directory (the SP-057 bracket, reused verbatim as mandated by PROMPT framing e).
# Claim under test: the real %APPDATA%\CcpClient is BYTE-IDENTICAL after the run, the
# override root is demonstrably populated, and the drive transcript carries the v6.7.x
# delta proofs (serve-probe 200 + 404 negative control, top-marks boundary verdict).
# Claim scope: %APPDATA%\CcpClient ONLY (WebView2/LibVLC may write under
# LocalAppData/Temp — stated, not swept).
# Owner DISPLAY3 convention: (-2576,1091) rect-verified before capture; when DISPLAY3 is
# absent, probe Screen.AllScreens and fall back loudly to a visible point (never capture
# black at an unattached origin — the SP-057 amendment disposition).
$ErrorActionPreference = "Stop"
$root = "C:\Code\Conditioning-Control-Panel---CSharp-WPF\.worktrees\spine-20260812T072253\lane-1"
$ev = "$root\spine-tasks\SP-058-graded-intake-v67-delta\evidence"
$exe = "$root\client\src\CcpClient.Desktop\bin\Debug\net10.0\CcpClient.Desktop.exe"
$real = "$env:APPDATA\CcpClient"
$sandbox = "$ev\override-root"
$tx = "$ev\run-drive.log"
"" | Out-File $tx

# 0. Clean slate for the override root (fresh sandbox; committed evidence is manifests+logs).
if (Test-Path $sandbox) { Remove-Item $sandbox -Recurse -Force }

# 1. PRE manifest of the REAL user data directory (app not running). Paths are
# sha256-hashed inside the manifest (owner file names never enter git).
pwsh -NoProfile -File "$ev\manifest.ps1" -Root $real -Out "$ev\pre-manifest.json" -DisplayRoot "%APPDATA%\CcpClient (path-hashed for privacy — SP-057 bracket reused by SP-058)" *>&1 | Out-File $tx -Append

# 2. Headed run under the override: intake host + the v6.7.x delta drive.
#    serve-probe 200 (accents.js) + 404 negative control; quiz-result:topmarks = the
#    exactly-90.0 boundary run; exit = the graceful close. The drive string carries NO
#    SPACES (Start-Process joins -ArgumentList with spaces unquoted — a spaced step list
#    arrives as separate args and silently truncates the script; first-run lesson).
$env:CCP_DATA_ROOT = $sandbox
$proc = Start-Process $exe -PassThru -ArgumentList @(
  "--intake-demo",
  "--intake-drive", "serve-probe:core/accents.js@14;serve-probe:core/accents-missing.js@17;quiz-result:topmarks@22;exit@44",
  "--intake-auto-close", "100"
) -RedirectStandardError "$ev\run.log" -RedirectStandardOutput "$ev\run.out.log"
"launched pid=$($proc.Id) with CCP_DATA_ROOT=$sandbox" | Out-File $tx -Append

# 3. Display placement + verification, then a capture (headed-ness proof). Owner
# convention is DISPLAY3 (-2576,1091); when the session has no DISPLAY3 the window is
# placed at a visible DISPLAY1 point and the fallback is recorded — an off-screen
# "verified" rect is NOT evidence. The window takes a few seconds to title itself —
# retry the capture until it lands (first run: no titled window at 8s).
Add-Type -AssemblyName System.Windows.Forms
$screens = [System.Windows.Forms.Screen]::AllScreens
"attached screens: $($screens | ForEach-Object { $_.DeviceName + ' ' + $_.Bounds.ToString() })" | Out-File $tx -Append
$placeX = -2576; $placeY = 1091
if (-not ($screens | Where-Object { $_.Bounds.Contains(-2576, 1091) })) {
  $placeX = 100; $placeY = 100
  "DISPLAY3 ABSENT in this session — falling back to DISPLAY1 (100,100); the DISPLAY3 convention gate is named in record.md" | Out-File $tx -Append
}
# The window boots small and layouts up over the first seconds — a capture without a
# size floor catches a 232x64 title-bar slice (second-run lesson). Require a plausible
# rect before accepting.
Start-Sleep -Seconds 12
$captured = $false
for ($i = 0; $i -lt 6 -and -not $captured; $i++) {
  $capOut = pwsh -NoProfile -File "$ev\capture-intake.ps1" -Out "$ev\run-host-live.png" -X $placeX -Y $placeY *>&1
  $capOut | Out-File $tx -Append
  $rectLine = ($capOut | Select-String "^rect (\d+),(\d+) (\d+)x(\d+)")
  $wide = $rectLine -and [int]$rectLine.Matches[0].Groups[3].Value -gt 800
  $captured = $wide -and (Test-Path "$ev\run-host-live.png") -and ((Get-Item "$ev\run-host-live.png").Length -gt 0)
  if (-not $captured) { Start-Sleep -Seconds 5 }
}
"capture landed (width-floored): $captured" | Out-File $tx -Append

# 4. Poll for the drive's DONE-equivalent: the graceful-exit line (exit@44) or process
# exit via auto-close, whichever first.
$done = $false
for ($i = 0; $i -lt 45; $i++) {
  Start-Sleep -Seconds 2
  if ($proc.HasExited) { $done = $true; break }
  if ((Get-Content "$ev\run.log" -Raw -ErrorAction SilentlyContinue) -match "intake: exit received|exit-done|graceful exit") { $done = $true; break }
}
"drive completion observed: $done" | Out-File $tx -Append

$proc.WaitForExit(120000) | Out-Null
$proc.Refresh()
"EXIT=$($proc.ExitCode)" | Out-File $tx -Append
$env:CCP_DATA_ROOT = $null

# 5. POST manifest + both-directions diff (app exited — no in-flight writer).
pwsh -NoProfile -File "$ev\manifest.ps1" -Root $real -Out "$ev\post-manifest.json" -DisplayRoot "%APPDATA%\CcpClient (path-hashed for privacy — SP-057 bracket reused by SP-058)" *>&1 | Out-File $tx -Append
pwsh -NoProfile -File "$ev\diff.ps1" -Pre "$ev\pre-manifest.json" -Post "$ev\post-manifest.json" *>&1 | Tee-Object -FilePath "$ev\diff-verdict.txt" | Out-File $tx -Append
$diffExit = $LASTEXITCODE

# 6. Positive controls (a crash-at-startup also leaves the profile untouched; byte-
# identity is vacuous without proof the run really persisted somewhere AND did the work).
pwsh -NoProfile -File "$ev\manifest.ps1" -Root $sandbox -Out "$ev\override-manifest.json" -PlainPaths *>&1 | Out-File $tx -Append
$controls = @(
  @{ path = "$sandbox\intake_settings.json"; why = "pass spend + fullscreen persist (the intake store)" },
  @{ path = "$sandbox\intake_punchcard.json"; why = "punch card (first hole + pending draft)" },
  @{ path = "$sandbox\intake\drafted_sessions"; why = "the drafted-session sink" }
)
foreach ($c in $controls) {
  $ok = Test-Path $c.path
  "positive control [$ok]: $($c.path) — $($c.why)" | Out-File $tx -Append
  if (-not $ok) { $diffExit = 1 }
}
$logRaw = Get-Content "$ev\run.log" -Raw
foreach ($needle in @(
  "data-root override active: CCP_DATA_ROOT",
  "intake: init sent",
  "intake: serve-probe GET /dtrh/core/accents.js -> 200",
  "intake: serve-probe GET /dtrh/core/accents-missing.js -> 404",
  "intake: graded verdict"
)) {
  $hit = $logRaw.Contains($needle)
  "positive control [$hit]: run.log contains '$needle'" | Out-File $tx -Append
  if (-not $hit) { $diffExit = 1 }
}
# The boundary pin: the top-marks run is 9/10 = EXACTLY 90.0 and the verdict must be True.
$boundaryOk = $logRaw.Contains("top-marks True (90% of max; category bambi; mantra credit x1)")
"positive control [$boundaryOk]: top-marks boundary verdict line is exactly True at 90%" | Out-File $tx -Append
if (-not $boundaryOk) { $diffExit = 1 }
"OVERALL VERDICT EXIT=$diffExit (0 = byte-identical real profile + populated override root + delta proofs present)" | Out-File $tx -Append
exit $diffExit
