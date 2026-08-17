# SP-084 — A cancelled capability probe can be recorded as a completed probe verdict

**Supersedes SP-075** (wave 31, escalated at the plan gate having written no product code; record: `spine-tasks/CONTEXT.md`, wave-31 section). Same work, new ID: the packet ID is execution state, the durable identity is the board row. Renamed rather than reissued so SP-0075-as-escalated stays exactly what the wave-31 record describes.

## Mission

`CapabilityProbeRunner` wraps every capability probe in an owned operation whose body is four lines: await the probe, return `Completed`. The return is **unconditional** — `CapabilityRegistry.cs:103` returns `OperationOutcome.Completed.Instance` no matter what the token did while the probe was running.

That is correct for every probe that lets cancellation propagate: the `OperationCanceledException` reaches `OperationRegistry.cs:223` and maps to `Cancelled`. It is **wrong for a probe that swallows cancellation** and returns a state anyway. The registry then applies that state as a probe verdict, and the app records "we probed this capability and here is the answer" for a probe that was stopped. Same lie SP-067 fixed at the loop check — "finished" reported for "stopped" — arriving through a different door.

The consequence is honesty of capability state, which the SP-006 truthful-capability contract rests on. `runtime-capability-contract.md` §3 rule 3 says a cancelled probe leaves the capability `Unavailable(not-probed)`, and the caller's switch treats not-probed as honest absence. A false `Completed` writes a verdict that never happened into the one place the product is supposed to be unable to lie.

**The premise of this row was re-verified against the port tree at authoring, symbol by symbol, and it holds.** The defect is real, it is at the line the board names, and no consumer refactor stands between you and it.

Your outcome: **a cancelled probe can no longer produce a recorded capability verdict, and a fact in the suite bites if that regresses.** Which of two mechanisms delivers it is decided in Step 2 against a rule pre-authorized both ways below.

## Dependencies

SP-067 (landed, integrate `75a09d61`) — the same class at the loop check, and the shape this packet's Branch A copies. Its board row is WIP pending owner ratification; **do not touch it**. SP-073 and the other lanes of this wave run concurrently against disjoint file scopes; nothing you need is in theirs.

## Context to Read First

Verified by the orchestrator at authoring — every line below was opened in the **port tree** and confirmed, not transcribed from the board:

**The defect itself**

- `client/src/CcpClient.Desktop/Capabilities/CapabilityRegistry.cs:100-104` — the owned-operation body. `:102` is `probed = await probe(token).ConfigureAwait(false);` and `:103` is the unconditional `return OperationOutcome.Completed.Instance;`. This is the line the row names and it is exactly as described.
- `:106-120` — the outcome→state switch. `:108-111` `Completed` → `registry.Apply(name, probed ?? Faulted)`. `:117-119` `Cancelled` → **an empty arm with a comment**: the capability stays not-probed. The remedy therefore needs no new switch arm; the honest destination already exists and is already wired.
- `:109-110` — `probed ?? new CapabilityState.Faulted(... ProbeFault, "probe completed without producing a state")`. Read this before you consider dropping `probed` on cancellation; see Do NOT item 3.
- `client/src/CcpClient.Desktop/Lifecycle/OperationRegistry.cs:216-245` — `RunAsync`'s body wrapper. `:223` is `catch (OperationCanceledException) when (token.IsCancellationRequested)` → `Cancelled`. **This is the door a swallowing probe walks straight past**, and it is why the board correctly dispositioned this out of SP-067: the OCE mapping is not broken, it is simply never reached.
- `client/src/CcpClient.Desktop/Lifecycle/CompositionRoot.cs:270` — `new CapabilityProbeRunner(infra.Registry.OwnerFor("CapabilityProbes"), capabilities)`. This is the live startup path, not a test-only seam.

**The two tokens — read this twice, it is the packet's central trap**

- `CapabilityRegistry.cs:89` — `RunAllAsync(CancellationToken cancellationToken)`. That parameter is the **startup** token, checked between probes at `:94-97`.
- `OperationRegistry.cs:200-212` — the token handed to the body comes from the owner's `_generationCts`, taken under the owner's lock. It is **not** `RunAllAsync`'s parameter and has no relationship to it.
- `OperationRegistry.cs:160-167` — `Cancel()` cancels `_generationCts` only. Teardown (`CancelAndDrainAsync`, `:79-82`) does the same. **Nothing in the product cancels the startup token during teardown.**

**The contracts**

- `client/docs/runtime-capability-contract.md` §3 rule 3 — the outcome→state mapping, and specifically "`OperationOutcome.Cancelled` (startup cancelled or teardown raced the probe) → the capability stays `Unavailable(not-probed)`".
- Same document §4 — the honesty rule ("Faking availability is a contract violation... in production code, in tests, and in CI").
- Same document §6 — §3 rule 3 is named as **the only bridge** across the row-3/row-5 boundary. You are editing that bridge; that is why this is Review Level 3 and not 2.
- `client/docs/async-lifecycle-fault-contract.md` §2 — the definitions of `Completed` and `Cancelled`, and the sentence that decides Do NOT item 1: expected failures are typed values, never exceptions-as-control-flow.
- `client/src/CcpClient.Desktop/Lifecycle/Participants.cs:110-112` — SP-067's fix, the token-typed ternary, cited to async contract §2. **The board cites this as `Participants.cs:108`; in the tree today the `return` is at `:110` and the two arms at `:111-112`.** Cite what you read.
- `client/src/CcpClient.Desktop/Capabilities/CapabilityState.cs:4-44` — `CapabilityReasonCodes`. `ProbeFault` exists at `:28`. **There is no cancellation reason code, and you may not add one** — that file is outside your scope, and neither branch of the decision needs it.

**The fixtures you will extend**

- `client/tests/CcpClient.Tests/CapabilityTests.cs:113-135` — `ProbeCancelledMidFlight_StaysNotProbed_NeverAvailable`. This is the shape to copy: register a probe, start `RunAllAsync(CancellationToken.None)`, await a `TaskCompletionSource` the probe sets, then `owner.Cancel()`. Note `:121` carries a `// wallclock-allow:` justification for its `Task.Delay(Timeout.Infinite, token)`; if you reuse that shape, carry an equivalent justification or the timing guard reds you.
- `client/tests/CcpClient.Tests/CapabilityTests.cs:93-111` — `StartupCancelled_LeavesRemainingProbesHonestlyNotProbed`. It cancels the **startup** token and returns at `:94-97` before any probe runs at all.
- Those two tests cancel **different tokens**, and no existing test cancels the generation token *and* observes a probe that returns normally. That gap is the whole defect, and it is also how a wrong fix could pass a careless new test.
- `client/tests/CcpClient.Tests/CapabilityTests.cs:15-19` — `RunnerFor`, which discards the owner. The mid-flight test at `:124-126` builds the owner by hand because it needs to cancel it. You will need the same.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Capabilities/CapabilityRegistry.cs`, `client/tests/CcpClient.Tests/CapabilityTests.cs`, `spine-tasks/SP-084-capability-probe-cancelled-completed/**` |
| Must not change | everything else, and specifically the files named in the contract below |

The scopes across this wave were assigned pairwise disjoint. **Do not widen this one**, including into `CapabilityState.cs`, `OperationRegistry.cs`, or `Participants.cs` — all three are adjacent and all three are tempting. The fix site and its fixture both sit inside the two files above; that was checked at authoring, not assumed.

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-084-capability-probe-cancelled-completed/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Capabilities/CapabilityRegistry.cs`, `client/tests/CcpClient.Tests/CapabilityTests.cs` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/docs/task-board.md`, `ConditioningControlPanel/**`, `client/docs/**`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-084-capability-probe-cancelled-completed/record.md`, `spine-tasks/SP-084-capability-probe-cancelled-completed/floor-delta.json` |

**You do not edit `client/tests/floor/floor.json`.** That file is the shared pin and concurrent lanes collide on it. Write your count change into `floor-delta.json` in your own folder instead:

```json
{ "packet": "SP-084-capability-probe-cancelled-completed", "unit": 0, "headless": 0, "reason": "one line naming the facts you added" }
```

Declare `0`/`0` if you add no tests; omitting the file is not the same as declaring zero. The land sums every packet's delta and applies one bump. `client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs` enforces both halves of this and will fail your run if the row or the disclaimer is missing.

## Review Level: 3 (Plan, Code, Final)

Three reasons, any one of which would be enough: the change re-types the terminal outcome of an operation whose token is cancelled by teardown from another thread; it edits the single mapping `runtime-capability-contract.md` §6 names as the only bridge across the row-3/row-5 boundary; and it sits on the live startup path (`CompositionRoot.cs:270`), so a mistake changes what the shipped app believes about its own capabilities.

## Steps

### Step 1: Reproduce the defect BEFORE you change any source

Write the swallowing-probe test first, against the unmodified tree, and **watch it go red**. The probe shape that reproduces it:

- register a probe that sets a `TaskCompletionSource`, awaits a token-observed wait, **catches the `OperationCanceledException` and does not rethrow**, then returns a state;
- start `RunAllAsync(CancellationToken.None)`, await the TCS, then `owner.Cancel()` — the teardown shape, exactly as `CapabilityTests.cs:113-135` does it;
- assert through `registry.GetState(name)` that the capability is `Unavailable` with `NotProbed`.

Paste the **actual assertion message** of that pre-fix red into `record.md`. A revert-red produced after the fix exists and a genuine pre-fix red are not the same evidence, and this run has been burned by the difference before. If the test does not go red on the unmodified tree, **stop and report** — either your probe is not actually swallowing, or the premise moved under you, and both are findings worth more than a fix.

### Step 2: Decide the mechanism against the pre-authorized rule

**THE DECISION IS PRE-AUTHORIZED BOTH WAYS. Resolve it on your evidence; do not ask.**

The discriminating question, answerable from `OperationRegistry.cs:216-245` and `CapabilityRegistry.cs:100-104` alone: **at the instant the body returns, can the registry distinguish (i) a probe that caught its cancellation and fabricated a state from (ii) a probe that genuinely finished its work in the window just before the token was cancelled?**

- **If they are indistinguishable** — the body knows only "a state was produced" and "the token is now cancelled" — then `Faulted` is not an honest classification, because it would record a fault against a probe that may have done nothing wrong, and a `Faulted` verdict is *sticky recorded state* where not-probed is *honest absence*. Take **Branch A**: token-type the return inside the lambda, the `Participants.cs:110-112` shape, cited to async contract §2. The existing empty `Cancelled` arm at `:117-119` then leaves the capability not-probed with no further change. **State the cost plainly in `record.md`:** a genuinely-complete probe that raced teardown now has its real verdict discarded as not-probed. That is a conservatism loss, not an honesty loss, and contract §3 rule 3 already prices it.
- **If you find a fact that DOES distinguish them**, **Branch B** becomes admissible: reject the swallowing probe at the boundary as `Faulted` using the **existing** `CapabilityReasonCodes.ProbeFault` (`CapabilityState.cs:28`). Then `record.md` must state that a raced-but-complete probe is now recorded as faulted, name the fact that makes that safe, and say why it beats Branch A.

Whichever branch you take, **the `Completed` arm must stay reachable.** SP-067's board row records that its own `Completed` arm became unreachable and was documentary only; here an uncancelled probe must still produce `Completed` and still have its state applied. Pin that with a negative control (Step 3).

Take the pre-approach advisory gate at the end of this step with your answer to the discriminating question attached and your branch stated. Do not ask before you have it.

### Step 3: Implement at the only site that can work

**Where the observation must live, and why nowhere else does.** Both branches need to read the **owner's generation token** — the lambda's `token` parameter at `CapabilityRegistry.cs:100`. Two facts make every other placement wrong:

1. `RunAllAsync`'s `cancellationToken` parameter is in closure scope inside that lambda. Writing `cancellationToken.IsCancellationRequested` there **compiles fine and fixes nothing**, because teardown cancels only the generation token (`OperationRegistry.cs:160-167`, `:79-82`). Worse, a new test that cancelled the *startup* token instead of calling `owner.Cancel()` would make that wrong fix look green. Your test must cancel via `owner.Cancel()`, like `CapabilityTests.cs:130` does.
2. Everything after `await owner.RunAsync(...)` — including the `switch` at `:106` — is outside the lambda and **has no access to the generation token at all**. This cannot be fixed in the switch. Do not spend time trying.

**Testability, settled now rather than in review:** the entire mechanism is reachable from a headless unit test. It is pure lifecycle logic in `CcpClient.Tests`, no Avalonia, no headed capture, no `CcpClient.HeadlessTests`, no manual gate — and `CapabilityTests.cs:113-135` already proves this exact fixture shape runs there today. This project has three times discovered at review that a mechanism sat where no headless fact could reach it; that is not the case here, and if you find yourself reaching for a headed capture or a new fixture project, you have wandered out of the packet.

### Step 4: Bind the behaviour, one source at a time

Every fact you add must be proven to bite by an **independent revert** of the single source change it guards, run one at a time, with the tree restored byte-identically between reverts and the red count recorded for each.

With one source change, "independent" means: restore `:103` to the unconditional `return OperationOutcome.Completed.Instance;`, run the suite, and record **which tests go red and which stay green**.

- Your swallowing-probe pin must be in the **red** set.
- Your negative control must be in the **green** set. A negative control that also reds is not a negative control, it is a duplicate wearing a different name.
- `ProbeCancelledMidFlight_StaysNotProbed_NeverAvailable` (`:113-135`) must stay green in **both** trees — it exercises the OCE path, which this change does not touch. If it moves, something is wrong with your fix, not with that test.

**The vacuity bar:** assert the **recorded state** through `registry.GetState(name)`, never the `OperationOutcome` object. The lie this row is about is a recorded capability verdict, so the fact has to read the verdict. An assertion on the outcome type passes through the same door the defect uses.

Consider a second pin for the sibling shape — a probe that never throws at all, checks `token.IsCancellationRequested` itself and returns a state early. It reaches the same unconditional `return` by a different route and costs almost nothing to bind. If you decide it is redundant, say so with the reason rather than silently omitting it.

### Step 5: Record

`record.md`: the pre-fix red with its real assertion message; your answer to Step 2's discriminating question and which branch it selected, with the reasoning; the revert matrix with red counts and the green set named; and an honesty section stating what is **not** proven — at minimum, that no shipped probe in `CompositionRoot.cs` has been shown to swallow cancellation, so this closes a **reachable** door rather than an **observed** production incident. Say that plainly; do not inflate it into a bug users hit.

`floor-delta.json` with your real counts (expect a small positive `unit`, `0` headless).

### Step 6: Verification

```
dotnet build client/CcpClient.sln -c Debug --nologo
```
```
node client/tests/floor/check-floor.mjs
```

Run them as **separate commands**. The worktree isolation guard refuses compound shell commands (`cd X && ...`), so chain nothing.

**Build immediately before the gate, every time.** The wrapper runs `--no-build`; a stale `bin/` once reported 1022 against a tree that contained 1018, which is a green that means nothing.

Your floor run will report a total that does **not** match the pin, because the pin is bumped at land from the summed deltas and not by you. That is expected and is not a failure of your work: confirm the observed total equals `pin + your declared delta`, and state both numbers in your report. READ THE PIN AND THE SKIP LIST FROM `client/tests/floor/floor.json`, never from this packet: both have already gone stale (it said 1018 with 5 named skips; the pin is now higher and the skip list longer).

## Completion Criteria

- The pre-fix red is captured with its real message, from the unmodified tree.
- Step 2's discriminating question is answered from the code, and the branch it selected is implemented.
- The observation reads the owner's generation token, and the new test cancels via `owner.Cancel()`.
- The `Completed` arm is still reachable and a negative control proves it.
- Every new fact bites under the independent revert; the negative control and the existing mid-flight test stay green in both trees.
- `record.md` and `floor-delta.json` exist and are accurate.
- Build 0W/0E.
- The SP-067 board row and the other 12 sites its sweep dispositioned are untouched.

## Do NOT

1. **Do not use `token.ThrowIfCancellationRequested()` after `await probe(token)`.** It reaches the right outcome through `OperationRegistry.cs:223` — and it is exceptions-as-control-flow for an expected condition, which `async-lifecycle-fault-contract.md` §2 rejects in its opening sentence. The typed value is the shape the contract and SP-067 both use.
2. **Do not edit `OperationRegistry.RunAsync` to observe the token after the body returns.** Out of scope, and it silently re-types the terminal outcome of *every* owned operation in the product — every participant, the AI pipeline, DtRH — on a live path, from a packet that verified one of them.
3. **Do not discard `probed` on cancellation while still returning `Completed`.** The null maps at `:109-110` to `Faulted(ProbeFault, "probe completed without producing a state")` — a fabricated fault for a probe that was merely stopped. Same lie, third door.
4. **Do not add a reason code to `CapabilityState.cs`,** or otherwise widen File Scope. Neither branch needs one.
5. **Do not close this row with prose in a contract document** saying probes must not swallow cancellation. A rule with no machinery behind it is the exact class board row T-18 exists to stop. It may accompany the fix; it may not be the fix.
6. **Do not widen this into a sweep of other `Completed` returns.** The SP-067 sweep dispositioned the other 12 sites and the row says so explicitly.
7. Do not weaken, delete, or quarantine any existing assertion to make a red go away — including `ProbeCancelledMidFlight_StaysNotProbed_NeverAvailable` and `StartupCancelled_LeavesRemainingProbesHonestlyNotProbed`.
8. Do not edit `client/tests/floor/floor.json`, `client/docs/task-board.md`, or anything under `client/docs/`, `.claude/`, `.spine/`, `.pi/`, or `ConditioningControlPanel/`.
9. Do not close, edit, or claim the SP-067 board row. A packet that "helpfully" closes a neighbouring row has changed a mechanism nobody reviewed.
10. Do not add a wall-clock wait. `client/tests/CcpClient.Tests/TestWait.cs` is the only approved helper; `Thread.Sleep`, bare `Task.Delay`, and `DateTime`/`Environment.TickCount64` polls fail the timing guard mechanically — `TestTimingGuardTests` will red your run. If you reuse the `Task.Delay(Timeout.Infinite, token)` stand-in from `:121`, carry its `// wallclock-allow:` justification.
11. Do not export `CCP_DATA_ROOT` process-wide.
12. Do not leave a TODO, a placeholder, or a partially wired mechanism.

## Git Commit Convention

Conventional commits, `feat(SP-084): ...`. One coherent slice, no unrelated files. Leave the tree buildable at every commit. Commit your own work on your branch; do not merge, do not land, and do not touch the shared pin.

## Documentation Requirements

`runtime-capability-contract.md` §3 rule 3 currently parenthesises the cancelled case as "(startup cancelled or teardown raced the probe)". After this change a third arrival is covered: a probe that returned **normally** under a cancelled token. If your branch makes that wording incomplete, say so in `record.md` and quote the exact replacement wording you believe is owed, plus any sentence owed to §4's honesty rule.

**Do not edit the contract document yourself** — policy-touching text is applied by the orchestrator at land (SP-059 precedent; SP-071, SP-072 and SP-073 all followed it).
