# Status: SP-004 — async lifecycle and fault policy

**Overall:** 🔄 In Progress — Current Step: 4

## Steps

### Step 1: Pre-approach consult and contract draft
**Status:** ✅ Complete

- [x] Pre-approach solo consult (Fable 5) run; verdict text persisted in record.md BEFORE checkbox
- [x] STATUS.md updated before starting work
- [x] WPF async/dispatcher outcomes digest in record.md (outcomes only)
- [x] `client/docs/async-lifecycle-fault-contract.md` written (ownership rule, generation scheme, dispatch boundary, fault classifications, row-5 boundary sentence, tested bans)

### Step 2: Async operation primitive and generation scheme
**Status:** ✅ Complete

- [x] Async-operation primitive implemented (registry-owned, generation, owned completion task, typed outcomes reusing SP-003 taxonomy)
- [x] Cancellation flows through SP-003's single guarded teardown entry point (no second path)
- [x] Unit tests: stale-generation completion discarded; mid-flight cancellation → typed Cancelled; deterministic fault routing (NOT via UnobservedTaskException); zero unobserved operations at teardown

### Step 3: UI dispatch boundary and demonstrator integration
**Status:** ✅ Complete

- [x] `IUiDispatch`-style boundary late-bound in phase 4 (no SynchronizationContext capture); tested pre-binding rule
- [x] Heartbeat wired through the primitive; user-visible update flows through the real boundary
- [x] Simulated resource failure → typed Recoverable/Degraded routed to owner
- [x] Headed Windows smoke: UIA-observed background callback reaching window; clean exit 0; recorded in record.md

### Step 4: Evidence, board reconciliation, pre-completion consult
**Status:** ⬜ Not Started

- [ ] record.md written (incl. engine-review presence/absence note)
- [ ] Pre-completion solo consult (Fable 5) run; verdict text persisted
- [ ] Board row 3 → `WIP` with evidence (not `DONE`)
- [ ] STATUS.md accurate

### Step 5: Testing & Verification
**Status:** ⬜ Not Started

- [ ] Contract testCommand passes
- [ ] `git diff --check` clean
- [ ] Only File Scope paths changed
