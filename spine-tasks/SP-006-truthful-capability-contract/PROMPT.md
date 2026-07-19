# Task: SP-006 — truthful runtime capability contract

## Mission

Execute `client/docs/task-board.md` row 5 (**"Define truthful runtime capability contract"**, P0, Phase 1 of `spine-tasks/CONTEXT.md`) against the landed SP-003–SP-005 foundation. Deliver `client/docs/runtime-capability-contract.md` plus its minimal implementation in `client/src/CcpClient.Desktop/Capabilities/` with tests: **a typed capability-state model where Windows, Linux X11, and Linux Wayland capabilities return availability/degradation/failure with structured reasons based on the selected backend's RUNTIME PROBE; OS checks, DI registration, assets present, stubs, external-browser/no-op fallbacks, and swallowed exceptions can NEVER claim support.** A capability reports `Available` only after its probe actually exercised the backend in the current environment; a probe that cannot run meaningfully MUST report `Unavailable`/`Degraded` with the environmental reason — faking availability to pass a test or CI is a contract violation. The contract instantiates the proposal (§6 row-5 column of `client/docs/architecture-proposal.md`) and the first-attempt capability-by-registration lesson. It proves honesty with **two demonstrator capabilities** — not feature capabilities.

## Dependencies

- **Task:** SP-005 (persistence store is the consumer pattern for probed-then-persisted state; SP-004's typed operations are the probe-execution primitive)

## Context to Read First

- `client/docs/async-lifecycle-fault-contract.md` — typed `OperationOutcome` vocabulary and the row-3/row-5 boundary sentence (row 3 owns OPERATION outcomes; row 5 OWNS capability-availability states — this task activates that boundary)
- `client/docs/architecture-proposal.md` — §6 row-5 deferred topics (typed capability states and runtime probes); §2 one-head decision (NO per-OS head split); §5.1 Wayland opt-in policy (open owner question — probes report environment facts, not backend claims)
- `client/docs/first-attempt-systemic-lessons.md` — capability lesson (`AvaloniaPlatformCapabilities.cs:24-65` reported from OS assumptions + DI-fallback checks; `WebKitGtkBrowserHost.cs:9-49` external-browser fallback registered as embedded host; `LinuxOverlaySurface.cs:9-78` faults→logged no-ops; `57cbfc81` Wayland backend claiming per-region behavior while stubbing it). Disposition: REJECT capability-by-platform/registration/assets-present; ADAPT graceful fallback only for explicitly optional behavior
- `client/docs/startup-shutdown-contract.md` — phase model (probes run in a named phase; no constructor-started work)
- `client/docs/architecture.md` — A-014 explicit foundation; A-001 composition boundaries
- `spine-tasks/CONTEXT.md` — Phase 1 scope and execution policy
- Required skills: load `port-feature` and `avalonia-research` before Step 1

## File Scope

- `client/src/CcpClient.Desktop/**` (Capabilities/ implementation; probe wiring in Lifecycle/Program)
- `client/tests/CcpClient.Tests/**` (capability tests)
- `client/docs/runtime-capability-contract.md` (contract deliverable)
- `client/docs/task-board.md` (row-5 evidence edit only)
- `spine-tasks/SP-006-truthful-capability-contract/**` (STATUS.md, record.md, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/docs/runtime-capability-contract.md`, `client/src/CcpClient.Desktop/Capabilities/` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `.spine/**` |
| artifactsMustExist | `client/docs/runtime-capability-contract.md`, `spine-tasks/SP-006-truthful-capability-contract/record.md` |

**Review Level 2 (plan + code)** — honesty of every later feature claim depends on this contract. Call `spine_review_step` after each step. Engine reviews are empirically dead (zero reviews in SP-001…SP-005 — diagnostic row T-2 open); if `spine_review_step` returns skipped, record that fact in record.md and rely on the mandatory Fable consults instead. Do not stall waiting for a reviewer.

## Steps

### Step 1: Pre-approach consult and contract draft

- [ ] Run a **pre-approach solo consult** (Fable 5 via `consult` tool, mode solo) with the planned contract outline (state model, probe rule, honesty rule, demonstrator choices, degradation semantics); record the verdict text in record.md **before** marking the checkbox (write-then-check)
- [ ] Update STATUS.md Step 1 checkboxes as you work (before, not after)
- [ ] Digest the first-attempt capability evidence (the three lying patterns: OS-assumption, DI-fallback-identity, fault→no-op) into a short evidence note inside record.md — outcomes only
- [ ] Write `client/docs/runtime-capability-contract.md`: the typed state model (`Available` / `Unavailable(reason)` / `Degraded(named surviving semantics + reason)` / `PermissionRequired` / `DependencyMissing` / `Faulted`) with structured reason codes; the probe rule (Available ONLY from a runtime probe of the selected backend in the current environment; registration, OS checks, assets present, stubs, no-op fallbacks, and swallowed exceptions NEVER claim support); probes execute as SP-004 typed operations (owned, cancellable, typed outcomes); degradation must name which semantics survive; **the honesty rule** (a probe that cannot run meaningfully in the current environment reports Unavailable/Degraded with the environmental reason — faking availability is a contract violation, and a test that requires fake availability is a defective test); the row-3/row-5 boundary activation (capability states here; operation outcomes stay row 3); fallback policy (explicit-optionality only, never silent); session-type reporting (X11 vs Wayland vs Windows are ENVIRONMENT FACTS the probe reports — Avalonia 12.1 defaults to X11 per proposal §5.1, Wayland opt-in is an open owner question, so the contract reports what the session offers and never claims Wayland backend behavior)

### Step 2: Capability registry and demonstrator probes

- [ ] Implement `Capabilities/`: a registry where each capability declares its probe; probe execution via SP-004's OperationRegistry (owned operation, typed outcome → mapped to capability state); query API returns the typed state with reason (never throws-and-claims; probe exceptions map to `Faulted` with the exception class as reason)
- [ ] **Demonstrator 1 — session-type probe:** reports Windows / Linux-X11 / Linux-Wayland from real session evidence (OS platform + `WAYLAND_DISPLAY`/`DISPLAY` + XWayland markers), truthful on every supported environment including WSLg; the record must note the XWayland distinction (WSLg sets `WAYLAND_DISPLAY` while Avalonia runs as an X11 client by default — the probe reports session facts, not backend claims)
- [ ] **Demonstrator 2 — atomic filesystem probe:** exercises SP-005's persistence guarantees for real (create temp, rename-over-existing, flush-to-disk, quarantine-style move) in the actual data directory and reports `Available`/`Degraded` with the fs-level reason — genuinely passes on Windows + native Linux fs, genuinely degrades on DrvFs (`/mnt/*`) semantics; this probe must actually perform the I/O, never infer from OS strings
- [ ] Unit tests: every state reachable via probe doubles; probe-throws → `Faulted` (never unhandled, never Available); unregistered capability query → `Unavailable(unknown)` not a fabricated state; registration alone NEVER yields Available (register-without-probe-run stays `Unavailable(not-probed)`)

### Step 3: Wiring, WSL2 honesty run, integration proof

- [ ] Probes run in a named startup phase as owned operations (SP-003 phase model; no constructor-started work); the placeholder window surfaces demonstrator capability states (user-visible trace, SP-003 integration-proof pattern)
- [ ] **WSL2 Linux verification (in-packet gate, SP-005 pattern):** copy `client/` to a native WSL dir (never `/mnt/e`), run the contract testCommand, AND record the ACTUAL demonstrator states observed under WSL2/WSLg in record.md — session-type reports its truthful value; any capability that cannot be probed meaningfully there MUST appear as Unavailable/Degraded with the environmental reason (that degraded report IS the honesty proof, not a failure)
- [ ] Headed Windows smoke (SP-003/004 pattern): launch the Debug exe, observe via UIA that capability states render truthfully, close gracefully, exit 0; record the observation
- [ ] Unit tests: full startup path populates states via real probes (no test-double substitution on the composition-root path); WSL2-observed states match what the probes reported (record comparison, not assertion)

### Step 4: Evidence, board reconciliation, pre-completion consult

- [ ] Write `spine-tasks/SP-006-truthful-capability-contract/record.md`: contract summary, demonstrator choices + why they resist gaming, first-attempt evidence digest, consult verdicts, test output (Windows AND WSL2), the actual observed WSL2/WSLg capability states, headed-smoke observation, surprises. **Also record engine-review presence/absence** (row T-2)
- [ ] Run a **pre-completion solo consult** (Fable 5, solo) on the diff and contract; record the verdict text in record.md
- [ ] Update `client/docs/task-board.md` row **"Define truthful runtime capability contract"** to `WIP` with evidence text citing record.md — never `DONE`
- [ ] Update STATUS.md — all checkboxes reflect reality before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes: `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo`
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- `runtime-capability-contract.md` exists with the state model, probe rule, honesty rule, degradation semantics, row-3/row-5 boundary activation, fallback policy, and session-type reporting rules
- Implementation matches the contract; all new unit tests pass on Windows; WSL2 run recorded with ACTUAL observed capability states (degraded reports count as honesty proof); 0 build warnings
- Every typed state is reachable in tests; registration/OS-checks/assets/stubs/no-op-fallbacks never yield Available; probe exceptions → `Faulted`
- Both demonstrators probe for real (session evidence, actual filesystem I/O); the window surfaces states via the real composition root; headed Windows smoke observed
- Both solo Fable consults run with verdict text persisted; STATUS.md accurate; board row `WIP` with evidence (not `DONE`)
- No tracked changes outside File Scope; `.spine/` untouched

## Do NOT

- Modify `.spine/`, `AGENTS.md`, `CLAUDE.md`, `.gitnexus/`, `.pi/`
- Touch `ConditioningControlPanel/**` (WPF + first attempt are read-only evidence)
- Implement feature capabilities (browser host, overlay, camera, audio) — seam points/names only; the two demonstrators prove the contract
- Add native interop (PInvoke/DllImport) — environment probes use BCL + env evidence only (native admission is a later row with its own decision)
- Split a per-OS head (proposal §2) or admit any package
- Claim Wayland backend behavior from WSLg session facts (§5.1 opt-in is an open owner question)
- Fake availability under test/CI/WSL2 to make assertions pass — a degraded truthful report passes; a fabricated Available fails the task
- Disturb SP-003/SP-004/SP-005 invariants (single guarded teardown, registry ownership, phases 1–3 pre-Avalonia, persistence contract)
- Use `consult` council mode (seats unproven — solo Fable 5 only)
- Set any board row to `DONE`
- Skip or fake STATUS.md updates, consult checkboxes, or review-evidence notes

## Git Commit Convention

- `feat(SP-006): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/runtime-capability-contract.md` (deliverable), `client/docs/task-board.md` (row-5 evidence), `spine-tasks/SP-006-truthful-capability-contract/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (append only if a durable surprise emerges), `client/docs/async-lifecycle-fault-contract.md` (only if the row-3/row-5 boundary activation needs a one-line cross-reference — minimal edit, say so in record.md)

## Amendments

- 2026-07-19 (authoring): **pre-authoring Fable consult NOT run — per-turn consult cap reached (3/3) after the SP-005 land consult; route healthy, so this is not a pause-protocol trigger.** In its place: (a) the SP-005 land consult's explicit SP-006 guidance is applied (capabilities must report truthfully degraded under WSL2 rather than fake support to pass tests — encoded as the Honesty Rule + WSL2 observed-states requirement); (b) demonstrator choices made by orchestrator judgment: session-type probe (truthful everywhere incl. WSLg; XWayland distinction recorded) + atomic-filesystem probe (real I/O exercising SP-005's guarantees; genuinely degrades on DrvFs — chosen over overlay/browser demonstrators because those need backends that don't exist yet and would invite stub-probing, the exact first-attempt sin). The worker's in-packet pre-approach consult (Step 1) is the design gate and may amend demonstrator details with reasons recorded.
- 2026-07-19 (authoring): WSL2 gate upgraded from "test run" to "test run + observed truthful capability states" — for this row the Linux environment is part of the contract's subject matter, so the observed states themselves are evidence.
- 2026-07-19 (authoring): coverage-gate checkbox omitted (row 7 scope); engine reviews assumed absent (T-2 open); Review Level stays 2 for auto-activation if T-2 lands.
