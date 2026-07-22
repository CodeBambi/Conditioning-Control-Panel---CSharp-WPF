## STATUS: SP-032 — Quips/sound arbitration q2: bark content pipeline + host wiring
**Current Step:** Step 4 — evidence consolidation + pre-completion consult
**Last Updated:** 2026-07-22 (Step 3 complete; plan review SKIPPED by engine, SP-195)
**Blockers:** none

### Step 1: archaeology + design + pre-approach consult
- [x] Update STATUS.md before starting work
- [x] WPF archaeology (rules engine, freshness/priority, payload assembly, mute, disabled-phrase persistence, pacing, rapid cues, stale-device UX)
- [x] Design (BarkPipeline over q1; disabled-phrase store on SP-005; pacing seam; DTRH bark upgrade; rapid-cue coexistence)
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: pipeline implementation + disabled-phrase persistence
- [x] BarkPipeline.cs + types (rules, variants, payload integrity, freshness/priority, mute degradation, pacing)
- [x] Disabled-phrase store on SP-005 machinery
- [x] Unit tests (payload integrity, ordering, mute, persistence round-trip+quarantine, pacing math, TryStart compliance)

### Step 3: rapid cues + DTRH wiring + backend-event evidence
- [x] Rapid click cues under voice/video (through-arbitration coexistence)
- [x] DTRH bark Deferred→Handled + dispatch seam (presence+shape logging; no b1–b5 regression)
- [x] WSL2 gate (contract green, mechanism facts)
- [x] Headed evidence per DISPLAY3/rect/modal/orphan rules if the host is used (headed host evidence NOT used — bindings do not arm; recorded)

### Step 4: evidence consolidation + pre-completion consult
- [x] record.md complete
- [ ] Pre-completion solo consult
- [x] STATUS.md accurate

### Step 5: verification
- [ ] testCommand green (verify.mjs exit 0; 0W/0E; ≥412/29 floor)
- [ ] git diff --check clean
- [ ] git status shows File Scope only
