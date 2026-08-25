# CCP greenfield verification harness - seeded-regression self-test.
# Proves the targeted gate catches a REAL visual regression, throwaway-edit pattern
# (AVLN2000 precedent; NO defect-injection flags in product code):
#   1. edit the REAL Themes/CcpTheme.cs — break the SELECTED rail-door border colour
#   2. build, capture rail-door/selected, assert CcpVerify FAILS the SPECIFIC NAMED check
#   3. restore (from the bytes captured in memory, never git), rebuild, re-capture, assert green
#
# THE ANCHOR HAS MOVED TWICE, AND EACH MOVE FOLLOWED THE COLOUR A USER ACTUALLY SEES.
# It first seeded Views/MainWindow.axaml, where #FFFF8FAF was pasted at three sites. The token
# layer arrived and the colour was declared ONCE, as `ShellAccentBright` in
# src/CcpClient.Desktop/Themes/Ccp.axaml, so the seed moved there - strictly stronger, because it
# then reached every consumer of the key rather than the three sites that happened to carry the
# literal.
#
# THEN THE PRODUCT LEARNED TO THEME ITSELF (2026-08-26) AND Ccp.axaml BECAME A DESIGN-TIME SEED.
# "CCP Default" is itself a mod - that is a fact about the SHIPPING product, measured headed
# (WPF Models/BuiltInMods.cs:918-926, applied at MainWindow/MainWindow.xaml.cs:317) - and the port
# does the same thing: CcpTheme.CcpDefault.ApplyTo rewrites `ShellAccentBright` from the theme's
# AccentLightColor in App.Initialize, before any window exists.
#
# SO A SEED IN Ccp.axaml WOULD NOW REACH NO PIXEL AT ALL. It would be painted over three lines into
# startup, both named checks would stay GREEN on the seeded build, and this script would report
# that its own regression detector had stopped working - a red for the wrong reason, on a product
# with no defect in it. The anchor is therefore the AccentLight line of CcpTheme.CcpDefault, which
# is where the colour a user sees is declared. The claim is unchanged: the two named checks below
# still have to trip, and that is what separates a check that catches a REAL regression in product
# source from one that only distinguishes two captures of a working build.
#
# The SAME seed covers the RACK, and it costs one extra capture per build to prove it.
# `ShellAccentBright` is not door-specific — it is also RadioButton.rack-row:checked's BorderBrush
# and Ellipse.dot.live's Fill (MainWindow.axaml:68-71, :101-105, :390-393 declare the three
# styles) — so one throwaway edit breaks the rail door's selected border AND the rack row's
# selection marker.
# Re-runnable: pwsh client/tools/verify/self-test.ps1
#
# =================================================================================================
# THE SWEEP, and the policy decision behind it being OPT-IN.
#
# `pwsh client/tools/verify/self-test.ps1 -Sweep` drives EVERY surface and state capture.ps1 can
# bind, runs each one's named checks, and reports a table.
#
# WHY IT EXISTS. This script drives two surfaces of nineteen, and that is how a broken surface
# rotted unseen for a day: `companion-transcript` stopped capturing in BOTH states and nothing
# noticed, because ONLY A HEADED RUN CATCHES A SURFACE THAT HAS STOPPED CAPTURING. No unit test, no
# headless frame and no build can: the failure is a real window on a real desktop refusing to
# photograph.
#
# WHY IT IS OPT-IN, which is the decision this row asked for - AND THE NUMBER THAT DECIDED IT IS
# NOT THE ONE I EXPECTED. The estimate going in was "the better part of an hour, so nobody will run
# it". MEASURED, whole sweep, this machine: 7.2 MINUTES for all 36 pairs. Most captures are 6-19 s
# and only two are slow, both for honest reasons the surfaces exist to state - popquiz-card/asking
# at 53 s waits for a real question to come up, and session-history/kept at 52 s must leave a real
# session running past upstream's 30-second retention line.
#
# So the clock is NOT what makes this opt-in; the LEASE is. The sweep holds the machine-wide
# real-desktop lease for those 7 minutes, and every other lane's floor run and every other capture
# queues behind it - a cost paid by other people, which is exactly the kind a default should not
# impose. At 7 minutes, though, the recommendation is far stronger than an hour would have allowed:
# run it whenever you touch the harness, a shared brush or a window's geometry, and run it before a
# wave lands. The fast legs stay the thing you run every time, because they answer a different
# question (below) and cost no lease time to speak of.
#
# WHAT IT DOES NOT DO, deliberately: it does not seed a regression. The default legs prove the
# named checks BITE; the sweep proves every surface still PHOTOGRAPHS and still passes its own
# checks. Those are different questions and conflating them would multiply an hour by two.
#
# It discovers the pairs from capture.ps1's OWN $statesFor table, through the PowerShell parser
# rather than a copy of the list - a duplicated list is the same rot vector this leg exists to
# close, and a surface added to capture.ps1 joins the sweep with no edit here.
# =================================================================================================
param([switch]$Sweep)
$ErrorActionPreference = 'Stop'

$verifyDir = $PSScriptRoot
$clientDir = Resolve-Path (Join-Path $verifyDir '..\..')
$anchorFile = Join-Path $clientDir 'src\CcpClient.Desktop\Themes\CcpTheme.cs'
$verify = Join-Path $clientDir 'tools\verify\CcpVerify\bin\Debug\net10.0\CcpVerify.exe'
$manifest = Join-Path $verifyDir 'checks.json'
$capture = Join-Path $verifyDir 'artifacts\windows-rail-door-selected.png'
$rackCapture = Join-Path $verifyDir 'artifacts\windows-rack-row-selected.png'

# -------------------------------------------------------------------------------------------------
# THE SWEEP LEG. Handled here, before any of the seeded-regression machinery below reads or writes
# the product's markup: this leg mutates nothing, and a run that cannot damage the tree should not
# even open the file it would have had to restore.
# -------------------------------------------------------------------------------------------------
if ($Sweep) {
    # THE PAIRS, out of capture.ps1's own table through the PowerShell parser. Never a copy: a
    # duplicated list would go stale exactly the way the two-surface coverage this leg replaces did.
    $captureScript = Join-Path $verifyDir 'capture.ps1'
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($captureScript, [ref]$null, [ref]$null)
    $assignment = $ast.Find({
            param($node)
            $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            $node.Left.Extent.Text -eq '$statesFor'
        }, $true)
    if ($null -eq $assignment) {
        Write-Output "SWEEP FAIL: capture.ps1 no longer assigns a `$statesFor table - the sweep cannot discover what to drive"
        exit 1
    }
    $hashtable = $assignment.Right.Find({
            param($node) $node -is [System.Management.Automation.Language.HashtableAst]
        }, $true)
    $statesFor = $hashtable.SafeGetValue()
    if ($statesFor.Count -eq 0) {
        Write-Output 'SWEEP FAIL: capture.ps1 declares an EMPTY surface table - a sweep of nothing is not a sweep'
        exit 1
    }

    # HOW MANY NAMED CHECKS EACH PAIR HAS. Counted here rather than inferred from CcpVerify's exit
    # code, because a pair with no declared check is not the same fact as a pair whose checks passed
    # and this table must not print them the same way.
    $manifestJson = ((Get-Content $manifest -Raw) -replace '(?m)^\s*//.*$', '') | ConvertFrom-Json
    $checkCount = @{}
    foreach ($check in $manifestJson.checks) {
        $key = "$($check.surface)/$($check.state)"
        $checkCount[$key] = 1 + ($(if ($checkCount.ContainsKey($key)) { $checkCount[$key] } else { 0 }))
    }

    $pairs = @()
    foreach ($surface in ($statesFor.Keys | Sort-Object)) {
        foreach ($state in $statesFor[$surface]) { $pairs += , @($surface, $state) }
    }
    Write-Output "--- sweep: $($statesFor.Count) surfaces, $($pairs.Count) surface/state pairs ---"

    # ONE build for the whole sweep. capture.ps1 launches the BUILT exe, so a stale tree would have
    # every row of this table describing yesterday's product.
    dotnet build (Join-Path $clientDir 'CcpClient.sln') -c Debug --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Output 'SWEEP FAIL: the solution did not build; every capture below would be of the previous build'
        exit 1
    }

    $rows = @()
    $failed = 0
    $sweepClock = [Diagnostics.Stopwatch]::StartNew()
    # Each row is printed AS IT COMPLETES as well as collected. A sweep is tens of minutes long and
    # the app's own stdout goes straight to the console - capture.ps1 starts it with an inherited
    # handle, so it never enters this pipeline - and without a row per pair the operator cannot tell
    # a live run from a wedged one, or say which pair wedged it.
    function Add-Row([string]$row) {
        Write-Output $row
        $script:rows += $row
    }

    foreach ($pair in $pairs) {
        $surface, $state = $pair
        $key = "$surface/$state"
        $declared = if ($checkCount.ContainsKey($key)) { $checkCount[$key] } else { 0 }
        $clock = [Diagnostics.Stopwatch]::StartNew()

        $captureOut = & (Join-Path $verifyDir 'capture.ps1') -Surface $surface -State $state 2>&1 | Out-String
        $captureExit = $LASTEXITCODE
        if ($captureExit -ne 0) {
            # THE TAIL IS THE FINDING. capture.ps1's refusals are one line and they name themselves,
            # so the row carries the reason rather than an exit code the reader has to go looking up.
            $reason = ($captureOut.Trim() -split "`n" | Where-Object { $_ -match '^(FAIL|VACUOUS)' } |
                Select-Object -Last 1)
            if ([string]::IsNullOrWhiteSpace($reason)) {
                $reason = ($captureOut.Trim() -split "`n" | Select-Object -Last 1)
            }
            Add-Row "  FAIL      $key ($([int]$clock.Elapsed.TotalSeconds)s) - capture exit ${captureExit}: $($reason.Trim())"
            $failed++
            continue
        }

        if ($declared -eq 0) {
            # Not a pass and not a failure: the capture is real and non-vacuous, and no named check
            # exists to say anything more about it. Printed as its own outcome so the total below
            # cannot be read as "every pair verified" - and CcpVerify is not even asked, because
            # EvaluateCapture REFUSES an empty selection by design (CheckEvaluator.cs:73-76), so
            # calling it here would manufacture a red for a pair the port chose not to check.
            Add-Row "  UNCHECKED $key ($([int]$clock.Elapsed.TotalSeconds)s) - captured, no named check is declared for this pair"
            continue
        }

        $png = Join-Path $verifyDir "artifacts\windows-$surface-$state.png"
        $verifyOut = & $verify --capture $png --surface $surface --state $state --manifest $manifest 2>&1 | Out-String
        $verifyExit = $LASTEXITCODE
        if ($verifyExit -ne 0 -or $verifyOut -notmatch 'ALL CHECKS PASSED') {
            $first = ($verifyOut -split "`n" | Where-Object { $_ -match 'FIRST FAILED CHECK|FAIL ' } | Select-Object -First 1)
            Add-Row "  FAIL      $key ($([int]$clock.Elapsed.TotalSeconds)s) - CcpVerify exit ${verifyExit}: $($first.Trim())"
            $failed++
            continue
        }
        Add-Row "  PASS      $key ($([int]$clock.Elapsed.TotalSeconds)s) - $declared named check(s)"
    }

    # The failures again, together, at the end. The rows above are interleaved with the app's own
    # stdout across tens of minutes of scrollback, which is exactly where a red goes to die.
    Write-Output '--- sweep results ---'
    foreach ($row in $rows) {
        if ($row -match '^\s*FAIL') { Write-Output $row }
    }
    if ($failed -eq 0) { Write-Output '  no failing pair' }
    $minutes = [math]::Round($sweepClock.Elapsed.TotalMinutes, 1)
    if ($failed -gt 0) {
        Write-Output "SWEEP FAIL: $failed of $($pairs.Count) surface/state pairs did not produce a verified capture ($minutes min)"
        exit 1
    }
    Write-Output "SWEEP PASS: $($pairs.Count) surface/state pairs, every capture non-vacuous and every declared check green ($minutes min)"
    exit 0
}

# RESTORE FROM MEMORY, NEVER FROM GIT (near-miss, 2026-08-18).
# This used `git checkout -- MainWindow.axaml` in both the failure path and the finally
# block. That restores the COMMITTED content, so running the self-test on a tree with
# uncommitted edits to that file DISCARDS them -- silently, and before anything is read.
# It ate a lane's in-progress rail markup mid-run and the lane had to reconstruct it.
# A verification harness must never be able to destroy the work it is verifying.
#
# The exact bytes are captured here, before any mutation, and every restore path writes
# them back. That is also STRICTER than git: it restores what was actually there, so an
# uncommitted work-in-progress survives the run untouched.
$original = [IO.File]::ReadAllText($anchorFile)

function Restore-Anchor {
    [IO.File]::WriteAllText($anchorFile, $original, [Text.UTF8Encoding]::new($false))
}

function Fail([string]$msg) {
    Write-Output "SELF-TEST FAIL: $msg"
    # Never leave the product source mutated -- and never discard anything either.
    Restore-Anchor
    exit 1
}

if ($original -notmatch '#FF6FB5') { Fail "$anchorFile does not declare the theme's AccentLightColor #FF6FB5 - self-test anchor missing" }

# Exactly ONE occurrence, and CcpTheme says so in its own comment. If a second ever appears - a
# worked example, another theme, a hex quoted in prose - the -replace below would rewrite it too,
# and the restore proof further down would then be checking a file this script had edited in two
# places for one reason. Cheaper to refuse than to explain.
$anchorCount = ([regex]::Matches($original, '#FF6FB5')).Count
if ($anchorCount -ne 1) { Fail "expected exactly ONE #FF6FB5 in $anchorFile, found $anchorCount - the seed would touch more than the theme" }

Write-Output '--- phase 1: seed the regression (CCP Default AccentLightColor -> #336633) ---'
[IO.File]::WriteAllText($anchorFile, ($original -replace '#FF6FB5', '#336633'), [Text.UTF8Encoding]::new($false))

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

    # The same seeded build, read at the RACK. The rack row's selection marker uses the
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
    Restore-Anchor
    # Prove the restore, rather than trusting the write: the seeded brush must be gone and
    # the anchor back. A silent partial restore would leave the product mutated and the
    # remaining phases would then be measuring the wrong tree.
    $restored = [IO.File]::ReadAllText($anchorFile)
    if ($restored -ne $original) { Fail 'restore did not reproduce the anchor file byte-for-byte' }
    if ($restored -match '#336633') { Fail 'the seeded colour survived the restore - the anchor file is still mutated' }
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
