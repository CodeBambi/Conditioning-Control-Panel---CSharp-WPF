# SP-087 record: the "five unbounded disk-store waits" row rests on a false premise

**Branch A.** Base `feat/crossplatform` at `cf9f7143`, lane branch `lane/SP-087-disk-store-unbounded-waits`,
worktree `.claude/worktrees/sp087`. Build 0W/0E. Every line cited below was opened in this tree.

---

## 1. Census (Step 1), re-derived rather than inherited

### 1.1 The two methods the question turns on

- `client/src/CcpClient.Desktop/Persistence/PersistenceStore.cs:158-170` `StartAsync`: no `async`, no
  `await`, no `Task.Run`. `Running = true` (`:166`), `_owner.Begin()` (`:167`), `Load()` **inline**
  (`:168`), `return Task.CompletedTask;` (`:169`).
- `:173-183` `StopAsync`: guard `if (!Running) return Task.CompletedTask;` (`:175-178`), then
  `Running = false` (`:180`), `_owner.Cancel()` (`:181`), `return Task.CompletedTask;` (`:182`). No file
  API is reachable from this method.
- `:288-402` `Load()`: synchronous disk I/O on the calling thread. `File.Exists` (`:294`), `File.Move`
  (`:297`), `File.Delete` (`:303`), `File.ReadAllText` (`:318`), `JsonNode.Parse` (`:322`), migrations
  (`:354-362`), `Deserialize` (`:367`).
- Contrast, the methods that DO produce a real task: `Save()` `:230-249` returns `_owner.RunAsync(...)`;
  `SaveImmediate()` `:252` awaits it. That body runs on `Task.Run` with `ConfigureAwait(false)`
  (`Lifecycle/OperationRegistry.cs:216-221`).

**`Begin()`/`Cancel()` cannot block, checked rather than assumed.** A CTS cancel runs registered callbacks
synchronously on the cancelling thread, so "a CTS cancel is fast" is an assumption until the callbacks are
counted. Grepping `client/src/**` for `.Register(` / `UnsafeRegister` returns eight hits and **every one is
the capability registry** (`Ai/AiOperationPipeline.cs:95,114`, `Ai/AiAwarenessService.cs:307`,
`Lifecycle/CompositionRoot.cs:252,254,259,261,268`). **Zero `CancellationToken.Register` callbacks exist
tree-wide**, so the owner CTS has nothing to run. `AsyncOperationOwner.Cancel` is `OperationRegistry.cs:161-167`.

### 1.2 Seven sites, three questions each

| # | Site | Q1: waits on anything? | Q2: caller on the UI thread? | Q3: bounded by | Real cost |
|---|---|---|---|---|---|
| 1 | `Features/Dtrh/DtrhHostWindow.axaml.cs:228` | **No** — `Task.CompletedTask` (`:169`) | **Yes** — `InitBarkPipeline` (`:205`) from the `Opened` handler `:113-124`, at `:120` | the operation itself | UI-thread blocking disk I/O |
| 2 | `Persistence/AssetSelectionDocument.cs:61` (`AssetSelectionStore.Start` `:54-68`) | **No** | **Yes, from two hosts** — `DtrhHostWindow.axaml.cs:190` (`Opened`, `:117`) and `Features/Intake/IntakeHostContext.cs:109` | the operation itself | UI-thread blocking disk I/O |
| 3 | `Features/Intake/IntakeHostContext.cs:84` | **No** | **Yes** — sole product caller `IntakeLaunchCoordinator.cs:56` (`Launch()` `:48`), wired at `App.axaml.cs:283` `dashboard.Opened += (_, _) => intakeCoordinator.Launch();` | the operation itself | UI-thread blocking disk I/O |
| 4 | `Features/Intake/IntakeHostContext.cs:95` | **No** | **Yes** — same path | the operation itself | UI-thread blocking disk I/O |
| 5 | `Features/Dtrh/DtrhSaveSlots.cs:467` (`StopAsync`, in `DeleteSlot` `:447`) | **No** — `Task.CompletedTask` (`:182`) | **Yes** — `DtrhSlotPickerWindow.axaml.cs:257` (`ConfirmDelete` `:254`), `ConfirmEraseButton.Click` wired at `:40` | nothing to bound | **nothing at all** |
| 6 | `Features/Dtrh/DtrhSaveSlots.cs:469` | **No** | **Yes** — same path | the operation itself | disk I/O, cheapest branch (`Missing`, `:311-315`) |
| 7 | `Ai/AiMemoryStore.cs:272` | **YES** — a real threadpool task chained onto every prior write (`PersistenceStore.cs:240-247`) | **NO** — `Task.Run` at `Features/Companion/CompanionViewModel.cs:312-315`, comment at `:314` | **unbounded by design** | required: quiescence before the delete at `:275-276`, or a queued write resurrects the file |

### 1.3 The numbers, stated plainly

- **|{unbounded wait} INTERSECT {UI thread}| = 0.** Sites 1-6 fail the first predicate; site 7 fails the
  second. **Not five. Zero.**
- **Six of the seven wait on nothing.**
- **Real count of `.GetAwaiter().GetResult()` on a `PersistenceStore` LIFECYCLE method in `client/src/**` = 6.**
  Site 7 is a `SaveImmediate` call, a different method with a different shape.
- **"Five" was FILES, not sites.** The row's citation list is five file names.
  `DtrhSaveSlots` and `IntakeHostContext` hold two sites each, and `AssetSelectionDocument` holds one site
  reached from two hosts. Seven sites, eight UI-reachable entries, five files.

**Full-tree honesty, measured:** `client/src/**` contains **18** real `.GetAwaiter().GetResult()`
occurrences. The grep returns 20 hits; two are comment lines
(`Audio/AudioSeams.cs:261`, `Features/Dtrh/SoundFlowDtrhAudio.cs:43`). Breakdown: 6 lifecycle sites above,
1 site-7 `SaveImmediate`, and 11 classified EXIT-PATH / BOUNDED-OK by `SP-071/record.md:42-49`
(`Program.cs:157,165,168,263`, `App.axaml.cs:92`, `Persistence/SecretStores.cs:145,158,170`,
`Audio/AudioSeams.cs:262,268,397`).

> **Do not conflate two different 18s.** `task-board.md:117` says SP-071 "censused every blocking wait in
> `client/src/**` (18 sites)". That population is every blocking-wait SHAPE (`.Wait(TimeSpan)`, `.Result`,
> `Join`, `.GetAwaiter().GetResult()`), not the `.GetAwaiter().GetResult()` occurrences counted here. The
> two numbers coincide at 18 by accident. This is the same trap "five" set, and it is flagged so the next
> reader does not repeat it.

An eighth blocking `StartAsync` exists in TEST code only:
`client/tests/CcpClient.HeadlessTests/DtrhSlotPickerHeadlessTests.cs:24`. Not a defect; census completeness.
It becomes load-bearing in §3 below.

### 1.4 Where the drift entered: one word

- `spine-tasks/SP-071-teardown-off-ui-thread/record.md:50-53` wrote "unbounded in-process disk-store
  **starts** on UI-reachable paths", and for site 7 wrote "UI-reachable via `CompanionViewModel.cs:315`,
  **holds the store gate** while waiting". The census was careful in all three respects: it said *starts*,
  it said *UI-reachable*, and it named the **store gate** rather than the UI thread.
- `client/docs/task-board.md:117` rewrote that as "Five unbounded in-process disk-store **waits** sit on
  UI-reachable paths", and added "each is a **UI-thread block with no bound**". Three separate degradations:
  *starts* became *waits*, *UI-reachable* became *UI-thread block*, and the *store gate* became the UI thread.
  That is the fabricated half.
- The contract citation is misapplied in **both** directions.
  `client/docs/async-lifecycle-fault-contract.md:51` (§5 rule 1) removes the class "teardown blocked on the
  UI thread **awaiting** an operation that awaits the UI thread"; six of seven sites await nothing, so the
  rule does not reach them. `:63` (§5 rule 6) bounds waits on "any **native or backgrounded** work";
  `Load()` is neither, being in-process and synchronous on the caller's thread. In-process synchronous I/O
  taken on a UI-thread path falls **between** the two rules. Proposed wording in §7.

---

## 2. Decision: Branch A

The intersection is empty, so Branch A is the pre-authorized outcome and Branch B is unavailable on its own
terms. Branch B needs a genuine unbounded await reachable on a UI thread **inside `PersistenceStore`**; the
only genuine unbounded wait (site 7) is neither inside my scope nor on a UI thread, and bounding it would
let the delete at `AiMemoryStore.cs:275-276` run while a write is in flight, which is the file-resurrection
hazard on a privacy operation that `AiMemoryStore.cs:244-258` exists to prevent.

**Deliverable: the correction plus the facts that pin it.** No bound, no new mechanism, no caller edit, no
escalation.

**The scope mismatch the packet predicted is confirmed.** All seven cited call sites live in five files,
none of them in my File Scope. Had the row's implied remedy been real, this packet could not have delivered
it. That mismatch is evidence about the row, not an obstacle to this packet.

---

## 3. Revert matrix, executed

Each revert was applied to **one source at a time**, built, measured, then restored with
`git checkout -- client/src/CcpClient.Desktop/Persistence/PersistenceStore.cs` and verified byte-identical
by `git diff --quiet` (exit 0) before the next.

Baseline (no revert): **CcpClient.Tests 1032 total, 0 failed, 2 skipped** (the two Linux-gated pinned names);
CcpClient.HeadlessTests met its pin of 35.

| Revert | Induced defect | Unit facts red | Other pins moved |
|---|---|---|---|
| **R1** | `StartAsync` → `async Task`, `await Task.Yield();` before `Load();` | **2** — F1 and F2 | **YES: `CcpClient.HeadlessTests` DEADLOCKS.** 8/35 completed in 3s, then 90s inactivity, `Test Run Aborted` |
| **R2** | `Load();` → `Task.Run(Load).GetAwaiter().GetResult();` | **0** — prediction MISSED, see below | none |
| **R2b** | `Load();` → `var t = new Thread(Load); t.Start(); t.Join();` | **1** — F2 only | none |
| **R3** | `StopAsync` → `async Task`, `await Task.Yield();` AFTER the `!Running` guard (`:175-178`), before `Running = false` | **1** — F3 only | **YES: `CcpClient.HeadlessTests` DEADLOCKS.** 16/35 completed in 9s, then 60s inactivity, aborted |
| **R4** | `SaveImmediate().GetAwaiter().GetResult();` inserted BEFORE `_owner.Cancel()` | **1** — F4 | **YES, and it is the good kind:** `DtrhSaveSlotsTests.DeleteSlot_RemovesFile_ReloadsFresh_DescendStartsOver` reds at `DtrhSaveSlotsTests.cs:202` |

Every fact bites under an independent revert: **F1 ← R1, F2 ← R2b, F3 ← R3, F4 ← R4.**

### 3.1 R2 was a wrong prediction, and the measurement is the finding

The plan predicted R2 would deterministically red F2 ("only the recorded thread changes, deterministically").
**It did not red anything**, across a full suite run, a clean-rebuild repeat, and a filtered 4-fact run.
Rather than record a shrug, I measured why with a standalone probe (scratch, outside the repo):

```
waiter = console Main (NOT a pool thread): inlined-onto-caller=0,   offloaded=500
waiter = ThreadPool thread (xunit's case) : inlined-onto-caller=438, offloaded=62
```

`Task.Run(work).GetAwaiter().GetResult()` can **inline `work` onto the blocked waiter**, but only when the
waiter is itself a ThreadPool thread. xunit runs fact bodies on pool threads, so under R2 the load really
did keep running on the calling thread roughly 7 times in 8, and **F2 was correct to stay green** — thread
affinity genuinely was preserved. My first probe measured from a console `Main`, saw 0/500 inlining, and
would have let me assert the opposite; that error is recorded here rather than hidden.

Three consequences worth keeping:

1. **R2 is not a valid independent revert** for a thread-affinity pin. It is a weighted coin flip, so it
   could equally have produced a *flaky* red and been mistaken for a live pin.
2. **The packet's own suggested revert is affected.** Step 3 offers "make `Load()` run under `Task.Run`
   inside `StartAsync`" as the synthetic revert. For the completion-shape pins that is fine; for the
   thread-affinity pin it is nondeterministic. R2b (a dedicated `Thread` + `Join`, which can never be
   inlined) is the deterministic form and is what proves F2.
3. It sharpens the correction itself: even the "obvious" way to move `Load()` off the caller frequently
   does not move it.

### 3.2 R1 and R3 deadlock the headless project, which is the row's real hazard inverted

This was not predicted and is the most useful thing the matrix produced.

Under **R1**, `await Task.Yield()` inside `StartAsync` captures the Avalonia dispatcher
`SynchronizationContext` and posts the continuation there. `DtrhSlotPickerHeadlessTests.cs:24` calls
`slots.StartAsync(...).GetAwaiter().GetResult()` inside an `[AvaloniaFact]`, i.e. **on the UI thread**, which
is now blocked and can never pump the continuation. Classic sync-over-async deadlock. `ConfigureAwait(false)`
at `DtrhSaveSlots.cs:295,298,301` does not save it, because the capture happens deeper, inside
`PersistenceStore.StartAsync` itself.

Under **R3**, the same shape via `DtrhSaveSlots.DeleteSlot`'s `old.StopAsync().GetAwaiter().GetResult()`
(`:467`), reached from the slot picker's `ConfirmDelete`.

**This is exactly the latent hazard the board row was groping at, and got backwards.** The row claims the
call sites block unboundedly today. They do not. But they are *one product edit away* from doing so, and
that edit is precisely the "fix" a lane that believed the row would reach for. The doc note added by this
packet says so at the two methods, and R1/R3 are the executed evidence.

### 3.3 R4's neighbour red is corroboration, not collateral

`DeleteSlot` deletes the slot file (`DtrhSaveSlots.cs:453-456`) and then stops the old store (`:467`) so that
"an in-flight write can't resurrect the deleted file" (`:463-465`). Under R4 `StopAsync` writes, so the
deleted save slot **comes back**, and the neighbouring pin catches it at `DtrhSaveSlotsTests.cs:202`
(`Assert.False` → actual `True`). An independent, pre-existing pin proves "StopAsync must not write" is a
real product invariant and not merely a property of my new fact.

### 3.4 Which channel of F3 fires

The plan flagged as uncertain which of F3's channels reds first under R3. **Measured:** the first,
`Assert.True(task.IsCompletedSuccessfully)` at `PersistenceStoreTests.cs:97`. xunit stops at the first failed
assertion, so the `Running` and post-stop-`Cancelled` channels are not reached under R3. They are not
decoration: the `Cancelled` channel is what pins `_owner.Cancel()` against deletion, a defect R3 does not
induce.

---

## 4. What landed

**A. `client/src/CcpClient.Desktop/Persistence/PersistenceStore.cs` — doc comment only, zero executable
statements changed.** `<remarks>` blocks on `StartAsync` (`:157`) and `StopAsync` (`:172`) stating that both
complete synchronously on the calling thread, that `Load()`'s blocking disk I/O finishes before the returned
task exists, that a caller-side timeout on that task therefore bounds nothing, that the real cost is thread
affinity, and that making `Load()` async or wrapping it in `Task.Run` without reaching the callers is the
change that would convert every call site into a genuine sync-over-async block. This is the sentence whose
absence let the census be misread.

**B. `client/tests/CcpClient.Tests/PersistenceStoreTests.cs` — new, 4 facts.** +4 unit, +0 headless.

- **F1** `StartAsync_ReturnsACompletedTask_WithTheLoadOutcomeAlreadyObservable` — observes the task WITHOUT
  awaiting: `IsCompletedSuccessfully`, `LastLoadOutcome` non-null and typed `Missing`.
- **F2** `Load_RunsOnTheCallingThread_RecordedFromInsideTheOperation` — the SP-072 read-from-inside shape.
  A `ThreadRecordingLogSink` records `Environment.CurrentManagedThreadId` when the product logs mid-`Load`.
  Drives the orphan-temp crash-recovery branch (`PersistenceStore.cs:294-298`), whose `_log.Log` at `:296`
  fires **unconditionally**.
- **F3** `StopAsync_OnARunningStore_ReturnsACompletedTask_AndCancelsTheGeneration` — awaits `StartAsync`
  first and asserts `Running` **before** the call, so the `!Running` guard is provably not the path under
  test; then completed-task, `!Running`, and a post-stop `Save()` terminating typed `Cancelled`.
- **F4** `StopAsync_WritesNothingToDisk_EvenWhenTheStoreIsDirty` — mutate, assert dirty, stop, assert
  nothing reached the directory.

### 4.1 Two evidence-based corrections to the packet's own test suggestions

1. The packet suggests driving the log via "a stale temp beside a valid main (`:299-309`)". That branch logs
   **only when `File.Delete` throws** (`:307`), which needs a locked file and is OS-dependent. F2 uses the
   orphan-temp branch (`:294-298`) instead, which logs unconditionally. The quarantine path (`:405-432`, log
   at `:423`) was the other deterministic option.
2. **A constraint the plan missed and execution caught.** `VacuousShapeDetector.cs:225-229` flags any fact
   body containing the literal `File.Exists(` or `Directory.Exists(` as an `fs-predicate` SITE, regardless of
   nesting, and an undispositioned site fails `VacuousShapeGuardTests` — whose ledger,
   `client/tests/floor/vacuous-shape-ledger.json`, is **outside this packet's File Scope**. The plan's F4 used
   `Assert.False(File.Exists(path))` and would have been unlandable. F4 asserts the same claim through
   `Directory.GetFiles(dir.Root)` (strictly stronger: nothing at all was written) plus
   `new FileInfo(path).Exists`. All four facts are otherwise shape-clean by construction: at least one
   top-level `Assert.`, no bare `return;`, no conditional or loop wrapping the only assertions, no platform
   or env predicate, no dynamic skip. **No new ledger entry is required and none was made.**

### 4.2 Locks and ordering

The change adds no lock and no thread, but the facts execute locked code. `_mutationGate` (`:102`) is taken
by `Mutate` (`:188`), `Replace` (`:204`), `IsDirty` (`:146`), `SetDefaults` (`:437`) and `WriteOnce`
(`:455,464`); `Load()` does not hold it across file I/O. `_writeGate` (`:103`) is taken by `Save` (`:232`)
and `FlushAsync` (`:262`). Nesting is `_writeGate` → `_mutationGate` (`FlushAsync` holds `_writeGate` at
`:262` then calls `IsDirty` at `:264`, which takes `_mutationGate` at `:146`). F4 asserts `StopAsync` writes
nothing precisely by never entering `_writeGate`.

---

## 5. Proposed replacement board row (orchestrator applies; I did not touch the board)

Replaces the P2 OPEN row at `client/docs/task-board.md:117`.

> \| P3 \| OPEN \| Five UI-thread `PersistenceStore.StartAsync` call sites do their disk load ON the UI thread (thread affinity, NOT an unbounded wait) \| **PREMISE CORRECTED 2026-08-17 by SP-087 (supersedes SP-078), which re-derived the census and pinned the correction with executed facts rather than asserting it in a record.** The previous wording ("Five unbounded in-process disk-store waits sit on UI-reachable paths ... each is a UI-thread block with no bound") was **false in three separate ways**, and the drift is traceable to one word: `SP-071/record.md:50-53` wrote disk-store "**starts**" on "**UI-reachable**" paths and named the **store gate** for site 7; the board rewrote that as "**waits**", "**UI-thread block**", and the UI thread. **The intersection {unbounded wait} INTERSECT {UI thread} is EMPTY — zero, not five.** `PersistenceStore.StartAsync` (`:158-170`) and `StopAsync` (`:173-183`) have no `async`, no `await` and no `Task.Run`; `Load()` runs inline and both return `Task.CompletedTask`, so the `.GetAwaiter().GetResult()` at six of the seven cited sites resolves a task that was already complete before the caller held it, and the two shipped `.Wait(TimeSpan.FromSeconds(2))` pairs are bounding an operation incapable of blocking. The seventh site (`AiMemoryStore.cs:272`) is the one genuine unbounded wait and is deliberately **off** the UI thread (`CompanionViewModel.cs:312-315`, SP-040 consult); bounding it would let the privacy delete run while a write is in flight, so it is **not** an open defect. "Five" was the number of FILES; there are seven sites in five files, eight UI-reachable entries. The **§5 citation was misapplied in both directions**: rule 1 (`:51`) covers *awaits*, rule 6 (`:63`) covers *native or backgrounded* work, and in-process synchronous I/O on a UI-thread path falls between them (wording proposed in the packet record, orchestrator applies). **THE RESIDUAL DEFECT, which is real and is what this row now tracks:** `Load()` performs blocking synchronous disk I/O **on whatever thread calls `StartAsync`**, and five UI-thread paths call it — `DtrhHostWindow.axaml.cs:228` and `:190` (both from the `Opened` handler `:113-124`), `IntakeHostContext.cs:84,95` and `:109` (from `IntakeLaunchCoordinator.cs:56`, wired at `App.axaml.cs:283`), `DtrhSaveSlots.cs:469` (from `DtrhSlotPickerWindow.axaml.cs:257`). **No timeout can bound this** — the I/O has already finished by the time a task exists — so any caller-side bound is theater; only moving the I/O would help, and that is not fixable inside `PersistenceStore` (see the DO-NOT below). **Downgraded P2 → P3** because the cost is a local disk read of a small JSON document on paths that are already doing window construction, and the previously claimed unbounded-block cost does not exist. **Pinned by `client/tests/CcpClient.Tests/PersistenceStoreTests.cs` (4 facts, +4 unit):** `StartAsync` returns an already-completed task with `LastLoadOutcome` already observable; `Load()` runs on the calling thread, **recorded from inside the operation** through an `ILogSink` fake (SP-072 shape) rather than sampled from outside; `StopAsync` on a **running** store completes synchronously and cancels the generation (witnessed by a post-stop `Save()` terminating typed `Cancelled`, a result the never-started guard path provably cannot produce, since `RunAsync` throws at `OperationRegistry.cs:204-208` with no live generation); and `StopAsync` writes nothing even when dirty. `PersistenceStore.cs` carries the doc note that makes the shape unmissable at the two methods. **DO NOT "fix" this by making `Load()` async or wrapping it in `Task.Run` inside `StartAsync`** — the SP-087 revert matrix **executed** that shape and it **deadlocks the headless suite** (R1: 8/35 tests then hang; R3, the same shape in `StopAsync`: 16/35 then hang), because `Task.Yield()` captures the Avalonia dispatcher while a UI-thread caller is blocked in `.GetAwaiter().GetResult()`; it also reorders the load against the SP-055 asset-selection-first ordering at `DtrhHostWindow.axaml.cs:115-117`. The repair would have to reach all five caller files, which is why this row is **not** closable inside `PersistenceStore`. **Acceptance:** either move the load off the UI thread **at the callers** (one row per host, honouring SP-055 ordering), or a measurement of `Load()` on a cold disk and a network-mapped data root showing the UI-thread cost is acceptable, and then close it. **Not measured today** — "bounded in practice" is an argument, and the P3 rests on it. Size S \|

---

## 6. Out of File Scope: filed, not fixed

1. **Five shipped `.Wait(TimeSpan.FromSeconds(2))` calls that bound nothing** — `DtrhHostWindow.axaml.cs:259,260`
   and `IntakeHostContext.cs:128,129,130`, all on `StopAsync`. Exactly five, and the coincidence with the
   row's "five" is named here so nobody conflates the two sets: **these are a different five** from the five
   files the row cites. By contrast `IntakeHostContext.cs:126-127` wraps `PersistenceStore.FlushAsync`, which
   genuinely can block (`PersistenceStore.cs:275`, `Task.WhenAny` over the write tail), so those two are
   legitimate. **`DtrhHostWindow.axaml.cs:257` is NOT a `PersistenceStore` call at all** — it wraps
   `BarkPipeline.FlushAsync()` (`_bark`, constructed at `:236`). `SP-071/record.md:42-44` called the whole
   group BOUNDED-OK, which is true but obscures that three of them bound nothing.
2. **Two `PersistenceStore` instances over one `asset_selection.json`** — owners `"DtrhAssetSelection"`
   (`DtrhHostWindow.axaml.cs:190`) and `"IntakeAssetSelection"` (`IntakeHostContext.cs:109`) against the same
   `dataDir` (`AssetSelectionDocument.cs:59`). Harmless today (read-only per the SP-055 comment at
   `DtrhHostWindow.axaml.cs:182-187`); becomes last-writer-wins with two independent write chains once the
   Assets-tree write path lands.
3. **The `_writeGate`/`_mutationGate` ordering is undocumented.** Nesting is `_writeGate` → `_mutationGate`
   (`FlushAsync:262` → `IsDirty:146`). Acyclicity does **not** rest on `Replace:222` calling `Save()` outside
   the lock alone: `Replace` invokes `SettingsReplaced` handlers **inside** `_mutationGate` (`:209-219`), so a
   handler that called `Save()` would nest `_mutationGate` → `_writeGate`, the reverse order. What actually
   holds the invariant is the handler contract at `:149-154` **plus** `Replace:222`. Proposed doc wording:
   *"Lock order is `_writeGate` then `_mutationGate`, never the reverse. `Replace` raises `SettingsReplaced`
   inside `_mutationGate` and enqueues the save only after releasing it (`:222`); a handler that itself calls
   `Save`/`FlushAsync` would invert the order and deadlock, which is why §8's handler contract forbids it."*
   Not applied: the packet authorizes the `StartAsync`/`StopAsync` doc note only, so a second doc block is
   scope creep.
4. **`async-lifecycle-fault-contract.md` §5 is misnumbered.** Rule **7** prints at `:62`, before rule **6**
   at `:63` (the SP-072 and SP-071 lands applied wording out of order). `client/docs/**` is
   `fileScopeMustNotChange`, so this is reported only.
5. **`DtrhSlotPickerHeadlessTests.cs:24`** blocks on `StartAsync` on the Avalonia UI thread in test code.
   Not a defect today for the same reason the product sites are not. It is, however, the exact tripwire that
   turned R1 and R3 from a red into a hang, so it is worth knowing it exists.

---

## 7. Documentation owed (proposed only, never applied)

Nothing this packet did changes a fact stated in `async-lifecycle-fault-contract.md` or
`persistence-migration-contract.md`. It does expose a **gap** between §5 rules 1 and 6. Proposed §5 rule 8:

> 8. **In-process synchronous work taken on a UI-thread path is a THREAD-AFFINITY fault, not a wait, and a
>    caller-side timeout never fixes it.** Rule 1 removes the *awaitable* deadlock class and rule 6 bounds
>    waits on *native or backgrounded* work; synchronous in-process work (a file read, a parse, a migration)
>    performed inline by a method that returns an already-completed task falls between them. Such a method
>    must say so in its doc comment, because a caller cannot tell from the `Async` suffix, and a bounded wait
>    on the returned task is theater: the work finished before the task existed. The only real remedies are
>    to move the work off the calling thread **at the caller** or to accept the cost with a measurement.
>    Wrapping the inline work in `Task.Run` or an `await` inside the callee is **forbidden without changing
>    every caller in the same change**: it converts each existing `.GetAwaiter().GetResult()` into a genuine
>    sync-over-async block, and where the caller is a UI thread the result is a deadlock, executed and
>    observed in the SP-087 revert matrix (R1, R3).

---

## 8. Carried conditions from the plan review (all six, plus the reviewer's note)

| # | Condition | Disposition |
|---|---|---|
| 1 | Plan §1.3's "14 `.GetAwaiter().GetResult()` in `client/src/**`" is wrong; fix before it lands in `record.md` | **DISCHARGED and corrected.** Measured **18** (20 grep hits minus 2 comment lines, `AudioSeams.cs:261` and `SoundFlowDtrhAudio.cs:43`), enumerated in §1.3. The load-bearing counts (6 lifecycle sites, intersection 0) are unaffected. I additionally flag a NEW conflation risk the condition did not name: the board row's own "18 sites" is a different population (all blocking-wait shapes). |
| 2 | §4's "the reverse is deliberately absent" is not strictly true — `Replace` raises handlers inside `_mutationGate` (`:209-219`) | **DISCHARGED.** Corrected in §4.2 and folded into the durable artifact, the §6 item 3 proposed wording, which now names the handler contract at `:149-154` **plus** `Replace:222` as jointly holding acyclicity. |
| 3 | §6 item 1 mis-attributes one `FlushAsync`: `DtrhHostWindow.axaml.cs:257` wraps `BarkPipeline.FlushAsync` | **DISCHARGED.** Verified by reading (`_bark` constructed at `:236`) and corrected in §6 item 1. The "exactly five `.Wait(TimeSpan.FromSeconds(2))` on `StopAsync`" claim is confirmed correct: `DtrhHostWindow.axaml.cs:259,260`, `IntakeHostContext.cs:128,129,130`. |
| 4 | R1's determinism is overstated; record as MEASURED, not "by contract, not a race" | **DISCHARGED, and it earned its keep.** R1, R3 and R4 are recorded as measured outcomes. R2's determinism claim was measured **false** (§3.1) and required an unplanned R2b to prove F2 bites. No revert in §3 is described as deterministic-by-contract. |
| 5 | Two line refs point at the brace: `_writeGate` is taken at `:262` (not `:263`), `WriteOnce`'s first `lock (_mutationGate)` at `:455` (not `:456`) | **DISCHARGED.** Both verified in source and this record uses the corrected refs throughout (§4.2). |
| 6 | F2 hardening: also assert the first message is the orphan-temp recovery line | **IMPLEMENTED.** `Assert.StartsWith("persistence: recovering settings from interrupted save", sink.FirstMessage)` at `PersistenceStoreTests.cs:78`, so a future edit that logs earlier in `StartAsync` cannot silently redirect the fact onto a different call. |
| — | Reviewer's note: condition 4 applies to R3's determinism sentence exactly as to R1's | **DISCHARGED.** R3's row and §3.4 report only what was observed, including which channel fired first. |

---

## 9. Honesty: what is NOT proven

- **Not measured:** how long `Load()` takes on a cold disk or a network-mapped data root. "Bounded in
  practice" is an argument, not a fact, and the proposed P3 severity rests on it. This is the single
  weakest load-bearing claim in the packet.
- **The facts prove THREAD AFFINITY, not "and that thread was the UI thread."** `CcpClient.Tests` has no
  Avalonia dispatcher, and acquiring one would mean the headless project, which is out of scope and the
  wrong home. The five UI-thread caller claims in §1.2 are **verified by reading the call chains**, cited
  line by line, and never asserted by a test. They were not executed on a real dispatcher.
- **Not proven:** that `CompanionViewModel.cs:312-315` is the only caller of `AiMemoryStore.Clear()` for
  future callers. Verified for today's tree only. If a second caller appears without the `Task.Run` wrapper,
  site 7 becomes a genuine UI-thread unbounded wait and this correction stops covering it.
- **The census is of today's tree**, which is exactly why F1 and F2 exist: they convert the perishable half
  of the reading into something the suite re-checks every run.
- **R2's inlining explanation** is measured on this machine and runtime (.NET SDK 10.0.303, Windows 11,
  8 physical cores) with a 500-iteration probe. The 438/500 ratio is not a contract; the load-bearing claim
  is only the qualitative one, that inlining onto a pool-thread waiter happens often enough to make R2
  useless as a revert.
- **The R1/R3 headless deadlocks were observed under `--blame-hang`**, which reports inactivity, not a
  proven lock cycle. I did not capture and read a dump to confirm the exact blocked frames; the mechanism in
  §3.2 is inferred from the code path plus the observation that the hang appears only under the two reverts
  that introduce a context-capturing `await`, and disappears on restore.
- **No Linux execution of any kind.** Windows only.
- **Round-1 plan defect, recorded rather than quietly fixed:** F3 was first specified against a never-started
  store, where `StopAsync`'s `!Running` guard (`:175-178`) satisfies both of its assertions with the
  mechanism deleted, and R3's yield sat on a path that store never reaches. Caught at the plan gate by
  review, not by execution. Same vacuity class the packet warns about, reached from the opposite direction:
  not a timeout that cannot fire, but an early return that makes the assertions free.
- **Plan defect caught in execution, not review:** the plan's F4 used `File.Exists(`, which
  `VacuousShapeDetector.cs:225-229` turns into a ledger SITE requiring an edit outside File Scope (§4.1).
  The plan's §3.2 shape analysis checked assertion nesting and early returns but not the token-based
  predicates.

---

## 10. Verification

Both commands run through the slot semaphore, build immediately before the gate, as separate commands.

```
node client/tools/gate/with-slot.mjs --slots 3 -- dotnet build client/CcpClient.sln -c Debug --nologo
node client/tools/gate/with-slot.mjs --slots 3 -- node client/tests/floor/check-floor.mjs
```

- **Build: 0 Warning(s), 0 Error(s).**
- **Floor: observed `CcpClient.Tests` 1032, pin 1028, declared delta +4. 1028 + 4 = 1032, exact.**
  `CcpClient.HeadlessTests` observed 35, pin 35, declared delta 0.
- The gate reports FLOOR VIOLATION on the total drift. **That is the designed state for a bound packet:**
  the shared pin is bumped at land from the summed deltas, never by the lane.
  `client/tests/floor/floor.json` was not touched.
- Skips: exactly the 2 pinned Linux-gated names
  (`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`,
  `SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`). The SP-057 pin did **not** skip;
  `CCP_DATA_ROOT` was never exported.
- `spine-tasks/SP-087-disk-store-unbounded-waits/floor-delta.json` declares `{unit: 4, headless: 0}`.
- The SP-071, SP-072 and SP-073 rows were not touched. The board was not touched. No caller file was edited.
