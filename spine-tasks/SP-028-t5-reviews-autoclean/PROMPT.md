# Task: SP-028 — T-5 local anchor-patch: eliminate the .reviews/ DirtyWorktree land tax (parallelism enabler 1)

## Mission

Deliver the **T-5 local anchor-patch** through the SP-020 manifest mechanism (`.spine/patches/`): the spine finalization stage deterministically fails every Level-2 batch with `DirtyWorktree` because the engine's own review stage writes `.reviews/` artifacts into the lane task folder AFTER the worker's last commit, and the post-commit porcelain check does not exclude them — **15 consecutive occurrences, each forcing the same manual orchestrator recovery** (read verdicts from journal → delete `.reviews/` → retry fast-path → manual orch ff → hand-written gate record). This is the owner-approved **parallelism enabler 1** (Engram decision #215): the manual land tax is the single-threaded bottleneck; killing it makes lands gate-only and is prerequisite to 2-lane waves (enabler 2 = board-row edits move to orchestrator-only — a packet-authoring convention the orchestrator applies from SP-029 onward, NOT part of this task).

**Honesty framings (binding):** (a) the manifest's **strictly-load-bearing admission rule** is satisfied by 15/15 deterministic occurrences — but the patch itself must be the MINIMAL shape; the T-12 lesson (shared-helper patches are unsafe) frames the design choice: patching the shared porcelain check (`resolvePostLaneCommitPorcelain`) touches code other consumers use, while deleting `.reviews/` in `commitLaneAndValidateWorktree` before the re-check mirrors the 15×-proven manual step in one contained function — the Step-1 consult decides WITH the consumer analysis; (b) verdicts are journal-durable (every land reads them from the journal, never from the files) — deleting `.reviews/` at finalization loses nothing, but the patch must run AFTER verdict recording, never before; (c) the REAL proof is the NEXT Level-2 batch landing without the manual recovery — this packet's in-lane proof is fixture + historical derivation; the boundary is recorded honestly; (d) the patch is committed to the repo (the engine tree is tracked) AND re-applied to the live install via the manifest (apply+verify idempotence).

## Dependencies

- **Task:** SP-027 (DTRH host slice cut complete; no product dependency — sequencing only)

## Context to Read First

- `.spine/patches/README.md` + `.spine/patches/manifest.json` + `apply.mjs`/`verify.mjs` (the SP-020 mechanism, its admission rule, its testedVersions convention, its honest automation limit)
- `spine-tasks/SP-020-spine-patch-mechanism/record.md` (the mechanism's delivery evidence + the T-12 excluded-patch analysis — the shared-helper risk framing this task inherits)
- The T-5 call chain (READ-ONLY, live tree): `.pi/npm/node_modules/pi-spine/src/batch/engine-lanes/commit.mjs` (`commitLaneAndValidateWorktree` — laneCommit → `resolvePostLaneCommitPorcelain` → DirtyWorktree), `.pi/npm/node_modules/pi-spine/src/batch/lane-dirty-check.mjs` AND `lane-dirty-check-commit.mjs` (**TWO copies of `resolvePostLaneCommitPorcelain` exist — determine which is the live import and whether the other is dead**), `.pi/npm/node_modules/pi-spine/src/batch/review-artifacts.mjs` + `contract-exec.mjs` (where `.reviews/` gets written — AFTER verdicts, before the final dirty check), `.pi/npm/node_modules/pi-spine/src/batch/review-scope.mjs:22` (the engine ALREADY excludes `.reviews/` from review scoping — precedent for treating it as engine-internal)
- The 15 journal T-5 occurrences (`.spine/runtime/*/journal/events.jsonl` — `task.failed` classification `DirtyWorktree` after `review.completed` final PASS; derive the count + that every one had `.reviews/` as the residue)
- `client/docs/port-lessons.md` T-5 entries + `client/docs/task-board.md` tooling rows (T-1 patch mechanism, T-12 excluded-patch decision, the T-5 occurrences recorded in gate history)

## File Scope

- `.spine/patches/**` (manifest entry + any README row)
- `.pi/npm/node_modules/pi-spine/src/batch/**` (the patch target — the LIVE engine file, committed to the repo)
- `client/docs/task-board.md` (row evidence edit only)
- `client/docs/port-lessons.md` (the T-5-closed entry)
- `spine-tasks/SP-028-t5-reviews-autoclean/**` (STATUS.md, record.md, evidence, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `.spine/patches/manifest.json` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/runtime/**` |
| artifactsMustExist | `spine-tasks/SP-028-t5-reviews-autoclean/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md.

## Steps

### Step 1: T-5 call-chain archaeology + patch-shape design + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] Call-chain archaeology (READ-ONLY): which `resolvePostLaneCommitPorcelain` copy is live (trace the import in `commit.mjs`; dead-copy determination with evidence); where/when `.reviews/` is written relative to verdict recording and the dirty check (event-order proof from a recent journal); why the lane auto-commit does not sweep it (ignorePatterns/scope analysis); the `review-scope.mjs:22` precedent
- [ ] Historical derivation: enumerate the T-5 occurrences across `.spine/runtime/*/journal/` (count, every one `.reviews/`-residue, every one recovered by the same manual step) — the strictly-load-bearing admission evidence
- [ ] Design: the patch shape — (a) teach the porcelain check to exclude `.reviews/` (shared-helper risk per T-12: consumer census required) vs (b) delete `.reviews/` in `commitLaneAndValidateWorktree` after verdicts, before the re-check (mirrors the 15×-proven manual step; one contained function) — with the exact anchor + replacement text drafted from the live 2.10.0 tree
- [ ] **Pre-approach solo consult** (Fable 5, solo) with the archaeology + both shapes + the draft anchor; verdict text + ACTUAL answering model in record.md BEFORE checkbox

### Step 2: Manifest entry + apply/verify on the live install

- [ ] Author the manifest patch (id e.g. `t5-reviews-autoclean`): anchor byte-exact from the live tree, replacement with the `// ponytail:` comment convention, rationale citing the 15 occurrences, `testedVersions` (verify against the versions recorded in the manifest's convention — if 2.8.0's tree is not honestly verifiable, record 2.10.0-only with the reason)
- [ ] `node .spine/patches/apply.mjs && node .spine/patches/verify.mjs` on the live install: applied, exit 0; **idempotence proof** (second apply = no-op); **loud-failure proof** (corrupt a COPY of the anchor in scratch → verify reports drifted/missing)
- [ ] Commit includes the patched live engine file (repo tracks the engine tree)

### Step 3: Fixture + historical proof

- [ ] Fixture: scratch lane worktree with `.reviews/` residue → the patched finalization path passes; pristine engine (patch reverted in scratch) → fails (negative control); `.reviews/` deletion runs AFTER verdict recording, never before (event-order assertion)
- [ ] Regression census: every consumer of the patched function enumerated with the no-regression argument (T-12 discipline — if the shared-check shape (a) was chosen, prove each consumer's behavior unchanged; if (b), prove the insertion point is finalization-only)
- [ ] The boundary recorded: in-lane proof = fixture + historical derivation; the REAL proof = the next Level-2 batch landing without the manual recovery (recorded as the packet's named post-land gate for the orchestrator)

### Step 4: Docs + board reconciliation + pre-completion consult

- [ ] `.spine/patches/README.md` patch-table row
- [ ] `client/docs/port-lessons.md` entry (T-5 closed by local anchor-patch; the design choice + the proof boundary)
- [ ] `client/docs/task-board.md` tooling row update (T-5 → CLOSED-by-patch with the named post-land gate: first Level-2 batch after this lands must skip the manual recovery — if it still T-5s, the patch is wrong and the row reopens)
- [ ] Write `spine-tasks/SP-028-t5-reviews-autoclean/record.md` (archaeology, design, consult verdicts + ACTUAL answering models, engine-review presence, fixture transcripts, proof boundary)
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the patch + proofs; verdict text in record.md
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (`verify.mjs` exit 0 + client build 0W/0E + both test projects green — counts ≥ the 391/29 floor; the client tree is untouched, counts must not drift)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Manifest patch authored, applied, verified, idempotent, loud-on-drift; patched live engine file committed
- Fixture proof (patched passes / pristine fails) + historical derivation (all occurrences enumerated) + consumer census with no-regression argument
- The proof boundary honestly recorded (real proof = next Level-2 land skips the manual recovery — named post-land gate in the board row)
- Board/port-lessons/README updated; contract green (client floor 391/29, no drift); both solo Fable consults persisted with actual answering models

## Do NOT

- Patch beyond the single T-5 site (T-12 stays excluded — shared merge-time scan is unsafe per SP-020's analysis); touch `resolvePostLaneCommitPorcelain`'s OTHER consumers without the census; automate the reinstall trigger (SP-020's honest automation limit stands — apply/verify stays a named manual step); claim the tax is gone before the next Level-2 land proves it (the named post-land gate); modify `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/spikes/**`, `.spine/runtime/**`; set any board row `DONE`
- Use `consult` council mode (route broken — solo Fable 5 only)

## Git Commit Convention

- `feat(SP-028): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `.spine/patches/README.md`, `client/docs/task-board.md`, `client/docs/port-lessons.md`, `spine-tasks/SP-028-t5-reviews-autoclean/record.md`

## Amendments

- 2026-07-22 (authoring): **owner-approved parallelism plan (Engram #215) enabler 1.** T-5 evidence base: 15 consecutive deterministic `.reviews/` DirtyWorktree finalization failures (SP-015…SP-027 gate history), each recovered by the identical manual step (journal-read → delete `.reviews/` → retry → orch ff → gate record). Enabler 2 (board-row edits → orchestrator-only) is a packet-authoring convention applied from SP-029 — NOT this task. Headless packet (no DISPLAY3 step); mustNotChange intersected against File Scope at authoring (SP-020 lesson). Contract testCommand leads with `verify.mjs` (the patch state is part of the contract). T-11 sizing: no headed step; standard worker timeout (no SPINE_WORKER_PI_TIMEOUT_MS override needed — but orchestrator exports the 4h budget anyway for consistency).
- 2026-07-22 (authoring): `## Review Level: 2` structured heading emitted (T-2 fixed format). Launch: validate → analyze → plan → preflight → detached batch per owner cycle.
