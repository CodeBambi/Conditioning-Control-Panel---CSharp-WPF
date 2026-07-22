# SP-029 — Quips/sound arbitration q1: arbitration core — worker record

**Started:** 2026-07-22 · **Lane:** spine-20260722T101444/lane-1

## Step 1 — WPF arbitration archaeology (READ-ONLY, File.cs:line)

Extracted via wpf-archaeologist subagent (37 tool uses; all citations verified against the WPF tree).

### Bark queue / priority / freshness (WPF VERIFIED)

- **BarkService owns no audio queue.** `Speak()` routes: ordinary → `avatar.Giggle(...)` (queued), priority (`rule.Class != Normal || rule.Priority >= 100`, threshold `PriorityBarkThreshold = 100` at `Services/Companion/BarkService.cs:81`) → `avatar.GigglePriority(...)` (preempts) (`BarkService.cs:1616-1626`).
- Queue = FIFO `Queue<(text, source, emotionLineId, mood)>` (`AvatarTubeWindow.Speech.cs:34`). **No max size, no drop-on-enqueue, no timestamps → NO ms-age freshness on queued items** (VERIFIED `:34-46`, enqueue `:273,:287`).
- Enqueue drops: uninterruptible clip (`:238`), waiting-for-AI (`:241-245`), AI bubble visible (`:248-253`); enqueued only while speaking or in the post-speech delay window (`:271-288`); else shown immediately.
- Dequeue pacing (`ProcessNextSpeech`, `:178-212`): delay = `MinSpeechDelaySeconds 2.0` + `AiSpeechBonusSeconds 5.0` if last was AI + `0.02 s/char` over 100 chars (`:112-119,148-164`).
- **Priority preemption = clear-all-queued + show-now** (`GigglePriority` `:319-360`, queue clear `:338`; same clear in moderation/listening bubbles `:373-375,:710-713`). Queued ordinary barks are discarded, not re-prioritized.
- **Voice stop-replace**: single `_spokenPlayer` under `_spokenLock` (`:124-129`); every show calls `StopSpokenAudio()` first (`:473,:1594`); play-loop identity check `ReferenceEquals` before clearing state (`:1623-1632`) — replaced player cannot clear the newer line's state (the WPF generation-filter equivalent). Init failure → log + return, no retry (`:1603-1607`); play-loop exceptions swallowed (`:1621`). Pause/Resume for world-freeze (`:1647-1663`).
- **Anti-stale drop (the only WPF "freshness" mechanism)**: ordinary barks dropped while `IsSpeaking` — comment: a queued bark would go stale behind an on-screen bubble; preempting barks exempt (`BarkService.cs:1359-1363`). Gate windows that exist as ms values (q2 policy inputs, not queue expiry): `SafetyHoldMs 6000` (`:87`), `BarkChatSuppressionMs ?? 10000` (`:1351-1354`), `GlobalMinGapMs 60000` (`:68`), `SelfEchoMuteMs 8000` (`:84`).
- Mute egg: EasterEgg + MasterVolume==0 → silent text-only bubble (`:1594-1600`) — q2.

### Ducking (WPF VERIFIED, `Services/AudioService.cs`)

- `_duckCount` refcount + `_isDucked` + `_duckAmount` default 0.8 (`:28-31`). `Duck(strength=80)` (`:766`): no-op if MasterVolume==0/no enumerator; overlapping holders just bump the count (`:774-776`); first duck stores per-PID original volumes and applies **multiplicative** reduction `max(0, current*(1-duckAmount))` across all render sessions, own PID + WebView2 PIDs excluded (`:782-829,1052-1084`).
- Watchdog 5 min force-unduck (`DuckWatchdogMs 300_000`, `:39,:845-853`); rescan 2500 ms for mid-duck sessions (`:42-43,856-860`); crash-recovery JSON of original volumes (`:618-649,:862,:988`).
- `Unduck(generation=-1)` (`:888`): stale-generation ignored (`:892-898`); restore only when count hits 0 (`:900-906`); restore by PID, fallback by process name (`:927-962`); unrestorable → `_pendingRestores` retried 5 s for 3 min (`:50-53,964-984`); unduck failure preserves state, `_duckCount=1` to stay recoverable, never a volume ratchet (`:1003-1016`).
- **`ForceUnduck()` (`:1024-1033`)**: generation++ invalidates pending stale unducks, forces count=1, unducks — "panic key / app exit" (doc `:1021-1023`; callers `App.xaml.cs:835`, `SessionEngine.cs:301`, `VideoService.cs:1138`, etc.).
- Holders: FlashService.cs:954-962, VideoService.cs:1454/:3210, KeywordTriggerService.cs:1127-1158, SubliminalService.cs:217/:284/:375/:412 acquire, :538/:551 release, QuizWindow 1288-1289/:1392, ChaosNarrator.cs:128-140, RemoteControlService.cs:1121.

### Whisper (WPF VERIFIED)

- Stop-replace single channel (`SubliminalService.cs:516`, stop+dispose `:555-568`); caller ducks before play (`:215-218`); volume `pow((sub/100)*(master/100),1.5)` (`:523-527`).
- Completion → `Task.Delay(500)` → `Unduck(duckGen)` generation-captured (`:532-541`); play failure → immediate unduck (`:547-551`).
- **Busy window = duration ESTIMATE** (`MarkWhisperAudio`, `AudioService.cs:750-758`: now+duration+0.25 s tail, extend-only CAS; NaN/<=0 ignored; `IsWhisperAudioPlaying` `:736-745`) — exists because no real completion signal was wired; the spike (A5) proves a real one is available. Bark whisper gate reads it (`BarkService.cs:1341-1343`).

### Device selection / fallback (WPF VERIFIED, `AudioService.cs`)

- Setting `AudioOutputDeviceId` + persisted `AudioOutputDeviceName` fallback (`:226,:253-254`). MMDevice ID → FriendlyName → bracketed driver name → **31-char-tolerant WaveOut prefix match, either direction + contains** (`:259-284`). No match → warn + default (`:292-293`).
- `CreateWaveOut` (`:86-132`): preferred/WAVE_MAPPER → on BadDeviceId scan all device numbers, first that opens latches `_workingWaveOutDeviceNumber` (`:107-127`) → none opens → `_waveOutPermanentlyUnavailable`, **audio disabled for the session** (`:129-131`).
- Chain: user ID → friendly name → driver name → prefix match → default → first-working → disabled-for-session.

### SFX pools (WPF VERIFIED)

- **ChaosSfx**: `MAX_VOICES = 6`, Interlocked counter, increment-check-decrement **drop** on overflow — "a one-shot SFX played late is worse than silence" (`ChaosSfx.cs:91-107`); per-voice Task.Run + WaveOutEvent, 40 ms poll loop, finally dispose+decrement (`:109-128`); volume linear `clamp(master*scale)` (`:82-89`); cap added after audio-storm crash dumps (`:89-92`). High-frequency cues routed to bubble pool (`:41-43`).
- **BubbleService**: `MAX_POOLED_DEVICES = 4` caps *idle retained* devices only, **no cap on concurrent players** (`BubbleService.cs:1880-1928`) — different shape (nothing dropped).
- **Rapid click cues**: NO dedicated queue — click handler stamps `_rapidClickTimestamps` (rolling 60 s), 50+/min → collapse trigger + reset (`AvatarTubeWindow.ChatInput.cs:31,:59-67`); click SFX only **1-in-25 chance** (`:81-83`) → unpooled Task.Run pop (`Speech.cs:2271-2303`). Latency demand on the SFX pool is low by design (INFERRED from the 1/25 gate). Recorded for q2; not built in q1.

### Bark rules engine surface (what q2 hands the core)

- `BarkRule` (`Bark/BarkRule.cs:42-118`): Id, Trigger, Conditions, Priority (≥100 preempt band), CooldownMs, Repeatable, Scope, Mood, Class, VariantPool/PoolRef, Chance.
- `BarkVariant` (`BarkVariant.cs:18-27`): `{ Text, Audio? }`.
- Hand-off (`BarkService.Speak` `:1578-1629`): substituted text, resolved nullable audioPath, mood, binary preempt-vs-queue. **No duration/volume/channel metadata crosses the boundary** — playback policy lives in the playback owner.

## Step 1 — Design (q1 arbitration core)

New home `client/src/CcpClient.Desktop/Audio/`, contract-named `SoundArbitration.cs`.

### Seams (testability; real = SoundFlow, tests = recording fakes)

- `IAudioBackend : IDisposable` — `TryInit(deviceName, out error)` with the F1 discipline (re-enumerate immediately before init, match by NAME, pass only a FRESH DeviceInfo, persist NAME never Id — spike F1/F3, port-lessons 2026-07-22); `EnumerateDevices()` (session facts for a future settings surface); `CreatePlayer(path, volume)` — **construction marshaled off-sync-context when `SynchronizationContext.Current` is present** (SoundFlow 1.4.1 AssetDataProvider sync-over-async deadlock, SP-025 dump-proven; seam shape mirrors `SoundFlowDtrhAudio.CreatePlayer`, `Features/Dtrh/SoundFlowDtrhAudio.cs`).
- `IAudioPlayer` — Play/Pause/Stop/Dispose, State, PositionSec, Volume, `PlaybackEnded`. SoundFlow backend fact (SP-017 A2): explicit Stop fires ZERO end events → interruption distinguishable from completion; the generation token is still required (NAudio F2 class — never assume per-backend).
- `IAudioDuckSink` — platform duck mechanism: `Apply(strength)` / `Restore()`. q1 ships the refcount machinery against this seam.

### SoundArbitration (app-wide owner)

- **Voice channel** — exclusive stop-replace + generation token (WPF stop-replace `Speech.cs:473,:1594`, identity-filter `:1623-1632`; SP-017 §6). `PlayVoice(path, gain)` → typed outcome; `VoiceCompleted` event fires only for the CURRENT generation ending naturally (interruption never reported as completion).
- **Voice queue** — ordinary FIFO + priority preemption (WPF: priority = clear-all + play-now, `Speech.cs:319-360`); pacing delay between items (WPF 2.0 s + 5.0 s-AI-bonus + 0.02 s/char>100, `Speech.cs:112-119,148-164` — the char-bonus needs the text, so pacing hint is caller-supplied). **Freshness: caller-supplied per-item `FreshnessWindow`** — WPF has NO ms-age queue expiry (VERIFIED); its freshness is gate-level anti-stale (`BarkService.cs:1359-1363`). Core provides the mechanism (expired-at-dequeue → typed `Dropped(Stale)`, logged); policy values are q2's from the WPF gate citations. No policy invented.
- **Whisper channel** — exclusive stop-replace; `WhisperBusy` set at play, cleared ONLY by the real `PlaybackEnded` (or stop/failure) — replaces the WPF duration estimate (`AudioService.cs:750-758`) with the real signal the spike proved (A5); `WhisperBusyChanged` event for the q2 whisper gate.
- **SFX pool** — bounded pool max **8** (SP-025 packet decree; WPF ChaosSfx cap-6 cited `ChaosSfx.cs:91-107`), **drop-on-overflow typed + logged** (`SfxDropped`), reclaim on real `PlaybackEnded`, fire-and-forget.
- **Ducking** — reference-counted `AcquireDuck(strength)`/`Dispose` with generation tokens (WPF `:766-906`), 5-min watchdog force-unduck (WPF `DuckWatchdogMs 300_000`), **`ForceUnduck()` panic release-all** (WPF `:1024-1033`), unduck-failure keeps state recoverable, never a ratchet (WPF `:1003-1016`). The cross-app session-volume MECHANISM (WASAPI on Windows / PipeWire-Pulse policy on Linux) sits behind `IAudioDuckSink`: **q1 registers a typed `Unavailable` sink** (real WASAPI session enumeration = its own port; spike named limit 8 says Linux policy is a separate decision; NAudio is admitted Windows-reference-only). Recorded as named limit.
- **Device layer** — `SetPreferredDevice(name)` persists NAME never Id; init/re-init re-enumerates + matches by NAME (WPF chain `:219-296`); stale → typed fallback to default (WPF `:292-293`); zero endpoints → typed audio-disabled-for-session (WPF `:129-131`). Mid-playback `SwitchDevice` exists on SoundFlow (spike named limit 10, untested) — q1 owns the re-probe at init; hot-plug UX = q2.
- **PanicReset()** — stop+dispose all channels + clear queue + ForceUnduck + generation bump; typed + logged. WPF had NO single StopAll entry (archaeology: only ForceUnduck + StopSpokenAudio separately) — the core provides one as an intentional improvement, cited.
- **Off-sync-context** — ALL player construction through the backend seam's marshal (SP-025 regression test: a thread carrying a SynchronizationContext must not deadlock).

### q1/q2 boundary (recorded for the orchestrator)

- **q1 (this slice):** channel ownership, queue+freshness mechanism, ducking refcount machinery + sink seam, device re-probe layer, panic cleanup, typed outcomes. Exposes: `PlayVoice/QueueVoice/PlayWhisper/PlaySfx`, `VoiceCompleted/WhisperBusyChanged`, `AcquireDuck/ForceUnduck/PanicReset`, `SetPreferredDevice/EnumerateDevices`, outcomes.
- **q2 (not this task):** bark rules engine port, text/audio/emotion payload integrity, gate policy VALUES (whisper gate reads `WhisperBusy`; min-gap/cooldown/chance/anti-stale from the WPF citations above), mute text-only, disabled-phrase persistence, rapid-cue UX, stale-device UX, volume curve application (`pow(channel×master,1.5)` — spike §6 product-layer), WASAPI duck sink decision.
- **DTRH boundary:** `DtrhNativeEffects` + `SoundFlowDtrhAudio` stay the DTRH-local owners (separate engine/device instance; miniaudio devices coexist). A future row may route DTRH through arbitration. NOT refactored in q1 (packet decree).

## Engine-review presence log (T-2)

- Step 1 plan review: `spine_review_step(step=1, type=plan)` → **SKIPPED by engine** ("Nested reviewer spawn blocked inside pi worker session… the batch engine runs reviews after worker success (SP-195)"; `spawnFailed=false`, artifact `.reviews/1-20260722T103050.md`). Engine-run code+final reviews expected after `.DONE`.

## Consult log

### Pre-approach (Step 1) — APPROVE with bindings

Requested route: solo Fable 5. Actual answering model: NOT surfaced in the consult tool output (same as SP-028 precedent).

Verdict (adapted):
1. **Ducking scope — deferral APPROVED.** The cross-app WASAPI session sink stays out of q1: the spike named limit 8 declares ducking a product-layer/platform concern with Linux policy a separate pending-owner decision; building Windows-only cross-app ducking now would strand the core with a platform asymmetry the owner hasn't decided. q1 implements the full refcount machinery (acquire/release symmetry, generation tokens, 5-min watchdog, ForceUnduck panic release-all) against the `IAudioDuckSink` seam with a typed `Unavailable` sink. **BINDING: pre-declare the Step-3 ducking evidence shape NOW:** duck-state transitions + sink-invocation symmetry (Apply/Restore call counts, strengths, ordering) under real playback + panic release-all — never cross-app session-volume claims. Recorded below.
2. **Freshness mechanism-without-policy APPROVED.** WPF has no queue-age window (VERIFIED); inventing a default would violate the never-invented binding. Caller-supplied `FreshnessWindow`, no-expiry default = WPF parity; policy values are q2's with WPF gate citations.
3. **PanicReset APPROVED as outcome-parity composition** (ForceUnduck callers + per-channel stops), NOT a divergence — frame as such. **BINDING: PanicReset must be idempotent and safe under callback races** (PlaybackEnded landing during teardown); the generation bump is the stale-event filter.

Additional advisor flags (accepted):
- **Injectable clock/timer abstraction REQUIRED** for unit tests (duck watchdog 5-min, queue pacing delays) — added to design: `ISoundClock` (Now + Schedule) seam; real = timer-based, tests = manual advance.
- SFX cap 8 = SP-025 decree, no re-litigation.
- NAudio WASAPI sink for a future cross-app-ducking row is WITHIN the SP-017-admitted set (Windows-reference fits a Windows-only mechanism) — recorded for that row, not q1.

**Pre-declared Step-3 ducking evidence shape (advisor binding):** (a) refcount transitions 0→1→2→1→0 under real voice playback; (b) sink Apply/Restore invocation symmetry (counts, strength, order) on a recording probe sink; (c) watchdog force-unduck via injected clock; (d) panic release-all (ForceUnduck + PanicReset) releasing N overlapping holders with exactly one Restore; (e) no cross-app session-volume claim (typed Unavailable sink = named limit).

## Engine-review presence log (T-2)
