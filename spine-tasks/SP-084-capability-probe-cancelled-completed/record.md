# SP-084 record — a cancelled capability probe can no longer be recorded as a completed probe verdict

Packet: `spine-tasks/SP-084-capability-probe-cancelled-completed/PROMPT.md`. Review level 3.
Lane branch `lane/SP-084-capability-probe-cancelled-completed`, based on `feat/crossplatform` at `cf9f7143`.

One behavioural product line changed (a three-line typed ternary replacing one unconditional return) plus the comment that explains it, three facts added, no shared file touched.

---

## 1. Census

### 1.1 The defect site, re-verified in this tree

`client/src/CcpClient.Desktop/Capabilities/CapabilityRegistry.cs:100-104` before the change:

```csharp
var outcome = await owner.RunAsync($"probe:{name}", async token =>
{
    probed = await probe(token).ConfigureAwait(false);
    return OperationOutcome.Completed.Instance;   // :103 — unconditional
}).ConfigureAwait(false);
```

The consuming switch at `:106-120` is unchanged by this packet and already had the honest destination wired:
`:108-111` `Completed` → `registry.Apply(name, probed ?? Faulted(ProbeFault, "probe completed without producing a state"))`;
`:112-116` `Failed` → `Faulted`; `:117-119` `Cancelled` → **an empty arm with a comment**, so the capability stays not-probed.

`client/src/CcpClient.Desktop/Lifecycle/OperationRegistry.cs:216-245` is the body wrapper: `:221` `outcome = await body(token)`, `:223` `catch (OperationCanceledException) when (token.IsCancellationRequested)` → `Cancelled`. A probe that returns normally never reaches `:223`. Every line the packet cited was opened and confirmed; none had moved.

### 1.2 The two tokens

- `CapabilityRegistry.cs:89` `RunAllAsync(CancellationToken cancellationToken)` — the **startup** token, read only at `:94-97` between probes.
- `OperationRegistry.cs:200-212` — `RunAsync` captures `_generationCts.Token` under the owner's lock at `:211` and hands **that** to the body. No relationship to `RunAllAsync`'s parameter.
- `OperationRegistry.cs:161-167` `Cancel()` cancels `_generationCts` only; `CancelAndDrainAsync` `:79-82` calls exactly that per owner. **Nothing in the product cancels the startup token during teardown.**

A consequence verified here that the packet does not state: `RunAllAsync` never re-checks the **generation** token between probes (`:94-97` reads only the startup token). After `owner.Cancel()` lands mid-sweep the loop keeps going, and every remaining probe starts under an already-cancelled generation token.

### 1.3 Shipped-probe census — and why it changes the reachability story

| Capability | Registration | Observes the token? |
|---|---|---|
| `ai.provider.local-ollama` | `Ai/AiOperationPipeline.cs:95` → `Ai/LoopbackOllamaProvider.cs` `ProbeCoreAsync` | **Yes.** `:225-228` `catch (OCE) when (cancellationToken.IsCancellationRequested) { throw; }` — rethrows ("the probe runner types this honestly"). The separate `catch (OCE)` at `:229-233` converts only the *linked* `CancelAfter(ProbeTimeout)` into `Unavailable(host-unreachable)`. |
| `ai.provider.cloud` | `Ai/AiOperationPipeline.cs:114` `_ => Task.FromResult(Unavailable(...))` | No |
| `display-session` | `Lifecycle/CompositionRoot.cs:252-253` `_ => Task.FromResult(...)` | No |
| `atomic-filesystem` | `Lifecycle/CompositionRoot.cs:254-255` `token => Task.Run(..., token)` | Only before the delegate starts; the body never reads it |
| `dtrh-webview-embedded`, `dtrh-web-dialog` | `Lifecycle/CompositionRoot.cs:259-262` `_ => Task.FromResult(...)` | No |
| `chaos-tunnel-webview-embedded` | `Lifecycle/CompositionRoot.cs:268-269` `_ => Task.FromResult(...)` | No |

Two findings:

1. **No shipped probe swallows an OperationCanceledException.** The only one that catches OCE rethrows under external cancellation. The headline shape this row names is therefore a *reachable door*, not an observed incident. See §5.
2. **Six of seven shipped probes never read the token at all.** Combined with §1.2 (no generation re-check between probes), `:103` was reachable in the shipped app by a second route needing no misbehaving probe: teardown cancels the owner while probe *k* is in flight; probes *k+1…n* still run, return `Task.FromResult(<state>)`, and were recorded as verdicts. That is the more production-relevant arrival, and it is bound as fact 2.

### 1.4 Who else reads the outcome being re-typed

`LastOutcome` is produced only at `OperationRegistry.cs:141` and consumed only by `AsyncLifecycleTests.cs` and `AiOperationContractTests.cs` — **no product consumer, and no assertion on a probe owner**. Of the `RunAllAsync` call sites in the suite, every one passes `CancellationToken.None` except `CapabilityTests.cs:101`, which passes `new CancellationToken(canceled: true)` and returns at `:94-97` before any probe runs. Only `CapabilityTests.cs:130` cancels a probe owner, and it is the OCE path. The change could not flip an existing assertion elsewhere, and did not.

---

## 2. Step 1 — the pre-fix red, from the unmodified tree

Facts 1 and 2 were written and run **before any product edit**. Build 0W/0E, then `dotnet test --filter "FullyQualifiedName~CapabilityTests"`:

```
[xUnit.net 00:00:02.50]     CcpClient.Tests.CapabilityTests.TeardownMidSweep_RemainingProbeRecordsNoVerdict_ThoughItNeverObservesTheToken [FAIL]
[xUnit.net 00:00:02.51]     CcpClient.Tests.CapabilityTests.ProbeSwallowsCancellation_ReturnsStateAnyway_NoVerdictIsRecorded [FAIL]

  Failed CcpClient.Tests.CapabilityTests.TeardownMidSweep_RemainingProbeRecordsNoVerdict_ThoughItNeverObservesTheToken [5 ms]
  Error Message:
   Assert.IsType() Failure: Value is not the exact type
Expected: typeof(CcpClient.Desktop.Capabilities.CapabilityState+Unavailable)
Actual:   typeof(CcpClient.Desktop.Capabilities.CapabilityState+Available)
  Stack Trace:
     at CcpClient.Tests.CapabilityTests.TeardownMidSweep_RemainingProbeRecordsNoVerdict_ThoughItNeverObservesTheToken() in ...\CapabilityTests.cs:line 205

  Failed CcpClient.Tests.CapabilityTests.ProbeSwallowsCancellation_ReturnsStateAnyway_NoVerdictIsRecorded [2 ms]
  Error Message:
   Assert.IsType() Failure: Value is not the exact type
Expected: typeof(CcpClient.Desktop.Capabilities.CapabilityState+Unavailable)
Actual:   typeof(CcpClient.Desktop.Capabilities.CapabilityState+Available)
  Stack Trace:
     at CcpClient.Tests.CapabilityTests.ProbeSwallowsCancellation_ReturnsStateAnyway_NoVerdictIsRecorded() in ...\CapabilityTests.cs:line 170

Failed!  - Failed:     2, Passed:    30, Skipped:     1, Total:    33, Duration: 585 ms
```

This is a genuine pre-fix red, not a revert-red produced after the fix existed. `ProbeCancelledMidFlight_StaysNotProbed_NeverAvailable` was among the 30 green in that same run.

---

## 3. Step 2 — the discriminating question, answered

> At the instant the body returns, can the registry distinguish (i) a probe that caught its cancellation and fabricated a state from (ii) a probe that genuinely finished its work in the window just before the token was cancelled?

**Answer: no. They are indistinguishable. → Branch A.** Four reasons read out of this tree:

1. **The body's total input set is two values.** `OperationRegistry.cs:216-221` invokes `body(token)`; the closure sees only `token` and the probe's return value. `OperationEntry` (`:251-260`) is `internal` and never passed into the body.
2. **`CancellationToken` exposes no time.** `IsCancellationRequested` is a monotone level flag — "cancelled now", never "cancelled before/after your state was produced".
3. **`CapabilityState` carries no provenance.** `CapabilityState.cs:54-75` — six payload-only records, no timestamp, no observed-cancellation flag. All eleven codes in `CapabilityReasonCodes` (`:4-44`) were read: there is **no cancellation code**, and Do NOT 4 forbids adding one.
4. **A timestamp would not be sound anyway.** The probe is an arbitrary caller-supplied delegate (`CapabilityRegistry.cs:19`); the runner cannot instrument when it decided. `Cancel()` runs on a foreign thread. No happens-before edge is observable.

Branch B is therefore inadmissible: no distinguishing fact exists, and classifying blind as `Faulted` would write *sticky recorded state* over a probe that may have done nothing wrong — the adjacent mistake Do NOT 3 names ("same lie, third door").

**The cost, stated plainly:** a probe that genuinely completed just before teardown cancelled the owner now has its real verdict discarded and stays not-probed. That is a **conservatism loss, not an honesty loss**. `runtime-capability-contract.md` §3 rule 3 already prices it, and §3 rule 5 defers re-probing entirely, so losing a true verdict during teardown costs nothing downstream.

**Advisory gate.** The packet's Step 2 gate was taken at the plan round with this answer and "Branch A" attached; the plan was approved in round 1 with the branch verified independently against source ("Indistinguishable at the instant of return -> Branch A, as pre-authorized"). No new decision arose during execution, so no second consult was opened.

---

## 4. The change

`client/src/CcpClient.Desktop/Capabilities/CapabilityRegistry.cs:100-117`, inside the lambda — the only place that can see the generation token:

```csharp
var outcome = await owner.RunAsync($"probe:{name}", async token =>
{
    probed = await probe(token).ConfigureAwait(false);
    // ... a probe that returns a state under a cancelled generation token ... must not be
    // recorded as a probe verdict that never happened ...
    return token.IsCancellationRequested
        ? OperationOutcome.Cancelled.Instance
        : OperationOutcome.Completed.Instance;
}).ConfigureAwait(false);
```

`probed` keeps its assignment and is simply not consulted on the `Cancelled` path. The switch, `CapabilityState.cs`, `OperationRegistry.cs` and `Participants.cs` are untouched.

**The comment enumerates three routes, not two.** The elided comment (`:103-113` in the tree) names every internal route to this one arrival — the probe swallowed its `OperationCanceledException`, it never observed the token at all, or it observed the token and returned a state anyway — and states out loud that the three are indistinguishable at this site, which reads only "a state was produced" and "the token is now cancelled" and never how the probe decided. That list, §5.1's disposition of Step 4's sibling pin, and §8's proposed contract wording are deliberately the same list; a two-item version of it would read as an exhaustive disjunction that excludes the third route.

**Why nowhere else works.** `cancellationToken` (startup) *is* in closure scope and would compile and fix nothing, because teardown cancels only `_generationCts`; both new facts cancel via `owner.Cancel()`, so that wrong fix stays red. Everything after `await owner.RunAsync(...)`, including the switch at `:119`, is outside the lambda and has no access to the generation token.

**Idiom reused, not invented.** The token-typed ternary is SP-067's shape, cited to async-lifecycle-fault-contract §2, and already appears at `Lifecycle/Participants.cs:110-112`, `StatusTickerParticipant.cs:150-152` and `Program.cs:302-304`. No new helper, type, abstraction, reason code, or fixture class was added.

**Concurrency.** The three lines execute on the `Task.Run` thread created at `OperationRegistry.cs:216`, holding **no lock**: `CapabilityRegistry.Probes()` releases `_gate` before the sweep's `foreach`; `RunAsync` releases `AsyncOperationOwner._gate` at `:212` before `Task.Run`; `Track` releases `OperationRegistry._gate` before the body starts. The read is `IsCancellationRequested` on the token captured under the owner's lock at `:211`; CTS flag reads are thread-safe, and reading the flag on a token whose source was later disposed by `Begin()` cannot throw (only `Token`/`Register`/`WaitHandle` do, and the token was captured before). `OperationRegistry.cs:223` already evaluates this exact captured token from this exact thread, so the precedent is in-tree.

---

## 5. Step 4 — the revert matrix, executed for real

One mechanism source change, so one revert. `:103` was restored to the unconditional `return OperationOutcome.Completed.Instance;` with the tree otherwise untouched, rebuilt, and the whole unit suite run. The file was then restored and **verified byte-identical by hash**:

```
before revert: 4a39a07d206854a50f63f76a06d0f4010b9387ed617e6632ef21d507bc784f5e
after restore: 4a39a07d206854a50f63f76a06d0f4010b9387ed617e6632ef21d507bc784f5e
```

**Reverted run:** `Failed: 2, Passed: 1027, Skipped: 2, Total: 1031`.
**Fixed run:** `Failed: 0, Passed: 1029, Skipped: 2, Total: 1031`.

**Red count under the single revert: 2.**

| Test | Fixed | Reverted `:103` |
|---|---|---|
| `ProbeSwallowsCancellation_ReturnsStateAnyway_NoVerdictIsRecorded` (fact 1) | green | **RED** |
| `TeardownMidSweep_RemainingProbeRecordsNoVerdict_ThoughItNeverObservesTheToken` (fact 2) | green | **RED** |
| `ProbeCompletesWhileOwnerStaysLive_ItsVerdictIsRecorded` (fact 3, negative control) | green | **green** |
| `ProbeCancelledMidFlight_StaysNotProbed_NeverAvailable` | green | green |
| `StartupCancelled_LeavesRemainingProbesHonestlyNotProbed` | green | green |
| `EveryState_ReachableViaProbe` (6 rows), `ProbeThrows_…`, `ProbeReturnsNoState_…` | green | green |
| `RealCompositionRoot_CapabilityProbesPhase_PopulatesStatesViaRealProbes` | green | green |
| every other test in both projects | green | green |

The negative control is a real control, not a duplicate: it stays green in both trees, which is what proves the `Completed` arm is still **reachable** and that the ternary does not fire when the owner was never cancelled. It would red if the ternary were inverted or always-`Cancelled`.

**Why each fact bites.** Fact 1: the swallowed OCE never reaches `OperationRegistry.cs:223`, so pre-fix `:103` returned `Completed` and `:108-111` applied `Available("fabricated after the token was cancelled")`. Fact 2: `"first"` takes the already-correct OCE path, but `"second"` starts under the already-cancelled generation token (`Cancel()` does not null `_generationCts`, so `RunAsync` still hands out that token), returns immediately with no exception ever constructed, and pre-fix was recorded as `Available`. Both are deterministic, not timing-dependent: `never` is never completed, so the parked probe can only leave its wait via cancellation, and iteration 2 is causally after `owner.Cancel()` because `Cancel` is what completed iteration 1.

**Vacuity bar.** All three facts assert the **recorded state** via `registry.GetState(name)`. None asserts on the `OperationOutcome` object, which would pass through the same door the defect used.

### 5.1 Step 4's sibling pin — considered, and deliberately NOT added

Step 4 requires a disposition either way of "a probe that never throws at all, checks `token.IsCancellationRequested` itself and returns a state early". **Decision: redundant. Not added, and the reason is stated here rather than omitted.**

The reason is §3's indistinguishability argument applied to the fixture instead of to the product. The observation site (`CapabilityRegistry.cs:114`) reads exactly two values: *a state was produced* (no exception escaped the probe) and *the token is now cancelled*. It never sees **how** the probe decided to return. So the sibling can only be built with one of two arrival timings, and each collapses onto a fact that already exists:

- **Sibling starts after `Cancel()`** — it reads the already-cancelled token and returns early. At the site this is the arrival fact 2's `"second"` probe already produces (`CapabilityTests.cs:189-190`, `Task.FromResult` under an already-cancelled token): same two observable values, same path, same recorded state.
- **Sibling starts before `Cancel()`, parks, then reads the flag and returns early** — at the site this is fact 1's arrival (`CapabilityTests.cs:147-160`): in flight when the token was cancelled, returns a state with no exception escaping. Fact 1's probe reaches that state by swallowing an `OperationCanceledException` and the sibling by an explicit flag read; that difference is invisible at the site by construction.

I checked this against a mutant rather than only by argument. The one mutant that *does* separate facts 1 and 2 — moving the check to **before** `await probe(token)`, which reds fact 1 and leaves fact 2 green — still moves the sibling in lockstep: sibling-after-`Cancel()` goes green with fact 2, sibling-parked goes red with fact 1. There is no mutation of the changed line, and none of its plausible mis-placements, under which a sibling pin reds while both existing pins stay green. By Step 4's own standard for the negative control, a pin that cannot be separated from an existing pin "is a duplicate wearing a different name".

What the sibling would genuinely have added is documentary: a third named internal route to the same observable arrival. That is bought instead, at no suite cost and with no lockstep duplicate to rot, by naming all three routes in the product comment (`CapabilityRegistry.cs:103-113`, §4) and in §8's proposed contract wording.

**What this concession is conditional on.** If a future change made the three routes distinguishable at the site — a provenance field on `CapabilityState`, which Do NOT 4 currently bars — the sibling would stop being a duplicate and would deserve its own pin. This is a property of the current shape of the observation, not a permanent one.

---

## 6. Verification

Build immediately before each gate; both run through the slot semaphore as separate commands.

```
node client/tools/gate/with-slot.mjs --slots 3 -- dotnet build client/CcpClient.sln -c Debug --nologo
node client/tools/gate/with-slot.mjs --slots 3 -- node client/tests/floor/check-floor.mjs
```

- Build: **0 Warning(s), 0 Error(s)**.
- Baseline before any edit: `FLOOR OK: CcpClient.Tests: 1028/1028, 2 skipped; CcpClient.HeadlessTests: 35/35, 0 skipped`.
- Final: `CcpClient.Tests` observed **1031**, `CcpClient.HeadlessTests` observed **35**, **0 failed**.

Pin read from `client/tests/floor/floor.json`: `CcpClient.Tests` **1028**, `CcpClient.HeadlessTests` **35**.
Declared delta (`floor-delta.json`): **+3 unit, +0 headless**.
`1028 + 3 = 1031` ✓ and `35 + 0 = 35` ✓.

The gate therefore exits non-zero with `FLOOR VIOLATION — total drift: 1031 result(s) (pin total 1028)`. **That is the designed state for a bound packet**, not a failure: the shared pin is bumped once at land from the summed deltas. `client/tests/floor/floor.json` was not edited.

**Re-run after the final-review revision** (which touched the `CapabilityRegistry.cs` comment, so a rebuild was owed): build **0 Warning(s), 0 Error(s)**; `CcpClient.Tests` `Failed: 0, Passed: 1029, Skipped: 2, Total: 1031`; `CcpClient.HeadlessTests` `total=35, passed=35, failed=0, notExecuted=0`. Pin 1028 / 35, declared delta +3 / +0, observed 1031 / 35 — unchanged, because the revision added and removed no fact.

The 2 skips are the two OS-gated Linux rows already in `allowedSkips` (`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`, `ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`). `CCP_DATA_ROOT` was never exported, and the SP-057 pin did not skip.

---

## 7. Honesty — what is NOT proven

- **No shipped probe has been shown to swallow cancellation.** This closes a **reachable door**, not an observed production incident. The only shipped probe that catches an `OperationCanceledException` (`LoopbackOllamaProvider.ProbeCoreAsync`) rethrows under external cancellation. Do not read this row as a bug users hit.
- **What makes the door reachable is the second arrival, not the first.** Six of the seven shipped probes never read the token at all (§1.3), and the sweep does not re-check the generation token between probes, so a teardown landing mid-sweep would have had every remaining probe's state recorded as a verdict. Fact 2 binds that shape. It is still a *reachable* path, not an observed one.
- **I did not prove that teardown provably overlaps the `CapabilityProbes` phase in the shipped app.** I did not attempt that measurement, and nothing here should be read as claiming it. The startup sweep is fast and teardown is user-initiated; the overlap window is real but unquantified.
- **The negative control proves reachability of the `Completed` arm, not correctness of any real probe's verdict.** It uses a synthetic probe.
- **The conservatism loss is real and unmeasured.** A probe that genuinely completed in the instant before `Cancel()` now has a true verdict discarded (§3). No fact distinguishes that case, because no such fact exists; that is precisely why Branch A was taken.
- **Only the normal-return door is closed; the taxonomy of this lie is not fully shut.** A probe whose cancellation surfaces as a **non-`OperationCanceledException`** exception is still recorded as a sticky `Faulted` verdict for a probe that was merely stopped. That is the same epistemic situation Branch A resolves the other way at the return, and it is resolved the opposite way in the `Failed` arm. It is out of File Scope by two independent bars and is filed unfixed at §9.4; nothing here should be read as claiming otherwise.
- **This is a headless unit-level change.** No headed or presentation-verified claim is made or needed: the mechanism is pure lifecycle logic in `CcpClient.Tests`.

---

## 8. Owed documentation (quoted, NOT applied — orchestrator applies at land)

Per Do NOT 5 and the SP-059/071/072/073 precedent, `client/docs/**` was not edited.

`runtime-capability-contract.md` §3 rule 3 currently parenthesises the cancelled case as "(startup cancelled or teardown raced the probe)". After this change a third arrival is covered — the probe **returned normally** under a cancelled token — and it is reached by three internal routes that are indistinguishable at the observation site, so the wording must not enumerate only two of them. Proposed replacement:

> `OperationOutcome.Cancelled` (startup cancelled, teardown raced the probe, **or the probe returned a state while its generation token was already cancelled — whether it swallowed the cancellation, never observed the token, or observed the token and returned a state anyway**) → the capability stays `Unavailable(not-probed)`. A state produced under a cancelled token is never recorded as a verdict: the runner cannot distinguish a fabricated answer from a genuinely-finished one, and honest absence is preferred to a recorded verdict that may never have happened.

The three routes in that tail are exhaustive **for the normal-return door only**, and they are the observation site's own blind spot stated out loud (§4, §5.1). A probe whose cancellation escapes as a **non-`OperationCanceledException`** exception does not arrive through this door at all: it is `Failed`, is still recorded as a fault, and is filed unfixed at §9.4. The proposed wording deliberately does not claim to cover it.

Sentence owed to §4's honesty rule:

> A recorded verdict for a probe that was stopped is a fake probe result, and is a contract violation by the same rule that bans faking availability.

---

## 9. Out of File Scope — filed, not fixed

1. **`RunAllAsync` does not re-check the generation token between probes** (`CapabilityRegistry.cs:92-97`). After this fix the remaining probes are recorded honestly but still *run* after teardown began — real I/O (`AtomicFileSystemProbe`) and a loopback GET (`LoopbackOllamaProvider`). A one-line guard would stop that work, but it is a second behaviour change and would **mask fact 2**, so it was deliberately not made. Owns a row.
2. **`LoopbackOllamaProvider.ProbeCoreAsync`** converts its linked probe-timeout into `Unavailable(host-unreachable)`; if external cancellation lands immediately after, that verdict is now discarded as not-probed — Branch A's priced conservatism. Correct as-is; the probe's timeout semantics under teardown deserve a documented sentence someday.
3. **`TestTimingGuardTests.cs` pin fragility.** `:54-78` pins wall-clock sites as (path, exact code, exact count) and fails on any mismatch *including a grown count*. `:67` pins `CapabilityTests.cs` → `"await Task.Delay(Timeout.Infinite, token); // observes cancellation"` at count **1**. Reusing that shape for the new facts would have required raising the pin to 2, i.e. editing a file outside File Scope. The design avoids it entirely (§10, S1/§7 below). Filed as a trap the next packet in this file will also hit.
4. **A probe whose cancellation surfaces as a NON-OCE exception is still recorded as a fault.** This fix closes the normal-return door only. If a probe converts its cancellation into any other exception type, that exception misses `OperationRegistry.cs:223` (`catch (OperationCanceledException) when (token.IsCancellationRequested)`), lands in the general `catch (Exception ex)` at `:227`, becomes `OperationOutcome.Failed` at `:240`, and reaches `CapabilityRegistry.cs:125-129` → `Apply(name, Faulted(ProbeFault, "<Type>: <reason>"))`. That is **sticky recorded state for a probe that was merely stopped** — the packet's own Do NOT 3 words, "a fabricated fault for a probe that was merely stopped. Same lie, third door". The epistemic situation is identical to the one Branch A resolves the other way at `:114`: the runner cannot tell stopped from genuinely-failed, and everywhere else in this fix it prefers honest absence. §3, §5 and §7 of the first draft of this record were silent about that asymmetry; this row and the §7 bullet fix that.

   **Not fixable inside this File Scope, and not attempted.** The `Failed` arm is in the `switch` at `:119-133`, outside the lambda, with **no access to the generation token** (packet Step 3 point 2). The only place that could re-type it is `OperationRegistry.RunAsync`, which Do NOT 2 forbids by name and which would silently re-type the terminal outcome of every owned operation in the product.

   **Reachability bound, stated small rather than inflated.** All seven shipped registrations were re-checked for this row: **none can throw a non-OCE as a consequence of cancellation today.** The six `Task.FromResult` / `Task.Run` probes (`CompositionRoot.cs:252-269`, `AiOperationPipeline.cs:114-115`) do not convert cancellation into an exception at all — the one nuance is `atomic-filesystem` (`CompositionRoot.cs:254-255`), where `Task.Run(..., token)` under an already-cancelled token yields a `TaskCanceledException`, which *is* an `OperationCanceledException` and so takes the honest `:223` path. `LoopbackOllamaProvider.ProbeCoreAsync` rethrows OCE under external cancellation (`:225-228`), and its `catch (HttpRequestException)` at `:234-238` **returns** a state rather than throwing, so that path now walks the `:114` ternary and is typed `Cancelled`. This is therefore the same class as the headline (§7): a **reachable door in the shape, not an observed incident**. Materiality is shape-level. Owns a row.

---

## 10. Discharge of the approved plan's carried conditions and the reviewer's non-blocking suggestions

**S1 — promote the vacuous-shape constraint from stylistic to hard.** Discharged, and it was load-bearing. `VacuousShapeDetector.IsGuardingBrace` treats `if`/`foreach`/`for`/`while`/`switch` **and lambda `=> {` bodies** as guarding; `try`/`catch`/`finally`/`using`/`lock` are transparent. Applied to all three facts: every `Assert.*` sits at method-body depth 0. Fact 2's `foreach` is preceded by `Assert.NotEmpty(registry.Names)` at depth 0, so `assertions.All(a => a.Depth > 0)` is false and the method is **not** classified `assertions-all-nested` — which would have required a new entry in `client/tests/floor/vacuous-shape-ledger.json`, a file outside File Scope, and `VacuousShapeGuardTests` would have redded the run. Also verified across the three new facts: no bare `return;` before the first assert (`return new CapabilityState...;` does not match the detector's `\breturn\s*;`), no `File.Exists`/`Directory.Exists`, no `Environment.GetEnvironmentVariable`, no `OperatingSystem.Is*`/`RuntimeInformation.IsOSPlatform`, no `Assert.Skip*`, and three distinct method names so no duplicate ledger key. The existing `StartupCancelled_…` ledger entry (`expectDetected: false`, verdict "fixed" — a resurrection guard) was not touched and its `Assert.NotEmpty` was not weakened. Confirmed empirically: the full suite is green with no ledger edit.

**S2 — the "every call site passes `CancellationToken.None`" claim was false as written.** Corrected in §1.4 of this record: `CapabilityTests.cs:101` passes `new CancellationToken(canceled: true)`. The conclusion is unchanged — that call returns at `:94-97` before any probe runs, so it cannot be flipped by this change — but the record now states it accurately rather than repeating the plan's overreach.

**S3 — make `RunContinuationsAsynchronously` explicit in fact 2.** Discharged: fact 2's `started` is constructed as `new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)`, identical to fact 1, so the record does not have to re-argue the inline-continuation ordering. Both orderings were walked at plan time and both land in the same state; with the flag set, the probe is parked in its token-observed wait before `Cancel()` in the normal ordering.

**S4 — file paths.** Discharged: this record cites `client/src/CcpClient.Desktop/Ai/AiOperationPipeline.cs` and `.../Ai/LoopbackOllamaProvider.cs` (directory `Ai/`, **not** `Features/Ai/`), verified by listing the directory.

**S5 — `--slots 3` is `with-slot.mjs`'s own default** (`$CCP_GATE_SLOTS`, else 3). Noted; passed explicitly anyway, which is harmless and matches the operational instruction verbatim.

**No `## Carried conditions` section existed in the approved plan**, so there are none beyond the five suggestions above.

### 10.1 Final-review round (REVISE at `58a6191b`) — discharge

Both blocking items were confined to this record; the mechanism, the branch decision, the revert matrix and the gate arithmetic were re-verified by the reviewer and stand unchanged.

**B1 — Step 4's sibling-shape disposition was silently omitted.** Verified against the packet and against the pre-revision record: Step 4 is binary ("If you decide it is redundant, say so with the reason rather than silently omitting it") and neither branch had been taken. Discharged at **§5.1** (redundant, with the reason and with the mutant check that establishes lockstep), and the incomplete two-route framing that the omission had propagated into is corrected in the two places the packet calls load-bearing: the product comment (`CapabilityRegistry.cs:103-113`, §4) and §8's rule-3 replacement wording, which the orchestrator applies verbatim. The comment fix is a comment-only source edit; the build and the floor gate were re-run after it (§6).

**B2 — the residual in the packet's own taxonomy of the lie was unfiled.** Verified against source, not accepted on assertion: `OperationRegistry.cs:227` traps any non-OCE, `:240` builds `OperationOutcome.Failed`, and `CapabilityRegistry.cs:125-129` applies `Faulted(ProbeFault, …)` — sticky recorded state for a probe that may merely have been stopped, which is the opposite disposition to the one Branch A takes at `:114` for the identical epistemic situation. Filed unfixed at **§9.4** with both bars (no generation token in the switch; Do NOT 2 forbids `RunAsync`) and with the reachability bound checked across all seven shipped registrations, plus a §7 honesty bullet. Not fixed, per the reviewer's instruction and per File Scope.

**Non-blocking items.** Taken: the `CapabilityRegistry.cs:103-113` comment alignment (cheap, in File Scope, and it is one of the two places carrying the two-route framing). Declined, with reasons: adding `TaskCreationOptions.RunContinuationsAsynchronously` to fact 3's `release` TCS — the reviewer walked the inline-continuation ordering and called the fact correct as written, and editing a green fact during a final-review round buys a reader's convenience at the price of re-verifying a passing pin; the §9.1 board row and §8's contract paragraphs — both are the orchestrator's to write at land (Do NOT 5, SP-059/071/072/073 precedent), and the lane may not touch `client/docs/task-board.md` or `client/docs/**`.

**Floor delta after the revision: unchanged at +3 unit / +0 headless.** No fact was added or removed; `floor-delta.json` needed no edit.

---

## 11. Completion criteria

| Criterion | Status |
|---|---|
| Pre-fix red captured with its real message, from the unmodified tree | §2 |
| Step 2's discriminating question answered from the code; selected branch implemented | §3, §4 — Branch A |
| The observation reads the owner's generation token; new tests cancel via `owner.Cancel()` | §4, facts 1 and 2 |
| `Completed` arm still reachable, proven by a negative control | fact 3, green in both trees |
| Every new fact bites under the independent revert; control and mid-flight test green in both | §5, red count 2 |
| Step 4's sibling shape dispositioned with its reason, not silently omitted | §5.1 — redundant, not added |
| The residual arrival this fix does NOT close is filed rather than left implied | §7 bullet 5, §9.4 |
| `record.md` and `floor-delta.json` exist and are accurate | this file, +3/+0 |
| Build 0W/0E | §6 |
| SP-067's board row and its 12 dispositioned sites untouched | not opened, not edited |
| `floor.json`, `task-board.md`, `client/docs/**`, `ConditioningControlPanel/**`, `.spine/**`, `.pi/**`, `.claude/**` unchanged | §6, and the commit's diff |

Files changed by this packet: `client/src/CcpClient.Desktop/Capabilities/CapabilityRegistry.cs`, `client/tests/CcpClient.Tests/CapabilityTests.cs`, `spine-tasks/SP-084-capability-probe-cancelled-completed/record.md`, `spine-tasks/SP-084-capability-probe-cancelled-completed/floor-delta.json`.
