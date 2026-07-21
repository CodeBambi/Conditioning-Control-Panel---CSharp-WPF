# STATUS: SP-017 — spike cross-platform audio channel backend

**Current Step:** Step 4 — selection record + board reconciliation + pre-completion consult
**Last Updated:** 2026-07-21 (Step 3 complete — WSLg + packaging green both RIDs)

### Step 1: Backend research + package admission pre-approach consult
**Status:** ✅ Complete

- [x] STATUS.md updated before starting work
- [x] WPF + first-attempt audio archaeology (READ-ONLY, `File.cs:line`)
- [x] Candidate backends from live feeds: exact versions, licenses (+ packaging implications), native deps per OS, maintenance signal
- [x] WSLg PulseAudio reality check (session facts)
- [x] **Package admission solo Fable 5 consult** (SoundFlow 1.4.1 + Silk.NET.OpenAL 2.23.0/Soft.Native 1.23.1 + NAudio 2.2.1 Windows reference ADMITTED with 8 binding corrections; verdict + Fable 5 in record.md)

### Step 2: Spike host + Windows evidence
**Status:** ✅ Complete

- [x] `client/spikes/CcpSpike.Audio/` (console, out of solution; synthetic generated tones)
- [x] Windows evidence per acceptance item, backend-event-verified: completion, interruption, pause/resume, bounded-latency overlapping SFX, whisper busy/completion, device enumerate/select/fallback, volume, teardown leak counts
- [x] Identical comparison per backend; failures recorded as findings (SoundFlow wild-Id AV crash, NAudio Stop-fires-event, Silk enumeration limit, SFX measurement artifact + fix)

### Step 3: WSLg/PulseAudio gate + packaging evidence
**Status:** ✅ Complete

- [x] WSL2 in-packet gate (`~/ccp-sp017`, never /mnt/e): REAL evidence = enumerate/select/fallback (RDP Sink), event-verified completion, teardown, packaging (`ldd` per .so); NAMED LIMIT = no latency/timing claims on WSLg
- [x] Contract testCommand green on WSL2 (0W/0E + 213/213 + 22/22 — pollution guard)
- [x] Self-contained publish win-x64 + linux-x64, native sidecars present/loading (miniaudio.dll/libminiaudio.so, soft_oal.dll/libopenal.so — session facts)

### Step 4: Selection record + board reconciliation + pre-completion consult
**Status:** 🔄 In Progress

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
