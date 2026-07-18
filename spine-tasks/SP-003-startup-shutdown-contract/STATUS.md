# Status: SP-003 — startup, shutdown, and integration contract

**Overall:** 🔄 In Progress

**Current Step:** Step 4

## Steps

### Step 1: Pre-approach consult and contract draft
**Status:** ✅ Complete

- [x] Pre-approach solo consult (Fable 5) run; verdict text persisted in record.md BEFORE checkbox
- [x] STATUS.md updated before starting work
- [x] WPF startup/shutdown outcomes digest in record.md (outcomes only)
- [x] `client/docs/startup-shutdown-contract.md` written (phases, failure taxonomy, ownership rule, teardown matrix, container decision, single-instance carve-out)

### Step 2: Startup phases and composition root
**Status:** ✅ Complete

- [x] Phase runner implemented (ordered phases, CancellationToken, typed failures, composition-root validation)
- [x] Manual construction only; no static locator; no constructor-started background work
- [x] Unit tests: phase order, inter-phase cancellation, typed phase failure, missing-registration validation

### Step 3: Teardown and integration proof
**Status:** ✅ Complete

- [x] Teardown on window-close, startup-failure, and panic paths (idempotent)
- [x] Demonstrator background participant with exactly one owner, stopped on every path
- [x] Integration proof: user path reaches registered code via real composition root (test + visible trace)
- [x] Unit tests: teardown exactly-once per path, repeated shutdown no-op, panic path logs + tears down

### Step 4: Evidence, board reconciliation, pre-completion consult
**Status:** ⬜ Not Started

- [ ] record.md written (incl. engine-review presence/absence note)
- [ ] Pre-completion solo consult (Fable 5) run; verdict text persisted
- [ ] Board row 2 → `WIP` with evidence (not `DONE`)
- [ ] STATUS.md accurate

### Step 5: Testing & Verification
**Status:** ⬜ Not Started

- [ ] Contract testCommand passes
- [ ] `git diff --check` clean
- [ ] Only File Scope paths changed
