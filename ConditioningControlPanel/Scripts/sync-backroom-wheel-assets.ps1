<#
.SYNOPSIS
    Re-syncs the Daily Daze wheel station assets from blender-scripting into the client, then checks the
    glb node names the station page drives (stations\wheel\nodes.js).

.DESCRIPTION
    The copies under Resources\web\backroom\stations\wheel\assets\ are a SNAPSHOT of the owner-approved
    wheel (blender-scripting wheel\APPROVED.md). Run this after a new wheel.glb export:

        powershell -ExecutionPolicy Bypass -File ConditioningControlPanel\Scripts\sync-backroom-wheel-assets.ps1

    The source tree is READ-ONLY here: files are only ever copied out of it, never written back. Each file
    prints size + md5 of the client copy before and after, so a PR body can record which snapshot shipped.
    The 3D room reads its own optimised copy (room\assets\wheel.glb, Scripts\build-backroom-room-assets.mjs);
    this script only feeds the close-up station.

    The node check runs tests\nodes-check.mjs with node (exit 1 when a REQUIRED node is missing, a warning
    for an optional one, the same split the page makes at load).

.PARAMETER Source
    blender-scripting wheel folder. Defaults to C:\Projects\blender-scripting\wheel.

.PARAMETER CheckOnly
    Skip the copy and only run the node check on the client's current wheel.glb.
#>
param(
    [string]$Source = 'C:\Projects\blender-scripting\wheel',
    [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'
$station = Join-Path $PSScriptRoot '..\Resources\web\backroom\stations\wheel' | Resolve-Path | Select-Object -ExpandProperty Path

# source (relative to $Source) -> destination (relative to stations\wheel)
$files = @(
    @{ From = 'out\wheel.glb';          To = 'assets\wheel.glb' },
    @{ From = 'out\emi-faces-slot.png'; To = 'assets\emi-faces-slot.png' }
)

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
        $dst = Join-Path $station $f.To
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

Write-Host ''
& node (Join-Path $station 'tests\nodes-check.mjs') (Join-Path $station 'assets\wheel.glb')
exit $LASTEXITCODE
