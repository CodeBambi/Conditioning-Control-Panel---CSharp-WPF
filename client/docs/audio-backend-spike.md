# Audio backend spike — evidence + channel-ownership selection

**Date:** 2026-07-21 · **Task:** SP-017 (task-board row "Spike cross-platform audio channel backend") · **Status:** spike outcome, **selection PENDING-OWNER ratification** (like every spike row; the board row stays `WIP`)

Quarantined spike host: `client/spikes/CcpSpike.Audio/` (console, NOT in `client/CcpClient.sln`; inherits only `client/Directory.Build.props`). Raw observation logs: `spine-tasks/SP-017-audio-backend-spike/evidence/run-windows.jsonl` (36 observations) and `run-wslg.jsonl` (26). Worker log, consult verdicts, and research citations: `spine-tasks/SP-017-audio-backend-spike/record.md`.

**Honesty framings (packet, binding):** (a) WSLg/PulseAudio = REAL Linux evidence for enumerate/select/fallback + event-verified completion + teardown + packaging; **latency/overlap-timing numbers are Windows-headed ONLY — Linux timing is a named limit** (WSLg jitter); (b) completion/interruption/pause/busy claims come only from backend-emitted events/positions cross-checked against a shared monotonic stopwatch — never from call returns or sleeps; (c) packages admitted via in-packet solo consult with exact versions/licenses/natives from live feeds; (d) NAudio 2.2.1 = Windows-reference baseline, never a cross-platform candidate; (e) no Wayland claim (§5.1 untouched).

## 1. Admitted backends (package admission gate, solo Fable 5 consult 2026-07-21)

| Backend | Version | Published | License | Native deps (Windows / Ubuntu 26.04) | Maintenance | Packaging implication (SP-010 natives-beside-exe) |
|---|---|---|---|---|---|---|
| **SoundFlow** (PRIMARY candidate) | 1.4.1 | 2026-05-11 | MIT | BUNDLED `miniaudio.dll` / `libminiaudio.so` per-RID (miniaudio = public-domain/MIT-0); dlopens libpulse/libasound at runtime — **zero apt packages needed** (WSLg libpulse0 preinstalled) | **"The maintainer is on hiatus from Jan 2026 to Feb 2027. Support and updates will be limited."** (README, verbatim — recorded matrix risk per consult; repo pushed 2026-05-11, 495 stars). net8.0-only TFM (runs fine on net10.0) | Cleanest possible: MIT end to end, natives flow into the existing publish layout automatically |
| **Silk.NET.OpenAL** + **Silk.NET.OpenAL.Soft.Native** (SECONDARY) | 2.23.0 / 1.23.1 | 2026-01-23 / Apr 2024 | Bindings MIT / native **LGPL-2.0-or-later** (`requireLicenseAcceptance=true`) | BUNDLED `soft_oal.dll` / `libopenal.so` per-RID | Very active (dotnet/Silk.NET pushed 2026-07-18, 5.1k stars) | LGPL sidecar obligations: license notice + source-availability offer for OpenAL Soft; sidecar layout naturally satisfies replace/relink. A real SP-010 differentiator vs miniaudio's public-domain/MIT-0 |
| **NAudio** + **NAudio.Wasapi** (Windows REFERENCE only) | 2.2.1 (WPF incumbent pins) | 2023-09-04 | MIT | None — Windows inbox (winmm) | Active (naudio/NAudio pushed 2026-07-20) | Windows-only by design; windows-TFM assemblies only (verified by reflection — `WaveOutEvent` absent from the non-windows TFM) |

**Rejected on evidence:** PortAudioSharp 0.3.0 (last publish 2020-06-24, stale nuspec → dead); ManagedBass 4.0.2 (BASS native proprietary, NOASSERTION — commercial redistribution licensing incompatible with this Patreon-funded product); CSCore 1.2.1.2 (2017, dead, Windows-only); LibVLCSharp 3.10.0 (backend-shape disqualifier: per-instance media player, no in-process sample mixer, no low-latency one-shot path, state-poll completion — the wrong shape for bounded SFX polyphony).

**Decoder row (doc-level; spike runtime-proves WAV only):** SoundFlow decodes wav/mp3/flac via miniaudio built-ins — covers the WPF MP3 bark/voice reality (`Mp3FileReader`/`MediaFoundationReader`). OpenAL is buffers-only — a product on that path needs a separate managed decoder (extra dependency, extra admission).

## 2. Environment facts

- **Windows:** Windows 11 x64, 13 real render endpoints (BEACN Studio routing stack), dotnet SDK 10.0.302. SoundFlow device period configured 10 ms (recorded — latency quantization: measurement resolution = 2 ms poll + device period; no sub-period precision claimed).
- **WSLg (Ubuntu 26.04, SDK 10.0.110):** `PULSE_SERVER=unix:/mnt/wslg/PulseServer` present; server lives in the WSLg system distro; `libpulse0` 17.0 installed; **no `pactl`/pulseaudio-utils** — all enumeration evidence comes from the backends themselves. Session offers X11 via XWayland; **Wayland never claimed (§5.1)**.

## 3. Named observations (identical probe suite per backend)

Completion window declared pre-run: [duration−100ms, duration+500ms]; pause freeze drift ≤ 20 ms; resume successor = duration+pauseWall ±600 ms; SFX = 8 triggers at 30 ms spacing of a sample-exact 300 ms/48 kHz tone, start latency = trigger→first backend position-advance at 2 ms poll (interleaved polling — see F4). All WAVs sample-exact (frames/sampleRate).

| # | Acceptance item | SoundFlow 1.4.1 | Silk.NET.OpenAL 2.23.0 | NAudio 2.2.1 (Windows ref) |
|---|---|---|---|---|
| A1 | Voice completion (end ≈ duration) | **WIN 2505.3 ms / WSLg 2506.3 ms — in-window** (native `PlaybackEnded` event; position 2.5 s at end). WSLg claim shape: **event fired + position at full duration = completion proven; the window fit is an observed session fact, not a timing guarantee** (named limit 1) | **WIN 2505.3 / WSLg 2529.5 — in-window** (state-poll@5ms → STOPPED; mechanism named; WSLg window fit = session fact, same shape) | **WIN 2552.5 — in-window** (`PlaybackStopped` event). WSLg: honest not-supported, no native calls attempted |
| A2 | Interruption (stop mid-stream; interrupt≠completion) | **Stop at 599 ms, position 0.61 s < duration; ZERO end events after stop — a BACKEND BEHAVIOR FACT (SoundFlow does not fire `PlaybackEnded` on explicit Stop)** → distinguishable by signal presence | Stop 599 ms, position 0.60 s; zero end events — **BY CONSTRUCTION, not a backend observation** (VoiceStop deletes the source; no late signal can exist). The two zeros are NOT symmetric (pre-completion consult correction) | Stop at 614 ms, position 0.75 s; **`PlaybackStopped` FIRED for the explicit stop (raw +1)** — NAudio does not discriminate at event level; player-identity filtering rejects it → generation/identity token required (F2) |
| A3 | Pause/resume (freeze + successor) | **Freeze drift 0.0 ms; end 2952.1 vs expected 2964.4 — in-window** | Freeze drift 0.0; end 2929.5 vs expected 2953 — in-window | Freeze drift 0.0; end 2995.6 vs expected 2964.5 — in-window |
| A4 | Bounded low-latency overlapping SFX (**Windows numbers only; WSLg values recorded in log but NOT claimed — named limit**) | **8/8 started, 8/8 completed, max 8 simultaneous on one MasterMixer; latency min 13.7 / median 15.5 / max 15.9 ms** (10 ms period + poll floor) | **8/8, 8/8, max 8 simultaneous on one context; min 15.0 / median 15.9 / max 28.9 ms** | **8/8, 8/8, max 8 simultaneous (OS-mixed WaveOutEvents); min 0.5 / median 12.1 / max 12.8 ms** |
| A5 | Whisper busy/completion | **Busy true at play; `PlaybackEnded` at 1500.5 ms in-window; busy cleared by the REAL event** (not a duration estimate) | Busy true; poll-observed end 1492.7 ms in-window; busy cleared | Busy true; event 1501.2 ms in-window; busy cleared |
| A6 | Device enumerate/select/fallback | **WIN: 13 real endpoints enumerated; non-default select OK. WSLg: "RDP Sink" enumerated (backend evidence, no pactl); single-device session fact → select skipped honestly.** Fallback: invalid id refused at validation layer, default re-init OK (F1) | WIN: default + first-entry only (F3); select-by-name OK; WSLg: "RDP Sink" via first-entry. Fallback: `alcOpenDevice("bogus")` → null → default path OK | WIN: 14 (mapper + 13; names truncated at 31 chars — the WPF prefix-matching pain, parity evidence). Fallback: `MmException BadDeviceId` → default path OK |
| A7 | Volume (per-channel gain) | Set 0.25/1.0 + backend readback verified (player.Volume). Audible level = **mechanism-only named** (no backend meter exposed at this layer) | Same (AL_GAIN readback). Mechanism-only named | Same (AudioFileReader.Volume readback). Mechanism-only named |
| A8 | Teardown (M=10 full init→play→dispose cycles) | **Δ0 handles / Δ0 threads** (Windows), Δ0/Δ0 (WSLg) | Δ1 handle / Δ0 threads (Windows), Δ2/Δ0 (WSLg) — bounded residue, recorded | Δ0/Δ0 (Windows) |
| A9 | Packaging (SP-010 self-contained single-file strategy) | **win-x64: `miniaudio.dll` beside exe, loads. linux-x64: `libminiaudio.so` beside binary, loads (RDP Sink enumerated from the published artifact). `ldd`: libc/libm only — libpulse dlopened at runtime (proven by enumeration succeeding)** | **win-x64: `soft_oal.dll` beside exe, loads. linux-x64: `libopenal.so` beside binary, loads. `ldd`: libstdc++/libgcc/libc/libm** | No natives (inbox winmm) — published artifact runs on Windows; Linux honest not-supported |
| A10 | Contract pollution guard | `dotnet build client/CcpClient.sln` 0W/0E + `CcpClient.Tests` + `CcpClient.HeadlessTests` green **on both platforms** (WSL2: 213/213 + 22/22; Windows run in Step-5 verification) — spike never in the solution, zero `client/src`/`client/tests` changes | same | same |

## 4. Findings (failures recorded, never patched over)

- **F1 — SoundFlow 1.4.1: unvalidated `DeviceInfo.Id` is a process-fatal native crash.** A fabricated invalid Id reaches `ma_device_init` as a wild native pointer → uncatchable access violation `0xC0000005` (observed 2×, 2026-07-21, stack in record.md). **Discipline (pre-completion-consult-sharpened): re-enumerate immediately before init, match by NAME, and pass the FRESH snapshot's `DeviceInfo` struct — never a stored one; persist the device NAME, never the Id** (Ids are process-lifetime pointers; WPF FriendlyName prefix-matching parity, `AudioService.cs:219-296`). The fabricated-id case is PROVEN (refuse → default re-init works); the genuinely-stale-device case (removed between enumeration and init) is untestable here — named limit 9; the residual TOCTOU window stays open by design until the SP-006-deferred re-probe row owns hot-plug semantics.
- **F2 — NAudio fires `PlaybackStopped` on explicit `Stop()`** (raw +1 after mid-stream stop). Interrupt ≠ completion at event level on the incumbent; the identity/generation-token filter (spike harness pattern) is REQUIRED for the voice channel on any backend that behaves this way. SoundFlow and OpenAL did NOT fire on stop (zero raw events) — a per-backend behavioral difference the channel owner must not assume away.
- **F3 — Silk.NET.OpenAL 2.23.0 cannot fully enumerate devices.** The binding exposes no `ALC_ALL_DEVICES_SPECIFIER` enum and its string marshaler returns only the FIRST entry of the multi-string list (verified by reflection + runtime: 2 entries where SoundFlow/NAudio see 13–14). Device selection by name works; full enumeration would require bypassing the binding (own P/Invoke) or a Silk fix. A6 remains proven on the backend's own terms (default + first-entry + named select + fallback).
- **F4 — SFX latency measurement artifact (fixed, method recorded).** Poll-after-all-triggers read 0 offsets for clips that had already finished (120 ms clip, 30 ms spacing) and produced a spurious ~30 ms/index latency ladder on all three backends. Interleaved polling + a 300 ms clip collapsed the ladder to tight distributions (A4). The A4 numbers come only from the corrected method; the artifact run is preserved in record.md as the evidence this measurement class is gameable.
- **F5 — SoundFlow maintainer hiatus (Jan 2026–Feb 2027)** — verbatim in §1. Not a spike failure; a selection risk the owner must weigh for a long-lived P0 subsystem on a solo-maintainer library.

## 5. WPF behavioral contract the selection serves (archaeology, File.cs:line in record.md)

| Channel | WPF semantics (VERIFIED) |
|---|---|
| Companion voice | Exclusive single channel, stop-replace newest-wins (`AvatarTubeWindow.Speech.cs:1585-1608`), precise completion needed (mic gating, speaking FX — today 40 ms polling), pause/resume for world-freeze (`:1651-1669`) |
| Whisper/subliminal | Exclusive, stop-replace (`SubliminalService.cs:514-548`), completion event drives unduck; duration-estimate busy window gates barks (`AudioService.cs:734-764`, `BarkService.cs:1342-1344`) — the estimate exists because a real completion signal wasn't wired; the spike proves a real one is available |
| SFX (pops/chaos/giggles) | N overlapping one-shots, OS-mixed, fire-and-forget; bounded: bubble pool max 4 (`BubbleService.cs:1881-1884`), ChaosSfx cap 6 with **drop-on-overflow** (`ChaosSfx.cs:91-107` — cap added after audio-storm crash dumps) |
| Ducking | Reference-counted system-wide session ducking with generation tokens (`AudioService.cs:766-1036`) — WASAPI-specific; **product-layer/platform concern, NOT a backend-selection item** (Linux ducking = PipeWire/Pulse policy, separate decision) |
| Bark/quip arbitration | Text queues, audio stop-replaces; gates DROP (whisper-active, narrator, min-gap, cooldown, chance) (`BarkService.cs:1336-1392,1616-1624`) |
| Output device | User-selectable, name-matched, missing → default, none → audio disabled for session (`AudioService.cs:86-361`) |

First-attempt REJECT lessons honored (cited): no generic replace-on-play player (`AvaloniaAudioPlayer.cs:44-76`), no detached Task.Run-per-cue with sleep polling (`AvaloniaSfxPlayer.cs:41-141`), no bubble-timer completion, no best-effort device routing, no unbounded player-per-cue.

## 6. SELECTION (spike outcome — PENDING-OWNER ratification)

**Backend: SoundFlow 1.4.1 (MiniAudio) as the single cross-platform audio backend.**

Rationale (every row evidence-backed above): only candidate with native completion EVENTS on both platforms (A1/A5) — OpenAL requires a poll thread by design (mechanism named); cleanest packaging story for SP-010 (MIT end-to-end, bundled natives, zero apt deps — A9); full real device enumeration (A6 vs F3); MP3 decode built-in (WPF bark/voice reality); bounded teardown Δ0/Δ0 both platforms (A8); SFX latency distribution tight and bounded on Windows (A4). Risks carried open to the owner: **F1** (crash-on-wild-Id — mitigated by validation-layer discipline, must be a contract rule), **F5** (maintainer hiatus).

**Channel ownership (explicit — the row's "ONE generic player REJECTED" requirement):**

| Channel | Owner shape (one SoundFlow playback device; per-channel SoundPlayers on MasterMixer) | Concurrency | Interruption | Completion |
|---|---|---|---|---|
| **Voice** | Exclusive channel owner: one active SoundPlayer, stop-replace with **generation token** (F2 discipline) | 1 | newest-wins stop-replace | `PlaybackEnded` filtered by player identity; pause/resume via player (A3) |
| **Whisper** | Exclusive channel owner: one active SoundPlayer, stop-replace | 1 | stop-replace | `PlaybackEnded` drives busy-flag clear — **replaces the WPF duration estimate with a real signal** (A5); the estimate remains as belt-and-braces tail if the owner wants it (product decision) |
| **SFX** | Bounded pool owner: N concurrent SoundPlayers on MasterMixer, **cap with drop-on-overflow** (WPF parity 4–6; exact bound pending-owner) | N ≤ cap | drop, never queue | none needed (fire-and-forget); pool reclaims on `PlaybackEnded` |
| **Volume** | per-player `Volume` with the WPF curve `pow(channel×master, 1.5)` applied at the product layer | — | — | — |
| **Device routing** | re-enumerate immediately before init → match by NAME → pass the FRESH `DeviceInfo`; persist NAME never Id (F1 discipline); on stale: refuse → default; ducking/video excluded (separate engines/decisions) | — | — | — |

Rejected alternatives: one generic player for all channels (the row's explicit requirement; first-attempt REJECT lesson); OpenAL as primary (poll-only completion + F3 enumeration + LGPL sidecar + no decoder — it remains the recorded fallback if SoundFlow's F5 risk materializes); NAudio as anything but the Windows reference (Windows-only); LibVLCSharp for audio channels (backend shape).

## 7. Named limits / explicit non-claims

1. **Linux latency/overlap-timing numbers NOT claimed** — WSLg PulseAudio jitter; values exist in the log as session facts only. Real-hardware Ubuntu re-measurement is the owner-gated follow-up (same class as the SP-011 WPE question).
2. **WAV-only runtime decode proof** — MP3 decode is a doc-level matrix fact (miniaudio built-in), not runtime evidence.
3. **Volume audible effect = mechanism-only** (set + backend readback verified; no backend meter at this layer).
4. **No Wayland claim** (§5.1 untouched); WSLg = X11/PulseAudio session facts.
5. **WSLg single-device session** ("RDP Sink") — non-default select proven on Windows only; WSLg recorded as skipped-single-device fact.
6. **Invalid-device fallback proven at the validation layer for SoundFlow** (wild-Id native crash = F1), at the native layer for OpenAL/NAudio. Real unplug/hot-plug not simulated (not automatable here; re-probe semantics deferred per SP-006 §3 rule 5).
7. **Selection, channel bounds (SFX cap value), and ducking-Linux policy are pending-owner** — this doc records a spike outcome, not a product decision.
8. Ducking itself (reference-counted, cross-app session volume) is NOT a backend-selection item — Windows WASAPI mechanism vs Linux PipeWire/Pulse policy is a separate platform decision.
9. **Genuinely-stale-device fallback untested** (TOCTOU: device removed between enumeration and init — plausibly the same F1 crash; untestable here). The sharpened F1 discipline (re-enumerate immediately before init, fresh `DeviceInfo` only) narrows but does not close this window; the SP-006-deferred re-probe row owns hot-plug semantics.
10. **Mid-playback device loss/switch untested** — the quips/sound-arbitration row's "stale-device fallback" acceptance names this; the spike proved INIT-TIME fallback only. `SwitchDevice` exists on the SoundFlow API surface (untested) and is recorded as the likely mechanism for that row.
