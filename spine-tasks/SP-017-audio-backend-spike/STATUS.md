# STATUS: SP-017 — spike cross-platform audio channel backend

**Current Step:** Step 1 — backend research + package admission pre-approach consult
**Last Updated:** 2026-07-21 (authored)

### Step 1: Backend research + package admission pre-approach consult
**Status:** ⬜ Not Started

- [ ] STATUS.md updated before starting work
- [ ] WPF + first-attempt audio archaeology (READ-ONLY, `File.cs:line`)
- [ ] Candidate backends from live feeds: exact versions, licenses (+ packaging implications), native deps per OS, maintenance signal
- [ ] WSLg PulseAudio reality check (session facts)
- [ ] **Package admission solo Fable 5 consult** (1–2 cross-platform candidates + NAudio Windows reference admitted; verdict + actual answering model in record.md BEFORE checkbox)

### Step 2: Spike host + Windows evidence
**Status:** ⬜ Not Started

- [ ] `client/spikes/CcpSpike.Audio/` (console, out of solution; synthetic generated tones)
- [ ] Windows evidence per acceptance item, backend-event-verified: completion, interruption, pause/resume, bounded-latency overlapping SFX, whisper busy/completion, device enumerate/select/fallback, volume, teardown leak counts
- [ ] Identical comparison per backend; failures recorded as findings

### Step 3: WSLg/PulseAudio gate + packaging evidence
**Status:** ⬜ Not Started

- [ ] WSL2 in-packet gate (`~/ccp-sp017`, never /mnt/e): REAL evidence = enumerate/select/fallback, event-verified completion, teardown, packaging (`ldd` per .so); NAMED LIMIT = no latency/timing claims on WSLg
- [ ] Contract testCommand green on WSL2 (pollution guard)
- [ ] Self-contained publish win-x64 + linux-x64, native sidecars present/loading (session facts)

### Step 4: Selection record + board reconciliation + pre-completion consult
**Status:** ⬜ Not Started

- [ ] `client/docs/audio-backend-spike.md` — named observation per item per backend + matrix + explicit channel-ownership SELECTION (pending-owner)
- [ ] record.md complete
- [ ] Pre-completion solo Fable 5 consult (verdict in record.md)
- [ ] Board row → `WIP` with evidence + named limits (never `DONE`)
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification
**Status:** ⬜ Not Started

- [ ] Contract testCommand green (client 0W/0E + both test projects; spike host builds clean separately)
- [ ] `git diff --check` clean
- [ ] `git status --short` = File Scope only
