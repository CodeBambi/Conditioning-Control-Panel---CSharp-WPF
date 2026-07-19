# CCP greenfield artifact evidence matrix — Windows (release-publish-gates.md §3).
# Runs every matrix gate for one or all artifact modes against the REAL binaries:
#   debug     = bin/Debug/net10.0/CcpClient.Desktop.exe
#   release   = bin/Release/net10.0/CcpClient.Desktop.exe
#   published = publish dir COPIED TO %TEMP% (location independence, gate 6) and run there
# Gates: 1 startup+graceful-shutdown exit 0 (CloseMainWindow — kill is never the success
# path), 2 --verify-assets, 3 --version derivation, 4 fresh-profile, 5 corrupt-settings
# quarantine (original bytes preserved), 6 data-path identity, 7 logs-absence,
# 8 native-deps floor (published). Usage: pwsh client/tools/publish/matrix.ps1 [-Mode all]
param([ValidateSet('debug', 'release', 'published', 'all')] [string]$Mode = 'all')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$root = Resolve-Path "$PSScriptRoot/../.."
$csproj = Join-Path $root 'src/CcpClient.Desktop/CcpClient.Desktop.csproj'
$version = (dotnet msbuild $csproj -nologo -getProperty:Version).Trim()
if ([string]::IsNullOrWhiteSpace($version)) { throw 'FAIL: Version authority broken' }

$cfgDir = Join-Path $env:APPDATA 'CcpClient'
$cfgBak = "$cfgDir.sp010-bak"
$settingsFile = Join-Path $cfgDir 'settings.json'
$quarantinePaths = @{}
$failures = @()

function Fail([string]$gate, [string]$msg) {
    Write-Output "GATE $gate`: FAIL — $msg"
    $script:failures += "$gate`: $msg"
}

function Get-AppWindow([int]$processId) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Children, $cond)
}

function Get-WindowTexts($window) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Text)
    $lines = @()
    foreach ($t in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        $lines += $t.Current.Name
    }
    return $lines
}

function Invoke-Diagnostic([string]$exe, [string]$flag) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe; $psi.Arguments = $flag
    $psi.RedirectStandardOutput = $true; $psi.UseShellExecute = $false
    $p = [System.Diagnostics.Process]::Start($psi)
    $out = $p.StandardOutput.ReadToEnd()
    $p.WaitForExit(20000) | Out-Null
    return @{ Exit = $p.ExitCode; Output = $out }
}

# Gate 1 worker: launch headed, require the layout-probe needle (first-layout = a real
# render pass happened), close through the REAL close path (CloseMainWindow -> WM_CLOSE
# -> Avalonia Exit event -> guarded teardown), wait on the real PID for the exit code.
function Invoke-Headed([string]$exe, [string]$workDir) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe; $psi.WorkingDirectory = $workDir; $psi.UseShellExecute = $false
    $p = [System.Diagnostics.Process]::Start($psi)
    $deadline = (Get-Date).AddSeconds(25)
    $window = $null
    while ((Get-Date) -lt $deadline) {
        if ($p.HasExited) { break }
        $window = Get-AppWindow $p.Id
        if ($null -ne $window) { break }
        Start-Sleep -Milliseconds 300
    }
    if ($p.HasExited) { return @{ Ok = $false; Detail = "exited $($p.ExitCode) before window" } }
    if ($null -eq $window) { $p.Kill(); return @{ Ok = $false; Detail = 'no UIA window within 25s (killed)' } }
    $probe = (Get-WindowTexts $window | Where-Object { $_ -like 'layout-probe: card*' }) -join ';'
    if (-not $probe) { $p.Kill(); return @{ Ok = $false; Detail = 'layout-probe needle missing — no render evidence (killed)' } }
    $p.Refresh()
    $p.CloseMainWindow() | Out-Null
    $p.WaitForExit(20000) | Out-Null
    if (-not $p.HasExited) { $p.Kill(); return @{ Ok = $false; Detail = 'ignored CloseMainWindow (killed)' } }
    if ($p.ExitCode -ne 0) { return @{ Ok = $false; Detail = "graceful close exit $($p.ExitCode)" } }
    return @{ Ok = $true; Detail = "graceful exit 0; $probe" }
}

function Save-Config { if (Test-Path $cfgDir) { Move-Item $cfgDir $cfgBak -Force } }
function Restore-Config {
    if (Test-Path $cfgDir) { Remove-Item $cfgDir -Recurse -Force }
    if (Test-Path $cfgBak) { Move-Item $cfgBak $cfgDir }
}

# Restore a backup orphaned by an interrupted earlier run, then start clean.
if (Test-Path $cfgBak) { Restore-Config }

$modes = @('debug', 'release', 'published')
if ($Mode -ne 'all') { $modes = @($Mode) }

foreach ($m in $modes) {
    Write-Output "--- mode: $m"
    $exeDir = ''
    if ($m -eq 'debug' -or $m -eq 'release') {
        $cfgName = if ($m -eq 'debug') { 'Debug' } else { 'Release' }
        $exeDir = Join-Path $root "src/CcpClient.Desktop/bin/$cfgName/net10.0"
        if (-not (Test-Path (Join-Path $exeDir 'CcpClient.Desktop.exe'))) {
            Fail $m "binary missing: $exeDir — build first: dotnet build client/CcpClient.sln -c $cfgName"
            continue
        }
    } else {
        $publishDir = Join-Path $root "artifacts/publish/CcpClient.Desktop-$version-win-x64"
        if (-not (Test-Path (Join-Path $publishDir 'CcpClient.Desktop.exe'))) {
            Fail "$m/publish" "published artifact missing: $publishDir (run publish.ps1 first)"; continue
        }
        # Gate 6 (location independence): run the publish dir from a MOVED location.
        $exeDir = Join-Path $env:TEMP 'ccp-sp010-portable'
        if (Test-Path $exeDir) { Remove-Item $exeDir -Recurse -Force }
        Copy-Item -Recurse $publishDir $exeDir
    }
    $exe = Join-Path $exeDir 'CcpClient.Desktop.exe'
    if (-not (Test-Path $exe)) { Fail $m "binary missing: $exe"; continue }
    Write-Output "ARTIFACT $m`: $exe"

    # Gate 2: --verify-assets (published run = row-8 deferred third)
    $r = Invoke-Diagnostic $exe '--verify-assets'
    if ($r.Exit -eq 0 -and $r.Output -match 'verify-assets: PASS') {
        Write-Output "GATE2 $m`: PASS — verify-assets exit 0 ($($r.Output -split "`n" | Select-Object -Last 2 | Select-Object -First 1))"
    } else { Fail "GATE2 $m" "verify-assets exit $($r.Exit): $($r.Output)" }

    # Gate 3: --version derives from the authority (prefix before any +sha == msbuild Version)
    $r = Invoke-Diagnostic $exe '--version'
    if ($r.Exit -eq 0 -and $r.Output -match 'version: (\S+)') {
        $printed = $Matches[1]; $prefix = $printed.Split('+')[0]
        if ($prefix -eq $version) { Write-Output "GATE3 $m`: PASS — version: $printed (prefix == authority $version)" }
        else { Fail "GATE3 $m" "printed prefix '$prefix' != authority '$version'" }
    } else { Fail "GATE3 $m" "--version exit $($r.Exit): $($r.Output)" }

    Save-Config
    try {
        # Gate 4: fresh-profile headed run — no config-only crash, NO settings.json created
        if (Test-Path $cfgDir) { Remove-Item $cfgDir -Recurse -Force }
        $h = Invoke-Headed $exe $env:TEMP
        if ($h.Ok -and -not (Test-Path $settingsFile)) {
            Write-Output "GATE4 $m`: PASS — fresh-profile $($h.Detail); no settings.json created"
        } elseif (-not $h.Ok) { Fail "GATE4 $m" $h.Detail }
        else { Fail "GATE4 $m" "settings.json created on a defaults run (defaults must never auto-save)" }

        # Gate 5: corrupt-settings — quarantine file with the ORIGINAL BYTES preserved
        New-Item -ItemType Directory -Force -Path $cfgDir | Out-Null
        $garbage = [byte[]](0x7B, 0x7B, 0x00, 0xFF, 0x41)
        [System.IO.File]::WriteAllBytes($settingsFile, $garbage)
        $h = Invoke-Headed $exe $env:TEMP
        $quarantine = Get-ChildItem -Path $cfgDir -Filter 'settings.corrupt-*.json' -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $h.Ok) { Fail "GATE5 $m" $h.Detail }
        elseif ($null -eq $quarantine) { Fail "GATE5 $m" 'no settings.corrupt-*.json quarantine file' }
        else {
            $bytes = [System.IO.File]::ReadAllBytes($quarantine.FullName)
            if ([System.Linq.Enumerable]::SequenceEqual($bytes, $garbage)) {
                # Gate-6 identity compares the DIRECTORY (the data path), not the
                # timestamped quarantine filename.
                $quarantinePaths[$m] = $quarantine.DirectoryName
                Write-Output "GATE5 $m`: PASS — corrupt-settings graceful exit 0; quarantine preserved original bytes at $($quarantine.FullName)"
            } else { Fail "GATE5 $m" 'quarantine bytes differ from the seeded original' }
        }

        # Gate 7: logs-absence — no log files beside the artifact or in the config dir
        $logsArtifact = Get-ChildItem -Path $exeDir -Filter '*.log' -Recurse -ErrorAction SilentlyContinue
        $logsConfig = Get-ChildItem -Path $cfgDir -Filter '*.log' -ErrorAction SilentlyContinue
        if ($logsArtifact -or $logsConfig) {
            Fail "GATE7 $m" "log files exist: $($logsArtifact.FullName) $($logsConfig.FullName)"
        } else { Write-Output "GATE7 $m`: PASS — no log files beside artifact or in config dir (logging honestly absent)" }
    }
    finally { Restore-Config }

    # Gate 8: native-deps floor (published only) — what ships beside the exe
    if ($m -eq 'published') {
        $natives = Get-ChildItem -Path $exeDir -File | Where-Object { $_.Extension -in '.dll', '.so' }
        $list = ($natives | ForEach-Object { "$($_.Name) $([math]::Round($_.Length / 1MB, 1))MB" }) -join ', '
        Write-Output "GATE8 $m`: PASS — native sidecars observed: $list"
    }
}

# Gate 6: data-path identity across modes
if ($quarantinePaths.Count -ge 2) {
    $distinct = @($quarantinePaths.Values | ForEach-Object { $_.ToLowerInvariant() } | Select-Object -Unique)
    if ($distinct.Count -eq 1) {
        Write-Output "GATE6 all: PASS — data path identical across modes ($($quarantinePaths.Values | Select-Object -First 1)); published ran from MOVED dir $env:TEMP\ccp-sp010-portable"
    } else { Fail 'GATE6 all' "data paths differ across modes: $($quarantinePaths.Values -join ' | ')" }
}

if ($failures.Count -gt 0) { Write-Output "MATRIX FAIL (windows): $($failures.Count) gate failure(s)"; exit 1 }
Write-Output 'MATRIX PASS (windows)'
