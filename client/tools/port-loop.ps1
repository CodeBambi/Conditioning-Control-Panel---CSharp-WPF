# port-loop.ps1 — unattended orchestration loop for the greenfield port.
#
# One pi session per PHASE, never per wave: the shell owns the long waits, the model owns
# the judgment. Each `pi -p` invocation reconciles, does exactly ONE phase (land a finished
# batch, or author + launch the next wave), then exits — so context is fresh every time and
# no session sits blocked for hours while a batch runs.
#
#   pwsh -File client/tools/port-loop.ps1
#   pwsh -File client/tools/port-loop.ps1 -MaxIterations 6 -MaxHours 4 -DryRun
#
# Stop conditions (any one halts the loop):
#   - .spine/STOP exists            (the phase prompt writes it: pause protocol, or no claimable work)
#   - -MaxIterations reached        (default 24)
#   - -MaxHours elapsed             (default 12)
#   - two consecutive non-zero pi exits
#   - Ctrl-C
#
# Deliberately NOT done here: exporting CCP_DATA_ROOT loop-wide. That would make the SP-057
# pin skip and every suite run report 891/1 instead of 892/0, destroying the exact-count floor
# discipline SP-062 restored. Isolation comes from spine's worktree lanes; CCP_DATA_ROOT is set
# per headed-evidence run, by the packet that needs it (SP-057's rule).

[CmdletBinding()]
param(
    [int]$MaxIterations = 24,
    [int]$MaxHours = 12,
    [string]$Model = 'anthropic/claude-opus-5',
    [string]$Prompt = 'client/port.txt',
    [string]$AuditModel = 'kimi-coding/kimi-for-coding-highspeed',
    [switch]$NoAudit,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repo

$pi = Join-Path $env:APPDATA 'npm\pi.cmd'
$spineBin = Join-Path $env:USERPROFILE '.pi\agent\npm\node_modules\.bin'
$env:PATH = "$env:PATH;$spineBin"
$spine = Join-Path $spineBin 'spine.cmd'

foreach ($p in @($pi, $spine, (Join-Path $repo $Prompt))) {
    if (-not (Test-Path $p)) { throw "missing: $p" }
}

$logDir = Join-Path $repo '.spine\runtime\loop'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$stopFile = Join-Path $repo '.spine\STOP'
$started = Get-Date
$consecutiveFailures = 0

function Write-Loop($msg) {
    $line = "[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $msg
    Write-Host $line
    Add-Content -Path (Join-Path $logDir 'loop.log') -Value $line
}

Write-Loop "port-loop start — repo=$repo model=$Model maxIterations=$MaxIterations maxHours=$MaxHours dryRun=$DryRun"
if (Test-Path $stopFile) {
    Write-Loop "STOP file present at start: $(Get-Content $stopFile -Raw)"
    Write-Loop "remove .spine/STOP to resume"
    exit 0
}

for ($i = 1; $i -le $MaxIterations; $i++) {

    if (Test-Path $stopFile) { Write-Loop "STOP file written — halting. Reason: $(Get-Content $stopFile -Raw)"; break }
    if (((Get-Date) - $started).TotalHours -ge $MaxHours) { Write-Loop "wall-clock cap ${MaxHours}h reached — halting"; break }

    # 1. If a batch is already running, the SHELL waits — never a pi session (hours of context for nothing).
    $status = & $spine status --json 2>&1 | Out-String
    $macro = try { ($status | ConvertFrom-Json).macroPhase } catch { 'unknown' }
    Write-Loop "iteration $i — spine macroPhase=$macro"

    if ($macro -eq 'executing' -or $macro -eq 'planning') {
        Write-Loop "batch in flight — shell waits (no model resident)"
        if (-not $DryRun) {
            & $spine wait --until completed,needs_integrate,failed,aborted,needs_retry --timeout 4h 2>&1 |
                Tee-Object -FilePath (Join-Path $logDir ("{0:000}-wait.log" -f $i)) | Out-Null
        }
        continue   # next iteration lands it with a fresh session
    }

    # 2. Otherwise run ONE phase in a fresh pi session.
    $log = Join-Path $logDir ("{0:000}-phase.log" -f $i)
    Write-Loop "running phase in a fresh session -> $log"
    if ($DryRun) { Write-Loop "DRYRUN: $pi -p --model $Model @$Prompt"; continue }

    $headBefore = (& git rev-parse HEAD).Trim()
    & $pi -p --model $Model -xt ask_user "@$Prompt" *>&1 | Tee-Object -FilePath $log | Out-Null
    $code = $LASTEXITCODE
    $headAfter = (& git rev-parse HEAD).Trim()
    Write-Loop "phase exit=$code head $($headBefore.Substring(0,8)) -> $($headAfter.Substring(0,8))"

    # 3. BLIND AUDIT — only when the phase moved HEAD.
    #
    # A fresh extension-less, read-only pi process re-derives the floor from the pushed tree.
    # It cannot see the phase's conversation, has no spine/consult/MCP tools, and cannot write
    # anything: it can only read the repo and run commands. That is the whole point — every
    # failure this project has actually shipped (stale gate evidence, a red base, a vacuously
    # green pin) was self-certified by the same context that produced it. Design borrowed from
    # pi-goal-list-loop-audit's detached auditor; the package itself is NOT installed (it drives
    # its own goal queue and would compete with spine and the task board).
    if ($code -eq 0 -and $headAfter -ne $headBefore -and -not $NoAudit) {
        $auditLog = Join-Path $logDir ("{0:000}-audit.log" -f $i)
        Write-Loop "blind audit of $($headAfter.Substring(0,8)) -> $auditLog"

        $auditPrompt = @'
You are a BLIND AUDITOR. You did not do this work and cannot see the session that did. Judge
only what the repository proves right now. Do not fix anything, do not commit, do not write files.

In C:/Code/Conditioning-Control-Panel---CSharp-WPF on branch feat/crossplatform:

1. Read the NEWEST entry of client/docs/port-digest.md and the newest wave section of
   spine-tasks/CONTEXT.md. Note the exact unit/headless test counts they claim.
2. Run, and read the real output:
     dotnet build client/CcpClient.sln -c Debug --nologo
     dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo
     dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo
3. Check: build 0 warnings 0 errors; the counts EXACTLY match the claim; SKIPPED is 0 (a skip is
   a failure here even though the exit code is 0 - it is the vacuous-green class); `git status
   --short` is empty; HEAD equals origin/feat/crossplatform (run `git fetch origin` first).

FAIL if any check fails, if a claimed count and the observed count differ in either direction,
or if you cannot verify a claim. Passing tests do not excuse a mismatched number.

Your LAST line must be exactly one of:
VERDICT: PASS
VERDICT: FAIL - <one line naming the check, the claimed value and the observed value>
'@

        & $pi -p -ne -ns -nc -np --no-session -t read,bash,grep,find,ls --model $AuditModel $auditPrompt *>&1 |
            Tee-Object -FilePath $auditLog | Out-Null

        $verdict = (Select-String -Path $auditLog -Pattern '^VERDICT:' | Select-Object -Last 1).Line
        if (-not $verdict) { $verdict = 'VERDICT: FAIL - auditor produced no verdict line' }
        Write-Loop "audit -> $verdict"

        if ($verdict -notmatch '^VERDICT:\s*PASS') {
            Set-Content -Path $stopFile -Value "blind audit failed at $headAfter`n$verdict`nlog: $auditLog"
            Write-Loop "halting for an operator - the tree does not support its own claims"
            break
        }
    }

    if ($code -ne 0) {
        $consecutiveFailures++
        if ($consecutiveFailures -ge 2) {
            Write-Loop "two consecutive non-zero exits — halting for an operator"
            Set-Content -Path $stopFile -Value "port-loop halted: two consecutive non-zero pi exits (last log: $log)"
            break
        }
        Write-Loop "retrying once with a fresh session"
    }
    else { $consecutiveFailures = 0 }
}

Write-Loop "port-loop end — iterations run, elapsed $([int]((Get-Date) - $started).TotalMinutes)m"
Write-Loop "digest: client/docs/port-digest.md · logs: .spine/runtime/loop/"
