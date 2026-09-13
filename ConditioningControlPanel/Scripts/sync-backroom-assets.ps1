<#
.SYNOPSIS
    Re-syncs the Back Room slot assets from blender-scripting into the client, then checks the glb
    node names against CONTRACT.md section 6.

.DESCRIPTION
    The slot cabinet is still being touched in Blender, so the copies under
    Resources\web\backroom\ are a SNAPSHOT. Run this after exporting a new slot.glb:

        powershell -ExecutionPolicy Bypass -File ConditioningControlPanel\scripts\sync-backroom-assets.ps1

    The source tree is READ-ONLY here: files are only ever copied out of it, never written back.
    For each file it prints size + md5 of the client copy before and after, so the PR body can
    record which snapshot shipped.

    The node check reads the "Driven nodes" paragraph of CONTRACT.md section 6 (so the contract,
    not this script, is the list), expands ranges like reel_1..3 and lights_chase_00..29, and
    looks every name up in the glb's JSON chunk. Names in parentheses there are materials and are
    skipped. A missing REQUIRED node fails the run (exit 1); a missing optional node is a warning,
    the same split the station page makes at load.

.PARAMETER Source
    blender-scripting slot folder. Defaults to C:\Projects\blender-scripting\slot.

.PARAMETER CheckOnly
    Skip the copy and only run the node check on the client's current slot.glb.
#>
param(
    [string]$Source = 'C:\Projects\blender-scripting\slot',
    [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'
$web = Join-Path $PSScriptRoot '..\Resources\web\backroom' | Resolve-Path | Select-Object -ExpandProperty Path

# source (relative to $Source) -> destination (relative to Resources\web\backroom)
$files = @(
    @{ From = 'out\slot.glb';           To = 'stations\slot\assets\slot.glb' },
    @{ From = 'out\emi-faces-slot.png'; To = 'stations\slot\assets\emi-faces-slot.png' },
    @{ From = 'out\emi-face-map.json';  To = 'stations\slot\assets\emi-face-map.json' },
    @{ From = 'refs\backroom_final.png'; To = 'room\backroom_final.png' }
)

# Only these may stop the page (CONTRACT hard rule); everything else degrades with a warning.
$required = @('cabinet', 'reel_1', 'reel_2', 'reel_3', 'lever', 'cam_seat', 'cam_target')

function Describe([string]$path) {
    if (-not (Test-Path $path)) { return '(missing)' }
    $len = (Get-Item $path).Length
    $md5 = (Get-FileHash $path -Algorithm MD5).Hash.ToLowerInvariant()
    return ('{0,10:N0} bytes  md5 {1}' -f $len, $md5)
}

if (-not $CheckOnly) {
    Write-Host "Syncing from $Source"
    foreach ($f in $files) {
        $src = Join-Path $Source $f.From
        $dst = Join-Path $web $f.To
        if (-not (Test-Path $src)) { throw "source missing: $src" }
        $before = Describe $dst
        New-Item -ItemType Directory -Force (Split-Path $dst) | Out-Null
        Copy-Item -LiteralPath $src -Destination $dst -Force
        Write-Host ''
        Write-Host $f.To
        Write-Host "  before $before"
        Write-Host "  after  $(Describe $dst)"
    }
}

# ---- node-name check ----
$contract = Get-Content (Join-Path $web 'CONTRACT.md') -Raw
$m = [regex]::Match($contract, '(?s)Driven nodes[^\n]*?:(.*?)(\r?\n\s*\r?\n)')
if (-not $m.Success) { throw 'CONTRACT.md section 6 "Driven nodes" paragraph not found' }
$para = [regex]::Replace($m.Groups[1].Value, '\([^)]*\)', '')   # drop material notes
$names = New-Object System.Collections.Generic.List[string]
foreach ($t in [regex]::Matches($para, '`([^`]+)`')) {
    $tok = $t.Groups[1].Value
    $r = [regex]::Match($tok, '^(.*?)(\d+)\.\.(\d+)$')
    if ($r.Success) {
        $w = $r.Groups[2].Value.Length
        for ($i = [int]$r.Groups[2].Value; $i -le [int]$r.Groups[3].Value; $i++) {
            $names.Add($r.Groups[1].Value + $i.ToString().PadLeft($w, '0'))
        }
    } else { $names.Add($tok) }
}

$glb = Join-Path $web 'stations\slot\assets\slot.glb'
$bytes = [System.IO.File]::ReadAllBytes($glb)
if ([System.BitConverter]::ToUInt32($bytes, 0) -ne 0x46546C67) { throw 'slot.glb is not a glb' }
$jsonLen = [System.BitConverter]::ToUInt32($bytes, 12)
$json = [System.Text.Encoding]::UTF8.GetString($bytes, 20, $jsonLen) | ConvertFrom-Json
$present = @{}
foreach ($n in $json.nodes) { if ($n.name) { $present[$n.name] = $true } }

Write-Host ''
Write-Host "Node check: $($names.Count) contract names against $($present.Count) glb nodes"
$missingRequired = 0
foreach ($req in $required) { if (-not $names.Contains($req)) { $names.Insert(0, $req) } }
foreach ($n in ($names | Select-Object -Unique)) {
    if ($present.ContainsKey($n)) { continue }
    if ($required -contains $n) {
        Write-Host "  MISSING (required) $n" -ForegroundColor Red
        $missingRequired++
    } else {
        Write-Host "  missing (optional, page degrades) $n" -ForegroundColor Yellow
    }
}
if ($missingRequired -gt 0) { Write-Host "FAIL: $missingRequired required node(s) missing"; exit 1 }
Write-Host 'OK: every required node is present'
