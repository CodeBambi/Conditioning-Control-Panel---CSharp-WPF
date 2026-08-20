# CCP greenfield verification harness - seeded-regression self-test (SP-008).
# Proves the targeted gate catches a REAL visual regression, throwaway-edit pattern
# (SP-007 AVLN2000 precedent; NO defect-injection flags in product code):
#   1. edit the REAL MainWindow.axaml — break the SELECTED rail-door border brush
#   2. build, capture rail-door/selected, assert CcpVerify FAILS the SPECIFIC NAMED check
#   3. restore (from the bytes captured in memory, never git), rebuild, re-capture, assert green
#
# SP-122: the SAME seed covers the RACK, and it costs one extra capture per build to prove it.
# #FFE066FF is not door-specific — it is also RadioButton.rack-row:checked's BorderBrush and
# Ellipse.dot.live's Fill (MainWindow.axaml:68-71, :101-105, :390-393) — so one throwaway edit
# breaks the rail door's selected border AND the rack row's selection marker, and both named
# checks must trip on it. That is what separates a check that catches a REAL regression in product
# markup from one that only distinguishes two captures of a working build.
# Re-runnable: pwsh client/tools/verify/self-test.ps1
$ErrorActionPreference = 'Stop'

$verifyDir = $PSScriptRoot
$clientDir = Resolve-Path (Join-Path $verifyDir '..\..')
$axaml = Join-Path $clientDir 'src\CcpClient.Desktop\Views\MainWindow.axaml'
$verify = Join-Path $clientDir 'tools\verify\CcpVerify\bin\Debug\net10.0\CcpVerify.exe'
$manifest = Join-Path $verifyDir 'checks.json'
$capture = Join-Path $verifyDir 'artifacts\windows-rail-door-selected.png'
$rackCapture = Join-Path $verifyDir 'artifacts\windows-rack-row-selected.png'

# RESTORE FROM MEMORY, NEVER FROM GIT (SP-094 near-miss, 2026-08-18).
# This used `git checkout -- MainWindow.axaml` in both the failure path and the finally
# block. That restores the COMMITTED content, so running the self-test on a tree with
# uncommitted edits to that file DISCARDS them -- silently, and before anything is read.
# It ate a lane's in-progress rail markup mid-run and the lane had to reconstruct it.
# A verification harness must never be able to destroy the work it is verifying.
#
# The exact bytes are captured here, before any mutation, and every restore path writes
# them back. That is also STRICTER than git: it restores what was actually there, so an
# uncommitted work-in-progress survives the run untouched.
$original = [IO.File]::ReadAllText($axaml)

function Restore-Axaml {
    [IO.File]::WriteAllText($axaml, $original, [Text.UTF8Encoding]::new($false))
}

function Fail([string]$msg) {
    Write-Output "SELF-TEST FAIL: $msg"
    # Never leave the product source mutated -- and never discard anything either.
    Restore-Axaml
    exit 1
}

if ($original -notmatch '#FFE066FF') { Fail "AXAML does not contain the selected-door brush #FFE066FF - self-test anchor missing" }

Write-Output '--- phase 1: seed the regression (selected-door border brush -> #FF336633) ---'
[IO.File]::WriteAllText($axaml, ($original -replace '#FFE066FF', '#FF336633'), [Text.UTF8Encoding]::new($false))

try {
    dotnet build (Join-Path $clientDir 'CcpClient.sln') -c Debug --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail 'build failed with seeded regression' }

    & (Join-Path $verifyDir 'capture.ps1') -Surface rail-door -State selected | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail 'capture failed with seeded regression' }

    $output = & $verify --capture $capture --surface rail-door --state selected --manifest $manifest 2>&1 | Out-String
    Write-Output ($output.Trim())
    if ($LASTEXITCODE -ne 2) { Fail "expected exit 2 with seeded regression, got $LASTEXITCODE" }
    if ($output -notmatch 'FIRST FAILED CHECK: rail-door-selected-border') {
        Fail "the SPECIFIC named check did not trip: $output"
    }
    Write-Output 'seeded regression caught by the SPECIFIC named check (exit 2)'

    # SP-122 — the same seeded build, read at the RACK. The rack row's selection marker uses the
    # same brush, so it must trip too, and it must trip by its OWN name.
    & (Join-Path $verifyDir 'capture.ps1') -Surface rack-row -State selected | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail 'rack-row capture failed with seeded regression' }

    $rackOutput = & $verify --capture $rackCapture --surface rack-row --state selected --manifest $manifest 2>&1 | Out-String
    Write-Output ($rackOutput.Trim())
    if ($LASTEXITCODE -ne 2) { Fail "expected exit 2 from the rack with seeded regression, got $LASTEXITCODE" }
    if ($rackOutput -notmatch 'FIRST FAILED CHECK: rack-row-selected-marker') {
        Fail "the rack's SPECIFIC named check did not trip: $rackOutput"
    }
    Write-Output 'seeded regression ALSO caught at the rack, by its own named check (exit 2)'
}
finally {
    Write-Output '--- phase 2: restore ---'
    Restore-Axaml
    # Prove the restore, rather than trusting the write: the seeded brush must be gone and
    # the anchor back. A silent partial restore would leave the product mutated and the
    # remaining phases would then be measuring the wrong tree.
    $restored = [IO.File]::ReadAllText($axaml)
    if ($restored -ne $original) { Fail 'restore did not reproduce the original AXAML byte-for-byte' }
    if ($restored -match '#FF336633') { Fail 'the seeded brush survived the restore - AXAML is still mutated' }
}

dotnet build (Join-Path $clientDir 'CcpClient.sln') -c Debug --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { Fail 'rebuild after restore failed' }

& (Join-Path $verifyDir 'capture.ps1') -Surface rail-door -State selected | Out-Null
if ($LASTEXITCODE -ne 0) { Fail 'capture after restore failed' }

$output = & $verify --capture $capture --surface rail-door --state selected --manifest $manifest 2>&1 | Out-String
Write-Output ($output.Trim())
if ($LASTEXITCODE -ne 0) { Fail "restored build did not return green (exit $LASTEXITCODE)" }
if ($output -notmatch 'ALL CHECKS PASSED') { Fail "restored build did not return green: $output" }

& (Join-Path $verifyDir 'capture.ps1') -Surface rack-row -State selected | Out-Null
if ($LASTEXITCODE -ne 0) { Fail 'rack-row capture after restore failed' }

$rackOutput = & $verify --capture $rackCapture --surface rack-row --state selected --manifest $manifest 2>&1 | Out-String
Write-Output ($rackOutput.Trim())
if ($LASTEXITCODE -ne 0) { Fail "restored build did not return green at the rack (exit $LASTEXITCODE)" }
if ($rackOutput -notmatch 'ALL CHECKS PASSED') { Fail "restored build did not return green at the rack: $rackOutput" }

Write-Output 'restored build green at both the rail door and the rack'
Write-Output 'SELF-TEST PASS'
