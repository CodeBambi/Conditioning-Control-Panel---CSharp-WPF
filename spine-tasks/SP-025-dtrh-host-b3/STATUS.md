## STATUS: SP-025 — DTRH host slice b3: native SFX/audio/video + freeze + rendered tint safety
**Current Step:** Step 3 — protocol upgrade Deferred → Handled
**Last Updated:** 2026-07-22 (Step 2 complete — 308/308 + 27/27, Rebuild 0W/0E)
**Blockers:** none

### Step 1: archaeology + admission + pre-approach consult
- [x] Update STATUS.md before starting work
- [x] WPF archaeology (sfx/freeze/tint/native-video sites, `File.cs:line`)
- [x] Payload protocol.js field verification for b3-owned messages
- [x] Package admission gate (SoundFlow live-feed re-confirm; video backend decision)
- [x] Design (DtrhNativeEffects, freeze semantics, divergence decision framing)
- [x] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: native effects core
- [x] DtrhNativeEffects.cs (SFX pool / freeze / tint mechanism)
- [x] Unit tests (pool bounds, freeze idempotency/stale/unwedge, tint transitions, tolerance)

### Step 3: protocol upgrade Deferred → Handled
- [ ] b3-owned messages wired to real effects
- [ ] Run lifecycle freeze invariants (start/end/teardown)
- [ ] Unit tests (dispatch, ordering, idempotency, Deferred remains for b4/b5)

### Step 4: headed/WX evidence + divergence executed + board + pre-completion consult
- [ ] DISPLAY3 headed evidence (SFX events, freeze/tint pixels, real media playback, teardown)
- [ ] WSL2 gate (contract green, WX facts, divergence decision executed on Linux)
- [ ] record.md complete
- [ ] Pre-completion solo consult
- [ ] Board row WIP with named limits
- [ ] STATUS.md accurate

### Step 5: verification
- [ ] testCommand green 0W/0E both platforms (≥292/27 floor)
- [ ] git diff --check clean
- [ ] git status shows File Scope only
