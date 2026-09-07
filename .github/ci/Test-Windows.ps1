# Only CI test orchestration. No product startup, browser flag changes, or host-wide cleanup.
param(
    [Parameter(Mandatory)][ValidateSet('SupportOnly', 'RequireNative')][string]$Mode,
    [string]$Receipts,
    [switch]$SelfCheckOnly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $false
if (!$IsWindows -or ![Environment]::Is64BitProcess) { throw 'BLOCKED: x64 Windows PowerShell 7 required' }
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$pwsh = (Get-Process -Id $PID).Path
$project = 'Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj'
$testBin = 'Tests/ConditioningControlPanel.Tests/bin/Release/net8.0-windows10.0.19041.0/win-x64'
$fact = 'ConditioningControlPanel.Tests.RaceFullscreenNativeTests.NotificationsDoNotEcho_ExplicitRequestsReceiveNativeAcknowledgements'
$nativePath = 'Tests/ConditioningControlPanel.Tests/RaceFullscreenNativeTests.cs'
$asset = 'ConditioningControlPanel/Resources/web/dtrh/raceBoot.js'
$out = if ($Receipts) { (Resolve-Path -LiteralPath $Receipts).Path } else {
    $directory = Join-Path ([IO.Path]::GetTempPath()) ('ccp-ci-' + [guid]::NewGuid().ToString('N'))
    (New-Item -ItemType Directory $directory).FullName
}
# Publish the receipt location before any capability check can fail. Never delete failed profiles.
if ($env:GITHUB_OUTPUT) { "receipts=$out" >> $env:GITHUB_OUTPUT }
Write-Host "Test-only receipts: $out"
function Quote([string]$s) { "'" + $s.Replace("'", "''") + "'" }
function Save($name, $value) { $value | ConvertTo-Json -Depth 12 | Set-Content "$out/$name.json" }
function InvokeRepoGit([string[]]$a) {
    $text = & $gitExe -C $repo @a
    if ($LASTEXITCODE) { throw "BLOCKED: git $a exit=$LASTEXITCODE" }
    return $text
}
function Hash([string]$path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash }
function Owned([string]$name, [string]$body, [string]$cwd, [int]$seconds = 90, [scriptblock]$Proof) {
    # Parent sets the absent child before CreateProcess; no test/module has initialized yet.
    $root = Join-Path $out ($name + '-userdata')
    if (Test-Path -LiteralPath $root) { throw 'BLOCKED: profile is not absent' }
    $old = $env:CCP_USERDATA_DIR
    try {
        $env:CCP_USERDATA_DIR = $root
        $command = "`$ErrorActionPreference='Stop'; `$PSNativeCommandUseErrorActionPreference=`$false; " + $body
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
        $r = [OwnedTestJob]::Run($pwsh, $encoded, $cwd, $seconds, "$out/$name-job.log", {
            param($receipt)
            Save "$name-job" $receipt
            if ($Proof) { & $Proof $receipt }
        })
        if (!$r.Reaped) { throw "BLOCKED: $name not reaped" }
        return $r
    } finally { $env:CCP_USERDATA_DIR = $old }
}
function TestCommand([string]$filter, [string]$directory, [string]$name, [switch]$List, [switch]$Ordinary) {
    # Native discovery/execution uses VSTest directly. Ordinary coverage keeps the standard
    # project invocation, with build-server reuse disabled inside the inherited job.
    if ($Ordinary) {
        $argsText = "test $(Quote $project) -c Release --no-build --disable-build-servers -p:ValidateExecutableReferencesMatchSelfContained=false"
        if ($filter) { $argsText += " --filter $(Quote $filter)" }
        $argsText += " --logger 'trx;LogFileName=tests.trx' --results-directory $(Quote "$out/$name")"
    } else {
        $argsText = "vstest $(Quote "$directory/$testBin/ConditioningControlPanel.Tests.dll")"
        if ($filter) { $argsText += " $(Quote "/TestCaseFilter:$filter")" }
        if ($List) { $argsText += ' /ListTests' }
        else { $argsText += " '/Logger:trx;LogFileName=tests.trx' $(Quote "/ResultsDirectory:$out/$name")" }
    }
    Save "$name-invocation" @{ mode = $Mode; cwd = $directory; executable = 'dotnet'; arguments = $argsText; filter = $filter }
    $proof = if (!$List -and !$Ordinary) { {
        param($receipt)
        if ($receipt.TimedOut) { throw "BLOCKED: $name external timeout (not regression)" }
        $members = $receipt.ObservedMembers
        $text = (Trx $name).Xml.InnerText
        $browser = [regex]::Match($text, 'runtime=\S+ browser=(\d+)')
        $owned = @([regex]::Matches($text, 'owned-process=(\d+)') | ForEach-Object { [uint]$_.Groups[1].Value })
        if (!$browser.Success -or !$owned.Count) { throw "BLOCKED: $name missing browser identities" }
        foreach ($id in @([uint]$browser.Groups[1].Value) + $owned) {
            if ($id -notin $members) { throw "BLOCKED: $name browser outside retained job identities (no escape fallback)" }
        }
        # Observed membership is not proof of every pre-core/late-startup WebView launch path.
        Save "$name-membership" @{ proof = 'job-queried handles retained through this comparison'; browser = $browser.Groups[1].Value; owned = $owned }
    } } else { $null }
    Owned $name "`$env:MSBUILDDISABLENODEREUSE='1'; & dotnet $argsText *> $(Quote "$out/$name.log"); exit `$LASTEXITCODE" $directory -Proof $proof
}
function AssertComposition([string]$root, [string]$expectation) {
    $markers = @($nativePath, 'Tests/ConditioningControlPanel.Tests/NativeFixtureLifetimeTests.cs',
        'ConditioningControlPanel/Resources/web/dtrh/race/smoke/fullscreen-check.mjs')
    $count = @($markers | Where-Object { Test-Path "$root/$_" }).Count
    $required = if ($expectation -eq 'RequireNative') { 3 } else { 0 }
    if ($count -ne $required) { throw "BLOCKED: $expectation fixture markers expected=$required actual=$count" }
}

function Trx([string]$name) {
    [xml]$xml = Get-Content -Raw "$out/$name/tests.trx"
    $results = @($xml.SelectNodes("//*[local-name()='UnitTestResult']"))
    $counters = $xml.SelectSingleNode("//*[local-name()='Counters']")
    if (!$counters -or [int]$counters.total -ne $results.Count) { throw "BLOCKED: $name TRX counts" }
    [pscustomobject]@{ Xml = $xml; Results = $results; Counters = $counters }
}
function SelfChecks {
    # Actual nonexecuting gate: removing every marker cannot downgrade the upper invocation.
    $empty = (New-Item -ItemType Directory "$out/missing-markers").FullName
    $rejected = $false
    try { AssertComposition $empty 'RequireNative' }
    catch { if ($_.ToString() -notlike '*RequireNative fixture markers expected=3 actual=0*') { throw }; $rejected = $true }
    if (!$rejected) { throw 'BLOCKED: missing-all-markers check' }
    [OwnedTestJob]::CheckReleaseFailures()
    foreach ($key in 'DOTNET_DiagnosticPorts', 'DOTNET_DefaultDiagnosticPortSuspend') {
        $old = [Environment]::GetEnvironmentVariable($key)
        $rejected = $false
        try {
            [Environment]::SetEnvironmentVariable($key, 'support-rejection-check')
            # Run must reject before platform/job/child acquisition; no diagnostic contact.
            try { [OwnedTestJob]::Run('', '', $repo, 5, '', $null) | Out-Null }
            catch { if ($_.ToString() -notlike "*inherited override: $key*") { throw }; $rejected = $true }
        } finally { [Environment]::SetEnvironmentVariable($key, $old) }
        if (!$rejected) { throw "BLOCKED: diagnostic rejection check $key" }
    }
    # Actual wrapper/application resolution, including in -SelfCheckOnly mode.
    if ((InvokeRepoGit @('rev-parse', '--show-toplevel')) -ne $repo.Replace('\', '/')) { throw 'BLOCKED: Git wrapper root' }
    Save 'git-check' @{ application = $gitExe; status = @(InvokeRepoGit @('status', '--porcelain=v1')) }
    foreach ($mode in 'normal', 'early-failure', 'hang', 'post-parent') {
        $marker = "$out/$mode-child.pid"
        $sleep = if ($mode -eq 'normal') { 1 } else { 30 }
        $child = "[IO.File]::WriteAllText($(Quote $marker), [string]`$PID); Start-Sleep $sleep"
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($child))
        $body = "`$p = Start-Process $(Quote $pwsh) -ArgumentList '-NoProfile','-NonInteractive','-EncodedCommand','$encoded' -PassThru; "
        $body += "while (!(Test-Path $(Quote $marker))) { Start-Sleep -Milliseconds 10 }; "
        # Let the inventory observe the benign child; failure to observe is itself a blocked check.
        $body += 'Start-Sleep -Milliseconds 200; '
        if ($mode -eq 'normal') { $body += 'Wait-Process -Id $p.Id; exit 0' }
        elseif ($mode -eq 'early-failure') { $body += 'exit 7' }
        elseif ($mode -eq 'hang') { $body += 'Start-Sleep 30' }
        else { $body += 'exit 0' }
        $seconds = 5
        $r = Owned "self-$mode" $body $repo $seconds -Proof {
            param($receipt)
            if ([uint](Get-Content $marker) -notin $receipt.ObservedMembers) { throw "BLOCKED: $mode retained child identity" }
        }
        if ($r.TotalProcesses -lt 2 -or $r.ActiveAfterReap -ne 0) {
            throw "BLOCKED: $mode descendant ownership/reap"
        }
        if ($r.TimedOut -ne ($mode -eq 'hang')) { throw "BLOCKED: $mode timeout classification" }
        $expected = if ($mode -eq 'early-failure') { 7 } else { 0 }
        if (!$r.TimedOut -and $r.ExitCode -ne $expected) { throw "BLOCKED: $mode exit" }
        if ($mode -ne 'normal' -and $r.ResidueBeforeReap -lt 1) { throw "BLOCKED: $mode did not exercise residue" }
    }
    $failed = $false
    try { [OwnedTestJob]::Run($pwsh, [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes('exit 0')), $repo, 5, $out, $null) | Out-Null }
    catch {
        $errorObject = $_.Exception
        while ($errorObject -isnot [AggregateException] -and $errorObject.InnerException) { $errorObject = $errorObject.InnerException }
        if ($errorObject -isnot [AggregateException] -or $errorObject.InnerExceptions.Count -ne 2 -or
            $errorObject.InnerExceptions[0] -isnot [UnauthorizedAccessException] -or
            $errorObject.InnerExceptions[1] -isnot [UnauthorizedAccessException]) { throw }
        $failed = $true
        Save 'self-cleanup-failure' @{ error = $_.ToString(); faults = @($errorObject.InnerExceptions | ForEach-Object { $_.ToString() }) }
    }
    if (!$failed) { throw 'BLOCKED: exceptional journal path did not fail' }
    Save 'self-checks' @{ outcome = 'passed'; ownerStillRunning = $PID; modes = 4; gates = 'composition,diagnostics,git,release-dispatch,journal-failure' }
}
try {
    # Reject rather than silently rewrite inherited runtime behavior. Policies are READ only.
    Add-Type -Path "$PSScriptRoot/OwnedTestJob.cs"
    [OwnedTestJob]::AssertEnvironment()
    AssertComposition $repo $Mode # caller-bound gate before ANY owned child or discovery
    $present = $Mode -eq 'RequireNative'
    Save 'mode' @{ expectation = $Mode; nativeRequired = $present }
    $gitExe = (Get-Command git -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    foreach ($hive in 'LocalMachine', 'CurrentUser') {
        foreach ($view in 'Registry64', 'Registry32') {
            $registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, $view)
            try {
                $key = $registry.OpenSubKey('Software\Policies\Microsoft\Edge\WebView2')
                try { if ($key -and ($key.ValueCount -or $key.SubKeyCount)) { throw 'BLOCKED: WebView2 policy present' } }
                finally { if ($key) { $key.Dispose() } }
            } finally { $registry.Dispose() }
        }
    }
    SelfChecks
    if ($SelfCheckOnly) { return }
    if (InvokeRepoGit @('status', '--porcelain=v1', '--untracked-files=all')) { throw 'BLOCKED: dirty CI checkout' }
    $head = InvokeRepoGit @('rev-parse', 'HEAD')
    Save 'checkout' @{ head = $head; tree = (InvokeRepoGit @('rev-parse', 'HEAD^{tree}')); os = [Environment]::OSVersion.ToString(); session = (Get-Process -Id $PID).SessionId; pwsh = $PSVersionTable.PSVersion.ToString(); sdk = (& dotnet --version) }
    # Inventory discovers but NEVER executes tests. Job/profile protections still apply to module init.
    $inventory = TestCommand '' $repo 'inventory' -List
    if ($inventory.TimedOut -or $inventory.ExitCode) { throw 'BLOCKED: inventory' }
    $listing = Get-Content "$out/inventory.log"
    $header = [array]::IndexOf($listing, 'The following Tests are available:')
    if ($header -lt 0) { throw 'BLOCKED: unrecognized VSTest discovery format' }
    $names = @($listing | Select-Object -Skip ($header + 1) | Where-Object { $_ -match '^    \S' } | ForEach-Object { $_.Trim() })
    $nativeNames = @($names | Where-Object { $_ -like '*RaceFullscreenNativeTests*' })
    if ($present -and ($nativeNames.Count -ne 1 -or $nativeNames[0] -cne $fact)) { throw 'BLOCKED: native discovery must be exactly one' }
    if (!$present -and $nativeNames.Count) { throw 'BLOCKED: unexpected native discovery' }
    Save 'discovery' @{ names = $names; native = $nativeNames; fixturePresent = $present }
    $nativePassed = $false
    if ($present) {
        $inverse = "$PSScriptRoot/race-callback-inverse.patch"
        if ((Hash $inverse) -ne '3C0416B2B37DBD957E4C6F120148EE703680B8B774FE8693F9CA548356374B2B') { throw 'BLOCKED: inverse provenance' }
        $stat = InvokeRepoGit @('apply', '--numstat', $inverse)
        if ($stat -cne "1`t1`t$asset") { throw 'BLOCKED: inverse must change raceBoot.js only +1/-1' }
        # Copy tracked checkout bytes only: archive would change CRLF checkout bytes to blob LF.
        # No obj/bin reuse, no history manipulation and no second fixture revision.
        $red = "$out/red-source"
        New-Item -ItemType Directory $red | Out-Null
        $sources = @(InvokeRepoGit @('ls-files'))
        foreach ($path in $sources) {
            $destination = Join-Path $red $path
            New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
            Copy-Item -LiteralPath "$repo/$path" -Destination $destination
        }
        & $gitExe -C $red apply --ignore-space-change --check $inverse
        if ($LASTEXITCODE) { throw 'BLOCKED: inverse applicability' }
        & $gitExe -C $red apply --ignore-space-change $inverse
        if ($LASTEXITCODE) { throw 'BLOCKED: inverse application' }
        $actualStat = & $gitExe diff --no-index --numstat -- "$repo/$asset" "$red/$asset"
        if ($LASTEXITCODE -ne 1 -or $actualStat -notmatch '^1\t1\t') {
            throw 'BLOCKED: actual inverse diff is not +1/-1'
        }
        $identities = foreach ($path in $sources) {
            $greenHash = Hash "$repo/$path"
            $redHash = Hash "$red/$path"
            if ($path -ne $asset -and $greenHash -ne $redHash) { throw "BLOCKED: pair source mismatch $path" }
            [pscustomobject]@{ path = $path; green = $greenHash; red = $redHash }
        }
        Save 'pair-sources' $identities
        if ((Hash "$repo/$asset") -eq (Hash "$red/$asset")) { throw 'BLOCKED: inverse made no change' }
        # Independent obj/bin directories and CallerFilePath roots. GREEN was built by build.yml.
        Push-Location $red
        try {
            & dotnet build $project -c Release -p:ValidateExecutableReferencesMatchSelfContained=false *> "$out/red-build.log"
            if ($LASTEXITCODE) { throw 'BLOCKED: RED compilation (not regression)' }
        } finally { Pop-Location }
        foreach ($member in 'red', 'green') {
            $source = if ($member -eq 'red') { $red } else { $repo }
            $bin = "$source/$testBin"
            $assets = @('race.html','raceBoot.js','bridge.js','hostMedia.js','shared/capability.js','shared/quality.js','styles.css','race/race.css','race/menu.css')
            $copies = foreach ($a in $assets) {
                $srcHash = Hash "$source/ConditioningControlPanel/Resources/web/dtrh/$a"
                $copyHash = Hash "$bin/Resources/web/dtrh/$a"
                if ($srcHash -ne $copyHash) { throw "BLOCKED: $member copied asset $a" }
                @{ asset = $a; source = $srcHash; deployed = $copyHash }
            }
            Save "$member-build" @{ assets = $copies; assembly = (Hash "$bin/ConditioningControlPanel.Tests.dll"); product = (Hash "$bin/ConditioningControlPanel.dll") }
            $r = TestCommand "FullyQualifiedName=$fact" $source $member
            if ($r.TimedOut) { throw "BLOCKED: $member external timeout (not regression)" }
            $t = Trx $member
            if ($t.Results.Count -ne 1 -or $t.Results[0].testName -cne $fact -or
                [int]$t.Counters.executed -ne 1 -or [int]$t.Counters.notExecuted -ne 0) {
                throw "BLOCKED: $member must execute exactly one, zero skips"
            }
            $text = $t.Xml.InnerText
            if ($text -notmatch 'CAPABILITY-PASSED:' -or $text -notmatch 'TEARDOWN-PASSED:' -or
                $text -match 'TEARDOWN/ORACLE-FAILED|TEARDOWN-BLOCKED') {
                throw "BLOCKED: $member capability/teardown"
            }
            if ($member -eq 'red') {
                $errorText = $t.Results[0].SelectSingleNode(".//*[local-name()='ErrorInfo']").InnerText
                if ($r.ExitCode -ne 1 -or $t.Results[0].outcome -ne 'Failed' -or
                    $errorText -notmatch 'REGRESSION: unsolicited/duplicate fullscreen command before forwarding cap') {
                    throw 'BLOCKED: RED did not fail the echo oracle'
                }
            } elseif ($r.ExitCode -ne 0 -or $t.Results[0].outcome -ne 'Passed' -or
                $text -notmatch 'GREEN-ORACLES-PASSED') {
                throw 'BLOCKED: GREEN native/liveness'
            }
        }
        $nativePassed = $true
    }
    # Exactly one ordinary execution; only the separately mandatory native Fact is excluded.
    $filter = if ($present) { "FullyQualifiedName!=$fact" } else { '' }
    $r = TestCommand $filter $repo 'compatibility' -Ordinary
    $t = Trx 'compatibility'
    if ($r.TimedOut -or $r.ExitCode -or [int]$t.Counters.failed -ne 0) { throw 'BLOCKED: compatibility' }
    # Deferred theories can expand after discovery: inventory names are diagnostic, not a TRX bijection.
    if (!$t.Results.Count -or [int]$t.Counters.executed -ne $t.Results.Count -or
        [int]$t.Counters.notExecuted -ne 0 -or [int]$t.Counters.passed -ne $t.Results.Count -or
        @($t.Results | Where-Object { $_.outcome -ne 'Passed' -or $_.testName -ceq $fact }).Count) {
        throw 'BLOCKED: compatibility failed/skipped/invalid results or native overlap'
    }
    if ($present -and !$nativePassed) { throw 'BLOCKED: ordinary exclusion without mandatory pair' }
    if ($present) {
        foreach ($identity in $identities) {
            if ((Hash "$repo/$($identity.path)") -ne $identity.green -or
                (Hash "$red/$($identity.path)") -ne $identity.red) {
                throw "BLOCKED: source changed during execution: $($identity.path)"
            }
        }
    }
    if (InvokeRepoGit @('status', '--porcelain=v1', '--untracked-files=all')) { throw 'BLOCKED: final checkout changed' }
    Save 'summary'  @{ outcome = 'passed'; nativeVerified = $nativePassed; supportOnly = !$present; compatibility = $t.Counters; diagnosticDiscovered = $names.Count; mode = $Mode; head = $head }
} catch {
    Save 'blocked' @{ outcome = 'blocked'; error = $_.ToString(); stack = $_.ScriptStackTrace }
    throw
}
