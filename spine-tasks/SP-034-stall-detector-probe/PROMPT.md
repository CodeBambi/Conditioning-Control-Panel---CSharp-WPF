# Task: SP-034 — Stall-detector probe tooling: CPU/write-delta worker-liveness gate (k3 silent-wedge class)

## Mission

Deliver the **stall-detector tooling row** for the twice-proven k3 silent-wedge class (SP-027 run-1: 84-min dead-API wedge with heartbeats firing; SP-029 replacement worker: 25-min severe-latency crawl that self-recovered): a single-command **probe script** that classifies a spine worker's true state — `alive-progressing` / `alive-crawling` (severe latency — stand down) / `wedged` (T-10 candidate) — replacing the orchestrator's hand-rolled per-incident probes, plus the steering-loop template updates that wire it. The engine's `lane.heartbeat worker_alive` watches PROCESS liveness only; the proven detection rule is **CPU delta + file-write delta over a bounded window** (0.00s over 15–20s twice + zero writes = wedged; 0.03–0.16s/15s = crawling, stand down).

**Honesty framings (binding):** (a) this is an ORCHESTRATOR tool, not an engine patch — the T-5/T-12 discipline (engine changes go through their own packets) does not apply, and this packet must NOT patch pi-spine; (b) the detection thresholds come from the TWO recorded incidents (0.00 = dead; 0.03–0.16 = crawl) — the script encodes them as defaults with the evidence cited, not invented new numbers; (c) the tool NEVER kills on its own — it classifies and prints the T-10 command for the orchestrator to run (the kill decision stays with the steering judgment: provenance salvage, duplicate-sweep, and budget checks come first); (d) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — record in record.md; the orchestrator reconciles at land; (e) the script is a `Tools/` artifact (gitignored local tooling per the repo's convention — the packet's tracked deliverables are the record + the template updates; the script path is referenced, and its content is reproduced in record.md so it is reconstructable).

## Dependencies

- **Task:** SP-030 (wave-1 landed — the second incident's source batch)

## Context to Read First

- `client/docs/port-lessons.md` 2026-07-22 k3 silent-wedge entries (both incidents' detection details + the wave recovery rule) + the stall-detector tooling-row justification
- The wave-1/wave-2 steering-loop prompts (the hand-rolled probe shape: `Get-CimInstance Win32_Process -match spine-<batchId>`, CPU measured twice 15–20s apart, plus the file-write `find -newermt` check) — the script codifies exactly this
- `.pi/npm/node_modules/pi-spine/skills/spine-orchestrate-waves/SKILL.md` (the outer-loop shape the template update must fit)
- `spine-tasks/SP-027-dtrh-host-b5/record.md` (the first incident's forensics) + the wave-1 gate-history entry (the second incident + crawl-vs-wedge distinction)

## File Scope

- `Tools/**` (the probe script — gitignored local tooling; referenced not tracked)
- `.pi/npm/node_modules/pi-spine/skills/spine-orchestrate-waves/SKILL.md` (steering-template amendment via the SP-020 patch manifest — the manifest carries the skill-amendment patch; the LIVE file edit rides the manifest, not git)
- `spine-tasks/SP-034-stall-detector-probe/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `spine-tasks/SP-034-stall-detector-probe/record.md` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/runtime/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md` |
| artifactsMustExist | `spine-tasks/SP-034-stall-detector-probe/record.md`, `Tools/spine-worker-probe.ps1` |

## Steps

### Step 1: Incident evidence consolidation + probe design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] Consolidate both incidents' measured thresholds (0.00s = wedged across two probes 84+25 min; 0.03–0.16s/15s = crawl; the file-write complement) into the probe's default parameter table with citations
- [ ] Design: the classification state machine (progressing/crawling/wedged), the probe windows (15s + confirm), the batch-id scoping (`-match "spine-<batchId>"` on pi/node command lines), the output contract (state + evidence numbers + the T-10 command template), the self-test shape (probe against THIS packet's own running batch — must report progressing/crawling, never a false wedge)
- [ ] **Pre-approach solo consult** (Fable 5, solo) with the design; verdict text + ACTUAL answering model in record.md BEFORE checkbox

### Step 2: The probe script + self-test

- [ ] `Tools/spine-worker-probe.ps1` (gitignored local tool): batch-id scoped process enumeration, CPU-delta measurement over two windows, file-write delta over the lane worktree, classification with cited thresholds, evidence numbers printed, T-10 command template printed for the wedged class only
- [ ] Self-test against the live batch (must NOT report a false wedge); a forced-wedged simulation (a spawned sleep-only node process tagged with the batch id in a scratch command line — must report wedged and print the T-10 template); a crawling simulation (busy-loop node process — must report crawling, never wedged)
- [ ] The skill-template amendment authored as an SP-020 manifest patch entry (anchor from the live SKILL.md; amendment text: the probe command + classification → action table + the kill-discipline note)

### Step 3: Manifest patch + apply/verify + docs

- [ ] Manifest entry (`skill-stall-detector-probe`) authored, applied, verified (idempotent, loud-on-drift); the amended SKILL.md text reviewed for the outer-loop fit
- [ ] Write `spine-tasks/SP-034-stall-detector-probe/record.md` (incident evidence, thresholds table, design, self-test transcripts, consult verdicts + ACTUAL answering models, engine-review presence, the script reproduced in full for reconstructability, durable-lesson candidates)
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the probe + thresholds; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green — counts ≥ the 446/29 floor; client tree untouched, no drift)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths (Tools/ is gitignored — status shows packet + manifest only)

## Completion Criteria

- `Tools/spine-worker-probe.ps1` delivered with cited thresholds, classification state machine, self-test transcripts (no false wedge on a live batch; wedged + crawling simulations classified correctly)
- Skill-template amendment landed via the manifest (applied, verified, idempotent)
- Both solo Fable consults persisted with actual answering models; contract green (≥446/29 floor, no drift)

## Do NOT

- Patch pi-spine engine code (this is orchestrator tooling); auto-kill (the tool classifies + prints the command; the kill decision stays with steering judgment); invent thresholds beyond the two incidents' measurements; edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/runtime/**`; set any board row state
- Use `consult` council mode (route broken — solo Fable 5 only)

## Git Commit Convention

- `feat(SP-034): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `spine-tasks/SP-034-stall-detector-probe/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-07-22 (authoring): **k3 silent-wedge class — twice proven (SP-027 run-1, SP-029 replacement); recurrence justifies the tooling row (the harvest's own rule).** Thresholds from the two incidents only. No engine patch (orchestrator tool). Tools/ gitignored convention — script reproduced in record.md. Skill-template amendment via the SP-020 manifest (same class as skill-headed-evidence-sizing). Enabler 2 encoded. Headless; 4h budget exported at launch.
- 2026-07-22 (authoring): `## Review Level: 2` structured heading emitted (T-2 fixed format). Launch: validate → analyze → plan → preflight → detached wave batch (SP-033 + SP-034, 2 lanes) per owner cycle.
