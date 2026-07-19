# Status: SP-005 — persistence and migration contract

**Overall:** 🔄 In Progress

**Current Step:** 1

## Steps

### Step 1: Pre-approach consult and contract draft
**Status:** 🔄 In Progress

- [x] Pre-approach solo consult (Fable 5) run; verdict text persisted in record.md BEFORE checkbox
- [x] STATUS.md updated before starting work
- [x] WPF persistence outcomes digest in record.md (outcomes only)
- [x] `client/docs/persistence-migration-contract.md` written (schema authority, atomic write, quarantine, unknown-member policy, migration journal, replacement notification + delivery context, secret seam, import/backup boundary, STJ decision, teardown flush + panic policy, debounce policy-only, Core deferral)

### Step 2: Persistence store implementation
**Status:** ⬜ Not Started

- [ ] Store implemented in `Persistence/` (JsonNode DOM version read, atomic temp+rename+flush writer with injection points, single serialized writer via OperationRegistry, quarantine path)
- [ ] One demonstrator settings model + one v0→v1 migration with journal (no framework)
- [ ] Save/SaveImmediate only — no debounce timer; secret seam only
- [ ] Unit tests: corrupt→quarantine+Degraded+flagged defaults; mid-rename crash recovery; unknown-member round-trip; migration idempotence; serialized concurrent writes; replacement notification

### Step 3: Teardown flush wiring and activation note
**Status:** ⬜ Not Started

- [ ] Flush wired at SP-003's reserved teardown position (before participant stop; single guarded entry point)
- [ ] Dirty-at-shutdown flush test; SP-003/SP-004 tests intact
- [ ] Minimal activation edit to `startup-shutdown-contract.md`, recorded in record.md
- [ ] WSL2 Linux contract testCommand run recorded in record.md (or exact blocker named)

### Step 4: Evidence, board reconciliation, pre-completion consult
**Status:** ⬜ Not Started

- [ ] record.md written (incl. engine-review presence/absence note)
- [ ] Pre-completion solo consult (Fable 5) run; verdict text persisted
- [ ] Board row 4 → `WIP` with evidence (not `DONE`)
- [ ] STATUS.md accurate

### Step 5: Testing & Verification
**Status:** ⬜ Not Started

- [ ] Contract testCommand passes
- [ ] `git diff --check` clean
- [ ] Only File Scope paths changed
