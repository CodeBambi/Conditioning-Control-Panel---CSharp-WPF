# SP-105 — record

Branch `lane/SP-105-continuous-effect`, base `252b8509`.
Floor after the final-review revision: pin **1314 unit / 81 headless**; observed **1372 unit /
87 headless**; declared delta **+58 unit / +6 headless** (`floor-delta.json`).
1372 = 1314 + 58, and 87 = 81 + 6. Two skips, both pre-existing
(`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`,
`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`); none added.
Build: 0 errors, 0 warnings. `client/tests/floor/floor.json` was never opened.

---

## 0. THE HEADLINE — the spine and the scheduler were not the same thing

**`ISessionEffect` fit a continuous module unchanged. `PacedSessionEffect<TFiring>` did not, and it
had been standing in for the spine since SP-098.**

- **What did not fit.** Not the interface. Every member of `ISessionEffect` — `Id`, `Title`,
  `Enabled`, `Dot`, `Completion`, `Changed`, `SetEnabled`, `Arm`, `Disarm` — is implemented by the
  continuous module with no edit to any of them. What did not fit was the only *implementation* of
  it, which was also the only thing a new module could inherit from. `PacedSessionEffect` requires a
  subclass to supply `NextInterval()`, `Compose()`, `Stamp()` and `Deliver()`, takes an
  `ISessionClock` in its constructor, and answers "is this module running" with `ScheduleArmed` — a
  pending one-shot. **A module that is simply on has no interval, no firing, no clock and no
  one-shot.** All four would have had to be answered with a lie.
- **What forced the split.** Concretely, `Arm()`. In the old class `Arm()` *was* `ScheduleNext()`:
  begin a generation, park a completion, put a one-shot on the clock, return
  `Available("…the next firing is on the clock in Ns")`. Eleven of those parts are what ANY module
  needs in order to take a session; one of them — the one-shot — is what a *paced* module needs.
  There was no way to reuse the eleven without implementing the one, and implementing
  `NextInterval()` on a module with no interval is the fake timer this packet exists to catch.
- **What the new seam is.** `Session/OwnedSessionEffect.cs`: an abstract class implementing
  `ISessionEffect`, with **three** abstract members instead of four plus a clock —
  `WorkIsRunning` (is this module's work really running), `Engage(int generation)` (start or
  re-evaluate it) and `ReleaseWork()` (drop it, from any thread). It owns the idempotent arm, the
  generation and its parked completion, the disarm order, the eligibility gate and its two typed
  refusals, `Refresh()`, `Ready()`, the `Changed` signal, and two of the dot's three clauses.
- **What each kind now depends on.**

  | | Paced module | Continuous module |
  |---|---|---|
  | Base | `PacedSessionEffect<TFiring>`, itself an `OwnedSessionEffect` | `OwnedSessionEffect` directly |
  | Clock | **Required** — a constructor parameter; the schedule rides it | **None at all.** The module takes no `ISessionClock` |
  | `WorkIsRunning` | `ScheduleArmed` — a claim about the CLOCK | the surface's confirmed presence — a claim about the SCREEN |
  | `Engage` | put the next one-shot on the clock | put the layer on the screen |
  | `ReleaseWork` | dispose the pending one-shot; nothing on screen is touched | withdraw the surface — for this kind, releasing the work IS taking it off screen |
  | Contributes | identity, dial, interval, payload, pool, presenter | identity, dial, payload, presenter |
  | Arm can refuse because | the dial is off, the generation died | those two, **plus** the surface refused, the opacity is zero, or no UI thread is bound |

- **What did NOT change.** `ISessionEffect`, `EffectDotState`, `SessionEngine`, `EffectSignal`,
  `ScheduledFire`, and every behaviour of `FlashImagesEffect` and `SubliminalsEffect`. The paced
  class kept its timing verbatim and lost only the parts that were never about timing.

---

## 1. THE SECOND TEMPLATE VERDICT

**`ISessionEffect` is a spine. `PacedSessionEffect` was never it — it was one implementation wearing
the spine's clothes, and a continuous module proved that by needing eleven of its parts and none of
its timing. The split is the finding. The dot's three states survived, but only because the clause
that decides `Live` moved from the base to the module: for a paced effect `Live` is a claim about the
CLOCK, and for a continuous one it can only be a claim about the SCREEN.**

### 1.1 The prediction, and how it held

The plan (`plan.md` §3) was written before any product edit and predicted:

| Predicted | Outcome |
|---|---|
| `ISessionEffect` fits unchanged | **Held.** Not one member changed. `Id`, `Title`, `Enabled`, `Dot`, `Completion`, `Changed`, `SetEnabled`, `Arm`, `Disarm` — none of them names a clock, and the continuous module implements all nine with no interval, no firing type, no counter and no clock |
| `PacedSessionEffect` does not fit and must not be bent to | **Held.** `NextInterval`, `Compose`, `Stamp`, `Deliver`, `ScheduleArmed` and `Fire` are all meaningless for it; implementing `NextInterval()` at all would have been the fake timer the packet named |
| A third class holds what is genuinely shared | **Held**, as `Session/OwnedSessionEffect.cs` — 11 members, listed in §1.2 |
| No fourth `EffectDotState`; the DERIVATION moves | **Held.** `WorkIsRunning` became abstract; the enum is untouched |
| A continuous module's arm depends on a UI thread, which a paced one's does not | **Held**, and it needed its own reason code (`effect-no-ui-thread`) |
| Opacity 0 is a real divergence with a `Degraded` arm and an `Armed` dot | **Held** (D78) |

One prediction was **wrong**, and the correction is in §1.4: I planned a `ContinuousSessionEffect`
intermediate class and did not write one.

### 1.2 What is genuinely shared, and is now written once

`Session/OwnedSessionEffect.cs` — everything both kinds do identically:

| Shared | Why it is not timing |
|---|---|
| The idempotent first arm, and the owned generation behind it | "This module holds the session" is not "this module is due" |
| `Completion`, and the parked operation that terminates `Cancelled` | The registry is the authority on liveness for both |
| `Disarm`'s order: clear the flag, release the work, undo the visible half, cancel the generation, raise | WPF's stop order for both kinds (`FlashService.cs:367-380`, `OverlayService.cs:398-409`) |
| The eligibility gate and its two refusals (`effect-dial-off`, `effect-generation-cancelled`) | Upstream has this test twice and it says the same thing both times: nothing starts while the service is stopped or the dial is off (`FlashService.cs:538-546`; `OverlayService.cs:421,434-437`) |
| `Refresh()` — re-evaluate against the current dials | WPF has this entry point per kind too: `RefreshSchedule` (`FlashService.cs:527-531`) and `RefreshOverlays` (`OverlayService.cs:419-452`). **Same shape, different verb** |
| `Ready(...)`, the narrowing seat | Both kinds refuse |
| `Changed` through one `EffectSignal` | Producer-owned since SP-101 |
| The dot's skeleton: `Off` on the dial, else `Live` iff armed AND generation live AND `WorkIsRunning` | Two of the three clauses are the same for every module |

`PacedSessionEffect` kept exactly the timing: `Clock`, `NextInterval`, `ScheduleArmed`, `Compose`,
`Stamp`, `Deliver`, `Fire`, `Project`, the counter and the last firing. It went from 388 lines to 250
and **its behaviour did not change** — verified below, §5.

### 1.3 What a CONTINUOUS module needed that a paced one did not

Five things, each of which would have been invisible to a fourth paced module:

1. **A dot whose `Live` is a claim about the screen.** This is the whole finding. `PacedSessionEffect`
   answers `WorkIsRunning` with `ScheduleArmed` — a firing is on the clock — and Subliminals over an
   empty phrase pool is correctly `Live` under that rule even though every firing shows nothing. A
   continuous module has no clock between "armed" and "on screen", so it answers with the surface's
   own confirmed presence. **A dot derived from the dial, or from "Engage returned", would read
   `Live` on Linux, where the overlay refuses by design and nothing is drawn at all.** Two facts are
   each other's control here: `ADialThatIsOnIsNotEnough_TheDotIsLiveONLYWhileTheSurfaceIsReallyUp`
   and `ADegradedArmHereIsNOTTheSameAsSubliminalsDegradedArm_AndTheDotIsWhereTheyDiffer`.
2. **A placement with no lifetime.** `OverlaySurfaceSet.Place` took a `TimeSpan`; it now takes a
   `TimeSpan?`, where null means "hold until retired". That is the ONLY change to the shared surface
   set, and it is deliberately not a very large `TimeSpan`: a lifetime of ten hours is a timer that
   exists, that a stop has to remember to cancel, and that fires in a session nobody meant it to
   reach. Fact: `AContinuousSurfaceGetsNoLifetimeTimerAtAll_SoNoAmountOfClockTakesItDown`.
3. **"Release the work" and "take it off the screen" became the same act.** For a paced module they
   are two different things — drop the pending one-shot, and let the surfaces already up retire on
   their own lifetimes, which is why nothing hides when a flash module's dial goes off mid-session
   (`FlashService.cs:538-546` returns without closing a window). For a continuous module the work IS
   the surface, so the withdraw lives in `ReleaseWork` rather than `OnDisarmed`, and the dial-off
   path therefore takes the tint down — which is upstream's own reconcile (`OverlayService.cs:434-437`).
   **This was found by a test, not by reading:**
   `SwitchingTheDialOffMidSession_TakesTheTintDownThroughTheSAMEEligibilityGate` failed on the first
   run with the tint still up.
4. **An arm that depends on a UI thread.** A paced module schedules on a clock (no UI needed) and its
   draw is a later posted projection that skip-until-bound silently drops (contract §5.3). Here the
   arm and the draw are the same act, so "skipped" is the whole outcome and has to be sayable:
   `effect-no-ui-thread`. It is unreachable on the product path (phase 4 binds before the window
   shows, and both entry points are gestures) and it is what stops a unit-test run creating a real
   full-screen always-on-top window.
5. **A topmost cadence that matters.** Flash Images can take it or leave it and Subliminals passes
   `null` because a card is up for a fifth of a second. A layer that is up for a whole session loses
   the band to anything that later claims it, and WPF spends a periodic unconditional kick reclaiming
   it — 10 ticks of its 500 ms loop, so 5 s (`OverlayService.cs:666-671`). That number is now a
   constant with a citation on it.

### 1.4 Where the plan was WRONG, and what I did instead

**I predicted a `ContinuousSessionEffect` intermediate class and did not write one.** Once
`OwnedSessionEffect` existed, everything that would have gone into a continuous base was already
abstract on it: `WorkIsRunning`, `Engage`, `ReleaseWork`. An empty intermediate class with one
implementation is speculative generality — the shape SP-101's own verdict warned about in its "what
did NOT generalise, and should not" section — so `PinkFilterEffect` derives from
`OwnedSessionEffect` directly. **When Spiral Overlay lands** (the second continuous module, same
mechanism, same two lines of quick-toggle) there will be evidence for what a continuous base should
contain, and it can be extracted then from two real bodies rather than guessed at from one.

### 1.5 What a FOURTH effect should change

1. **A continuous module is three files** (effect, surface presenter, preset document) plus a rack
   row, against a paced module's four (it also needs a pool). Nothing suggests either should be fewer.
2. **`SessionEngine.ArmOutcomes` is still recorded and not yet rendered.** SP-101 said this was "fine
   for two and wrong for fifteen"; it is now three, and one of them can produce a `Degraded` arm the
   UI has no place to show (opacity 0 is visible in the module panel's own line, but a user who never
   opens that panel sees a session that reports itself running with a module that is drawing nothing).
   **This is the highest-value next UI row.**
3. **The dot has run out of room, and the next module will say so.** Three states are honest for
   these three. A module with a device precondition — Mandatory Video, or anything on the audio path
   — will arm `DependencyMissing`, and the row will read `Armed`, which is true but says nothing about
   why. The enum does not need a fourth member for that; the ROW needs somewhere to put the reason,
   which is the same surface as point 2.
4. **`Views/Pages/StudioPage.axaml.cs` is now the file that will not scale.** Three modules, three
   dot paints, three `Describe*` families and three `Describe*Surface` switches that differ only in
   nouns and tense. At six modules that is a per-module view model; at three, extracting it would be
   the same speculative generality as §1.4. **Named here so the fourth module's author extracts it
   from four real bodies rather than inheriting six.**
5. **Spiral Overlay is the natural fourth**, and for the opposite reason Pink Filter was the third:
   it is the same continuous mechanism with a real payload (an animated GIF, an asset library, a
   randomiser), so it tests whether the split holds when a continuous module has content.

---

## 2. THE RACK ROWS — D72 closed, and the grammar generalised

Three rows now carry the full grammar: **Flash Images**, **Subliminals** and **Pink Filter**. The
rack is in WPF's own order (`StudioTabView.xaml.cs:483-493`) with the unported rows removed:
Flash Images, Subliminals, Spiral Overlay, Pink Filter.

- **Left-click opens the module's panel**, and only that one.
- **Right-click quick-toggles**, through `SessionEngine.QuickToggle` — the same one dispatch entry the
  panel's Enable checkbox uses, so the two gestures cannot drift into two behaviours.
- **The dot reports what is running**, off each effect's own `Dot`.

**Spiral Overlay still has neither**, and that is still correct: D5/D6 stay open for exactly one row
now instead of one of two. WPF's own rule for a row it cannot wire honestly is to omit the dot
(`StudioTabView.xaml.cs:494-496`) and leave the gesture unhandled (`:659`).

**The toggles really toggle**, and it is proved with real headless input on the real controls, both
ways, per row: `RightClickOnTheSubliminalsRow_ReallyTurnsTheModuleOn_WhichNoGestureCouldDoBefore`
and `RightClickOnThePinkFilterRow_ReallyTurnsTheContinuousModuleOn_AndTheDotFollows` each click the
row twice and assert the dial and the rendered dot both move and both come back.

**The panels carry only dials the running effect really reads.** Subliminals: enable, messages per
minute, the active phrase count, the surface line. Pink Filter: enable, tint opacity, a read-only
swatch naming the tint in force, the surface line. Everything else is absent rather than greyed
(§9 D7), and what is absent is recorded as D81.

---

## 3. Divergences

D73-D82 in `client/docs/wpf-surface-reachability.md`, plus D4 corrected (it still said the rack had
one row) and **D72 closed**. The three that a reader should not skip:

- **D76** — WPF reclaims the topmost band two ways and the port ports one, because
  `IOverlayPresence.Reassert()` deliberately confirms nothing and there is no earned z-order read to
  condition the other on. Porting the 500 ms conditional pass unconditionally would be ten times
  upstream's `SetWindowPos` traffic claiming the same outcome.
- **D78** — at opacity 0 WPF puts an invisible full-screen always-on-top window on the desktop and
  the port puts nothing. Same thing seen (nothing); the port additionally does not leave a ghost
  window over the user's screen, and can say why the module is doing nothing.
- **D79** — the rendered alpha differs by at most one step of 255, because the overlay capability
  rounds where WPF truncates. **Not fixed here on purpose**: the rounding lives in
  `Overlay/OverlaySurfaceRequest.cs`, which is outside this packet's File Scope and is shared by all
  three drawing modules, and a per-module correction would put two alpha laws in the port.

---

## 4. Scope discovery, and one landed test that had to move

**`Persistence/**` is out of scope again**, so Pink Filter persists to its own
`session_pinkfilter.json` on the D71/SP-101 precedent. Same reasoning, unchanged, and still the
owner's call to fold together (D80).

**One landed assertion changed, and it is not a weakening.**
`SecondEffectSpineTests.ArmingAModuleWhoseDialIsOff_SaysSoInType_...` asserted
`ArmRefusals == ["subliminal"]`. Pink Filter also ships off (`AppSettings.cs:3726`), so a cold START
now declines twice. The expectation became `["subliminal", "pinkfilter"]` — the FACT is unchanged
(the session can name the holes rather than merely having them) and the assertion is stronger: two
modules declined for the same reason and both are named, in rack order. No other landed test was
touched, and no landed fact's meaning changed.

---

## 5. Proving it bites

**Five mutations across the whole packet**, every one reverted and the file compared byte-for-byte afterwards: the three in the table below, the fake timer (§8.1) and the live-state collapse (§9.1).

These three were each run against the whole unit suite as it stood at the time — **1362 results**, before the final review added ten:

| Mutation | Result |
|---|---|
| `PinkFilterEffect.ReleaseWork` no longer withdraws (**the module's OFF half**) | **5 FAILED**, all the new module's (2 `PinkFilterEffectTests`, 3 `ContinuousEffectSpineTests`); 1355 passed, including **every** `SessionSpineTests`, `FlashEffectTests`, `FlashSurfacePresenterTests`, `SubliminalEffectTests`, `SecondEffectSpineTests` and `SubliminalSurfacePresenterTests` fact |
| `PinkFilterEffect.Engage` returns `Available` without asking the surface (**the module's ON half**) | **14 FAILED**, all the new module's (8 `PinkFilterEffectTests`, 6 `ContinuousEffectSpineTests`); 1346 passed, again with every landed module's facts green |
| `OwnedSessionEffect.Disarm` no longer calls `ReleaseWork()` (**the SHARED body**) | **7 FAILED, spread across all three modules**: 3 `SessionSpineTests` (Flash Images), 1 `SecondEffectSpineTests` (Subliminals), 2 `ContinuousEffectSpineTests` + 1 `PinkFilterEffectTests` (Pink Filter) |

The third is the extraction check SP-101's reviewer ran, extended to three modules: **one line of
shared code, and at least one fact reds per module.** The first two are the packet's own step 6: the
new module's on/off breaks reds only the new module.

---

## 6. What this work does NOT prove

- **No headed capture was taken.** Nothing here claims a human has seen a pink tint.
  `presentation-verified` remains the orchestrator's gate and is not discharged by anything in this
  packet, including the headless facts.
- **Nothing here proves composition.** The pink filter's facts assert what the presenter asked the
  overlay to do — the request's bounds, opacity and click-through, and the frame's colour byte for
  byte — not what the OS then held. The `IOverlayPresence` behind them is a recording double in every
  fact in this packet; the real backend's evidence is SP-099/SP-100's, unchanged.
- **The headless rack facts drive no session.** They toggle dials with the engine stopped, on purpose:
  a headless test that started a session with the tint on would put a real full-screen always-on-top
  Win32 window over whoever ran it. So **no test anywhere proves the tint reaches a real screen.**
- **Linux is unproven and refuses by design.** On Linux the overlay backend returns
  `Unavailable(overlay-mechanism-absent)` with its own manual gate, so the module arms, reports that
  refusal verbatim, reads `Armed`, and shows nothing. That is asserted as a REFUSAL path
  (`ABackendThatRefusesToPresent_...`), never as Linux support.
- **Multi-monitor is unverified** (D73) — this machine reports one display.
- **The 5 s topmost cadence is proven as a schedule, not as a band.** The fact advances a clock and
  counts `Reassert()` calls; nothing here proves a real window regained a contested z-order, and
  `Reassert()` is documented as confirming nothing.
- **No interaction, focus, window behaviour or animation is verified** beyond the headless input
  routing already in the rack tests: no real pointer passes through the tint, and no capture shows a
  click landing under it.
- **The alpha byte the OS finally holds is not asserted anywhere in this packet** (see D79).

---

## 7. Files changed

**Product — new**
`Session/OwnedSessionEffect.cs`, `Session/PinkFilterPresetDocument.cs`,
`Effects/PinkFilterEffect.cs`, `Effects/PinkFilterSurfacePresenter.cs`, `Effects/PinkFilterTint.cs`.

**Product — changed**
`Session/PacedSessionEffect.cs` (now over the shared base; behaviour unchanged),
`Session/EffectReasonCodes.cs` (three codes), `Session/SessionParticipant.cs` (composes the third
module and its store), `Effects/OverlaySurfaceSet.cs` (nullable lifetime — the one change),
`Views/Pages/StudioPage.axaml` + `.axaml.cs` (two rack rows, two module panels; and at final review
the continuous module's live-state line split into one sentence per state, §9.1),
`Views/MainWindow.axaml.cs` (the stale duplicate `<summary>` at `:207-211` deleted — its text said
the marshalling lived there, directly above the block that says it moved to the producer at SP-101).

**Tests — new**
`PinkFilterEffectTests.cs` (**28** TRX results = 17 `[Fact]` plus three theories' 11 rows),
`ContinuousEffectSpineTests.cs` (**10**), `PinkFilterSurfacePresenterTests.cs` (**10**).

**Tests — changed**
`SecondEffectSpineTests.cs` (one expectation, §4),
`StudioSurfaceNoticeTests.cs` (**+10**: a 7-row theory and 3 facts for the live-state line, §9.1),
`PinkFilterEffectTests.cs` (the reflection fact renamed, §8.2 — no count change),
`ContinuousEffectSpineTests.cs` (one comment corrected, §9.3 — no count change),
`CcpClient.HeadlessTests/StudioRackHeadlessTests.cs` (+6 rack facts).

28 + 10 + 10 + 10 = **+58 unit** and **+6 headless**, measured per file with `--filter`, matching the
declared delta and the observed 1372 / 87.

**Docs** `client/docs/wpf-surface-reachability.md` (D73-D82, D72 closed, D4 corrected).


---

## 8. THE ANTI-FAKE-TIMER GUARD — how it was PROVED to bite

The named failure of this packet is wrapping a continuous effect in a timer to make it fit the
spine. Three things stand against that, and only the first is real evidence.

**8.1 The behavioural guard, and the mutation that proves it.** Two facts in
`ContinuousEffectSpineTests` are the protection, and they work by counting the SESSION CLOCK rather
than by inspecting a type:

- `PressingStart_ArmsAllThree_AndTheContinuousOneIsAlreadyRunningWithNoClockAtAll` asserts
  `rig.Clock.PendingCount == 2` with all three modules armed — one one-shot for Flash Images, one
  for Subliminals, **nothing for Pink Filter** — while simultaneously asserting the continuous
  module is already `Live` with its surface up.
- `NoAmountOfClockChangesTheContinuousModule_BecauseItIsNotPaced` advances twenty flash intervals
  and asserts the module's engagement and withdrawal counters have not moved.

**Executed, not asserted.** The fake timer was really written, in the shape the trap would take: an
optional `ISessionClock` added to `PinkFilterEffect`'s constructor, wired from `SessionParticipant`
with the same `sessionClock` the paced modules use, and a one-second one-shot re-armed inside
`Engage` that calls `EngageIfEligible()`. Result: **4 facts red** —
`PressingStart_ArmsAllThree_…` (PendingCount 3, expected 2),
`NoAmountOfClockChangesTheContinuousModule_…`,
`Stop_TakesTheTintOffAtOnce_AndNoAmountOfClockBringsAnyOfTheThreeBack`, and the structural tripwire —
with 34 of the 38 filtered results still passing. Reverted byte-identically: `PinkFilterEffect.cs`
md5 `79f0460dbbd5633f5005586564ce8139` and `SessionParticipant.cs` md5
`65a2982b2dcccd138f899e969f400cff` before and after, and `git status` shows neither file modified.

**The bound on that claim, stated.** These facts see any timer on the **session clock** — which is
the only clock a module can reach through the spine, and therefore the only shape the trap can
plausibly take. A module that constructed its own `SystemSessionClock` internally would evade the
count (it would still be caught by the tripwire in §8.2 only if it also took one in its
constructor). Nothing in this packet proves that case, and it is named here rather than glossed.

**8.2 The structural tripwire, renamed to what it is.** The reflection fact used to be called
`…SoAFakeTimerCannotBeSmuggledIn`, which over-claimed: `GetConstructors`/`BaseType` is defeated by a
field-constructed clock in one line. It is now
`TheContinuousModulesConstructorAndBaseClass_StillCarryNoClockAndNoPacedBase`, and its doc comment
names both behavioural facts above as the place the guard actually lives. It earns its keep for two
reasons: it fails at the line a reader is editing rather than three files away, and it pins the other
half of the split — that `PacedSessionEffect<T>` is a SIBLING of the continuous module under
`OwnedSessionEffect`, not its parent.

**8.3 The type system.** `PinkFilterEffect` cannot inherit `PacedSessionEffect` without implementing
`NextInterval()`, which is the moment a reviewer sees the lie. Not evidence, but it is why the trap
takes deliberate effort rather than drift.

---

## 9. FINAL-REVIEW REVISION — one blocker and two cheap fixes

### 9.1 BLOCKER — a running session was told to start a session

`StudioPage.DescribePinkFilterState` answered **every** `EffectDotState.Armed` with *"Armed. Nothing
is drawn until the session starts."* `OwnedSessionEffect` returns `Armed` for anything that is not
`Off` and not really running, and for this module "really running" is the SCREEN — so **a running
session whose overlay refused landed in that arm.** On Linux the overlay refuses by design, so that
is not an edge case there: every Linux user would have read, for the whole of every session, an
instruction to start a session they had already started.

That is a message misdescribing state, which is exactly what SP-101 was sent to fix one string
earlier (`StudioPage.axaml:152`). **I introduced it while writing the very finding that predicts
it**: §1.3 already said `Armed` "covers both 'no session yet' and 'the session is running and the
tint is not on screen'", and the words only covered the first. Worth stating plainly — the finding
was right and the sentence built on it was wrong, which is the failure mode of a packet that proves
something about a base class and then hand-writes the copy.

**The fix.** The `Armed` arm splits on state the page already holds — `_session.Engine.Running` and
the surface's last `CapabilityState` — so each situation names its own cause:

| Situation | Sentence |
|---|---|
| running, opacity 0 | "Running, but nothing is on your screen: the opacity is at 0%. Move the slider up." |
| running, surface refused | "Running, but nothing is on your screen: this build could not put the tint's overlay surface up (*reason-code*)." |
| running, nothing up and nothing recorded | "Running, but nothing is on your screen: the tint's overlay surface is not up." |
| not running, opacity 0 | "Armed, but the opacity is at 0%, so there is nothing to draw. Move the slider up." |
| not running | "Armed. Nothing is drawn until the session starts." |

The refusal names the reason **code**, not the detail: on Linux the detail is the backend's whole
manual gate, and `DescribePinkFilterSurface` already prints it verbatim in the notice directly
beneath. Saying it twice in one panel is not twice as honest.

**Pinned, and proved to bite.** `StudioSurfaceNoticeTests` gains a 7-row theory plus 3 facts
(**+10 unit**): every state a user can be in produces a non-blank and **mutually distinct** sentence;
every running state starts with "Running" and none contains "until the session starts"; no
non-running state contains "Running"; the refusal case names the surface and carries the reason code;
the zero-opacity case blames the dial and offers the remedy without borrowing the surface's wording;
and the unexplained case invents no cause. Mutation: the two running arms collapsed back into the
single blocked sentence — **7 results red** (5 theory rows + 2 facts), 1363 passed, every
`FlashImagesEffect`, `SubliminalsEffect` and `PinkFilterEffect` behavioural fact green, because this
is a wording defect and the mutation is confined to wording. Restored byte-identically
(`StudioPage.axaml.cs` md5 `842f1acd513dacdf6ba22f8edddf40eb` before and after).

**The paced siblings were NOT changed**, as the review directed and for the reason it gave: their
`WorkIsRunning` is `ScheduleArmed`, which is surface-independent, so a running session whose overlay
refuses still reads `Live` there — correctly, because their schedule really is on the clock.

`DescribePinkFilterState` became `public` to be reachable from the test project, matching the three
`Describe*Surface` methods beside it.

### 9.2 The reflection fact, renamed — see §8.2.

### 9.3 A comment that claimed more than its assertion

`ContinuousEffectSpineTests` said the withdraw "has already happened when Stop returns".
`ReleaseWork()` ends in `Signal.Post(_surface.Withdraw)` and `EffectSignal.Post` is "always a post,
never inline" by contract, so the green came from the rig's inline dispatch — and this record's own
threading paragraph (§1.3 item 3) says the opposite. **The assertion stands; the prose was wrong.**
It now says what is really proved: Stop *decides* synchronously and *queues* the teardown before it
returns, nothing is left scheduled, and nothing can re-place itself afterwards. On a real dispatcher
the withdraw lands on the UI thread's next turn.

### 9.4 Floor after the revision

Declared delta moved **+48 → +58 unit** (headless unchanged at **+6**) in the same commit as the ten
new facts. Observed **1372 / 87** against pin **1314 / 81**; 1372 = 1314 + 58 and 87 = 81 + 6.
`floor.json` was not opened.
