# Plan — two P1 defects (ramp stand-down; recap on app close)

## Defect 1: two ramps drive spiral + pink opacity

Upstream evidence (read, not summarised):
- `MainWindow/MainWindow.StartStop.cs:492` — `var sessionActive = _sessionEngine?.IsRunning == true`,
  read ONCE per tick. `_sessionEngine` is the SCRIPTED engine (`Services/Session/SessionEngine.cs:81`).
- The guard is PER-LINK, not wholesale. Guarded: flash opacity `:509`, spiral opacity `:515`,
  pink filter opacity `:523`. **Deliberately NOT guarded**: master volume `:529`, subliminal volume `:537`.
- A FOURTH guarded site the board row does not name: `:549`, the `EndSessionOnRampComplete`
  auto-stop, guarded for upstream's stated reason (#444) at `:544-548`.
- The ramp keeps ticking, keeps its captured bases and still restores them at
  `StopRampTimer` (`:439-481`). Standing down is about WRITES, not about stopping.
- `StartSessionAsync` never touches StartEngine/StopEngine, and sets `_isRunning = true`
  BEFORE `ApplySessionSettings` (`Services/Session/SessionEngine.cs:157`, `:181`).

Port plan:
- `IntensityRampEffect` takes a `Func<bool> scriptedSessionActive` (upstream's null-tolerant read
  shape). `SessionParticipant` wires `() => Scripted is { Running: true }` — a closure, because the
  Ramp is constructed at :369 and the run at :538.
- `Advance`: skip capture AND write for a guarded dial while a session is active. Skipping CAPTURE
  is the port-side requirement: the port captures lazily on first link (D97), so capturing during a
  session would capture the SESSION's value and hand it back to the user at stop (#471 class).
- Completion branch gains `&& !sessionActive` and must NOT latch `_completionAnnounced`, so the
  stop fires on the first tick after the session ends, as upstream's unlatched re-test does.
- `Engage`'s held==0 refusal sentence branches on the cause (code unchanged — `EffectReasonCodes.cs`
  is outside File Scope; report as discovery).

Discovery to verify and report: `ScriptedSessionRun.Start` captures the snapshot BEFORE
`_engine.Stop()`, so a ramp holding spiral/pink/flash makes the session snapshot the RAMPED value
and the ramp's restore then clobbers the session's applied opacity. Fix inside scope by handing the
engine's borrowed dials back before the snapshot.

Fact: real `SessionParticipant`, both clocks hand-driven, one ramp tick inside a running session.
Reds when the ramp writes the dial the session owns.

## Defect 2: recap on app close

Chain: `ApplicationHost.ShutdownAsync` (pre-drain) -> `SessionParticipant.FlushAsync` ->
`Scripted.Stop()` -> `Ended` -> `MediaLog.Complete` -> `LogReady` -> `StudioPage:434` ->
`SessionRecapLaunch.ShowRecap` -> `Show(closedOwner)` -> InvalidOperationException.
`App.axaml.cs:88-97`: `ShutdownMode.OnMainWindowClose`, teardown runs from `desktop.Exit` — the
shell is closed and the UI thread is blocked in `GetAwaiter().GetResult()`.

DECISION: a recap is NOT owed on app close.
1. The user asked the app to quit; a card that appears during process exit cannot be read.
2. The UI thread is blocked inside Exit, so a shown window would never pump — "shown" would be a lie.
3. Nothing is lost: `ScriptedSessionLogStore.Complete` persists synchronously BEFORE it raises
   `LogReady`, and Studio's Recent sessions button reads the same store next launch.

Implementation: `SessionRecapLaunch` watches its owner's `Closed` once; `ShowRecap` refuses first,
logs a DECISION line, constructs nothing. Fixes every caller at the one place they route through.
The try/catch is untouched.
