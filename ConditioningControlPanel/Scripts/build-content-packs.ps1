<#
.SYNOPSIS
    Builds the 6 release-hosted content packs + content-manifest.json.

.DESCRIPTION
    Phase E of docs/CONTENT_PACKS_PLAN.md. ~1.36 GB of version-stable audio (plus the two
    bundled .ccpmod archives) no longer ships in the installer -- see the
    $(ContentPackSoundsExclude) / $(ContentPackWebExclude) properties in
    ConditioningControlPanel.csproj, which strip exactly the same file set from the build.
    This script zips that payload straight out of the SOURCE TREE (not the publish output,
    which by definition no longer contains it) and emits the manifest the client reads.

    Run on X.Y.0 releases only. The zips + manifest are uploaded as assets on the vX.Y.0
    GitHub release; every X.Y.Z patch in that cycle reuses them.

.PARAMETER Version
    Release version, e.g. 6.6.0. Recorded in the manifest as cycleTag "v6.6.0" and used by the
    client to derive the download URL. Only major.minor matter for hosting; the patch is
    normalised to 0 in the cycleTag.

.PARAMETER PreviousManifest
    Optional path to the content-manifest.json from the previous cycle. A pack whose sha256 is
    unchanged KEEPS its old contentVersion, so users who already hold that pack re-download
    nothing when they move to the new cycle. Changed (or new) packs get contentVersion+1.

.PARAMETER OutputDir
    Defaults to <repo-root>\releases\content-packs. Created if missing.

.PARAMETER PackIds
    Optional subset to (re)build, e.g. -PackIds mod-sissy,audio-web. The manifest is still
    written for ALL packs -- packs not rebuilt are read back from OutputDir, so a partial run
    never produces a truncated manifest.

.NOTES
    LAYOUT CONTRACT
    ---------------
    Every pack has targetRoot "" and every zip entry is a path relative to the app's
    BaseDirectory. The client extracts the whole zip into
        %LOCALAPPDATA%\ConditioningControlPanel\content\
    which mirrors the install-dir layout (plan section 3), so
        Resources/sounds/flashes_audio/x.mp3  ->  content\Resources\sounds\flashes_audio\x.mp3

    The two .ccpmod archives are the exception in KIND, not in mechanism: they are themselves
    zips consumed by ModService, so they cannot be unpacked into Resources\. They ride inside
    their pack at the stable entry path
        packs/drone-mode.ccpmod
        packs/locked-resources.ccpmod
    and therefore land at content\packs\<name>.ccpmod, where the mod-install wave (phase C)
    picks them up. They are also listed per-pack in the manifest's "ccpmods" array so the
    client never has to guess. NOTE: this deviates from the phase-E brief's suggestion of
    targetRoot "packs" for those two packs -- mod-locked carries BOTH loose Resources\ audio
    AND a .ccpmod, so a single per-pack targetRoot cannot describe it. One uniform extraction
    root (content\) plus an explicit ccpmods list is the only self-consistent contract.

    DETERMINISM
    -----------
    contentVersion only means something if identical content produces an identical sha256, so
    the zips are built deterministically: entries are sorted by path, written with '/'
    separators, and stamped with a fixed timestamp (git does not preserve mtimes, so without
    this every fresh clone would produce a new hash and force a global re-download).

    LONG PATHS
    ----------
    builtin-sissyhypno\flashes_audio has filenames that push the absolute path near MAX_PATH.
    We zip directly from the source tree using \\?\-prefixed paths rather than robocopy-staging
    into a temp dir first -- that avoids both the MAX_PATH ceiling and a pointless 1.36 GB copy.

    KEEP IN SYNC
    ------------
    The $PackSpecs table below and the csproj exclude properties describe the same file set from
    opposite directions. A file matched by neither ships nowhere; a file matched by both ships
    twice. If you touch one, touch the other.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$PreviousManifest,

    [string]$OutputDir,

    [string[]]$PackIds,

    # Resolve + report the file set without writing any zip. Use this to sanity-check that the
    # packs cover EXACTLY what the csproj strips (see KEEP IN SYNC above) before a real run.
    [switch]$ListOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# ---------------------------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------------------------
$ProjectDir = Split-Path -Parent $PSScriptRoot                     # ...\ConditioningControlPanel
$RepoRoot   = Split-Path -Parent $ProjectDir                       # ...\<repo>
if (-not $OutputDir) { $OutputDir = Join-Path $RepoRoot 'releases\content-packs' }

$verParts = $Version.Split('.')
$CycleTag = "v$($verParts[0]).$($verParts[1]).0"

# Fixed zip timestamp -- see DETERMINISM above.
$FixedStamp = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)

# ---------------------------------------------------------------------------------------------
# Pack definitions (docs/CONTENT_PACKS_PLAN.md section 1)
#
#   Folders : @{ Path = <dir relative to ConditioningControlPanel\>; Ext = <extensions> }
#             Recursive. Only the listed extensions are taken, so manifests (*.json, *.js) and
#             art that stays in-box are never swept up by accident.
#   Files   : @{ Path = <file relative to ConditioningControlPanel\>; Entry = <path inside zip> }
# ---------------------------------------------------------------------------------------------
$AudioExt = @('.mp3', '.wav', '.ogg', '.m4a')

$PackSpecs = @(
    @{
        Id      = 'audio-base'
        File    = 'audio-base.zip'
        Label   = 'Baseline voice'
        Folders = @(
            @{ Path = 'Resources\sounds\flashes_audio'; Ext = $AudioExt }
        )
        Files   = @()
    },
    @{
        # Non-persona web audio. Everything else under dtrh\assets (barks\manifest.js, vn art,
        # bubble sprites, cheshire_script.js) stays in the installer.
        Id      = 'audio-web'
        File    = 'audio-web.zip'
        Label   = 'Intake + DTRH audio'
        Folders = @(
            @{ Path = 'Resources\web\intake\assets\vo';        Ext = $AudioExt }
            @{ Path = 'Resources\web\intake\assets\sfx';       Ext = $AudioExt }
            @{ Path = 'Resources\web\intake\assets\music';     Ext = $AudioExt }
            @{ Path = 'Resources\web\dtrh\assets\audio';       Ext = $AudioExt }  # drone1.mp3
            @{ Path = 'Resources\web\dtrh\assets\bubbles';     Ext = $AudioExt }  # drops/sfx/voices
        )
        Files   = @()
    },
    @{
        Id      = 'mod-bambi'
        File    = 'mod-bambi.zip'
        Label   = 'BambiSleep companion voice'
        Folders = @(
            @{ Path = 'Resources\sounds\companion_audio\mods\builtin-bambisleep'; Ext = $AudioExt }
            @{ Path = 'Resources\web\dtrh\assets\vn\vo\builtin-bambisleep';       Ext = $AudioExt }
        )
        Files   = @()
    },
    @{
        # Sissy moves its portraits too -- the whole payload except the three .json manifests.
        Id      = 'mod-sissy'
        File    = 'mod-sissy.zip'
        Label   = 'Sissy Hypno companion voice + portraits'
        Folders = @(
            @{ Path = 'Resources\sounds\companion_audio\mods\builtin-sissyhypno'; Ext = ($AudioExt + '.png') }
            @{ Path = 'Resources\web\dtrh\assets\barks\sissy';                    Ext = $AudioExt }
            @{ Path = 'Resources\web\dtrh\assets\vn\vo\builtin-sissyhypno';       Ext = $AudioExt }
        )
        Files   = @()
    },
    @{
        Id      = 'mod-locked'
        File    = 'mod-locked.zip'
        Label   = 'Locked / Circe voice + mod art'
        Folders = @(
            @{ Path = 'Resources\sounds\companion_audio\mods\builtin-locked'; Ext = $AudioExt }
            @{ Path = 'Resources\web\dtrh\assets\barks\circe';                Ext = $AudioExt }
            @{ Path = 'Resources\web\dtrh\assets\vn\vo\builtin-locked';       Ext = $AudioExt }
            @{ Path = 'Resources\web\dtrh\assets\vn\vo\cheshire';             Ext = $AudioExt }
        )
        Files   = @(
            @{ Path = 'LockedMod\locked-resources.ccpmod'; Entry = 'packs/locked-resources.ccpmod' }
        )
    },
    @{
        Id      = 'mod-drone'
        File    = 'mod-drone.zip'
        Label   = 'Drone Mode mod'
        Folders = @()
        Files   = @(
            @{ Path = 'DroneMod\drone-mode.ccpmod'; Entry = 'packs/drone-mode.ccpmod' }
        )
    }
)

# ---------------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------------

# \\?\ prefix so File APIs survive paths past MAX_PATH (builtin-sissyhypno\flashes_audio).
function Get-LongPath([string]$Path) {
    if ($Path.StartsWith('\\?\')) { return $Path }
    if ($Path.StartsWith('\\'))   { return '\\?\UNC\' + $Path.Substring(2) }
    return '\\?\' + $Path
}

function Get-Sha256([string]$Path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $fs = [System.IO.File]::OpenRead((Get-LongPath $Path))
        try { return ([BitConverter]::ToString($sha.ComputeHash($fs))).Replace('-', '').ToLowerInvariant() }
        finally { $fs.Dispose() }
    } finally { $sha.Dispose() }
}

# Resolves a pack spec to a sorted list of @{ Source; Entry } with '/' entry separators.
function Resolve-PackFiles($spec) {
    $result = New-Object System.Collections.Generic.List[object]

    foreach ($folder in $spec.Folders) {
        $abs = Join-Path $ProjectDir $folder.Path
        if (-not (Test-Path -LiteralPath $abs)) {
            throw "Pack '$($spec.Id)': source folder missing: $abs`n" +
                  "       Shipping an empty pack would silently break the feature. Fix the path in `$PackSpecs."
        }
        $extSet = @{}
        foreach ($e in $folder.Ext) { $extSet[$e.ToLowerInvariant()] = $true }

        $found = Get-ChildItem -LiteralPath $abs -Recurse -File |
                 Where-Object { $extSet.ContainsKey($_.Extension.ToLowerInvariant()) }
        if (-not $found) {
            throw "Pack '$($spec.Id)': no files matched $($folder.Ext -join ',') under $abs"
        }
        foreach ($f in $found) {
            $rel = $f.FullName.Substring($ProjectDir.Length).TrimStart('\')
            $result.Add([pscustomobject]@{ Source = $f.FullName; Entry = $rel.Replace('\', '/') })
        }
    }

    foreach ($file in $spec.Files) {
        $abs = Join-Path $ProjectDir $file.Path
        if (-not (Test-Path -LiteralPath $abs)) {
            throw "Pack '$($spec.Id)': source file missing: $abs"
        }
        $result.Add([pscustomobject]@{ Source = $abs; Entry = $file.Entry })
    }

    # Deterministic ordering (ordinal, so it does not drift with the machine's locale).
    # @() keeps a single-file pack (mod-drone) an array rather than a scalar.
    return @($result | Sort-Object -Property @{ Expression = { $_.Entry }; Ascending = $true })
}

function New-PackZip($spec, [string]$ZipPath) {
    $files = @(Resolve-PackFiles $spec)

    if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
    $tmp = "$ZipPath.tmp"
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force }

    $zip = [System.IO.Compression.ZipFile]::Open($tmp, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($f in $files) {
            $entry = $zip.CreateEntry($f.Entry, [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $FixedStamp
            $in  = [System.IO.File]::OpenRead((Get-LongPath $f.Source))
            try {
                $out = $entry.Open()
                try { $in.CopyTo($out) } finally { $out.Dispose() }
            } finally { $in.Dispose() }
        }
    } finally { $zip.Dispose() }

    Move-Item -LiteralPath $tmp -Destination $ZipPath -Force
    return $files.Count
}

# ---------------------------------------------------------------------------------------------
# Previous manifest (contentVersion carry-over)
# ---------------------------------------------------------------------------------------------
$prev = @{}
if ($PreviousManifest) {
    if (-not (Test-Path -LiteralPath $PreviousManifest)) {
        throw "-PreviousManifest not found: $PreviousManifest"
    }
    $prevDoc = Get-Content -LiteralPath $PreviousManifest -Raw | ConvertFrom-Json
    # Accept both the wrapped object and a bare array, so an older manifest still works.
    $prevPacks = if ($prevDoc -is [array]) { $prevDoc } else { $prevDoc.packs }
    foreach ($p in $prevPacks) { $prev[$p.id] = $p }
    Write-Host "Previous manifest: $PreviousManifest ($($prev.Count) packs)" -ForegroundColor DarkGray
}

# ---------------------------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------------------------
if ($ListOnly) {
    Write-Host ""
    Write-Host "Content packs  cycle $CycleTag  (-ListOnly: nothing will be written)" -ForegroundColor Cyan
    Write-Host ""
    $grand = 0; $grandCount = 0; $longest = 0
    foreach ($spec in $PackSpecs) {
        # @() at the call site too: PowerShell unrolls a returned single-element array.
        $files = @(Resolve-PackFiles $spec)
        $bytes = [long]((@($files) | ForEach-Object { (Get-Item -LiteralPath (Get-LongPath $_.Source)).Length } |
                  Measure-Object -Sum).Sum)
        $max = [int]((@($files) | ForEach-Object { $_.Source.Length } | Measure-Object -Maximum).Maximum)
        if ($max -gt $longest) { $longest = $max }
        $grand += $bytes; $grandCount += $files.Count
        Write-Host ("  {0,-11} {1,6} files {2,9:n1}M  longest source path {3}" -f $spec.Id, $files.Count, ($bytes / 1MB), $max)
    }
    Write-Host ""
    Write-Host ("  TOTAL       {0,6} files {1,9:n1}M" -f $grandCount, ($grand / 1MB)) -ForegroundColor Cyan
    Write-Host ("  Longest source path: {0} chars (MAX_PATH is 260; zipping uses \\?\ so this is informational)" -f $longest) -ForegroundColor DarkGray
    Write-Host ""
    return
}

if (-not (Test-Path -LiteralPath $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

Write-Host ""
Write-Host "Content packs  cycle $CycleTag" -ForegroundColor Cyan
Write-Host "  source : $ProjectDir"
Write-Host "  output : $OutputDir"
Write-Host ""

$entries = New-Object System.Collections.Generic.List[object]

foreach ($spec in $PackSpecs) {
    $zipPath = Join-Path $OutputDir $spec.File
    $build   = (-not $PackIds) -or ($PackIds -contains $spec.Id)

    if ($build) {
        Write-Host ("  {0,-11} building..." -f $spec.Id) -NoNewline
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $count = New-PackZip $spec $zipPath
        $sw.Stop()
        Write-Host (" {0} files in {1:n1}s" -f $count, $sw.Elapsed.TotalSeconds)
    }
    else {
        if (-not (Test-Path -LiteralPath $zipPath)) {
            throw "Pack '$($spec.Id)' was excluded by -PackIds but $zipPath does not exist, so the manifest would be incomplete."
        }
        Write-Host ("  {0,-11} reused (not in -PackIds)" -f $spec.Id) -ForegroundColor DarkGray
        $count = $null
    }

    $sha  = Get-Sha256 $zipPath
    $size = (Get-Item -LiteralPath $zipPath).Length

    $contentVersion = 1
    if ($prev.ContainsKey($spec.Id)) {
        $old = $prev[$spec.Id]
        if ($old.sha256 -eq $sha) { $contentVersion = [int]$old.contentVersion }
        else                      { $contentVersion = [int]$old.contentVersion + 1 }
    }

    $ccpmods = @($spec.Files | ForEach-Object { $_.Entry })

    $entries.Add([ordered]@{
        id             = $spec.Id
        file           = $spec.File
        label          = $spec.Label
        sizeBytes      = $size
        sha256         = $sha
        contentVersion = $contentVersion
        targetRoot     = ''          # extract into %LOCALAPPDATA%\...\content\ -- see NOTES
        ccpmods        = $ccpmods    # entry paths of .ccpmod archives inside this zip (may be empty)
    })
}

# Envelope field names must match Models\ContentPackInfo.cs (ReleaseContentManifest):
# schemaVersion / cycle / packs. cycleTag + builtFrom + generatedUtc are extra human-facing
# fields; Newtonsoft ignores unknown properties, so they are free.
$manifest = [ordered]@{
    schemaVersion = 1
    cycle         = $CycleTag
    cycleTag      = $CycleTag
    builtFrom     = $Version
    generatedUtc  = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    packs         = $entries
}

$manifestPath = Join-Path $OutputDir 'content-manifest.json'
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

# ---------------------------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------------------------
Write-Host ""
Write-Host ("  {0,-11} {1,10}  {2,3}  {3}" -f 'PACK', 'SIZE', 'VER', 'SHA256') -ForegroundColor DarkGray
$total = 0
foreach ($e in $entries) {
    $total += $e.sizeBytes
    Write-Host ("  {0,-11} {1,9:n1}M  {2,3}  {3}" -f $e.id, ($e.sizeBytes / 1MB), $e.contentVersion, $e.sha256.Substring(0, 16))
}
Write-Host ("  {0,-11} {1,9:n1}M" -f 'TOTAL', ($total / 1MB)) -ForegroundColor Cyan
Write-Host ""
Write-Host "  manifest: $manifestPath" -ForegroundColor Green
Write-Host ""
Write-Host "  Next: upload all 6 zips + content-manifest.json as assets on the $CycleTag GitHub release." -ForegroundColor Yellow
Write-Host ""
