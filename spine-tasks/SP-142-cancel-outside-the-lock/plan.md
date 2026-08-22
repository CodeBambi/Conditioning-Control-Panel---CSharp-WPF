# SP-142 plan — cancel outside the lock

Review level 3. Nothing under `client/src` or `client/tests` is edited until this plan is approved.

## 0. Premise re-verified against source at `ee6398b1e` — CONFIRMED, no correction needed

| Packet claim | Source | Verdict |
|---|---|---|
| `OperationRegistry.cs:148-158` `Begin()` cancels + disposes inside `lock (_gate)` | reads `_generationCts?.Cancel(); _generationCts?.Dispose(); _generation++; _generationCts = new(...)` all inside the lock | TRUE |
| `:161-167` `Cancel()` cancels inside the same lock | `lock (_gate) { _generationCts?.Cancel(); }` | TRUE |
| `:177-183` `IsLive` re-takes `_gate` | it does | TRUE |
| `OwnedSessionEffect.cs:143-152` `Dot` holds the effect gate across `_owner.IsLive(_generation)` | line 149 is inside the `lock (_gate)` opened at 143 | TRUE |
| `CancellationTokenSource.Cancel()` runs registrations synchronously on the caller's thread | .NET documented behaviour; the registration in question is `token.Register` at `OwnedSessionEffect.cs:350-354` | TRUE |

One sharpening, not a refutation: **the deadlock is latent, not live.** Nothing in the tree deadlocks
today, because `ReleaseIfStillOurs` is deliberately lock-free (`OwnedSessionEffect.cs:378-381`) and
`Arm()` calls `Begin()` outside the effect gate on purpose (`:183-186`). The defect is that the
lock-free discipline is the ONLY thing holding it, it is unenforced, it binds all thirteen
subclasses, and it is what forced SP-106's revert. `OwnedSessionEffect.cs:385-402` agrees with the
packet in every particular and asks for exactly this fix by name (`:406-407`: *"The real fix is the
owner cancelling outside its own lock, which lives in `Lifecycle/**` and is outside SP-106's File
Scope"*). Nothing to refute.

Also confirmed: no caller can be made worse by the change. The callback still runs on the same
thread, at the same point in the call; the change only REMOVES `_gate` from the set of locks held
across it, which can only delete edges from the wait-for graph. The two genuinely new hazards are
traps 1 and 2, below.

## 1. The exact new shape

```csharp
public int Begin()
{
    CancellationTokenSource? retired;
    int generation;
    lock (_gate)
    {
        retired = _generationCts;
        _generation++;
        _generationCts = new CancellationTokenSource();
        generation = _generation;
    }

    // SP-142: OUTSIDE the lock. Cancel() runs registered callbacks synchronously on THIS
    // thread, so a callback that takes any other lock used to take it beneath _gate.
    retired?.Cancel();
    return generation;
}

public void Cancel()
{
    CancellationTokenSource? current;
    lock (_gate)
    {
        current = _generationCts;
    }

    current?.Cancel();
}
```

`IsLive`, `IsCurrent`, `RunAsync`, `Complete`, `CancelAndDrainAsync` are untouched.

Trap 3 discharged by construction: the increment and the install stay in ONE critical section, so no
caller can observe a generation that was never installed; `Begin()` returns the local captured under
that same lock; `Cancel()` stays idempotent because a second `CancellationTokenSource.Cancel()` on an
already-cancelled source is a no-op.

One ordering consequence, deliberate and stated: the retired generation's callbacks now run when
`_generation` is ALREADY the new value. It is the tighter answer (at that instant the new generation
IS current), no product callback asks the owner anything (`ReleaseIfStillOurs` reads the effect's own
field), and it is what makes fact 3 below a zero-wait deterministic pin.

## 2. Trap 1 — the disposal race. Chosen: STOP DISPOSING the retired source

Rejected, with reasons:

- **Swallow `ObjectDisposedException` around the out-of-lock `Cancel()`.** It hides the wrong half of
  the race. `CancellationTokenSource.Dispose` is not safe against a concurrent `Cancel` that has
  already passed the disposed check — the BCL asserts `_executingCallback == null` in `Dispose` and
  nulls the registration list — so the exception is the visible symptom of an interleaving that is
  also unsafe when it does NOT throw. A catch converts a real race into an invisible one, and it
  would swallow genuine misuse forever after.
- **Defer the dispose to a retired list drained at teardown.** New field, new teardown coupling, an
  unbounded retained set for a long session (one entry per `Begin()`), and it is still unsafe unless
  teardown is quiescent. More code, same race.
- **Refcount / claim protocol so exactly one thread disposes.** A refcount here is a lock with extra
  steps; it re-serialises the very thing this packet is removing.
- **Keep disposing, cancel under the lock (do nothing).** That is the defect.

Why not disposing is correct and not merely cheap: a generation source owns **nothing that Dispose
frees**. Verified in the tree — no `WaitHandle` is ever touched (`grep WaitHandle client/src` is
empty), no timer is ever set (no `CancelAfter`/`new CancellationTokenSource(TimeSpan)` on this type),
and all three `CreateLinkedTokenSource` sites (`Ai/LoopbackOllamaProvider.cs:138,215`,
`Persistence/SecretStores.cs:270`) are `using`-scoped, so they deregister themselves. After
`Cancel()` the registration list is already cleared and the source is unreachable, so `Dispose()`
frees exactly what the GC frees. The choice is a DELETION, and it is the only candidate that removes
the race rather than handling it — which is also why it leaves nothing to test (see §4).

Marked in code with a `ponytail:` comment naming the ceiling: if a generation source ever gains a
timer, a linked registration or a `WaitHandle`, the dispose has to come back, and it comes back with
an ownership protocol, not with a `try/catch`.

## 3. Trap 2 — the widened visibility window. The contract clause, quoted

`async-lifecycle-fault-contract.md` §5.4 (delivery-context table, the one stream row):

> Generation check runs **inside the posted delegate on the UI thread**; a stale or never-run post is
> harmless

§5.5:

> Posted delegates must be harmless if they run late or never: during teardown the UI thread is
> blocked inside `ShutdownAsync` ... so queued posts may execute stale or not at all. The generation
> check inside the delegate is what makes this safe.

And the projection check's own contract, `OperationRegistry.cs:172-176`:

> A posted delegate runs this on the UI thread and **must be harmless when it answers false** (stale,
> torn down, or never-run post).

**It tolerates the widening, and the reason is that the clause constrains the FALSE answer only.**
`IsLive` is specified as a filter that may over-suppress, never as a lease on a live generation: the
contract's safety property is that a suppressed or never-run projection is harmless, not that a TRUE
answer authorises anything. What actually protects applied state is §3.2/§3.3 — staleness gates
application at the point of application — and that runs through `Complete` → `IsCurrent`, which is
generation-only, still inside the lock, and untouched by this change (`Cancel()` never moves the
generation).

Second, independent argument: the widening is **not observable**. No caller holds `_gate` across the
work a TRUE answer authorises, and none can — the field is private and every accessor releases before
returning. So a caller already cannot distinguish "Cancel was entered 1 ns ago and has not yet
flipped the flag" from "Cancel is entered 1 ns from now", and every interleaving the new window adds
is already reachable today through the pre-existing TOCTOU. Checked at the two real consumers:

- `OwnedSessionEffect.Dot` (`:143-152`) — the `_armed` read is serialised against `Disarm`'s clear by
  the EFFECT gate, and `Disarm` clears `_armed` before calling `_owner.Cancel()` (`:210-226`), so if
  `Dot` sees `_armed == true` the cancel has not been requested yet at all.
- `EngageIfEligible` (`:306-331`) — already calls `IsLive` outside the gate, and the callback-vs-
  Engage ordering it depends on was never serialised by `_gate` either.

Both are reported in the record as reasoning, not as a claim of test coverage.

## 4. The test — two threads, explicit handshakes, no wall-clock wait

Three facts in `client/tests/CcpClient.Tests/AsyncLifecycleTests.cs` (existing home for this
contract; no new file). `floor-delta.json`: `unit: 3`, `headless: 0`.

**Fact 1 `Cancel_RunsCancellationCallbacks_WithoutHoldingTheOwnersGate`** and
**Fact 2 `Begin_RunsThePreviousGenerationsCallbacks_WithoutHoldingTheOwnersGate`** — one shared
helper, parameterised by the trigger (`owner.Cancel()` / `owner.Begin()`). The handshake:

1. `owner.Begin()`, then `owner.RunAsync("parked", ...)` whose body registers a cancellation callback
   and parks on a TCS — the product's own `ParkUntilCancelledAsync` shape. The body signals
   `registered`; the test awaits that signal via `TestWait.Until(Task, ...)`.
2. Dedicated background **thread C** calls the trigger. Its cancellation callback runs synchronously
   on C: it sets `callbackEntered`, then blocks on `releaseCallback.Wait()` — **no argument, no
   timeout**: it is a deterministic signal the test's `finally` always sets, not a clock.
3. Test thread awaits `callbackEntered`. The callback is now provably mid-flight.
4. Dedicated background **thread P** calls `owner.IsLive(generation)` — the exact product chain
   `Dot` takes — and signals `probed` when it RETURNS. If the callback is running under `_gate`, P
   cannot return until the callback does.
5. Test thread: `await TestWait.Until(probed.Task, "the owner's gate to be free while a cancellation
   callback runs")`.
6. `finally`: `releaseCallback.Set()`, `Join` both threads, then assert the owned completion is
   `Cancelled` and `OutstandingOperations == 0`.

Every wait is either a deterministic signal or `TestWait`. No `Thread.Sleep`, no `Task.Delay`, no
`DateTime`/`TickCount64`, and none of the guard's forbidden tokens (`.Wait(TimeSpan`,
`.WaitOne(TimeSpan`, `SpinWait`, ...) — `ManualResetEventSlim.Wait()` and `Thread.Join()` take no
timeout.

**Against the reverted fix this FAILS rather than hangs**, which is the property that makes it
shippable: `TestWait.Until(Task)` expires on its shared window with
`TIMING-VERDICT:CONDITION-NEVER-TRUE — the deterministic signal never completed: treat as a REAL
product/test failure`, the `finally` then releases the callback, the trigger returns, `_gate` is
released, P unblocks and both threads join. No wedged host, no leaked thread. On the PASSING path no
wait consumes any wall clock at all: every signal is already set when it is awaited.

**Named limitation, not smuggled:** the failing path costs one `TestWait` window per fact. That is
irreducible — "the other thread made progress" is a positive observation, and its negation (no
progress within any interval) is only observable by bounding. The alternative shapes both lose: an
unbounded wait inside the callback is a genuine deadlock that hangs the test host forever (this suite
has no per-test timeout), and a real ABBA reproduction against a second lock hangs for the same
reason.

**Fact 3 `Begin_InstallsTheNewGeneration_BeforeThePreviousGenerationsCallbackRuns`** — single
thread, zero waits, zero timeouts. The retired generation's callback records `owner.Generation`.
Fixed: it observes the NEW generation (the install completed before the lock was released). Reverted:
`lock` is re-entrant on the same thread, so it observes the OLD one and the test fails instantly on
`Assert.Equal`. Also pins trap 3: `second == first + 1`, and `Begin()` returned what it installed.

**Revert-proof procedure (recorded in `record.md`, both observations):** restore the two methods to
their pre-fix bodies locally, run the three facts, record the exact failure text and duration for
each; restore the fix; record the green.

**What no test here can pin, stated rather than hidden:** the disposal race of trap 1. Staging
"`Cancel()` captured the source, then `Begin()` disposed it" needs a seam inside `Cancel()` between
capture and use, and there is none. Under the chosen resolution the race is absent by construction —
there is no `Dispose` call to lose to — so the only available test would be vacuous, and a
probabilistic Begin/Cancel stress loop could never be shown to bite. It is argued in the record
instead.

## 5. Doc edits, and one scope judgement I want ruled on

Packet scope names `OwnedSessionEffect.cs:385-402`. That paragraph is one of **three** places in the
SAME file asserting the now-false live inversion:

- `:42-50` (class doc) — *"The operation owner cancels a generation while holding ITS OWN lock ...
  therefore inverts lock order between two threads that both really exist"*.
- `:346-349` (`ParkUntilCancelledAsync`) — *"it runs on a teardown thread, UNDER the operation
  owner's own lock (OperationRegistry.cs:163-166 cancels inside it)"*.
- `:389-408` (`ReleaseIfStillOurs`, the packet's `:385-402`) — the SP-106 reasoning.

I intend to correct all three, minimally, because the packet's criterion is *"no longer asserts a
live inversion"* and leaving two false statements behind in the same file is worse than the one the
packet happened to cite. All three are doc comments in a file the scope already opens. Each will say:
the inversion existed, SP-142 removed it, the residual read-then-release window is **still open and
deliberately NOT closed here**, and it is now closable under `Gate` without inverting anything. Line
citations into `OperationRegistry.cs` get re-anchored to the post-fix lines. **No code in
`OwnedSessionEffect.cs` changes** — `ReleaseIfStillOurs` stays exactly as it is, lock-free.

If the reviewer wants strictly the cited paragraph, say so and I will leave `:42-50` and `:346-349`
false and report them as a discovery instead.

## 6. Divergences, floor, and out-of-scope findings

- `client/docs/wpf-surface-reachability.md`, new rows **D329+** (4 columns, 5 pipes per row, `|`
  inside code spans escaped and counted by delimiter): the inversion removal and what it does NOT
  close; the never-dispose choice and its ceiling; the intra-`client` citation drift my edit causes.
- **D91 (`:907`) also asserts the live inversion** and is OUT of scope (the file is open to
  divergences D329+ only). My row will supersede it by name; the record flags it for the land.
- Out of scope, reported not edited: `client/docs/task-board.md:84` (the P1 row, orchestrator's) and
  `client/memories/port-status.md:706` (*"Standing trap, do not relearn it"*), both of which become
  stale the moment this lands.
- Citation drift: my edit shifts `OperationRegistry.cs` line numbers, so `:161-167`, `:177-183`,
  `:204-208`, `:216-245`, `:148-159` as cited from `Persistence/PersistenceStore.cs:197`,
  `Ai/AiMemoryStore.cs:253`, `Ai/AiOperationPipeline.cs:446`, `Features/Dtrh/DtrhSaveSlots.cs:208`,
  `Effects/IntensityDial.cs:48` and `tests/CcpClient.Tests/PersistenceStoreTests.cs:103` move with
  it. None is enforced by a guard (`CitationNeedleTests` resolves UPSTREAM needles at a frozen SHA,
  never intra-client line numbers), so nothing goes red — it is D327's rot class, and all six files
  are outside File Scope. Recorded as a divergence + a report line, not silently widened.
- Floor: base pin 2622 unit / 152 headless; declared delta `unit +3`, `headless 0`; expected observed
  **2625 / 152**. Failure SETS compared against the base, never counts — the standing environmental
  family (`PointerCoexistenceTests` x3, `BubbleCountCapabilityTests.THEOVERLAY...`) oscillates 3-4
  reds with none of my code. `client/tests/floor/floor.json` is never opened.
