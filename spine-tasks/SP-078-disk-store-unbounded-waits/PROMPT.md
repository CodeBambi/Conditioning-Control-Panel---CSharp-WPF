# SP-078 — Correct the "five unbounded disk-store waits" board row, and pin the fact that makes it correctable

## Mission

The board row you were filed against (`client/docs/task-board.md`, the P2 row reading "Five unbounded in-process disk-store waits sit on UI-reachable paths") **rests on a false premise, and the orchestrator verified that before authoring this packet.** Six of the seven sites the row names do not wait on anything: `PersistenceStore.StartAsync` and `StopAsync` complete synchronously and return `Task.CompletedTask`, so the `.GetAwaiter().GetResult()` at those call sites resolves an already-completed task. The one site that genuinely waits without a bound (`AiMemoryStore.cs:272`) is deliberately **not** on the UI thread: `CompanionViewModel.cs:312` wraps it in `Task.Run` with a comment naming the consult that decided it.

The intersection the row asserts, "unbounded wait AND UI thread", is **empty**. Not five. Zero.

There is a real defect underneath, and it is a different one: `PersistenceStore.Load()` performs **blocking synchronous disk I/O on whatever thread calls `StartAsync`**, and five of those callers are UI-thread paths. That is a thread-affinity defect, not a wait, and no timeout can bound it because the I/O has already finished by the time the caller holds a task.

**Your single outcome is to correct the row's premise, and to correct it with an executed fact rather than an assertion in `record.md`.** The count must become a thing the suite observes, not a thing a record claims, which is the same standard SP-073 was held to. You do not implement the row's remedy. There is no wait to bound.

## Dependencies

SP-071 (landed) produced the census this row was filed from. Read what it actually said (below): the census wrote "starts", the board row rewrote it as "waits", and that single word is where the premise broke. SP-073 (teardown residue) and SP-072 (orphan disposal) are adjacent and out of scope; do not touch their rows.

## Context to Read First

Verified by the orchestrator at authoring. Every line below was opened in the **port tree** and confirmed, not transcribed from the board.

**The class the row blames:**

- `client/src/CcpClient.Desktop/Persistence/PersistenceStore.cs:158-170` — `StartAsync`. It calls `Load()` **inline** and then `return Task.CompletedTask;`. There is no `async`, no `await`, no `Task.Run`. The blocking work is complete before the caller ever sees a task.
- `:173-183` — `StopAsync`. `Running = false; _owner.Cancel(); return Task.CompletedTask;`. No I/O at all.
- `:288-402` — `Load()`. Synchronous `File.Exists`, `File.Move`, `File.Delete`, `File.ReadAllText`, JSON parse, migrations, bind. This is the real blocking cost, and it runs on the caller's thread.
- `:230-252` — `Save()` / `SaveImmediate()`. These DO produce a real task: `_owner.RunAsync(...)`.
- `client/src/CcpClient.Desktop/Lifecycle/OperationRegistry.cs:161-167` — `AsyncOperationOwner.Cancel()` is a CTS cancel under a short lock. It cannot block.
- `:216-221` — `RunAsync` bodies run on `Task.Run` with `ConfigureAwait(false)`. No captured context.

**The seven call sites the row cites (it says "five"; five is the number of FILES, and one of them holds two sites while another is reached from two hosts):**

| # | Site | Awaits what | Caller thread | Real cost |
|---|---|---|---|---|
| 1 | `Features/Dtrh/DtrhHostWindow.axaml.cs:228` (`InitBarkPipeline`, reached from the `Opened` handler at `:113-124`) | `Task.CompletedTask` | UI | inline `Load()` disk I/O |
| 2 | `Persistence/AssetSelectionDocument.cs:61` (`AssetSelectionStore.Start`) | `Task.CompletedTask` | UI, from **two** hosts: `DtrhHostWindow.axaml.cs:190` (`Opened`) and `Features/Intake/IntakeHostContext.cs:109` | inline `Load()` disk I/O |
| 3 | `Features/Intake/IntakeHostContext.cs:84` | `Task.CompletedTask` | UI, via `IntakeLaunchCoordinator.cs:56` `Launch()` | inline `Load()` disk I/O |
| 4 | `Features/Intake/IntakeHostContext.cs:95` | `Task.CompletedTask` | UI, same path | inline `Load()` disk I/O |
| 5 | `Features/Dtrh/DtrhSaveSlots.cs:467` (`DeleteSlot`, from `DtrhSlotPickerWindow.axaml.cs:257` `ConfirmDelete`) | `Task.CompletedTask` | UI | **nothing.** `StopAsync` only cancels a CTS |
| 6 | `Features/Dtrh/DtrhSaveSlots.cs:469` | `Task.CompletedTask` | UI | inline `Load()` disk I/O |
| 7 | `Ai/AiMemoryStore.cs:272` (`Clear`) | a **real** threadpool task (the chained write tail) | **NOT UI** | genuinely unbounded, off the UI thread by design |

- `client/src/CcpClient.Desktop/Features/Companion/CompanionViewModel.cs:312-315` — the wrapper that takes site 7 off the UI thread, with the comment `// Clear() blocks on the store's write chain (SP-040 consult) — never on the UI thread.` This is a reviewed decision that already exists. Site 7 is not an open defect.
- `client/src/CcpClient.Desktop/Ai/AiMemoryStore.cs:246-258` — the `Clear()` doc comment, including why the sync block is deadlock-safe (it cites `OperationRegistry.cs:216-221`) and why the write must reach quiescence **before** the delete (a queued write would otherwise resurrect the file). Read this before you consider touching site 7. You will not be touching site 7.

**Timeout theater already shipped, which you must not extend:**

- `client/src/CcpClient.Desktop/Features/Dtrh/DtrhHostWindow.axaml.cs:259-260` and `client/src/CcpClient.Desktop/Features/Intake/IntakeHostContext.cs:128-130` already call `.Wait(TimeSpan.FromSeconds(2))` on `StopAsync()`, an operation proven above to be incapable of blocking. Five bounded waits on nothing, already in the tree. This is the exact shape a lane that believed the row would add seven more of.

**What SP-071 actually wrote, versus what the board says:**

- `spine-tasks/SP-071-teardown-off-ui-thread/record.md:50-53` — "unbounded in-process disk-store **starts** on UI-reachable paths", listing the seven line numbers, and noting site 7 is "UI-reachable via `CompanionViewModel.cs:315`, holds the store gate while waiting". The census was careful. It said "starts", it said "UI-reachable", and for site 7 it named the store gate rather than the UI thread. The board row's "each is a UI-thread block with no bound" is the fabricated half.

**The contract the row appeals to:**

- `client/docs/async-lifecycle-fault-contract.md:51` (§5 rule 1) — the post-only rule removes the class "teardown blocked on the UI thread **awaiting** an operation that awaits the UI thread". Six of seven sites await nothing, so this rule does not reach them.
- `:63` (§5 rule 6) — bounds waits on "any **native or backgrounded** work". `Load()` is neither: it is in-process, synchronous, on the caller's thread. The row's citation of §5 is misapplied in both directions.

**Test-side facts:**

- `client/tests/CcpClient.Tests/PersistenceStoreTests.cs` **does not exist.** You create it. Note that `client/tests/CcpClient.Tests/PersistenceTests.cs` already exists and holds class `PersistenceTests` with `private` helpers (`NewStore` at `:300-307`, `ListLogSink` at `:309`, `TempDir`). Those helpers are not visible to your new file and `PersistenceTests.cs` is **not** in your scope. Declare your own minimal helpers in your new file; duplicating a small `TempDir` per test file is the established pattern here (`AiMemoryStoreTests.cs:332`, `AiMemoryPipelineTests.cs:315`, `AiMemoryPromptAssemblyTests.cs:394`).
- `client/src/CcpClient.Desktop/Persistence/DemoSettings.cs:28` — `public sealed class DemoSettings`, the product-owned demo document. Use `PersistenceStore<DemoSettings>` so you need no new model type and no scope widening.
- `client/tests/floor/floor.json` — the shared pin. READ THE PIN FROM THE FILE, never from this packet: it has already gone stale twice (it said 1018; wave 30 made it 1022 and wave 31 made it 1028). Open `client/tests/floor/floor.json` and use what is there.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Persistence/PersistenceStore.cs`, `client/tests/CcpClient.Tests/PersistenceStoreTests.cs`, `spine-tasks/SP-078-disk-store-unbounded-waits/**` |
| Must not change | everything else, and specifically the files named in the contract below |

**Scope note, read before Step 3.** This scope was pre-assigned across the wave to be pairwise disjoint, and it is correct for the packet you are actually executing: the correction lands in `record.md`, and the fact that carries it lands in the two files above. It would **not** have been sufficient for the remedy the board row implies, because all seven cited call sites live in five other files, none of them yours. That mismatch is itself evidence and belongs in your `record.md`. Do not widen the scope to reach a call site. If you conclude the honest deliverable requires editing a caller, **stop and escalate**; do not edit it.

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-078-disk-store-unbounded-waits/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Persistence/PersistenceStore.cs`, `client/tests/CcpClient.Tests/PersistenceStoreTests.cs` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/docs/task-board.md`, `ConditioningControlPanel/**`, `client/docs/**`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-078-disk-store-unbounded-waits/record.md`, `spine-tasks/SP-078-disk-store-unbounded-waits/floor-delta.json` |

**You do not edit `client/tests/floor/floor.json`.** That file is the shared pin and concurrent lanes collide on it. Write your count change into `floor-delta.json` in your own folder instead:

```json
{ "packet": "SP-078-disk-store-unbounded-waits", "unit": 0, "headless": 0, "reason": "one line naming the facts you added" }
```

Declare `0`/`0` if you add no tests; omitting the file is not the same as declaring zero. The land sums every packet's delta and applies one bump. `client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs:160-242` enforces both halves and will fail your run if the row or the disclaimer is missing.

## Review Level: 3 (Plan, Code, Final)

Level 3, not 2, for three reasons. The paths under discussion are live and user-visible (DTRH host open, intake launch, save-slot delete, companion memory clear). The one genuine wait is a concurrency question (a chained writer, a store gate, and a privacy-bearing delete ordered against it). And a correction packet that gets the correction wrong is worse than no packet: it re-files a bad premise in a more authoritative place, or it licenses a future lane to "fix" something that is not broken.

## Steps

### Step 1: Re-run the census yourself. Do not inherit the table above.

Open all seven sites and the two `PersistenceStore` methods yourself and answer, per site, three separate questions that the board row collapsed into one:

1. **Does the call site wait on anything?** The test is whether the returned task can be incomplete when the caller receives it. Read `StartAsync`/`StopAsync` and answer from the code, not from the method name ending in `Async`.
2. **Is the caller on the UI thread?** Trace the enclosing method to its trigger.
3. **Is the work bounded?** For a synchronous inline call, "bounded" means bounded by the operation itself, not by a timeout.

Then state the size of the intersection {unwaited} is empty, {unbounded wait} AND {UI thread}. Report the real count of `.GetAwaiter().GetResult()` call sites, and say plainly whether "five" was files or sites.

### Step 2: Resolve the deliverable against the pre-authorized rule

**THE DECISION RULE IS PRE-AUTHORIZED BOTH WAYS. Resolve it on your evidence; do not ask.**

- **Branch A, if your census confirms the intersection is empty** (the orchestrator's finding): the deliverable is **the correction plus the fact that pins it**. No bound, no new mechanism, no caller edit. A defect that does not exist does not get a fix, and shipping one would be mechanism no path drives. Proceed to Step 3.
- **Branch B, if you find a genuine unbounded await reachable on a UI thread inside `PersistenceStore`**: land the bound **inside `PersistenceStore`**, degrade typed through the existing `LoadOutcome` vocabulary (never a new untyped null or a swallowed exception), never block the caller further, and name in `record.md` exactly which site you proved and how. Branch B is only available for a wait you can bound **inside your scope**. If the only honest fix is at a call site, that is the escalation case in the Scope note, not a licence to widen.

Take the pre-approach advisory gate at the end of this step with your census table attached and your branch named. Do not ask before you have the census.

### Step 3: Bind the correction to an executed fact

Under Branch A, the correction is a claim about `PersistenceStore`'s own behaviour, so it can and must be pinned in `PersistenceStoreTests.cs`. Pin at minimum:

- `StartAsync` returns an **already-completed** task: the task is complete, and `LastLoadOutcome` is already non-null, before the caller awaits or observes it. That is what makes every `.GetAwaiter().GetResult()` at the seven sites a no-op wait.
- `Load()` runs on the **calling thread**. Assert this from **inside** the operation, not from outside: an `ILogSink` fake (`CompositionRoot.cs:7-10`, one method, `void Log(string)`) records `Environment.CurrentManagedThreadId` when `Load()` logs, and the test compares it to the caller's. Drive a load path that logs, for example a stale temp beside a valid main (`PersistenceStore.cs:299-309`) or a quarantine (`:405-432`). An outside-the-operation sentinel is the vacuity class this project has hit repeatedly; assert from inside where the shape allows it, which is the SP-072 `disposeCountAtTeardownEnd` shape.
- `StopAsync` returns an already-completed task and performs no I/O.

**The testability constraint, named here at authoring rather than discovered in review.** You cannot assert "and that thread was the UI thread" from `CcpClient.Tests`. There is no Avalonia dispatcher in that project, and `[assembly: AvaloniaTestApplication]` is assembly-wide, so reaching one means the headless project, which is not in your scope and would be the wrong home regardless: the portable, transferable half of this fact is **thread-affinity** (whatever thread calls, that thread blocks), and thread-affinity is exactly what a plain unit test can prove. The other half, "and five of those callers are UI-thread paths", is established by reading the call sites and belongs in `record.md` as cited evidence, never as an assertion. Do not attempt to move this file into `CcpClient.HeadlessTests`, and do not weaken the fact into something a dispatcher-free test can only imply.

Also add, in `PersistenceStore.cs`, a doc note on `StartAsync` and `StopAsync` stating that they complete synchronously on the caller's thread and that `Load()`'s blocking disk I/O runs before the returned task exists, so a caller-side timeout on the returned task bounds nothing. This is the sentence whose absence let the census be misread. Keep it to the doc comment; change no behaviour under Branch A.

**Every new fact must be proven to bite by an INDEPENDENT revert**, one source at a time, restoring the tree byte-identically between reverts, with the red count recorded per revert. The revert for the synchrony pins is synthetic and you induce it: make `Load()` run under `Task.Run` inside `StartAsync`, or insert an `await Task.Yield()` before it, and confirm the corresponding pin goes red **and no other pin does**. That is the SP-067 precedent (inducing the defective shape at each source in turn). It is also the point of the pin: that induced shape is precisely the change that would convert all seven call sites into genuine sync-over-async blocks overnight, which is the latent hazard the row was groping at and got backwards.

### Step 4: Record, and propose the row's replacement wording

`record.md` carries:

- The census table, all three questions answered per site, with `File.cs:line` for every claim.
- The size of the intersection, and the correction stated flatly: what the row says, what is true, and the one word (`starts` becoming `waits`) where the drift entered between `SP-071/record.md:50-53` and the board.
- The revert matrix with red counts.
- **Proposed replacement wording for the board row**, written out in full so the orchestrator can apply it verbatim. It should keep the residual true defect (blocking synchronous disk I/O on the UI thread at the five UI-thread `Load()` sites), drop the false claims, and correct the count. Say whether you believe the replacement is P2 or lower given that a local disk read is bounded in practice.
- An honesty section naming what is NOT proven. At minimum: you have not measured how long `Load()` actually takes on a cold disk or a network-mapped data root, so "in practice bounded" remains an argument and not a fact; and you have not proven that site 7's off-UI-thread wrapper is the only caller of `AiMemoryStore.Clear()` for all future callers, only for today's tree.

`floor-delta.json` with your real counts. READ THE PIN FROM THE FILE, never from this packet: it has already gone stale twice (it said 1018; wave 30 made it 1022 and wave 31 made it 1028). Open `client/tests/floor/floor.json` and use what is there.

### Step 5: Verification

```
dotnet build client/CcpClient.sln -c Debug --nologo
```
```
node client/tests/floor/check-floor.mjs
```

Run them as **separate commands**. The worktree isolation guard refuses compound shell commands (`cd X && ...`), so chain nothing.

**Build immediately before the gate, every time.** The floor wrapper runs `--no-build`; a stale `bin/` once reported 1022 against a tree containing 1018, and the run read green while measuring a tree that no longer existed.

Your floor run will report a total that does **not** match the pin, because the pin is bumped at land from the summed deltas and not by you. That is expected and is not a failure of your work: confirm the observed total equals `pin + your declared delta`, and state both numbers in your report.

## Completion Criteria

- The census is complete, its three questions answered per site, and the branch it selects is stated with evidence.
- The intersection size is stated as a number.
- Under Branch A: the correction is pinned by facts in `PersistenceStoreTests.cs`, and `PersistenceStore.cs` carries the doc note. Under Branch B: the bound exists inside `PersistenceStore`, degrades typed, and its site is named.
- Every new fact bites under its own independent revert, and no other pin moves under that revert.
- `record.md` contains the proposed replacement row wording, in full, ready to apply.
- `record.md` and `floor-delta.json` exist and are accurate.
- Build 0W/0E.
- The SP-071, SP-072 and SP-073 rows are untouched.

## Do NOT

- **Add a timeout to any of the seven call sites, or to the returned task of `StartAsync`/`StopAsync`.** The task is already complete when the caller receives it; a timeout that can never fire is theater, and a test asserting it would pass with the mechanism removed, which is the vacuity bar this project has failed three times.
- **Copy the shape at `DtrhHostWindow.axaml.cs:259-260` or `IntakeHostContext.cs:128-130`.** Those `.Wait(TimeSpan.FromSeconds(2))` calls bound an operation that cannot block. They are the existing instance of the mistake, not a precedent.
- **Make `Load()` asynchronous, or move it onto `Task.Run` inside `StartAsync`.** That converts every one of the seven call sites into a real sync-over-async block, and it reorders the load against `DtrhHostWindow.axaml.cs:115-117`, where the SP-055 comment states the asset selection must load before the effects and manifest init. It is also unfixable inside your scope, since the repair would have to reach the callers. Induce this shape only as the revert in Step 3, and restore the tree.
- **Touch `AiMemoryStore.cs:272`, `CompanionViewModel.cs:312-315`, or the `Clear()` ordering.** Out of scope, and the wait is already off the UI thread by a reviewed decision. Bounding the write-quiescence there would let the delete run while a write is in flight, which is exactly the resurrection hazard `AiMemoryStore.cs:246-251` exists to prevent, on a privacy operation.
- Edit any of the five caller files. Widening collides with another lane in this wave.
- Edit `client/tests/floor/floor.json`, `client/docs/task-board.md`, or anything under `client/docs/`, `.claude/`, `.spine/`, `.pi/`, or `ConditioningControlPanel/`.
- Edit `client/tests/CcpClient.Tests/PersistenceTests.cs`, or move your new tests into `CcpClient.HeadlessTests`.
- Close, edit, or claim a neighbouring board row. Propose wording in `record.md`; the orchestrator applies it at land.
- Add a wall-clock wait. `client/tests/CcpClient.Tests/TestWait.cs` is the only approved helper; `Thread.Sleep`, bare `Task.Delay`, and `DateTime`/`Environment.TickCount64` polls fail the timing guard mechanically and `TestTimingGuardTests` will red your run.
- Export `CCP_DATA_ROOT` process-wide.
- Leave a TODO, a placeholder, or a partially wired mechanism.

## Git Commit Convention

Conventional commits, `fix(SP-078): ...` under Branch A (the deliverable corrects a false record and pins the truth), `feat(SP-078): ...` under Branch B. One coherent slice, no unrelated files. Leave the tree buildable at every commit. Commit your own work on your branch; do not merge, do not land, and do not touch the shared pin.

## Documentation Requirements

If your work changes a fact stated in `client/docs/async-lifecycle-fault-contract.md` or `client/docs/persistence-migration-contract.md`, say so in `record.md` and quote the wording you believe is owed. **Do not edit either contract document yourself**, and do not edit the task board; policy-touching text and board rows are applied by the orchestrator at land (SP-059 precedent, followed by SP-071, SP-072 and SP-073).

Consider proposing wording for §5 of the async contract, since `:51` and `:63` were both read as covering these sites and neither does: rule 1 addresses awaits, rule 6 addresses native or backgrounded work, and in-process synchronous I/O taken on a UI-thread path falls between them. Propose it; do not apply it.
