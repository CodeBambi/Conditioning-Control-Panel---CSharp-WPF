# SP-142 record — `AsyncOperationOwner` cancels outside its own lock

Correctness fix in `Lifecycle/**`. **Not parity work, not a capability**, no user-visible behaviour
change. Plan checkpoint at `plan.md`, approved before any product edit.

## 1. The premise, re-verified against source at `ee6398b1e`

Every cited line read as the packet claimed. Nothing to refute.

| Packet claim | What the source said | Verdict |
|---|---|---|
| `OperationRegistry.cs:148-158` — `Begin()` cancels and disposes inside `lock (_gate)` | `_generationCts?.Cancel(); _generationCts?.Dispose(); _generation++; _generationCts = new(...)`, all inside | TRUE |
| `:161-167` — `Cancel()` cancels inside the same lock | `lock (_gate) { _generationCts?.Cancel(); }` | TRUE |
| `:177-183` — `IsLive` re-takes `_gate` | it does | TRUE |
| `OwnedSessionEffect.cs:143-152` — `Dot` evaluates `_owner.IsLive(_generation)` under the effect gate | line 149, inside the `lock (_gate)` opened at 143 | TRUE |
| `Cancel()` runs registrations synchronously on the calling thread | .NET behaviour; the registration is `token.Register` at `OwnedSessionEffect.cs:350-354` | TRUE |
| Thirteen `OwnedSessionEffect` subclasses sit on it | five override `ReleaseWork` directly, the rest inherit `PacedSessionEffect`'s sealed `Interlocked` one | TRUE |

`OwnedSessionEffect.cs:385-402` (the preserved SP-106 reasoning) was read first, as instructed. It
agrees with the packet in every particular and asks for exactly this fix by name at `:406-407`:
*"The real fix is the owner cancelling outside its own lock, which lives in `Lifecycle/**` and is
outside SP-106's File Scope."* It corrected nothing in the packet.

### The one sharpening, which the coordinator asked to be recorded here

**The inversion was LATENT, not live. No deadlock was ever observed — not in a test, not in the
product, not at SP-106.** The packet said "both threads are real in the product", which is true and
was verified, but it implies a reachability nobody established. What is actually true is worse in the
way that matters: the ONLY thing preventing the deadlock was an unenforced convention that every
cancellation callback stay lock-free. That convention is restated in three doc sites in one file
(`OwnedSessionEffect.cs:38-40`, `:346-349`, `:378-381`), it binds thirteen subclasses, no test or
analyzer enforces it, and the one time someone violated it (SP-106, closing the stale-teardown window
under `Gate`) the change passed a fully green suite and had to be reverted by reading. `Arm()` even
calls `_owner.Begin()` outside the effect gate at `:186` with the reason written out — a second piece
of load-bearing discipline held up by a comment.

## 2. The fix

`OperationRegistry.cs` — both cancel paths now capture the source under `_gate` and cancel after
releasing it. `Begin()` at `:173-190`, `Cancel()` at `:196-205`. `IsLive`, `IsCurrent`, `RunAsync`,
`Complete` and `CancelAndDrainAsync` are untouched.

Trap 3 is discharged by construction: the increment and the install remain ONE critical section, so
no caller can observe a generation that was never installed; `Begin()` returns the local captured
under that lock; `Cancel()` stays idempotent because a second `CancellationTokenSource.Cancel()` on
an already-cancelled source is a no-op. Both are asserted (fact 3, and the existing
`CancellationMidFlight` / `StaleGenerationCompletion` facts, which still pass).

One ordering consequence, deliberate: a retired generation's callbacks now run with `_generation`
already advanced. It is the tighter answer — at that instant the new generation IS the current one —
no product callback asks the owner anything (`ReleaseIfStillOurs` reads the effect's own field,
lock-free), and it is what makes fact 3 a zero-wait deterministic pin.

**A second, quieter safety argument for the whole change:** moving a foreign callback out of a lock
can only DELETE edges from the wait-for graph. The callback still runs on the same thread at the same
point in the call; only the set of locks held across it shrinks. So no existing caller can be made
worse, and the only genuinely new hazards are traps 1 and 2 below.

## 3. Trap 1 — the disposal race. Chosen: stop disposing the retired source

**Upstream is the evidence that this race is reachable rather than theoretical, and it takes the
option I rejected.** `Services/Flash/FlashService.cs:345-351` — `Start()` disposes the previous
`_cancellationSource` and installs a new one, with no lock anywhere. `:3910` — teardown disposes it
again. And `:369-370`, inside `Stop()`:

```csharp
try { _cancellationSource?.Cancel(); }
catch (ObjectDisposedException) { }
```

The shipping product cancels a generation source it might have already disposed, and swallows the
exception. That is precisely the shape this packet warned the port could drift into.

**Chosen: the retired source is no longer disposed at all.** It is a deletion, and it is the only
candidate that REMOVES the race instead of handling it. Verified rather than asserted: the source
owns nothing `Dispose` frees — no timer is ever set on it, no `WaitHandle` is touched anywhere under
`client/src`, and all three `CreateLinkedTokenSource` sites (`Ai/LoopbackOllamaProvider.cs:138,215`,
`Persistence/SecretStores.cs:270`) are `using`-scoped and deregister themselves. After `Cancel()` the
registration list is already cleared and the source is unreachable, so `Dispose` frees exactly what
the GC frees.

**No teardown disposal is being skipped, which the coordinator asked me to check.** `Begin()`'s line
153 was the ONLY `Dispose` in the file, and neither `AsyncOperationOwner` nor `OperationRegistry`
implements `IDisposable` — so the FINAL generation's source was never disposed before this change
either. The treatment is now uniform rather than newly absent. I did not add a teardown disposal
path; adding one would be new machinery outside this packet's question.

Rejected alternatives:

- **Swallow `ObjectDisposedException`** (upstream's answer). It hides the wrong half. A `Dispose`
  concurrent with a `Cancel` that already passed the disposed check pulls the registration list out
  from under a running callback — the BCL asserts `_executingCallback == null` inside `Dispose` — and
  that interleaving does not throw. The catch would convert a real race into an invisible one and
  would swallow genuine misuse of a live source forever after.
- **Defer the dispose to a retired list drained at teardown.** New field, new teardown coupling, an
  unbounded retained set (one entry per `Begin()` for the life of a session), and still unsafe unless
  teardown is quiescent. More code, same race.
- **Refcount or claim protocol so exactly one thread disposes.** A refcount here is a lock with extra
  steps; it re-serialises the thing this packet exists to remove.

**Named ceiling, marked in the code:** if a generation source ever gains a timer, a linked
registration or a `WaitHandle`, the dispose comes back — with an ownership protocol, never with a
`try/catch`.

**Not pinned by a test, and why.** Staging *"`Cancel` captured the source, then `Begin` disposed it"*
needs a seam inside `Cancel` between the capture and the use, and there is none. Under the chosen
resolution the race is absent by construction — there is no `Dispose` to lose to — so any test would
be vacuous, and a probabilistic `Begin`/`Cancel` stress loop could never be shown to bite, which this
repository treats as worthless. Argued here instead. Recorded as **D330**.

## 4. Trap 2 — the widened window, quoted against the contract

The widening is real: `Cancel()` can be entered while `IsLive` still answers `true`, where the two
used to be atomic under `_gate`.

`client/docs/async-lifecycle-fault-contract.md` §5.4, the delivery-context table's only stream row:

> Generation check runs **inside the posted delegate on the UI thread**; a stale or never-run post is
> harmless

§5.5:

> Posted delegates must be harmless if they run late or never: during teardown the UI thread is
> blocked inside `ShutdownAsync` (SP-003 invokes it synchronously from the lifetime `Exit` handler),
> so queued posts may execute stale or not at all. The generation check inside the delegate is what
> makes this safe.

And the projection check's own contract, `OperationRegistry.cs:210-214`:

> Projection check for UI posts (contract §5.4/§5.5): same generation AND not cancelled. A posted
> delegate runs this on the UI thread and **must be harmless when it answers false** (stale, torn
> down, or never-run post).

**The contract tolerates it, because every clause constrains the FALSE answer.** `IsLive` is
specified as a filter that may over-suppress, never as a lease that a TRUE answer grants. What
protects applied state is a different rule and is untouched: §3.2/§3.3 gate at the point of
APPLICATION, through `Complete` into `IsCurrent`, which is generation-only, still inside the lock,
and unaffected because `Cancel()` never moves the generation.

**And the widening is unobservable, which is what makes this tolerable rather than merely tolerated.**
No caller holds `_gate` across the work a TRUE answer authorises, and none can — the field is private
and every accessor releases before returning. So a caller **already cannot distinguish** *"Cancel was
entered a nanosecond ago and has not yet flipped the flag"* from *"Cancel happens a nanosecond from
now"*. Every interleaving the new window adds was already reachable through the pre-existing TOCTOU
at a coarser grain. Checked at the two real consumers by reading:

- `OwnedSessionEffect.Dot` (`:143-152`) — its `_armed` read is serialised against `Disarm`'s clear by
  the EFFECT gate, and `Disarm` clears `_armed` *before* calling `_owner.Cancel()` (`:210-226`). So
  when `Dot` sees `_armed == true`, cancellation has not been requested at all yet.
- `EngageIfEligible` (`:306-331`) — already called `IsLive` outside its own gate, and the
  callback-versus-`Engage` ordering it depends on was never serialised by `_gate` either.

**Argued, not tested.** No fact here pins the window. Recorded as **D331**.

## 5. The tests, and the proof that they bite

Three facts in `client/tests/CcpClient.Tests/AsyncLifecycleTests.cs` (the existing home for this
contract; no new file).

1. `Cancel_RunsCancellationCallbacks_WithoutHoldingTheOwnersGate`
2. `Begin_RunsThePreviousGenerationsCallbacks_WithoutHoldingTheOwnersGate`
3. `Begin_InstallsTheNewGeneration_BeforeThePreviousGenerationsCallbackRuns`

Facts 1 and 2 share the two-thread handshake: the parked body registers a callback and signals;
thread C runs the trigger and its callback signals that it is running and then blocks on a
`ManualResetEventSlim` the `finally` **always** sets (a deterministic signal, no timeout argument);
thread P then calls `owner.IsLive(...)` — the exact chain `Dot` takes — and signals when it RETURNS.
If the callback were running under the owner's gate, P could not return until the callback did.

**Why fact 3 bites, since it is not obvious.** A `lock` is re-entrant. In the pre-fix shape the
retired generation's callback ran INSIDE `lock (_gate)` and BEFORE `_generation++`, on the same
thread — so `owner.Generation` re-entered the lock happily and answered the OLD generation.
Cancelling after the critical section is exactly what makes the new generation visible from there.
No second thread, no wait, no timeout, and it fails on `Assert.Equal` in milliseconds if the cancel
moves back under the lock.

### Revert-proof — BOTH observations

**Reverted** (both method bodies restored to their pre-fix form, docs and tests left in place,
rebuilt, `--filter FullyQualifiedName~AsyncLifecycleTests`):

```
Failed!  - Failed: 3, Passed: 12, Skipped: 0, Total: 15, Duration: 40 s
```

- `Cancel_RunsCancellationCallbacks_WithoutHoldingTheOwnersGate` [FAIL, 20 s] —
  `TIMING-VERDICT:CONDITION-NEVER-TRUE — waited the full 20s for the owner's gate to be free while
  one of its cancellation callbacks runs and the deterministic signal never completed: treat as a
  REAL product/test failure. EVIDENCE: ... elapsed=20016ms against a 20000ms window,
  threadpool-pending=0, actor-state: callback running=True, canceller=Background, WaitSleepJoin,
  probe=Background, WaitSleepJoin`
- `Begin_RunsThePreviousGenerationsCallbacks_WithoutHoldingTheOwnersGate` [FAIL, 20 s] — identical
  verdict and identical actor-state.
- `Begin_InstallsTheNewGeneration_BeforeThePreviousGenerationsCallbackRuns` [FAIL, 7 ms] —
  `Assert.Equal() Failure: Values differ. Expected: 1, Actual: 0` (the callback saw the old
  generation, from inside the lock).

The `actor-state` snapshot is worth keeping: **both threads parked in `WaitSleepJoin`** is the
mechanism itself showing up in the failure text — the probe blocked on the owner's gate, the callback
blocked on its release — not merely a symptom. The run **terminated cleanly in 40 s and did not hang
the host**: the `finally` released the callback, the trigger returned, the gate was released, the
probe unblocked and both threads joined. The other 12 facts in the class stayed green.

**Restored** (fix back in place, rebuilt):

```
Passed!  - Failed: 0, Passed: 16, Skipped: 0, Total: 16, Duration: 1 s
```

One second for the whole class, which is the other half of the design: on the passing path **no wait
consumes any wall clock at all** — every signal is already set when it is awaited.

### The named limitation, stated rather than smuggled

The FAILING path costs one `TestWait` window per threaded fact. That is irreducible, not a
convenience: "the other thread made progress" is a positive observation, and its negation — no
progress — is only observable by bounding. The two unbounded alternatives both lose: an unbounded
wait inside the callback is a genuine deadlock that wedges a host with no per-test timeout, and a
real ABBA reproduction against a second lock hangs for the same reason. Failing beats hanging.

No new wall-clock construct was introduced: every bounded wait is `TestWait.Until`, and the only
other waits are `ManualResetEventSlim.Wait()` and `Thread.Join()` with no timeout argument.
`TestTimingGuardTests` passes unchanged and no pin was added to it.

### One guard I tripped and fixed properly

The first floor run added a fifth red of my own:
`VacuousShapeGuardTests.EverySilencingShapeSite_IsDispositionedInTheLedger` flagged facts 1 and 2 as
`no-assertion`, because their assertions lived in the shared helper. That is the documented false
positive of `VacuousShapeDetector` (its own header names it: *"a fact whose assertions live in a
CALLED HELPER reads as no-assertion here while asserting plenty at runtime"*), and the designed
handling is a ledger disposition. **I removed the shape instead of dispositioning it**: the helper
now returns the parked operation's terminal outcome and each fact asserts on it in its own body. That
keeps `client/tests/floor/vacuous-shape-ledger.json` — a shared file outside this packet's File Scope
— untouched, and it makes each fact self-evidently assert.

## 6. Doc corrections

`OwnedSessionEffect.cs` asserted the now-false live inversion in **three** places, not the one the
packet cited. All three were corrected under ruling 1; **no code in that file changed**.

- `:42-50` (class doc) → the inversion existed, SP-142 removed it, **and the lock-free callback
  discipline explicitly STAYS**, with its still-current reason spelled out: the callback runs on
  whatever thread called `Cancel`/`Begin` (a teardown thread, the UI thread, a pool thread — a
  subclass may not assume which), it runs inside that caller's call, so anything it blocks on blocks
  that caller, and a callback that takes locks is fragile against every future caller rather than
  against one known ordering. This was the coordinator's specific trap and it is handled head-on: a
  reader must not conclude the rule was retired with its original rationale.
- `:346-349` (`ParkUntilCancelledAsync`) → same treatment, in comment form.
- `:389-408` (`ReleaseIfStillOurs`, the packet's `:385-402`) → the first chain is gone, the second
  (`Dot` → `IsLive`) is unchanged, closing the residual window under `Gate` is **now possible**, and
  it is **deliberately still not closed** — a follow-up row owns it.

**SP-106's stale-teardown window is still OPEN.** `ReleaseIfStillOurs` is byte-identical to before.

`client/docs/wpf-surface-reachability.md`: new rows **D329-D332**, five unescaped pipes each
(verified by counting delimiters with `awk`, not by reading — every new row totals exactly 5 and no
cell contains an embedded `|`). D91 at line 907 received a **strictly additive** one-sentence pointer
under ruling 2, no rewrite and no deletion; its pipe count is still exactly 5.

## 7. Floor, warnings, failure sets

- **Warning gate: 0 W / 0 E** across 4 projects, forced non-incremental
  (`node client/tests/floor/check-warnings.mjs`). Dropping a `Dispose` on a disposable field raised
  no analyzer diagnostic, as the coordinator asked me to confirm — `AsyncOperationOwner` already held
  a `CancellationTokenSource?` field without implementing `IDisposable`, so nothing changed there.
- **Unit: total 2625** = base pin 2622 + declared delta 3. **Headless: 152 / 152 passing**, = pin 152
  + 0. `floor-delta.json` declares `unit: 3, headless: 0`. **`client/tests/floor/floor.json` was
  never opened.** The floor script reports FAILED against the un-bumped pin; that is expected and is
  the mechanism working, not a failure.
- **Failure SET, compared as a set and not as a count.** The reds are the standing real-desktop
  family and nothing else. Final run with the fix: `PointerCoexistenceTests` x3 +
  `BubbleCountCapabilityTests.THEOVERLAY...` (4).
- **I verified the family against the BASE rather than assuming it**, because one intermediate run of
  mine showed a name the packet's list did not mention
  (`PointerCapabilityTests.AClickAtAPointTheTargetHasLEFTDoesNotReachIt...`). With my entire change
  stashed and the tree rebuilt, the same family run produced **3 reds with yet another membership**:
  `BubbleCountCapabilityTests.THEOVERLAY...`,
  `PointerCapabilityTests.AMoveThatWouldRESIZEAPlacedWindowIsRefused...`, and
  `PointerCapabilityTests.ASTYLESOMETHINGELSECLEARED...`. So the family is
  `{PointerCoexistenceTests.*, PointerCapabilityTests.*, BubbleCountCapabilityTests.THEOVERLAY...}`,
  its membership rotates run to run on the base with none of my code, and **no red outside that
  family appeared in any run of mine**. The owner's application was never closed and
  `CCP_DATA_ROOT` was never exported.

## 8. What this does NOT prove

- **No deadlock was ever observed, before or after.** Nothing here reproduced a product hang, and the
  new facts do not reproduce one: they pin the LOCK-HOLDING property — a second thread can take the
  owner's gate while one of its cancellation callbacks runs — not the absence of every ABBA in the
  tree. The pre-fix red-watch shows two threads parked for the full window, which is the mechanism,
  not a hang in the product.
- **Nothing here is verified anywhere near a screen.** No rendering, no interaction, no audio, no
  focus, no window behaviour, no animation, no headed capture. In-process logic under `dotnet test`,
  Windows only. Nothing about the Linux head was exercised.
- **The disposal race (D330) is argued, not pinned**, for the reason in §3.
- **The widened window (D331) is argued, not pinned**, for the reason in §4. The two consumers were
  checked by reading, not by a test.
- **Nothing was proved about the thirteen subclasses individually.** The fix removes the constraint
  that bound them; no subclass was changed and none now takes a lock in a callback.
- **The stale-teardown window is still open** and this packet does not claim otherwise.

## 9. Discoveries outside File Scope — for the land, not edited here

- `client/docs/task-board.md:84` — the P1 row states the inversion as live and cites the pre-fix line
  numbers. It is the orchestrator's file.
- `client/memories/port-status.md:706` — *"Standing trap, do not relearn it: `AsyncOperationOwner`
  runs cancellation callbacks INSIDE its own lock"*. False as of this commit; the durable memory
  should say the fix landed and that the lock-free callback rule survives it for a different reason.
- **Intra-`client` citation drift (D332).** The fix moved `OperationRegistry.cs` line numbers, so six
  citations elsewhere now point a few lines off: `Persistence/PersistenceStore.cs:197`,
  `Ai/AiMemoryStore.cs:253`, `Ai/AiOperationPipeline.cs:446`, `Features/Dtrh/DtrhSaveSlots.cs:208`,
  `Effects/IntensityDial.cs:48`, `tests/CcpClient.Tests/PersistenceStoreTests.cs:103`. All six are
  outside File Scope, so they are recorded rather than silently re-anchored. Nothing goes red:
  `CitationNeedleTests` resolves UPSTREAM needles at a frozen SHA and never re-derives intra-`client`
  line numbers — which is exactly why this rot stays invisible until someone follows one.
