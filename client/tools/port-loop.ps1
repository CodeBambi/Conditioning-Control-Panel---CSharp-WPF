# port-loop.ps1 - unattended Claude Code phase scheduler for the greenfield Avalonia port.
#
# REWRITTEN 2026-08-14. The previous version drove the retired pi-spine engine (`pi -p`,
# `spine status --json`, `.spine/STOP`). It was disabled in place rather than deleted because
# `pi` is still installed on this machine, so it would NOT have failed safe: it would have
# started and handed a Claude-Code-shaped prompt to a Pi session. Its structure and its stop
# conditions are preserved here; only the engine underneath them changed.
#
# ===========================================================================================
# SAFETY DEFAULT: THIS SCRIPT DOES NOTHING UNLESS YOU PASS -Execute.
#
#   Without -Execute it runs in DRY RUN. Dry run performs every preflight check, prints the
#   exact argument vectors it would hand to `claude`, and writes NOTHING: no directories, no
#   lock file, no log file, no STOP file. It never starts a model.
#   -Execute and -DryRun together is an error, so "add -Execute" is always a deliberate act.
# ===========================================================================================
#
#   pwsh -File client/tools/port-loop.ps1                          # dry run, the default
#   pwsh -File client/tools/port-loop.ps1 -Execute
#   pwsh -File client/tools/port-loop.ps1 -Execute -MaxIterations 6 -MaxHours 4
#
# ONE `claude` INVOCATION PER PHASE, NEVER PER WAVE. The shell owns the waiting; the model owns
# the judgment. Each invocation reconciles, does exactly ONE phase (land a finished wave, or
# author + launch the next one), then exits - so context is fresh every time and no session
# sits resident for hours. Under Claude Code the lanes are in-session subagents, so the phase
# itself owns the wave wait; there is no detached engine for the shell to poll any more, and
# the `spine status` / `spine wait` coordination the Pi version needed is simply gone.
#
# Stop conditions (any one halts the loop):
#   - .port/STOP exists                  (the phase prompt writes it: pause protocol, or no
#                                         claimable work). NOTE .port/, not the old .spine/.
#   - -MaxIterations reached             (default 24)
#   - -MaxHours elapsed                  (default 12)
#   - two consecutive non-zero `claude` exits
#   - a blind audit that is not PASS, or that produced no verdict at all
#   - a HEAD-moving landing phase that did not append to client/docs/port-digest.md
#   - a phase that blew -PhaseTimeoutMinutes (killed, then halted for an operator)
#   - -MaxNoProgress consecutive phases that exited 0 without moving HEAD (this is what a
#     stale .port/WAVE-LOCK looks like from outside: every phase reads case C and exits at
#     once. Without this the loop would spin, cheaply and forever, doing nothing.)
#   - Ctrl-C
#
# BLIND AUDIT after every HEAD-moving phase, FAIL CLOSED. A fresh `claude --safe-mode` process
# with no skills, no CLAUDE.md, no memory, no MCP and no knowledge of the session that did the
# work re-derives the floor from the pushed tree and compares it with the claims in
# client/docs/port-digest.md and spine-tasks/CONTEXT.md. A count mismatch in either direction,
# an unexpected skip, a dirty or unpushed tree, or a missing verdict all halt the loop. Every
# failure this project has actually shipped (stale gate evidence, a red base, a vacuously green
# pin) was self-certified by the same context that produced it.
#   - Unlike the Pi version, the audit runs after ANY HEAD movement, including one made by a
#     phase that then exited non-zero. A half-landed tree is exactly the state worth auditing.
#   - --safe-mode, not --bare: bare mode does not read subscription credentials.
#   - The auditor model MUST differ from the phase model and from the fallback model, so weight
#     diversity survives now that cross-vendor diversity does not. Enforced at preflight.
#
# THE AUDITOR PROMPT IS A FILE ON STDIN, and so is the phase prompt. A multi-line argument does
# not survive a shell shim: the first Pi implementation inlined it, the auditor received
# nothing, asked what to audit, produced no verdict, and the fail-closed default halted the run
# on a false FAIL (2026-08-13). Claude Code has no `@file` prompt syntax, so stdin is the
# required form. Both prompts are delivered by Start-Process -RedirectStandardInput, which
# hands the child a byte-exact file handle: no shim, no encoding rewrite, no argument joining.
#
# NEVER export CCP_DATA_ROOT loop-wide. It makes the SP-057 pin skip, the suite reports a
# vacuous green, and the exact-count floor goes blind. This script does not set it and REFUSES
# TO START if it is already set in the environment. Isolation comes from worktree lanes;
# CCP_DATA_ROOT is set per headed-evidence run, by the packet that needs it.
#
# ONE ORCHESTRATOR AT A TIME. The loop writes .port/LOOP-LOCK (pid, process start time, host)
# and removes it on exit. It refuses to start while a live lock exists. While the loop runs, an
# interactive session must not author, launch or land anything.
#
# MSBUILDDISABLENODEREUSE=1 is exported for child processes so parallel worktrees do not
# accumulate msbuild nodes holding file locks (port-workflow.md, Machine limits).
#
# -------------------------------------------------------------------------------------------
# CLI FLAGS: DISCOVERED, NOT ASSUMED (claude 2.1.232, `claude --help` on this machine).
#   -p, --print                  non-interactive; prints the final message and exits
#   --model <alias|name>         'opus' resolves to claude-opus-5, 'sonnet' to claude-sonnet-5,
#                                'fable' to claude-fable-5 (observed in the request body)
#   --fallback-model <list>      only works with --print; used so a saturated model degrades
#                                the phase instead of stopping the run (client/port.txt)
#   --permission-mode <mode>     acceptEdits|auto|bypassPermissions|manual|dontAsk|plan
#   --safe-mode                  disables CLAUDE.md, skills, plugins, hooks, MCP, custom agents
#                                and commands; auth, model selection and permissions still work
#   --no-session-persistence     only works with --print; the auditor leaves no resumable trace
#   --tools <list>               restricts the built-in tool set; the auditor gets exactly
#                                Bash,Read,Grep,Glob (no Write, no Edit, no Task)
#   --max-budget-usd <amount>    only works with --print; optional cap, off by default
#   -n, --name <name>            display name, so a phase is findable in the /resume picker
# There is no --worktree flag in play here: lanes are subagents the PHASE launches, not
# processes this script launches. `claude --help` does list -w/--worktree, but that is for an
# interactive session, not for lane fan-out.
#
# NAMED LIMITS, recorded rather than glossed:
#   1. .port/ IS NOT GITIGNORED in this repository (`git check-ignore .port/STOP` exits 1,
#      while the old `.spine/STOP` exits 0). The phase prompt writes .port/WAVE-LOCK and
#      .port/handoff.md, and this loop adds .port/LOOP-LOCK and .port/runtime/loop/*, so the
#      very first phase would leave the tree dirty and EVERY blind audit would then fail on
#      "git status --short is empty" - a false FAIL, the exact class that halted a Pi run.
#      Preflight refuses to start until .gitignore ignores it. Fixing .gitignore was out of
#      scope for the commit that wrote this file.
#   2. The auditor is prompt-constrained, not sandbox-constrained. --tools excludes Write and
#      Edit, but Bash can still write. This is the same posture the Pi auditor had.
#   3. Neither a real phase nor a real audit has been run end to end against this script. The
#      argument vectors, the stdin delivery, the model aliases, the fallback path and the
#      --safe-mode isolation were all verified against a local capture endpoint; the model
#      calls themselves were not, because the machine that wrote this could not authenticate a
#      child `claude`. Run once with -Execute -MaxIterations 1 and read the logs before
#      trusting it with a night.

[CmdletBinding()]
param(
    # Real execution. Without this, the script is a dry run and writes nothing.
    [switch]$Execute,
    # Explicit dry run. Default behaviour; accepted so the intent can be stated out loud.
    [switch]$DryRun,

    [int]$MaxIterations = 24,
    [int]$MaxHours = 12,
    # A phase that outlives this is killed and the loop halts. 0 disables the cap, and then a
    # single wedged phase can outlive -MaxHours, which is only checked between iterations.
    [int]$PhaseTimeoutMinutes = 360,
    [int]$AuditTimeoutMinutes = 60,
    # Consecutive zero-exit phases that did not move HEAD before the loop calls it a stall.
    [int]$MaxNoProgress = 3,

    [ValidateNotNullOrEmpty()][string]$Model = 'opus',
    [string]$FallbackModel = 'fable',
    [ValidateNotNullOrEmpty()][string]$AuditModel = 'sonnet',

    [string]$Prompt = 'client/port.txt',
    [string]$AuditPrompt = 'client/tools/port-audit-prompt.md',

    [ValidateSet('acceptEdits', 'auto', 'bypassPermissions', 'manual', 'dontAsk', 'plan')]
    [string]$PermissionMode = 'bypassPermissions',

    # 0 leaves --max-budget-usd off entirely.
    [double]$MaxBudgetUsd = 0,

    # Disables the fail-closed blind audit. Debugging only; say so in the record if you use it.
    [switch]$NoAudit,
    # Take over a lock whose owning process is gone.
    [switch]$TakeStaleLock
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
# Deterministic native-command behaviour: git check-ignore and friends signal through exit
# codes, and must not throw just because ErrorActionPreference is Stop.
$PSNativeCommandUseErrorActionPreference = $false

$script:Repo = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
Set-Location $script:Repo

$script:PortDir = Join-Path $script:Repo '.port'
$script:LogDir = Join-Path $script:PortDir 'runtime\loop'
$script:StopFile = Join-Path $script:PortDir 'STOP'
$script:WaveLock = Join-Path $script:PortDir 'WAVE-LOCK'
$script:LoopLock = Join-Path $script:PortDir 'LOOP-LOCK'
$script:DigestPath = Join-Path $script:Repo 'client\docs\port-digest.md'
$script:ContextPath = Join-Path $script:Repo 'spine-tasks\CONTEXT.md'
$script:PromptPath = Join-Path $script:Repo $Prompt
$script:AuditPromptPath = Join-Path $script:Repo $AuditPrompt
$script:ClaudeExe = ''
$script:LiveRun = $false
$script:HaltedByLoop = $false

# ---------------------------------------------------------------------------- mode resolution
if ($Execute -and $DryRun) {
    Write-Host 'REFUSING: -Execute and -DryRun are mutually exclusive. Pick one.' -ForegroundColor Red
    exit 2
}
$script:LiveRun = [bool]$Execute

# ---------------------------------------------------------------------------------- utilities
function Write-Loop {
    param([string]$Message, [string]$Colour = '')
    $line = '[{0}] {1}' -f (Get-Date -Format 'HH:mm:ss'), $Message
    if ($Colour) { Write-Host $line -ForegroundColor $Colour } else { Write-Host $line }
    if ($script:LiveRun -and (Test-Path $script:LogDir)) {
        Add-Content -Path (Join-Path $script:LogDir 'loop.log') -Value $line -Encoding utf8
    }
}

function Get-Head {
    $sha = (& git rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or -not $sha) { return '' }
    return $sha.Trim()
}

function Get-ShortSha([string]$Sha) {
    if ($Sha.Length -ge 8) { return $Sha.Substring(0, 8) } else { return $Sha }
}

function Get-DigestState {
    if (-not (Test-Path $script:DigestPath)) {
        return [pscustomobject]@{ Exists = $false; Hash = ''; Lines = @() }
    }
    return [pscustomobject]@{
        Exists = $true
        Hash   = (Get-FileHash -Path $script:DigestPath -Algorithm SHA256).Hash
        Lines  = @(Get-Content -Path $script:DigestPath)
    }
}

# A landing phase must touch the digest. An authoring phase legitimately does not: it commits
# packets and tracker state and then launches lanes. Classify mechanically by what the commit
# range TOUCHED rather than by commit-message discipline, and treat anything ambiguous as a
# landing so the digest requirement is the default, not the exception.
function Test-AuthoringOnlyRange {
    param([string]$Before, [string]$After)
    if (-not $Before -or -not $After) { return $false }
    $merges = @(& git rev-list --merges "$Before..$After" 2>$null)
    if ($LASTEXITCODE -ne 0) { return $false }
    if ($merges.Count -gt 0) { return $false }
    $paths = @(& git diff --name-only $Before $After 2>$null)
    if ($LASTEXITCODE -ne 0) { return $false }
    if ($paths.Count -eq 0) { return $false }
    foreach ($p in $paths) {
        $ok = $p -like 'spine-tasks/*' -or
              $p -like '.port/*' -or
              $p -like 'client/memories/*' -or
              $p -eq 'client/docs/task-board.md'
        if (-not $ok) { return $false }
    }
    return $true
}

function Test-LockAlive([string]$Path) {
    if (-not (Test-Path $Path)) { return $null }
    try { $lock = Get-Content -Path $Path -Raw | ConvertFrom-Json } catch { return $null }
    $alive = $false
    try {
        $p = Get-Process -Id $lock.pid -ErrorAction Stop
        # Guard against PID reuse: the recorded start time must match too.
        $alive = ([math]::Abs(($p.StartTime - [datetime]$lock.startedUtc).TotalSeconds) -lt 2)
    } catch { $alive = $false }
    return [pscustomobject]@{ Lock = $lock; Alive = $alive }
}

function Stop-ForOperator([string]$Reason) {
    Write-Loop "HALT: $Reason" 'Red'
    if ($script:LiveRun) {
        Set-Content -Path $script:StopFile -Value "port-loop halted $(Get-Date -Format 'u')`n$Reason" -Encoding utf8
        Write-Loop "wrote .port/STOP - remove it to resume" 'Red'
    }
    $script:HaltedByLoop = $true
}

# Every `claude` invocation goes through here. The prompt is a FILE on the child's stdin.
function Invoke-ClaudeWithStdinPrompt {
    param(
        [Parameter(Mandatory)][string[]]$ClaudeArgs,
        [Parameter(Mandatory)][string]$PromptFile,
        [Parameter(Mandatory)][string]$OutLog,
        [Parameter(Mandatory)][string]$ErrLog,
        [int]$TimeoutMinutes = 0,
        [string]$Label = 'claude'
    )
    # Start-Process joins ArgumentList with spaces, so an argument containing whitespace or a
    # quote would be silently re-split. Every vector this script builds is space-free; assert
    # it rather than hope, because the failure would look like a model error, not a shell one.
    foreach ($a in $ClaudeArgs) {
        if ($a -match '[\s"]') { throw "internal: argument [$a] contains whitespace or a quote" }
    }

    $started = Get-Date
    $proc = Start-Process -FilePath $script:ClaudeExe `
        -ArgumentList $ClaudeArgs `
        -WorkingDirectory $script:Repo `
        -NoNewWindow -PassThru `
        -RedirectStandardInput $PromptFile `
        -RedirectStandardOutput $OutLog `
        -RedirectStandardError $ErrLog

    $deadline = if ($TimeoutMinutes -gt 0) { $started.AddMinutes($TimeoutMinutes) } else { [datetime]::MaxValue }
    $lastBeat = $started
    while (-not $proc.WaitForExit(5000)) {
        $now = Get-Date
        if ($now -gt $deadline) {
            Write-Loop "$Label exceeded ${TimeoutMinutes}m - killing the process tree" 'Red'
            & taskkill.exe /F /T /PID $proc.Id *> $null
            $proc.WaitForExit(30000) | Out-Null
            return [pscustomobject]@{ ExitCode = -1; TimedOut = $true; Minutes = [int]($now - $started).TotalMinutes }
        }
        if (($now - $lastBeat).TotalMinutes -ge 10) {
            $lastBeat = $now
            Write-Loop "$Label still running (elapsed $([int]($now - $started).TotalMinutes)m)"
        }
    }
    return [pscustomobject]@{
        ExitCode = $proc.ExitCode
        TimedOut = $false
        Minutes  = [int]((Get-Date) - $started).TotalMinutes
    }
}

function Get-PhaseArgs([int]$Iteration) {
    $a = [System.Collections.Generic.List[string]]::new()
    $a.AddRange([string[]]@('-p', '--model', $Model))
    if ($FallbackModel) { $a.AddRange([string[]]@('--fallback-model', $FallbackModel)) }
    $a.AddRange([string[]]@('--permission-mode', $PermissionMode))
    if ($MaxBudgetUsd -gt 0) { $a.AddRange([string[]]@('--max-budget-usd', ([string]$MaxBudgetUsd))) }
    $a.AddRange([string[]]@('--name', ('port-phase-{0:000}' -f $Iteration)))
    return $a.ToArray()
}

# The auditor: fresh process, no customizations, no persisted session, read-shaped tool set,
# and a model that differs from the phase model by preflight assertion.
function Get-AuditArgs {
    return [string[]]@(
        '-p',
        '--safe-mode',
        '--model', $AuditModel,
        '--permission-mode', $PermissionMode,
        '--no-session-persistence',
        '--tools', 'Bash,Read,Grep,Glob'
    )
}

# Rendered for the operator's eyes. `< file` is literal here: delivery is Start-Process
# -RedirectStandardInput, a byte-exact file handle, not a shell pipe and not an argument.
function Format-Command([string]$Exe, [string[]]$Arguments, [string]$StdinFile) {
    return ('{0} {1} < {2}' -f $Exe, ($Arguments -join ' '), $StdinFile)
}

# ----------------------------------------------------------------------------------- preflight
$preflight = [System.Collections.Generic.List[string]]::new()

$claudeCmd = Get-Command claude -ErrorAction SilentlyContinue
if (-not $claudeCmd) {
    $preflight.Add('`claude` is not on PATH. Claude Code is the engine; there is no fallback.')
} else {
    $script:ClaudeExe = $claudeCmd.Source
}

foreach ($needed in @($script:PromptPath, $script:AuditPromptPath, $script:DigestPath, $script:ContextPath)) {
    if (-not (Test-Path $needed)) { $preflight.Add("missing required file: $needed") }
}

if (-not $NoAudit) {
    foreach ($tool in @('node', 'dotnet')) {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
            $preflight.Add("``$tool`` is not on PATH; the blind audit runs the build and the floor wrapper and would fail closed on a false FAIL.")
        }
    }
}

# The prompt must be the Claude Code one. Detect by content, not by date, so this stays correct
# if either side is edited. This is the mirror image of the guard the Pi version ended with.
if (Test-Path $script:PromptPath) {
    $promptText = Get-Content -Path $script:PromptPath -Raw
    if ($promptText -notmatch '\.claude/agents/|port-slice-executor|WAVE-LOCK') {
        $preflight.Add("$Prompt does not look like the Claude Code phase prompt (no .claude/agents/, no port-slice-executor, no WAVE-LOCK). Refusing to hand an unknown prompt to an unattended run.")
    }
}

# Model diversity. Context blindness plus a different weight set is all the independence the
# blind audit has left; if the auditor is the phase model, the audit is self-certification.
function Get-ModelKey([string]$m) { return ($m -replace '^.*/', '').Trim().ToLowerInvariant() }
if (-not $NoAudit) {
    if ((Get-ModelKey $AuditModel) -eq (Get-ModelKey $Model)) {
        $preflight.Add("audit model '$AuditModel' equals the phase model '$Model'; the blind audit would be self-certification.")
    }
    if ($FallbackModel -and (Get-ModelKey $AuditModel) -eq (Get-ModelKey $FallbackModel)) {
        $preflight.Add("audit model '$AuditModel' equals the fallback model '$FallbackModel'; a saturated phase would silently land on the auditor's weights.")
    }
}

# CCP_DATA_ROOT loop-wide makes the SP-057 pin skip and the floor reports a vacuous green.
if ($env:CCP_DATA_ROOT) {
    $preflight.Add("CCP_DATA_ROOT is set in this environment ('$($env:CCP_DATA_ROOT)'). It must never be process-wide: the SP-057 pin skips and the exact-count floor goes blind. Clear it and start again.")
}

# git sanity
& git rev-parse --is-inside-work-tree *> $null
if ($LASTEXITCODE -ne 0) {
    $preflight.Add("$($script:Repo) is not a git work tree.")
} else {
    $branch = (& git rev-parse --abbrev-ref HEAD).Trim()
    if ($branch -ne 'feat/crossplatform') {
        Write-Loop "NOTE: on branch '$branch', not feat/crossplatform. The phase prompt's bootstrap checks this out itself." 'Yellow'
    }
    # See NAMED LIMIT 1 in the header. Without this the first phase dirties the tree and every
    # blind audit fails on "git status --short is empty".
    & git check-ignore -q '.port/STOP'
    if ($LASTEXITCODE -ne 0) {
        $preflight.Add(".port/ is not gitignored, so loop and phase state would dirty the tree and every blind audit would fail on 'git status --short is empty'. Add these two lines to .gitignore (mirroring the .spine/ entries already there):`n           .port/runtime/`n           .port/")
    }
}

# One orchestrator at a time.
$existing = Test-LockAlive $script:LoopLock
if ($existing -and $existing.Alive) {
    Write-Host ''
    Write-Host 'REFUSING TO START: another port-loop owns this repository.' -ForegroundColor Red
    Write-Host ("  pid {0} on {1}, started {2}" -f $existing.Lock.pid, $existing.Lock.host, $existing.Lock.startedUtc)
    Write-Host '  One orchestrator at a time. Stop it, or wait for it.'
    Write-Host ''
    exit 3
}
if ($existing -and -not $existing.Alive -and -not $TakeStaleLock -and $script:LiveRun) {
    Write-Host ''
    Write-Host 'REFUSING TO START: a STALE .port/LOOP-LOCK exists (its process is gone).' -ForegroundColor Red
    Write-Host ("  pid {0} on {1}, started {2}" -f $existing.Lock.pid, $existing.Lock.host, $existing.Lock.startedUtc)
    Write-Host '  A crashed loop may have left work half-landed. Reconcile first, then pass -TakeStaleLock.'
    Write-Host ''
    exit 3
}

# -------------------------------------------------------------------------------------- dry run
if (-not $script:LiveRun) {
    $phaseArgs = Get-PhaseArgs 1
    $auditArgs = Get-AuditArgs
    $exe = if ($script:ClaudeExe) { $script:ClaudeExe } else { 'claude' }

    Write-Host ''
    Write-Host 'port-loop DRY RUN - nothing was written and no model was started.' -ForegroundColor Cyan
    Write-Host '  Pass -Execute to run for real.'
    Write-Host ''
    Write-Host "  repo             $($script:Repo)"
    Write-Host "  claude           $exe"
    Write-Host "  phase model      $Model (fallback: $(if ($FallbackModel) { $FallbackModel } else { 'none' }))"
    Write-Host "  audit model      $(if ($NoAudit) { 'DISABLED (-NoAudit)' } else { $AuditModel })"
    Write-Host "  caps             $MaxIterations iterations / ${MaxHours}h wall clock / ${PhaseTimeoutMinutes}m per phase / ${AuditTimeoutMinutes}m per audit"
    Write-Host "  stall cap        $MaxNoProgress consecutive zero-exit phases that do not move HEAD"
    Write-Host "  budget           $(if ($MaxBudgetUsd -gt 0) { "--max-budget-usd $MaxBudgetUsd" } else { 'unset' })"
    Write-Host ''
    Write-Host '  would create     .port/, .port/runtime/loop/, .port/LOOP-LOCK'
    Write-Host "  watches          $($script:StopFile)"
    Write-Host "  wave lock        $($script:WaveLock) $(if (Test-Path $script:WaveLock) { '(PRESENT - a phase would read case C and exit at once)' } else { '(absent)' })"
    Write-Host "  digest           $($script:DigestPath)"
    Write-Host ''
    Write-Host '  PHASE (one per iteration, prompt on stdin):' -ForegroundColor Cyan
    Write-Host ("    " + (Format-Command $exe $phaseArgs $script:PromptPath))
    Write-Host ''
    if ($NoAudit) {
        Write-Host '  AUDIT: DISABLED by -NoAudit. The fail-closed gate is OFF; do not run a night like this.' -ForegroundColor Yellow
    } else {
        Write-Host '  BLIND AUDIT (after any HEAD-moving phase, prompt on stdin):' -ForegroundColor Cyan
        Write-Host ("    " + (Format-Command $exe $auditArgs $script:AuditPromptPath))
    }
    Write-Host ''
    Write-Host '  child env        MSBUILDDISABLENODEREUSE=1'
    Write-Host "  CCP_DATA_ROOT    $(if ($env:CCP_DATA_ROOT) { 'SET - preflight refuses' } else { 'not set (correct; never set it loop-wide)' })"
    Write-Host ''

    if ($preflight.Count -gt 0) {
        Write-Host '  PREFLIGHT FAILURES - -Execute would exit 4:' -ForegroundColor Red
        foreach ($p in $preflight) { Write-Host "    - $p" -ForegroundColor Red }
        Write-Host ''
        exit 4
    }
    Write-Host '  preflight: all checks pass.' -ForegroundColor Green
    Write-Host ''
    exit 0
}

# --------------------------------------------------------------------------------- live preflight
if ($preflight.Count -gt 0) {
    Write-Host ''
    Write-Host 'REFUSING TO START: preflight failed.' -ForegroundColor Red
    foreach ($p in $preflight) { Write-Host "  - $p" -ForegroundColor Red }
    Write-Host ''
    exit 4
}

# ------------------------------------------------------------------------------------- live run
New-Item -ItemType Directory -Force -Path $script:LogDir | Out-Null

$selfStart = (Get-Process -Id $PID).StartTime
Set-Content -Path $script:LoopLock -Encoding utf8 -Value (
    [pscustomobject]@{
        pid        = $PID
        host       = [System.Net.Dns]::GetHostName()
        startedUtc = $selfStart.ToString('o')
        model      = $Model
        auditModel = $(if ($NoAudit) { 'disabled' } else { $AuditModel })
        note       = 'one orchestrator at a time: an interactive session must not author, launch or land while this exists'
    } | ConvertTo-Json -Compress
)

# Inherited by every child: eight worktrees must not accumulate msbuild nodes holding locks.
$env:MSBUILDDISABLENODEREUSE = '1'

$started = Get-Date
$consecutiveFailures = 0
$noProgress = 0
$exitCode = 0

try {
    Write-Loop "port-loop start - repo=$($script:Repo) model=$Model audit=$(if ($NoAudit) { 'off' } else { $AuditModel }) maxIterations=$MaxIterations maxHours=$MaxHours"

    if (Test-Path $script:StopFile) {
        Write-Loop "STOP file present at start: $(Get-Content $script:StopFile -Raw)"
        Write-Loop 'remove .port/STOP to resume'
        exit 0
    }
    if (Test-Path $script:WaveLock) {
        Write-Loop 'NOTE: .port/WAVE-LOCK exists. A phase will read case C and exit at once; the stall cap will halt the loop if its owner is gone.' 'Yellow'
    }

    for ($i = 1; $i -le $MaxIterations; $i++) {

        if (Test-Path $script:StopFile) {
            Write-Loop "STOP file written - halting. Reason: $(Get-Content $script:StopFile -Raw)"
            break
        }
        if (((Get-Date) - $started).TotalHours -ge $MaxHours) {
            Write-Loop "wall-clock cap ${MaxHours}h reached - halting"
            break
        }

        $phaseOut = Join-Path $script:LogDir ('{0:000}-phase.log' -f $i)
        $phaseErr = Join-Path $script:LogDir ('{0:000}-phase.err.log' -f $i)
        $headBefore = Get-Head
        $digestBefore = Get-DigestState

        Write-Loop "iteration $i - one phase, fresh context -> $phaseOut"
        $phase = Invoke-ClaudeWithStdinPrompt -ClaudeArgs (Get-PhaseArgs $i) `
            -PromptFile $script:PromptPath -OutLog $phaseOut -ErrLog $phaseErr `
            -TimeoutMinutes $PhaseTimeoutMinutes -Label "phase $i"

        $headAfter = Get-Head
        $moved = ($headAfter -ne $headBefore)
        Write-Loop ("phase exit={0} in {1}m, head {2} -> {3}{4}" -f
            $phase.ExitCode, $phase.Minutes, (Get-ShortSha $headBefore), (Get-ShortSha $headAfter),
            $(if ($moved) { ' (MOVED)' } else { '' }))

        if ($phase.TimedOut) {
            # The kill may have landed mid-merge or mid-push. Do not try to recover here.
            Stop-ForOperator "phase $i exceeded ${PhaseTimeoutMinutes}m and was killed; the tree may be mid-operation. Reconcile by hand. Log: $phaseOut"
            $exitCode = 5
            break
        }

        if ($moved) {
            $noProgress = 0

            # Owner digest. The loop never writes it; it verifies the phase did.
            $digestAfter = Get-DigestState
            $digestTouched = ($digestAfter.Hash -ne $digestBefore.Hash)
            if (-not $digestTouched) {
                if (Test-AuthoringOnlyRange $headBefore $headAfter) {
                    Write-Loop 'digest untouched, and the commit range is authoring-only (packets/tracker/memories, no merge). Allowed.'
                } else {
                    Stop-ForOperator "a HEAD-moving landing phase left client/docs/port-digest.md untouched. Every landing phase appends three lines (landed / does not prove / owner question); unattended running must not bury named limits. Range $(Get-ShortSha $headBefore)..$(Get-ShortSha $headAfter), log: $phaseOut"
                    $exitCode = 5
                    break
                }
            } else {
                $added = @()
                if ($digestAfter.Lines.Count -gt $digestBefore.Lines.Count) {
                    $added = $digestAfter.Lines[$digestBefore.Lines.Count..($digestAfter.Lines.Count - 1)]
                }
                $blob = ($added -join "`n")
                $missing = @(@('LANDED', 'DOES NOT PROVE', 'OWNER') | Where-Object { $blob -notmatch [regex]::Escape($_) })
                if ($missing.Count -gt 0) {
                    Write-Loop "digest changed but the appended text does not name: $($missing -join ', '). Not halting; read the entry." 'Yellow'
                } else {
                    Write-Loop 'digest: three-line owner entry appended.'
                }
            }

            # Blind audit, fail closed. Runs on ANY head movement, including one made by a
            # phase that then failed: a half-landed tree is exactly what deserves an auditor.
            if (-not $NoAudit) {
                $auditOut = Join-Path $script:LogDir ('{0:000}-audit.log' -f $i)
                $auditErr = Join-Path $script:LogDir ('{0:000}-audit.err.log' -f $i)
                Write-Loop "blind audit of $(Get-ShortSha $headAfter) [$AuditModel, --safe-mode, prompt on stdin] -> $auditOut"

                $audit = Invoke-ClaudeWithStdinPrompt -ClaudeArgs (Get-AuditArgs) `
                    -PromptFile $script:AuditPromptPath -OutLog $auditOut -ErrLog $auditErr `
                    -TimeoutMinutes $AuditTimeoutMinutes -Label "audit $i"

                $verdict = ''
                if (Test-Path $auditOut) {
                    $hit = Select-String -Path $auditOut -Pattern '^\s*VERDICT:' -ErrorAction SilentlyContinue |
                        Select-Object -Last 1
                    if ($hit) { $verdict = $hit.Line.Trim() }
                }

                if (-not $verdict) {
                    # Distinguish "the tree failed the audit" from "the auditor never ran".
                    # Both halt, but a false FAIL that reads as a real one has cost this
                    # project a run before, so name the real reason.
                    $why = if ($audit.TimedOut) { "auditor exceeded ${AuditTimeoutMinutes}m and was killed" }
                           elseif ($audit.ExitCode -ne 0) { "auditor process exited $($audit.ExitCode) without a verdict (infrastructure, not evidence)" }
                           else { 'auditor produced no VERDICT line' }
                    Stop-ForOperator "blind audit inconclusive at $(Get-ShortSha $headAfter): $why. Log: $auditOut"
                    $exitCode = 5
                    break
                }

                Write-Loop "audit -> $verdict"
                if ($verdict -notmatch '^VERDICT:\s*PASS\s*$') {
                    Stop-ForOperator "blind audit failed at $(Get-ShortSha $headAfter). $verdict. Log: $auditOut. The tree does not support its own claims; do not resume by re-running."
                    $exitCode = 5
                    break
                }
            }
        }
        elseif ($phase.ExitCode -eq 0) {
            $noProgress++
            Write-Loop "phase $i exited 0 without moving HEAD ($noProgress/$MaxNoProgress). Case C (a live WAVE-LOCK) or nothing claimable looks like this." 'Yellow'
            if ($noProgress -ge $MaxNoProgress) {
                Stop-ForOperator "$MaxNoProgress consecutive phases exited 0 without moving HEAD. Most likely an orphaned .port/WAVE-LOCK whose owner is gone, or an empty board. Check $($script:WaveLock) and client/docs/task-board.md."
                $exitCode = 5
                break
            }
        }

        if ($phase.ExitCode -ne 0) {
            $consecutiveFailures++
            if ($consecutiveFailures -ge 2) {
                Stop-ForOperator "two consecutive non-zero claude exits (last: $phaseOut / $phaseErr)"
                $exitCode = 5
                break
            }
            Write-Loop 'retrying once with a fresh session' 'Yellow'
        }
        else { $consecutiveFailures = 0 }
    }

    Write-Loop "port-loop end - elapsed $([int]((Get-Date) - $started).TotalMinutes)m"
    Write-Loop 'digest: client/docs/port-digest.md  logs: .port/runtime/loop/'
}
finally {
    if (Test-Path $script:LoopLock) { Remove-Item -Path $script:LoopLock -Force -ErrorAction SilentlyContinue }
}

exit $exitCode
