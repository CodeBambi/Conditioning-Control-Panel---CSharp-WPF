# SP-118 — Scheduler, the row that drives the engine instead of living in it

## Mission

Thirteen of fifteen rack rows are ported and **the session effect spine is complete**. Two rows remain, and SP-117 proved neither is an effect module. **Scheduler is the tractable one.**

It is the first thing this port has built that **owns the engine rather than running under it**: `SchedulerTimer_Tick` (`ConditioningControlPanel/MainWindow/MainWindow.StartStop.cs:601-637`) runs on a 30-second `DispatcherTimer` **while nothing is running**, and calls **`StartEngine()`** (`:618`, inside the `!_isRunning` branch opened at `:608`) and **`StopEngine()`** (`:628`, reset at `:634-636`), with a 60-second grace (`MainWindow.xaml.cs:616-620`, `:623-635`).

Your outcome: **a scheduler that starts and stops the session on a local-time window, owned at app lifetime — or a recorded finding naming what stops it.**

## THE CENTRAL TRAP: this thing can start a session the user did not ask for

Every module so far runs *inside* a session the user started. **This one starts one.** A defect here does not degrade an effect — **it puts a conditioning session on a user's screen unbidden**, or refuses to stop one.

So: **prove the guard, not the trigger.** The interesting facts are the ones where it must *not* fire — outside the window, already running, disabled, an unparseable time, the overnight wrap's closed end. SP-110 shipped a card the user could not dismiss; the same class of harm is available here and larger.

## The predicate, verbatim from source

`IsInScheduledTimeWindow` (`MainWindow.StartStop.cs:642-696`): seven day booleans on `DateTime.Now.DayOfWeek`; `TimeSpan.TryParse` with **16:00 / 22:00** fallbacks; and an **overnight wrap, half-open at the end**. **Verify every clause against source** — do not port the description.

## THE OTHER TRAPS

### 1. `ISessionClock` is `UtcNow` and this needs LOCAL time
`Session/SessionClock.cs:17-25` is `UtcNow` plus `Schedule`. **Widening `ISessionClock` is an equivalence claim over twelve module consumers and both presenters** — the standing rule in `client/docs/port-workflow.md` applies in full. Prefer a **new seam** over widening a shared one, and if you widen it, discharge the claim against every consumer by name.

### 2. App lifetime, not session lifetime
It must run when **no session exists**. `Lifecycle/**` is **open to you** — the first packet for which it is. Follow `CompositionRoot`'s existing ownership discipline; do not invent a second lifetime model.

### 3. Do not disturb thirteen modules or six capabilities
All capability folders are **closed**. Their facts pass unchanged.

### 4. Standing rules, both now in `port-workflow.md`
An equivalence claim is inadmissible until every consumer of the mutated symbol is enumerated by `grep` and discharged by name. A tolerance is exactly the size of the defect it will hide.

### 5. No wall-clock waits
`TestWait` only. A scheduler is the most tempting place in this port to sleep on a real clock — **inject the time.**

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Scheduling/**` (new), `client/src/CcpClient.Desktop/Lifecycle/**`, `client/src/CcpClient.Desktop/Session/**`, `client/src/CcpClient.Desktop/Views/**`, `client/tests/**`, `client/docs/wpf-surface-reachability.md` (divergences ONLY), and `spine-tasks/SP-118-scheduler/**` |
| Must not change | everything else, and specifically `client/src/CcpClient.Desktop/{Overlay,Input,Audio,Video,Pointer,Glyph}/**`, `client/src/CcpClient.Desktop/Effects/**`, `client/tests/floor/floor.json`, `client/tests/floor/check-floor.mjs`, `client/tests/floor/check-warnings.mjs`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

`Effects/**` is closed: **a scheduler that needs an effect changed is a finding.**

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-118-scheduler/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Scheduling` |
| fileScopeMustNotChange | `client/src/CcpClient.Desktop/Overlay/**`, `client/src/CcpClient.Desktop/Input/**`, `client/src/CcpClient.Desktop/Audio/**`, `client/src/CcpClient.Desktop/Video/**`, `client/src/CcpClient.Desktop/Pointer/**`, `client/src/CcpClient.Desktop/Glyph/**`, `client/src/CcpClient.Desktop/Effects/**`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-118-scheduler/record.md`, `spine-tasks/SP-118-scheduler/floor-delta.json` |

**Pin: 2123 unit / 125 headless.** Run `check-warnings.mjs` then `check-floor.mjs`, **both ALONE**. `sum-deltas` before deleting any delta file. **Keep every artifact inside your worktree.**

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint:** the predicate verified clause by clause against source; your time seam and why it is not a widening of `ISessionClock` (or the discharged equivalence claim if it is); your ownership point in `Lifecycle/**`; and **which refusals you will pin.**
2. Build it. `Available`/enabled state earned, never asserted.
3. **Pin the refusals first** — outside the window, already running, disabled, unparseable, the wrap's closed end.
4. Rack row with a truthful dot, or a recorded reason it has none (upstream passes what?).
5. **Prove it bites**, then sweep every predicate; discharge or withdraw every equivalence claim.
6. Divergences from D180.

## Completion Criteria

- The window predicate matches source clause by clause, including both fallbacks and the wrap's half-open end.
- It cannot start a session outside its window, when one is running, or when disabled — each pinned.
- Thirteen landed modules' facts pass unchanged.
- Both gates green; build 0 warnings / 0 errors.

## Do NOT

- Sleep on a real clock.
- Widen `ISessionClock` without discharging the equivalence claim by name.
- Touch `Effects/**` or any capability folder.
- Ship a scheduler that can start a session the user did not ask for.

## Git Commit Convention

Conventional commit, `feat(SP-118): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the clause-by-clause predicate verification, the time-seam decision and its reasoning, the refusal pins and the sweep; divergences in `client/docs/wpf-surface-reachability.md`.
