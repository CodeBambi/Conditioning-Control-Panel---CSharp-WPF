#Requires -Version 7
# SP-081 audit driver. Reproduces client/tools/port-loop.ps1 Invoke-ClaudeWithStdinPrompt
# (:251-296) with the scratch worktree substituted for $script:Repo. Untracked, lives
# outside the repository, never inside a tree under audit.
param(
    [Parameter(Mandatory)][string]$WorkDir,      # the scratch worktree = auditor cwd
    [Parameter(Mandatory)][string]$PromptFile,   # stdin, byte-exact file handle
    [Parameter(Mandatory)][string]$OutLog,       # ABSOLUTE, into the LANE worktree evidence dir
    [Parameter(Mandatory)][string]$ErrLog,       # ABSOLUTE, into the LANE worktree evidence dir
    [int]$TimeoutMinutes = 60,
    [string]$Label = 'audit'
)

$ErrorActionPreference = 'Stop'

$ClaudeExe   = 'C:\Users\Micha\.local\bin\claude.exe'
$AuditModel  = 'sonnet'
$PermissionMode = 'bypassPermissions'

# Get-AuditArgs, reproduced verbatim from port-loop.ps1:310-319.
$ClaudeArgs = [string[]]@(
    '-p',
    '--safe-mode',
    '--model', $AuditModel,
    '--permission-mode', $PermissionMode,
    '--no-session-persistence',
    '--tools', 'Bash,Read,Grep,Glob'
)

# port-loop.ps1:260-265 — Start-Process joins ArgumentList with spaces, so an argument
# containing whitespace or a quote would be silently re-split. Assert, do not hope.
foreach ($a in $ClaudeArgs) {
    if ($a -match '[\s"]') { throw "internal: argument [$a] contains whitespace or a quote" }
}

# The blindness precondition port-loop.ps1:371-373 enforces for the wrapper.
if ($env:CCP_DATA_ROOT) { throw "internal: CCP_DATA_ROOT is set [$($env:CCP_DATA_ROOT)] — refusing to launch" }

foreach ($p in @($WorkDir, $PromptFile)) {
    if (-not (Test-Path -LiteralPath $p)) { throw "internal: missing path $p" }
}
foreach ($p in @($OutLog, $ErrLog)) {
    if (-not [System.IO.Path]::IsPathRooted($p)) { throw "internal: log path is not absolute: $p" }
    $dir = Split-Path -Parent $p
    if (-not (Test-Path -LiteralPath $dir)) { throw "internal: log directory does not exist: $dir" }
    # C-1: a log written into the tree under audit would dirty it and fail the auditor's
    # own `git status --short` check with our harness's own bytes.
    if ($p.StartsWith($WorkDir, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "internal: log path $p is inside the tree under audit ($WorkDir)"
    }
}

Write-Host "[$Label] exe        $ClaudeExe"
Write-Host "[$Label] args       $($ClaudeArgs -join ' ')"
Write-Host "[$Label] cwd        $WorkDir"
Write-Host "[$Label] stdin      $PromptFile"
Write-Host "[$Label] stdout     $OutLog"
Write-Host "[$Label] stderr     $ErrLog"
Write-Host "[$Label] rendered   $ClaudeExe $($ClaudeArgs -join ' ') < $PromptFile"

$started = Get-Date
$proc = Start-Process -FilePath $ClaudeExe `
    -ArgumentList $ClaudeArgs `
    -WorkingDirectory $WorkDir `
    -NoNewWindow -PassThru `
    -RedirectStandardInput $PromptFile `
    -RedirectStandardOutput $OutLog `
    -RedirectStandardError $ErrLog

$deadline = $started.AddMinutes($TimeoutMinutes)
$timedOut = $false
while (-not $proc.WaitForExit(5000)) {
    $now = Get-Date
    if ($now -gt $deadline) {
        Write-Host "[$Label] exceeded ${TimeoutMinutes}m - killing the process tree"
        & taskkill.exe /F /T /PID $proc.Id *> $null
        $proc.WaitForExit(30000) | Out-Null
        $timedOut = $true
        break
    }
}

$minutes = [math]::Round(((Get-Date) - $started).TotalMinutes, 1)
$exit = if ($timedOut) { -1 } else { $proc.ExitCode }
Write-Host "[$Label] EXITCODE=$exit TIMEDOUT=$timedOut MINUTES=$minutes"
