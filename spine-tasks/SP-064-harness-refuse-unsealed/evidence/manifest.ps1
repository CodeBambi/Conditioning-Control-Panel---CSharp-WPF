# SP-057 manifest: recursive snapshot of a directory -> JSON.
# shape: { root, exists, capturedUtc, files: [{ path (relative, /), length, sha256 }] }
# File SET + per-file content hash: the diff asserts equality BOTH directions
# (no adds, no deletes, no content drift) — consult (c) hole 1.
# Paths are sha256-hashed BY DEFAULT: a committed manifest of the real profile must
# never leak the owner's file names (user media, spiral names). -PlainPaths for
# sandbox roots under the task evidence dir (path readability aids review there).
param(
  [Parameter(Mandatory=$true)][string]$Root,
  [Parameter(Mandatory=$true)][string]$Out,
  [string]$DisplayRoot = "",
  [switch]$PlainPaths
)
$ErrorActionPreference = "Stop"
$files = @()
if (Test-Path $Root) {
  Get-ChildItem $Root -Recurse -File -Force | ForEach-Object {
    $rel = $_.FullName.Substring((Resolve-Path $Root).Path.Length).TrimStart('\','/') -replace '\\','/'
    if (-not $PlainPaths) {
      $rel = 'sha256:' + [System.BitConverter]::ToString(
        [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($rel))
      ).Replace('-','').ToLowerInvariant()
    }
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $files += [pscustomobject]@{ path = $rel; length = $_.Length; sha256 = $hash }
  }
}
[pscustomobject]@{
  root = ($DisplayRoot ? $DisplayRoot : $Root)
  exists = [bool](Test-Path $Root)
  capturedUtc = (Get-Date).ToUniversalTime().ToString("o")
  fileCount = $files.Count
  files = ($files | Sort-Object path)
} | ConvertTo-Json -Depth 4 | Out-File $Out -Encoding utf8
Write-Output "manifest: $Root -> $Out ($($files.Count) files)"
