# Status: SP-009 — define asset and packaged-output manifest

**Overall:** 🔄 In Progress

**Current Step:** Step 5

## Steps

### Step 1: Pre-approach consult, v12 research, contract draft
**Status:** ✅ Complete

- [x] Pre-approach solo consult (Fable 5) run; verdict text persisted BEFORE checkbox
- [x] STATUS.md updated before starting work
- [x] v12 AssetLoader research (platform-init requirement, embedding mechanics, publish-resolution changes — research only)
- [x] `client/docs/asset-manifest.md` written (schema, override/trust policy, two-direction rule, self-check contract, row-9 hook, evidence classes)

### Step 2: Catalogue and validation tests
**Status:** ✅ Complete

- [x] assets.manifest.json with one real entry, embedded as avares resource
- [x] Two-direction tests: manifest opens via AssetLoader + completeness sweep fails on unmanifested assets
- [x] Case-exactness named check (ordinal, meaningful on ext4)
- [x] Schema-validation tests (user/mod/copied entries validate; loading unimplemented, recorded)

### Step 3: `--verify-assets` self-check mode
**Status:** ✅ Complete

- [x] Diagnostic flag on real binary (bounded path; per-failure diagnostic line; exit 0/non-zero; no window/side effects)
- [x] Unit tests for manifest parse + outcome mapping
- [x] SP-003 startup invariants intact

### Step 4: Debug+Release output runs, WSL2 gate, budgets
**Status:** ✅ Complete

- [x] `--verify-assets` exit 0 on Debug + Release Windows binaries (recorded)
- [x] WSL2 gate: full testCommand green + Debug/Release Linux self-check exit 0 (case-exactness meaningful; session facts recorded)
- [x] Measured budgets (cold precondition verified; both platforms)
- [x] Harness integration decision recorded (verification-harness.md line or not — justified)

### Step 5: Evidence, board reconciliation, pre-completion consult
**Status:** 🔄 In Progress

- [ ] record.md written (incl. engine-review presence/absence)
- [ ] Pre-completion solo consult (Fable 5) run; verdict text persisted
- [ ] Board row 8 → `WIP` with evidence naming the publish-mode gate (not `DONE`)
- [ ] STATUS.md accurate

### Step 6: Testing & Verification
**Status:** ⬜ Pending

- [ ] Contract testCommand passes (solution + BOTH test projects)
- [ ] `git diff --check` clean
- [ ] Only File Scope paths changed
