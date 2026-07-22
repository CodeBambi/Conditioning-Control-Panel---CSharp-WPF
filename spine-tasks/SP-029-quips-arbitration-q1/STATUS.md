## STATUS: SP-029 — Quips/sound arbitration q1: arbitration core
**Current Step:** Step 1 — WPF arbitration archaeology + design + pre-approach consult
**Last Updated:** 2026-07-22 (Step 1 in progress)
**Blockers:** none

### Step 1: archaeology + design + pre-approach consult
- [x] Update STATUS.md before starting work
- [x] WPF archaeology (BarkService queue/freshness/priority/panic; ducking refcount; device paths; rapid-cue demands recorded)
- [x] Design (SoundArbitration: channel ownership, queue+freshness, ducking, device re-probe, off-sync-context; q1/q2 boundary)
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: arbitration core implementation
- [ ] SoundArbitration.cs + types (ownership state machine, queue, ducking, device layer)
- [ ] Unit tests (generations, ordering/freshness, ducking symmetry+panic, device re-probe, off-sync-context regression)

### Step 3: backend-event evidence + panic + WSL gate
- [ ] Windows backend-event evidence (voice/whisper/SFX pool/ducking/panic)
- [ ] WSL2 gate (contract green, Linux mechanism facts, leak counts)
- [ ] record.md complete
- [ ] Pre-completion solo consult
- [ ] STATUS.md accurate

### Step 4: verification
- [ ] testCommand green (verify.mjs exit 0; 0W/0E; ≥391/29 floor)
- [ ] git diff --check clean
- [ ] git status shows File Scope only
