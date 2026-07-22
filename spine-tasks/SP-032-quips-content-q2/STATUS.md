## STATUS: SP-032 — Quips/sound arbitration q2: bark content pipeline + host wiring
**Current Step:** Step 1 — content-pipeline archaeology + design + pre-approach consult
**Last Updated:** 2026-07-22 (authored)
**Blockers:** none

### Step 1: archaeology + design + pre-approach consult
- [ ] Update STATUS.md before starting work
- [ ] WPF archaeology (rules engine, freshness/priority, payload assembly, mute, disabled-phrase persistence, pacing, rapid cues, stale-device UX)
- [ ] Design (BarkPipeline over q1; disabled-phrase store on SP-005; pacing seam; DTRH bark upgrade; rapid-cue coexistence)
- [ ] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: pipeline implementation + disabled-phrase persistence
- [ ] BarkPipeline.cs + types (rules, variants, payload integrity, freshness/priority, mute degradation, pacing)
- [ ] Disabled-phrase store on SP-005 machinery
- [ ] Unit tests (payload integrity, ordering, mute, persistence round-trip+quarantine, pacing math, TryStart compliance)

### Step 3: rapid cues + DTRH wiring + backend-event evidence
- [ ] Rapid click cues under voice/video (through-arbitration coexistence)
- [ ] DTRH bark Deferred→Handled + dispatch seam (presence+shape logging; no b1–b5 regression)
- [ ] WSL2 gate (contract green, mechanism facts)
- [ ] Headed evidence per DISPLAY3/rect/modal/orphan rules if the host is used

### Step 4: evidence consolidation + pre-completion consult
- [ ] record.md complete
- [ ] Pre-completion solo consult
- [ ] STATUS.md accurate

### Step 5: verification
- [ ] testCommand green (verify.mjs exit 0; 0W/0E; ≥412/29 floor)
- [ ] git diff --check clean
- [ ] git status shows File Scope only
