# SP-108 — record

Branch `lane/SP-108-non-drawing-module`, base `c547f1c1`.
Floor: pin **1477 unit / 90 headless**; observed **1527 unit / 95 headless**; declared delta
**+50 unit / +5 headless** (`floor-delta.json`). 1527 = 1477 + 50 and 95 = 90 + 5, confirmed by
`node client/tests/floor/sum-deltas.mjs --check --packets SP-108-non-drawing-module`. Two skips, both
pre-existing (`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`,
`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`); none added.
Build: 0 errors, 0 warnings. `client/tests/floor/floor.json` was never opened.

---

## 0. THE HEADLINE — the module was chosen, not assigned, and the escape clause was not taken

The packet did not name a module. Four rows in one rack group had been ported and the criterion was
**distance from what is already proven**, with an escape: if every candidate in the other three
groups needs a capability the port lacks, say so with evidence per candidate.

**One candidate is fully in scope, and it is the furthest of all of them from the four proven seams.**

> **Intensity Ramp** — WPF's TIMING group, rack key `ramp`
> (`Views/Tabs/StudioTabView.xaml.cs:538-541`).

It satisfies all three of the packet's distance criteria at once, which no other candidate does:

| Criterion | Evidence |
|---|---|
| observable **without** an overlay surface | the mechanism is `settings.SpiralOpacity = newVal` and four siblings — no window, no draw (`MainWindow/MainWindow.StartStop.cs:504-540`). Upstream's own words at `:451-456`: "the settings write is the whole job now" |
| driven by **session progress** | `progress = Math.Min((DateTime.Now - _rampStartTime).TotalMinutes / duration, 1.0)` (`:484-493`) |
| **interacts with** an existing module | it moves `SpiralOpacity` (`:517`) and `PinkFilterOpacity` (`:523`) — precisely `SpiralPresetDocument.OpacityPercent` and `PinkFilterPresetDocument.OpacityPercent`, the dials the two landed continuous modules read |

The refusals for every other row, with citations, are in `plan.md` §1 and summarised in §7 below. The
runner-up was **Mind Wipe** and it is named as the runner-up rather than dismissed: it is reachable
(the port has real audio), and it is **less distant**, because it is a PACED module — random intervals
off a frequency-per-hour dial (`Services/LockCard/MindWipeService.cs:18-30`) — that
`PacedSessionEffect<TFiring>` fits. It would have tested the audio capability, not the spine.

---

## 1. THE FOURTH TEMPLATE VERDICT

**A non-drawing module needed exactly two things no drawing module did, and the spine supplied
neither and obstructed neither.**

1. **A `Live` that is a claim about CUSTODY** — state this module has taken from other modules and
   owes back. Not the clock, even though this module genuinely has one. Not the screen. Not change.
2. **A release that UNDOES a change the module made to somebody else's state.** Every module before
   it released something it owned: a pending one-shot, a surface. This one has to hand back what it
   borrowed, and if it does not, the user's own settings are gone.

**The spine itself fits a fifth time, unchanged.** `ISessionEffect`: not one member edited.
`OwnedSessionEffect`: `WorkIsRunning` / `Engage` / `ReleaseWork` are exactly the three questions a
non-drawing module has to answer, and it answers all three. `EffectDotState`: still three states,
still no fourth member. `SessionEngine`, `EffectSignal`, `ScheduledFire`, `PacedSessionEffect`,
`OverlaySurfaceSet`: **untouched, not one line**. The only shared-code change in the packet is three
additive reason codes.

### 1.1 The prediction, and how it held

`plan.md` §2 was written before any product edit.

| Predicted | Outcome |
|---|---|
| P1 `ISessionEffect` fits unchanged, a fifth time | **Held.** No member edited |
| P2 `OwnedSessionEffect` fits unchanged | **Held.** No member edited at all — the first module since SP-105 that needed nothing from the shared body |
| P3 `PacedSessionEffect` does not fit, for a DIFFERENT reason than SP-105's | **Held**, and it is the sharper half of the verdict — §1.2 |
| P4 no surface, no presenter, no `OverlaySurfaceSet`; the cadence lives in the module | **Held.** Pinned structurally and behaviourally (§3) |
| P5 the dot gains a fourth MEANING and the enum does not gain a fourth member | **Held** — §1.3 |
| P6 no shared-code change beyond three reason codes | **Held** |
| P7 two links where WPF has five | **Held**, D93 |

**Nothing in the plan was wrong.** One thing it did not predict: the mutation testing found a **hole
in my own fact** rather than in the product (§5.1).

### 1.2 Having a timer is not sufficient to be paced

SP-105 rejected `PacedSessionEffect<TFiring>` for a continuous module on the ground that the module
had no interval, no firing, no counter and no clock — four members that would all have to be answered
with a lie. **That argument does not apply here.** This module has a real, constant, upstream-cited
interval: `TimeSpan.FromSeconds(2)`, WPF's `_rampTimer` (`MainWindow.StartStop.cs:426-428`).
`NextInterval()` would have been honest.

It still does not fit, for two reasons the paced base cannot bend to:

| Paced base | What this module needs |
|---|---|
| `WorkIsRunning` is `ScheduleArmed` — a claim about the CLOCK | a ramp with nothing linked has a callback on the clock forever and will change nothing anywhere. `ScheduleArmed` reads `Live` for a module the user cannot observe by ANY means |
| `ReleaseWork` disposes the pending one-shot and leaves the world alone | this module's release must **give back two other modules' dials**. "Leave the world alone" is the failure |

So the finding is:

> **SP-106's distinction — an INTERVAL that decides when a MODULE is due, versus a CADENCE that keeps
> something correct — is the real line, and it was never about which FILE the timer lives in.** SP-106
> put a cadence in a surface because it was keeping a surface correct. This one keeps borrowed DIALS
> correct, and there is no surface to put it in, so it lives in the module. A ramp is never due; it is
> *on*, and what it is on is a set of numbers.

### 1.3 THE DOT'S FOURTH MEANING

- Paced `Live` = a claim about the **CLOCK** (a firing is scheduled). SP-101/SP-105.
- Continuous `Live` = a claim about the **SCREEN** (a surface is confirmed up). SP-105.
- Moving `Live` = the screen AND that it will be a **different** screen a moment from now. SP-106.
- **Non-drawing `Live` = a claim about CUSTODY: the module holds state belonging to other modules,
  is driving it, and owes it back.**

```
Running  =  the progress cadence is scheduled
         && this module holds at least one linked dial's base value
```

**Neither clause is redundant, and the SECOND one is the finding.** Without the first, a module whose
tick died would keep claiming to be climbing. Without the second, the dot degenerates into the paced
rule and lights up for a ramp with nothing linked — which, because every link ships OFF
(`AppSettings.cs:2589-2621`), is the state a freshly enabled ramp is really in.

**Two rules that were right for earlier modules give the WRONG answer here, and that is why this is a
fourth meaning rather than a re-use of one of the three.**

| Situation | SP-106's "Live means it will change" | Custody | What is true |
|---|---|---|---|
| the climb has finished at 3.0× | **Armed** — nothing changes again | **Live** | it is still holding the user's dials and STOP still gives them back |
| the multiplier sits at its 1.0 floor (the shipped default) | **Armed** | **Live** | moving the slider starts the climb with no re-arm; a live machine at neutral gain |
| nothing is linked | Armed | **Armed** | correct in both, and it is the negative control |

**And the 1.0× case takes the SUBLIMINALS answer, not the PINK FILTER answer** — a distinction the
port now has three modules' worth of evidence for. A tint at 0 % opacity has literally no window, so
it is `Degraded` and its dot is `Armed` (SP-105). A ramp at 1.0× has real custody of real dials, so
like Subliminals over an empty phrase pool it is `Degraded` in the ARM RESULT and `Live` in the DOT.
Three typed reason codes carry the difference: `ramp-no-dial`, `ramp-no-linked-dial`,
`ramp-multiplier-flat`.

### 1.4 What a non-drawing module needed that a drawing one did not

1. **A release that undoes somebody else's state**, and it is the whole second half of the module.
   `ReleaseWork` writes every held base value back. Nothing before this had a counterpart: a paced
   module's release drops a one-shot, a continuous or moving module's withdraws a surface it owns.
2. **A split between "write the value" and "apply the value"**, because they have different thread
   rules and different failure modes. The persisted write is synchronous and lock-safe; the re-apply
   touches a native overlay and is dispatched. **The split closes a PORT-SIDE hazard with no upstream
   counterpart** (D95): in this port a post is not delivered once the dispatcher is down, and
   `PersistenceStore.FlushAsync` runs after `Engine.Stop()`, so a single combined operation would
   leave the ramped value in the document for the flush to write over the user's own. Upstream
   reaches the same outcome by a different route — every writer of `_exitRequested` stops the engine
   before `SaveSettings()`, and `StopEngine` -> `StopRampTimer` IS the restore
   (`MainWindow/MainWindow.StartStop.cs:388` -> `:437-479`). **An earlier draft of this record and of
   D95 claimed WPF loses the value here; that was false and is corrected — see §9.**
3. **A seam so the module does not know what it drives.** `IIntensityDial` (`Id`, `Label`, `Ceiling`,
   `Read`, `Write`, `Reapply`) with two adapters built in the composition root. Upstream writes five
   named settings by hand inside its tick, which is why upstream's ramp cannot be exercised without an
   `AppSettings`, a dispatcher and a window. **This is what makes "no surface anywhere" testable
   rather than merely claimed**: `IntensityRampEffectTests` has no overlay, no presence and no
   display in it at all.
4. **A panel line that is not about a screen.** Every other module's panel ends with "where did the
   pixels go". This one reports **custody**: which dials it borrowed, what they were, and what they
   are now — "Spiral Overlay opacity 10% → 27%… every one goes back to the first number when the
   session stops". A headless fact asserts that `RampSurfaceState` does not exist while the other four
   panels' surface lines do.
5. **A way to end the session it is inside.** WPF's ramp calls `StopEngine()` at full progress when
   the user asked for it (`MainWindow.StartStop.cs:547-555`). A module cannot call the engine that
   owns it without closing the cycle, so the module raises `Completed` and `SessionParticipant` — the
   only thing that knows a session exists — makes the call. **This is the first ported module that
   can stop a session.**

### 1.5 What a SIXTH module should change

1. **`Views/Pages/StudioPage.axaml.cs` is still the file that will not scale**, and SP-106 §1.4 was
   right that four bodies justified the extraction. This packet did not do it — a refactor of the file
   carrying all four landed modules' rendered claims is the wrong risk in the same wave as a new
   module — but it did **stop making it worse**: the fifth module's text lives in
   `Views/Pages/RampPanelNotices.cs`, which is where the extraction would put it. The sixth module
   should move the other five families there and delete nothing else.
2. **`SessionEngine.ArmOutcomes` is still recorded and not rendered**, and it is now worse again:
   **three** of the five modules can arm `Degraded` with a cause no row shows, and this module's
   `ramp-no-linked-dial` is the first one a user will hit by default rather than by misconfiguration.
   Still the highest-value next UI row.
3. **The port's rack is 2 groups / 5 rows against WPF's 4 / 15.** The two groups it has not opened are
   GAMES & CARDS (every row needs input-capturing windows or video) and IMMERSION — where Mind Wipe is
   paced audio outright, **Brain Drain is paced audio plus a desktop-wide blur the port cannot draw**,
   and Haptics is a device backend. **The next module is a capability question, not a spine question**
   — see §7, which also names the two unported EFFECTS rows this record does not survey.
4. **`Lifecycle/OperationRegistry.Cancel`/`Begin` still cancel inside their own lock**, which is
   SP-106 §4.2's unfixed root cause. This packet stayed clear of it by construction (§4) rather than
   by luck, and the follow-up is unchanged.

---

## 2. THE RACK — a SECOND GROUP, created because a module needed it

The port's rack was 1 group / 4 rows. It is now **2 groups / 5 rows**: `EFFECTS` (Flash Images,
Subliminals, Spiral Overlay, Pink Filter) then `TIMING` (Intensity Ramp), in WPF's own group order
(`StudioTabView.xaml.cs:482-541`) with the group headers upstream's own strings
(`st4_studio_group_effects` / `st4_studio_group_timing`, `en.json:4816,4819`).

The new row carries the full grammar and needed no new gesture:

- **Left-click opens its panel**, and only that one.
- **Right-click quick-toggles**, through the same one `SessionEngine.QuickToggle` entry the panel's
  Enable checkbox uses.
- **The dot reports what is running**, off the effect's own `Dot`.

Its panel carries the six dials the running module really reads (enable, duration, multiplier, curve,
end-at-complete, two links), the live-state line, and the custody line. **It has no surface line, and
that absence is asserted rather than left to a reader.**

**Two landed headless facts changed and neither was weakened.** `TheRackIsInWpfsOrder_…` and
`TheSpiralRow_NowCarriesTheGrammarToo_…` each carried a four-name row list and a four-name dot loop;
both now carry five. The claim in each is unchanged and strictly stronger — "every rack row, without
exception" now covers a row from a group that did not exist when the sentence was written.

---

## 3. Where the second trap was answered structurally

The packet's second trap: *if your module needs no surface it must not acquire one*. Three
independent pins, because one of them is only a claim about a constructor:

| Pin | Claim |
|---|---|
| `TheNonDrawingModuleTakesNOSurface_AndIsNotAPacedEffect` | no constructor parameter and no field of `IntensityRampEffect` is in `CcpClient.Desktop.Overlay` or named `*Surface*`; it derives from `OwnedSessionEffect` and from no `PacedSessionEffect<>`; and it DOES take an `ISessionClock`, in the module, because there is no presenter to put one in |
| `TheRampItselfPutsNOTHINGOnScreen_NoOverlayIsEverAcquiredForIt` | with ONLY the ramp enabled (Spiral Overlay switched off by hand, because it is the one ported module that ships ON, `AppSettings.cs:2645`) and linked to both drawing modules, 200 cadence samples leave both recording overlay presences with an **empty call log** — and the ramp reads `Live` throughout, holding two dials, with nothing on any screen |
| `TheRampPanelOpensOnItsOwn_AndHasNOSurfaceLineBecauseItHasNoSurface` (headless) | the four other panels each have their surface `TextBlock` in the mounted visual tree and `RampSurfaceState` does not exist |

`SessionParticipant` composes it with no surface argument, and the composition passes no presenter.

---

## 4. Threading — the SP-106 §4.2 hazard, avoided by construction rather than by luck

This module writes into two OTHER modules' state from a clock callback and from a teardown thread,
which is the shape that produced SP-106's reverted lock-order inversion.

**CORRECTED at final review: an earlier draft of this section OVERSTATED the exposure.** It described
the chain `rampGate → targetStore.mutationGate → targetEffect.Gate → targetOwner.Gate` and said the
lock order was kept acyclic. **That four-deep chain cannot form**, because its third edge does not
exist: `Reapply` — the only thing that reaches `targetEffect.Gate` — is always DISPATCHED, never
called under a ramp lock (`Effects/IntensityRampEffect.cs`, both call sites in `Advance` and
`ReleaseWork`). What the ramp can actually hold at once is **two** locks, and the second is a leaf:

1. **`rampGate → targetStore._mutationGate`, and that is the whole chain.**
   `IIntensityDial.Write` is a bare `PersistenceStore.Mutate` (`Persistence/PersistenceStore.cs:220-228`),
   which takes one lock, runs the caller's pure lambda inside it, and **raises nothing**. It calls
   out to nothing that could take a third lock.
2. **`_hold` is a LEAF lock.** Nothing in this class calls out of it while holding it — every
   external call (reading a dial, writing a dial, scheduling a tick) runs on a snapshot taken and
   released first — and nothing takes `OwnedSessionEffect.Gate` while holding it. The one order this
   class produces is `Gate → _hold`, which is the order `Dot` already produces.
3. **Each `AsyncOperationOwner` owns its own lock** (`Lifecycle/OperationRegistry.cs:122`), so the
   ramp's owner lock and a target's are different objects — and the ramp never reaches a target's
   owner lock at all, per (1).

**So no cycle is constructible, and a pin here would assert a property with no counter-shape to
catch.** The concurrency residual is a follow-up row rather than a missing fact, and this section no
longer claims otherwise.

**THE TRIP-WIRE, so the next author knows when that stops being true.** Any ONE of these supplies the
missing second edge and the pin becomes required:

- a `SettingsReplaced` subscriber on the Spiral or Pink Filter store — that event is raised INSIDE
  `_mutationGate` (`Persistence/PersistenceStore.cs:243`) and today has **zero** subscribers anywhere
  in `client/src`, which is what makes `Mutate` a leaf;
- an `IIntensityDial.Write` that becomes more than a bare `Mutate` (anything that raises, notifies, or
  touches its owning effect);
- a synchronous `Reapply` — i.e. removing the dispatch, which would put `targetEffect.Gate` under a
  ramp lock and rebuild the four-deep chain this note exists to say does not exist.

**What is still not proven.** No test in this packet drives the ramp's tick and a dot repaint on two
real threads at once: every fact here is single-threaded by construction, exactly as SP-106 recorded
of its own suite. The reasoning above is a reading of the lock graph, not a measurement — the
correction narrows what is claimed, it does not turn the reading into evidence.

---

## 5. Proving it bites

**Four mutations, each run against the whole unit suite, each reverted and the file compared by md5.**

| Mutation | Result |
|---|---|
| `IntensityRampEffect.ReleaseWork` no longer writes the held base values back — **the half no earlier module had** | **11 FAILED**, every one of them the new module's (6 `IntensityRampEffectTests`, 5 `NonDrawingEffectSpineTests`); **1514 passed, including every `SessionSpineTests`, `FlashEffectTests`, `SubliminalEffectTests`, `SecondEffectSpineTests`, `PinkFilterEffectTests`, `ContinuousEffectSpineTests`, `SpiralOverlayEffectTests` and `MovingEffectSpineTests` fact** |
| `WorkIsRunning` drops its custody clause and becomes `_tick is not null` — **the paced module's rule, applied here** | **2 FAILED**, and they are precisely the two facts written for the packet's first trap: `TheDotIsLiveONLYWhileItHoldsSomething_ARampWithNothingLinkedIsArmedForever` and `ARampWithNothingLinkedIsARMEDNotLive_EvenThoughItsCadenceIsRunningInTheSameSession` |
| the dial write rounds instead of truncating — **the behaviour-visible arithmetic** | **0 FAILED on the first attempt.** See §5.1 |
| `OwnedSessionEffect.Disarm` no longer calls `ReleaseWork()` — **the SHARED body, five modules deep** | **14 FAILED, spread across ALL FIVE modules**: 2 `SessionSpineTests` (Flash Images), 3 `SecondEffectSpineTests` (Subliminals), 1 `PinkFilterEffectTests` + 1 `ContinuousEffectSpineTests` (Pink Filter), 2 `SpiralOverlayEffectTests` (Spiral Overlay), 5 `IntensityRampEffectTests` (Intensity Ramp) |

The last is SP-101's extraction check extended to five modules: **one line of shared code, and at
least one fact reds per module.** md5 before and after each mutation:
`IntensityRampEffect.cs` `07ea8279e4a8a0ecebbd6b2f07136410`,
`OwnedSessionEffect.cs` `6db92ca01a2622e3afe51fd92aba33ea`.

**Three more at the code-review revision** (§9), against the revised files
(`IntensityRampEffect.cs` `441241fc3931f03add9e32b1bde88af7`,
`RampPanelNotices.cs` `ee6ae2b59b639c35f262f11ffd5b5bef`, both restored byte-identically):

| Mutation | Result |
|---|---|
| the module's `Math.Min(baseValue × multiplier, dial.Ceiling)` becomes a bare cast — **the per-dial ceiling** | **2 FAILED** (`TheCeilingIsTheOWNINGDialsOwn…` on a request of **120**, `ADialLinkedMIDSESSION…` on **60**). Before the revision this mutation reded **nothing** — §5.2 |
| `DescribeRampState` returns one constant sentence for every state | **9 FAILED**: all 7 rows of the ramp's notice theory plus 2 facts. Before the revision it reded **nothing** — §5.3 |
| the module's `Math.Min(elapsed / duration, 1.0)` clamp is deleted — **the progress ceiling** | **2 FAILED** (`TheClimbStopsAtFullProgress…`, `ARampThatCompletesWithoutTheSwitch…`). Run because it was the third sweep candidate and reading alone had already been wrong once |

### 5.1 THE MUTATION THAT DID NOT BITE, AND THE HOLE IT FOUND IN MY OWN FACT

`TheDialClimbsOnWpfsOwnArithmetic_AndTheIntegerIsTRUNCATEDNotRounded` was written around a value of
**12.5** — chosen deliberately as the hardest case for a truncation. It is not: .NET's `Math.Round`
defaults to `MidpointRounding.ToEven`, so 12.5 rounds to **12** as well, and a rounding
implementation passed the fact that exists to catch it.

The fact now takes a second step to **12.6**, where truncation says 12 and both rounding modes say 13.
Re-run with the mutation: **FAILED, expected 12, actual 13.** Restored, green, md5 identical.

**Worth more than the fix:** a midpoint is the intuitive value to test a truncation with and it is the
one value that cannot distinguish truncation from the default rounding mode. The fact read as though
it proved something it did not, and only a mutation found that.

**And the lesson was not applied.** I recorded this class and did not sweep the rest of the packet for
it. The code review found a second instance immediately — §5.2 — which is the real finding here: a
mutation that survives is a hole in the FACTS, and one hole of a given shape is evidence that others
of the same shape exist.

### 5.2 THE SECOND INSTANCE, FOUND BY THE CODE REVIEW: an assertion a COLLABORATOR satisfies

`TheCeilingIsTheOWNINGDialsOwn_AndTheRampCannotDriveItPast` asserted `rig.Pink.Value == 50` after a
base of 40 at 3.0×. Deleting the module's `Math.Min(baseValue × multiplier, dial.Ceiling)` makes the
module ask for **120** — and the fact stayed green, because the test double's `Write` clamps on the
way in, exactly as the real dial does (`PinkFilterPresetDocument.OpacityPercent` clamps to 50 in its
own setter). `Assert.Equal(50, rig.Pink.Ceiling)` beside it was a tautology about the double.
**The module's ceiling clamp was pinned by nothing, in either test project.**

The fix is to assert against **what the module ASKED for** rather than what the collaborator stored:
`FakeDial` now records every raw request, and the fact asserts no request ever leaves `[0, 50]` and
the last one is exactly 50. Verified: the mutation now reds it, and reds
`ADialLinkedMIDSESSION…` with it (which had the identical blind spot at its own `Assert.Equal(50, …)`).

**The generalised rule this packet ends with:** *when an assertion's expected value could be produced
by a collaborator's own guard, it does not test the code under test.* A clamp, a `Math.Clamp` in a
document setter, a `Math.Min` in a helper and a default in a `switch` are all collaborators in this
sense.

### 5.3 THE SWEEP, and what it found

Every new fact in the packet re-read against that rule, and each candidate settled by MUTATION rather
than by reading — because reading alone had already been wrong twice.

| Fact | Verdict |
|---|---|
| `TheCeilingIsTheOWNINGDialsOwn…` | **HOLE** — the double's clamp satisfied it. Fixed (§5.2) |
| `ADialLinkedMIDSESSION…` | **HOLE**, same shape at its `Assert.Equal(50, …)`. Fixed the same way |
| `TheClimbStopsAtFullProgress…` | **SOUND, and it looked like a third hole.** `RampCurves.ApplyCurve` clamps its own input, so deleting the module's progress `Math.Min` still leaves the dial at 20 — but the fact also asserts `Ramp.Progress == 1.0`, which reads the module's own stored value. Mutation confirms: 2 red |
| `TheRampsLiveLine…` theory | **HOLE of a different shape** — no comparison between states at all. Fixed (Blocker 3), and the mutation now reds all 7 rows |
| `TheDialClimbsOnWpfsOwnArithmetic…` | sound since §5.1's fix; `FakeDial`'s ceiling of 100 is never reached, so no clamp can mask it |
| `ADialIsWrittenOnlyWhenItsIntegerReallyMoves…` | sound — it counts writes, and no collaborator can suppress one |
| `AStopGivesEveryHeldDialBackEXACTLY…`, the two quick-toggle facts, the teardown fact | sound — the expected values (17, 23, 12, 21) are arbitrary user numbers no clamp can produce |
| the five-module spine facts asserting 24/36 | sound — both are below the 50 ceiling, so no clamp is in the path |

**Two holes found, two fixed, one near-miss identified and proved sound.**

---

## 6. What this work does NOT prove

- **No headed capture was taken.** Nothing here claims a human has seen an opacity climb.
  `presentation-verified` remains the orchestrator's gate and is not discharged by anything in this
  packet, including the headless facts.
- **Nothing here proves composition.** The one fact about a live re-tint asserts what the presenter
  asked the overlay to do — that a tint already up was re-placed while the session ran, and how few
  times — not what the OS then held. The `IOverlayPresence` behind it is a recording double.
- **The headless rack facts drive no session**, on purpose, exactly as SP-105/SP-106 recorded: a
  headless test that started a session with a drawing module on would put a real full-screen
  always-on-top window over whoever ran it. So no test anywhere proves this module's effect reaches a
  real screen — which for this module is a weaker gap than usual, because it has no screen of its own.
- **Linux is unproven and unchanged.** The ramp itself is platform-neutral only because it touches no
  surface; the two modules it drives refuse by design on Linux, so a Linux ramp would climb dials
  whose modules show nothing, and their dots would correctly read `Armed` while the ramp's read
  `Live`. That combination is not exercised on a Linux machine here.
- **No concurrency is measured** (§4). Every fact drives arm, tick and disarm on one thread.
- **The auto-stop's tray notification is not ported** (D100), so a user who is not looking at the
  shell learns the session ended only from the shell.
- **`RampCurve` ordinals are persisted but no migration is exercised.** An unknown ordinal is proved
  to behave as Linear and to survive a round trip, but no document written by another build is read
  here.
- **Three of WPF's five links do not exist** (D93), so this module drives two dials where upstream
  drives five, and nothing here is evidence about flash opacity or either volume.
- **`Scheduler`, the other TIMING row, is READ and not ported** (D92). Its citations are in `plan.md`;
  nothing in this packet is evidence about it.

---

## 7. Per-candidate refusals, for the record

The escape clause was not taken, but the evidence it asked for was gathered anyway and belongs here:
if the next packet wants a module from GAMES & CARDS or IMMERSION, this is what it is up against.

| Group | Row | Blocker |
|---|---|---|
| GAMES & CARDS | Bubble Pop | 4918 lines of spawn timer plus per-bubble `DispatcherTimer` hops (`Services/BubbleService.cs:192,392,430,796,1779`) driving **clickable** moving windows. D84's class, plus windows that must CATCH clicks — which the port's overlay refuses by design |
| | Bubble Count | needs **video playback** and interactive message windows (`Services/BubbleCountService.cs:30-39`) |
| | Lock Card | `LockCardWindow.ShowOnAllMonitors(phrase, repeats, strict, isTest, voice)` (`Services/LockCard/LockCardService.cs:299`) — an input-capturing modal on every monitor |
| | Bouncing Text | already refused with evidence at SP-106 (D83/D84) |
| IMMERSION | Mind Wipe | **reachable**, and the runner-up. NAudio one-shots at random intervals (`Services/LockCard/MindWipeService.cs:18-30`) — a PACED module `PacedSessionEffect<TFiring>` fits, so it tests the audio capability rather than the spine |
| | Brain Drain | **HALF reachable, and the halves must not be conflated.** Its AUDIO half is the same paced-NAudio shape as Mind Wipe (`Services/LockCard/BrainDrainService.cs:13-30`; `DispatcherTimer` + `WaveOutEvent`, no window, no capture), engine-started at `MainWindow.StartStop.cs:243` and stopped at `:342` exactly as Mind Wipe is — **so its audio half is reachable today.** Its VISUAL half is not: the SAME `BrainDrainEnabled` flag also starts a desktop-wide blur/melt overlay (`Services/Notifications/OverlayService.cs:382-386`), whose strength dial the shipping panel itself labels the "VISUAL half" (`Views/Controls/Studio/BrainDrainFeatureControl.xaml.cs:170-174`). Both of its routes are out of this port's reach: the compositor route (`Services/Compositor/BrainDrainCapturePump.cs`, `Services/Compositor/BrainDrainLayer.cs` — **shipping tree**, not `CCP.*`) and the fallback, which runs a **30-60 FPS screen-capture timer** feeding per-screen blur windows (`OverlayService.cs:1965-1995`). Blurring what is BEHIND an overlay needs a desktop read-back this port has no capability for at all, and doing it per frame is D84's class on top |
| | Haptics | device backends — Buttplug/Intiface and Lovense (`Services/Haptics/ButtplugProvider.cs`, `LovenseProvider.cs`). A capability the port does not have, and the rack's one paid row (`StudioTabView.xaml.cs:528`, `tier: 1`) |
| TIMING | Scheduler | structurally outside the spine: it starts the engine from OUTSIDE a session (`MainWindow.StartStop.cs:562-620`), needs tray minimize plus notification, and runs when nothing is running. D92 |

**The honest summary for the board, and its SCOPE is the other three groups only.** This table covers
the **eight** unported rows in GAMES & CARDS, IMMERSION and TIMING. It is **not** a whole-rack claim:
WPF's EFFECTS group has two further unported rows the port has never surveyed here — **Mandatory
Video** (`StudioTabView.xaml.cs:486`) and **Visuals** (`:496`, which upstream itself gives no dot
because it has no single master toggle, `:494-496`). Ten rows are unported in all; eight are in
scope below.

Of those eight: **one (Mind Wipe) is reachable today** and is a repeat of a proven shape;
**one (Brain Drain) is HALF reachable** — its paced-audio half is, its desktop-blur half is not;
three need input-capturing windows, one needs video, one needs a device stack, one needs a
per-pixel-alpha overlay, and one is not a session module at all.

**What a board should act on.** An audio-capability packet closes **one row outright and the audio
half of a second**, and would have to name Brain Drain's desktop blur as a separate, still-open
capability gap rather than land the row as complete. That is the honest arithmetic, and it is neither
the "one row" this record first claimed nor the "two rows" the final review proposed.
**The port's next gap after this packet really is a capability, not a module.**

---

## 8. Files changed

**Product — new**
`Effects/RampCurves.cs` (the `RampCurve` enum and `ApplyCurve`, ported from `Helpers/RampCurves.cs:47-73`),
`Effects/IntensityDial.cs` (`IIntensityDial` and its two adapters),
`Effects/IntensityRampEffect.cs` (the module),
`Session/IntensityRampPresetDocument.cs` (its persisted dials, clamps as WPF's),
`Views/Pages/RampPanelNotices.cs` (its two panel sentences, where the deferred extraction would put them).

**Product — changed**
`Session/EffectReasonCodes.cs` (three codes, additive),
`Session/SessionParticipant.cs` (composes the fifth module, its store, its two dials, its dispatch,
and wires `Completed` to `Engine.Stop()`),
`Views/Pages/StudioPage.axaml` + `.axaml.cs` (the TIMING group, the row's dot and quick-toggle, the
panel and its six dials).

**Nothing else in the product was touched.** `OwnedSessionEffect.cs`, `PacedSessionEffect.cs`,
`SessionEffect.cs`, `SessionEngine.cs`, `OverlaySurfaceSet.cs` and the four landed modules are
byte-identical to base.

**Tests — new**
`IntensityRampEffectTests.cs` (**27** TRX results), `NonDrawingEffectSpineTests.cs` (**12**).

**Tests — changed**
`StudioSurfaceNoticeTests.cs` (**+11**: a 7-row theory and 4 facts for the non-drawing panel),
`ContinuousEffectSpineTests.cs` (rack order gains a fifth member; the refusal list gains the ramp — 0
count change), `SecondEffectSpineTests.cs` (the refusal list gains the ramp — 0 count change),
`CcpClient.HeadlessTests/StudioRackHeadlessTests.cs` (**+5**, and two landed facts' row lists extended
from four names to five).

**27 + 12 + 11 = 50 unit**, and **95 − 90 = +5 headless** — measured per file with `--filter` against
a stashed baseline.

**Docs** `client/docs/wpf-surface-reachability.md` (D92–D100).

---

## 9. THE CODE-REVIEW REVISION (REVISE -> three blockers, all addressed)

### 9.1 D95 asserted an upstream bug that does not exist — CORRECTED

The first draft claimed WPF loses the user's opacity when the window closes mid-ramp, citing the bare
`_rampTimer?.Stop()` at `MainWindow/MainWindow.WindowChrome.cs:167`. **That was false**, and the
review was right. Verified at the source before correcting:

- `:167` runs only inside `if (_exitRequested)` (`MainWindow/MainWindow.WindowChrome.cs:137`).
- All three writers of `_exitRequested` restore first: the tray Exit handler
  (`MainWindow/MainWindow.xaml.cs:327-328`, `_exitRequested = true; if (_isRunning) StopEngine();`),
  the in-app Exit button (`MainWindow/MainWindow.Settings.cs:454-462`, `StopEngine()` inside
  `if (_isRunning)` and the flag after it), and the double-panic exit
  (`MainWindow/MainWindow.xaml.cs:1207,1230`, gated on `!_isRunning` with `StopEngine()` already run
  at `:1169`).
- `StopEngine` -> `StopRampTimer` (`MainWindow/MainWindow.StartStop.cs:388` -> `:437-479`) **IS** the
  restore, and it runs before `SaveSettings()` on every one of those paths.
- `:167` is a defensive "Stop ALL timers" sweep (`:165`) over a timer already nulled at
  `MainWindow.StartStop.cs:440`. `SaveSettings()` is at `:163`, four lines before it, not five.

**The product is unaffected.** The split write is independently correct for a port-side reason that
has no upstream counterpart: here a post is not delivered once the dispatcher is down, and
`PersistenceStore.FlushAsync` runs after `Engine.Stop()`.
`TheWriteIsSYNCHRONOUSAndTheReApplyIsDISPATCHED_SoARestoreSurvivesADispatcherThatIsDown` proves that
property without needing any claim about WPF. Corrected in all six places the claim appeared: the D95
row, `Effects/IntensityDial.cs`, `Effects/IntensityRampEffect.cs`, this record's §1.4,
`IntensityRampEffectTests.cs` and `NonDrawingEffectSpineTests.cs`.

**What went wrong in the method, not just in the fact.** I read one line and inferred a control flow
around it. Every other WPF claim in this packet was taken from a body I had read end to end; this one
was taken from a `grep` hit. **A citation is only as good as the enclosing scope you read with it.**

### 9.2 The ceiling clamp was pinned by nothing — FIXED, and the sweep is §5.3

See §5.2 and §5.3. Two holes of the same shape, both fixed, both proved by mutation; one further
candidate proved sound the same way rather than by argument.

### 9.3 The ramp's notice theory could not fail — FIXED

`TheRampsLiveLineSaysADIFFERENTTrueThingInEveryStateItCanBeIn` asserted only non-blankness and a
trailing full stop, so a `DescribeRampState` returning one constant would have passed all seven rows.
Both landed siblings in the same file carry a distinctness loop plus SP-105's final-review rule, and
that guard exists because SP-105 shipped exactly this bug. The theory now:

- builds every other state's sentence through a `RampLineFor` helper and asserts **no two states share
  a sentence**;
- asserts every RUNNING state starts with `"Running"` and never says `"When a session starts"`, and
  every non-running state never says `"Running"`.

**One product line changed with it, and it is a real improvement.** The finished-ramp sentence used to
open `"Finished:"`. At full progress the dot reads **Live** — that is this packet's own headline
finding — so a line opening "Finished" contradicted the dot on the same row. It now opens
`"Running: the climb has FINISHED at N x ..."`. The panel and the dot agree, which is the property the
three-state design exists to enforce.

`RampLiveState` was also asserted by no test in either project; the headless panel fact now pins its
exact rendered sentence.

### 9.4 Citation drift — CORRECTED, and two of the review's corrections were themselves wrong

Systematic 1-2 line drift throughout, behaviour correct everywhere. Every citation re-derived from the
source rather than taken on trust, which caught two errors in the review's own list:

| Citation | Review said | Source says | Kept |
|---|---|---|---|
| `AppSettings.cs:2675` (spiral opacity clamp) | should be `:2674` | `:2674` is the getter; the `Math.Clamp(value, 0, 100)` setter is `:2675` | **mine** |
| `AppSettings.cs:3737` (pink opacity clamp) | should be `:3736` | `:3736` is the getter; the `Math.Clamp(value, 0, 50)` setter is `:3737` | **mine** |

Everything else in the review's list was correct and is applied, plus several the review did not list
that the same re-derivation caught: the flash cap `:508`->`:509`, the auto-stop condition
`:546`->`:547` and its block `:546-554`->`:547-555`, the notification `:550`->`:552`, the tick
`:481-539`->`:481-556`, the two link branches `:514-519`/`:520-524`->`:513-519`/`:521-525`, the
haptics tier row `:527`->`:528`, the ramp settings region `:2575-2641`->`:2574-2640` and every
property inside it, all six panel handlers, the combo default `:53`->`:54` and the write default
`:135`->`:133`. D96's `AppSettings.cs:2469` was the `SchedulerMultiplier` getter and is replaced by
the two setters the row is actually about (`:2675`, `:3737`).

**`plan.md` — what was actually done to it, stated narrowly.** Its line-number citations were
corrected, and its predictions and refusals are untouched. It is **not** true that every number in it
now points at the right line by correction alone: `plan.md:109` carried the refuted D95 claim and
both of its wrong numbers, and the first revision edited the paragraph directly above it without
touching it. That line is now **ANNOTATED IN PLACE** — the sentence struck, the false premise named,
and a pointer to §9.1 — rather than rewritten, so the checkpoint still shows what was planned and on
what mistaken premise. An earlier draft of this paragraph claimed the file had simply been corrected;
that was false and is narrowed here.

### 9.5 THE FINAL REVIEW, and the same class a THIRD and FOURTH time

Two more documentation blockers, neither in product code, neither able to move the floor. Both are
§5.2's rule — *an expected value produced by a collaborator that was not allowed to produce it* —
applied outside the test suite, which is where I had stopped looking.

**Third occurrence: a refusal sourced from a forbidden tree.** §7's Brain Drain row rested its
blocker on `CCP.Avalonia/Compositor/BrainDrainScreenCapture.cs`, and my own row admitted the file
"exists only in the `CCP.*` tree". `docs/constitution.md` makes `CCP.*` failure-and-lessons evidence
only, so **it cannot establish a requirement for `client/`**. The requirement was produced by a
collaborator with no standing — exactly the §5.2 shape, one level up from an assertion.

**But re-deriving it from the SHIPPING tree did not vindicate the review's proposed fix either**, and
this is why the rule is "re-derive", not "accept the correction". The final review concluded Brain
Drain is fully reachable and the count should go one → two. Source says otherwise: the same
`BrainDrainEnabled` flag that starts the audio service also starts a **desktop-wide blur/melt
overlay** (`Services/Notifications/OverlayService.cs:382-386`), the shipping panel labels its dial the
"VISUAL half" in as many words (`Views/Controls/Studio/BrainDrainFeatureControl.xaml.cs:170-174`), the
compositor files are in the **shipping** tree after all (`Services/Compositor/BrainDrainCapturePump.cs`,
`Services/Compositor/BrainDrainLayer.cs`), and the non-compositor fallback runs a **30-60 FPS
screen-capture timer** feeding per-screen blur windows (`OverlayService.cs:1965-1995`). So the row is
**half** reachable, the count is one row plus one half, and §7 now says so. The strategic conclusion —
audio is the next gate — survives all three versions of the arithmetic.

**Fourth occurrence: six named sites corrected, the class not swept.** §9.1 corrected the false D95
claim everywhere the review named it and nowhere else. `plan.md:109` carried the same sentence and
both wrong numbers, and the first revision edited the paragraph immediately above it — so the file was
open and the false line was stepped over. It is now annotated in place (§9.4).

**The lesson, stated once for the packet.** §5.1 found a hole and I did not sweep. §5.2 found the same
class and I swept the test suite — only the test suite. §9.6 found it twice more, in a refusal's
evidence and in prose. **A sweep bounded by where the last instance was found is not a sweep.** The
rule generalises past assertions to any claim: *when what justifies a statement could have been
produced by something not entitled to produce it, the statement is unproven* — whether that something
is a test double's clamp, a forbidden source tree, or an earlier draft of my own document.

### 9.6 The concurrency residual — NOT a blocker, and §4 CORRECTED rather than pinned

**No pin is added, and §4 is corrected instead.** The final review verified the lock graph
independently and found the code **safer than §4 claimed**: `PersistenceStore.Mutate`
(`Persistence/PersistenceStore.cs:220-228`) is a leaf that raises nothing, `Reapply` is always
dispatched so `targetEffect.Gate` is never reached under a ramp lock, `_hold` is a verified leaf, and
`SettingsReplaced` (`PersistenceStore.cs:243`) has **zero** subscribers in `client/src`. The
four-deep chain §4 described therefore **cannot form**, and a pin would assert a property with no
counter-shape to catch.

§4 now says that, names the two-deep chain that really exists, and carries the **trip-wire** that
turns the follow-up back into a required pin: a `SettingsReplaced` subscriber on either target store,
an `IIntensityDial.Write` that grows past a bare `Mutate`, or a synchronous `Reapply`. It remains a
follow-up row at land, and it remains a reading of the graph rather than a measurement — correcting
an overstatement does not upgrade the evidence class.
