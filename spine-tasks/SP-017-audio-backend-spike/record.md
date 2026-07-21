# SP-017 record — spike cross-platform audio channel backend

Worker session log. Design decisions, consult verdicts (provenance), research citations, measurement methods, surprises, engine-review presence (T-2).

## Engine-review presence log (T-2)

Packet emits structured `## Review Level: 2` heading. Per-call record:

| Step | spine_review_step call | Result | Engine review fired? |
|------|------------------------|--------|----------------------|
| 1 | type=plan after step-1 commit | `skipped: true`, `spawnFailed: false` — "Nested reviewer spawn blocked inside pi worker session (SP-195); batch engine runs reviews after worker success" | NO in-worker (by design); post-.DONE engine review observable at land time |

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
