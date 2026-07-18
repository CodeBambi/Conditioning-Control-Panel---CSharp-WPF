# Task: SP-003 — startup, shutdown, and integration contract

## Mission

Execute `client/docs/task-board.md` row 2 (**"Define startup, shutdown, and integration contract"**, P0, Phase 1 of `spine-tasks/CONTEXT.md`) against the landed SP-002 scaffold. Deliver `client/docs/startup-shutdown-contract.md` plus its minimal bootstrap implementation in `client/src/CcpClient.Desktop/` with tests: **ordered cancellable startup phases, composition-root validation, typed initialization failures, one owner for each background participant, panic/close/startup-failure teardown, and an end-to-end proof that registered code is reachable from a user path. No global service locator; no constructor-started background work.** The contract instantiates the proposal (§3, §6 row-2 column of `client/docs/architecture-proposal.md`) and the A-014/first-attempt lifecycle lessons; it decides container admission (manual construction vs a DI container) with reasons. It does **not** implement features — it proves the lifecycle shape with the placeholder window and one demonstrator background participant.

## Dependencies

- **Task:** SP-002 (the `client/` scaffold, `architecture-proposal.md`, and its deferred-topics table are this task's inputs)

## Context to Read First

- `client/docs/architecture-proposal.md` — §3 composition-root shape, §6 row-2 deferred topics (DI admission, startup-phase machine, lifetime shape, splash/crash-sentinel/hang-watchdog triage, shutdown ordering incl. `TerminateProcess` analogue); §5.3 single-instance owner question
- `client/docs/architecture.md` — A-014 explicit foundation; A-001 composition boundaries
- `client/docs/first-attempt-systemic-lessons.md` — "Startup order and hidden globals became architecture" (REJECT static locator), "'Unwired but verified' is not a shippable intermediate state" (integration proof required)
- `client/docs/row-1-research-inputs.md` — §4 lifetime/dispatcher facts (Q7/Q8: `StartWithClassicDesktopLifetime` vs manual `Start(AppMain, args)` + `app.Run(cts.Token)`)
- `spine-tasks/CONTEXT.md` — Phase 1 scope and execution policy
- WPF behavioral evidence (read-only): `ConditioningControlPanel/App.xaml.cs` — `OnStartup` ordering, Serilog bootstrap, `DispatcherUnhandledException`/crash handling, `OnExit`/settings flush, single-instance mutex + ack handshake (lines ~41-48, ~950+). Extract outcomes, not mechanics. Load skill `wpf-parity` for this.
- Required skills: load `port-feature` and `avalonia-research` before Step 1; `wpf-parity` when citing WPF behavior

## File Scope

- `client/src/CcpClient.Desktop/**` (startup/shutdown implementation)
- `client/tests/CcpClient.Tests/**` (lifecycle tests)
- `client/docs/startup-shutdown-contract.md` (contract deliverable)
- `client/docs/task-board.md` (row-2 evidence edit only)
- `spine-tasks/SP-003-startup-shutdown-contract/**` (STATUS.md, record.md, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/docs/startup-shutdown-contract.md`, `client/src/CcpClient.Desktop/Program.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `.spine/**` |
| artifactsMustExist | `client/docs/startup-shutdown-contract.md`, `spine-tasks/SP-003-startup-shutdown-contract/record.md` |

**Review Level 2 (plan + code)** — this contract gates every later row that starts/stops code. Call `spine_review_step` after each step. If reviewer spawn stalls (environment unproven for engine reviews — zero reviews ran in SP-001/SP-002 batches), the orchestrator will amend to Level 1 and rely on the land-time Fable consult.

## Steps

### Step 1: Pre-approach consult and contract draft

- [ ] Run a **pre-approach solo consult** (Fable 5 via `consult` tool, mode solo) with the planned contract outline (phase list, typed failure taxonomy, ownership model, container decision); record the verdict text in record.md **before** marking the checkbox (write-then-check — SP-002's recorded gap: checkbox claimed a verdict that was never persisted)
- [ ] Update STATUS.md Step 1 checkboxes as you work (before, not after)
- [ ] Digest WPF startup/shutdown outcomes from `App.xaml.cs` (ordered init, crash surfacing, settings flush ordering, second-instance behavior) into a short evidence note inside record.md — outcomes only, no mechanics transplant
- [ ] Write `client/docs/startup-shutdown-contract.md`: named startup phases with order + cancellation semantics; typed initialization-failure taxonomy (recoverable/degraded/fatal); composition-root validation rules; per-participant ownership rule (exactly one owner per background participant); teardown matrix for panic / window-close / startup-failure; the container-admission decision (manual construction vs DI container) with reasons tied to A-014; **single-instance carve-out** — the contract reserves a named seam point but designs NO mechanism (requirement pending owner question §5.3; WPF's mutex+ack is Windows-only)

### Step 2: Startup phases and composition root

- [ ] Implement the phase runner in `CcpClient.Desktop` (minimal, no abstractions without a consumer): ordered phases, `CancellationToken` threaded through, typed failure results (not exceptions-as-control-flow for expected failures), composition-root self-validation (fail fast with a typed error when a registration is missing)
- [ ] Manual construction only — no static `App.Services`-style locator, no background work started from constructors (contract must state this as a tested rule)
- [ ] Unit tests: phase order is enforced; cancellation between phases stops later phases; a failing phase yields the typed failure (no unhandled exception); composition-root validation catches a deliberately missing registration

### Step 3: Teardown and integration proof

- [ ] Implement shutdown: window-close path, startup-failure path, and panic/unhandled-exception path each run the contract's teardown (idempotent stop/dispose; no orphaned background participant)
- [ ] One **demonstrator** background participant (e.g., a heartbeat/no-op service with a single owner) proving the ownership rule end-to-end — it starts in a named phase and is provably stopped on every teardown path
- [ ] Integration proof per the board acceptance: registered code is reachable from a user path — the placeholder window shows phase outcomes (or an equivalent user-visible trace), and a test walks the real composition root (not a mock) to assert the window's dependencies resolve
- [ ] Unit tests: each teardown path stops the demonstrator exactly once; repeated shutdown is a no-op; unhandled-exception path logs and tears down without hanging

### Step 4: Evidence, board reconciliation, pre-completion consult

- [ ] Write `spine-tasks/SP-003-startup-shutdown-contract/record.md`: contract summary, container decision + reasons, WPF evidence digest, consult verdicts, test output, surprises. **Also record whether engine review events appear for this batch** (the review pipeline is unproven — zero reviews in SP-001/SP-002; the orchestrator verifies the journal at land, and your note is the worker-side cross-check: if `spine_review_step` returned skipped, say so)
- [ ] Run a **pre-completion solo consult** (Fable 5, solo) on the diff and contract; record the verdict text in record.md
- [ ] Update `client/docs/task-board.md` row **"Define startup, shutdown, and integration contract"** to `WIP` with evidence text citing record.md — never `DONE` (orchestrator flips at land after evidence review)
- [ ] Update STATUS.md — all checkboxes reflect reality before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes: `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo`
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- `startup-shutdown-contract.md` exists with phases, failure taxonomy, ownership rule, teardown matrix, container decision, and the single-instance carve-out
- Implementation matches the contract; all new unit tests pass on Windows; 0 build warnings
- Teardown proven on all three paths (close / startup-failure / panic); demonstrator participant has exactly one owner
- Integration proof: user path reaches registered code via the real composition root
- Both solo Fable consults run with verdict text persisted; STATUS.md accurate; board row `WIP` with evidence (not `DONE`)
- No tracked changes outside File Scope; `.spine/` untouched

## Do NOT

- Modify `.spine/`, `AGENTS.md`, `CLAUDE.md`, `.gitnexus/`, `.pi/`
- Touch `ConditioningControlPanel/**` (WPF + first attempt are read-only evidence)
- Design or implement a single-instance mechanism (owner question §5.3 pending — carve-out only)
- Introduce a DI container, logging framework, or any package beyond the SP-002 baseline without recording the admission decision + reasons in the contract (Serilog/logging decisions belong to row 2 only insofar as the panic path needs a logger seam — prefer the smallest seam, no framework admission without justification)
- Start background work from constructors or expose a static service locator
- Implement features (settings, overlays, media) — the demonstrator participant is a no-op proving ownership, not a product feature
- Use `consult` council mode (seats unproven — solo Fable 5 only)
- Set any board row to `DONE`
- Skip or fake STATUS.md updates, consult checkboxes, or review-evidence notes (reviewers check them)

## Git Commit Convention

- `feat(SP-003): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/startup-shutdown-contract.md` (deliverable), `client/docs/task-board.md` (row-2 evidence), `spine-tasks/SP-003-startup-shutdown-contract/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (append only if a durable surprise emerges), `client/docs/architecture-proposal.md` (only if the contract invalidates a §6 row-2 assignment — then flag, don't silently rewrite)

## Amendments

- 2026-07-18 (authoring, per proposal-review Fable consult): **single-instance explicitly carved out** — §5.3's requirement question is unanswered in continuous mode, so the contract reserves a seam point and designs no mechanism; the worker must not build a cross-platform mutex replacement for a requirement the owner may delete.
- 2026-07-18 (authoring): coverage-gate checkbox omitted — no coverage collector is configured for the client yet (`testing.testWithCoverage` is the plain test command; pi-spine's ≥77% default is an npm-project policy). Coverage tooling belongs to row 7 (verification harness).
- 2026-07-18 (authoring): record.md must note engine-review presence/absence — the spine review pipeline is empirically unproven (zero review events in both prior batches); SP-003 is its first clean-run test. Orchestrator verifies journal review events at land.
