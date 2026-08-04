# Task: SP-039 — T-14: lane-local patch application at worktree creation (worktree-setup hook)

## Mission

Execute the `client/docs/task-board.md` P1 tooling row **"T-14: lane-local pi-spine needs re-patching on every fresh machine/lane — automate via worktree-setup hook"** (OPEN, filed 2026-08-04): make every fresh spine lane worktree start with the `.spine/patches/` manifest applied, so no packet's `verify.mjs` contract step reds on a fresh lane. 3rd occurrence in one day (SP-035/SP-036/SP-037 workers all remediated mid-task with `apply.mjs`); the recurrence rule fired; this is the durable encoding.

**Honesty framings (binding):** (a) **the mechanism is decided from ENGINE EVIDENCE, never assumed:** the open question is TIMING — the engine's worktree-setup hook (if any) fires at worktree creation, but the lane's `.pi/npm` pi-spine install may not exist yet at that point (it appears when the worker/engine first runs pi in the lane). If the hook fires before the install exists, applying patches there is a no-op and the real seam is elsewhere (the engine's lane-install step or the worker-runner's pre-prompt phase). Read the engine source and decide; record the rejected alternatives; (b) the hook must be **idempotent and fail-safe** — a hook that breaks worktree provisioning would block every future batch (worse than the disease); `apply.mjs` is all-or-nothing + loud on drift, and a fresh lane may legitimately have nothing to patch yet — the hook's exit semantics must match the engine's contract exactly; (c) **this packet's OWN lane starts unpatched** — its contract run will hit the standing verify.mjs red once more; remediate per the norm (`apply.mjs`) and record it as (hopefully) the last manual occurrence — never claim the hook is proven by this packet alone; (d) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — orchestrator reconciles at land; (e) proof of the fix = a fresh worktree provisioned THROUGH THE ENGINE'S OWN PATH (a stub batch or the engine's provisioning call) arriving with patches present BEFORE any worker runs — a named post-land gate carries the first real 2-lane wave.

## Dependencies

- **None** (SP-020 patch mechanism + SP-031 two-root model landed long ago)

## Context to Read First

- `client/docs/task-board.md` row T-14 (acceptance text) + row T-5 (the two-root truth: engine executes from the GLOBAL install; lanes get their own project-root installs)
- `.spine/patches/` (manifest.json, apply.mjs, verify.mjs, README — the mechanism being wired)
- Engine source (READ-ONLY, the global install `C:\Users\Micha\.pi\agent\npm\node_modules\pi-spine\`): `src/config/worktree-setup-hook.mjs` (hook resolution contract), `src/batch/worktree.mjs` (`provisionLaneWorktree` + the hook call site + `WORKTREE_SETUP_HOOK_TIMEOUT_MS`), wherever the lane's `.pi/npm` install is triggered (worker spawn path — `bin/spine-worker-runner.mjs` or the engine's worker launch)
- `spine-tasks/SP-037-asset-manifest-v663-resync/record.md` + `spine-tasks/SP-035-ai-companion-c2/record.md` + `spine-tasks/SP-036-avalonia-mcp-audit/record.md` (the three mid-task remediation incidents — exact failure shape)
- `spine-tasks/CONTEXT.md` execution policy §5 (stub-first rule) + the T-5 row's standing verify rule

## File Scope

- `.spine/spine-config.json` (hook wiring, if the engine contract uses config)
- `.spine/patches/**` (the hook script itself, manifest README note if the contract lives there)
- `spine-tasks/SP-039-worktree-patch-hook/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `.spine/spine-config.json` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/**`, `.pi/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md` |
| artifactsMustExist | `spine-tasks/SP-039-worktree-patch-hook/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md. **Authoring rule (SP-034 defect): verify `grep -c "Review Level" PROMPT.md` ≥ 2 before launch.**

## Steps

### Step 1: Engine archaeology + mechanism decision + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] Read the engine's worktree-setup-hook contract (resolution, invocation point, arguments/env, timeout, exit-code semantics) + the lane `.pi/npm` install timing (when does a lane's pi-spine install appear relative to provisioning and to the worker's first run — engine source evidence, not assumption)
- [ ] Mechanism decision in record.md: the chosen seam (hook at creation / post-install step / runner pre-prompt) with the timing evidence; rejected alternatives with reasons; the hook's exit semantics on "nothing to patch yet" vs "apply failed" (fail-safe, never blocks provisioning)
- [ ] **Pre-approach solo consult** (per the 2026-08-04 rewire: Opus 5 main route; Fable 5 fallback per the pause protocol) with the archaeology + mechanism; verdict text + ACTUAL answering model in record.md BEFORE checkbox

### Step 2: Implement + scratch verification

- [ ] Hook script + config wiring (idempotent; respects the engine's exit contract; logs presence+shape only)
- [ ] Scratch verification: provision a worktree THROUGH THE ENGINE'S OWN PATH (a `SPINE_WORKER_STUB=1` stub batch on a throwaway scope, or the engine's provisioning call directly — T-6 discipline: stub and real runs are separate; abort+clean the stub artifacts) and show the patches present in the fresh lane BEFORE any worker runs (verify.mjs exit 0 inside the lane)
- [ ] Negative control: a worktree provisioned with the hook disabled/absent shows the old unpatched state (falsifiable contrast)

### Step 3: Evidence + pre-completion consult

- [ ] Write `spine-tasks/SP-039-worktree-patch-hook/record.md` (archaeology, mechanism decision + rejected alternatives, hook contract, scratch transcripts, the named post-land gate = first real 2-lane wave with zero mid-task verify reds, consult verdicts + ACTUAL answering models, engine-review presence, durable-lesson candidates)
- [ ] **Pre-completion solo consult** (same route discipline as Step 1) on the evidence + diff; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 — after the ONE recorded manual `apply.mjs` remediation per honesty framing (c) + build 0W/0E + both test projects green; counts EXACTLY the 492/29 floor, zero product change)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Every fresh lane worktree arrives with the patch manifest applied before any worker runs (scratch-verified through the engine's own provisioning path, with negative control)
- Hook is idempotent + fail-safe (exit semantics match the engine contract; provisioning never blocked by the hook)
- Named post-land gate recorded: the first real 2-lane wave lands with zero mid-task verify.mjs reds (row reopens if it reds)
- record.md carries the timing evidence, mechanism decision + rejected alternatives, both solo consult verdicts with actual answering models, engine-review presence per call

## Do NOT

- Assume the hook timing (decide from engine source evidence — honesty framing (a)); break or slow worktree provisioning (fail-safe first); edit the engine's own files (the seam is config/scripts the engine CALLS, never engine source — the T-1 manifest owns engine patches); edit `client/**`, `ConditioningControlPanel/**`, `.pi/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2); set any board row state; claim the fix proven without the scratch verification + named gate
- Use `consult` council mode (T-7: council unproven; `kimi-api` provider unregistered on this laptop — solo only, Opus 5 main / Fable 5 fallback per the 2026-08-04 rewire)

## Git Commit Convention

- `feat(SP-039): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `spine-tasks/SP-039-worktree-patch-hook/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-08-04 (authoring, orchestrator): **T-14 row filed at the wave-4 land (3rd mid-task re-patch occurrence in one day; recurrence rule).** Timing question left open deliberately — the packet's Step 1 decides the seam from engine evidence. Zero-product-change tooling packet; contract floor EXACTLY 492/29. **`## Review Level: 2` heading present + grep-verified ≥2 (SP-034 authoring rule).**
- 2026-08-04 (authoring, orchestrator): Launch: validate → analyze → plan → preflight → detached wave batch (SP-038 + SP-039, 2 lanes) per owner cycle.
