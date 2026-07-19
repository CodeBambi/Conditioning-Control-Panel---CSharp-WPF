# CCP greenfield verification harness - seeded-regression self-test (SP-008).
# Proves the targeted gate catches a REAL visual regression, throwaway-edit pattern
# (SP-007 AVLN2000 precedent; NO defect-injection flags in product code):
#   1. edit the REAL MainWindow.axaml — break the lit border brush
#   2. build, capture dashboard-card/lit, assert CcpVerify FAILS the SPECIFIC NAMED check
#   3. restore (git checkout), rebuild, re-capture, assert green
# Re-runnable: pwsh client/tools/verify/self-test.ps1
$ErrorActionPreference = 'Stop'

$verifyDir = $PSScriptRoot
$clientDir = Resolve-Path (Join-Path $verifyDir '..\..')
$axaml = Join-Path $clientDir 'src\CcpClient.Desktop\Views\MainWindow.axaml'
$verify = Join-Path $clientDir 'tools\verify\CcpVerify\bin\Debug\net10.0\CcpVerify.exe'
$manifest = Join-Path $verifyDir 'checks.json'
$capture = Join-Path $verifyDir 'artifacts\windows-dashboard-card-lit.png'

function Fail([string]$msg) {
    Write-Output "SELF-TEST FAIL: $msg"
    # Never leave the product source mutated.
    git -C $clientDir checkout -- 'src/CcpClient.Desktop/Views/MainWindow.axaml' 2>$null
    exit 1
}

$original = [IO.File]::ReadAllText($axaml)
if ($original -notmatch '#FFE066FF') { Fail "AXAML does not contain the lit brush #FFE066FF - self-test anchor missing" }

Write-Output '--- phase 1: seed the regression (lit border brush -> #FF336633) ---'
[IO.File]::WriteAllText($axaml, ($original -replace '#FFE066FF', '#FF336633'), [Text.UTF8Encoding]::new($false))

try {
    dotnet build (Join-Path $clientDir 'CcpClient.sln') -c Debug --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail 'build failed with seeded regression' }

    & (Join-Path $verifyDir 'capture.ps1') -Surface dashboard-card -State lit | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail 'capture failed with seeded regression' }

    $output = & $verify --capture $capture --surface dashboard-card --state lit --manifest $manifest 2>&1 | Out-String
    Write-Output ($output.Trim())
    if ($LASTEXITCODE -ne 2) { Fail "expected exit 2 with seeded regression, got $LASTEXITCODE" }
    if ($output -notmatch 'FIRST FAILED CHECK: dashboard-card-lit-border') {
        Fail "the SPECIFIC named check did not trip: $output"
    }
    Write-Output 'seeded regression caught by the SPECIFIC named check (exit 2)'
}
finally {
    Write-Output '--- phase 2: restore ---'
    git -C $clientDir checkout -- 'src/CcpClient.Desktop/Views/MainWindow.axaml'
    if ($LASTEXITCODE -ne 0) { Fail 'git restore failed - AXAML may still be mutated' }
}

dotnet build (Join-Path $clientDir 'CcpClient.sln') -c Debug --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { Fail 'rebuild after restore failed' }

& (Join-Path $verifyDir 'capture.ps1') -Surface dashboard-card -State lit | Out-Null
if ($LASTEXITCODE -ne 0) { Fail 'capture after restore failed' }

$output = & $verify --capture $capture --surface dashboard-card --state lit --manifest $manifest 2>&1 | Out-String
Write-Output ($output.Trim())
if ($LASTEXITCODE -ne 0) { Fail "restored build did not return green (exit $LASTEXITCODE)" }
if ($output -notmatch 'ALL CHECKS PASSED') { Fail "restored build did not return green: $output" }

Write-Output 'restored build green'
Write-Output 'SELF-TEST PASS'
