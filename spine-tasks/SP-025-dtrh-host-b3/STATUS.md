## STATUS: SP-025 — DTRH host slice b3: native SFX/audio/video + freeze + rendered tint safety
**Current Step:** Step 1 — SFX/freeze/tint/video archaeology + design + package admission + pre-approach consult
**Last Updated:** 2026-07-21 (authored)
**Blockers:** none

### Step 1: archaeology + admission + pre-approach consult
- [ ] Update STATUS.md before starting work
- [ ] WPF archaeology (sfx/freeze/tint/native-video sites, `File.cs:line`)
- [ ] Payload protocol.js field verification for b3-owned messages
- [ ] Package admission gate (SoundFlow live-feed re-confirm; video backend decision)
- [ ] Design (DtrhNativeEffects, freeze semantics, divergence decision framing)
- [ ] Pre-approach solo consult (verdict + actual model in record.md)

### Step 2: native effects core
- [ ] DtrhNativeEffects.cs (SFX pool / freeze / tint mechanism)
- [ ] Unit tests (pool bounds, freeze idempotency/stale/unwedge, tint transitions, tolerance)

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
