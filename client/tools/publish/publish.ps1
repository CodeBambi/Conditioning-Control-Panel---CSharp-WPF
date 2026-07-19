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
dotnet publish $Csproj -c Release -r $Rid --self-contained true -p:PublishSingleFile=true -o $Out --nologo
if ($LASTEXITCODE -ne 0) { throw "FAIL: dotnet publish exit $LASTEXITCODE" }

Write-Host "PUBLISHED $Name"
Write-Host "ARTIFACT $Out"
