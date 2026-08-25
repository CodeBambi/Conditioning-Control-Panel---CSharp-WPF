# CCP greenfield publish — Windows (release-publish-gates.md §1).
# Self-contained single-file per RID. Artifact naming DERIVES from the one version
# authority (client/Directory.Build.props) via `dotnet msbuild -getProperty:Version` —
# never a hardcoded or reparsed version string. Usage:
#   pwsh client/tools/publish/publish.ps1 [-Rid win-x64]
param([string]$Rid = "win-x64")
$ErrorActionPreference = "Stop"

$Root = Resolve-Path "$PSScriptRoot/../.."
$Csproj = Join-Path $Root "src/CcpClient.Desktop/CcpClient.Desktop.csproj"

$Version = (dotnet msbuild $Csproj -nologo -getProperty:Version).Trim()
if ([string]::IsNullOrWhiteSpace($Version)) { throw "FAIL: Version property empty — the one authority (client/Directory.Build.props) is broken" }

$Name = "CcpClient.Desktop-$Version-$Rid"
$Out = Join-Path $Root "artifacts/publish/$Name"
# Always publish to a CLEAN output dir: an incremental `dotnet publish` into an existing
# single-file output dir silently DROPS the native sidecars (libSkiaSharp/libHarfBuzzSharp/
# av_libglesv2) and the app then dies with BadImageFormatException 0x8007000B.
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
dotnet publish $Csproj -c Release -r $Rid --self-contained true -p:PublishSingleFile=true -o $Out --nologo
if ($LASTEXITCODE -ne 0) { throw "FAIL: dotnet publish exit $LASTEXITCODE" }

# REDISTRIBUTION OBLIGATIONS, checked against the real output directory rather than trusted.
# Both properties are carried by project-file wiring, and a build property that silently stops
# doing anything is the failure mode this whole area has: it leaves a green build and an artifact
# that is out of compliance. So the script that produces the artifact is where they are asserted.
#   THIRD-PARTY-NOTICES.md — Apache-2.0 §4 / LGPL-2.1 §1: the notices must accompany the
#   distribution; a file that stays in the repository discharges nothing.
#   LibVLCSharp.dll as a SIDECAR — LGPL-2.1 §6: single-file would fuse the LGPL assembly into the
#   apphost, and a user cannot substitute their own build of a fused assembly.
foreach ($required in @("THIRD-PARTY-NOTICES.md", "LibVLCSharp.dll")) {
    if (-not (Test-Path (Join-Path $Out $required))) {
        throw "FAIL: $required is missing from $Out — the artifact does not carry what its licences oblige (see client/THIRD-PARTY-NOTICES.md §4, §6)"
    }
}

Write-Host "PUBLISHED $Name"
Write-Host "ARTIFACT $Out"
