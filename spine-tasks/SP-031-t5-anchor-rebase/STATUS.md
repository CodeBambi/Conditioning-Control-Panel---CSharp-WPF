## STATUS: SP-031 — T-5 anchor re-base (provenance-faithful fixture)
**Current Step:** Step 1 — failure forensics + re-base design + pre-approach consult
**Last Updated:** 2026-07-22 (authored)
**Blockers:** none

### Step 1: forensics + design + pre-approach consult
- [ ] Update STATUS.md before starting work
- [ ] Call-site forensics (real worktreePath/projectRoot/taskFolder values; correct lane-task-folder expression + edge cases)
- [ ] Design (new anchor/replacement; live-tree migration path; post-land gate re-point)
- [ ] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: manifest re-base + migration + apply/verify
- [ ] Manifest re-based
- [ ] Live-tree migration executed + apply/verify exit 0 + idempotence + loud-failure proofs

### Step 3: provenance-faithful fixture + regression proof
- [ ] Fixture v2 (base-shaped taskFolder; negative control preserved; caller-shape regression pass)
- [ ] Consumer census re-confirmed
- [ ] Boundary re-recorded (named post-land gate re-pointed)

### Step 4: docs + pre-completion consult
- [ ] README row
- [ ] record.md complete
- [ ] Pre-completion solo consult
- [ ] STATUS.md accurate

### Step 5: verification
- [ ] testCommand green (verify.mjs exit 0; 0W/0E; ≥412/29 floor, no drift)
- [ ] git diff --check clean
- [ ] git status shows File Scope only
