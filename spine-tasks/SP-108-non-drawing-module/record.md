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
| observable **without** an overlay surface | the mechanism is `settings.SpiralOpacity = newVal` and four siblings — no window, no draw (`MainWindow/MainWindow.StartStop.cs:503-539`). Upstream's own words at `:513-517`: "the settings write is the whole job now" |
| driven by **session progress** | `progress = Math.Min((DateTime.Now - _rampStartTime).TotalMinutes / duration, 1.0)` (`:484-493`) |
| **interacts with** an existing module | it moves `SpiralOpacity` (`:517`) and `PinkFilterOpacity` (`:521`) — precisely `SpiralPresetDocument.OpacityPercent` and `PinkFilterPresetDocument.OpacityPercent`, the dials the two landed continuous modules read |

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
(`AppSettings.cs:2590-2622`), is the state a freshly enabled ramp is really in.

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
   rules and, it turned out, different failure modes. The persisted write is synchronous and
   lock-safe; the re-apply touches a native overlay and is dispatched. **The split closes an upstream
   data-loss bug** (D95): WPF's window-close path stops the ramp timer without restoring
   (`MainWindow/MainWindow.WindowChrome.cs:167`), five lines after `SaveSettings()` at `:162`, so
   quitting mid-ramp persists the ramped opacity as though the user had chosen it. Here the value
   always comes back and only the re-tint of a window being destroyed anyway can be dropped.
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
   the user asked for it (`MainWindow.StartStop.cs:546-554`). A module cannot call the engine that
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
   GAMES & CARDS (every row needs input-capturing windows or video) and IMMERSION (two paced audio
   modules and one device backend). **The next module is a capability question, not a spine question**
   — see §7.
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
which is exactly the shape that produced SP-106's reverted lock-order inversion. Three properties
keep it acyclic, and they are written at the members rather than assumed:

1. **Each `AsyncOperationOwner` owns its own lock** (`Lifecycle/OperationRegistry.cs:122`), so the
   ramp's owner lock and a target module's are different objects.
2. **The dial adapters are strictly one-directional.** The ramp calls the target; no target ever calls
   the ramp. So the only chain the ramp can produce is
   `rampGate → targetStore.mutationGate → targetEffect.Gate → targetOwner.Gate`, and the only other
   thread in the picture (a UI dot repaint) takes a **suffix** of that chain.
3. **`_hold` is a LEAF lock.** Nothing in the class calls out of it while holding it — every external
   call (reading a dial, writing a dial, scheduling a tick) runs on a snapshot taken and released
   first — and nothing takes `OwnedSessionEffect.Gate` while holding it. The one order this class ever
   produces is `Gate → _hold`, which is the order `Dot` already produces.

**What this does NOT prove.** No test in this packet drives the ramp's tick and a dot repaint on two
real threads at once: every fact here is single-threaded by construction, exactly as SP-106 recorded
of its own suite. The argument above is a reading of the lock graph, not a measurement, and it is
recorded as such.

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
| | Brain Drain | same paced-audio shape (`Services/LockCard/BrainDrainService.cs:13-30`), plus a screen-capture compositor layer that exists only in the `CCP.*` tree |
| | Haptics | device backends — Buttplug/Intiface and Lovense (`Services/Haptics/ButtplugProvider.cs`, `LovenseProvider.cs`). A capability the port does not have, and the rack's one paid row (`StudioTabView.xaml.cs:527`, `tier: 1`) |
| TIMING | Scheduler | structurally outside the spine: it starts the engine from OUTSIDE a session (`MainWindow.StartStop.cs:562-620`), needs tray minimize plus notification, and runs when nothing is running. D92 |

**The honest summary for the board:** of the eight unported rows in the other three groups, **one**
(Mind Wipe) is reachable today and is a repeat of a proven shape; three need input-capturing windows,
one needs video, one needs a device stack, one needs a per-pixel-alpha overlay, and one is not a
session module at all. **The port's next gap after this packet really is a capability, not a module.**

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
