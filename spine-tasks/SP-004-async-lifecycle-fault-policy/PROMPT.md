# Task: SP-004 — async lifecycle and fault policy

## Mission

Execute `client/docs/task-board.md` row 3 (**"Establish async lifecycle and fault policy"**, P0, Phase 1 of `spine-tasks/CONTEXT.md`) against the landed SP-003 lifecycle. Deliver `client/docs/async-lifecycle-fault-contract.md` plus its minimal implementation in `client/src/CcpClient.Desktop/` with tests: **every long-running operation has exactly one owner, a cancellation generation, an owned completion task, and a typed terminal outcome; one deliberate UI dispatch boundary with documented per-stream delivery context; out-of-order/stale completions are invalidated by generation; the Recoverable/Degraded fault taxonomy (reserved in SP-003) is activated as operation-outcome classifications; unobserved required work and blanket catch-as-success are forbidden as tested rules.** The contract instantiates the proposal (§6 row-3 column of `client/docs/architecture-proposal.md`: dispatcher discipline, cancellation generations, out-of-order completion policy) and the first-attempt async lessons. It does **not** implement features — it proves the async/fault shape through the existing demonstrator participant.

## Dependencies

- **Task:** SP-003 (the Lifecycle implementation — phase runner, composition root, guarded teardown entry point, `ILogSink` seam, fault taxonomy, Heartbeat demonstrator — is this task's foundation and must not be disturbed)

## Context to Read First

- `client/docs/startup-shutdown-contract.md` — SP-003 contract: phases, teardown matrix, ownership rule, taxonomy (Recoverable/Degraded reserved — "first consumer is row 3/5"), logger seam
- `client/docs/architecture-proposal.md` — §6 row-3 deferred topics (dispatcher discipline, cancellation generations, out-of-order completion policy); §2 one-head decision
- `client/docs/first-attempt-systemic-lessons.md` — async ownership lesson (detached `Task.Run`/`async void`/blanket catches → "every long operation has an owner, cancellation generation, completion task, terminal outcome"; ADAPT async but reject implicit callback-thread assumptions; delivery-context documentation; tests inject out-of-order completion/cancellation/background callbacks)
- `client/docs/architecture.md` — A-014 explicit foundation; A-001 composition boundaries
- `spine-tasks/CONTEXT.md` — Phase 1 scope and execution policy
- `spine-tasks/SP-003-startup-shutdown-contract/record.md` — SP-003 surprises (runner owns start AND stop; panic-path single teardown entry point) and the pre-approach consult's phase-1–3-before-Avalonia constraint
- WPF behavioral evidence (read-only): `ConditioningControlPanel/App.xaml.cs` dispatcher/crash handling and `ConditioningControlPanel/CLAUDE.md` known-issues (DispatcherTimer vs `Task.Delay` loops, cross-thread UI updates). Extract outcomes, not mechanics. Load skill `wpf-parity` for this.
- Required skills: load `port-feature` and `avalonia-research` before Step 1; `wpf-parity` when citing WPF behavior

## File Scope

- `client/src/CcpClient.Desktop/**` (async lifecycle implementation, extending Lifecycle/)
- `client/tests/CcpClient.Tests/**` (async lifecycle tests)
- `client/docs/async-lifecycle-fault-contract.md` (contract deliverable)
- `client/docs/task-board.md` (row-3 evidence edit only)
- `spine-tasks/SP-004-async-lifecycle-fault-policy/**` (STATUS.md, record.md, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/docs/async-lifecycle-fault-contract.md`, `client/src/CcpClient.Desktop/Lifecycle/` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `.spine/**` |
| artifactsMustExist | `client/docs/async-lifecycle-fault-contract.md`, `spine-tasks/SP-004-async-lifecycle-fault-policy/record.md` |

**Review Level 2 (plan + code)** — this contract gates every later row that starts async work. Call `spine_review_step` after each step. Engine reviews are empirically dead (zero reviews in SP-001/SP-002/SP-003 batches — diagnostic row T-2 open); if `spine_review_step` returns skipped, record that fact in record.md and rely on the mandatory Fable consults instead. Do not stall waiting for a reviewer.

## Steps

### Step 1: Pre-approach consult and contract draft

- [ ] Run a **pre-approach solo consult** (Fable 5 via `consult` tool, mode solo) with the planned contract outline (operation ownership model, generation scheme, dispatch boundary shape, fault classification rules, tested bans); record the verdict text in record.md **before** marking the checkbox (write-then-check — SP-002's recorded gap: checkbox claimed a verdict that was never persisted)
- [ ] Update STATUS.md Step 1 checkboxes as you work (before, not after)
- [ ] Digest WPF async/dispatcher outcomes (Dispatcher discipline, timer rules, crash handling tiers — outcomes only, no mechanics transplant) into a short evidence note inside record.md
- [ ] Write `client/docs/async-lifecycle-fault-contract.md`: operation ownership rule (every long op: exactly one owner in the runner's registry, a cancellation generation, an owned completion task, a typed terminal outcome); **one deliberate UI dispatch boundary** with per-event/stream documented delivery context; generation-invalidation rule for stale/out-of-order completions; fault policy activating `Recoverable`/`Degraded` as **operation-outcome** classifications supplied per-operation by the owner (one sentence drawing the boundary: row 5 owns capability-availability states and runtime probes — this contract builds no probe machinery); explicit bans as contract rules: no `async void` except genuine event handlers, no unobserved required work (every required op's completion is owned and observed), no blanket catch-as-success (faults route to the owner as typed outcomes)

### Step 2: Async operation primitive and generation scheme

- [ ] Implement the minimal async-operation primitive in `Lifecycle/` (no abstractions without a consumer): registration in the runner's ownership registry (extending SP-003's one-owner rule — the registry owns start AND stop), a monotonic cancellation generation per owner, an owned completion task per operation, typed terminal outcomes (Completed / Cancelled / Recoverable / Degraded / Fatal — reusing the SP-003 taxonomy, not a parallel enum)
- [ ] Cancellation: teardown of an owner cancels its generation; in-flight operations observe the token and terminate with the typed `Cancelled` outcome. **No second teardown path** — in-flight cancellation flows through SP-003's single guarded teardown entry point
- [ ] Unit tests: out-of-order completion from a stale generation is discarded (late result cannot overwrite a newer generation's state); cancellation mid-flight yields typed `Cancelled` (no unhandled exception); a faulting operation surfaces its typed outcome synchronously-awaitable through the registry (deterministic — do NOT test via `TaskScheduler.UnobservedTaskException`, which needs GC timing and will flake; that hook stays as SP-003's backstop, untested here); registry reports zero unobserved operations at teardown

### Step 3: UI dispatch boundary and demonstrator integration

- [ ] Implement the dispatch boundary as a small injected interface (e.g., `IUiDispatch`) — **late-bound in phase 4** (phases 1–3 run before Avalonia exists, so there is no `SynchronizationContext` to capture at composition time; do NOT capture `SynchronizationContext.Current`). Production implementation wraps `Dispatcher.UIThread`; tests inject a fake. Include a tested rule for calls made **before** the boundary is bound (pick typed failure or explicit throw — state the choice in the contract and test it)
- [ ] Wire the demonstrator participant (Heartbeat) through the new primitive: its async work is a registered operation with a generation, and one user-visible update (e.g., heartbeat tick text in the placeholder window) flows through the real dispatch boundary
- [ ] Simulated native/resource failure test: an operation failing with a resource-style exception is classified by its owner as the typed `Recoverable`/`Degraded` outcome (activating the reserved taxonomy members) and routed to the owner — no blanket catch-as-success
- [ ] **Headed Windows smoke** (SP-003 precedent — headless is deferred to row 7, so this is how the boundary claim becomes observed, not believed): launch the Debug exe, observe via UIA that a background callback reaches the window through the boundary (visible heartbeat update), close gracefully, confirm exit code 0; record the observation in record.md

### Step 4: Evidence, board reconciliation, pre-completion consult

- [ ] Write `spine-tasks/SP-004-async-lifecycle-fault-policy/record.md`: contract summary, dispatch-binding decision + reasons (late-bound phase 4 vs SynchronizationContext capture — cite the pre-authoring Fable correction), WPF evidence digest, consult verdicts, test output, headed-smoke observation, surprises. **Also record engine-review presence/absence** (pipeline empirically dead — row T-2; if `spine_review_step` returned skipped, say so)
- [ ] Run a **pre-completion solo consult** (Fable 5, solo) on the diff and contract; record the verdict text in record.md
- [ ] Update `client/docs/task-board.md` row **"Establish async lifecycle and fault policy"** to `WIP` with evidence text citing record.md — never `DONE` (orchestrator flips at land after evidence review)
- [ ] Update STATUS.md — all checkboxes reflect reality before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes: `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo`
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- `async-lifecycle-fault-contract.md` exists with the ownership rule, generation scheme, dispatch boundary, fault classifications, row-5 boundary sentence, and the tested bans
- Implementation matches the contract; all new unit tests pass on Windows; 0 build warnings
- Generation invalidation, mid-flight cancellation, deterministic fault routing, and zero-unobserved-at-teardown are all tested
- Dispatch boundary is late-bound in phase 4 with a tested pre-binding rule; headed Windows smoke observed a background callback reaching the window through it
- All SP-003 tests still pass — the single guarded teardown entry point is undisturbed (no second teardown path)
- Both solo Fable consults run with verdict text persisted; STATUS.md accurate; board row `WIP` with evidence (not `DONE`)
- No tracked changes outside File Scope; `.spine/` untouched

## Do NOT

- Modify `.spine/`, `AGENTS.md`, `CLAUDE.md`, `.gitnexus/`, `.pi/`
- Touch `ConditioningControlPanel/**` (WPF + first attempt are read-only evidence)
- Disturb SP-003 invariants: the single guarded teardown entry point (no second teardown path for async ops), the runner's registry as sole owner set, phases 1–3 running before Avalonia starts
- Capture `SynchronizationContext.Current` at composition time (it is null pre-Avalonia — silent breakage; late-bind in phase 4 instead)
- Test fault observation via `TaskScheduler.UnobservedTaskException` (GC-timing flaky; test deterministic registry routing)
- Admit `Avalonia.Headless.XUnit` (deferred to row 7), a DI container, a logging framework, or any package beyond the SP-002 baseline
- Build capability probes or availability states (row 5 scope — this contract only classifies operation outcomes)
- Implement features (settings, overlays, media) — the demonstrator proves the async shape, not a product feature
- Design or implement a single-instance mechanism (owner question §5.3 pending — carve-out only)
- Use `consult` council mode (seats unproven — solo Fable 5 only)
- Set any board row to `DONE`
- Skip or fake STATUS.md updates, consult checkboxes, or review-evidence notes (reviewers check them)

## Git Commit Convention

- `feat(SP-004): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/async-lifecycle-fault-contract.md` (deliverable), `client/docs/task-board.md` (row-3 evidence), `spine-tasks/SP-004-async-lifecycle-fault-policy/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (append only if a durable surprise emerges), `client/docs/startup-shutdown-contract.md` (only if the fault-taxonomy activation requires amending the SP-003 "reserved" note — then make the minimal edit and say so in record.md)

## Amendments

- 2026-07-18 (authoring, per pre-authoring Fable 5 consult — **truncated mid-reply**, received portion applied): **(1)** dispatch boundary must be a small injected interface **late-bound in phase 4**, never `SynchronizationContext.Current` capture — phases 1–3 run before Avalonia exists, so capture-at-composition yields null and a silently broken boundary that fake-injected tests cannot catch; require the headed smoke to *observe* a background callback reaching the window. **(2)** dropped the `UnobservedTaskException`-based test — nondeterministic (GC/finalization timing); the tested rule is deterministic registry-owned fault routing + zero-unobserved-at-teardown. **(3)** contract must state the row-3/row-5 boundary: Recoverable/Degraded here are operation-outcome classifications supplied per-operation by the owner; capability availability/probes are row 5. Truncated tail (SP-003-invariant warnings) reconstructed from the received fragment "no second teardown path; in-flight cancellation h[appens through the guarded teardown]" — encoded in Do NOT; if the full verdict is recovered it supersedes this note.
- 2026-07-18 (authoring): coverage-gate checkbox omitted — no coverage collector configured for the client yet (row 7 scope), same disposition as SP-003.
- 2026-07-18 (authoring): engine reviews assumed absent (T-2 open); the two Fable consults + land-time orchestrator evidence review are the quality gates. Review Level stays 2 so reviews activate automatically if T-2 restores the pipeline.
