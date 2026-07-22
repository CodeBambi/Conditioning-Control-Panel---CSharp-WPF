## STATUS: SP-028 — T-5 local anchor-patch (parallelism enabler 1)
**Current Step:** Step 2 — manifest entry + apply/verify
**Last Updated:** 2026-07-22 (Step 1 complete pending plan review)
**Blockers:** none

### Step 1: archaeology + design + pre-approach consult
- [x] Update STATUS.md before starting work
- [x] Call-chain archaeology (live resolvePostLaneCommitPorcelain copy; .reviews/ write order; auto-commit non-sweep; review-scope precedent)
- [x] Historical derivation (all T-5 occurrences enumerated from journals)
- [x] Patch-shape design ((a) shared-check filter vs (b) finalization-only delete; anchor + replacement drafted — adapted to (b′) commitLaneWorktree per consult + resume-path evidence)
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: manifest entry + apply/verify
- [x] Manifest patch authored (anchor byte-exact, rationale, testedVersions — 2.8.0 verified via npm-pack scratch, full apply+verify green on it)
- [x] apply+verify green on live install; idempotence + loud-failure proofs
- [x] Patched live engine file committed — DEVIATION RECORDED: `.pi/npm/` is gitignored (.gitignore:50); engine tree is NOT tracked. Durable committed copy = manifest entry (the SP-020 mechanism's purpose); live install patched + verify exit 0

### Step 3: fixture + historical proof
- [x] Fixture (patched passes / pristine fails / deletion after verdicts) — 7/7 GREEN, transcript in evidence/
- [x] Consumer census + no-regression argument (4 consumers, all post-review finalization)
- [x] Proof boundary recorded (named post-land gate = next Level-2 batch)

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
