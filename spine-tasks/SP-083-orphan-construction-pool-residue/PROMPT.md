# SP-083: Bound the outstanding abandoned CONSTRUCTIONS a wedged endpoint can accumulate

**Supersedes SP-074** (wave 31, escalated at the plan gate having written no product code; record: `spine-tasks/CONTEXT.md`, wave-31 section). Same work, new ID: the packet ID is execution state, the durable identity is the board row. Renamed rather than reissued so SP-0074-as-escalated stays exactly what the wave-31 record describes.

## Mission

SP-072 (wave 29) stopped the two `CreatePlayer` sites blocking their caller inside a native `AssetDataProvider` construction. It did that with `OrphanSafePlayerFactory<TPlayer>`: the construction runs on a thread-pool thread, the caller waits a bounded 2s, and on expiry the caller abandons it and takes a typed `PlayerConstructionTimeoutException`. The orphan invariant is intact and pinned by seven facts: an abandoned construction never reaches the mixer, never plays, is disposed exactly once, ordered against device teardown.

It left a residue, and the SP-072 pre-approach consult named it before the lane ran rather than discovering it after: **the abandoned construction keeps running.** The pool thread it occupies is never returned, because nothing in .NET can interrupt a native constructor that has not come back. One abandonment costs one permanently parked pool thread, and nothing in the port counts them.

Your outcome: **the number of outstanding abandoned constructions is either PROVEN bounded per app session by a fact, or BOUNDED by a mechanism.** Which one you deliver is decided in Step 1 by a census against a rule pre-authorized both ways (below). A second fork, where the bound has to *live*, is pre-authorized the same way in Step 2. What you may not deliver is a third thing: a caller that waits again.

## Premise, verified at authoring

The board row is `client/docs/task-board.md:125`. Its premise was checked against the PORT tree before this packet was written, because a P1 row in this repo was recently found to describe port code that does not exist. **This row's premise HOLDS**, with one correction to how it has been paraphrased:

- **TRUE:** `OrphanSafePlayerFactory` exists in the port at `client/src/CcpClient.Desktop/Audio/AudioSeams.cs:183`, and `Create` starts every construction with `var task = Task.Run(() => _construct(path, volume));` at **`AudioSeams.cs:236`**. That is the pool thread. If `_construct` never returns, that thread parks for the process lifetime.
- **TRUE:** the growth is one per cue. Five call sites reach `Create`, and every one of them catches the typed refusal, logs, and continues: `SoundArbitration.cs:548` (`PlaySfx`), `:761` (the shared `CreatePlayer` helper behind `PlayVoice`/`PlayWhisper` at `:386`/`:463`), `:877` (queued voice), `DtrhNativeEffects.cs:114` (DTRH sfx), `:454` (DTRH whisper). None of them latches a "backend unhealthy" state. The next cue calls `Create` again and starts another `Task.Run`.
- **TRUE:** nothing counts them. Read all of `AudioSeams.cs:183-375`: the factory holds `_lifecycle`, `_tornDown`, and a per-call `ConstructionSlot`. There is no outstanding-construction counter and no cap of any kind.
- **CORRECTION, and it matters because a lane would otherwise edit the wrong code:** the residue is **not** `ISoundClock.Schedule` and **not** `AudioSeams.cs:133-137`. Lines 133-137 are `SystemSoundClock.Schedule`, a one-shot `System.Threading.Timer`. It has exactly three live uses, none of them player construction: the SP-070 recovery re-probe (`SoundArbitration.cs:726`, disposed at `:1091-1092`), the voice pacing timer (`:849`, disposed at `:924-925`), and the duck watchdog (`:932`, disposed at `:644` / `:971`). Every one of those callbacks returns promptly and every one is disposed. **Do not touch `SystemSoundClock` or `ISoundClock`.** The mechanism under this row is `Task.Run` at `AudioSeams.cs:236`.

Two candidate existing bounds were checked at authoring and **neither bites**. You must still re-derive them in Step 1, but they are recorded here so the census starts from evidence rather than from scratch:

- **SP-070's session suppression does not throttle this.** `_suppressedUntilUtc` / `_consecutiveInitFailures` are armed only by `NoteInitFailure`, which is a *device init* failure. A construction timeout is caught at `SoundArbitration.cs:762-768` and returns `SoundOutcome.Failed` without ever reaching the recovery path. A wedged endpoint that initialised successfully and then wedges inside `AssetDataProvider` is exactly the case the 30s cooldown never sees.
- **The SFX pool cap does not throttle this.** `_sfxPool.Count >= _options.MaxSfxVoices` (`SoundArbitration.cs:539`, `DtrhNativeEffects.cs:105`) is checked *before* construction, and the pool only grows on a construction that *succeeded*. Against a wedged endpoint the pool stays empty forever, so the cap never fires and every cue gets a fresh `Task.Run`.

The cost is not one idle thread. A parked pool worker is never returned to the pool; the .NET thread-pool starvation heuristic responds by injecting replacement threads slowly (roughly one per 0.5-1s beyond `MinThreads`), so N abandonments degrade every other `Task.Run` in the app, not just audio. Name that in `record.md` if you confirm it; do not assert it if you do not.

## Dependencies

SP-072 (landed, integrate `c04ecb67`) is the direct parent and the only hard dependency. SP-070 (`_suppressedUntilUtc` recovery) and SP-071 (`_teardownState`) are adjacent and out of scope.

**SP-073 runs in this same wave and owns `client/src/CcpClient.Desktop/Audio/SoundArbitration.cs`.** The scopes were pre-assigned pairwise disjoint. You own `AudioSeams.cs`. Do not edit `SoundArbitration.cs` for any reason, including a change you can justify. If your evidence says the fix needs that file, stop and report it rather than taking it.

## Context to Read First

Every line below was opened and confirmed by the orchestrator at authoring. It is cited so you can check the citation, not so you can transcribe it.

- `client/src/CcpClient.Desktop/Audio/AudioSeams.cs:153-182`: the SP-072 doc comment, including the five-clause orphan invariant. Clause 4 and the `_lifecycle` leaf-lock argument constrain anything you add.
- `:228-314` `Create`: the whole mechanism. Specifically `:236` (`Task.Run`, the residue), `:239-250` (the P4 completion continuation and the fault-observing continuation, both `ExecuteSynchronously` on the completing thread, so neither adds a thread), `:255` (`task.Wait(_budget)`), `:271` (`Monitor.TryEnter(_lifecycle, _budget)`, the second bounded wait), `:300` (`slot.Abandoned = true`), `:301-307` (the **load-bearing log order**, which an existing race pin rendezvous on: moving it makes that pin vacuous), `:313` (the typed throw).
- `:322-334` `Teardown` and `:336-348` `SpawnDisposer`. Note that `SpawnDisposer`'s `task.Wait()` is only reached when `task.IsCompletedSuccessfully`, so it returns immediately and is **not** a second parked thread. Confirm that before you count it as one.
- `:350-368` `DisposeOrphan`: the abandonment check, the `Interlocked.CompareExchange` single-dispose latch, and the ordering lock. Any decrement you add interacts with this and must be exactly-once for the same reason the dispose is.
- `client/src/CcpClient.Desktop/Audio/SoundFlowAudioBackend.cs:113-122` and `client/src/CcpClient.Desktop/Features/Dtrh/SoundFlowDtrhAudio.cs:107-116`: both real backends do nothing but delegate to `_players.Create`. This is why the mechanism must live in the factory.
- The five call sites listed under Premise above. Read all five; SP-072's census found one more construct-under-a-lock site than its packet predicted, which is why this one makes you re-derive.
- **The factory instances and where they are built**, because this is Decision B: `SoundFlowAudioBackend` at `client/src/CcpClient.Desktop/Features/Dtrh/DtrhHostWindow.axaml.cs:213` (inside `InitBarkPipeline`), `SoundFlowDtrhAudio` at `DtrhHostWindow.axaml.cs:315` (inside `InitNativeEffects`), and `SoundFlowDtrhAudio` again at `client/src/CcpClient.Desktop/Features/Dtrh/DtrhLoomWindow.axaml.cs:78`. These are per-window-open constructions, not singletons, and each one gets its own `OrphanSafePlayerFactory` with its own `_lifecycle` and its own `_tornDown`.
- `client/tests/CcpClient.Tests/SoundArbitrationTests.cs:895-1130`: `OrphanPlayer`, `OrphanHarness`, `StartCreate`, and the five existing orphan facts. Read how the fake parks before writing anything. **Three properties of the harness block a naive loop and you will hit all three:** `ConstructStarted` is a `ManualResetEventSlim` that is `Set()` once, so it is a latch and cannot tell you that N constructions are parked; `LastPlayer` is overwritten per construction, so it cannot give you all N players; and `ConstructGate` is one shared gate, which is usable for a loop only if you actually want all N released together. Extending the harness is in scope and expected.
- `client/docs/async-lifecycle-fault-contract.md:62`, rule 7. It is SP-072's landed wording and it already states this residue in the open: *"When a caller's bounded wait for a resource construction expires, the construction keeps running."* If your work changes what that sentence should say, that is a Documentation Requirement below, not an edit you make.
- `client/docs/port-lessons.md:237`: the placement lesson, third occurrence, which is the reason the File Scope is what it is.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Audio/AudioSeams.cs`, `client/tests/CcpClient.Tests/SoundArbitrationTests.cs`, `spine-tasks/SP-083-orphan-construction-pool-residue/**` |
| Must not change | everything else, and specifically the files named in the contract below. `client/src/CcpClient.Desktop/Audio/SoundArbitration.cs` is called out by name: SP-073 owns it this wave. |

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-083-orphan-construction-pool-residue/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Audio/AudioSeams.cs`, `client/tests/CcpClient.Tests/SoundArbitrationTests.cs` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/docs/task-board.md`, `ConditioningControlPanel/**`, `client/docs/**`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-083-orphan-construction-pool-residue/record.md`, `spine-tasks/SP-083-orphan-construction-pool-residue/floor-delta.json` |

**You do not edit `client/tests/floor/floor.json`.** That file is the shared pin and concurrent lanes collide on it. Write your count change into `floor-delta.json` in your own folder instead:

```json
{ "packet": "SP-083-orphan-construction-pool-residue", "unit": 0, "headless": 0, "reason": "one line naming the facts you added" }
```

Declare `0`/`0` if you add no tests; omitting the file is not the same as declaring zero. The land sums every packet's delta and applies one bump. `client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs` parses this PROMPT.md and enforces both halves mechanically: the `floorDelta` row must point at **your own** folder, and `fileScopeMustNotChange` must contain the shared pin. Both are already correct above. Do not reword the contract table.

## Review Level: 3 (Plan, Code, Final)

Three reasons, any one of which is sufficient. (1) Concurrency: you are adding a counted resource to a path already arbitrated by a `CompareExchange` latch and a leaf lock, and a decrement that fires twice is a cap that silently stops capping. (2) A live user-visible path: audio cues. A cap that fires when it should not is silence the user hears, on the ordinary healthy path, and the negative control matters as much as the bound. (3) The overflow behaviour has two candidate designs that are worse than the residue, both of which look like fixes.

## Steps

### Step 1: Census the growth, then decide the deliverable against the pre-authorized rule

Establish by reading and by executed facts, not by argument:

1. Every path that reaches `OrphanSafePlayerFactory.Create`, and for each, whether it can recur within one app session against a wedged endpoint. The five sites under Premise are the starting set, not the answer. **Say plainly if that list was wrong**, the way SP-072's census corrected its own packet from one construct-under-lock site to two.
2. Whether anything already bounds the outstanding count. Re-derive the two candidates recorded under Premise (SP-070 suppression, the SFX pool cap) yourself and state whether you reached the same conclusion. If you find a third candidate the authoring missed, that is the most valuable thing you can produce in this step.
3. The maximum number of outstanding abandoned constructions reachable in one app session against a permanently wedged endpoint, and the maximum number of simultaneously live factory instances (Decision B depends on it).

**DECISION A IS PRE-AUTHORIZED BOTH WAYS. Resolve it on your evidence; do not ask.**

- **If the census proves the count is already bounded per app session** by a mechanism that exists today, the deliverable is **the fact that pins that bound**, plus a named statement of the residual, plus the correction to the board row's premise in `record.md`. No new product mechanism. A bound that already holds does not need a second one, and adding one would be mechanism no path drives.
- **If it is not bounded**, land a bound on outstanding abandoned constructions.

Either way, the count must become something the suite **observes**, not something `record.md` asserts.

### Step 2: If a bound is owed, resolve where it accounts, then design its overflow behaviour

**DECISION B IS PRE-AUTHORIZED BOTH WAYS.** The factories are per-window-open, not singletons (three construction sites, cited above), so a per-instance cap of N bounds a *factory lifetime*, not necessarily a *session*. This is the same shape as SP-073's `_relaunchSpent` per-host-lifecycle versus per-app-session question, and it is decided by census item 3:

- **If the census proves the DtRH host and loom windows can be opened at most once per app session**, land the cap **per factory instance**. That is the simple design: instance state, no static, no cross-test bleed, and it bounds the session because the instance count is bounded.
- **If a host can be opened, closed, and opened again in one session** (assume it can until you have proven it cannot; the wedged-endpoint case is precisely a user retrying), a per-instance cap yields `opens x factories x N` and is not a session bound. Then choose between these two, and **both are approved**:
  - **(i) Process-wide accounting on the factory type**, *only if* it can be reset or injected deterministically. The suite runs parallel collections; a static counter that a test cannot reset is cross-test bleed, and it will surface as an unrelated packet's scheduling change (the exact failure the `ProcessEnvCollection` board row exists for). If you cannot make it deterministically resettable **without** widening File Scope, (i) is off the table.
  - **(ii) The per-instance cap, landed, with the per-session remainder named** as an explicit limit in `record.md` and stated as an owed board row. A bound on the dominant term with an honest name is a real deliverable here. It is not a WIP dodge, because the per-instance cap alone removes the one-per-cue growth that the row is actually about.

State which branch your evidence selected and why. Do not land both.

**The overflow behaviour has three candidates and two of them are worse than the residue:**

- **Block the caller until a slot frees.** Forbidden. That is SP-072 reverted, and the board row forbids it in its own text.
- **Skip or defer disposal to stay under the cap.** Forbidden. It converts a bounded residue into an unbounded leak plus an undisposed native object, and it breaks orphan invariant clause 3.
- **Refuse the new construction, typed, before starting any `Task.Run`.** This is the expected answer and it is cheap, because SP-072 already proved all five callers map `PlayerConstructionTimeoutException` to their existing refusal vocabulary (`SoundOutcome.Failed` at three sites, the logged silent no-op at two). Verify that claim at all five sites yourself rather than inheriting it. If a refusal at the cap needs to be distinguishable from a refusal at the budget, say so and justify it; a second exception type is allowed but is not free, and every caller must still handle it.

Whatever you land must keep SP-072's invariants intact: the abandoned construction never attaches, never plays, is disposed exactly once, disposal stays ordered under `_lifecycle`, and the ordinary path stays observably unchanged (same object, same volume, attach before return, same unwrapped exception surface, **zero new log lines on the healthy path**).

Take the pre-approach advisory gate here, with your census attached and both decision branches stated. Do not ask before you have the census.

### Step 3: The testability constraint, stated now rather than in review

This project has shipped the fixture-cannot-reach-the-mechanism failure three times (SP-067, SP-070, and the class SP-072 designed out in advance). It is being stated at authoring again:

- **The mechanism lives in `OrphanSafePlayerFactory` in `AudioSeams.cs`.** `SoundFlowAudioBackend` and `SoundFlowDtrhAudio` have zero headless coverage and cannot be constructed without a real engine and a real device. Anything you put inside either backend is unprovable and will be rejected. This is the same constraint that decided SP-072's placement (`port-lessons.md:237`).
- **It does not live in `SoundArbitration.cs`** either, on two independent grounds: that file is out of File Scope this wave, and a bound there would cover three of the five call sites and miss both DTRH ones.
- **The counter must be observable from `OrphanHarness`.** If your design puts the count somewhere the harness cannot read it, the design is wrong, not the harness. A public read-only property on the factory is fine and is not test-only scaffolding; it is the thing the row asks to be made countable.
- **If Decision B goes process-wide, resettability is a design requirement, not a testing afterthought.** Decide it before you write the mechanism.

### Step 4: Bind the behaviour, one source at a time

Every fact you add must be proven to bite by an **independent revert** of the single source line or clause it guards, run one at a time, with the tree restored byte-identically between reverts. Record the red count per revert in a matrix.

**The vacuity bar:**

- An assertion that passes with the mechanism reverted is not a fact.
- **Drive `OrphanHarness` in a loop that exceeds the cap by at least one.** A single abandonment cannot distinguish "bounded" from "bounded by one", and neither can a loop that stops exactly at the cap.
- **Observe the count while the wedge is still parked**, from inside the wedged operation where you can. SP-072's `disposeCountAtTeardownEnd` reads the dispose count while the teardown still holds the lock, and that is the shape to copy. An `Array.IndexOf` ordering sentinel is not, and is itself an open board row.
- **Prove the decrement, not just the increment.** Assert the outstanding count is at the cap with N constructions parked, then release the gate and assert it returns to zero. A cap that only ever counts up is a refuse-forever bug that a cap-only test cannot see, and it would silence audio for the rest of the session after one transient slow patch.
- **Keep a negative control.** An ordinary construction that completes inside its budget must not touch the counter, must not log, and must attach exactly as it does today.
- Do not weaken or reorder the existing five orphan facts to make room. If one of them genuinely conflicts with your mechanism, say so in `record.md` and state which; do not quietly rewrite it. The log-order comment at `AudioSeams.cs:301-307` is load-bearing for `Construction_CompletionRacesAbandonment_DisposedExactlyOnce`, and moving that line makes an existing pin pass vacuously.

### Step 5: Record

`record.md` carries: the census table; which branch of Decision A and which branch of Decision B your evidence selected, with the evidence; the revert matrix with red counts; a confirmation or correction of the two non-bounds recorded under Premise; and an honesty section naming what is **not** proven. At minimum the honesty section owes: the real native `AssetDataProvider` wedge is never exercised here (no audio device is inducible on this machine), Linux is unproven (zero WSL distros), and, if you took Decision B branch (ii), the per-session remainder.

`floor-delta.json` with your real counts, in the shape given in the Contract.

### Step 6: Verification

```
dotnet build client/CcpClient.sln -c Debug --nologo
```
```
node client/tests/floor/check-floor.mjs
```

Run them as **separate commands**. The worktree isolation guard refuses compound shell commands (`cd X && ...`), so chain nothing.

**Build immediately before the gate, every time.** The floor wrapper runs `dotnet test --no-build` (`client/tests/floor/check-floor.mjs:253`), so a stale `bin/` is reported as truth. That has already produced an observed 1022 against a tree containing 1018.

Your floor run will report a total that does **not** match the pin, because the pin is bumped at land from the summed deltas and not by you. That is expected and is not a failure of your work. READ THE PIN FROM `client/tests/floor/floor.json`, never from this packet: it has already gone stale twice (it said 1018; wave 30 made it 1022 and wave 31 made it 1028). Confirm that `observed == pin + your declared delta` on both projects, and state all four numbers in your report. If it does not reconcile, that is a real finding: do not adjust the delta to make it match.

## Completion Criteria

- The census is complete, and both Decision A and Decision B state their selected branch with evidence.
- Either the bound exists with a safe overflow behaviour, or the existing per-app-session bound is pinned by a fact that bites.
- The outstanding count is observable by the suite, at the cap and back down to zero.
- Every new fact bites under its own independent revert, matrix recorded.
- The five existing orphan facts are unchanged and green.
- The healthy path is observably unchanged, with a negative control proving it.
- `record.md` and `floor-delta.json` exist and are accurate.
- Build 0W/0E.
- SP-072's invariants intact, and its board row untouched.

## Do NOT

- **Make the caller wait again**, in any form, including a "short" wait or a wait only on the overflow path. The board row forbids it by name and it is SP-072 reverted.
- **Skip or defer orphan disposal** to satisfy a cap. Bounded residue becomes an unbounded leak plus an undisposed native object.
- **Pass a `CancellationToken` to the construction `Task.Run` and call the thread reclaimed.** A token cannot interrupt a blocked native constructor. It would complete the *task record* while the thread stays parked, producing a counter that reads bounded while the residue is not. That is the vacuous class in mechanism form, and it is the single most likely wrong answer here.
- **Use `Thread.Abort` or any equivalent.** It does not exist on .NET Core and there is no replacement.
- **Replace `Task.Run` with a dedicated `IsBackground` thread per construction.** It moves the leak off the pool without bounding it, trading pool starvation for unbounded thread creation, and it reads like a fix.
- **Count total abandonments instead of outstanding ones.** A monotonic counter refuses forever after a transient slow patch, which is permanent silence on a live user-visible path.
- **Route the construction timeout into SP-070's suppression path** (`NoteInitFailure` / `_suppressedUntilUtc`). It is out of File Scope, and it changes live behaviour: one slow file load would suppress all audio for 30 seconds.
- **Touch `SystemSoundClock` or `ISoundClock`** (`AudioSeams.cs:118-138`). The row has been paraphrased as if that were the mechanism. It is not, and it is correctly disposed today.
- **Call `ThreadPool.SetMaxThreads` / `SetMinThreads` or install a custom `TaskScheduler`.** Process-global, affects every subsystem, and bounds nothing about this row.
- Edit `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/src/CcpClient.Desktop/Audio/SoundArbitration.cs`, or anything under `client/docs/`, `.claude/`, `.spine/`, `.pi/`, or `ConditioningControlPanel/`.
- Close, edit, or claim the SP-071, SP-072, or SP-073 board rows. A packet that helpfully closes a neighbouring row has changed a mechanism nobody reviewed.
- Add a wall-clock wait. `client/tests/CcpClient.Tests/TestWait.cs` is the only approved helper (`TestWait.UntilSync`, `TestWait.Until`, `TestWait.InjectedBudget`). `Thread.Sleep`, bare `Task.Delay`, and `DateTime` / `Environment.TickCount64` polls fail the timing guard mechanically, and `TestTimingGuardTests` will red your run.
- Export `CCP_DATA_ROOT` process-wide. It makes the SP-057 pin skip and the floor goes blind.
- Leave a TODO, a placeholder, or a partially wired mechanism.

## Git Commit Convention

Conventional commits, `feat(SP-083): ...`. One coherent slice, one commit, no unrelated files. Leave the tree buildable at every commit. Commit on your own branch; do not merge, do not land, and do not touch the shared pin.

## Documentation Requirements

Your work almost certainly changes what `client/docs/async-lifecycle-fault-contract.md:62` (rule 7) should say, since that rule currently states the residue as a fact: *"the construction keeps running"*. If you land a bound, say so in `record.md` and quote the exact wording you believe is owed.

**Do not edit the contract document yourself.** Policy-touching text is applied by the orchestrator at land (SP-059 precedent; SP-071 and SP-072 both followed it, and rule 7 itself was landed that way).

Also record in `record.md`, for the orchestrator to apply at land: the board row at `client/docs/task-board.md:125` should be corrected to cite `AudioSeams.cs:236` (`Task.Run`) as the residue mechanism. It has been paraphrased as `ISoundClock.Schedule` / `AudioSeams.cs:133-137`, which is a different and correctly-behaving mechanism. Do not edit the board yourself.
