# SP-118 — plan (checkpoint 1, committed before the first product edit)

Branch `lane/SP-118-scheduler`, base `907ea805`. Pin **2123 unit / 125 headless**.

SP-116 committed its protocol before its first measurement and SP-117 made that the standard. This
file is that commit for SP-118: the predicate verified clause by clause against source, the time
seam and why it is not a widening, the ownership point, and **the refusals that will be pinned**.

---

## 0. The premise, checked rather than inherited

SP-117 §1.1 is this packet's scope, and every claim in it that this packet rests on was re-read
against source before a line was planned.

| SP-117's claim | verified |
|---|---|
| `SchedulerTimer_Tick` at `MainWindow/MainWindow.StartStop.cs:601-637` | **holds** — `:601` is the signature, `:637-638` the last branch body |
| it calls `StartEngine()` at `:618` inside the `!_isRunning` branch opened at `:608` | **holds**, verbatim |
| it calls `StopEngine()` at `:628`, resetting the flag at `:630` | **holds** — the packet says "reset `:634-636`"; `:630` is `_schedulerAutoStarted = false` inside the STOP branch and `:637-638` is the separate RESET branch. See §5 |
| 30 s `DispatcherTimer` at `MainWindow.xaml.cs:616-620` | **holds** — `:615` comment, `:616-619` the object with `Interval = TimeSpan.FromSeconds(30)`, `:620` the handler |
| 60 s grace at `:623-635` | **holds** — `:622-623` comment, `:624` `const int schedulerGracePeriodSeconds = 60`, `:627` the `Task.Delay`, `:634` `_schedulerTimer.Start()`, `:635` `CheckSchedulerOnStartup()` |
| the predicate is `:642-696` | **holds** |
| `ISessionClock` is `UtcNow` + `Schedule` (`Session/SessionClock.cs:17-25`) | **holds** |
| it needs none of the six capabilities | **holds** — nothing in `:601-696` touches a screen, a device or the OS beyond `DateTime.Now` |

**One correction to the packet's framing, and it is the reason §5 exists.** The packet describes the
tick as start/stop plus a reset. The tick has **four** exits, not three, and the first one is a
refusal that also suppresses the reset: `if (!settings.SchedulerEnabled) return;` (`:604`) runs
BEFORE `IsInScheduledTimeWindow()` is ever called, so a disabled scheduler does not clear its flags
and does not stop a session it started. That is ported, and pinned, as R1/R11.

---

## 1. THE PREDICATE, CLAUSE BY CLAUSE (`MainWindow.StartStop.cs:642-696`)

| # | line | clause, verbatim | what the port does |
|---|---|---|---|
| P1 | `:645` | `var now = DateTime.Now;` — **LOCAL**, `Kind == Local` (measured, §2) | `IScheduleClock.LocalNow`, a `DateTime` |
| P2 | `:648-659` | `now.DayOfWeek switch { Monday => SchedulerMonday, … Sunday => SchedulerSunday, _ => false }` — seven booleans, one per day | same switch, same seven members, same `_ => false` arm |
| P3 | `:661-665` | `if (!isDayActive) … return false;` — **the day gate short-circuits BEFORE either time is parsed** | the verdict conjoins `DayActive`, which is exactly `isDayActive ? inWindow : false`. The parse still runs so the panel can SHOW what it read, and it cannot change the verdict |
| P4 | `:667-671` | `if (!TimeSpan.TryParse(SchedulerStartTime, out var startTime)) startTime = new TimeSpan(16,0,0);` | `TimeSpan.TryParse`, the SAME BCL call and the same current-culture overload, fallback `16:00` |
| P5 | `:673-677` | `if (!TimeSpan.TryParse(SchedulerEndTime, out var endTime)) endTime = new TimeSpan(22,0,0);` | same, fallback `22:00` |
| P6 | `:679` | `var currentTime = now.TimeOfDay;` | same |
| P7 | `:682-685` | `if (endTime < startTime) inWindow = currentTime >= startTime \|\| currentTime < endTime;` — the overnight wrap | same, same operators, same order |
| P8 | `:687-690` | `else inWindow = currentTime >= startTime && currentTime < endTime;` | same |

**Four consequences of P7/P8 that are behaviour and are pinned, not paraphrased:**

* **Both branches are half-open: `[start, end)`.** The start boundary is IN (`>=`) and the end
  boundary is OUT (`<`). At exactly `endTime` the window is **closed** — that is the packet's
  "overnight wrap's closed end", and it is the same rule on the same-day branch.
* **`endTime == startTime` takes the SAME-DAY branch** (`<` is strict), so the window is
  `t >= x && t < x` — **always false, an EMPTY window, never a 24-hour one.** Measured, §2.
* **The fallbacks are NOT the defaults.** `AppSettings` ships `SchedulerStartTime = "00:00"` and
  `SchedulerEndTime = "22:00"` (`CCP.Core/Models/AppSettings.cs:2509-2522`), and both setters
  null-coalesce to those, so **null can never reach `TryParse`** — only a non-null unparseable
  string can. The `16:00` at P4 is reachable ONLY through user text. The `16:00`/`22:00` literals in
  `Features/SchedulerFeatureControl.xaml:47,56` are design-time text, overwritten from settings by
  `LoadFromSettings` on `Loaded` (`.xaml.cs:42-43`).
* **`TimeSpan.TryParse` is not a time-of-day parser.** `"8"` parses as **8 DAYS** and `"25:00"`
  fails. Both measured, §2. Ported unchanged — and SURFACED on the panel, which is where this
  packet reduces the harm instead of changing the predicate.

## 2. What was measured rather than remembered

`TimeSpan.TryParse` and the boundary arithmetic were run on this machine's .NET 10 SDK before
anything rested on them:

```
[16:00] ok=True 16:00:00      [8]      ok=True 8.00:00:00  (192 h — EIGHT DAYS)
[00:00] ok=True 00:00:00      [25:00]  ok=False            (→ fallback)
[9:5]   ok=True 09:05:00      [-01:00] ok=True -01:00:00
[]      ok=False (→ fallback) [24:00]  ok=False (→ fallback)
end<start? False  ⇒ equal start/end takes the SAME-DAY branch ⇒ inWindow at 12:00 = False
DateTime.Now.Kind = Local
```

## 3. THE TIME SEAM — a NEW one, and `ISessionClock` is not touched

**`Scheduling/ScheduleClock.cs`: `IScheduleClock { DateTime LocalNow; IDisposable Schedule(TimeSpan, Action); }`**
plus `SystemScheduleClock`, which returns `DateTime.Now` and contains a faulting callback the way
`SystemSessionClock` does (SP-101: an exception escaping a pool-thread timer callback is unhandled
and kills the process).

**Why not widen `ISessionClock`.** Two reasons, and the second is the stronger one.

1. **The enumeration, done before the decision.** `grep -rn ISessionClock client/{src,tests}`:
   twelve module/presenter consumers in `Effects/**` (`AudioCueEffect`, `BouncingTextSurfacePresenter`,
   `BrainDrainEffect`, `BubbleCountEffect`, `BubblePopSurfacePresenter`, `FlashImagesEffect`,
   `FlashSurfacePresenter`, `IntensityRampEffect`, `LockCardEffect`, `MandatoryVideoEffect`,
   `MindWipeEffect`, `OverlaySurfaceSet`, `PinkFilterSurfacePresenter`, `SpiralSurfacePresenter`,
   `SubliminalsEffect`, `SubliminalSurfacePresenter`, `VideoSurfacePresenter`), the shared base
   `PacedSessionEffect`, the composition root's `SessionClockFactory`, `SessionParticipant`, and
   **~24 hand-written implementations in `client/tests/**`** — every one of which would have to grow
   a member. `Effects/**` is CLOSED to this packet, so a widening is not even reachable: it would be
   a File Scope violation before it was an equivalence claim.
2. **They are different clocks, and a session module must never have the local one.** `UtcNow` is
   monotone across a DST transition and a timezone change; `DateTime.Now` is not. A paced effect
   that read local time would fire twice or not at all at 02:00 on a transition night. The scheduler
   MUST read local time, because the user typed "16:00" meaning the clock on their wall. Keeping
   them apart is a correctness property, not scope avoidance — and it is exactly the reasoning
   `Session/SessionClock.cs:7-13` already gives for declaring `ISessionClock` separately from
   `Audio.ISoundClock`.

**No equivalence claim is made anywhere in this packet.** The enumeration above is recorded because
`port-workflow.md:283-304` requires it of the claim I would otherwise have had to make.

## 4. THE OWNERSHIP POINT — `CompositionRoot.DefaultParticipants`, one more `IBackgroundParticipant`

No second lifetime model. `Scheduling/SchedulerParticipant.cs` implements the existing
`IBackgroundParticipant` and is registered **last** in `CompositionRoot.DefaultParticipants`, after
`SessionParticipant`, holding a reference to it. That buys every property the packet needs from
machinery that already exists:

* **phase 3 starts it** (`ApplicationHost.StartParticipantsAsync`), after the session's preset load,
  because registration order IS start order;
* **teardown stops it FIRST** (reverse order), so the poll is dead before the session tears down;
* its store's flush goes in the **reserved pre-drain slot** (`CompositionRoot.Build`), like the other
  four;
* its poll is an **owned generation** (`infra.OwnerFor("Scheduler")`), so the host's
  cancel-and-drain kills it too — which is the port's analogue of WPF's
  `HasShutdownStarted` guard at `MainWindow.xaml.cs:629` and of `_schedulerTimer?.Stop()` at
  `MainWindow.WindowChrome.cs:166`.

The participant owns the 60 s grace one-shot and re-arms a 30 s one-shot after every tick (the port
has no repeating-timer seam; `ScheduledFire` + one-shot is the landed shape). It does **not** own the
decision: that is `Scheduling/SessionScheduler.cs`, which is pure state plus a `SessionEngine`.

**No double stands in for the engine.** `SessionScheduler` drives the REAL `SessionEngine`; a unit
test constructs one with an empty effect list and a real preset store. SP-110's review found a
defect that survived because "the test double diverged from the product exactly where the bug
lived", and the cheapest way not to repeat that is not to have a double.

## 5. THE TICK, CLAUSE BY CLAUSE (`:601-639`) — four exits, in order

```
:604  if (!settings.SchedulerEnabled) return;                                   → R1  (and R11)
:606  bool inWindow = IsInScheduledTimeWindow();
:608  if (inWindow && !_isRunning && !_schedulerAutoStarted && !_manuallyStoppedDuringSchedule)
:613-619       MinimizeToTray(); ShowNotification(...); StartEngine(); _schedulerAutoStarted = true;
:622  else if (!inWindow && _isRunning && _schedulerAutoStarted)
:628-631       StopEngine(); _schedulerAutoStarted = false; ShowNotification(...);
:635  else if (!inWindow)
:637-638       _schedulerAutoStarted = false; _manuallyStoppedDuringSchedule = false;
```

The two flags are written from three other places, all verified:

* `_manuallyStoppedDuringSchedule = true` at `:100` — the user pressing STOP **while enabled and in
  the window** (`:98`);
* `_manuallyStoppedDuringSchedule = false` at `:107` — the user pressing START;
* `MainWindow.Settings.cs:363-368` — **PROVABLY DEAD, and not by its comment.** `:363` is
  `var schedulerWasEnabled = s.SchedulerEnabled;` and `:364` is
  `if (s.SchedulerEnabled && !schedulerWasEnabled)` — `b && !b`, a tautological false. So
  `CheckSchedulerAfterSettingsChange` (`:583-599`) is unreachable in the shipping product, and the
  shipping product's own note agrees ("enabling the scheduler now arms within one 30s
  SchedulerTimer_Tick instead of instantly",
  `Views/Controls/Studio/SchedulerRackPanel.xaml.cs:14-18`). **The port ports the LIVE behaviour**
  — enabling arms within one poll — and records the dead method as a divergence rather than
  reproducing code that cannot run.

## 6. THE REFUSALS I WILL PIN

The trigger is one fact. These are the guard, and they are the packet.

| # | it must NOT | source |
|---|---|---|
| R1 | act at all while the enable is off — **including not clearing its flags and not stopping a session it started** | `:604` returns before everything |
| R2 | start outside the window | `:608` `inWindow &&` |
| R3 | start while a session is running | `:608` `!_isRunning` |
| R4 | re-start after the user manually stopped inside the window, until the window closes | `:608` `!_manuallyStopped…`, set `:98-101`, cleared only at `:107` and `:638` |
| R5 | start on a day whose box is unticked, whatever the time says | `:661-665` |
| R6 | treat an unparseable time as "no window" — it falls back to 16:00 / 22:00 | `:667-677` |
| R7 | be in the window at exactly `endTime`, on EITHER branch (half-open) | `:684`, `:689` |
| R8 | read `start == end` as all day — it is an EMPTY window | `:682` `<` is strict |
| R9 | act during the 60 s start-up grace: nothing is on the clock yet | `MainWindow.xaml.cs:624-635` |
| R10 | act after teardown has begun | `:629` `HasShutdownStarted`; `WindowChrome.cs:166` |
| R11 | stop a session it did not start | `:622` `_schedulerAutoStarted` |
| R12 | mark a session auto-started when the engine refused the start | port-only; `SessionEngine.Start()` returns false |
| R13 | start twice for one window opening | `:608` `!_schedulerAutoStarted` |

R12 is the one WPF does not have: `StartEngine()` (`:161`) has **no `_isRunning` guard of its own**,
so WPF's `CheckSchedulerOnStartup` (`:570-580`) can re-arm every service over a session the user
started during the grace, and then marks it auto-started so the window's close stops it. The port's
`SessionEngine.Start()` returns `false` when `Running`, and the scheduler will not claim what it did
not do. Divergence.

## 7. The rack row, and what upstream passes

Upstream's entry is `Add("scheduler", "📅", null, "Scheduler", "section_scheduler", …, () =>
App.Settings?.Current?.SchedulerEnabled, toggle: () => FlipMasterCheckBox(PanelScheduler?.Inner.ChkEnabled))`
(`Views/Tabs/StudioTabView.xaml.cs:535-537`), first in the TIMING group, before Intensity Ramp
(`:538`). So: **a dot IS passed** (unlike Visuals' `null` at `:496`), it reads the ENABLE, and the
row takes the right-click, which flips the panel's own master box rather than driving a service
(`:531-534` says so in as many words).

The port's dot has three states and is a claim about what the module can DO, so:

```
Off   = the enable is off, OR no tick is on the clock (the grace has not elapsed / teardown)
Armed = enabled, a tick is really on the clock, and the local clock is OUTSIDE the window
Live  = enabled, a tick is really on the clock, and the local clock is INSIDE the window
```

`Off` during the grace is a divergence from WPF's checkbox-shaped dot and it is the honest answer:
for those 60 seconds the scheduler will not act, and the panel says why in words.

## 8. What is NOT ported, and why (findings, not omissions)

* **The tray balloons** ("Scheduler Active" `:616`, "Scheduled session ended." `:631`).
  `ShellTray` exposes no arbitrary-notification entry and `Tray/**` is outside this packet's File
  Scope. Recorded as an absence on the panel and in the ledger. **The minimize IS ported** —
  `ShellTray.Duck()` is public and is the port's landed analogue of `MinimizeToTray()`.
* **`CheckSchedulerAfterSettingsChange`** — dead upstream (§5), so porting it would ship a path the
  shipping product cannot reach.
* **`SchedulerDurationMinutes` / `SchedulerMultiplier` / `SchedulerLinkAlpha`**
  (`AppSettings.cs:2461-2478`) are read by the intensity RAMP, not by the scheduler, and are not on
  the shipping scheduler panel (`Features/SchedulerFeatureControl.xaml` has nine controls: enable,
  two times, seven days). Out of scope by upstream's own layout.

## 9. Files

**New:** `Scheduling/ScheduleClock.cs`, `Scheduling/SchedulerPresetDocument.cs`,
`Scheduling/ScheduleWindow.cs`, `Scheduling/SessionScheduler.cs`,
`Scheduling/SchedulerParticipant.cs`, `Scheduling/SchedulerReasonCodes.cs`,
`Views/Pages/SchedulerPanelNotices.cs`; tests `SchedulerWindowTests.cs`, `SchedulerModuleTests.cs`.

**Changed:** `Lifecycle/CompositionRoot.cs` (registration + flush slot + a clock factory seam),
`Views/MainWindow.axaml.cs` (the START button tells the scheduler, and the shell ducks on an
auto-start), `Views/Pages/StudioPage.axaml` + `.axaml.cs` (the row, the panel, the dot),
`client/docs/wpf-surface-reachability.md` (§SP-118, from D180), plus the two participant-count
assertions and the two rack-order assertions the new row moves.

**Untouched:** all six capability folders, `Effects/**`, `Persistence/**`, `Tray/**`,
`client/tests/floor/**`.
