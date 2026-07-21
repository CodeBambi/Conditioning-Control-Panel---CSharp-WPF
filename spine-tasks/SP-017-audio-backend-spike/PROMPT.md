# Task: SP-017 — spike cross-platform audio channel backend

## Mission

Execute `client/docs/task-board.md` row **"Spike cross-platform audio channel backend"** (P0, SECOND row of Phase 3 in `spine-tasks/CONTEXT.md`): compare maintained backends using exact versions/licenses/native dependencies; prove Windows/Linux companion voice completion/interruption/pause, bounded low-latency overlapping SFX, whisper busy/completion, output-device enumerate/select/fallback, volume, teardown, and packaging; select explicit channel ownership rather than one generic player. Deliverable: a quarantined spike host (`client/spikes/CcpSpike.Audio/`, OUT of the solution — SP-011 pattern) + `client/docs/audio-backend-spike.md` with a named observation per acceptance item and a recorded backend/channel-ownership SELECTION (spike outcome, pending-owner ratification like every spike row). **Zero product-code change.**

**Honesty framings (Phase 3 decomposition consult, binding):** (a) **WSLg PulseAudio scoping: enumerate/select/fallback + teardown + packaging = REAL Linux evidence; latency/overlap-timing numbers = Windows-headed ONLY, Linux latency/timing = named limit** (WSLg audio jitter — same discipline as SP-015's cadence scoping); (b) playback completion/interruption/pause/busy claims are made ONLY from backend-emitted events/positions cross-checked against a stopwatch — never from "the call returned" or sleeps; (c) packages are admitted via the in-packet pre-approach consult with exact versions/licenses/native deps FROM LIVE FEEDS (pinned, recorded); **license implications for the SP-010 packaging strategy are part of the comparison matrix** (e.g. LGPL copyleft obligations vs MIT-style); (d) the WPF incumbent (NAudio 2.2.1 + NAudio.Wasapi, Windows-only) is the Windows-reference baseline, not a candidate solution; (e) no Wayland claim (§5.1); WSLg = X11/PulseAudio session facts recorded honestly.

## Dependencies

- **Task:** SP-016 (Phase-3 serial chain)

## Context to Read First

- `client/docs/task-board.md` — the audio spike row + Decisions-needed + gate history (Phase 3 decomposition verdict with the WSLg scoping)
- `client/docs/runtime-capability-contract.md` (SP-006) — honesty rule (degraded-truthful > fake-available; backend events ≠ OS registration)
- `client/docs/release-publish-gates.md` (SP-010) — natives-beside-exe packaging strategy the license/native-deps matrix must speak to
- WPF sources (READ-ONLY, `File.cs:line`): `ConditioningControlPanel/Services/Audio/`, `Services/AudioService.cs`, `Services/AudioSyncService.cs`, `Services/AutonomyService.Voice.cs`, `Services/MantraVoiceService.cs`, `Services/Bark/` — the behavioral evidence for voice completion/interruption, overlapping SFX, whisper busy/completion, ducking, device fallback (NAudio 2.2.1/NAudio.Wasapi 2.2.1 incumbent)
- First attempt (READ-ONLY, lessons-only): `ConditioningControlPanel/CCP.Core/Services/Audio/` (`WhisperVoicePlayer.cs`, `WhisperAudioBusyness.cs`, `AudioAnalyzer.cs`) + `client/docs/first-attempt-lessons.md` — cite audio REJECT lessons explicitly (channel ownership, timer/bus patterns)
- `spine-tasks/SP-011-webview-dtrh-spike/record.md` — quarantined-spike pattern (out-of-solution host, package admission consult, named observation per acceptance item)
- Required skills: load `wpf-parity` before Step 1

## File Scope

- `client/spikes/CcpSpike.Audio/**` (quarantined spike host — NOT added to `client/CcpClient.sln`)
- `client/docs/audio-backend-spike.md` (evidence deliverable + selection record)
- `client/docs/task-board.md` (row evidence edit only)
- `spine-tasks/SP-017-audio-backend-spike/**` (STATUS.md, record.md, evidence, .DONE)

## Contract

| Field | Value |
|-------|-------|
| testCommand | `dotnet build client/CcpClient.sln -c Debug --nologo && dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo && dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` |
| fileScopeMustChange | `client/docs/audio-backend-spike.md`, `client/spikes/CcpSpike.Audio/CcpSpike.Audio.csproj` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/CcpClient.sln`, `client/src/**`, `client/tests/**`, `.spine/**` |
| artifactsMustExist | `client/docs/audio-backend-spike.md`, `spine-tasks/SP-017-audio-backend-spike/record.md` |

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. **T-2 heading format is load-bearing** — record engine-review presence/absence per call in record.md.

## Steps

### Step 1: Backend research + package admission pre-approach consult

- [ ] Update STATUS.md before starting work
- [ ] WPF + first-attempt audio archaeology (READ-ONLY, `File.cs:line`): voice completion/interruption/pause semantics, overlapping SFX bounds, whisper busy/completion signaling, ducking, device enumerate/select/fallback, channel ownership — what the product actually needs, with evidence
- [ ] Candidate backends FROM LIVE FEEDS (nuget.org + project repos, exact current versions): NAudio (Windows-reference only), PortAudio bindings (e.g. PortAudioSharp or current maintained fork), Silk.NET.OpenAL, and any other maintained cross-platform .NET audio output the research surfaces — per candidate: exact version, LICENSE (with packaging/copyleft implication for SP-010's natives-beside-exe strategy), native dependencies per OS (Windows inbox vs apt package names on Ubuntu), maintenance signal (last release, repo activity)
- [ ] WSLg audio reality check: PulseAudio server present on the WSL2 image? Which apt packages provide the candidate natives? Record as session facts
- [ ] **Pre-approach solo consult = PACKAGE ADMISSION GATE** (Fable 5, solo; council unavailable T-7): candidates + license/native matrix + spike design (channel-ownership test shape, event-based completion/latency measurement); admit 1–2 cross-platform candidates + the NAudio Windows reference. Verdict text + ACTUAL answering model in record.md BEFORE checkbox. Keep questions few/pointed

### Step 2: Spike host + Windows evidence

- [ ] `client/spikes/CcpSpike.Audio/` (console, NOT in the solution; synthetic test assets — generated WAV/PCM tones with machine-checkable durations, no copyrighted fixtures, no WPF asset copying)
- [ ] Windows evidence per acceptance item, backend-event-verified: companion-voice-style playback completion (end event ≈ asset duration), interruption (stop mid-stream → position < duration, no late end event), pause/resume (position freeze + successor continuation), bounded low-latency overlapping SFX (N rapid short triggers overlap; per-trigger start-latency distribution measured via backend play-position/callback — numbers recorded with the measurement method), whisper busy/completion (busy flag transitions), output-device enumerate/select/fallback (real enumeration; select non-default; unplug-simulated or invalid-device fallback path), volume (per-channel gain effect verified via backend meter/level where the backend exposes it, else named mechanism-only), teardown (no device leaks across M cycles — handle/session counts via the backend or OS)
- [ ] Per-backend comparison executed identically; failures recorded as findings, never patched over

### Step 3: WSLg/PulseAudio gate + packaging evidence

- [ ] WSL2 in-packet gate (native-dir `~/ccp-sp017`, never /mnt/e): spike host builds and runs; **REAL evidence: device enumerate/select/fallback, playback completion via backend events, teardown, packaging (native deps resolved beside the artifact — `ldd` per shipped .so per SP-010)**; **NAMED LIMIT: latency/overlap-timing numbers not claimed on WSLg** (jitter)
- [ ] Contract testCommand ALSO green on WSL2 (pollution guard both platforms)
- [ ] Packaging evidence: spike published self-contained per SP-010 strategy on win-x64 + linux-x64; native sidecars present and loading (session facts)

### Step 4: Selection record + board reconciliation + pre-completion consult

- [ ] `client/docs/audio-backend-spike.md` — named observation per acceptance item per backend + the license/native-deps/maintenance matrix + **explicit channel-ownership SELECTION** (voice channel, SFX channel, whisper channel — owners, queueing/interruption semantics; ONE generic player REJECTED per the row) recorded as spike outcome pending-owner
- [ ] Write `spine-tasks/SP-017-audio-backend-spike/record.md` (archaeology, package admission verdict, versions/licenses/natives as admitted, measurement methods, budgets, surprises)
- [ ] **Pre-completion solo consult** (Fable 5, solo) on the evidence + selection; verdict text in record.md
- [ ] Update `client/docs/task-board.md` row → `WIP` with evidence + named limits (Linux latency/timing, Wayland §5.1, selection pending-owner) — row never `DONE`
- [ ] STATUS.md accurate before .DONE

### Step 5: Testing & Verification

- [ ] Contract testCommand passes (client build 0W/0E + both test projects green — pollution guard; spike host builds clean separately)
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths

## Completion Criteria

- Every acceptance item has a named observation per admitted backend (or an honest not-supported finding): voice completion/interruption/pause, bounded low-latency overlapping SFX (Windows numbers + method), whisper busy/completion, device enumerate/select/fallback, volume, teardown, packaging
- Backend SELECTION recorded with explicit channel ownership (voice/SFX/whisper owners), license/native-deps/maintenance matrix, pending-owner
- WSLg real evidence: enumerate/select/fallback + completion + teardown + packaging; Linux latency/timing named-limit; Wayland untouched
- Quarantine holds: zero `client/src/**`/`client/tests/**`/`client/CcpClient.sln` changes; contract green both platforms; both solo Fable consults persisted with actual answering models; board row `WIP` (not `DONE`)

## Do NOT

- Add the spike to `client/CcpClient.sln` or touch product code/tests; use NAudio as the cross-platform answer (Windows-only incumbent = reference baseline); copy WPF audio assets (generate synthetic tones); claim latency/timing on WSLg; claim Wayland; make network calls beyond package research/restore; log sensitive data
- Answer owner questions (final backend ratification, channel-ownership values, latency bounds as product constants — record pending-owner); modify `ConditioningControlPanel/**`, `.spine/`, `AGENTS.md`, `CLAUDE.md`, `.gitnexus/`; set any board row `DONE`
- Use `consult` council mode (route broken — solo Fable 5 only)

## Git Commit Convention

- `feat(SP-017): complete Step N — <summary>` at step boundaries

## Documentation Requirements

**Must Update:** `client/docs/audio-backend-spike.md` (deliverable), `client/docs/task-board.md` (row evidence), `spine-tasks/SP-017-audio-backend-spike/record.md`
**Check If Affected:** `client/docs/port-lessons.md` (durable surprises only)

## Amendments

- 2026-07-21 (authoring): **Phase 3 decomposition consult verdicts applied (solo Fable 5):** audio spike SECOND (unblocks the quips/sound BLOCKED row — highest downstream value); WSLg PulseAudio scoping binding (enumerate/select/fallback/teardown/packaging REAL, latency/overlap numbers Windows-headed only, Linux latency named limit); serial execution. T-11 sizing: Step 2 (Windows evidence) and Step 3 (WSLg gate) are separate <2h steps; orchestrator sets `SPINE_WORKER_PI_TIMEOUT_MS=14400000` at launch (audio evidence runs are real-device work).
- 2026-07-21 (authoring): `## Review Level: 2` structured heading emitted (T-2 fixed format). Launch: validate → analyze → plan → preflight → detached batch per owner cycle.
