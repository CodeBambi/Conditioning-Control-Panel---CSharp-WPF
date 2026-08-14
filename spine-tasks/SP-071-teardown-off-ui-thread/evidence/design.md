# SP-071 design — teardown handoff (invariant first)

## Invariant

- **I1 single disposer:** exactly one thread ever calls `_backend.Dispose()` — the backgrounded
  teardown thread started by the FIRST `Dispose` call.
- **I2 never concurrent:** the backend is never disposed while a native device call
  (`EnumerateDevices`/`TryInit`) is in flight. Mechanism (unchanged from SP-070): `_tornDown` is
  set under `_gate` BEFORE any teardown wait, so `InitializeCore`'s early return guarantees no
  NEW native call can start; the teardown thread then drains any ALREADY in-flight call by
  acquiring `_initLock` before disposing.
- **I3 bounded UI wait:** the calling thread's wait is bounded by
  `SoundArbitrationOptions.TeardownBudget`.
- **I4 give-up never touches the backend:** on budget expiry the caller logs ONE typed give-up
  line and returns. No `_backend` call, no `_initLock` acquisition on that path.
- **I5 completion still lands:** after a give-up the backgrounded teardown still acquires
  `_initLock`, waits out the native call, disposes exactly once, logs ONE completion line.
- **I6 idempotent:** a second `Dispose` sees `_tornDown`, starts no second teardown, disposes
  nothing, returns promptly (no wait at all).

## Why a timeout on `_initLock` is the WRONG fix (stop-condition statement)

A bounded `Monitor.TryEnter(_initLock, budget)` that CONTINUES on expiry reaches
`_backend.Dispose()` while a native init is still in flight — the process-fatal
concurrent-native-call class `_initLock` exists to prevent (SP-070). The design below contains
NO path that proceeds to the backend after failing to acquire `_initLock`: the lock wait lives
on the background thread and is UNBOUNDED there; only the caller's observation of completion is
bounded, and its give-up path never reaches the backend.

## Design

`Dispose` (calling thread, UI-safe work unchanged):

1. Under `_gate`: if `_tornDown` return promptly (I6); else set `_tornDown`, capture+clear the
   pending recovery timer, clear `_reprobeInFlight` (SP-070 teardown semantics unchanged).
2. Cancel the captured timer; `PanicReset()` ON THE CALLING THREAD — it stops/disposes
   PLAYERS, which `_initLock` does not guard (it serializes backend DEVICE calls only).
   Players were already disposed off-lock pre-SP-070 (StopAllChannels, PanicReset from any
   thread) and `StopDispose` is best-effort; keeping this on the caller preserves today's
   ordering and keeps player teardown off the backend's native path.
3. Hand backend teardown to a NAMED background thread (`IsBackground = true`, name
   `SoundArbitrationTeardown` — WPF 5a168554 "name the next one" remedy shape): acquire
   `_initLock` (unbounded — drains an in-flight native call), `_backend.Dispose()`, set the
   completion signal. Wrapped in try/catch: an escaping exception on a background thread is
   process-fatal, so a throw degrades to ONE logged line and the signal still sets.
4. Caller waits `done.Wait(TeardownBudget)`. On expiry: set the give-up flag, log ONE give-up
   line, return — never touching `_backend` (I4). On success: return (no new log line —
   ordinary teardown's observable logging is byte-identical to before).
5. The background thread logs the completion line ONLY if the give-up flag is set (transition
   pair: give-up → completion). Ordinary teardown logs nothing new.

## Budget

`SoundArbitrationOptions.TeardownBudget`, default **2 s** — the in-repo precedent is
`TeardownBarkPipeline`'s store waits (`DtrhHostWindow.axaml.cs:257,259,260`, all 2 s on the
same close handler that calls this `Dispose`). Long enough that a healthy teardown (a device
stop+dispose, milliseconds) never trips it; short enough that a wedged endpoint costs the user
one paused close click, not a dead app.

## Reopened host

Each host window constructs its OWN `SoundFlowAudioBackend` + `SoundArbitration`
(`DtrhHostWindow.axaml.cs:213-214`), so a reopened host never inherits the torn-down owner —
it builds a fresh backend. After a give-up, TWO backends can exist momentarily: the old one
(background teardown still waiting out the wedged native call) and the new one. That overlap
is safe by construction: miniaudio devices coexist (the DTRH boundary runs a second
engine/device today, SoundArbitration class doc), and the old backend's only pending native
activity is the wedged init the teardown is waiting on — it plays nothing (PanicReset already
ran on the caller).

## No new dispatch machinery

A plain named `Thread`, an event, and an options knob. No awaitable UI dispatch, no
`SynchronizationContext.Current` capture, no change to `IUiDispatch` (contract §5 stays
post-only).
