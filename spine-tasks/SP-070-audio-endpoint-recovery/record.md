# SP-070 — Record: the audio session-disable expires (WPF d33b5d8d / #778 / #779 parity)

## Step 1 — WPF recovery re-derived by symbol (found-vs-given)

All WPF anchors re-derived 2026-08-14 against the landed tree (SP-069, `6feb11e4`).

| Anchor | Given | Found | Verdict |
|---|---|---|---|
| Trip threshold `OutputFailuresToTrip = 5` | AudioService.Playback.cs:101 | `:101` (`private const int OutputFailuresToTrip = 5;`) | MATCH |
| Cooldown `OutputCooldown = 30s` | `:104` | `:104` (`TimeSpan.FromSeconds(30)`) | MATCH |
| Breaker region | `:373` | `#region Circuit breaker` at `:373` | MATCH |
| `NoteOutputSuccess` | `:379` | `:379-385` — resets `_outputFailStreak` to 0, clears `_outputSuppressedUntilTicks`, logs "output recovered — playback resumed" once via `_outputRecoveryLogPending` | MATCH |
| `NoteOutputFailure` | `:393` | `:393-417` — increments streak; below trip → debug line only; at/above trip → sets suppressed-until = now + cooldown; **only the healthy→suppressed transition logs the warning + calls `InvalidateOutputDeviceCache()`** | MATCH |
| `InvalidateOutputDeviceCache` | `:152`, `:414`, `:536` | Called at `:152` (cooldown expiry in `IsOutputSuppressed`), `:414` (trip in `NoteOutputFailure`), `:536-538` (endpoint watcher, device returned — also clears streak + suppression). Definition `AudioService.cs:387-395`: resets the device-number caches **and `_waveOutPermanentlyUnavailable = false`** — the actual "no longer permanent" clearing site | MATCH (definition moved: given sites are call sites) |
| `_waveOutPermanentlyUnavailable` + "NOT permanent any more (#779)" comment | AudioService.cs (port parity cite :129-131) | Flag declared `:102`; early-return `if (...) return null` at `:121`; **set at `:166` with the #779 comment at `:163-166`**. The port's inherited cite `:129-131` is STALE BY OFFSET (the early-return moved to `:121`); the semantics (refuse while unavailable; the disable is now cleared by the breaker/watcher) hold | OFFSET AGED, SEMANTICS HOLD |
| `EndpointWatcher : IMMNotificationClient` | `:553` | `:553`; `TryRegisterEndpointWatcher :497`; clears suppression + streak on device return (`:536-538`) | MATCH — deliberately NOT ported (non-item 3) |
| `IsOutputSuppressed` (cooldown gate — not a packet-given anchor, found in re-derivation) | — | `:141-157`: 0 → healthy; future → suppressed; expired → CAS-clears, resets streak, invalidates device cache, logs once ("cooldown expired — retrying on a freshly resolved device"), next play probes the device for real | FOUND (the self-expiry the port lacked) |

Port anchors (all verified by full read of `SoundArbitration.cs` on this tree):

| Anchor | Given | Found | Verdict |
|---|---|---|---|
| `Initialize` + disable sites + clearing path | `:200-249`, `:214`/`:236`, `:243-247` | Disable on zero endpoints and on failed `TryInit` both present; success path clears `_audioDisabledForSession` and remembers `_preferredDeviceName` | MATCH (offsets within a few lines, semantics exact) |
| `AudioDisabledForSession` | `:183` | present | MATCH |
| `SetPreferredDevice` stop-then-`Initialize` | `:257-260` | `StopAllChannels("device change"); return Initialize(deviceName);` | MATCH — the reuse-don't-fork precedent is live |
| `ReadyLocked` | `:595-608` | refuses: torn down / not initialised / `_audioDisabledForSession` ("audio disabled for the session") | MATCH |
| `CreatePlayer` | `:611-631` | consults `ReadyLocked` under `_gate`, constructs outside it | MATCH |
| `PanicReset` | `:541` | **verified: never reads or writes `_audioDisabledForSession`** (full-file read + grep) | MATCH (FACT 2) |
| `SoundArbitrationOptions` | `:67-77` | `MaxSfxVoices`/`DuckWatchdog`/`VoicePacingDelay` | MATCH — the two new knobs go here |
| `_clock` | `:104` | injected `ISoundClock` | MATCH |
| `ISoundClock` (`UtcNow` + one-shot `Schedule`) | AudioSeams.cs:113-135 | present exactly | MATCH (read-only) |
| `OffSyncContext` | AudioSeams.cs:143 | `:150`; `CreatePlayer` marshals through it (SoundFlowAudioBackend.cs:108) | MATCH (read-only) |
| Sole product `Initialize(null)` | DtrhHostWindow.axaml.cs:213-220 | `:220`, inside `InitBarkPipeline` (`:205-222`), once during host-window construction; grep: `SoundArbitration` referenced in product only from `BarkPipeline.cs` (field/ctor), `DtrhHostWindow.axaml.cs`, and a `DtrhProtocol.cs` comment | MATCH — permanence is live user-visible |
| Blast radius "consumed by BarkPipeline, DtrhNativeEffects and the DTRH host" | Review-level scoring | **AGED:** only `BarkPipeline` consumes the arbitration play seam. `DtrhNativeEffects`/`DtrhFxRouter` route SFX/whisper through the DTRH-local audio owner (`SoundFlowDtrhAudio`), never `SoundArbitration` | DIVERGENCE (scoring note only; the edited file is unchanged) |

## Step 1 — The three non-items of d33b5d8d in this port

1. **One-shot MTA worker thread** (replaces ~15 copies of `new WaveOutEvent()` + `Thread.Sleep`): **non-item.** That idiom does not exist in this port — grep of `client/src` finds no `WaveOut`/NAudio usage; playback is SoundFlow through `IAudioBackend`/`IAudioPlayer`, and player construction already runs off-sync-context (`AudioSeams.cs:150` `OffSyncContext`, `SoundFlowAudioBackend.cs:108`). Nothing to replace.
2. **10-concurrent one-shot cap + drop-not-queue:** **non-item — already landed.** `SoundArbitrationOptions.MaxSfxVoices = 8` with typed `Dropped(PoolOverflow)` on overflow (SP-025/SP-029, `SoundArbitration.cs` SFX pool). No second cap added.
3. **`IMMNotificationClient` endpoint watcher** (WPF `AudioService.Playback.cs:553`): **non-item here — its own board row.** Windows-only native code with no headless proof on this machine; the lazy re-probe delivers the user-visible outcome (audio returns by itself on the next play attempt after the cooldown). Not implemented, no Linux/headed equivalent faked. **Intended board filing:** new row "port the IMMNotificationClient endpoint watcher (re-arm on device return instead of waiting for the cooldown)" — Windows-only, headed/manual gate.

## Step 1 — FACT 1: the calling thread of the play seam

**A UI thread CAN be on the play-seam path.** The only product consumer of `SoundArbitration`'s
play seam is `BarkPipeline`, reached only from the DTRH host window's web-message handler:

`WebView message → DtrhHostWindow.axaml.cs` web-message handlers `Dispatcher.UIThread.Post(() => HandleWebMessageBody(...))` (`:478`, `:519`) → `HandleWebMessageBody` → `_bark.Raise(barkTrigger, barkFills)` (`:1090`) → `BarkPipeline.Raise` (`:255`) → `:562-563` `PlayVoicePriority`/`QueueVoice` → `SoundArbitration.CreatePlayer` / `ReadyLocked`.

The Avalonia dispatcher thread carries a `SynchronizationContext` (Avalonia's). A second,
non-UI path exists: `OnPacingFire` (queued voice start) runs on a thread-pool thread via
`SystemSoundClock` (`System.Threading.Timer`).

**Consequence (design-deciding):** the re-probe (`EnumerateDevices` + `TryInit` — native calls
that can block on a dead audio-service RPC, WPF d33b5d8d's own root cause) must never run
inline on the discovering thread. The discovering thread only schedules a one-shot
`_clock.Schedule(TimeSpan.Zero, probe)` under a single-flight flag; the real clock fires it on
a thread-pool thread; `ManualClock` fires it deterministically on `Advance` in tests.

## Step 1 — FACT 2: panic and teardown are separate mechanisms

- `PanicReset` (`:541-583`): bumps generations, stops/disposes channel players, clears the
  voice queue + pacing timer, clears whisper busy, force-releases ducks. **It never reads or
  writes `_audioDisabledForSession`** (full-file read + grep — confirmed, not assumed).
- `Dispose` (teardown): sets `_tornDown = true`, then `PanicReset` + `_backend.Dispose()`.
  Never touches suppression state.
- Recovery separation: the probe writes ONLY `_audioDisabledForSession` / failure streak /
  suppression window — never player, queue, generation, or duck state — so it cannot
  resurrect anything panic cleared (a post-recovery play constructs a NEW player through the
  normal seam). The probe callback checks `_tornDown` before probing and `Initialize` itself
  returns `Unavailable("arbitration torn down")` early on a torn-down instance, so no
  recovery runs after teardown. **No stop condition — separation confirmed.**

## Step 1 — Design (written before code)

State (all guarded by `_gate`):

- `_consecutiveInitFailures` (int) — consecutive failed `Initialize` calls; **success resets to 0**.
- `_suppressedUntilUtc` (DateTimeOffset?) — the cooldown window, computed from the injected `ISoundClock`.
- `_reprobeInFlight` (bool) — single-flight guard.
- `_recoveryTimer` (IDisposable?) — the scheduled one-shot.

Knobs (on `SoundArbitrationOptions`, beside `MaxSfxVoices`) — **WPF values adopted, no value divergence:**

- `RecoveryFailureThreshold = 5` (WPF `OutputFailuresToTrip`, AudioService.Playback.cs:101).
- `RecoveryCooldown = 30s` (WPF `OutputCooldown`, AudioService.Playback.cs:104).

Semantics divergence (values identical, argued from the port's own shape): WPF arms
suppression only at streak ≥ 5 because its failure unit is a per-play `waveOutOpen`
(transient-tolerant — one bad open must not mute the app). The port's ONLY failure unit is
`Initialize` itself (a port play is not a device attempt — CreatePlayer failures are typed
and never set the disable), and the port already disables on the FIRST init failure (existing
behavior; weakening it is forbidden by the packet). Therefore in the port the threshold's
observable role is the **escalation transition**: when the streak REACHES 5 (a still-dead
endpoint across ≥4 cooldown windows ≈ 2 min) one escalation line is logged (transition-only —
fires once at `streak == threshold`); the cooldown governs the re-probe window and **every
failed probe re-arms it** (WPF's streak-gated re-arm cannot apply: it exists to let
individual plays retry the device, and a port play never touches the device).

Mechanism:

1. `Initialize`'s two failure sites record a failure: `_audioDisabledForSession = true`,
   streak++, arm `_suppressedUntilUtc = _clock.UtcNow + RecoveryCooldown`. The success path
   keeps clearing everything it clears today and additionally resets the streak and the
   window; a success while suppressed logs the recovery line once (WPF `NoteOutputSuccess`
   parity, `:379-385`).
2. Play seam (`ReadyLocked`, consulted under `_gate` by `CreatePlayer`, `QueueVoice`,
   `PlaySfx`): when refusing due to suppression, if `_clock.UtcNow >= _suppressedUntilUtc`
   and `!_reprobeInFlight`, set `_reprobeInFlight = true` and schedule the one-shot
   (`_clock.Schedule(TimeSpan.Zero, RunRecoveryProbe)`). **The cooldown is checked BEFORE any
   attempt; the refusal still returns the typed `Unavailable` immediately — the discovering
   caller is never blocked by the native probe.** The discovering play is refused (recorded
   in the honesty cell): WPF's post-expiry play probes the device synchronously because a WPF
   play IS a device open; the port cannot block FACT 1's UI thread, so recovery lands on the
   NEXT play after a successful probe.
3. `RunRecoveryProbe`: under `_gate` — drop the timer handle; if `_tornDown` or no longer
   suppressed, clear `_reprobeInFlight` and return. Remember `_preferredDeviceName` (NAME,
   never an Id — F1). Outside `_gate` — call `Initialize(preferred)` (the one init path;
   short locks only, backend calls outside them, exactly as today). Finally under `_gate` —
   `_reprobeInFlight = false`. Success cleared suppression + reset the streak inside
   `Initialize`; failure re-armed the window there, so failure cannot become a busy loop
   (exactly one probe per cooldown window, enforced by single-flight + the pre-attempt
   cooldown gate).
4. Teardown: `Dispose` disposes `_recoveryTimer`; the callback re-checks `_tornDown`. Panic:
   untouched — no recovery path reads or writes anything `PanicReset` owns.
5. Logging, transitions only: the trip line (healthy→suppressed), the escalation line
   (streak reaches threshold, once), the recovery line (suppressed→ready, once), one line at
   kick ("cooldown expired — endpoint re-probe scheduled", WPF `IsOutputSuppressed:153`
   parity). **Never a line per refused play.** No new observation, persistence, or network
   call; device NAMEs only (already logged today, SP-017 A6).
6. Refusal reason strings stay honest: suppression is no longer "for the session" — the
   reasons become "audio unavailable — endpoint down (re-probe after cooldown)" /
   "...re-probe in flight", distinct from "not initialised" and "arbitration torn down".

Constraints honored: cooldown before attempt; `_gate` never held across `EnumerateDevices` /
`TryInit` / `CreatePlayer`; discovering caller never blocked; no polling timer, no background
service, no wall clock; `Initialize` reused (the `SetPreferredDevice` precedent), no second
init path; preferred NAME only.

## Step 1 — Bounded-restoration clearance

| Situation | What the recovery does | Proof |
|---|---|---|
| Teardown (`Dispose`) | **Nothing.** `_recoveryTimer` disposed; callback re-checks `_tornDown`; `Initialize` early-returns `Unavailable("arbitration torn down")` | Step 3 teardown fact |
| Panic (`PanicReset`) | **Nothing** to panic-owned state — the probe touches only suppression fields, which `PanicReset` never reads/writes (FACT 2) | Step 3 panic fact |
| Explicit stop (`StopAllChannels` / per-channel stops) | **Nothing.** Stops don't touch suppression; suppression doesn't touch players | code: disjoint field sets (grep) |
| Active device change (`SetPreferredDevice`) | **Nothing new** — it already stops channels and calls `Initialize`; a success now also resets the streak/window (a fresh init clearing suppression is correct, not a widening) | existing test `Device_SetPreferred_StopsChannels_ReInits` unchanged in meaning |
| Healthy session | **Nothing.** No schedule, no extra `EnumerateDevices`/`TryInit`, no new log lines, no state transition | Step 3 byte-for-byte negative control |

A recovery restores only what a healthy endpoint would already have permitted: the next play
attempt passes `ReadyLocked` and constructs a player through the normal seam. It never
overrides teardown, panic, or an explicit stop.

## Step 1 — Pre-approach consult (solo)

- Mode: `solo` (T-7 — never council). Question asked narrowly with a <200-word cap (T-18
  cap technique). Reply: complete verdict, not truncated, not reasoning-only.
- **Actual answering model: NOT SURFACED by the tool in this session.** The tool output
  carried no model identity and no metadata; session env shows only the main model
  (`kimi-coding/k3`). Recorded as returned rather than guessed (T-18 discipline: never
  stitch a verdict out of reasoning, never invent a model name).
- **Verdict: CHANGE — one real defect, then proceed.**
  1. **`ReadyLocked` order kills the user story (real defect, adopted).** `_initialized` is
     set only on `Initialize`'s success path, and `ReadyLocked` checks `!_initialized`
     BEFORE `_audioDisabledForSession` — so after the zero-endpoint STARTUP failure (the
     exact defect) every play refuses as "not initialised" and the suppression branch + kick
     is never reached. Fix: evaluate suppression (and the kick) on
     `_audioDisabledForSession && !_tornDown` BEFORE the `_initialized` check; do NOT set
     `_initialized` on failure. (Side observation, verified in the tree: today the
     startup-failure path refuses "not initialised" forever; the "audio disabled for the
     session" reason was reachable only after a successful-then-failed device change. Both
     paths are permanent today; the reorder covers both.)
  2. **Probe callback must be exception-proof (adopted).** It runs on a
     `System.Threading.Timer` thread; an escaping exception is process-fatal and leaves
     `_reprobeInFlight` stuck true — permanence returns by a new door. Whole body wrapped in
     try/catch-all (a throw degrades to a typed failed probe: streak++, window re-armed,
     one log line) + `finally { _reprobeInFlight = false; }`.
  3. **Test caveat (noted).** `ManualClock._timers` is an unsynchronised `List`; the
     concurrent single-flight pin is safe only because refused plays schedule nothing but
     the one winner — kept that way.
  4. **Threshold-as-log-line: acceptable, argued** — one line only; reset-on-success pinned
     BEHAVIOURALLY (streak restarts at 1: after a success, one failure must NOT produce the
     escalation line or a still-armed-from-before window), not just by log text.
