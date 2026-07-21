# SP-017 record — spike cross-platform audio channel backend

Worker session log. Design decisions, consult verdicts (provenance), research citations, measurement methods, surprises, engine-review presence (T-2).

## Engine-review presence log (T-2)

Packet emits structured `## Review Level: 2` heading. Per-call record:

| Step | spine_review_step call | Result | Engine review fired? |
|------|------------------------|--------|----------------------|
| 1 | type=plan after step-1 commit | `skipped: true`, `spawnFailed: false` — "Nested reviewer spawn blocked inside pi worker session (SP-195); batch engine runs reviews after worker success" | NO in-worker (by design); post-.DONE engine review observable at land time |
| 2 | type=plan after step-2 commit | `skipped: true`, `spawnFailed: false` (same SP-195 skip) | NO in-worker |
| 3 | type=plan after step-3 commit | `skipped: true`, `spawnFailed: false` (same SP-195 skip) | NO in-worker |

## Step 1 — backend research + package admission pre-approach consult

### WPF + first-attempt archaeology (READ-ONLY, via wpf-archaeologist subagent + direct reads)

WPF findings (File.cs:line evidence in the subagent report; key rows):

- **Companion voice** = ONE exclusive channel: `PlaySpokenAudio` — one `AudioFileReader` + one `WaveOutEvent`, stop-replace ("two lines never overlap"), completion via background poll (`PlaybackState != Stopped`, 40ms sleep), pause/resume for world-freeze (`AvatarTubeWindow.Speech.cs:1585-1669`). Consumers poll `IsSpeakingAudio` (`AutonomyService.VoiceCommands.cs:1028-1036`). No PlaybackStopped event on this channel.
- **SFX** = N overlapping one-shots, one `WaveOutEvent` per cue, OS-mixed (no `MixingSampleProvider` anywhere). Bounds: bubble-pop pool max 4 devices (`BubbleService.cs:1881-1884`), ChaosSfx hard voice cap 6, excess DROPPED not queued (`ChaosSfx.cs:91-107` — cap added after crash dumps showed 6-15 threads in audio storms). Fire-and-forget, no completion needed.
- **Whisper/subliminal** = exclusive stop-replace (`SubliminalService.cs:514-548`), completion via REAL `PlaybackStopped` event (drives unduck+500ms), PLUS a duration-estimate busy window (`AudioService.MarkWhisperAudio`/`IsWhisperAudioPlaying`, `AudioService.cs:734-764` — Interlocked CAS, only extends; bark gate consults it at `BarkService.cs:1342-1344`). Flash audio also marks it (`FlashService.cs:946`).
- **Ducking** = reference-counted (`_duckCount`/`_duckGeneration`, `AudioService.cs:766-1036`), WASAPI per-session `SimpleAudioVolume` across all render endpoints, watchdog + crash-recovery JSON. Companion voice does NOT duck; whispers/video/narrator/keyword do. (Windows-only mechanism — Linux equivalent is a product-layer/platform concern, out of backend scope.)
- **Output devices** = `MMDeviceEnumerator` + settings `AudioOutputDeviceId/Name` (`AppSettings.cs:987,996`); synthetic "System default" entry; name-prefix matching to WaveOut numbers; fallback: missing chosen device → default; `BadDeviceId` scan → first openable device; none → audio disabled for session (`AudioService.cs:86-361`). NOTE: `PlaySpokenAudio` and several avatar one-shots do NOT apply the preferred device — likely WPF bug, flagged as port decision, not copied.
- **Bark/quip audio** = text + optional audio per variant; ordinary barks queue TEXT (`_speechQueue`), audio channel stop-replaces; priority preempts (clears queue); gates all DROP (whisper-active, narrator-playing, min-gap, cooldown, chance) (`BarkService.cs:1336-1392,1616-1624`).
- **NAudio inventory**: `NAudio` 2.2.1 + `NAudio.Wasapi` 2.2.1 (`ConditioningControlPanel.csproj:77-78`). Used: `WaveOutEvent` (every path), `AudioFileReader`, `MediaFoundationReader`, `WaveFileReader`/`Mp3FileReader` (duration probes), `MMDeviceEnumerator`/`AudioSessionControl`/`SimpleAudioVolume` (ducking). NOT used: `WasapiOut`, `MixingSampleProvider`. MP3 decode matters (barks/voice clips are MP3).
- Channel-need table (voice exclusive stop-replace precise completion; whisper exclusive stop-replace event+busy; SFX N-overlap capped drop fire-and-forget; narrator/enhancement estimates) recorded in the subagent report, mirrored into the spike doc's selection rationale.

First-attempt (REJECT lessons, cited):
- `CCP.Avalonia/Platform/AvaloniaAudioPlayer.cs:44-76` — LibVLC generic player, `StopInternal()` before every play (replace-on-play), `PlayAsync` returns `Task.CompletedTask` immediately = no truthful completion.
- `CCP.Avalonia/Platform/AvaloniaSfxPlayer.cs:41-141` — detached `Task.Run` per cue, new MediaPlayer per cue, `Thread.Sleep(30)` state polling, best-effort device routing = the rejected unbounded player-per-cue pattern.
- `first-attempt-lessons.md`: "REJECT one generic replace-on-play player… ADAPT separate voice/SFX/whisper/media concepts into explicit channel ownership with real lifecycle outcomes"; "REJECT one generic player can own quips, voice, whispers, and test audio".
- Portable pieces worth noting (not backend selection): `CCP.Core/Services/Audio/WhisperAudioBusyness.cs` (byte-identical WPF busy-window algorithm) and `WhisperVoicePlayer.cs` (play/mark/duck decision) — the busy-window ALGORITHM is WPF parity; the spike tests whether the backend can replace the estimate with real completion events.

### Candidate backends from live feeds (2026-07-21)

| Backend | Exact version | Published | License | Native deps (Windows / Ubuntu) | Maintenance signal |
|---|---|---|---|---|---|
| NAudio + NAudio.Wasapi (Windows REFERENCE only) | 2.2.1 (WPF incumbent pins; latest feed: 3.0.0-preview.18) | 2.2.1: 2023-09-04 | MIT (naudio/NAudio repo) | None — Windows inbox (winmm/wasapi) / N-A | Active (repo pushed 2026-07-20) |
| SoundFlow | 1.4.1 | 2026-05-11 | MIT (LICENSE.md in nupkg) | BUNDLED libminiaudio per-RID (win-x64 miniaudio.dll, linux-x64 libminiaudio.so) — flows into SP-010 natives-beside-exe automatically; miniaudio = public-domain/MIT-0; runtime dlopens libpulse/libasound (WSLg libpulse0 already installed) | **RISK: README — "maintainer on hiatus Jan 2026–Feb 2027, limited support"** (repo pushed 2026-05-11); net8.0-only TFM (compatible with net10.0) |
| Silk.NET.OpenAL + Silk.NET.OpenAL.Soft.Native | 2.23.0 / 1.23.1 | 2026-01-23 / Apr 2024 | Bindings MIT / native **LGPL-2.0-or-later** (`requireLicenseAcceptance=true`) | BUNDLED OpenAL Soft per-RID (soft_oal.dll, libopenal.so); LGPL sidecar obligations: notice + source-availability offer for OpenAL Soft; natives-beside-exe layout naturally satisfies replaceability | Very active (Silk.NET repo pushed 2026-07-18) |
| PortAudioSharp — REJECTED | 0.3.0 | 2020-06-24 | — | needs system libportaudio2 (apt candidate 19.7.0 exists) | DEAD — 6 years, stale nuspec projectUrl points at unrelated Bassoon project |
| ManagedBass — REJECTED | 4.0.2 | 2025-10-03 | wrapper maintained; BASS native PROPRIETARY (NOASSERTION) | un4seen BASS per-OS | Commercial redistribution licensing incompatible with this Patreon-funded product |
| CSCore — REJECTED | 1.2.1.2 | 2017-10-22 | — | Windows-only | DEAD |
| LibVLCSharp — REJECTED for audio channels | 3.10.0 | current | LGPL native | already in repo for VIDEO | Backend shape disqualifier: per-instance media player, no in-process sample mixer, no low-latency one-shot path, completion via state polling — the wrong shape for bounded SFX polyphony (NOT a blame-on-LibVLC for the first attempt's generic-player misuse; consult correction) |

Decoder matrix row (doc-level, per consult — spike runtime-proves WAV only): SoundFlow decodes wav/mp3/flac via miniaudio built-ins; OpenAL is buffers-only — a product on that path needs a separate managed decoder (MP3 barks/voice are a WPF fact: `Mp3FileReader`/`MediaFoundationReader`).

### WSLg audio reality check (session facts, 2026-07-21)

- `PULSE_SERVER=unix:/mnt/wslg/PulseServer` SET; `/mnt/wslg/PulseServer` socket present (WSLg system distro serves PulseAudio). `libpulse0` 17.0 INSTALLED; `pactl`/`pulseaudio-utils` NOT installed; no pulse/pipewire user process in the WSL2 distro (server lives in the WSLg system distro).
- apt candidates on Ubuntu 26.04: `libportaudio2` 19.7.0+git20260206, `libopenal1` 1.25.1, `pulseaudio`/`pulseaudio-utils` 17.0 — all available but NONE required by the admitted backends (both bundle natives; miniaudio dlopens libpulse which is present).
- Consequence (consult): WSLg enumeration evidence must come from the backend itself (no pactl).

### Pre-approach consult — PACKAGE ADMISSION GATE (solo Fable 5, 2026-07-21)

Presented: candidate matrix above + rejections + spike design (one device/context + per-channel players/mixers; backend-event-verified completion/interruption/pause; N=8 SFX overlap with per-trigger start-latency; invalid-device fallback; Windows-only latency numbers).

**VERDICT: ADMISSION APPROVED with corrections — SoundFlow 1.4.1 (primary) + Silk.NET.OpenAL 2.23.0 / Soft.Native 1.23.1 (secondary) + NAudio 2.2.1/NAudio.Wasapi 2.2.1 (Windows reference baseline only). Rejections all sound.** Binding corrections (verbatim-gist; response truncated in transit during Q2-item-4 — truncation recorded for provenance, completion declared below):

1. SoundFlow hiatus = recorded matrix risk, NOT admission blocker; the hiatus line goes VERBATIM in the maintenance column (audio is a long-lived P0 subsystem on a solo-maintainer library — don't bury it). net8.0-only TFM recorded.
2. LGPL OpenAL Soft sidecar acceptable AS SPIKE CANDIDATE; obligations recorded (LGPL-2.0-or-later notice + source-availability offer + `requireLicenseAcceptance=true`); contrast miniaudio public-domain/MIT-0 vs LGPL explicitly — a real SP-010 differentiator and likely selection input.
3. LibVLCSharp rejection rephrased: disqualifier is BACKEND SHAPE (per-instance media player, no in-process mixer, no low-latency one-shot path, state-poll completion), not the first attempt's misuse.
4. Missing matrix row added: DECODER support (doc-level; spike runtime-proves WAV only).
5. Latency quantization: record `PeriodSizeInMilliseconds`/buffer config; measurement resolution = polling interval + period size; no sub-period precision claims.
6. Stop-vs-ended discrimination: interruption probe RESTATED — after mid-stream stop, either no end event, or an end event distinguishable from natural completion (state/position/flag); record which. If Stop() fires PlaybackEnded that's a FINDING, not a failure; establish whether interrupt≠completion needs a synthesized generation token.
7. Falsifiable completion window: declared tolerance BEFORE running (duration + one period + scheduling slop, number named in spike doc); generated WAVs sample-exact (frames/sampleRate). OpenAL has NO completion callback — completion = `AL_SOURCE_STATE`→STOPPED observed by POLL; named as polling in the method column (within the honesty framing "backend-emitted events/positions").
8. Device fallback = invalid-device fallback (fabricated/stale DeviceInfo; `alcOpenDevice("bogus")` → null → default path), recorded as invalid-device fallback, never "unplug". WSLg enumeration evidence from the backend itself.

**Truncation completion (declared, faithful to verdict shape):** the response cut mid-item-8 ("enumerating the PulseServ…"); the item-8 completion above is the obvious remainder. Q3 (one device/context vs per-channel) received no visible answer before truncation → worker proceeds with the declared ONE playback device/context + per-channel players/mixer shape: it matches the WPF OS-mixing behavior (N WaveOutEvents on one default endpoint), exercises the row's explicit-channel-ownership requirement with the fewest moving parts, and per-device-per-channel multiplies handles without any WPF behavioral requirement. Owner ratifies the final topology with the spike evidence.

## Step 2 — spike host + Windows evidence

### Host shape
- `client/spikes/CcpSpike.Audio/` — net10.0 + net10.0-windows multi-TFM console, NOT in `client/CcpClient.sln` (verified), inherits `client/Directory.Build.props` only. Two TFMs because NAudio 2.2.1 ships `WaveOutEvent`/MMDevice in windows-TFM assemblies ONLY (verified by reflection on the restored nupkg — `net6.0/NAudio.dll` has no `WaveOut`/`WaveOutEvent`); the net10.0 build compiles an honest not-supported NAudio stub.
- Packages exactly as admitted: SoundFlow 1.4.1, Silk.NET.OpenAL 2.23.0 + Soft.Native 1.23.1, NAudio 2.2.1 + NAudio.Wasapi 2.2.1. Clean restore, 0W/0E build both TFMs.
- Synthetic tones generated at runtime (`ToneGen`): sample-exact 48 kHz PCM16 mono — voice 2500 ms, SFX 300 ms, whisper 1500 ms (frames asserted divisible; expected durations exact per consult item 7). No WPF assets, no copyrighted fixtures.
- Identical probe suite per backend (`Program.RunBackend`): devices enumerate/select/invalid-fallback → volume → voice completion → interruption → pause/resume → SFX overlap ×8 → whisper busy → teardown ×10 cycles. JSONL per observation; shared monotonic clock.

### Worker bugs found+fixed during the runs (worker-owned, recorded so the evidence is never misread)
- **Monotonic-clock bug (the big one):** `NowMs()` initially computed `Stopwatch.GetElapsedTime(GetTimestamp())` ≈ 0 always → every `WaitFor` with a null probe became an infinite loop (300s hang) and early end-signal stamps were garbage ("completion at 4.5ms"). Fixed to `GetTimestamp()*1000/Frequency`. The 4.5ms completion value in the first full run was THIS bug, not a backend behavior.
- **SFX measurement artifact (F4):** poll-after-all-triggers + 120ms clip → clips finished before first poll read 0 offsets (OpenAL "4/8 started" false negative) and a spurious ~30ms/index latency ladder on ALL backends. Fixed: interleaved polling in the spacing gaps + 300ms clip → ladder collapsed to tight distributions (soundflow 13.7-15.9ms, openal 15.0-28.9ms, naudio 0.5-12.8ms).
- Volatile-field compile iterations (double?/long can't be volatile → Volatile.Read/Write on plain long ticks); NAudio bogus-id FormatException (harness boundary must not throw — maps to out-of-range device number probe).
- `--diag` isolation mode timed SoundFlow Stop/Remove/Dispose individually (all instant — the hang was the clock bug, NOT a SoundFlow blocking Stop; that hypothesis was FALSIFIED before being recorded as a backend fact).

### Backend findings (product-relevant, preserved in the deliverable)
- **F1 SoundFlow wild-Id AV crash** (0xC0000005, 2× observed; native stack `SoundFlow.Backends.MiniAudio.Native.DeviceInit` ← `MiniAudioEngine.InitializePlaybackDevice` ← `SoundFlowHarness.TryInit`): unvalidated DeviceInfo.Id is process-fatal; ids are process-lifetime POINTERS (differ across runs) → validate against enumeration, match by NAME (WPF FriendlyName parity). Fallback moved to the validation layer and proven there.
- **F2 NAudio fires PlaybackStopped on explicit Stop()** (raw +1) — interrupt≠completion needs identity/generation filtering; SoundFlow/OpenAL fired ZERO events on stop. Harness implements player-identity filtering (raw count + filtered signal both recorded per probe).
- **F3 Silk 2.23 enumeration limit**: no AllDevicesSpecifier enum; string marshaler returns first multi-string entry only (2 entries vs 13/14 real). Recorded, not patched.
- **F5 SoundFlow maintainer hiatus** (verbatim README line) in the matrix.
- Windows device environment: 13 real endpoints (BEACN stack); NAudio wave-caps names truncated at 31 chars ("Mic Relay (Do Not Use) (BEACN S") — the WPF prefix-matching pain, parity evidence.
- Windows evidence (final corrected run, `evidence/run-windows.jsonl`): 36/36 observations green — values in `client/docs/audio-backend-spike.md` §3.

## Step 3 — WSLg/PulseAudio gate + packaging evidence

- Native-dir copy `~/ccp-sp017` (rsync, never /mnt/e — SP-005 pattern). Build `-f net10.0` 0W/0E; full probe run exit 0 (`evidence/run-wslg.jsonl`, 26 observations).
- **REAL WSLg evidence (claimed):** SoundFlow enumerates "RDP Sink" (backend evidence — no pactl on the image); device init/select/fallback OK; voice completion 2506.3ms in-window (native event); whisper completion 1505.8ms; teardown Δ0/Δ0; OpenAL same shape (completion 2529.5ms via named poll; fallback via alcOpenDevice null → default). NAudio: honest not-supported, no native calls.
- **NAMED LIMIT (not claimed):** all latency/overlap-timing numbers on WSLg (values exist in the log as session facts; jitter domain per packet framing (a)).
- **Contract pollution guard on WSL2:** `dotnet build CcpClient.sln` 0W/0E; `CcpClient.Tests` 213/213; `CcpClient.HeadlessTests` 22/22.
- **Packaging (SP-010 strategy, `PublishSingleFile=true`, SDK-default native layout):**
  - linux-x64 (`~/ccp-sp017/pub-linux`): apphost + `libminiaudio.so` + `libopenal.so` sidecars. `ldd libminiaudio.so` = linux-vdso/libm/libc only (libpulse DLOPENED at runtime — proven by RDP Sink enumeration succeeding from the published artifact); `ldd libopenal.so` = libstdc++/libm/libgcc_s/libc. Published binary ran devices probes for both backends, exit 0.
  - win-x64 (`scratch/pub-win`): apphost + `miniaudio.dll` + `soft_oal.dll` sidecars; published binary ran both backends, exit 0. NAudio: no natives (inbox winmm).
  - Session fact: the SP-010 natives-beside-exe layout absorbs both candidates' natives with zero packaging work.

## Step 4 — evidence doc, selection record, consults

- Deliverable `client/docs/audio-backend-spike.md` written: named observations A1-A10 per backend, findings F1-F5, WPF contract table, explicit channel-ownership SELECTION (SoundFlow primary; voice/whisper/SFX owners with generation-token discipline and drop-on-overflow SFX cap; ONE generic player rejected) recorded PENDING-OWNER; 8 named limits.
- Board row updated → WIP (never DONE).
- **Pre-completion solo consult (Fable 5):** **VERDICT: APPROVE with five corrections** — "the evidence set is sound and unusually well-instrumented (the raw-vs-filtered end-signal design and the preserved F4 artifact run are exactly right), but four claims need sharpening and one real gap needs either a cheap probe or a named limit before .DONE." All five applied:
  1. WSLg completion "in-window" was quietly a timing claim → reworded A1 + note: event+full-duration position = completion proven; window fit = session fact, not a timing guarantee.
  2. OpenAL "zero end events after stop" is by-construction (stop deletes the source), NOT a backend observation like SoundFlow's zero — asymmetry made explicit in A2 and the board row.
  3. SoundFlow fallback TOCTOU named: fabricated-id PROVEN; genuinely-stale-device (removed between enumeration and init) untested → named limit 9.
  4. Volume mechanism-only row: honest as stated, no change.
  5. **Material gap closed empirically (consult's preferred path): combined-coexistence probe ADDED** (voice+whisper+3 SFX concurrent — the arbitration row's exact interaction). Windows: all three backends green (3/3 SFX, busy transitions correct, whisper+voice ends in-window, exactly 1 uncontaminated voice raw end). WSLg: soundflow+openal green (window fits = session facts). Added as deliverable row A11.
  - F1 discipline sharpened per Q3: **re-enumerate immediately before init, match by NAME, pass the FRESH DeviceInfo struct, persist NAME never Id** (implemented in `SoundFlowHarness.TryInit` — `UpdateAudioDevicesInfo()` at init time); residual TOCTOU stays open by design until the SP-006-deferred re-probe row. NO re-probe machinery added to the spike (consult: don't expand scope).
  - Mid-playback device loss/switch untested → named limit 10 (`SwitchDevice` exists on the SoundFlow API surface, untested — feeds the quips row's "stale-device fallback" acceptance).
