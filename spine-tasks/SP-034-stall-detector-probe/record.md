# SP-034 — Stall-detector probe tooling: record

Task: CPU/write-delta worker-liveness probe for the twice-proven k3 silent-wedge class.
Orchestrator tooling — NOT an engine patch. The tool classifies and prints the T-10 command; it never kills.

## 1. Incident evidence (thresholds source — the only numbers allowed)

### Incident 1 — SP-027 run-1 (batch `20260722T051051`, 2026-07-22 ~08:24 UTC)

- Source: `client/docs/port-lessons.md` 2026-07-22 "k3 silent-wedge class (SP-027 run-1)"; SP-027 record.md line 70; wave CONTEXT row SP-027.
- Measured: **84 min no commit + 30 min no file writes + 0.0s CPU delta over a 20s probe = functionally dead.**
- The engine's `lane.heartbeat worker_alive` kept firing the whole time — heartbeats watch PROCESS liveness, not progress.
- Recovery: T-10 kill + WIP salvage (commit `038fe603`, marked UNVERIFIED provenance) + retry; run-2 clean.

### Incident 2 — SP-029 replacement worker (batch `20260722T101444`, 2026-07-22 10:53 UTC)

- Source: wave-1 gate-history — `.spine/runtime/20260722T101444/evidence/summary.md` "Wave recovery history" item 3; port-lessons 2026-07-22 "SECOND occurrence same day".
- Measured: **0.00s CPU + zero writes for ~25 min, then SELF-RECOVERED** (k3 severe-latency patch; CPU climbing **0.03 → 0.16s per 15s**; harness built). Kill-watch stood down at the right threshold — a sub-30-min drought with 0 CPU is NOT a wedge.
- Consequence for thresholds: the proven-DEAD write drought is incident-1's **30 min**; a ~25-min drought recovered. The wedged verdict must be gated on the 30-min drought, not on the probe span.

### Derived default parameter table (each number cited)

| Parameter | Default | Evidence |
|---|---|---|
| `WindowSeconds` | 15 | Both incidents used 15–20s probes (port-lessons L87, L95). |
| `ConfirmWindows` | 2 | Incident-1 rule: 0.00s across consecutive probes, not one sample. |
| `WedgedEpsilonSeconds` | 0.005 | "0.0s CPU delta" — below this rounds to the measured 0.00. |
| Crawl band (advisory) | 0.03–0.16 s/15s | Incident-2 measured climb during self-recovery. |
| `CrawlCeilingSeconds` | 1.0 | NOT an incident number — order-of-magnitude judgment margin above the observed 0.16 ceiling; separates "crawl-band activity" from "clearly active" (e.g. a burning dotnet test child). Overridable. |
| `WedgedDroughtMinutes` | 30 | Incident-1 proven-dead drought; incident-2 self-recovered at ~25 — the wedged gate must sit at the proven-dead value. |
| No-commit context (printed, not gated) | 84 min | Incident-1's human-judgment threshold the probe replaces. |

## 2. Design

### Process scoping (verified against the live batch 2026-07-22)

Live enumeration of this packet's own batch shows the worker shape: `pi.exe` (root wrapper) → `node.exe` (pi CLI) → tool children (`bash.exe`, `powershell.exe`, `dotnet.exe`). The engine runner (`spine-worker-runner.mjs`) does NOT carry the batch id in its command line, so engine heartbeat activity can never contaminate the worker measurement.

- Roots: `pi.exe`/`node.exe` processes whose `CommandLine` matches `spine-<BatchId>` (regex-escaped), optional `-Lane N` narrowing to `spine-<BatchId>\lane-N`.
- **Tree walk (consult Correction 1):** the matched roots alone false-wedge during `dotnet build/test` — children run with relative paths that lack the batch id. The probe walks `ParentProcessId` over a fresh `Win32_Process` snapshot at each sample and sums `Get-Process.TotalProcessorTime` across the whole tree (roots + transitive descendants). Pids are re-resolved each sample; tree churn (spawned/exited) is reported.

### Measurement

- CPU: per-window tree-summed `TotalProcessorTime` delta over `WindowSeconds`, `ConfirmWindows` consecutive windows (advisor note: `Get-Process -Id` resolution, not the CIM property; deltas over union pids, clamped ≥ 0, churn noted).
- Writes: full recursive scan of the lane worktree(s) — `<RepoRoot>/.worktrees/spine-<BatchId>/lane-*` when run from the base repo; the worktree root itself when run from inside a lane (self-test); none when the batch id has no worktree (simulations → zero writes, infinite drought). Reports files written during the probe span + newest write path/age. Includes `.git` (commits are progress) and `bin/obj` (build writes are progress).

### Classification state machine

```
writes during/near probe span        → alive-progressing (unconditional progress evidence)
all windows CPU-zero (< epsilon):
    newest-write age ≥ WedgedDroughtMinutes (30) → wedged        (incident-1 proven-dead rule)
    else                                          → alive-crawling (stand down — incident-2 self-recovered here)
some window CPU-active:
    max window delta ≤ CrawlCeilingSeconds (1.0)  → alive-crawling (observed band, advisory for trees)
    else                                          → alive-progressing (CPU clearly active, e.g. test run)
```

### Multi-lane aggregation (named limit, consult Gap 2)

Without `-Lane`, roots span ALL lanes of the batch and the write drought is computed over all lane dirs: a progressing lane masks a wedged sibling, and a batch-wide `wedged` verdict's T-10 template would name healthy lanes' pids. The tool never kills, but the printed template must not over-name. Guard shipped: a batch-wide probe on a multi-lane batch prints a `NOTE: multi-lane probe ... re-probe with -Lane N before acting on a wedged verdict` line. Deliberately NOT re-based into the already-applied manifest amendment (the `[-Lane N]` hint is already in its probe command; per the consult, script warning + this record note is enough).

### Output contract

State line + evidence numbers (per-window tree CPU deltas, matched root pids, tree size/churn, writes-during-probe count, newest-write path/age, drought threshold). **T-10 template printed for the wedged class only**, prefixed by the kill discipline: (1) salvage lane WIP with UNVERIFIED-provenance commit, (2) duplicate-worker sweep (resume spawns replacements without reaping originals — wave recovery rule), (3) budget/stall context check — THEN `Stop-Process -Id <rootPids> -Force`. Exit codes: 0 progressing, 1 crawling, 2 wedged, 3 usage/no-match.

### Self-test shape

1. Live probe against THIS packet's own batch (`20260722T140255`) — must report progressing/crawling, never a false wedge.
2. Forced-wedged simulation: sleep-only `node` process tagged with a scratch batch id in its command line (no worktree → zero writes) — must report wedged and print the T-10 template; sim process cleaned up after.
3. Crawling simulation: throttled busy-loop `node` (~15ms busy per 2s cycle ≈ 0.11s/15s, inside the observed 0.03–0.16 band) — must report alive-crawling, never wedged.

### Skill-template amendment

`skill-stall-detector-probe` entry in `.spine/patches/manifest.json` (SP-020 mechanism, same class as `skill-headed-evidence-sizing`): anchor-based patch on `skills/spine-orchestrate-waves/SKILL.md`, PROJECT-TREE-ONLY (CCP-specific text must not land in the shared engine tree — SP-031 rationale). Content: the probe command, the classification → action table, the kill-discipline note. Applied via `apply.mjs`, verified via `verify.mjs` (idempotent, loud-on-drift).

## 3. Pre-approach consult (solo; tool: `consult`)

Requested route: solo Fable 5 (packet decree — council route broken). Actual answering model: NOT surfaced in the consult tool output (SP-028/SP-029/SP-031/SP-032 precedent — recorded as such).

Provenance note (same class as SP-032): the first consult call's verdict text TRUNCATED mid-Correction-1 in delivery ("require **the"). A follow-up gut-check recovered honestly: the truncated tail was unrecoverable (no hidden buffer); Correction 1 was captured in full; the follow-up supplied its own fresh read, flagged as the second advisor's judgment, not the prior verdict.

**Verdict (call 1, solo):** "design is structurally sound — proceed, with two corrections and three minor notes." Captured in full before truncation:

- **CORRECTION 1 (binding, adopted):** CPU must be measured over the process TREE, not just batch-id-matched node processes — during `dotnet build/test` the matched roots legitimately sit near 0.00s CPU while a dotnet child burns a core (children's command lines lack the batch id), and writes cluster at build/restore/result-flush. "That's your main false-wedge vector." Fix: walk `ParentProcessId` descendants, sum tree deltas. (Remainder truncated mid-sentence.)

**Follow-up read (call 2, gut-check — advisor's own judgment, flagged as such):**

- **Likely Correction 2 (adopted):** zero-writes must look back further than the probe span — incident-1's rule was "30 min no file writes + 0.0s CPU". A between-steps worker can sit silent 30s without being wedged. Fix: report time-since-newest-write/commit; gate `wedged` on the write drought (≥ incident evidence), CPU double-zero is the trigger, drought the corroborator. Implemented as `WedgedDroughtMinutes = 30` (incident-1 proven-dead; incident-2's ~25-min self-recovery proves sub-threshold must stand down).
- Minor note 1 (adopted): `TotalProcessorTime` sums across cores — tree-summing breaks the per-process 0.03–0.16 band's direct applicability; wedged epsilon stays authoritative, crawl band demoted to advisory. (Reflected in the state machine + amendment wording.)
- Minor note 2 (adopted): read CPU via `Get-Process -Id .TotalProcessorTime`; re-resolve pids each sample (tree spawns/dies mid-window).
- Minor note 3 (adopted): do NOT hard-code 0.16+margin as a magic progressing threshold — no incident evidence above 0.16. Above-epsilon-but-modest → crawling; progressing requires writes or clearly-active CPU. (`CrawlCeilingSeconds = 1.0` documented as a judgment margin, overridable, not an incident number.)

---

## 4. Self-test transcripts (2026-07-22, this packet's own batch `20260722T140255`)

### 4.1 Live probe — must NOT false-wedge (PASS)

```
PROBE spine-20260722T140255 @ 2026-07-22 16:25:15
roots: 35972, 25340, 22632, 17316  (pattern: spine-20260722T140255)
tree: 11 pids
window 1: tree CPU delta 0.125s over 15s (+0 spawned / -0 exited)
window 2: tree CPU delta 0.156s over 15s (+0 spawned / -0 exited)
writes: 0 file(s) during probe; newest write: ...\lane-2\Tools\spine-worker-probe.ps1 (0.6 min ago); drought threshold 30min
STATE: alive-progressing
EXIT=0
```

### 4.2 Forced-wedged simulation — sleep-only node tagged with scratch batch id (PASS)

Sim: `node -e "setInterval(()=>{},60000); void 'spine-selftest-wedge'"` (no worktree → zero writes, infinite drought).

```
PROBE spine-selftest-wedge @ 2026-07-22 16:28:52
roots: 38472, 34840  (pattern: spine-selftest-wedge)
tree: 2 pids
window 1: tree CPU delta 0.000s over 15s (+0 spawned / -0 exited)
window 2: tree CPU delta 0.000s over 15s (+0 spawned / -0 exited)
writes: no lane worktree found for batch id - zero writes, drought = infinite
STATE: wedged

T-10 CANDIDATE - the kill decision stays with steering judgment:
  1. SALVAGE lane WIP first:  git -C <laneDir> add -A; git -C <laneDir> commit -m 'salvage (UNVERIFIED provenance)'
  2. DUPLICATE-WORKER SWEEP:  resume spawns a replacement WITHOUT reaping the original - confirm these roots are the engine-untracked pair
  3. BUDGET/STALL CONTEXT:    check journal + STATUS.md before killing
  then: Stop-Process -Id 38472,34840 -Force
WEDGE_EXIT=2
```

Sim processes killed after the probe; a post-run sweep (`selftest` cmdline match) removed two leftover sims from the first (option-parsing-failed) attempt. (Two roots matched because a leftover sim from the failed first attempt shared the tag; the sweep cleaned both.)

### 4.3 Crawling simulation — throttled busy-loop node (PASS)

Sim: `node -e "setInterval(()=>{const t=Date.now();while(Date.now()-t<15){}},2000); void 'spine-selftest-crawl'"` (~15ms busy per 2s cycle = ~0.11s/15s, inside the observed 0.03-0.16 band).

```
PROBE spine-selftest-crawl @ 2026-07-22 16:27:41
roots: 16280  (pattern: spine-selftest-crawl)
tree: 1 pids
window 1: tree CPU delta 0.125s over 15s (+0 spawned / -0 exited)
window 2: tree CPU delta 0.094s over 15s (+0 spawned / -0 exited)
writes: no lane worktree found for batch id - zero writes, drought = infinite
STATE: alive-crawling
CRAWL_EXIT=1
```

### 4.4 Post-consult re-runs (per-pid clamp + multi-lane note; pre-completion consult Gaps 1-2)

Wedged sim re-run (clamp touches every classification path):

```
PROBE spine-selftest-wedge2 @ 2026-07-22 16:40:23
roots: 40668  (pattern: spine-selftest-wedge2)
tree: 1 pids
window 1: tree CPU delta 0.000s over 15s (+0 spawned / -0 exited)
window 2: tree CPU delta 0.000s over 15s (+0 spawned / -0 exited)
writes: no lane worktree found for batch id - zero writes, drought = infinite
STATE: wedged
[ T-10 CANDIDATE block printed; Stop-Process -Id 40668 -Force ]
WEDGE_EXIT=2
```

Live per-lane re-run (`-Lane 2` filter proven: roots narrowed to lane-2's pi/node pair):

```
PROBE spine-20260722T140255 lane-2 @ 2026-07-22 16:40:54
roots: 22632, 17316  (pattern: spine-20260722T140255[\\/]lane-2([^0-9]|$))
tree: 8 pids
window 1: tree CPU delta 0.094s over 15s (+0 spawned / -0 exited)
window 2: tree CPU delta 0.047s over 15s (+0 spawned / -0 exited)
writes: 0 file(s) during probe; newest write: ...\lane-2\Tools\spine-worker-probe.ps1 (1.3 min ago); drought threshold 30min
STATE: alive-crawling
LIVE_EXIT=1
```

Alive-crawling on a worker parked between tool calls awaiting its model is the honest verdict (CPU in the observed band, drought minutes old) - and it is NOT a false wedge, which is the requirement. The 4.1 batch-wide live probe ran before the multi-lane NOTE shipped; the note code path is a one-line guard on `$laneDirs.Count -gt 1` without `-Lane`.

### 4.5 Defects found by the self-tests (fixed same-step)

1. **CIM uint32/int hashtable key mismatch:** `Win32_Process.ParentProcessId` is uint32; hashtable lookups with int keys miss (boxed numeric type inequality), so the tree walk found zero descendants (tree = roots only). Fix: `[int]` cast once at map build. Found by the live probe reporting `tree: 4 pids` when bash/powershell children existed.
2. **StrictMode `$null.Count`:** `@($windows | Where-Object ...)` needed — on the all-zero (wedged) path the unwrapped filter returns `$null` and `.Count` throws under `Set-StrictMode -Version Latest`. The wedged simulation was the only path that exercised it — the self-test matrix caught what the live probe could not.
3. **Empty-tree `Get-Process -Id` binding error** when no roots match — guarded; the no-match branch (exit 3) now reachable.
4. **PowerShell 5.1 smart-quote hazard:** UTF-8-without-BOM .ps1 with em-dashes — 5.1 reads the file as ANSI and `0x94` parses as a string delimiter. Script is ASCII-only.
5. **Manifest anchor reproduced by its own replacement** (first `skill-stall-detector-probe` draft verified `drifted: anchor×1 replacement×1`): the anchor spanned the junction my replacement re-created. Re-based to span the last diagnosis-table row + junction (consumed on apply, never reproduced) - same consumption pattern as `skill-headed-evidence-sizing`. Loud-on-drift proven live by this failure; idempotency proven by the re-apply `skip` below.
6. **Window-TOTAL negative-delta clamp let an exiting busy child cancel live CPU** (pre-completion consult Gap 1): a dotnet child with 200s accumulated CPU exiting mid-window dragged the sum to -199.9, clamped to 0.000 - a window reading exactly like a wedge at the exact moment long silent test runs end. Fixed per-pid (only positive per-pid deltas summed).
7. **Multi-lane dilution** (pre-completion consult Gap 2): see §2 named limit; guarded with the batch-aggregation NOTE.

## 5. Manifest patch — apply/verify transcript

Entry `skill-stall-detector-probe` in `.spine/patches/manifest.json` (project-tree-only, no `engine` flag — CCP-specific text must not land in the shared engine tree, SP-031 rationale). Target: `skills/spine-orchestrate-waves/SKILL.md`, inserted as a new "Worker stall detection" section between "Diagnosis -> agent action" and "Anti-patterns" (probe command + classification-to-action table + kill discipline).

```
node .spine/patches/apply.mjs  -> apply skill-stall-detector-probe [project]; wrote skills/spine-orchestrate-waves/SKILL.md; OK
node .spine/patches/verify.mjs -> applied x7 [project] + applied x5 [engine]; OK - all patches applied on all roots. EXIT=0
re-run apply.mjs               -> skip skill-stall-detector-probe [project] (already applied)  (idempotent)
```

Outer-loop fit review: the section sits at the exact decision point an orchestrator hits during `spine wait` (a lane that has gone silent), references the wave recovery rule's duplicate-worker class, and routes all three verdicts to stand-down actions except the cited wedged class.

## 6. The probe script (reproduced in full — Tools/ is gitignored local tooling; this is the durable copy)

```powershell
#requires -Version 5.1
<#
.SYNOPSIS
    spine-worker-probe - CPU/write-delta liveness probe for spine batch workers.

.DESCRIPTION
    Orchestrator tool for the k3 silent-wedge class (twice proven 2026-07-22:
    SP-027 run-1 84-min dead-API wedge with heartbeats firing; SP-029 replacement
    worker 25-min severe-latency crawl that self-recovered). The engine's
    lane.heartbeat worker_alive watches PROCESS liveness only; this probe measures
    what heartbeats cannot: CPU delta over the worker's whole process tree plus
    file-write delta over the lane worktrees.

    Classification (thresholds from the two incidents ONLY, cited in
    spine-tasks/SP-034-stall-detector-probe/record.md):
      alive-progressing - writes during the probe, or CPU clearly active
      alive-crawling    - CPU above ~0 but in/near the observed 0.03-0.16s/15s
                          crawl band, or CPU-zero with a sub-threshold write
                          drought (incident 2 self-recovered here - STAND DOWN)
      wedged            - 0.00s tree CPU delta over BOTH windows AND newest-write
                          age >= WedgedDroughtMinutes (incident-1 proven-dead rule)

    The probe NEVER kills. For the wedged class it prints the T-10 candidate
    command; the kill decision stays with steering judgment (salvage WIP first,
    duplicate-worker sweep, budget/stall check).

    Exit codes: 0 = alive-progressing, 1 = alive-crawling, 2 = wedged,
                3 = no matching worker processes found.

.EXAMPLE
    pwsh Tools/spine-worker-probe.ps1 -BatchId 20260722T140255
.EXAMPLE
    pwsh Tools/spine-worker-probe.ps1 -BatchId 20260722T140255 -Lane 2 -WindowSeconds 20
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BatchId,
    [string]$Lane,
    [int]$WindowSeconds = 15,              # both incidents used 15-20s probes
    [int]$ConfirmWindows = 2,              # incident-1 rule: consecutive zero probes
    [double]$WedgedEpsilonSeconds = 0.005, # below this rounds to the measured "0.0s"
    [double]$CrawlCeilingSeconds = 1.0,    # ponytail: judgment margin (order of magnitude over the observed 0.16 ceiling), NOT an incident number
    [int]$WedgedDroughtMinutes = 30,       # incident-1 proven-dead drought; incident-2 self-recovered at ~25
    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Resolve lane worktree dirs -------------------------------------------
if (-not $RepoRoot) { $RepoRoot = Split-Path -Parent $PSScriptRoot }  # script lives in Tools/
$waveDir = Join-Path $RepoRoot ".worktrees\spine-$BatchId"
$laneDirs = @()
if (Test-Path $waveDir) {
    $laneDirs = @(Get-ChildItem -Path $waveDir -Directory -Filter 'lane-*' | ForEach-Object FullName)
    if ($Lane) { $laneDirs = @($laneDirs | Where-Object { $_ -match "[\\/]lane-$Lane([^0-9]|$)" }) }
}
elseif ($RepoRoot -match "spine-$([regex]::Escape($BatchId))") {
    # Running from inside a lane worktree (self-test shape)
    $laneDirs = @($RepoRoot)
}

# --- Process tree sampling -------------------------------------------------
$escBatch = [regex]::Escape($BatchId)
$rootPattern = "spine-$escBatch"
if ($Lane) { $rootPattern = "spine-$escBatch[\\/]lane-$Lane([^0-9]|$)" }

function Get-WorkerTreeSample {
    # One CIM snapshot; roots = pi/node cmdline match; tree = roots + descendants.
    $all = @(Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name, CommandLine)
    $roots = @($all | Where-Object {
        ($_.Name -eq 'pi.exe' -or $_.Name -eq 'node.exe') -and
        $_.CommandLine -and $_.CommandLine -match $rootPattern
    })
    $byParent = @{}
    foreach ($p in $all) {
        $ppid = [int]$p.ParentProcessId  # ponytail: CIM uint32 keys miss int lookups in hashtables - cast once
        if (-not $byParent.ContainsKey($ppid)) { $byParent[$ppid] = @() }
        $byParent[$ppid] += [int]$p.ProcessId
    }
    $tree = @{}
    $queue = New-Object 'System.Collections.Generic.Queue[int]'
    foreach ($r in $roots) { $queue.Enqueue([int]$r.ProcessId) }
    while ($queue.Count -gt 0) {
        $pid_ = $queue.Dequeue()
        if ($tree.ContainsKey($pid_)) { continue }
        $tree[$pid_] = $true
        if ($byParent.ContainsKey($pid_)) { foreach ($c in $byParent[$pid_]) { $queue.Enqueue($c) } }
    }
    $cpu = @{}
    if ($tree.Count -gt 0) {
        foreach ($proc in (Get-Process -Id @($tree.Keys) -ErrorAction SilentlyContinue)) {
            $cpu[$proc.Id] = $proc.TotalProcessorTime.TotalSeconds
        }
    }
    [PSCustomObject]@{
        Roots    = @($roots | ForEach-Object ProcessId)
        TreePids = @($tree.Keys)
        Cpu      = $cpu
    }
}

$s0 = Get-WorkerTreeSample
if ($s0.Roots.Count -eq 0) {
    Write-Host "PROBE spine-$BatchId - no pi/node worker processes matched (pattern: $rootPattern)"
    Write-Host "STATE: no-match"
    exit 3
}

$probeStart = Get-Date
$windows = @()
for ($w = 1; $w -le $ConfirmWindows; $w++) {
    Start-Sleep -Seconds $WindowSeconds
    $s1 = Get-WorkerTreeSample
    $union = @($s0.TreePids + $s1.TreePids | Sort-Object -Unique)
    $delta = 0.0
    foreach ($p in $union) {
        $a = 0.0; $b = 0.0
        if ($s0.Cpu.ContainsKey($p)) { $a = $s0.Cpu[$p] }
        if ($s1.Cpu.ContainsKey($p)) { $b = $s1.Cpu[$p] }
        $d = $b - $a
        if ($d -gt 0) { $delta += $d }  # per-pid positive clamp: an exiting busy child must not cancel live CPU (false-wedge class)
    }
    $spawned = @($s1.TreePids | Where-Object { $s0.TreePids -notcontains $_ }).Count
    $exited  = @($s0.TreePids | Where-Object { $s1.TreePids -notcontains $_ }).Count
    $windows += [PSCustomObject]@{ Index = $w; Delta = $delta; Spawned = $spawned; Exited = $exited }
    $s0 = $s1
}
$probeEnd = Get-Date

if (-not $Lane -and $laneDirs.Count -gt 1) {
    Write-Host "NOTE: multi-lane probe ($($laneDirs.Count) lanes) - verdicts are batch-aggregated; re-probe with -Lane N before acting on a wedged verdict"
}

# --- File-write delta ------------------------------------------------------
$writesDuringProbe = 0
$newestWrite = $null
foreach ($dir in $laneDirs) {
    foreach ($f in (Get-ChildItem -Path $dir -Recurse -File -Force -ErrorAction SilentlyContinue)) {
        if ($f.LastWriteTime -ge $probeStart -and $f.LastWriteTime -le $probeEnd) { $writesDuringProbe++ }
        if (-not $newestWrite -or $f.LastWriteTime -gt $newestWrite.LastWriteTime) { $newestWrite = $f }
    }
}
$droughtMin = if ($newestWrite) { [math]::Round(((Get-Date) - $newestWrite.LastWriteTime).TotalMinutes, 1) } else { [double]::PositiveInfinity }

# --- Classification ---------------------------------------------------------
$allZero = @($windows | Where-Object { $_.Delta -ge $WedgedEpsilonSeconds }).Count -eq 0
$maxDelta = ($windows | Measure-Object -Property Delta -Maximum).Maximum
$probeSpanMin = [math]::Max(1, [math]::Ceiling(($probeEnd - $probeStart).TotalMinutes))

if ($writesDuringProbe -gt 0 -or $droughtMin -le $probeSpanMin) {
    $state = 'alive-progressing'
}
elseif ($allZero) {
    if ($droughtMin -ge $WedgedDroughtMinutes) { $state = 'wedged' }
    else { $state = 'alive-crawling' }  # incident-2 zone: 0 CPU + ~25min drought self-recovered - STAND DOWN
}
elseif ($maxDelta -le $CrawlCeilingSeconds) {
    $state = 'alive-crawling'
}
else {
    $state = 'alive-progressing'
}

# --- Output ------------------------------------------------------------------
Write-Host "PROBE spine-$BatchId$(if ($Lane) { " lane-$Lane" }) @ $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Host "roots: $($s0.Roots -join ', ')  (pattern: $rootPattern)"
Write-Host "tree: $($s0.TreePids.Count) pids"
foreach ($w in $windows) {
    Write-Host ("window {0}: tree CPU delta {1:N3}s over {2}s (+{3} spawned / -{4} exited)" -f $w.Index, $w.Delta, $WindowSeconds, $w.Spawned, $w.Exited)
}
if ($laneDirs.Count -gt 0) {
    $newestStr = if ($newestWrite) { "$($newestWrite.FullName) ($droughtMin min ago)" } else { 'none found' }
    Write-Host "writes: $writesDuringProbe file(s) during probe; newest write: $newestStr; drought threshold ${WedgedDroughtMinutes}min"
}
else {
    Write-Host "writes: no lane worktree found for batch id - zero writes, drought = infinite"
}
Write-Host "STATE: $state"

if ($state -eq 'wedged') {
    Write-Host ""
    Write-Host "T-10 CANDIDATE - the kill decision stays with steering judgment:"
    Write-Host "  1. SALVAGE lane WIP first:  git -C <laneDir> add -A; git -C <laneDir> commit -m 'salvage (UNVERIFIED provenance)'"
    Write-Host "  2. DUPLICATE-WORKER SWEEP:  resume spawns a replacement WITHOUT reaping the original - confirm these roots are the engine-untracked pair"
    Write-Host "  3. BUDGET/STALL CONTEXT:    check journal + STATUS.md before killing"
    Write-Host "  then: Stop-Process -Id $($s0.Roots -join ',') -Force"
}

switch ($state) {
    'alive-progressing' { exit 0 }
    'alive-crawling'    { exit 1 }
    'wedged'            { exit 2 }
}
```

## 7. Engine-review presence

Review Level 2. In-worker `spine_review_step` (plan) called at Steps 1 and 2: both returned `skipped` (`nested_spawn_blocked` — the wave-1 precedent; the batch engine runs code + final reviews after `.DONE`). No review artifact paths were produced in-worker; engine reviews will appear under `.reviews/` post-`.DONE`.

## 8. Durable-lesson candidates (orchestrator harvests at land — enabler 2)

1. **Probe the process TREE, never the matched roots alone** — batch-id command-line matching catches pi/node, but a healthy worker's `dotnet build/test` children carry no batch id in their command lines; root-only CPU measurement false-wedges exactly during the busiest legitimate work. Walk `ParentProcessId`; re-resolve each sample.
2. **CPU-zero is the trigger, write-drought the corroborator** — incident 2 self-recovered from 0.00s CPU + ~25-min zero writes; the wedged verdict must gate on the incident-1 proven-dead 30-min drought, or the detector re-creates the false-kill class it exists to prevent.
3. **A stall-detector's own self-test matrix needs a forced-wedged simulation** — the StrictMode `$null.Count` defect lived on the wedged code path only; a live-batch probe can never exercise it. Sleep-only + throttled-busy-loop tagged sims are cheap and deterministic.
4. **Windows PowerShell 5.1 parses UTF-8-without-BOM .ps1 as ANSI; em-dash `0x94` becomes a string delimiter** — keep Tools/ scripts ASCII-only (or BOM them).
5. **SP-020 anchor patches must not reproduce their own anchor** — a replacement that re-creates the anchor text verifies `drifted` (anchor×1 replacement×1) immediately after apply; span the anchor across content the replacement consumes.

## 9. Pre-completion consult (solo; tool: `consult`)

Requested route: solo Fable 5 (packet decree). Actual answering model: NOT surfaced in the consult tool output (established precedent - recorded as such). Provenance: the verdict text TRUNCATED again at the tail ("Minor") - both gaps and the ship verdict were captured in full before the cut; the truncated tail was unrecoverable (same class as the pre-approach consult, recorded honestly).

**Verdict: "the work is sound and shippable as orchestrator tooling; no blocking reason found. Two real gaps to close before .DONE, both cheap."**

- **Gap 1 (correctness, ADOPTED + FIXED):** window-TOTAL negative-delta clamp let an exiting busy child cancel live CPU (dotnet child with 200s accumulated exits, sum -199.9 clamped to 0.000 = a window reading exactly like a wedge at the end of a long silent test run - "a false wedge from the tool built to prevent false kills"). Fixed per-pid: only positive per-pid deltas summed; churn evidence stays in the spawned/exited columns.
- **Gap 2 (usage sharp edge, ADOPTED + GUARDED):** multi-lane dilution without `-Lane` - a progressing lane masks a wedged sibling, and a batch-wide wedged T-10 template names healthy pids. Guard: multi-lane NOTE line telling the orchestrator to re-probe per-lane before acting; named limit in §2; manifest amendment deliberately NOT re-based (its `[-Lane N]` hint already covers the discovery path).
- Post-fix instructions followed: wedged sim + live probe re-run (§4.4, both correct), §6 regenerated to match shipped bytes, this verdict persisted.
