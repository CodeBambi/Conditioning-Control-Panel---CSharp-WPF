## STATUS: SP-029 — Quips/sound arbitration q1: arbitration core
**Current Step:** complete — .DONE
**Last Updated:** 2026-07-22 (Step 4 verification green)
**Blockers:** none

### Step 1: archaeology + design + pre-approach consult — COMPLETE (plan review engine-skipped SP-195, recorded)
- [x] Update STATUS.md before starting work
- [x] WPF archaeology (BarkService queue/freshness/priority/panic; ducking refcount; device paths; rapid-cue demands recorded)
- [x] Design (SoundArbitration: channel ownership, queue+freshness, ducking, device re-probe, off-sync-context; q1/q2 boundary)
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: arbitration core implementation — COMPLETE (plan review engine-skipped SP-195, recorded)
- [x] SoundArbitration.cs + types (ownership state machine, queue, ducking, device layer)
- [x] Unit tests (generations, ordering/freshness, ducking symmetry+panic, device re-probe, off-sync-context regression) — 19 new, 410/410 green

### Step 3: backend-event evidence + panic + WSL gate — COMPLETE (plan review engine-skipped SP-195, recorded)
- [x] Windows backend-event evidence (voice/whisper/SFX pool/ducking/panic) — harness 28/28, leak delta 0/0
- [x] WSL2 gate (contract green 412/29, Linux mechanism facts 28/28, leak counts bounded)
- [x] record.md complete
- [x] Pre-completion solo consult (1 finding — panic/start race — FIXED + evidence regenerated)
- [x] STATUS.md accurate

### Step 4: verification — COMPLETE
- [x] testCommand green (verify.mjs exit 0; Rebuild 0W/0E; 412/412 + 29/29 — ≥391/29 floor)
- [x] git diff --check clean
- [x] git status shows File Scope only
