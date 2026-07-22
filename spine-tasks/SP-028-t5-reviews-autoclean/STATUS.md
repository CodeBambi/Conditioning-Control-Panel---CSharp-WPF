## STATUS: SP-028 — T-5 local anchor-patch (parallelism enabler 1)
**Current Step:** Step 1 — T-5 call-chain archaeology + patch-shape design + pre-approach consult
**Last Updated:** 2026-07-22 (authored)
**Blockers:** none

### Step 1: archaeology + design + pre-approach consult
- [ ] Update STATUS.md before starting work
- [ ] Call-chain archaeology (live resolvePostLaneCommitPorcelain copy; .reviews/ write order; auto-commit non-sweep; review-scope precedent)
- [ ] Historical derivation (all T-5 occurrences enumerated from journals)
- [ ] Patch-shape design ((a) shared-check filter vs (b) finalization-only delete; anchor + replacement drafted)
- [ ] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: manifest entry + apply/verify
- [ ] Manifest patch authored (anchor byte-exact, rationale, testedVersions)
- [ ] apply+verify green on live install; idempotence + loud-failure proofs
- [ ] Patched live engine file committed

### Step 3: fixture + historical proof
- [ ] Fixture (patched passes / pristine fails / deletion after verdicts)
- [ ] Consumer census + no-regression argument
- [ ] Proof boundary recorded (named post-land gate)

### Step 4: docs + board + pre-completion consult
- [ ] README row
- [ ] port-lessons entry
- [ ] Board tooling row T-5 → CLOSED-by-patch + named post-land gate
- [ ] record.md complete
- [ ] Pre-completion solo consult
- [ ] STATUS.md accurate

### Step 5: verification
- [ ] testCommand green (verify.mjs exit 0; client 0W/0E; ≥391/29 floor, no drift)
- [ ] git diff --check clean
- [ ] git status shows File Scope only
