# Task: SP-020 — durable pi-spine local-patch mechanism (T-1)

## Mission

Execute `client/docs/task-board.md` row **"T-1 (tooling): durable pi-spine local-patch mechanism"** (P2, FINAL row of Phase 4 in `spine-tasks/CONTEXT.md`): provide one checked-in patch manifest + apply script, re-applied after any pi-spine install/update; evidence: fresh `npm i pi-spine` followed by patch application and a green `spine preflight` + evidence command validation + a batch whose worker tail exceeds 16KB. Deliverable: `.spine/patches/` (checked-in manifest + apply script + verify script + README) proven against a SCRATCH install — **the worker NEVER touches the repo's real `.pi/npm`** (reinstalling the engine under a running engine is banned; the real reinstall is the post-land orchestrator gate, named in the packet).

**Honesty framings (binding):** (a) the inventory must be EMPIRICAL — diff the live `.pi/npm/node_modules/pi-spine` against a pristine scratch install and classify every delta (patch vs noise); never transcribe the T-row texts as if they were the ground truth of what's applied; the `windowsHide` spawn mass-patch (86 sites, CLEARED as a T-2 suspect) must be recorded for presence and load-bearing status, not assumed; (b) patches must be **anchor-based** (unique surrounding content), never line-number-based — a version bump must fail loudly, not mis-patch; (c) **"re-applied automatically after any install/update" is scoped honestly:** full npm-hook automation is likely infeasible without engine modification — the durable shape is apply + verify scripts + a loud missing-patch check + runbook wiring; any residual manual trigger is recorded as a named limit, never papered over; (d) the real `.pi/npm` reinstall evidence is **post-land orchestrator-side with the run parked** (a worker reinstalling the engine that hosts it is incoherent) — the packet's in-lane evidence is the full scratch cycle; (e) scratch work happens OUTSIDE the repo (`%TEMP%/sp020-scratch`) — never inside the lane (`.pi/npm` auto-install into a worktree = the T-5 trap).

## Dependencies

- **Task:** SP-019 (Phase-4 serial chain; owner decision 2026-07-21: author now)

## Context to Read First

- `client/docs/task-board.md` — the T-1 row (three named load-bearing patches + evidence shape) + T-12 row (the 4th candidate: merge-time tracked-ignored set-minus — evaluate whether a local patch is feasible/safe to include or stays upstream) + T-5/T-8 rows (context only)
- `client/docs/port-lessons.md` — the patch fragility lines (fsync, dotnet allowlist, worker-runner `@file`, 32KB CreateProcess root cause, reinstall-kills-patches warnings)
- `.pi/npm/node_modules/pi-spine/` — the live patched tree (READ for inventory diffing only; the worker never writes to the repo's `.pi/`)
- `.spine/spine-config.json` — engine config context (untouched by this task)
- Required skills: none beyond the standing referenceDocs (this is engine-tooling archaeology, not WPF/port work)

## File Scope

- `.spine/patches/**` (manifest + apply + verify + README + patch fixtures if any)
- `client/docs/task-board.md` (T-1 row evidence edit only)
- `spine-tasks/SP-020-spine-patch-mechanism/**` (STATUS.md, record.md, evidence, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `.spine/patches/manifest.json`, `.spine/patches/README.md` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/src/**`, `client/tests/**`, `client/CcpClient.sln`, `client/spikes/**`, `.pi/**`, `.spine/spine-config.json`, `spine-tasks/CONTEXT.md` |
| artifactsMustExist | `.spine/patches/manifest.json`, `.spine/patches/apply.mjs`, `.spine/patches/verify.mjs`, `.spine/patches/README.md`, `spine-tasks/SP-020-spine-patch-mechanism/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md.

## Steps

### Step 1: Empirical patch inventory + pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] Pristine scratch install (`%TEMP%/sp020-scratch`, `npm i pi-spine` — record the EXACT installed version) and full recursive diff vs the repo's live `.pi/npm/node_modules/pi-spine` — classify EVERY delta: fsync `"r"`→`"r+"` (which files), `dotnet` gate-evidence allowlist (evidence-command.mjs), worker-runner `@file` tail (bin/spine-worker-runner.mjs), windowsHide mass-patch (presence? which files? load-bearing?), any other delta found (version drift between live and fresh install must be separated from intentional patches — record the live install version vs fresh version)
- [ ] T-12 candidate evaluation: is a local anchor-patch of the merge-time tracked-ignored scan (set-minus vs merge target) feasible and safe, or does it stay upstream? Record the decision with the scan's code location
- [ ] **Pre-approach solo consult** (Fable 5, solo; council unavailable T-7) with the inventory + manifest design (anchor-based patches, apply/verify split, honest automation scope, scratch-cycle evidence plan, post-land real-reinstall gate); verdict text + ACTUAL answering model in record.md BEFORE checkbox. Keep questions few/pointed

### Step 2: Manifest + apply + verify

- [ ] `.spine/patches/manifest.json` — machine-readable: per patch — id, target file(s) (repo-relative under `.pi/npm/node_modules/pi-spine/`), anchor (unique content block), replacement, rationale (link to the T-row/lesson), version-tested-against
- [ ] `.spine/patches/apply.mjs` — node script: applies every manifest patch idempotently (re-running is a no-op when already applied), FAILS LOUDLY with a named patch id when an anchor is missing (version drift), never partial-applies silently (all-or-nothing with rollback note)
- [ ] `.spine/patches/verify.mjs` — node script: reports per-patch applied/missing/drifted; exit 0 only when all applied; designed to be run after any pi-spine install/update and before any batch
- [ ] `.spine/patches/README.md` — what/why/how: the fragility problem (patches die on reinstall), the mechanism, the re-apply trigger (after ANY `pi install`/npm change to pi-spine: `node .spine/patches/apply.mjs && node .spine/patches/verify.mjs`), the honest automation limit (named limit if full automation is infeasible), links to rows

### Step 3: Scratch-cycle evidence (full reinstall simulation, OUTSIDE the repo)

- [ ] In `%TEMP%/sp020-scratch2`: fresh `npm i pi-spine` (confirm patches ABSENT via verify against the pristine tree — negative control) → `apply.mjs` → `verify.mjs` exit 0 → **scratch `spine preflight` GREEN** (run the scratch spine against the scratch install) → **evidence-command validation** (the dotnet-allowlist patch proven: the gate-evidence executable allowlist accepts a dotnet command) → **a STUB batch (`SPINE_WORKER_STUB=1`) in the scratch project whose worker tail exceeds 16KB** (the `@file` worker-runner patch proven end-to-end — the 32KB CreateProcess failure mode cannot regress silently); every step's output in record.md
- [ ] Idempotence proven: apply → apply again → verify (no double-patch, no error)

### Step 4: Board reconciliation + record + pre-completion consult

- [ ] Write `spine-tasks/SP-020-spine-patch-mechanism/record.md` (inventory table, T-12 include/exclude decision, consult verdicts + ACTUAL answering models, scratch-cycle transcripts, budgets, surprises)
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the mechanism + evidence; verdict text in record.md
- [ ] Update `client/docs/task-board.md` T-1 row → `WIP` with evidence + named limits (**real `.pi/npm` reinstall = post-land orchestrator gate with the run parked; full npm-hook automation if infeasible; T-12 local-patch decision**) — row never `DONE`
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (client build 0W/0E + both test projects green — pollution guard; the mechanism itself is node scripts with their scratch-cycle evidence)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Empirical inventory table covers EVERY live-vs-pristine delta with patch/noise classification (incl. windowsHide presence + load-bearing status, version drift)
- `.spine/patches/` checked-in: anchor-based manifest, idempotent loud-failing apply, verify, README with re-apply trigger + honest automation limit
- Scratch cycle proven twice over (fresh install → negative control → apply → verify → scratch preflight GREEN → dotnet allowlist proven → >16KB-tail stub batch → idempotent re-apply)
- T-12 local-patch decision recorded with code location; board row `WIP` (not `DONE`) with the post-land real-reinstall gate named; both solo Fable consults persisted with actual answering models

## Do NOT

- Touch the repo's real `.pi/**` (worker never reinstalls, never writes there — scratch only); reinstall the engine under the running batch; use line-number-based patches; transcribe T-row texts without the empirical diff; claim full npm-hook automation if it isn't delivered (name the limit); modify `client/**`, `ConditioningControlPanel/**`, `.spine/spine-config.json`, `spine-tasks/CONTEXT.md`, `AGENTS.md`, `CLAUDE.md`, `.gitnexus/`; set any board row `DONE`
- Use `consult` council mode (route broken — solo Fable 5 only)

## Git Commit Convention

- `feat(SP-020): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `.spine/patches/README.md` (deliverable), `client/docs/task-board.md` (T-1 row evidence), `spine-tasks/SP-020-spine-patch-mechanism/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (durable surprises only — **UTF-8 only**)

## Amendments

- 2026-07-21 (authoring): **owner decision (ask_user): author SP-020 now** (over skip-to-close-out). Phase 4 consult verdicts applied: optional tail confirmed as legitimate scope; evidence shaped as in-packet scratch cycle + post-land orchestrator real-reinstall gate (worker never touches real `.pi/npm` — engine-under-itself reinstall banned); T-12 local-patch feasibility is an inventory-driven decision, not assumed.
- 2026-07-21 (authoring): `## Review Level: 2` structured heading emitted (T-2 fixed format). Launch: validate → analyze → plan → preflight → detached batch per owner cycle.
- 2026-07-21 (contract fix, applied on-lane): `fileScopeMustNotChange` narrowed from blanket `client/**` to `client/src/**`, `client/tests/**`, `client/CcpClient.sln`, `client/spikes/**` — the blanket ban contradicted the declared-scope `client/docs/task-board.md` row edit + Check-If-Affected port-lessons convention (contract_failed ×2; the engine re-verifies against the LANE packet copy). Identical fix committed to base.
