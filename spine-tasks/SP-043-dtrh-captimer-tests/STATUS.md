## STATUS: SP-043 — T-16: DTRH cap-timer tests deterministic timing
**Current Step:** Step 1 — timing archaeology + fix design + pre-approach consult
**Last Updated:** 2026-08-04 (step 1 in progress)
**Blockers:** none

### Step 1: archaeology + design + pre-approach consult
- [x] Update STATUS.md before starting work
- [x] Every timing dependency classified (wall-clock poll / real timer / sleeping assertion)
- [x] Design (deterministic shapes per dependency; loud classifier; conditional seam)
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: implement the timing discipline
- [ ] DtrhNativeEffectsTests.cs conversions (assertion meanings unchanged)
- [ ] Conditional product seam (additive-only, real-clock default, justified)
- [ ] Full DTRH test class green

### Step 3: stability proof + evidence + pre-completion consult
- [ ] 10 consecutive full-suite runs zero cap-timer reds (transcripts)
- [ ] Any other flake named via TRX + recorded (never silently absorbed)
- [ ] record.md (classifications, design, transcripts, consults, review presence)
- [ ] Pre-completion solo consult (verdict + actual model in record.md)
- [ ] STATUS.md accurate before .DONE

### Step 4: Testing & Verification
- [ ] Contract testCommand passes (verify.mjs + build 0W/0E + ≥537/29 floor)
- [ ] git diff --check clean
- [ ] git status --short shows only File Scope paths
