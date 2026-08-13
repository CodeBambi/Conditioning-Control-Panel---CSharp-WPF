# SP-057 manifest diff: byte-identity verdict, BOTH directions (consult c hole 1).
# Exit 0 = identical (same existence state, same file set, same length+sha256 per file).
param(
  [Parameter(Mandatory=$true)][string]$Pre,
  [Parameter(Mandatory=$true)][string]$Post
)
$ErrorActionPreference = "Stop"
$a = Get-Content $Pre -Raw | ConvertFrom-Json
$b = Get-Content $Post -Raw | ConvertFrom-Json
$fail = @()
if ($a.exists -ne $b.exists) { $fail += "existence changed: $($a.exists) -> $($b.exists)" }
$mapA = @{}; foreach ($f in $a.files) { $mapA[$f.path] = $f }
$mapB = @{}; foreach ($f in $b.files) { $mapB[$f.path] = $f }
foreach ($p in $mapA.Keys) {
  if (-not $mapB.ContainsKey($p)) { $fail += "DELETED: $p" }
  elseif ($mapA[$p].sha256 -ne $mapB[$p].sha256 -or $mapA[$p].length -ne $mapB[$p].length) { $fail += "CHANGED: $p" }
}
foreach ($p in $mapB.Keys) { if (-not $mapA.ContainsKey($p)) { $fail += "ADDED: $p" } }
if ($fail.Count -gt 0) {
  Write-Output "DIFF VERDICT: NOT IDENTICAL ($($fail.Count) deltas)"
  $fail | ForEach-Object { Write-Output "  $_" }
  exit 1
}
Write-Output "DIFF VERDICT: BYTE-IDENTICAL ($($a.fileCount) files, set-equal both directions, all hashes match)"
exit 0
