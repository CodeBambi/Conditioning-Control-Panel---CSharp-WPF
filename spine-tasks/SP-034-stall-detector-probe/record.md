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
