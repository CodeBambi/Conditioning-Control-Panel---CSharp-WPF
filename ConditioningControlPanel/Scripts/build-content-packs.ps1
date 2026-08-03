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

    KEEP IN SYNC (ENFORCED)
    -----------------------
    The $PackSpecs table below and the csproj exclude properties describe the same file set from
    opposite directions. A file matched by neither ships nowhere; a file matched by both ships
    twice (and the in-box copy shadows the pack forever, because ContentLocator probes
    BaseDirectory first). This is no longer a promise: $CsprojStripPatterns mirrors the csproj
    globs and Assert-NoStripPackDrift hard-fails the build on any disagreement in either
    direction. If you touch one, touch the other -- the script will tell you if you forgot.

    The third leg, installer.iss [InstallDelete], is GENERATED rather than mirrored: every run
    (and -DeletionsOnly on its own) rewrites <repo>\installer-content-deletions.iss with one
    exact-name deletion line per packed file, and installer.iss #includes it. Exact names,
    never wildcards, so files USERS hand-dropped into the swept folders survive upgrades. The
    fragment is committed - commit the regenerated copy whenever the pack set changes.
#>

[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$PreviousManifest,

    [string]$OutputDir,

    [string[]]$PackIds,

    # Resolve + report the file set without writing any zip. Use this to sanity-check that the
    # packs cover EXACTLY what the csproj strips (see KEEP IN SYNC above) before a real run.
    [switch]$ListOnly,

    # Regenerate ONLY installer-content-deletions.iss (the exact-name [InstallDelete] list that
    # installer.iss #includes) and exit. No zips, no manifest, no -Version needed. A normal pack
    # build regenerates it too; either way the file is COMMITTED, so run this after any change
    # to $PackSpecs / the csproj strip set and commit the result.
    [switch]$DeletionsOnly
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

# Committed next to installer.iss, which #includes it into [InstallDelete].
$DeletionsIss = Join-Path $RepoRoot 'installer-content-deletions.iss'

if (-not $Version -and -not $DeletionsOnly) {
    throw '-Version is required (e.g. 6.6.0) unless -DeletionsOnly.'
}
$CycleTag = if ($Version) { $verParts = $Version.Split('.'); "v$($verParts[0]).$($verParts[1]).0" } else { $null }

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
            # Cheshire is NOT a persona -- despite living under vn\vo alongside the three
            # builtin-* mod folders, she is the DTRH tutorial narrator for EVERY run:
            # cheshire_script.js hardcodes voFolder:'cheshire' and chaosRun.js mounts it
            # regardless of the active mod. Routing this with mod-locked (as the plan's §1 table
            # originally did) gives every Default/Bambi/Sissy user a silent FTUE.
            @{ Path = 'Resources\web\dtrh\assets\vn\vo\cheshire'; Ext = $AudioExt }
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
            # NOTE: vn\vo\cheshire is deliberately NOT here -- it is mod-independent narration
            # and lives in audio-web. See the comment there.
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
            # Kind='folder' = swept out of the source tree by a csproj strip glob, and therefore
            # subject to the drift self-check. Kind='file' = the .ccpmod archives, which left the
            # build by deleting their <Content> item, not by an exclude glob.
            $result.Add([pscustomobject]@{ Source = $f.FullName; Entry = $rel.Replace('\', '/'); Kind = 'folder' })
        }
    }

    foreach ($file in $spec.Files) {
        $abs = Join-Path $ProjectDir $file.Path
        if (-not (Test-Path -LiteralPath $abs)) {
            throw "Pack '$($spec.Id)': source file missing: $abs"
        }
        $result.Add([pscustomobject]@{ Source = $abs; Entry = $file.Entry; Kind = 'file' })
    }

    # Deterministic ordering. This MUST be ORDINAL. Sort-Object collates with the CURRENT CULTURE,
    # and PS 5.1 (NLS) and pwsh 7 (ICU) disagree about where '_' , '-' and digits sort -- so
    # rebuilding this cycle's packs in the other shell would reorder every zip entry, change all
    # six sha256s, bump every contentVersion and make the entire installed base re-download
    # ~1.1 GB for zero content change. Ordinal is locale-proof.
    #
    # !!! Never "optimise" this to the keyed [Array]::Sort($keys, $items, comparer). PowerShell
    # binds that to the generic Sort[TKey,TValue] overload and CONVERTS $items to a fresh
    # TValue[] before the call, so the reorder lands in a throwaway copy and the original array
    # comes back untouched -- a silent no-op that left the zips in filesystem-enumeration order
    # (stable on one NTFS volume, hence green determinism re-runs, but not across filesystems).
    # SortedList mutates its own storage and cannot fail that way; .Add also throws on a
    # duplicate entry path, which would be a $PackSpecs bug worth dying on.
    # @() keeps a single-file pack (mod-drone) an array rather than a scalar.
    $sorted = New-Object 'System.Collections.Generic.SortedList[string,object]' ([System.StringComparer]::Ordinal)
    foreach ($item in $result) { $sorted.Add($item.Entry, $item) }
    return @($sorted.Values)
}

function New-PackZip($spec, [string]$ZipPath, $Files) {
    $files = @($Files)

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
# Strip/pack drift self-check
# ---------------------------------------------------------------------------------------------
# !!! HAND-MAINTAINED MIRROR OF ConditioningControlPanel.csproj !!!
# These globs must be character-for-character the same file set as the
# $(ContentPackSoundsExclude) + $(ContentPackWebExclude) properties in the csproj -- the set the
# BUILD STRIPS. $PackSpecs above is the set the PACKS SHIP. The two are edited independently and
# nothing in MSBuild or Inno cross-checks them, so this list plus Assert-NoStripPackDrift is the
# only thing standing between a one-line csproj edit and a file that ships NOWHERE (stripped but
# never packed = silently missing audio for everyone) or TWICE (packed but not stripped = the
# in-box copy shadows the downloaded pack forever, because ContentLocator probes BaseDirectory
# first).
#
# Deliberately NOT a catch-all: pack membership has to stay explicit, because which pack a file
# lands in is a product decision (persona gating), not something a glob can infer. So we keep the
# enumeration AND fail loudly the moment it stops matching the csproj.
#
# Pattern shape: <dir>\**\<filemask>  (the only shape the csproj uses).
$CsprojStripPatterns = @(
    # $(ContentPackSoundsExclude)
    'Resources\sounds\flashes_audio\**\*.mp3'
    'Resources\sounds\flashes_audio\**\*.wav'
    'Resources\sounds\companion_audio\mods\builtin-bambisleep\**\*.mp3'
    'Resources\sounds\companion_audio\mods\builtin-bambisleep\**\*.wav'
    'Resources\sounds\companion_audio\mods\builtin-bambisleep\**\*.ogg'
    'Resources\sounds\companion_audio\mods\builtin-bambisleep\**\*.m4a'
    'Resources\sounds\companion_audio\mods\builtin-locked\**\*.mp3'
    'Resources\sounds\companion_audio\mods\builtin-locked\**\*.wav'
    'Resources\sounds\companion_audio\mods\builtin-locked\**\*.ogg'
    'Resources\sounds\companion_audio\mods\builtin-locked\**\*.m4a'
    'Resources\sounds\companion_audio\mods\builtin-sissyhypno\**\*.mp3'
    'Resources\sounds\companion_audio\mods\builtin-sissyhypno\**\*.wav'
    'Resources\sounds\companion_audio\mods\builtin-sissyhypno\**\*.ogg'
    'Resources\sounds\companion_audio\mods\builtin-sissyhypno\**\*.m4a'
    'Resources\sounds\companion_audio\mods\builtin-sissyhypno\**\*.png'
    # $(ContentPackWebExclude)
    'Resources\web\intake\assets\vo\**\*.mp3'
    'Resources\web\intake\assets\sfx\**\*.mp3'
    'Resources\web\intake\assets\music\**\*.mp3'
    'Resources\web\dtrh\assets\**\*.mp3'
)

# Expands $CsprojStripPatterns against the SOURCE TREE. Returns a hashtable of full path -> $true
# (lower-cased keys; NTFS is case-insensitive and MSBuild globs are too).
function Get-StrippedFileSet {
    $set = @{}
    foreach ($pattern in $CsprojStripPatterns) {
        $split = $pattern -split '\\\*\*\\'
        if ($split.Count -ne 2) {
            throw "Strip pattern '$pattern' is not of the form <dir>\**\<filemask>. " +
                  "Either fix the pattern or teach Get-StrippedFileSet the new shape."
        }
        $dir  = Join-Path $ProjectDir $split[0]
        $mask = $split[1]
        if (-not (Test-Path -LiteralPath $dir)) {
            throw "Strip pattern '$pattern': source folder missing: $dir`n" +
                  "       The csproj strips a folder that no longer exists - drop the pattern from BOTH files."
        }
        # -Filter is the fast path, but the Win32 wildcard behind it also matches 8.3 short-name
        # aliases (the classic "*.htm matches .html" trap), whereas MSBuild's **\*.mp3 is exact.
        # Re-check the extension so this mirrors MSBuild and not FindFirstFile.
        $ext = if ($mask -match '^\*(\.[A-Za-z0-9]+)$') { $Matches[1].ToLowerInvariant() } else { $null }
        foreach ($f in @(Get-ChildItem -LiteralPath $dir -Recurse -File -Filter $mask)) {
            if ($ext -and $f.Extension.ToLowerInvariant() -ne $ext) { continue }
            $set[$f.FullName.ToLowerInvariant()] = $true
        }
    }
    return $set
}

# Hard-fails if the stripped set and the packed set disagree in either direction.
# $Resolved: ordered hashtable of packId -> resolved file list (output of Resolve-PackFiles).
function Assert-NoStripPackDrift($Resolved) {
    $stripped = Get-StrippedFileSet

    # Only Kind='folder' entries are in scope: the two .ccpmod archives left the build by having
    # their <Content> item deleted outright, so no strip glob covers them and none should.
    $packed = @{}
    foreach ($id in $Resolved.Keys) {
        foreach ($f in @($Resolved[$id])) {
            if ($f.Kind -ne 'folder') { continue }
            $packed[$f.Source.ToLowerInvariant()] = $id
        }
    }

    $strippedNotPacked = @($stripped.Keys | Where-Object { -not $packed.ContainsKey($_) })
    $packedNotStripped = @($packed.Keys   | Where-Object { -not $stripped.ContainsKey($_) })

    if ($strippedNotPacked.Count -or $packedNotStripped.Count) {
        $msg = "KEEP IN SYNC violation between ConditioningControlPanel.csproj and `$PackSpecs.`n"
        if ($strippedNotPacked.Count) {
            $msg += "`n  STRIPPED BUT NOT PACKED ($($strippedNotPacked.Count) file(s)) - these would ship NOWHERE:`n"
            foreach ($p in @($strippedNotPacked | Sort-Object)[0..([Math]::Min(19, $strippedNotPacked.Count - 1))]) {
                $msg += "    $p`n"
            }
            if ($strippedNotPacked.Count -gt 20) { $msg += "    ... and $($strippedNotPacked.Count - 20) more`n" }
            $msg += "  Fix: add the folder to `$PackSpecs (choosing a pack), or stop stripping it in the csproj.`n"
        }
        if ($packedNotStripped.Count) {
            $msg += "`n  PACKED BUT NOT STRIPPED ($($packedNotStripped.Count) file(s)) - these would ship TWICE," +
                    " and the in-box copy would shadow the pack forever:`n"
            foreach ($p in @($packedNotStripped | Sort-Object)[0..([Math]::Min(19, $packedNotStripped.Count - 1))]) {
                $msg += "    $p`n"
            }
            if ($packedNotStripped.Count -gt 20) { $msg += "    ... and $($packedNotStripped.Count - 20) more`n" }
            $msg += "  Fix: add the extension/folder to the csproj Exclude properties (BOTH the plain and the" +
                    " ...Abs variant) and to `$CsprojStripPatterns above, then rerun (installer-content-deletions.iss regenerates itself).`n"
        }
        throw $msg
    }

    Write-Host ("  strip/pack self-check OK ({0} files stripped by the csproj, all packed exactly once)" -f $stripped.Count) -ForegroundColor DarkGray
}

# ---------------------------------------------------------------------------------------------
# installer.iss [InstallDelete] fragment
# ---------------------------------------------------------------------------------------------
# Emits installer-content-deletions.iss: one explicit "Type: files" line per file that moved
# into a pack, plus deepest-first "dirifempty" lines. installer.iss #includes it.
#
# WHY EXACT NAMES AND NEVER WILDCARDS: a wildcard ("flashes_audio\*.mp3") cannot tell a shipped
# file from one the USER hand-dropped into the same folder, so it would delete user content on
# every upgrade. An explicit list only ever matches what we shipped; user files survive in
# place and keep working (ContentLocator unions the install dir with the content root).
# Shipped manifests (bark_rules.json, vn\manifest.json, ...) are inherently safe too: they are
# not pack payload, so they can never appear in this list.
#
# Folders below vn\vo that hold no surviving manifest can genuinely empty out once all four
# persona VO folders are swept, so vn\vo itself is the one ancestor added by hand here.
$ExtraDirIfEmpty = @('Resources\web\dtrh\assets\vn\vo')

function Write-InstallerDeletions($Resolved, [string]$Path) {
    # Install-relative source paths, both kinds: loose audio AND the two .ccpmod archives
    # ($f.Source is always under $ProjectDir; $f.Entry is the ZIP path, wrong for ccpmods).
    $rels = New-Object System.Collections.Generic.List[string]
    foreach ($id in $Resolved.Keys) {
        foreach ($f in @($Resolved[$id])) {
            $rels.Add($f.Source.Substring($ProjectDir.Length).TrimStart('\'))
        }
    }
    $files = $rels.ToArray()
    # Ordinal for the same reason the zip entries are (see Resolve-PackFiles): the committed
    # file must not churn just because the script ran under a different culture/shell.
    [Array]::Sort($files, [System.StringComparer]::Ordinal)

    # dirifempty candidates: every dir on the chain from each deleted file UP TO the pack-spec
    # root folder it came from (inclusive). Never higher - parents like Resources\sounds hold
    # surviving content and must not even be candidates.
    $roots = New-Object System.Collections.Generic.List[string]
    foreach ($spec in $PackSpecs) {
        foreach ($fo in $spec.Folders) { $roots.Add([string]$fo.Path) }
        foreach ($fi in $spec.Files)   { $roots.Add((Split-Path -Parent $fi.Path)) }
    }

    $dirSet = @{}
    foreach ($rel in $files) {
        $dir = Split-Path -Parent $rel
        $best = $null
        foreach ($r in $roots) {
            $isUnder = ($dir.Length -eq $r.Length -and $dir -ieq $r) -or
                       ($dir.Length -gt $r.Length -and $dir.Substring(0, $r.Length + 1) -ieq ($r + '\'))
            if ($isUnder -and ($null -eq $best -or $r.Length -gt $best.Length)) { $best = $r }
        }
        if ($null -eq $best) {
            throw "Deletion generator: '$rel' is under no `$PackSpecs root - teach Write-InstallerDeletions about it."
        }
        $d = $dir
        while ($true) {
            $dirSet[$d.ToLowerInvariant()] = $d
            if ($d -ieq $best) { break }
            $d = Split-Path -Parent $d
        }
    }
    foreach ($extra in $ExtraDirIfEmpty) { $dirSet[$extra.ToLowerInvariant()] = $extra }

    # Deepest first, so a child folder is removed before its parent is tested for emptiness.
    # Key = zero-padded inverted depth + path -> one ordinal sort gives both orderings.
    # SortedList, not keyed [Array]::Sort -- see the warning in Resolve-PackFiles.
    $sortedDirs = New-Object 'System.Collections.Generic.SortedList[string,string]' ([System.StringComparer]::Ordinal)
    foreach ($d in $dirSet.Values) {
        $sortedDirs.Add((('{0:d3}' -f (999 - ($d -split '\\').Count)) + $d), $d)
    }
    $dirs = @($sortedDirs.Values)

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('; ============================================================================================')
    [void]$sb.AppendLine('; AUTO-GENERATED by ConditioningControlPanel\Scripts\build-content-packs.ps1 - DO NOT HAND-EDIT.')
    [void]$sb.AppendLine('; Regenerate (and commit) after any change to $PackSpecs / the csproj strip set:')
    [void]$sb.AppendLine(';   powershell -ExecutionPolicy Bypass -File ConditioningControlPanel\Scripts\build-content-packs.ps1 -DeletionsOnly')
    [void]$sb.AppendLine(';')
    [void]$sb.AppendLine('; #included from installer.iss [InstallDelete]. Every file that moved out of the installer')
    [void]$sb.AppendLine('; into a content pack, deleted from {app} by EXACT name on upgrade. No wildcards: files a')
    [void]$sb.AppendLine('; user hand-dropped into these folders are never matched and survive. dirifempty removes')
    [void]$sb.AppendLine('; only folders the sweep actually emptied.')
    [void]$sb.AppendLine(('; {0} files, {1} folder candidates.' -f $files.Count, $dirs.Count))
    [void]$sb.AppendLine('; ============================================================================================')
    foreach ($rel in $files) {
        # Real pack filenames are phrase text and DO go past ASCII (en dash, swung dash), hence
        # UTF-8 WITH BOM below - Inno 6 is Unicode and reads BOM'd .iss natively. Quotes and
        # control chars stay fatal: there is no safe way to quote them in an Inno Name.
        foreach ($ch in $rel.ToCharArray()) {
            if ([int]$ch -lt 32 -or $ch -eq '"') {
                throw "Deletion generator: '$rel' contains a character Inno cannot safely express (char $([int]$ch))."
            }
        }
        # '{' opens an Inno constant; '{{' is the literal escape.
        [void]$sb.AppendLine(('Type: files;      Name: "{{app}}\{0}"' -f $rel.Replace('{', '{{')))
    }
    foreach ($d in $dirs) {
        [void]$sb.AppendLine(('Type: dirifempty; Name: "{{app}}\{0}"' -f $d.Replace('{', '{{')))
    }

    # UTF-8 WITH BOM (Inno reads BOM-less files as ANSI) + CRLF, byte-stable across runs, so
    # git diffs are content-only.
    [System.IO.File]::WriteAllText($Path, $sb.ToString(), (New-Object System.Text.UTF8Encoding($true)))
    Write-Host ("  installer deletions: {0} ({1} files + {2} dirifempty)" -f $Path, $files.Count, $dirs.Count) -ForegroundColor DarkGray
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
    # Set-StrictMode Latest turns a missing property into a hard throw, so every field of a
    # foreign document is probed before it is read. A malformed / partial previous manifest must
    # degrade to "no previous data" (every pack starts at contentVersion 1), never crash the
    # build -- worst case users re-download, which is exactly what happens without the flag.
    $prevPacks = @()
    if ($prevDoc -is [array]) {
        $prevPacks = @($prevDoc)
    }
    elseif ($null -ne $prevDoc -and ($prevDoc.PSObject.Properties.Name -contains 'packs') -and $null -ne $prevDoc.packs) {
        $prevPacks = @($prevDoc.packs)
    }
    else {
        Write-Warning "Previous manifest has no 'packs' array - ignoring it (all packs restart at contentVersion 1)."
    }
    foreach ($p in $prevPacks) {
        if ($null -eq $p -or ($p.PSObject.Properties.Name -notcontains 'id')) {
            Write-Warning "Previous manifest: skipping an entry with no 'id'."
            continue
        }
        $prev[[string]$p.id] = $p
    }
    Write-Host "Previous manifest: $PreviousManifest ($($prev.Count) packs)" -ForegroundColor DarkGray
}

# ---------------------------------------------------------------------------------------------
# Resolve every pack up front (even the ones -PackIds excludes: the drift self-check below is
# only meaningful against the COMPLETE packed set) and refuse to go further if the csproj strip
# set and the packed set have drifted apart.
# ---------------------------------------------------------------------------------------------
$Resolved = [ordered]@{}
# @() at the call site too: PowerShell unrolls a returned single-element array.
foreach ($spec in $PackSpecs) { $Resolved[$spec.Id] = @(Resolve-PackFiles $spec) }
Assert-NoStripPackDrift $Resolved

# ---------------------------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------------------------
if ($ListOnly) {
    Write-Host ""
    Write-Host "Content packs  cycle $CycleTag  (-ListOnly: nothing will be written)" -ForegroundColor Cyan
    Write-Host ""
    $grand = 0; $grandCount = 0; $longest = 0
    foreach ($spec in $PackSpecs) {
        $files = @($Resolved[$spec.Id])
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

# Always regenerate the committed [InstallDelete] fragment when the resolved set is in hand --
# a pack build whose file set changed but whose fragment didn't is exactly the drift this whole
# script exists to prevent. (-ListOnly returned above: it promises to write nothing.)
Write-InstallerDeletions $Resolved $DeletionsIss
if ($DeletionsOnly) {
    Write-Host ""
    Write-Host "  -DeletionsOnly: fragment written. Commit it if 'git status' says it changed." -ForegroundColor Yellow
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
        $count = New-PackZip $spec $zipPath $Resolved[$spec.Id]
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
        $old      = $prev[$spec.Id]
        $oldNames = $old.PSObject.Properties.Name
        $oldSha   = if ($oldNames -contains 'sha256') { [string]$old.sha256 } else { '' }
        $oldVer   = 0
        $hasVer   = ($oldNames -contains 'contentVersion') -and
                    [int]::TryParse([string]$old.contentVersion, [ref]$oldVer) -and ($oldVer -ge 1)

        if (-not $oldSha -or -not $hasVer) {
            Write-Warning ("Previous manifest entry for '{0}' is missing sha256/contentVersion - treating it as a new pack (contentVersion 1)." -f $spec.Id)
        }
        elseif ($oldSha -eq $sha) { $contentVersion = $oldVer }
        else                      { $contentVersion = $oldVer + 1 }
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
