## STATUS: SP-034 — Stall-detector probe tooling
**Current Step:** Step 1 — incident evidence consolidation + probe design + pre-approach consult
**Last Updated:** 2026-07-22 (authored)
**Blockers:** none

### Step 1: evidence + design + pre-approach consult
- [ ] Update STATUS.md before starting work
- [ ] Threshold consolidation from both incidents (cited)
- [ ] Design (classification state machine, windows, scoping, output contract, self-test shape)
- [ ] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: probe script + self-test
- [ ] Tools/spine-worker-probe.ps1 (classification, evidence numbers, T-10 template)
- [ ] Self-test (live batch no false wedge; wedged simulation; crawling simulation)
- [ ] Skill-template amendment authored (manifest patch entry)

### Step 3: manifest patch + apply/verify + docs
- [ ] Manifest entry applied + verified (idempotent, loud-on-drift)
- [ ] record.md complete (script reproduced)
- [ ] Pre-completion solo consult
- [ ] STATUS.md accurate

### Step 4: verification
- [ ] testCommand green (verify.mjs exit 0; 0W/0E; ≥446/29 floor, no drift)
- [ ] git diff --check clean
- [ ] git status shows File Scope only
