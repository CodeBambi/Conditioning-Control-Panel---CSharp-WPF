# Task: SP-031 — T-5 anchor re-base: fix the .reviews/ deletion path (provenance-faithful fixture)

## Mission

Fix the `t5-reviews-autoclean` manifest patch that **failed its named post-land gate** on its first live use (wave `20260722T101444` lane-2, 10:46:27Z): the patch deletes `path.join(taskFolder, ".reviews")`, but the engine's `taskFolder` at the `commitLaneWorktree` finalization call is the **BASE-repo packet path** while `.reviews/` is written to the **LANE WORKTREE's** task folder — the deletion no-ops. Re-base the anchor so the deletion targets the lane worktree's task folder, with a fixture that **mirrors the engine's real parameter provenance** (the first fixture's taskFolder was fixture-local — the exact flaw that let the wrong patch pass). Closes board row T-5 (REOPENED 2026-07-22) for the second and final time.

**Honesty framings (binding):** (a) the failure evidence is primary — `spine-tasks/SP-030-ai-companion-admission` batch journal + the wave-1 gate-history entry + the port-lessons fixture-provenance entry; the fix must address the PROVEN failure mode, not a theorized one; (b) the live install currently carries the WRONG patch line (applied 2026-07-22 post-SP-028-land) — the re-base path must restore/replace it cleanly on the live tree AND keep the manifest's anchor valid for pristine installs (the all-or-nothing apply fails loudly on a mismatched anchor — the migration path is a Step-1 design item); (c) the fixture this time MUST replicate the real call: `taskFolder` = a base-shaped packet path OUTSIDE the worktree, `.reviews/` residue INSIDE the worktree's packet folder — plus the original negative control (pristine engine still fails); (d) **ENABLER 2: the worker does NOT edit `client/docs/task-board.md` or `client/docs/port-lessons.md`** — record in record.md; the orchestrator reconciles at land; (e) the REAL proof remains the next Level-2 batch landing without the manual recovery — the named post-land gate gets re-pointed at the NEXT wave after this lands.

## Dependencies

- **Task:** SP-030 (wave 1 landed; the failure evidence's source batch)

## Context to Read First

- `.spine/patches/manifest.json` (`t5-reviews-autoclean` entry — the wrong replacement text) + `README.md` + `apply.mjs`/`verify.mjs` semantics (all-or-nothing, idempotence-on-replacement-present, loud-on-missing-anchor)
- `.pi/npm/node_modules/pi-spine/src/batch/lane-commit.mjs:245-253` (the live wrong line + its surrounding anchor context)
- `.pi/npm/node_modules/pi-spine/src/batch/engine-lanes/commit.mjs` (`commitLaneAndValidateWorktree` — the call site: what `worktreePath`, `projectRoot`, `taskFolder` actually are at finalization; batch-state provenance: `tasks[].taskFolder` = BASE-repo packet path, journal `artifactPath` = LANE worktree path)
- `spine-tasks/SP-028-t5-reviews-autoclean/record.md` (the original design + its fixture's flaw) + `spine-tasks/SP-028-t5-reviews-autoclean/evidence/fixture-t5.mjs` (the fixture to correct)
- `client/docs/port-lessons.md` 2026-07-22 fixture-provenance entry + the wave-1 gate-history entry (failure evidence)
- `client/docs/task-board.md` T-5 row (REOPENED text)

## File Scope

- `.spine/patches/**` (manifest entry re-base + README row + fixture asset if it lives here)
- `spine-tasks/SP-031-t5-anchor-rebase/**` (STATUS.md, record.md, evidence, .DONE)
- **`client/docs/task-board.md` and `client/docs/port-lessons.md` are NOT in scope (enabler 2 — orchestrator writes them at land).**
- Note: the live engine file (`.pi/npm/node_modules/pi-spine/src/batch/lane-commit.mjs`) is gitignored — the patch does not ride the git delta (SP-028 deviation recorded); the worker modifies it via the apply/migration path, and the state is proven by `verify.mjs`.

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `.spine/patches/manifest.json` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/runtime/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md` |
| artifactsMustExist | `spine-tasks/SP-031-t5-anchor-rebase/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md.

## Steps

### Step 1: Failure forensics + re-base design + migration path + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] Forensics: the exact `commitLaneWorktree` signature + the real values of `worktreePath`/`projectRoot`/`taskFolder` at a finalization call (journal + batch-state evidence, not assumptions); the correct lane-task-folder expression (e.g., `path.join(worktreePath, path.relative(projectRoot, taskFolder), ".reviews")` — with the edge cases enumerated: taskFolder already inside worktreePath, projectRoot absent/eq worktreePath, relative escaping)
- [ ] Design: the new anchor + replacement text (anchor must match the PRISTINE engine text so pristine installs apply cleanly); the migration path for the LIVE tree carrying the wrong line (documented one-line restore → apply new → verify; or a manifest supersedure mechanism if apply.mjs already supports one — check, don't invent); re-point the named post-land gate at the next Level-2 wave
- [ ] **Pre-approach solo consult** (Fable 5, solo) with the forensics + design; verdict text + ACTUAL answering model in record.md BEFORE checkbox. Keep questions few/pointed

### Step 2: Manifest re-base + live-tree migration + apply/verify

- [ ] Manifest entry re-based (new anchor from pristine text, new replacement with the corrected path expression, `// ponytail:` comment updated to cite the gate failure + fix, rationale updated)
- [ ] Live-tree migration executed per the Step-1 design (wrong line → correct line on `.pi/npm/node_modules/pi-spine/src/batch/lane-commit.mjs`, documented); `node .spine/patches/apply.mjs && node .spine/patches/verify.mjs` exit 0; idempotence proof; loud-failure proof on a scratch drift copy

### Step 3: Provenance-faithful fixture + regression proof

- [ ] Fixture v2: `taskFolder` OUTSIDE the worktree (base-shaped), `.reviews/` residue INSIDE the worktree's packet folder → patched engine passes; pristine engine fails (negative control preserved); the ORIGINAL fixture's taskFolder-inside-worktree case ALSO still passes (no regression for the resume-path callers whose taskFolder shapes differ — enumerate from the engine source)
- [ ] Consumer census re-confirmed for the corrected expression (the 4 callers from SP-028 + the relative-path edge cases)
- [ ] The boundary re-recorded: in-lane proof = fixture v2 + negative control; REAL proof = next Level-2 wave's finalizations skip the manual recovery (named post-land gate, re-pointed)

### Step 4: Docs + pre-completion consult

- [ ] `.spine/patches/README.md` row updated (the re-base note)
- [ ] Write `spine-tasks/SP-031-t5-anchor-rebase/record.md` (forensics, design, migration transcript, fixture v2, consult verdicts + ACTUAL answering models, engine-review presence, durable-lesson candidates)
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the re-based patch + fixture; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (verify.mjs exit 0 + build 0W/0E + both test projects green — counts ≥ the 412/29 floor; client tree untouched, no drift)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Manifest re-based; live tree migrated; apply/verify exit 0, idempotent, loud-on-drift
- Fixture v2 proves the REAL provenance case (base-shaped taskFolder, lane residue) with the pristine negative control preserved and no caller regressions
- Named post-land gate re-pointed at the next Level-2 wave
- Both solo Fable consults persisted with actual answering models; contract green (≥412/29 floor, no drift)

## Do NOT

- Patch beyond the single line/expression (T-12 stays excluded); invent a supersedure mechanism without checking apply.mjs first; claim the tax is gone before the re-pointed post-land gate proves it; edit `client/docs/task-board.md` or `client/docs/port-lessons.md` (enabler 2); modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/runtime/**`; set any board row state
- Use `consult` council mode (route broken — solo Fable 5 only)

## Git Commit Convention

- `feat(SP-031): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `.spine/patches/README.md`, `spine-tasks/SP-031-t5-anchor-rebase/record.md`
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md` (enabler 2 — orchestrator reconciles at land)

## Amendments

- 2026-07-22 (authoring): **T-5 row REOPENED at the wave-1 live gate** (batch `20260722T101444` lane-2 DirtyWorktree 10:46:27Z; lane-1 11:55:07Z) — this packet is the fix. Failure root cause: `taskFolder` = BASE-repo packet path at the finalization call; `.reviews/` = lane-worktree artifact. Fixture-provenance lesson encoded as a hard requirement (fixture v2 mirrors the real call). Enabler 2 encoded (no hot docs in worker scope). Headless tooling packet; 4h budget exported for consistency.
- 2026-07-22 (authoring): `## Review Level: 2` structured heading emitted (T-2 fixed format). Launch: validate → analyze → plan → preflight → detached wave batch (SP-031 + SP-032, 2 lanes) per owner cycle.
