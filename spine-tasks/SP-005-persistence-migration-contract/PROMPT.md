# Task: SP-005 — persistence and migration contract

## Mission

Execute `client/docs/task-board.md` row 4 (**"Define persistence and migration contract"**, P0, Phase 1 of `spine-tasks/CONTEXT.md`) against the landed SP-003/SP-004 lifecycle. Deliver `client/docs/persistence-migration-contract.md` plus its minimal implementation in `client/src/CcpClient.Desktop/Persistence/` with tests: **one schema/version authority, atomic temp-file+rename write with flush, a single serialized writer (built on SP-004's OperationRegistry), corruption quarantine with a typed Degraded outcome (never silent defaults), an unknown-member preserve policy, a migration journal with idempotent migrations, settings-object replacement notification with a documented delivery context, secret exclusion (seam only), an import/backup boundary, and failure-injection tests.** The contract instantiates the proposal (§6 row-4 column of `client/docs/architecture-proposal.md`), decides the serializer (System.Text.Json vs Newtonsoft) with reasons, and **activates the teardown-flush ordering guarantee SP-003 reserved for this row**. It does **not** implement feature settings — one demonstrator settings model exercises the whole contract.

## Dependencies

- **Task:** SP-004 (OperationRegistry/typed outcomes are the writer-ownership primitive; SP-003's guarded teardown carries the reserved flush position this task activates)

## Context to Read First

- `client/docs/startup-shutdown-contract.md` — teardown sequence with the RESERVED settings-flush position ("Settings-flush-before-disposal ordering belongs to row 4"); panic-path policy
- `client/docs/async-lifecycle-fault-contract.md` — operation ownership, typed `OperationOutcome` vocabulary (quarantine/recovery surfaces as `Degraded`), delivery-context documentation rule (the replacement notification must follow it)
- `client/docs/architecture-proposal.md` — §6 row-4 deferred topics (serializer choice, atomic write/debounce/backup/quarantine parity); §2 Core-library deferral rule
- `client/docs/first-attempt-systemic-lessons.md` — persistence lesson (ACCEPT atomic replacement / corruption preservation / explicit migration / crash recovery; REJECT scattered saves, detached unordered writes, silent defaults over unreadable user data; commit-history evidence `b694b543`, `a2d1b9a8`, `e9501ce8`, `03d91c86`, `750d2615`, `f403261d`/`eeef31e2`)
- `client/docs/architecture.md` — A-014 explicit foundation; A-001 composition boundaries
- `spine-tasks/CONTEXT.md` — Phase 1 scope and execution policy
- WPF behavioral evidence (read-only): `ConditioningControlPanel/CCP.Core/Services/Settings/SettingsService.cs` — `:70-190` (recovery, partial-member parsing, corruption quarantine, migrations), `:374-444` (debounced temp-file replacement, whole-object replacement, `CurrentReplaced` event), `:3208` (settings flush FIRST in teardown). Extract outcomes, not mechanics. Load skill `wpf-parity` for this.
- Required skills: load `port-feature` and `avalonia-research` before Step 1; `wpf-parity` when citing WPF behavior

## File Scope

- `client/src/CcpClient.Desktop/**` (Persistence/ implementation; teardown wiring in Lifecycle/Program)
- `client/tests/CcpClient.Tests/**` (persistence tests)
- `client/docs/persistence-migration-contract.md` (contract deliverable)
- `client/docs/startup-shutdown-contract.md` (reserved-guarantee activation note ONLY — one minimal edit marking the flush position activated, cited in record.md)
- `client/docs/task-board.md` (row-4 evidence edit only)
- `spine-tasks/SP-005-persistence-migration-contract/**` (STATUS.md, record.md, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/docs/persistence-migration-contract.md`, `client/src/CcpClient.Desktop/Persistence/` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `.spine/**` |
| artifactsMustExist | `client/docs/persistence-migration-contract.md`, `spine-tasks/SP-005-persistence-migration-contract/record.md` |

**Review Level 2 (plan + code)** — this contract protects user data and gates every feature settings model. Call `spine_review_step` after each step. Engine reviews are empirically dead (zero reviews in SP-001…SP-004 — diagnostic row T-2 open); if `spine_review_step` returns skipped, record that fact in record.md and rely on the mandatory Fable consults instead. Do not stall waiting for a reviewer.

## Steps

### Step 1: Pre-approach consult and contract draft

- [ ] Run a **pre-approach solo consult** (Fable 5 via `consult` tool, mode solo) with the planned contract outline (schema authority, atomic write shape, quarantine/recovery policy, migration journal, replacement notification, serializer decision, teardown flush wiring, panic-path flush policy); record the verdict text in record.md **before** marking the checkbox (write-then-check — SP-002's recorded gap)
- [ ] Update STATUS.md Step 1 checkboxes as you work (before, not after)
- [ ] Digest WPF persistence outcomes (atomic replacement, quarantine, migration history, replacement notification, flush-first teardown — outcomes only, no mechanics transplant) into a short evidence note inside record.md
- [ ] Write `client/docs/persistence-migration-contract.md`: one schema/version authority; atomic temp-file+rename write with flush; single serialized writer via SP-004's OperationRegistry; corruption quarantine (preserve original bytes, typed `Degraded`, defaults only with an explicit user-visible flag — never silent); unknown-member policy (preserve, never strip); migration journal + idempotence; settings-object replacement notification with **documented delivery context per SP-004's contract** (raised on writer context; UI projection goes through `IUiDispatch`); secret exclusion (no secrets in the settings file — named seam, store implementation deferred); import/backup boundary; **serializer decision: System.Text.Json with the explicit tolerance stack** — parse to `JsonNode` first (version checks + migrations at DOM level, tolerant of unknown shape), `[JsonExtensionData]` round-trips unknown members, bind failure → quarantine + `Degraded`; record Newtonsoft's rejection (new package admission; its per-member salvage compensated for schema churn the migration journal replaces) with a revisit trigger; **teardown flush ordering** (flush at SP-003's reserved position BEFORE participants stop/dispose) and a stated **panic-path flush policy** (attempt or deliberate no-flush — decide, state, never leave silent); **debounce policy stated but NOT implemented** (`Save`/`SaveImmediate` only — debounce arrives with feature-scale churn); **Core-library deferral** recorded with revisit trigger ("first second-assembly consumer")

### Step 2: Persistence store implementation

- [ ] Implement the minimal store in `Persistence/` (no abstractions without a consumer): schema-versioned document model with `JsonNode` DOM-level version read; atomic writer (temp file + rename + flush, failure-injection points); single serialized writer registered as an SP-004 operation (owned completion, typed outcomes); quarantine path (corrupt original moved aside preserved, never deleted)
- [ ] One **demonstrator settings model** (not a feature model) exercising the full contract; one **demonstrator v0→v1 migration** with journal entry — proving the machinery, not building a framework
- [ ] `Save`/`SaveImmediate` only — **no debounce timer** (policy-only per the contract); no secret-store implementation (seam only)
- [ ] Unit tests: corrupt file → quarantine + typed `Degraded` + flagged defaults (original bytes preserved); simulated crash mid-rename → recovery on next load; unknown member round-trips (preserve, never strip); migration idempotence (run twice → same result, one journal entry); concurrent writes serialized (no interleaved/partial file); replacement notification fires on whole-object replacement with the documented delivery context

### Step 3: Teardown flush wiring and activation note

- [ ] Wire the store's flush into SP-003's guarded teardown at the reserved position — flush completes BEFORE participants stop/dispose (WPF `:3208` outcome), flowing through the single guarded entry point (no second teardown path)
- [ ] Test: dirty-settings-at-shutdown are flushed before reverse-order stop executes; repeated shutdown still a no-op (SP-003 invariants intact — all SP-003/SP-004 tests still pass)
- [ ] Make the minimal activation edit to `client/docs/startup-shutdown-contract.md` (reserved flush position → activated, citing this task) and record the edit in record.md
- [ ] **WSL2 Linux verification** (this slice is file-I/O — rename/flush semantics differ by platform; SP-002 proved WSL2 Ubuntu + dotnet SDK 10 works): run the contract testCommand under WSL2 and record the result in record.md. If WSL2 is genuinely unavailable, record the exact blocker instead — do not skip silently (constitution honesty rule)

### Step 4: Evidence, board reconciliation, pre-completion consult

- [ ] Write `spine-tasks/SP-005-persistence-migration-contract/record.md`: contract summary, serializer decision + reasons, Core-deferral decision, panic-path flush policy, WPF evidence digest, consult verdicts, test output (Windows AND WSL2), surprises. **Also record engine-review presence/absence** (pipeline empirically dead — row T-2; if `spine_review_step` returned skipped, say so)
- [ ] Run a **pre-completion solo consult** (Fable 5, solo) on the diff and contract; record the verdict text in record.md
- [ ] Update `client/docs/task-board.md` row **"Define persistence and migration contract"** to `WIP` with evidence text citing record.md — never `DONE` (orchestrator flips at land after evidence review)
- [ ] Update STATUS.md — all checkboxes reflect reality before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes: `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo`
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- `persistence-migration-contract.md` exists with schema authority, atomic write, quarantine/recovery, unknown-member policy, migration journal, replacement notification + delivery context, secret-exclusion seam, import/backup boundary, serializer decision, teardown flush + panic-path policy, debounce policy (unimplemented), Core-deferral decision
- Implementation matches the contract; all new unit tests pass on Windows; WSL2 Linux test run recorded (or exact blocker named); 0 build warnings
- Failure-injection tests prove: corruption → quarantine + Degraded + flagged defaults; mid-rename crash recovery; unknown-member round-trip; migration idempotence; serialized concurrent writes; replacement notification
- Teardown flush wired at SP-003's reserved position with a dirty-at-shutdown test; activation note in `startup-shutdown-contract.md`; all SP-003/SP-004 tests intact
- Both solo Fable consults run with verdict text persisted; STATUS.md accurate; board row `WIP` with evidence (not `DONE`)
- No tracked changes outside File Scope; `.spine/` untouched

## Do NOT

- Modify `.spine/`, `AGENTS.md`, `CLAUDE.md`, `.gitnexus/`, `.pi/`
- Touch `ConditioningControlPanel/**` (WPF + first attempt are read-only evidence)
- Add feature settings models, cloud sync, backup scheduling, or a secret-store implementation (seam only — later rows' consumers)
- Admit Newtonsoft or any other package (System.Text.Json is in-box; the contract records the decision)
- Create `CcpClient.Core` (no second-assembly consumer yet — revisit trigger recorded instead)
- Implement a debounce timer (policy only — debounce tests are timer-flaky and no feature-scale churn exists)
- Build a generic migration framework (one demonstrator migration proving journal + idempotence)
- Disturb SP-003/SP-004 invariants: single guarded teardown entry point, runner's registry as sole owner set, phases 1–3 before Avalonia
- Use `consult` council mode (seats unproven — solo Fable 5 only)
- Set any board row to `DONE`
- Skip or fake STATUS.md updates, consult checkboxes, or review-evidence notes (reviewers check them)

## Git Commit Convention

- `feat(SP-005): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/persistence-migration-contract.md` (deliverable), `client/docs/task-board.md` (row-4 evidence), `spine-tasks/SP-005-persistence-migration-contract/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (append only if a durable surprise emerges), `client/docs/architecture-proposal.md` (only if the contract invalidates a §6 row-4 assignment — then flag, don't silently rewrite)

## Amendments

- 2026-07-19 (authoring, per pre-authoring Fable 5 consult — reply truncated at the replacement-notification point; received portion applied): **(1)** teardown flush ordering is THIS ROW's obligation — SP-003 reserved the position explicitly ("Settings-flush-before-disposal ordering belongs to row 4"); the packet requires wiring + dirty-at-shutdown test + a stated panic-path flush policy + the minimal activation edit to `startup-shutdown-contract.md` (placed in File Scope to keep #144 compliance). **(2)** store lands in `CcpClient.Desktop`, NOT a new Core library — the proposal pre-decided this ("rows 2–4 have a landing spot before Core exists"); revisit trigger recorded instead. **(3)** serializer = System.Text.Json with the explicit tolerance stack (`JsonNode` DOM-level version/migration, `[JsonExtensionData]` preserve, bind failure → quarantine + Degraded); Newtonsoft rejected as a new package admission. **(4)** scope traps fenced: debounce policy-only, one demonstrator migration not a framework, replacement notification documents its delivery context per SP-004 (truncated tail reconstructed as: raised on writer context; UI projection via `IUiDispatch` — consistent with SP-004's contract; if the full verdict is recovered it supersedes this note).
- 2026-07-19 (authoring): **WSL2 Linux verification added to Step 3** — rows 2 and 3 both landed Windows-only and carry the missing-Linux-gate caveat as debt; this slice is pure file-I/O where rename/flush semantics differ by platform, so the Linux run happens in-packet rather than as land debt. Pattern should continue for all later rows where WSL2 can exercise the slice.
- 2026-07-19 (authoring): coverage-gate checkbox omitted — no coverage collector configured for the client yet (row 7 scope), same disposition as SP-003/SP-004. Engine reviews assumed absent (T-2 open); Review Level stays 2 so reviews activate automatically if T-2 restores the pipeline.
