# SP-073 — Bound the teardown give-up residue across AUTOMATIC host closes

## Mission

SP-071 (wave 28) stopped the UI thread blocking on a wedged native audio teardown. It did that by moving the backend teardown onto a named `IsBackground` thread and giving the caller a bounded `Join`; on expiry the caller logs one typed give-up line and returns **without touching `_backend`**. That was correct and the alternative was worse.

It left a residue, and the SP-071 land consult named it: **after a give-up, the old backend stays alive and one background thread persists until the wedged native call returns.** The packet framed that residue as "bounded by user close actions". **That framing is false, and the wave-28 land already corrected it on the board** — the DtRH host also closes from automatic paths.

Your outcome: **the residue is either PROVEN bounded per app session by a fact, or BOUNDED by a mechanism.** Which one you deliver is decided in Step 1 by a census against a rule that is pre-authorized both ways (below). What you may not deliver is a third thing: a caller that waits again.

This is the port's first packet under the Claude Code engine and the first under the floor-delta mechanism. Both are noted in the contract; neither changes what the code must do.

## Dependencies

SP-071 (landed, integrate `d1c69617`). SP-070 (`_initLock`), SP-072 (`OrphanSafePlayerFactory`) are adjacent but out of scope.

## Context to Read First

Verified by the orchestrator at authoring — every line below was opened and confirmed, not transcribed from the board:

- `client/src/CcpClient.Desktop/Audio/SoundArbitration.cs:78-79` — `TeardownBudget`, default 2s, and the doc comment stating the give-up contract.
- `:163` `_teardownState`; `:1114-1154` — the backgrounded teardown thread (`IsBackground = true, Name = "SoundArbitrationTeardown"`), the `Interlocked.CompareExchange` 3-state that makes disposal exactly-once and pairs the give-up/completion lines, the bounded `Join(_options.TeardownBudget)`, and the give-up line at `:1151`.
- The five **automatic** close paths, each confirmed to exist:
  - `client/src/CcpClient.Desktop/Features/Dtrh/DtrhLaunchCoordinator.cs:104` — `dead.CloseForRecovery();`
  - `client/src/CcpClient.Desktop/Features/Dtrh/DtrhLaunchCoordinator.cs:120` — `HostWindow?.CloseForRecovery();`
  - `client/src/CcpClient.Desktop/Features/Dtrh/DtrhHostWindow.axaml.cs:777` — `Close();` (forced-exit timer)
  - `client/src/CcpClient.Desktop/Features/Dtrh/DtrhHostWindow.axaml.cs:1037` — `Close();` (page `ExitDone`)
  - `client/src/CcpClient.Desktop/Features/Dtrh/DtrhHostWindow.axaml.cs:1059` — `Close();` (page `BootError`)
- `client/src/CcpClient.Desktop/Features/Dtrh/DtrhWatchdog.cs:55` — `private bool _relaunchSpent;  // WPF _relaunchedOnce (:39)`, and `:139-141` where it is spent. This is the reason the count may already be bounded **per host lifecycle**; it is NOT obviously a bound **per app session**, and that gap is the whole packet.
- `client/tests/CcpClient.Tests/SoundArbitrationTests.cs` — the SP-071 pins and the parked-probe fixture you will drive in a loop. Read how the fake parks before writing anything.
- `client/docs/async-lifecycle-fault-contract.md` §5, §5.6, §5.7 — the UI dispatch boundary is **post-only** by construction. §5.6 is SP-071's wording; §5.7 is SP-072's.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Audio/SoundArbitration.cs`, `client/tests/CcpClient.Tests/SoundArbitrationTests.cs`, `spine-tasks/SP-073-teardown-residue-bound/**` |
| Must not change | everything else, and specifically the files named in the contract below |

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-073-teardown-residue-bound/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Audio/SoundArbitration.cs` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/docs/task-board.md`, `ConditioningControlPanel/**`, `client/docs/**`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-073-teardown-residue-bound/record.md`, `spine-tasks/SP-073-teardown-residue-bound/floor-delta.json` |

**You do not edit `client/tests/floor/floor.json`.** That file is the shared pin and concurrent lanes collide on it. Write your count change into `floor-delta.json` in your own folder instead:

```json
{ "packet": "SP-073-teardown-residue-bound", "unit": 0, "headless": 0, "reason": "one line naming the facts you added" }
```

Declare `0`/`0` if you add no tests; omitting the file is not the same as declaring zero. The land sums every packet's delta and applies one bump. `client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs` enforces both halves of this and will fail your run if the row or the disclaimer is missing.

## Review Level: 3 (Plan, Code, Final)

## Steps

### Step 1: Census the close paths, then decide the deliverable against the pre-authorized rule

Establish, by reading and by executed facts rather than by argument:

1. Every path that reaches `SoundArbitration.Dispose`, and for each, whether it can recur **within one app session**. The five automatic paths above are the starting set, not the answer — find the rest yourself and say if the list was wrong.
2. Whether `_relaunchSpent` actually bounds the count per app session, or only per host lifecycle. A DtRH host that is opened, closed, and opened again is the case that matters. **State plainly which one you proved.**
3. The maximum number of outstanding backgrounded teardowns reachable in one app session against a permanently wedged endpoint.

**THE DECISION RULE IS PRE-AUTHORIZED BOTH WAYS. Resolve it on your evidence; do not ask.**

- **If the census proves the count is bounded per app session** by a mechanism that already exists, the deliverable is **the fact that pins it** plus a named statement of the residual. No new product mechanism. A bound that already holds does not need a second one, and adding one would be mechanism no path drives.
- **If it is not bounded**, land a bound on outstanding backgrounded teardowns.

Either way, the count must become a thing the suite observes, not a thing the record asserts.

### Step 2: If a bound is owed, design its overflow behaviour first

This is the hard part and the reason this packet is Review Level 3. A cap has three candidate overflow behaviours and two of them are worse than the residue:

- **Block the caller until a slot frees** — forbidden. That is SP-071 reverted, and the row says so explicitly.
- **Skip disposal when the cap is hit** — forbidden. That converts a bounded residue into an unbounded leak plus an undisposed native object.
- Anything you do land must keep SP-071's invariants intact: exactly-once disposal, the give-up line never touching `_backend`, and the give-up/completion log pair.

Take the pre-approach advisory gate here, with your census attached and your proposed overflow behaviour stated. Do not ask before you have the census.

### Step 3: Bind the behaviour, one source at a time

Every fact you add must be proven to bite by an **independent revert** of the single source line it guards, run one at a time, restoring the tree byte-identically between reverts. Record the red count per revert.

**The vacuity bar, stated because this run has hit it three times (SP-067, SP-070, and the class SP-072 designed out):** an assertion that passes with the mechanism reverted is not a fact. Assert from **inside** the wedged operation where you can — SP-072's `disposeCountAtTeardownEnd` reads the count while the teardown still holds the lock, and that is the shape to copy, not an `Array.IndexOf` ordering sentinel, which is itself an open board row.

Drive the parked-probe fixture in a **loop** — open/close cycles against a permanently parked probe — because a single cycle cannot distinguish "bounded" from "bounded by one".

### Step 4: Record

`record.md`: the census table, which branch of the decision rule your evidence selected and why, the revert matrix with red counts, and an honesty section naming what is NOT proven. `floor-delta.json` with your real counts.

### Step 5: Verification

```
dotnet build client/CcpClient.sln -c Debug --nologo
```
```
node client/tests/floor/check-floor.mjs
```

Run them as **separate commands**. The worktree isolation guard refuses compound shell commands (`cd X && ...`), so chain nothing.

Your floor run will report a total that does NOT match the pin, because the pin is bumped at land from the summed deltas and not by you. That is expected and is not a failure of your work: confirm the observed total equals `pin + your declared delta`, and state both numbers in your report.

## Completion Criteria

- The census is complete and its decision-rule branch is stated with evidence.
- Either the bound exists with a safe overflow behaviour, or the per-app-session bound is pinned by a fact that bites.
- Every new fact bites under its own independent revert.
- `record.md` and `floor-delta.json` exist and are accurate.
- Build 0W/0E.
- SP-071's invariants are intact and its board row is untouched.

## Do NOT

- Make the caller wait again, in any form, including a "short" wait.
- Skip or defer disposal to satisfy a cap.
- Edit `client/tests/floor/floor.json`, `client/docs/task-board.md`, or anything under `client/docs/`, `.claude/`, `.spine/`, `.pi/`, or `ConditioningControlPanel/`.
- Close, edit, or claim the SP-071 board row, or the `CreatePlayer` / orphan rows. A packet that "helpfully" closes a neighbouring row has changed a mechanism nobody reviewed.
- Add a wall-clock wait. `client/tests/CcpClient.Tests/TestWait.cs` is the only approved helper; `Thread.Sleep`, bare `Task.Delay`, and `DateTime`/`Environment.TickCount64` polls fail the timing guard mechanically — `TestTimingGuardTests` will red your run.
- Export `CCP_DATA_ROOT` process-wide.
- Leave a TODO, a placeholder, or a partially wired mechanism.

## Git Commit Convention

Conventional commits, `feat(SP-073): ...`. One coherent slice, no unrelated files. Leave the tree buildable at every commit. Commit your own work on your branch; do not merge, do not land, and do not touch the shared pin.

## Documentation Requirements

If your work changes a fact stated in `client/docs/async-lifecycle-fault-contract.md`, say so in `record.md` and quote the wording you believe is owed. **Do not edit the contract document yourself** — policy-touching text is applied by the orchestrator at land (SP-059 precedent, and SP-071/SP-072 both followed it).
