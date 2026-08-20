# SP-118 — record

Branch `lane/SP-118-scheduler`, base `907ea805`.
Floor: pin **2123 unit / 125 headless**; observed **2191 unit / 133 headless**, zero failures;
declared **+68 unit / +8 headless** (`floor-delta.json`). 2123 + 68 = 2191 and 125 + 8 = 133,
confirmed by `node client/tests/floor/sum-deltas.mjs --check --packets SP-118-scheduler`. The floor
run therefore REPORTS a violation against the pin, which is the expected shape: the orchestrator
sums the deltas and applies one bump. Two skips, both pre-existing
(`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`,
`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`); none added, none widened.
Build: 0 errors, 0 warnings (`check-warnings.mjs`, forced non-incremental, 4 projects).
`client/tests/floor/floor.json` was never opened.

> **The plan was committed before the first product edit**, at
> `spine-tasks/SP-118-scheduler/plan.md`, commit `0429ba71`. SP-116 committed its protocol before
> its first measurement and SP-117 made that the standard. §1 below is the summary; the
> clause-by-clause verification itself is in `plan.md` and is not restated in full here.

---

## 0. WHAT THIS ROW IS, and why the whole packet is weighted towards refusals

**This is the first thing the port has built that OWNS the engine rather than running under it.**
Thirteen ported rows are modules inside a session someone started. This one runs on a 30 s timer
while nothing is running and calls `StartEngine()` / `StopEngine()`
(`ConditioningControlPanel/MainWindow/MainWindow.StartStop.cs:601-639`, `:618`, `:629`).

So a defect here does not degrade an effect. **It puts a conditioning session on a user's screen
unbidden, or refuses to end one.** SP-110 shipped a card the user could not dismiss and a reviewer
caught it; the same class is available here and it is larger, because the harm needs no gesture at
all to trigger and can repeat every thirty seconds for the length of a window.

**Thirteen refusals are pinned by name** (`SchedulerModuleTests`, R1-R13) and the whole of §1 is
the predicate that decides them, verified clause by clause against source rather than ported from
its description.

---

## 1. THE PREDICATE, CLAUSE BY CLAUSE (`MainWindow.StartStop.cs:642-696`)

Every line number below was read before anything rested on it. The full table is `plan.md` §1.

| # | line | clause | port |
|---|---|---|---|
| P1 | `:645` | `var now = DateTime.Now;` — **LOCAL** (`Kind == Local`, measured) | `IScheduleClock.LocalNow` |
| P2 | `:648-659` | seven per-day booleans on `now.DayOfWeek`, plus an unreachable `_ => false` | `ScheduleWindow.IsDayActive`, same seven, same default arm |
| P3 | `:660-664` | `if (!isDayActive) return false;` — **the day gate short-circuits BEFORE either parse** | the verdict conjoins `DayActive`, which is exactly `isDayActive ? inWindow : false` |
| P4 | `:667-671` | `TimeSpan.TryParse(SchedulerStartTime, …)`, fallback `new TimeSpan(16,0,0)` | the SAME BCL call on the same current-culture overload; `StartFallback = 16:00` |
| P5 | `:673-677` | same for the end, fallback `new TimeSpan(22,0,0)` | `EndFallback = 22:00` |
| P6 | `:679` | `var currentTime = now.TimeOfDay;` | same |
| P7 | `:683-686` | `if (endTime < startTime) inWindow = currentTime >= startTime \|\| currentTime < endTime;` | same operators, same order |
| P8 | `:688-691` | `else inWindow = currentTime >= startTime && currentTime < endTime;` | same |

### 1.1 The four consequences that are behaviour, and are pinned at the tick

* **Both branches are half-open, `[start, end)`.** Start inclusive (`>=`), **end exclusive (`<`)** —
  the packet's "overnight wrap's closed end", and the same rule on the same-day branch.
  `TheSameDayWindowIsHalfOpen_TheStartTickIsIN_AndTheENDTickIsOUT` and
  `TheOvernightWrapIsHalfOpenTOO_AndItsENDIsTheCLOSEDOne` assert at `end - 1 tick`, `end`, and
  `end + 1s`, so a `<=` cannot hide between samples.
* **`start == end` is an EMPTY window, never a whole day.** `<` at `:683` is strict, so an equal
  pair takes the SAME-DAY branch and the test becomes `t >= x && t < x`. Swept as **M-e**
  (`end <= start`), which turns it into an all-day window; caught.
* **The fallbacks are NOT the defaults.** `AppSettings` ships `SchedulerStartTime = "00:00"` and
  `SchedulerEndTime = "22:00"` (`CCP.Core/Models/AppSettings.cs:2510`, `:2517`) and both setters
  null-coalesce to those (`:2514`, `:2521`), so **a null can never reach `TryParse`** — only
  non-null unparseable text can, which is the only way the **16:00** at P4 is reachable. The
  `16:00`/`22:00` literals in `Features/SchedulerFeatureControl.xaml:47,56` are design-time text
  that `LoadFromSettings` overwrites on `Loaded` (`.xaml.cs:43-44`).
* **`TimeSpan.TryParse` is not a time-of-day parser**, measured on this runtime rather than
  remembered: `"8"` parses SUCCESSFULLY as **8 days** (192 h), `"25:00"` and `"24:00"` fail into the
  fallback, `"-01:00"` is minus one hour, `"9:5"` is 09:05. A user who types `8` meaning eight in
  the morning gets a start greater than any `TimeOfDay`, so with an end of 22:00 the pair goes down
  the OVERNIGHT branch and the window silently becomes **00:00-22:00**. **The port does not change
  the parse** — that would change when sessions start — and instead carries the reading out to the
  panel, which is where the harm is reduced. D184.

### 1.2 The tick's FOUR exits, and the packet's framing corrected

The packet describes start, stop and a reset. There are **four** exits and the first one is a
refusal that also suppresses the reset:

```
:604  if (!settings.SchedulerEnabled) return;                        exit 1 — before any clock read
:608  inWindow && !_isRunning && !_schedulerAutoStarted && !_manuallyStoppedDuringSchedule → START
:622  !inWindow && _isRunning && _schedulerAutoStarted                                     → STOP
:635  !inWindow                                                       → reset BOTH flags
```

Two consequences of that ordering are surprising enough to be pinned rather than discovered later:
a disabled scheduler **does not stop the session it started** (D186, `R1b`), and
`_schedulerAutoStarted` records that **this window's OPENING has been served** rather than that the
current session is the scheduler's (D187, inside `R4c`).

**And one shipping method is provably dead.** `CheckSchedulerAfterSettingsChange` (`:583-599`) is
reached only from `MainWindow.Settings.cs:363`, whose condition is
`s.SchedulerEnabled && !schedulerWasEnabled` — with `schedulerWasEnabled` assigned from
`s.SchedulerEnabled` on the line immediately above (`:362`). That is `b && !b`: a tautological
false. The shipping product's own note reaches the same conclusion from the other direction
("enabling the scheduler now arms within one 30s SchedulerTimer_Tick instead of instantly",
`Views/Controls/Studio/SchedulerRackPanel.xaml.cs:14-18`). **The port ports the LIVE behaviour and
records the dead method** (D181) rather than shipping a path upstream cannot reach.

---

## 2. THE TIME SEAM — a NEW one, and `ISessionClock` was not touched

`Scheduling/ScheduleClock.cs` declares `IScheduleClock { DateTime LocalNow; IDisposable Schedule(…); }`
with `SystemScheduleClock` over `System.Threading.Timer`, carrying `SystemSessionClock`'s
fault-containment discipline verbatim (SP-101: an exception escaping a pool-thread timer callback is
UNHANDLED and kills the process — and this callback runs while nothing else is happening, in an app
the user left running on purpose, so the crash would look like the app simply vanishing overnight).

**Why not widen `ISessionClock`, in the order the standing rule requires — enumeration first.**
`grep -rn ISessionClock client/{src,tests}` answers with:

* **`Effects/**` (17 files):** `AudioCueEffect`, `BouncingTextSurfacePresenter`, `BrainDrainEffect`,
  `BubbleCountEffect`, `BubblePopSurfacePresenter`, `FlashImagesEffect`, `FlashSurfacePresenter`,
  `IntensityRampEffect`, `LockCardEffect`, `MandatoryVideoEffect`, `MindWipeEffect`,
  `OverlaySurfaceSet`, `PinkFilterSurfacePresenter`, `SpiralSurfacePresenter`, `SubliminalsEffect`,
  `SubliminalSurfacePresenter`, `VideoSurfacePresenter`;
* **`Session/**`:** `PacedSessionEffect` (the shared base), `SessionClock.cs` itself,
  `SessionParticipant`, and `ScheduledFire`'s doc contract;
* **`Lifecycle/**`:** `CompositionRoot.SessionClockFactory`;
* **`client/tests/**` (~24 files):** each with its own hand-written implementation.

**Two reasons, and the second is the stronger one.**

1. **`Effects/**` is CLOSED to this packet**, so a widening was not merely inadvisable, it was
   unreachable: it would have been a File Scope violation before it was an equivalence claim.
2. **They are different clocks and a session module must never have the local one.** `UtcNow` is
   monotone across a daylight-saving transition and a timezone change; `DateTime.Now` is not. A
   paced effect reading local time would fire twice, or not at all, at 02:00 on a transition night.
   The scheduler MUST read local time, because the user typed "16:00" meaning the clock on their
   wall. Keeping them apart is a correctness property, and it is the reasoning
   `Session/SessionClock.cs:7-13` already gives for declaring `ISessionClock` separately from
   `Audio.ISoundClock`.

**No equivalence claim is made anywhere in this packet.** The enumeration above is recorded because
`port-workflow.md`'s rule requires it of the claim I would otherwise have had to make; the sweep
(§5) asserts no equivalent mutants at all.

---

## 3. THE OWNERSHIP POINT — `CompositionRoot.DefaultParticipants`, one more `IBackgroundParticipant`

No second lifetime model was invented. `Scheduling/SchedulerParticipant.cs` implements the existing
`IBackgroundParticipant` and is registered **last** in `CompositionRoot.DefaultParticipants`, after
`SessionParticipant` and against **that participant's own `Engine`** (the session is built into a
local first, so the rack row and the scheduler can never drive two different sessions). That buys
four properties from machinery that already exists:

| property | mechanism | WPF's counterpart |
|---|---|---|
| phase 3 starts it AFTER the session's preset load | registration order IS start order (`ApplicationHost.StartParticipantsAsync`) | the timer is a `MainWindow` field created during window construction (`MainWindow.xaml.cs:206`, `:616-620`) |
| teardown stops it FIRST | participant stop is reverse order | `_schedulerTimer?.Stop()` at close (`MainWindow.WindowChrome.cs:166`) |
| a tick in flight cannot act during teardown | an owned generation (`infra.OwnerFor("Scheduler")`) plus the `_running` re-check inside the callback | `if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;` (`MainWindow.xaml.cs:629`) |
| its ten settings reach disk | the reserved pre-drain flush slot in `CompositionRoot.Build` | `SaveSettings()` on close (`MainWindow.WindowChrome.cs:163`) |

The participant owns the **60 s grace** one-shot and re-arms a **30 s** one-shot after every tick
(the port has no repeating-timer seam; `ScheduledFire` + a one-shot is the landed shape, and the
callback clears its slot with `CompareExchange` so a superseded tick does nothing at all). It does
**not** own the decision: that is `Scheduling/SessionScheduler.cs`, which is state plus a real
`SessionEngine`.

**The arm order is WPF's, and it is deliberate:** the next tick goes on the clock BEFORE the
decision runs (`MainWindow.xaml.cs:634-635` is `_schedulerTimer.Start(); CheckSchedulerOnStartup();`),
so a decision that throws cannot silently end the schedule.

**No double stands in for the engine anywhere in this packet.** `SessionScheduler` drives a real
`SessionEngine` over a real `PersistenceStore<SessionPresetDocument>` with an empty effect list.
SP-110's review found a user-harm defect that a whole mutation sweep had missed because "the test
double diverged from the product exactly where the bug lived"; the cheapest way not to repeat that
is not to have a double.

---

## 4. THE REFUSALS I PINNED

| # | it must NOT | fact | source |
|---|---|---|---|
| R1 | act at all while the enable is off — **and it does not even read the clock** | `R1_ADisabledScheduler_DoesNotEvenLOOKAtTheClock` (the injected clock counts its own reads, so this pins the clause ORDER, not just the outcome) | `:604` |
| R1b | stop the session it started, once switched off | `R1b_…WhichIsUpstreamsOwnOrdering` | `:604` before `:622` |
| R2 | start outside the window | `R2_OutsideTheWindow_NoNumberOfTicksStartsAnything` (16 polls) | `:608` |
| R3/R11 | start while a session is running — **or CLAIM it**, which is why the window's close leaves it alone | `R3andR11_ASessionTheUserStarted_IsNeitherTouchedNorCLAIMED` | `:608`, `:622` |
| R4 | re-start after the user pressed STOP inside the window | `R4_AfterTheUserPressesSTOP…ItNEVERComesBackWhileThatWindowIsOPEN` (12 polls) **and** the headless `PressingTheShellsSTOPButtonInsideTheWindow_StopsItComingBack` on the REAL button | `:608`, latched `:98-101` |
| R4b | forget the latch too early — it clears when the window CLOSES | `R4b_TheHandStopIsForgottenWhenThatWindowCLOSES…` | `:638` |
| R4c | ignore a manual START, which overrides the latch | `R4c_AManualSTARTOverridesTheHandStop…` | `:107` |
| R4d | latch on a stop OUTSIDE the window | `R4d_StoppingOUTSIDETheWindowLatchesNothing…` | `:98` |
| R4e | latch on a stop with the scheduler OFF | `R4e_AndTheSameStopWithTheSchedulerOFFLatchesNothingEither` | `:98` |
| R5 | start on an unticked day, whatever the clock says | `R5_AnUntickedDayRefusesForTheWholeDay…` (15 polls, then Tuesday works) | `:660-664` |
| R6 | treat unreadable text as "no window" — it MOVES the window to 16:00 | `R6_AnUnreadableStartTimeReallyDOESMoveTheWindowToSixteenHundred_ThroughTheLiveTick` | `:667-671` |
| R7 | be in the window at exactly `endTime` | `R7_TheWindowsCLOSEDEndIsWhatTheLiveTickUses…` | `:686`, `:691` |
| R8 | read `start == end` as all day | `R8_AnEmptyWindowStartsNothing_AllDayAndEveryDay` | `:683` |
| R9 | act during the 60 s grace | `R9_NothingHappensDuringTheSixtySecondGrace…` (59 s: nothing; 60 s: the start-up check runs) | `MainWindow.xaml.cs:624-635` |
| R10 | act after teardown | `R10_AfterTeardown_NoAmountOfClockMakesItStartASession` (six hours of clock after `ShutdownAsync`) | `:629`, `WindowChrome.cs:166` |
| R12 | mark a session auto-started when the engine refused the start | `R12_AStartTheSessionRefuses_IsNotCountedAsSchedulerStarted` | port-only — see D182 |
| R13 | start twice for one window opening | `R13_OneWindowOpeningStartsExactlyONESession…` (10 polls) | `:608` |

**R12 is the one WPF does not have.** `StartEngine()` (`:161`) has no `_isRunning` guard of its own
and `CheckSchedulerOnStartup` (`:562-581`) does not test one either, so upstream can re-arm every
service over a session the user started during the grace and then mark it auto-started — after
which the window's close stops it. The port's `SessionEngine.Start()` returns `false` and the
scheduler records `scheduler-start-refused-by-session` instead of claiming it. D182.

---

## 5. PROVING IT BITES — 73 mutations, six rounds, **1 survivor, and it is UNCOVERED not equivalent**

Every conjunct, boundary, constant, flag write and wiring line this packet added was mutated one at
a time by `spine-tasks/SP-118-scheduler/sweep.mjs`, which lives inside this packet's folder and
writes only inside it (SP-112's rule). It normalises for MATCHING and writes each mutant back in the
file's OWN line endings — the tree is CRLF and the needles are LF, which is what silently skipped 27
of SP-112's hardest cases — builds the product project BEFORE the suite so a mutant the compiler
rejects is reported as `NOT COMPILED` rather than counted as a catch, and restores each file
byte-identically. The raw logs are beside this record (`sweep-round1.log`, `-round2.log`,
`-round3.log`) and every count below is taken from them.

**A needle-only pre-pass (`--match-only`) ran before every round and is why `NOT PATCHED` is
zero:** it applies and restores all 73 without building, so a needle that no longer matches is found
in seconds instead of an hour into a round. It found two (M-bm at authoring, M-ax after the round-4
hardening moved the lines under it) and it is an instrument for the driver, never evidence about the
code.

**The books: 73 distinct mutations; 72 caught; 1 survives; 0 not patched; 0 not compiled.** Every
round's log carries a non-zero passing count from the same filters (59, then 67-77 unit / 62-63
headless), so no filter in this sweep matched zero tests.

### WHICH VERDICTS ARE CURRENT AGAINST THE SHIPPED TREE — and the round-6 answer

**Presenting 72 catches as equally fresh would have been false, and the final review said so.** A
verdict is only evidence about the tree it was taken against, and this packet changed product code
*after* rounds 1-3 — twice, in one file. §5's own M-av disclosure proves that is not a formality:
that hardening silently turned a caught mutant into a masked one.

| file | product code changed after its verdicts? | current verdicts |
|---|---|---|
| **`Scheduling/SchedulerParticipant.cs`** (11 mutations) | **YES, twice** — `GenerationLive` (after r1) and `OnDue`'s second liveness gate (r4) | **all eleven re-run after both**: M-av/M-aw/M-ax/M-az/M-bt/M-bu in r4, M-av again in r5, and **M-au/M-ay/M-ba/M-bb/M-bc in round 6** |
| `Scheduling/ScheduleWindow.cs`, `SchedulerPresetDocument.cs`, `SessionScheduler.cs`, `ScheduleClock.cs`, `Views/**`, `Lifecycle/CompositionRoot.cs` | **NO.** `git diff <r1 commit> HEAD` over all of them, with comment lines filtered out, is **two lines of an AXAML comment** ("NINE" → "TEN") | round-1 verdicts stand, against byte-identical executable code |
| `Scheduling/SchedulerReasonCodes.cs` | two constants deleted | no mutation targets this file |

**Round 6 exists because round 4's selection rule did not cover the failure mode round 4 itself
discovered.** That rule was "re-run the mutations whose CLOSERS were newly written", which is a
test-side rule — and M-av was re-opened by a PRODUCT change. So five `F.part` mutations kept
round-1 verdicts against a participant that then grew two guards. The worst of them to leave stale
was **M-ay**, which asks whether a stop leaves a live one-shot behind: D188's own harm class.

**Round 6 re-ran all five against the shipped tree: 5 caught, 0 survived, 0 not patched, 0 not
compiled, tree restored byte-identically** (`sweep-round6.log`). Nothing was masked. Test-side
changes since a verdict are not a freshness risk in the other direction: this packet only ADDED
facts (and removed one duplicate, whose mutation M-bd was re-run in r4 and is caught by the new
`SystemScheduleClockTests`), and adding an assertion cannot turn a catch into a survivor.

> **A note on the driver's own tree check.** Rounds 2 and 3 report `tree restored byte-identically:
> NO — M SchedulerParticipant.cs`. That is MY uncommitted edit (the `GenerationLive` property added
> between rounds), not sweep damage: `git diff` over `client/src` shows exactly those ten added
> lines and nothing else. Round 1 reported `YES` on a clean tree. Recorded rather than quietly
> re-run, because a driver whose cleanliness check can be satisfied by an unrelated edit is a
> driver that could also hide real damage.

### Round 1 — 58 caught, 10 survived

The ten: **M-m** (the unreachable day default), **M-x** (the HAND-STOP clause), **M-z** (STOP drops
the outside-the-window clause), **M-aa** (a refused start marked as ours, in the TICK), **M-av** (the
due callback's liveness re-check), **M-aw** (CompareExchange → Exchange), **M-az** (stop does not
cancel the generation), **M-bd** (the real clock reads UTC), **M-bl** (every day box writes Monday),
**M-bo** (the flush dropped from the pre-drain slot).

### The two that were REAL HOLES, and one of them was in the packet's own headline fact

**M-x — `START drops the HAND-STOP clause` — survived, and it is the most valuable thing this sweep
found.** R4 stops a session the SCHEDULER started, so `_autoStarted` is still true and the START
branch is blocked by *that* conjunct alone: dropping `!_manuallyStopped` changed nothing, and **the
refusal this whole packet is weighted around was never actually exercised**. The case the latch is
really for is the one R4 did not construct — the user starts a session THEMSELVES inside the window
(so `_autoStarted` is false) and then stops it — which is exactly "I pressed STOP and it came back".
Closed by `TheHandStopLatchAlsoBlocksAStartTheSchedulerNeverMade_WhichIsTheCaseItIsREALLYFor`, and
closing it needed one honest addition to the test clock: `SetLocalTime`, which moves the wall clock
WITHOUT delivering a tick. At a thirty-second cadence the ordinary case is that time passes and the
user acts between two polls; a clock that could only move by firing could never put a gesture there.

**M-z — `STOP drops the outside-the-window clause` — survived, and it is a live defect.** Without
`!reading.InWindow`, the STOP branch fires on the very next tick after an auto-start: the session
ends thirty seconds after it began, the flag is cleared, and the tick after that starts it again —
**a thirty-second on/off flap for the length of the window.** No fact ticked inside the window with
the auto-started session still running, so nothing saw it. Closed by
`AnAutoStartedSessionSurvivesEVERYTickInsideItsWindow` (11 polls).

### The six that were gaps rather than holes, each closed by a fact

| id | what it was | closer |
|---|---|---|
| M-m | the `_ => false` default arm was unreachable because every fact fed it a real `DayOfWeek`. A C# enum is not a closed set and **the direction of an unreachable default matters for a module that starts sessions** | `ADayOutSIDETheSevenFailsCLOSED_WhichIsUpstreamsOwnDefaultArm` — `(DayOfWeek)7`, `99` and `-1` all answer NO |
| M-aw | the CompareExchange identity was unreachable because the test clock DROPPED a spent timer. `System.Threading.Timer` suppresses a disposed callback only best-effort, which is why `IScheduleClock.Schedule` says so in its own contract | the clock now RETAINS spent callbacks and a fact re-delivers the grace's after the poll owns the slot (`ASupersededTickDoesNothing_AndDoesNotSTEALTheLivePollsSlot`) |
| M-az | `_owner.Cancel()` in `StopAsync` had no observable, so the participant contract's own rule (SP-003 §5.3) was unpinned | one read-only property, `SchedulerParticipant.GenerationLive`, and `StoppingTheParticipantCancelsItsGeneration…`. **This is the packet's one product change made because a mutation survived**, and it is a diagnostic read of state the class already keeps, not a behaviour |
| M-bd | **no test drove the REAL clock at all**, so the one property that made a new seam necessary instead of widening `ISessionClock` was unpinned | `TheRealClockIsLOCAL_AndThatIsTheWholeReasonThisSeamExists` asserts `Kind == Local`. Kind rather than value, deliberately: on a machine whose timezone IS UTC the two readings are equal, so a value comparison would pass under the mutant exactly where the port is hardest to check |
| M-bl | seven day handlers were wired by inspection only; no headless fact clicked one | `EachDayBoxWritesItsOWNDay_AndLeavesTheOtherSixAlone` clicks all seven and asserts the other six are untouched each time |
| M-bo | every panel gesture saves as it goes, so the earlier root fact was asserting its own `Save()` and could not see the flush | `AnUnsavedSettingStillReachesDiskThroughTheReservedPreDrainSlot` mutates the document DIRTY and never saves it — which is the guarantee persistence contract §11 is about |

### Round 2 — 8 caught, 2 survived. Round 3 — 1 caught, 1 survived

**M-av survived rounds 1 AND 2, and the reason round 2's closer missed it is worth having.** After
`StopAsync` the pending slot is null, so the CompareExchange identity guard returns FIRST and the
liveness re-check is never reached: masked, not covered. The window where the liveness check is the
only thing between a live token and a started session is the product's own teardown order —
`ApplicationHost.ShutdownAsync` cancels and DRAINS every generation **before** it stops participants
in reverse, so between those two steps the scheduler is still `_running`, its poll token is still in
the slot, and its generation is dead. Reproduced exactly by
`ATickThatComesDueBETWEENTheGenerationDrainAndTheParticipantStop_StartsNothing`, and caught in round 3.

### Round 4 — the CODE REVIEW's round, and it found the biggest hole in the packet

**`SystemScheduleClock` was compiled and never executed**, which is SP-101's exact finding
reproduced in a new class nine waves later. It is the default on every product path
(`SchedulerParticipant`'s constructor), its callback runs `Tick()` → `SessionEngine.Start()` →
`Arm()` across the whole rack on a pool thread with no caller above it, and the only fact touching
it read `LocalNow.Kind` twice. **Deleting its `catch` left 2182/133 green and both gates green.**
The sweep had one `F.clock` entry, which contradicted this record's own claim to have mutated every
line the packet added — the claim was false and is corrected here rather than defended.

`client/tests/CcpClient.Tests/SystemScheduleClockTests.cs` closes it, structured on
`SystemSessionClockTests` so the two clocks read side by side, with **no wall-clock wait**: every
fact waits on a deterministic signal through the approved helper, and the negative observation uses
an ordering barrier rather than an interval. Five new mutations came with it, all CAUGHT:

| id | mutation | closer |
|---|---|---|
| M-bq | the fault is caught **silently** — the reporting dropped | `ACallbackThatThrows_IsContainedAndREPORTED…` |
| M-br | **no containment at all**; the exception escapes and ends the process | the same fact, and its catch really is the host dying — named, because that is the crashed-host channel rather than an assertion |
| M-bs | the `Math.Max(0, …)` negative-delay clamp dropped | `ANegativeDelay_IsClampedToImmediate_RatherThanThrowing` |
| M-bt | `Arm`'s post-check dropped — a stop leaves a live one-shot | `AGenerationCancelledWHILETheClockIsBeingAsked_LeavesNoLiveTickBehind` |
| M-bu | the second liveness gate dropped (see below) | the same fact |

**And writing M-bt's closer found a real defect in the product.** `Arm`'s post-check tears the new
one-shot down when the generation dies mid-schedule — but `OnDue` then ran its DECISION anyway, so a
tick could start a conditioning session with the host already draining. **One product change closes
it:** `OnDue` re-checks liveness after `Arm` and refuses the decision, which is the port's analogue
of upstream's own `HasShutdownStarted` refusal (`MainWindow.xaml.cs:629`) and leaves WPF's ordering
untouched (the next tick is still on the clock before any decision runs, `:634-635`).

### Round 5 — and M-av had to be re-closed, which is recorded rather than quietly re-run

**Round 4's hardening RE-OPENED a mutation round 3 had caught.** M-av drops `OnDue`'s FIRST liveness
check; the new second gate then refuses the same decision, so the fact that used to catch it passed.
That is a masked mutant, not a fixed one, and the honest reading is that the first check's unique
contribution had never been isolated. It is this: **a callback arriving after the drain must not ask
the clock for anything either.** Without the first check the callback runs on to `Arm`, which puts a
one-shot up and has it torn straight back down — invisible in `PendingCount`, and a teardown racing
an endless re-arm. The test clock now counts `Schedules` and the fact asserts it does not move.
Re-closed in round 5.

### The ONE survivor, and it is dispositioned UNCOVERED — never "equivalent"

**M-aa: `if (_engine.Start())` → `if (_engine.Start() || true)` in the TICK.**

`port-workflow.md`'s rule is that an equivalence claim is inadmissible until every consumer of the
mutated symbol is enumerated and the claim discharged by name. **I make no such claim.** The
enumeration is this: `SessionEngine.Start()` returns `false` on exactly one condition — `Running`
(`Session/SessionEngine.cs`, the first statement) — and the branch that calls it conjoins
`!_engine.Running` two lines above (`:608`'s port). So on a single thread the false path is
**unreachable**, and no input this suite can construct distinguishes the mutant.

**It is reachable under concurrency**, and I am naming that rather than arguing it away: the tick
runs on a pool thread and holds `_gate`, while the shell's START button calls `Engine.Toggle()` on
the UI thread without taking it, so a press landing between the guard and the call really can make
`Start()` return false. Constructing that deterministically needs a seam inside the product placed
there purely for the test, and **I did not add one.**

Two things bound the risk, and both are measured rather than asserted. First, the same defensive
line in `RunStartupCheck` **is** reachable single-threadedly — because WPF genuinely has no
`_isRunning` guard there (D182) — and its mutation **M-am was CAUGHT** in round 1 by
`R12_AStartTheSessionRefuses_IsNotCountedAsSchedulerStarted`. Second, the damage the mutant would do
is bounded: it marks a session the scheduler did not start as auto-started, so the window's close
would end it. That is a stop, never an unbidden start.

**Uncovered is an honest gap; a false equivalence is a false belief, and it propagates.**

### What this sweep does NOT close

`compiles()` gated every round here, so the NOT-COMPILED channel is closed. **The remaining
false-clean channels — an empty `--filter`, a crashed test host, and the 15-minute timeout — are
UNCLOSED**, and are named here rather than left for a reader to find. The filter channel is bounded
empirically: every round's log shows a non-zero passing count from the same filters. The
crashed-host channel is no longer only theoretical — **M-br deliberately produces it**, and its
CAUGHT verdict comes from the host dying rather than from an assertion. That is the right outcome
for that mutation (the process kill IS the defect) but it is stated plainly, because a reader
counting M-br as an assertion catch would be counting something that never ran.

---

## 6. THE RACK ROW, AND WHAT UPSTREAM PASSES

**Upstream passes a dot predicate for this row**, unlike Visuals:
`Add("scheduler", "📅", null, "Scheduler", "section_scheduler", HostScheduler, PanelScheduler,
"SchedulerRamp", () => App.Settings?.Current?.SchedulerEnabled, toggle: () =>
FlipMasterCheckBox(PanelScheduler?.Inner.ChkEnabled))` (`Views/Tabs/StudioTabView.xaml.cs:535-537`),
**first in the TIMING group, before Intensity Ramp** (`:538`). So the row has a dot and it takes the
right-click.

**The dot is earned rather than read off the checkbox** (D180):

```
Off   = the enable is off, OR no tick is on the clock (the grace, or after teardown)
Armed = enabled, really polling, and the LOCAL clock is OUTSIDE the window
Live  = enabled, really polling, and the LOCAL clock is INSIDE the window
```

`Polling` is the scheduler's analogue of `PacedSessionEffect.ScheduleArmed` — it is written only by
the participant, when a tick is really scheduled — so the dot cannot claim a tick that does not
exist. The visible consequence is the grace: for the first sixty seconds after launch the box is
ticked and the dot is **Off**, because the module genuinely cannot act, and the panel says so in
words. Upstream's dot would be lit there.

**`Live` is NOT "a session is running"**, and that is deliberate: a scheduler inside its window
whose session the user stopped by hand is still doing its job — waiting out the window without
restarting anything — and a dark dot there would claim the schedule had ended.

**The row is the FIRST in this rack with a dot and no module behind it.** It is deliberately absent
from `SessionEngine.Effects` (asserted), so its right-click does not go through
`SessionEngine.QuickToggle`: it flips the scheduler's own enable, which is what upstream's own
`toggle:` does and what upstream's own comment says it does ("neither drives a service directly …
so the honest quick-toggle is the panel's own enable box", `:532-534`).

**The panel is upstream's TEN controls and no more** — an enable, two free-text times and seven day
boxes (`Features/SchedulerFeatureControl.xaml.cs:42-51`), asserted as a SET so an eleventh cannot
arrive unnoticed. The four other `Scheduler*` settings on `AppSettings` (`:2461-2478`) belong to the
intensity RAMP and appear on no scheduler surface upstream either.

**The times stay free text on purpose.** A picker would make the unparseable-time fallback
unreachable, and that fallback is live behaviour that moves a user's window by up to sixteen hours.
The box keeps what the user typed and the panel reports what was really parsed.

---

## 7. FILES CHANGED

**Product — new (`Scheduling/**`, the whole folder):** `ScheduleClock.cs` (the LOCAL seam and the
real clock), `SchedulerPresetDocument.cs` (upstream's ten settings, its defaults and its
null-coalesce), `ScheduleWindow.cs` (the predicate and the reading), `SessionScheduler.cs` (the tick
machine, the two flags, the dot and the manual-toggle note), `SchedulerParticipant.cs` (the
app-lifetime owner, the grace and the poll), `SchedulerReasonCodes.cs` (eight codes, six of them
refusals). Plus `Views/Pages/SchedulerPanelNotices.cs` (the panel's five sentences).

**Product — changed:** `Scheduling/SchedulerParticipant.cs` gained ONE read-only property after
the sweep (`GenerationLive`, §5) and, after the code review, the second liveness gate in `OnDue`
that refuses a decision when teardown began during `Arm`; `Scheduling/SchedulerReasonCodes.cs` lost
`scheduler-not-polling`, a code no path ever emitted (the contract's rule is that codes land with
their consumer row); `Lifecycle/CompositionRoot.cs` (the `ScheduleClockFactory` seam, the session
built into a local so the scheduler takes ITS engine, the ninth participant, the flush slot),
`Views/MainWindow.axaml.cs` (the scheduler reached from the host, the START/STOP button's
manual-toggle note, the duck on auto-start), `Views/Pages/StudioPage.axaml` + `.axaml.cs` (the row
in WPF's TIMING position, the ten-control panel, the dot, the right-click, the four notice lines).

**All six capability folders, `Effects/**`, `Persistence/**` and `Tray/**` are byte-identical to
base** (`git diff --stat` over each is empty).

**Tests — new:** `SchedulerWindowTests.cs` (**29** cases: the boundaries at the tick, the empty
window, the day gate per day and outside the seven, both fallbacks, the parse trap, the defaults,
both daylight-saving nights, and a week-long verdict sweep), `SchedulerModuleTests.cs` (**33** cases: two positives, the thirteen
refusals, the poll cadence, the dot, the settings round-trip, the real composition root, and the
sweep's closers), `SystemScheduleClockTests.cs` (**6** cases on the REAL clock that ships — the
review's blocker), `SchedulerRowHeadlessTests.cs` (**8** cases on real input). The final review added the two
daylight-saving predicate facts and the fall-back double-start (§8).
**Tests — changed at zero count:** `CompositionRootValidationTests.cs` and `IntegrationProofTests.cs`
(8 → 9 participants, plus new assertions that the ninth starts NO session),
`StudioRackHeadlessTests.cs` (the row list gains `RowScheduler` in WPF's position, and the
order fact gains two assertions: this row HAS a dot and has NO effect behind it).

29 + 33 + 6 = **68** unit, **8** headless — the declared delta.

**Docs:** `client/docs/wpf-surface-reachability.md` (§SP-118, **D180-D187**).
**Sweep artefacts, inside this packet's folder:** `sweep.mjs`, `sweep-round*.log`.

---

## 8. WHAT THIS WORK DOES NOT PROVE

**First, and largest: nothing here proves a human was ever conditioned by a session they did not
start.** Every fact stops at the engine's `Running`, the row's dot, or the document behind them. No
headed capture was taken; `presentation-verified` is untouched.

- **The interval residual, narrowed to what is really left.** An earlier draft said "the 60 s
  grace and the 30 s poll are proved only on an injected clock", and the final review was right
  that this overstates it. **M-au** and **M-ax** pin WHICH constant reaches `Arm` and that the poll
  re-arms at all; `SystemScheduleClockTests` executes the real `System.Threading.Timer` at zero and
  at negative delays. What remains unproven is only that `Timer` honours a POSITIVE `dueTime` —
  **a BCL property, not a port claim**, and one no amount of test-writing here would make more
  true.
- **Daylight saving is now largely PINNED rather than named**, and the final review was right that
  it was far cheaper to close than the earlier draft claimed: the injected clock reproduces both
  transition sequences with no OS timezone change. **Spring forward** — a window inside the lost
  hour never opens and the session is silently missed. **Fall back** — the predicate cannot tell
  the two 02:00s apart, the window closes on the first pass (clearing `_schedulerAutoStarted`), and
  the repeated hour re-opens it, so **a second unbidden auto-start really can happen in one night**.
  That second case is this row's own headline harm and it is now a fact
  (`AFallBackNightCanAutoStartTWICE_AndThatIsUpstreamsBehaviourNotADefect`), recorded as D190 and
  deliberately NOT "fixed": a DST-aware predicate would start sessions at moments upstream does
  not. **What is still untested** is a real machine crossing a real transition, and a timezone
  changed underneath a running app. Closing the second needed one honest modelling correction in
  the test clock: a rewound wall clock shifts every pending timer by the same offset, because
  `System.Threading.Timer` counts an elapsed DURATION and not a wall-clock instant.
- **Nothing proves the minimize is seen.** The headless fact asserts `ShellTray.IsDucked` and
  `WindowState == Minimized` in a headless window. Whether a real desktop shows a taskbar button, a
  tray icon, or anything at all is a headed claim and is not made.
- **The two tray balloons are ABSENT, not deferred** (D183). What tells a user a scheduled session
  began is the ducked window, the icon that goes up with it, and the START control turning red.
- **Linux is unproven.** The scheduler itself is platform-neutral — it reads a clock and calls the
  engine — but every effect the session it starts would run draws through capabilities that refuse
  on Linux, and the tray duck's icon half refuses there too.
- **Concurrency is barely exercised.** The tick runs on a pool thread in production and inline in
  every test; a tick landing exactly as the user presses STOP is not covered. The decision and the
  flag write are atomic under one lock, which is why the interleaving is bounded rather than
  proved.
- **Nothing proves the scheduler behaves over a real multi-day run.** Every window opening and
  closing in this packet is a jump on an injected clock.
