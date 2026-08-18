# SP-101 — record

Branch `lane/SP-101-second-effect`, base `f471455b`.
Floor: pin **1231 unit / 81 headless**; observed **1314 unit / 81 headless**; declared delta
**+83 unit / 0 headless** (`floor-delta.json`). 1314 = 1231 + 83. Two skips, both pre-existing
(`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`,
`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`); none added.
Build: 0 errors, 0 warnings.

---

## 1. THE TEMPLATE VERDICT

**The pattern holds — but not as it was written. Three of its six parts were per-module copies that
had to become shared machinery, and building the second module found three real defects in the first
one's body plus one in the clock every module will pace on. None of those four would have been found
by a third, fourth or fifteenth copy; they would have been multiplied by it.**

### 1.1 What generalised, and is now written once

| Shared | Was | Is |
|---|---|---|
| **The pacing arithmetic** — `Effects/EffectSchedule.cs` | `FlashSchedule`'s four functions | `IntervalLaw(SecondsPerUnit, VarianceFraction, MinimumSeconds)` + one implementation. `FlashSchedule` and `SubliminalSchedule` are named facades carrying only their own three numbers and their own citations |
| **The whole effect body** — `Session/PacedSessionEffect.cs` | ~200 lines inside `FlashImagesEffect` | Arm, disarm, generation, parked completion, the one-shot, the three stale-generation checks, the counter, the last firing, the derived dot, `RefreshSchedule`, `Changed`, and the UI projection. A module now contributes an identity, a dial, an interval and a payload |
| **The overlay slot pool** — `Effects/OverlaySurfaceSet.cs` | inside `FlashSurfacePresenter` | Presence recycling, present-then-paint, withdraw-on-failed-paint, verbatim `Last*` bookkeeping, the no-display refusal, an optional topmost cadence, `HideAll`/`Dispose` |
| **The change signal** — `Session/EffectSignal.cs` | two hand-written copies in `Views/` | One rule, in the producer |
| **The one-shot identity** — `Session/ScheduledFire.cs` | a raw `IDisposable` field | A token the callback can compare itself against |
| **GDI+ startup** — `Effects/GdiPlusRuntime.cs` | private inside the image decoder | Per process, for both rasterisers |

`FlashImagesEffect` went from 442 lines to 182, and every line that left it is now shared. **Its
behaviour did not change**: `SessionSpineTests`, `FlashEffectTests`, `FlashSurfacePresenterTests` and
`FlashDrawTests` — 87 facts — pass verbatim, with no edit to any of them beyond a test double's
signature.

### 1.2 What did NOT generalise, and should not

- **The pools.** `IFlashImagePool.Draw(count)` walks a filesystem with a cache and returns N paths
  with replacement; `ISubliminalPhrasePool.Draw()` returns one string from an in-memory dictionary
  and needs no cache at all (WPF re-reads its pool every firing, `SubliminalService.cs:206`). They
  share the word "pool". Forcing one generic seam would have given the subliminal pool an
  invalidation concept it has no use for.
- **The geometry.** A 40 %-of-monitor box with an overlap-avoiding placement roll against a
  full-screen rectangle. Nothing in common.
- **The rasterisers.** Image decode at display size vs. centred outlined text. Same library, opposite
  jobs.
- **The dials and their clamps.** Different ranges, different units, different defaults. Sharing them
  is how a clamp drifts.
- **The firing record.** `FlashEvent` carries an image count; `SubliminalEvent` carries a hold
  duration. Both are content-free, and the *rule* is shared while the shape is not — which is why the
  base is generic over the module's own firing type rather than over a common event base class.

### 1.3 Where the template FOUGHT, and what changed because of it

Three places, and all three were the template being wrong rather than the module being awkward:

1. **`Compose()` had to become nullable.** Flash Images counts a flash over an empty pool
   (`FlashService.cs:2589-2593`); Subliminals counts nothing at all over an empty phrase pool
   (`SubliminalService.cs:207-212` returns *before* the counter at `:611-612`) and still re-schedules
   (`:189-201`). A base that assumed every due firing produced an event was wrong on module two of
   fifteen. Both rules are now pinned, in facts that are each other's control
   (`ASubliminalOverAnEmptyPool_CountsNOTHING...` and
   `TheSameEmptyPoolOnFlashImages_STILLCountsAFlash...`) — so a later "tidy-up" into one counting rule
   reds one of them.
2. **The surface presenter could not be shared, only its core.** The first attempt at reuse was to
   parameterise `FlashSurfacePresenter`; it fell apart at the fourth parameter (stagger, cap,
   placement, lifetime, cadence). Extracting `OverlaySurfaceSet` and writing a 160-line second
   presenter over it was smaller and honest.
3. **The persisted dials had nowhere to go.** See §4 — a File Scope finding that turned into an
   architectural position.

### 1.4 What a THIRD effect should change

Concretely, in order of expected pain:

1. **A module is still four files** (effect, pool, presenter, preset document) plus a rack row. That
   is the irreducible part; nothing suggests it should be fewer.
2. **`PacedSessionEffect` assumes a self-paced module.** Spiral Overlay and Pink Filter — the next two
   EFFECTS rows — are **continuous**, not paced: WPF drives them through `App.Overlay.RefreshOverlays()`
   with no timer at all (`MainWindow.Presets.cs:1254-1255`). The third effect should therefore be a
   *continuous* one, deliberately, because it will show whether `ISessionEffect` (arm/disarm/dot/typed
   arm) is the real spine and `PacedSessionEffect` merely one implementation of it, or whether the
   spine has quietly been assumed to be a scheduler. **That is the highest-value third module**, and
   it is a stronger choice than a second paced one.
3. **`Ready(scheduled)` is the seat for a capability refusal and has one real user.** Subliminals
   narrows `Available` to `Degraded` for an empty pool. The first module with a device — Mandatory
   Video, or anything on the audio path — will return `DependencyMissing` from it. Nothing else needs
   to change for that to work, which was the point of doing it now.
4. **`SessionEngine.ArmOutcomes` is recorded and not yet rendered.** No UI shows which modules
   declined a session. That is fine for two and wrong for fifteen; the rack row packet should paint it.
5. **The rack row is the missing half.** Subliminals has no Studio row (divergence D72) because
   `Views/**` was open here for one narrow reason. A module that cannot be switched on from the UI is
   not finished, and the next Views packet should land the row *and* the ArmOutcomes surface together.

---

## 2. THE THREE HAZARDS — closed, per hazard

### H1 — `Arm()` returned `void` with no typed way to refuse. **CLOSED.**

`ISessionEffect.Arm()` returns `CapabilityState`, and it is load-bearing on day one rather than a seat
kept warm:

- **Flash Images** returns `Available` when a firing is on the clock, and
  `Unavailable(effect-dial-off)` when the module's dial is off. Before this, those two were *literally
  the same observation* — a session could arm fifteen modules, schedule three, and nothing could name
  which twelve did nothing.
- **Subliminals** additionally returns `Degraded("the schedule is armed and paced, and every firing
  will be a no-op", subliminal-no-active-phrase)`. Not `Unavailable` — the schedule really is running,
  which is WPF's own behaviour. Not `Available` — nothing will be shown. `Degraded` is the only honest
  one of the three, and it is a state Flash Images cannot produce.
- `SessionEngine.ArmOutcomes` keeps every outcome verbatim, per module id, and `ArmRefusals` lists the
  modules that did not take the session. The quick-toggle records a mid-session switch-off too.
- No refusal was invented. Neither ported module has a device precondition; the type exists so the
  ones that do (audio, webcam) have somewhere to say no, and `Ready(scheduled)` is the one-line
  override that does it.

Facts: `ArmingAModuleWhoseDialIsOff_SaysSoInType...`,
`AModuleThatIsPacedButCannotShowAnything_ArmsDEGRADED...`,
`PuttingAPhraseBackInThePool_TurnsTheDegradedArmIntoAnAvailableOne`.

### H2 — `Changed` fired on arbitrary threads, marshalling pushed onto every consumer. **CLOSED.**

`EffectSignal` owns the rule: inline when there is no UI to marshal to, inline when the caller is
already on the UI thread, posted otherwise — which is exactly the body both consumers hand-wrote and
agreed on. `Views/MainWindow.axaml.cs` and `Views/Pages/StudioPage.axaml.cs` deleted their copies.
`SessionEngine`'s own three raises (START, STOP, quick-toggle) go through it too — **found by a test,
not by reading**: the first version routed only the modules and the fact
`EveryChangedNotificationArrivesThroughTheBoundary...` reported 8 notifications and 5 marshalled ones.

One deliberate limit, stated: `Lifecycle/UiDispatch.cs` is outside this packet's File Scope, so the
thread-identity query is **injected** into `EffectSignal` (defaulting to
`Dispatcher.UIThread.CheckAccess`) rather than added to `UiDispatchBoundary`. It is consulted only
after `IsBound`, so a process with no Avalonia runtime never touches the dispatcher. If a later packet
puts a `CheckAccess` on the boundary, this parameter collapses into it.

### H3 — `FlashImagesEffect.Fire`'s benign handle race, in the file that gets copied. **CLOSED, and it was not benign.**

`_pending` now holds a `ScheduledFire` identity, and `Fire` opens with
`Interlocked.CompareExchange(ref _pending, null, token) != token → return`. Two things changed:

- **It no longer steals the live schedule's slot.** The old `Interlocked.Exchange(ref _pending, null)`
  cleared whatever was there. When a `RefreshSchedule` (the frequency slider) replaced the handle
  while the previous one-shot's callback was in flight, the stale callback nulled the *new* timer's
  slot: the dot then read `Armed` with a firing genuinely on the clock, and — the part that is not
  benign — the next `Disarm` found an empty slot, disposed nothing, and **left a live one-shot behind a
  stop**. It survived only because the downstream generation check refused to do the work.
- **A superseded callback no longer fires at all.** Letting it through would deliver at the *old* pace
  immediately after the user moved the slider, which is the one thing `RefreshSchedule` exists to
  prevent.

Fact: `ASupersededOneShotFiringLate_NeitherFires_NorStealsTheLiveSchedulesSlot`, driven by a clock
that retains every callback so the race is deterministic rather than waited for. Second fact:
`AOneShotThatIsCancelledWhileTheClockIsStillHandingBackItsHandle_IsDisposedAnyway`, for the other half
of the window (`ScheduledFire.Attach`).

### H4 — `SystemSessionClock` had zero coverage. **TAKEN, and it found a process-killer.**

Seven facts (`SystemSessionClockTests`), all on deterministic signals through `TestWait`; the negative
observation uses a scheduling barrier, never a wait, and no fact asserts elapsed time.

**The finding:** a callback that throws inside `Timer`'s pool-thread invocation is an *unhandled*
exception, and .NET ends the process. Measured, not theorised — the first version of that fact killed
the test host (`Catastrophic failure: System.InvalidOperationException`). Fifteen modules will do real
work inside these callbacks; `FlashImagePool` alone touches a filesystem and catches only `IOException`
and `UnauthorizedAccessException`. `SystemSessionClock` now takes an `onCallbackFault` reporter, catches,
reports and does not re-throw — **contained and reported, never swallowed**, because a silent catch is
the worse half of the same defect. `SessionParticipant` wires it to the host log.

---

## 3. Three defects the second module surfaced in the first one's body

Beyond H3, and each found by a test rather than by reading:

1. **A spent one-shot handle was never disposed.** `Fire` dropped the handle instead of disposing it,
   so every firing leaked one OS timer for the life of a session. Found by
   `ASupersededOneShotFiringLate...` asserting `LiveHandles == 0` after a disarm. Present since SP-098.
2. **`SessionEngine`'s own `Changed` bypassed the marshalling** (see H2).
3. **`SystemSessionClock` could kill the process** (see H4).

None of these is subliminal-specific. All three were in the code fourteen modules were about to copy.

---

## 4. Scope discovery — reported, resolved, and the owner's to overturn

Subliminals needs persisted dials. `SessionPresetDocument` lives in
`client/src/CcpClient.Desktop/Persistence/`, which is **outside this packet's File Scope**. I did not
edit it. The resolution is a per-module document, `Session/SubliminalPresetDocument.cs` →
`session_subliminal.json`, on the `AssetSelectionDocument` precedent that the session preset itself
cites for existing.

Stated honestly: half the reason is procedural. The other half is that it is arguably the right shape,
and the argument is not retrofitted — fifteen modules editing one document is the same chokepoint that
`floor.json` is, and it has a cost the floor does not: the persistence store's Degraded load path takes
the **whole** document to defaults, so one hand-broken phrase list would today also reset the user's
flash frequency. One file per module quarantines that, and lets a module land with no schema bump
anywhere else.

**This is the owner's call, not mine.** Recorded as divergence D71 with the follow-up named: fold it
into `session_preset.json` if one file is preferred. Nothing behavioural depends on the answer.

---

## 5. Proving it bites

Two mutations, each reverted, each run against the second module's facts *and* all of Flash Images':

| Mutation | Result |
|---|---|
| `SubliminalsEffect.OnDisarmed` no longer hides the card (**stop**) | `SecondEffectSpineTests.AfterStop_NoAmountOfClockMakesEitherModuleWorkAgain` **FAILED**; 88 others passed, including every `SessionSpineTests`, `FlashEffectTests` and `FlashSurfacePresenterTests` fact |
| `SubliminalSurfacePresenter.Show` never places the card (**draw**) | **10** `SubliminalSurfacePresenterTests` facts FAILED; 94 passed, including every Flash Images fact and `FlashDrawTests` |

Flash Images' facts are untouched by both, which is the property the packet asked for.

---

## 6. What this work does NOT prove

- **No headed capture was taken.** Nothing here claims a human has seen a subliminal.
  `presentation-verified` remains the orchestrator's gate and is not discharged.
- **The card's pixels are proven on Windows only**, in the pure-logic project, through the product's
  own rasteriser. On Linux the same fact asserts that nothing rasters and nothing throws — which is a
  real assertion, and is not evidence of a subliminal.
- **Nothing here proves composition.** `FlashDrawTests`' desktop-read instruments were not extended to
  the card; the subliminal facts assert what the presenter asked the overlay to do, not what the OS
  then held. The overlay capability behind it is SP-099/SP-100's evidence, unchanged.
- **No interaction, focus, window behaviour or animation is verified.** The module has no rack row
  (D72), so no gesture reaches it in the shipping UI; the fade does not exist (D65).
- **Multi-monitor is unverified** (D66) — this machine reports one display.
- **The `EffectSignal` marshalling is proven with an injected thread predicate**, not against a real
  Avalonia dispatcher on a real background thread. The headless suite exercises the bound path; a
  genuine off-UI-thread `Changed` under a live dispatcher is not directly asserted anywhere.
- **`SystemSessionClock`'s fault containment is proven for a synchronous throw.** An `async void`
  callback that faults after its first await would still escape, and no module writes one today.

---

## 7. Files changed

**Product — new**
`Effects/EffectSchedule.cs`, `Effects/SubliminalSchedule.cs`, `Effects/SubliminalsEffect.cs`,
`Effects/SubliminalPhrasePool.cs`, `Effects/SubliminalSurfacePresenter.cs`,
`Effects/SubliminalFrameSource.cs`, `Effects/OverlaySurfaceSet.cs`, `Effects/GdiPlusRuntime.cs`,
`Session/PacedSessionEffect.cs`, `Session/EffectSignal.cs`, `Session/ScheduledFire.cs`,
`Session/EffectReasonCodes.cs`, `Session/SubliminalPresetDocument.cs`.

**Product — changed**
`Effects/FlashImagesEffect.cs` (now over the shared base; behaviour unchanged),
`Effects/FlashSchedule.cs` (facade over the shared law), `Effects/FlashSurfacePresenter.cs` (over the
shared surface set; call order unchanged), `Effects/FlashFrameSource.cs` (GDI+ startup moved out),
`Session/SessionEffect.cs` (typed `Arm`), `Session/SessionEngine.cs` (arm outcomes, marshalled
`Changed`), `Session/SessionClock.cs` (fault containment), `Session/SessionParticipant.cs` (composes
the second module and its store), `Views/Pages/StudioPage.axaml` + `.axaml.cs` (the surface notice,
and the marshalling copy removed), `Views/MainWindow.axaml.cs` (the marshalling copy removed).

**Tests — new**
`SubliminalEffectTests.cs` (36), `SecondEffectSpineTests.cs` (18),
`SubliminalSurfacePresenterTests.cs` (17), `SystemSessionClockTests.cs` (7),
`StudioSurfaceNoticeTests.cs` (5), plus the non-test helper `SubliminalCardObservations.cs`.

**Tests — changed**
`SessionSpineTests.cs` and `FlashSurfacePresenterTests.cs` (test-double signatures only — no fact
touched), `CcpClient.HeadlessTests/StudioRackHeadlessTests.cs` (the renamed surface notice).

**Docs** `client/docs/wpf-surface-reachability.md` (D65–D72, and D47's Studio sentence closed).
