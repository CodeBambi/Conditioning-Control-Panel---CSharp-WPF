## STATUS: SP-043 — T-16: DTRH cap-timer tests deterministic timing
**Current Step:** complete (all 4 steps; pre-.DONE)
**Last Updated:** 2026-08-04 (steps 3-4 complete: 10/10 chain green, contract green)
**Blockers:** none

### Step 1: archaeology + design + pre-approach consult — COMPLETE
- [x] Update STATUS.md before starting work
- [x] Every timing dependency classified (wall-clock poll / real timer / sleeping assertion)
- [x] Design (deterministic shapes per dependency; loud classifier; conditional seam)
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: implement the timing discipline — COMPLETE
- [x] DtrhNativeEffectsTests.cs conversions (assertion meanings unchanged)
- [x] Conditional product seam (additive-only, real-clock default, justified)
- [x] Full DTRH test class green

### Step 3: stability proof + evidence + pre-completion consult — COMPLETE
- [x] 10 consecutive full-suite runs zero cap-timer reds (transcripts)
- [x] Any other flake named via TRX + recorded (never silently absorbed)
- [x] record.md (classifications, design, transcripts, consults, review presence)
- [x] Pre-completion solo consult (verdict + actual model in record.md)
- [x] STATUS.md accurate before .DONE

### Step 4: Testing & Verification — COMPLETE
- [x] Contract testCommand passes (verify.mjs + build 0W/0E + ≥537/29 floor)
- [x] git diff --check clean
- [x] git status --short shows only File Scope paths
