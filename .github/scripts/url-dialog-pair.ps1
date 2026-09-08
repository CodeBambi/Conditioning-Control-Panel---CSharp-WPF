param([switch]$SelfCheck)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Classify only completed real smoke runs, never a build/bootstrap exception as RED.
function Test-SmokeResult($Mode, $Code, [string]$Output, [string]$Errors) {
    $passes = @([regex]::Matches($Output, '(?m)^  \[PASS\] ')).Count
    $fails = @([regex]::Matches($Output, '(?m)^  \[FAIL\] (.*)\r?$') | ForEach-Object { $_.Groups[1].Value.TrimEnd("`r") })
    if ($Errors.Trim() -or $Output -notmatch '(?m)^  \[PASS\] URL prompt bootstrap has no desktop lifetime\r?$') { return $false }
    if ($Mode -eq 'green') {
        return $Code -eq 0 -and $passes -eq 111 -and $fails.Count -eq 0 -and
            $Output.TrimEnd().EndsWith('Linux head can produce every value it renders.')
    }
    $expected = @(
        foreach ($route in 'Load', 'Enter') {
            foreach ($inputText in 'not a URL', 'relative/path', 'https://', 'file:///tmp/example.mp4',
                'ftp://example.invalid/a', 'data:text/plain,x', 'javascript:void(0)') {
                "URL prompt $route '$inputText': rejects without completing  (Result=$inputText, visible=False, completed=True, errorVisible=False, error=)"
            }
            "URL prompt $route before correction: rejects without completing  (Result=not a URL, visible=False, completed=True, errorVisible=False, error=)"
        }
        foreach ($dismiss in 'Cancel', 'Escape') {
            "URL prompt $dismiss (invalid first: True): rejects without completing  (Result=not a URL, visible=False, completed=True, errorVisible=False, error=)"
        }
    )
    return $Code -eq 1 -and $passes -eq 87 -and $fails.Count -eq 18 -and
        ($fails -join "`n") -ceq ($expected -join "`n") -and $Output.TrimEnd().EndsWith('18 assertion(s) failed.')
}

if ($SelfCheck) {
    $ok = "  [PASS] URL prompt bootstrap has no desktop lifetime`n" + ("  [PASS] fixture`n" * 110) + 'Linux head can produce every value it renders.'
    if (!(Test-SmokeResult green 0 $ok '') -or (Test-SmokeResult red 1 $ok '') -or
        (Test-SmokeResult green 0 $ok 'startup error') -or (Test-SmokeResult green 1 $ok '') -or
        (Test-SmokeResult green 0 ($ok.Replace('no desktop lifetime', 'desktop lifetime')) '') -or
        (Test-SmokeResult green 0 ($ok.Replace("  [PASS] fixture`n", '')) '')) { throw 'Classifier self-check failed' }
    'Managed classifier self-check passed (no application/process launch).'
    return
}

$root = Join-Path $env:RUNNER_TEMP 'url-dialog-pair'
if (Test-Path $root) { throw "Evidence directory already exists: $root" }
New-Item -ItemType Directory $root | Out-Null
function Save-Json($Path, $Value) { $Value | ConvertTo-Json -Depth 12 | Set-Content $Path -Encoding utf8 }

# One bounded child at a time; stdout/stderr and exit/argv survive nonzero exits and timeouts.
function Invoke-Owned($Exe, [string[]]$Arguments, $Cwd, $Label, $Seconds = 60, $Environment = $null) {
    $prefix = Join-Path $root $Label
    $receipt = [ordered]@{ exe = $Exe; argv = $Arguments; cwd = $Cwd; timeoutSeconds = $Seconds; environment = $Environment; exit = $null; timedOut = $false }
    Save-Json "$prefix.command.json" $receipt
    $info = [Diagnostics.ProcessStartInfo]::new($Exe)
    $info.WorkingDirectory = $Cwd
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($arg in $Arguments) { $info.ArgumentList.Add($arg) }
    if ($null -ne $Environment) {
        $info.Environment.Clear() # No parent env mutation/restoration, credentials or startup hooks.
        foreach ($key in $Environment.Keys) { $info.Environment[$key] = $Environment[$key] }
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    $outFile = [IO.File]::Create("$prefix.stdout.log")
    $errFile = [IO.File]::Create("$prefix.stderr.log")
    try {
        if (!$process.Start()) { throw "Could not start $Label" }
        $stdout = $process.StandardOutput.BaseStream.CopyToAsync($outFile)
        $stderr = $process.StandardError.BaseStream.CopyToAsync($errFile)
        $finished = $process.WaitForExit($Seconds * 1000)
        if (!$finished) {
            $receipt.timedOut = $true
            $process.Kill($true)
            if (!$process.WaitForExit(10000)) { throw "$Label did not terminate after kill" }
        }
        $receipt.exit = $process.ExitCode
        if (![Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdout, $stderr), 10000)) { throw "$Label log drain timed out" }
        $outFile.Dispose()
        $errFile.Dispose()
        if (!$finished) { throw "$Label timed out" }
        return [pscustomobject]@{ Code = $process.ExitCode; Output = [IO.File]::ReadAllText("$prefix.stdout.log"); Errors = [IO.File]::ReadAllText("$prefix.stderr.log") }
    } finally {
        $outFile.Dispose()
        $errFile.Dispose()
        Save-Json "$prefix.command.json" $receipt
        $process.Dispose()
    }
}
function Require-Success($Result) {
    if ($Result.Code -ne 0) { throw "Required invocation exited $($Result.Code); see raw logs" }
    return $Result.Output.Trim()
}
function New-Profile($Path) {
    $map = @{}
    foreach ($key in 'SystemRoot', 'WINDIR', 'SystemDrive', 'ComSpec', 'PATH', 'PATHEXT',
        'PROCESSOR_ARCHITECTURE', 'NUMBER_OF_PROCESSORS', 'ProgramFiles', 'ProgramFiles(x86)', 'ProgramW6432') {
        $value = [Environment]::GetEnvironmentVariable($key)
        if ($null -ne $value) { $map[$key] = $value }
    }
    foreach ($key in 'HOME', 'USERPROFILE', 'APPDATA', 'LOCALAPPDATA', 'XDG_CONFIG_HOME', 'XDG_DATA_HOME',
        'XDG_CACHE_HOME', 'CCP_USERDATA_DIR', 'DOTNET_CLI_HOME', 'TEMP', 'TMP') {
        $map[$key] = Join-Path $Path $key
        New-Item -ItemType Directory $map[$key] | Out-Null
    }
    $map.DOTNET_ROOT = Split-Path $dotnet
    $map.DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $map.DOTNET_NOLOGO = '1'
    $map.DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $map.NUGET_PACKAGES = Join-Path $root 'packages'
    return $map
}
function Manifest($Path) {
    Get-ChildItem $Path -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($Path, $_.FullName); sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash }
    }
}

try {
    if (!$IsWindows) { throw 'This pair requires Windows; use -SelfCheck for managed-only local validation' }
    $repo = (Resolve-Path "$PSScriptRoot/../..").Path
    $dotnet = (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source
    $git = (Get-Command git -CommandType Application | Select-Object -First 1).Source
    $identity = Require-Success (Invoke-Owned $git @('-C', $repo, 'rev-parse', 'HEAD', 'HEAD^{tree}') $repo 'checkout')
    $commit = Require-Success (Invoke-Owned $git @('-C', $repo, 'cat-file', '-p', 'HEAD') $repo 'commit-object') # Real parents even with shallow checkout.
    Save-Json "$root/identity.json" @{ checkout = $identity; commitObject = $commit; githubSha = $env:GITHUB_SHA; os = [Environment]::OSVersion.VersionString; powershell = "$($PSVersionTable.PSVersion)" }
    # Freeze the reviewed safe Program/App/null-lifetime route and every product dependency.
    foreach ($entry in @(@('CCP.Avalonia', '79be6413c2f2674a9a4039c2294e7855fdfb6146'), @('CCP.Core', '125429304e1d6251f8a5a0c8a5cd87287f8b472f'))) {
        $tree = Require-Success (Invoke-Owned $git @('-C', $repo, 'rev-parse', "HEAD:$($entry[0])") $repo "tree-$($entry[0])")
        if ($tree -ne $entry[1]) { throw "Unreviewed source tree: $($entry[0])" }
    }
    # Export committed LF bytes independently of the host's Git line-ending settings.
    Require-Success (Invoke-Owned $git @('-C', $repo, '-c', 'core.autocrlf=false', '-c', 'core.eol=lf', 'archive', '--format=zip', "--output=$root/source.zip", 'HEAD', 'CCP.Avalonia', 'CCP.Core') $repo 'archive') | Out-Null
    $patch = Join-Path $root 'url-dialog-original.patch'
    [IO.File]::WriteAllText($patch, [IO.File]::ReadAllText("$PSScriptRoot/url-dialog-original.patch").Replace("`r`n", "`n"))
    if ((Get-FileHash $patch).Hash -ne '234a5a02dd46cdc09ec6a78af76366d44aee5af71b9f4ad1caaffe79d2610807') { throw 'Inverse patch changed' }
    # Inverse derived from ead7 -> 91486f3621ff483357f9113cb48b1f4d7e5b6b27.
    # Original dialog blob 27210f53cac0fc997eb0fb42892fa997dc9737bb also equals owner 704c5451.
    $dialog = 'CCP.Avalonia/Views/Dialogs/UrlPromptDialog.axaml.cs'
    $tests = 'CCP.Avalonia/HeadlessSmoke.cs'
    foreach ($mode in 'red', 'green') {
        $pair = Join-Path $root $mode
        $source = Join-Path $pair 'source'
        Expand-Archive "$root/source.zip" $source
        if ($mode -eq 'red') {
            Require-Success (Invoke-Owned $git @('-C', $source, '-c', 'core.autocrlf=false', '-c', 'core.eol=lf', 'apply', '--unidiff-zero', '--check', $patch) $source 'inverse-check') | Out-Null
            Require-Success (Invoke-Owned $git @('-C', $source, '-c', 'core.autocrlf=false', '-c', 'core.eol=lf', 'apply', '--unidiff-zero', $patch) $source 'inverse-apply') | Out-Null
        }
        $testHash = (Get-FileHash "$source/$tests" -Algorithm SHA256).Hash
        if ($testHash -ne '6d0b5d59dcea00a7a4c3cee2f39987894a9b200de438aeebd5012483d46995b1') { throw 'Test bytes changed' }
        $expectedDialog = if ($mode -eq 'red') { '4e135e6f3261b2ced8389ac5a584628afa19c00132b072a4cc1230ab0d3a0437' } else { 'e3944bb609bc6d110b3f362bb98043ccdb3fd6266d26475aeaa5c5329f0b5381' }
        if ((Get-FileHash "$source/$dialog" -Algorithm SHA256).Hash -ne $expectedDialog) { throw 'Dialog bytes changed' }
        $before = @(Manifest $source)
        Save-Json "$pair/source-hashes.json" $before
        $buildEnv = New-Profile "$pair/build-profile"
        $sdk = Require-Success (Invoke-Owned $dotnet @('--version') $source "$mode-sdk" 60 $buildEnv)
        $runtimes = Require-Success (Invoke-Owned $dotnet @('--list-runtimes') $source "$mode-runtimes" 60 $buildEnv)
        $versions = @([regex]::Matches($runtimes, '(?m)^Microsoft.NETCore.App (8\.0\.\d+) ') | ForEach-Object { [version]$_.Groups[1].Value })
        if (!$versions.Count) { throw 'No stable .NET 8 runtime installed' }
        $runtime = ($versions | Sort-Object -Descending | Select-Object -First 1).ToString()
        Save-Json "$pair/toolchain.json" @{ sdk = $sdk; runtime = $runtime; dotnet = $dotnet }
        if ($mode -eq 'red') { $redSdk = $sdk; $redRuntime = $runtime }
        elseif ($sdk -ne $redSdk -or $runtime -ne $redRuntime) { throw 'Pair toolchain mismatch' }
        $artifacts = Join-Path $pair 'artifacts'
        $buildArgs = @('build', 'CCP.Avalonia/CCP.Avalonia.csproj', '-c', 'Release', '--artifacts-path', $artifacts, '--disable-build-servers')
        Require-Success (Invoke-Owned $dotnet $buildArgs $source "$mode-build" 600 $buildEnv) | Out-Null
        $after = @(Manifest $source)
        if (($before | ConvertTo-Json -Depth 4) -cne ($after | ConvertTo-Json -Depth 4)) { throw 'Build changed source inputs' }
        $bin = Join-Path $artifacts 'bin/CCP.Avalonia/release'
        $config = @('CCP.Avalonia.runtimeconfig.json', 'CCP.Avalonia.deps.json') | ForEach-Object { (Get-FileHash "$bin/$_").Hash }
        if ($mode -eq 'red') { $redConfig = $config }
        elseif (($config -join ',') -ne ($redConfig -join ',')) { throw 'Pair runtime/dependency configuration mismatch' }
        # Fresh process env BEFORE CorePaths/Localization/App initialize; no shell/caller/fetcher.
        $runEnv = New-Profile "$pair/run-profile"
        $runEnv.DOTNET_ROLL_FORWARD = 'Disable'
        $runEnv.COREHOST_TRACE = '1'
        $runEnv.COREHOST_TRACEFILE = "$pair/host-trace.log"
        $runEnv.DOTNET_HOST_TRACE = '1' # .NET 10 host spelling; COREHOST remains for older hosts.
        $runEnv.DOTNET_HOST_TRACEFILE = $runEnv.COREHOST_TRACEFILE
        $runArgs = @('exec', '--fx-version', $runtime, '--roll-forward', 'Disable', "$bin/CCP.Avalonia.dll", '--smoke')
        $result = Invoke-Owned $dotnet $runArgs $pair "$mode-smoke" 90 $runEnv
        $trace = Get-Content $runEnv.COREHOST_TRACEFILE -Raw
        if ($trace -notmatch ('Microsoft.NETCore.App[\\/]' + [regex]::Escape($runtime) + '[\\/]coreclr.dll')) { throw 'Missing selected .NET 8 CoreCLR evidence' }
        $accepted = Test-SmokeResult $mode $result.Code $result.Output $result.Errors
        Save-Json "$pair/result.json" @{ mode = $mode; exit = $result.Code; expectedClassification = $accepted; testSHA256 = $testHash; runtime = $runtime
            passes = [regex]::Matches($result.Output, '(?m)^  \[PASS\] ').Count; failures = [regex]::Matches($result.Output, '(?m)^  \[FAIL\] ').Count }
        if (!$accepted) { throw "$mode is not the required URL regression outcome; see raw logs" }
        if (@(Get-ChildItem $runEnv.CCP_USERDATA_DIR -Force -Recurse).Count) { throw 'Smoke wrote application userdata' }
    }
    Save-Json "$root/result.json" @{ status = 'passed'; scope = 'Windows/.NET8 real headless dialogs, not native desktop/visual parity' }
} catch {
    $_ | Out-String | Set-Content "$root/error.log"
    Save-Json "$root/result.json" @{ status = 'failed'; error = $_.ToString() }
    throw
} finally {
    foreach ($mode in 'red', 'green') {
        $pair = Join-Path $root $mode
        if (Test-Path $pair) { Save-Json "$root/$mode-artifact-hashes.json" @(Manifest $pair) }
    }
}
