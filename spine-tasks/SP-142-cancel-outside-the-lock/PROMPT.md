# SP-142 — `AsyncOperationOwner` must cancel OUTSIDE its own lock, and the ordering must be pinned by a test that bites

## Mission

`Lifecycle/OperationRegistry.cs` runs cancellation callbacks synchronously **inside `lock (_gate)`**, and
`Session/OwnedSessionEffect.cs` reaches `_owner.IsLive(...)` **from under the effect gate**. Two locks, two
orders, both threads real in the product. **Your outcome: the owner never runs a foreign callback while
holding `_gate`, and a test fails if that regresses.**

This is a **correctness fix, not a new user-facing capability.** Do not bill it as parity work.
"The owner" in this packet always means `AsyncOperationOwner`, **never the human owner** — no decision is
gated on this.

## The premise, RE-VERIFIED AGAINST SOURCE at the authoring commit. Reproduce it before you change anything

- `Lifecycle/OperationRegistry.cs:148-158` — `Begin()` calls `_generationCts?.Cancel()` then `?.Dispose()`
  **inside `lock (_gate)`**.
- `Lifecycle/OperationRegistry.cs:161-167` — `Cancel()` does the same inside the same lock.
- `Session/OwnedSessionEffect.cs:143-152` — `Dot` takes the effect gate and evaluates
  `_owner.IsLive(_generation)` while holding it.
- `Lifecycle/OperationRegistry.cs:177-183` — `IsLive` **re-takes `_gate`**.
- **The hazard is that `CancellationTokenSource.Cancel()` runs registered callbacks SYNCHRONOUSLY on the
  calling thread**, so a callback wanting the effect gate meets an effect thread holding the effect gate and
  waiting on `_gate`.
- **Thirteen `OwnedSessionEffect` subclasses sit on this**: `AudioCue`, `BouncingText`, `BrainDrain`,
  `BubbleCount`, `BubblePop`, `FlashImages`, `IntensityRamp`, `LockCard`, `MandatoryVideo`, `MindWipe`,
  `PinkFilter`, `SpiralOverlay`, `Subliminals`.

**READ `Session/OwnedSessionEffect.cs:385-402` FIRST.** It is the preserved reasoning of the SP-106 attempt
that was REVERTED for exactly this inversion. If any of the above disagrees with the source you read, **stop
and report** — a stale premise is worth more than the task.

## THREE TRAPS. The first one is a NEW bug this fix introduces if you are careless

### 1. Moving `Cancel()` out of the lock creates a disposal race that does not exist today
The obvious shape — capture the CTS under the lock, release, then cancel — lets `Cancel()` capture a
reference that a concurrent `Begin()` then **disposes**. `CancellationTokenSource.Cancel()` on a disposed
source throws `ObjectDisposedException`. Today that race is impossible because the lock serialises it.
**Name your chosen resolution in the record and say why the alternatives lose.** Swallowing the exception,
deferring the dispose, and not disposing at all are all defensible; picking one silently is not.

### 2. The stale window WIDENS, and the contract must be shown to tolerate it
Today `IsLive` observes cancellation atomically with the generation. Once cancellation happens outside the
lock there is a window where `Cancel()` has been entered but `IsCancellationRequested` is still false, so
`IsLive` can answer **true** just after a caller asked for cancellation. **Quote the async-contract clause
(§5.4/§5.5, the "must be harmless when it answers false" projection rule) and state whether it tolerates
this.** If it does not, that is a finding and the fix needs a different shape. **Do not widen a window and
leave it unstated.**

### 3. Idempotency and `Begin()`'s return value must not move
`Cancel()` is documented idempotent and must stay so. `Begin()` must still return the generation it
installed, and no caller may observe a generation that was never installed.

## What is NOT in scope

SP-106's stale-teardown read-then-release window under the effect gate stays **OPEN**. This packet only
removes the inversion that made closing it impossible. **Closing that window is a follow-up row, not this
task** — but `OwnedSessionEffect.cs:385-402` currently says closing it under `Gate` *inverts lock order
against the operation owner*, and after this lands that sentence is **false**. Update that doc comment to
say the inversion is gone and the window is now closable, without closing it.

## The test is the hard half, and a test that cannot deadlock proves nothing

Acceptance requires the ordering pinned **deterministically**. The existing suite could never see this
defect because **every test drives arm/disarm on one thread**.

- **No wall-clock waits.** `TestWait` only (`client/tests/CcpClient.Tests/TestWait.cs`). No `Thread.Sleep`,
  no bare `Task.Delay`, no `DateTime`/`Environment.TickCount64` polling. The timing guard fails the build.
- A real ABBA pin needs two threads and explicit handshakes: register a cancellation callback that signals
  it has entered and then blocks until the other thread has taken the effect gate and called into `IsLive`.
- **PROVE THE TEST BITES: revert your fix locally, show the new test hangs or fails, restore it, and put
  both observations in the record.** A concurrency test that passes against the broken code is worthless,
  and this repo has a standing precedent (SP-140's red-watch) for proving a guard bites at its own source.
- If a deterministic pin is genuinely unreachable without a timeout, **say so and explain why** rather than
  smuggling in a wall-clock wait. A named limitation beats a disguised one.

## Standing rules

No TODOs. No new wall-clock waits. Conventional commit. Divergence ids **D329 onward**, exactly five
unescaped pipes per row — escape `|` inside code spans as `\|` and **verify by counting delimiters, not by
reading**. A literal `||` inside a code span has silently destroyed a table cell in this repo twice.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Lifecycle/OperationRegistry.cs`, `client/src/CcpClient.Desktop/Session/OwnedSessionEffect.cs` (the `:385-402` doc comment, and only what the fix requires), `client/tests/CcpClient.Tests/**` (new facts), `client/docs/wpf-surface-reachability.md` (divergences ONLY, D329+), `spine-tasks/SP-142-cancel-outside-the-lock/**` |
| Must not change | everything else, and specifically `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/docs/capability-inventory.md`, `docs/constitution.md`, `ConditioningControlPanel/**` (it is byte-identical to `main` as of SP-141 and MUST STAY SO), `.claude/**`, `client/tools/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-142-cancel-outside-the-lock/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Lifecycle/OperationRegistry.cs` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/docs/task-board.md`, `docs/constitution.md`, `ConditioningControlPanel/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-142-cancel-outside-the-lock/record.md`, `plan.md`, `floor-delta.json` |

**Base pin: 2622 unit / 152 headless.** You are ADDING facts, so declare the positive delta you actually
observe. Do not edit `floor.json` — `sum-deltas` moves the pin from your declaration at the land.

**The floor has a standing environmental family on this machine**: `PointerCoexistenceTests` (3) and
`BubbleCountCapabilityTests.THEOVERLAY...`, which oscillate between 3 and 4 reds and are present on the base
with none of your code. **Compare failure SETS, never counts**, and never close the owner's running
application to obtain a green.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint BEFORE any edit.** State: the exact new shape of `Begin()` and `Cancel()`; your
   resolution of trap 1 and why the alternatives lose; the contract clause that settles trap 2; and the
   two-thread handshake your test will use to pin the ordering without a wall-clock wait.
2. Implement. Cancellation callbacks must not run under `_gate`.
3. Add the ordering facts. Prove they bite by reverting the fix locally.
4. Update `OwnedSessionEffect.cs:385-402` so it no longer claims an inversion that is gone.
5. Verify: floor at or above the pin by your declared delta, warning gate 0W/0E, failure SET unchanged.
6. Divergences **D329 onward**.

## Completion Criteria

- No path in `OperationRegistry` runs a cancellation callback while holding `_gate`.
- The disposal race of trap 1 is resolved and the resolution is argued in the record.
- Trap 2's widened window is quoted against the contract and shown tolerable, or reported as a finding.
- A test pins the ordering and is **shown to fail against the reverted fix**.
- `OwnedSessionEffect.cs:385-402` no longer asserts a live inversion.
- Floor at pin + declared delta; `client/` builds 0W/0E.

## Do NOT

- Close SP-106's stale-teardown window. Out of scope; make it possible, do not do it.
- Add a wall-clock wait to make a concurrency test deterministic.
- Touch `ConditioningControlPanel/**`. It matches `main` byte for byte as of SP-141 and that is now a
  standing invariant.
- Claim this as parity or capability work.

## Git Commit Convention

Conventional commit, `fix(SP-142): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` carrying: the re-verified premise, the chosen resolution of the disposal race with rejected
alternatives, the contract quote settling the widened window, the revert-proof that the new test bites
(both observations), the before/after failure sets, and anything the fix does NOT prove.
