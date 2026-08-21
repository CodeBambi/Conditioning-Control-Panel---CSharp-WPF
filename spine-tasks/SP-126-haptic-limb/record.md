# SP-126 — the haptic limb, built. Option C+, no loop, and nothing moves.

Branch `lane/SP-126-haptic-limb`. Plan and checkpoint grants: `plan.md` in this folder.
Divergences: `client/docs/wpf-surface-reachability.md` **D210-D218**.

**Observed floor: 2379 unit / 144 headless. Pin: 2332 / 144. Declared delta: +47 unit / 0 headless
(`floor-delta.json`).** 2332 + 47 = 2379, which is exactly the observed total; the floor gate
therefore reports a total that does not match the pin, and that is the expected state.
Warnings gate: **0 warnings, 0 errors across 4 projects**, forced non-incremental.

---

## 1. THE C+ DECISION, RE-DERIVED FROM THE BYTES BEFORE IT WAS BUILT

I did not take SP-120's summary on trust. Every link was read at the line:

| link | source |
|---|---|
| the flash ladder posts at priority 1 | `HapticService.cs:786` — `HapticPatterns.Render(rule.Mode, intensity, 250, priority: 1, target: rule.Target)` |
| the subliminal pulse posts at priority 1 | `HapticService.cs:880` — `PostEvent(HapticEventKind.SubliminalTrigger, null, duration, null, 1)` |
| the SUM is within a priority group | `HapticMixer.cs:487-503` |
| the MAX is across groups, then over the floor | `:502`, `:506` |
| the master arithmetic | `:509-518` |
| the shipped defaults | `Models/HapticSettings.cs:29` (0.7), `HapticMixer.cs:77` (0.70), `:83` (0.06), `:79` (4), `:75` (800 ms); event rows default `Enabled=true, Intensity=0.5` (`HapticSettings.cs:741-742`), seeded from `:46`, `:50`, `:53` |

Worked at those defaults: two overlapping 0.5 transients **sum** to 1.0, scale to 0.70 and hit the
cap; **MAX** gives 0.5, scales to 0.35 and does not. **A factor of two on the level a person feels.**

**And it is reachable TWICE, not once.** The packet named the flash-with-subliminal pair. There is a
second instance the packet did not name and it is stronger, because it lives inside ONE module:
**bounces post at priority 0** (`HapticService.cs:821`) **and therefore sum with each other.** A
bounce renders as a 158 ms envelope, so two wall hits inside that window — routine at a high speed
setting in a narrow field — combine upstream and would MAX under option C.
`TwoBouncesInsideOneEnvelopeAlsoSUM_TheSecondReachableInstanceOfTheSameRule` pins it.

**So C+ is right and I did not stop.** The evaluator is also buildable without a timer, which was the
other stop condition: evaluation is a pure function of (live envelopes, layer values, instant), and
the wake-up set is the union of the envelopes' own sample instants rather than a poll.

---

## 2. WHAT WAS BUILT

Three new files under `client/src/CcpClient.Desktop/Haptics/`:

- **`HapticEnvelope.cs`** — `HapticPulse` (upstream's `ActivePulse.Envelope`, `HapticMixer.cs:1214-1224`,
  including the trailing clamp that stops a hard edge rendering at full level past its end),
  `HapticStep`, `HapticShapes.Render` (`HapticPatterns.cs:42-134`), `HapticMix` (the C+ evaluator as
  pure functions), and `HapticEnvelopes` (one factory per wired site).
- **`IHapticLimb.cs`** — five verbs, each naming a MOMENT and never a level, a duration, a shape or a
  priority. **No no-op implementation exists**: consumers hold `IHapticLimb?` and call through `?.`,
  so "this build wires no limb" and "the limb decided nothing" stay different facts.
- **`HapticLimb.cs`** — the evaluator, the scheduler, the concurrency cap, the soft-ramped floor, the
  central gate and the send path.

**No 10 Hz loop.** Upstream's loop is the SENDER: the Lovense LAN rate limit (`HapticMixer.cs:68-70`),
the 1 s unchanged-target refresh (`:86-92`) and the 2 s zero re-assert (`:93-97`). All three are
delivery, and SP-119's seam already assigns delivery to the provider
(`HapticContracts.cs:70-73`, quoted on `IHapticSink`). Each posted envelope schedules one-shot wakes
at its start, its crest, its decay edge, its end and upstream's own 100 ms grid between them; at each
wake EVERY live envelope is evaluated at that same instant. **Nothing wakes while nothing is playing**
— asserted across a simulated minute of latched video layer, and again after a ladder has run out.

**Every wake carries the instant it stands for** and never asks the clock on arrival. A late timer
therefore renders the value its instant calls for instead of smearing the shape, and a fact can jump
a manual clock and still read the exact sequence a real run produces.

---

## 3. THE FIVE TRIGGER POINTS AND THE ENVELOPE EACH CARRIES

| port statement | census sites | verb | envelope, and the upstream line it is |
|---|---|---|---|
| `Effects/FlashSurfacePresenter.cs:307` (after `_surfaces.Place`) | 1, 2, 3 | `FlashPlaced()` | 8 rungs of `max(0.5·0.7^i, 0.06)` at 450 ms, each a 250 ms `Constant` render at priority 1 → **41 + 250 + 62 = 353 ms per rung, span 3503 ms** (`HapticService.cs:781-788`, `HapticPatterns.cs:123-128`). Each call REPLACES the ladder running (`:774-776`) |
| `Effects/MandatoryVideoEffect.cs:287` (after `_surface.Begin`, **Available arm only**) | 6, 7 | `VideoStarted()` | continuous layer at `max(0.5·0.1, 0.06)` = **0.06**, latched with no auto-zero (`HapticService.cs:832-851`) |
| `Effects/MandatoryVideoEffect.cs:416` (`OnClipEnded`) **and** `:337` (`OnDisarmed`) | 12 | `VideoStopped()` | layer → 0 (`HapticService.cs:853-858`) |
| `Effects/SubliminalsEffect.cs:210` (after `_surface?.Show`) | 14, 15 | `SubliminalShown(card.Text)` | `Pulse` at 0.5, priority 1, duration keyed off the phrase — 250 / 120 / 150 (`HapticService.cs:877-881`, `:899-909`) |
| `Effects/BouncingTextField.cs:230` (after `Bounces++`, above the 10 % re-roll) | 18 | `BounceHit()` | `Pulse` at 0.5, priority 0, 60 ms floored to a 130 ms on-time tap = 158 ms (`HapticService.cs:819-821`, `HapticPatterns.cs:36-65`) |

**Nine of the ten mappable sites are wired.** The tenth is site 4 (§6). The eight
`absent-by-decision` sites are untouched; I re-checked every one against its quoted decision and
found **none** I believe is reachable: all flash surfaces are `ClickThrough: true` so there is no pop
route of any kind, there is no script player, there are no attention checks, the seam carries no
inbound verb, and neither Bambi phrase is shown.

The clip start is on the **Available arm only** because upstream fires immediately under its own
*"Playback is REAL from here: a window (and, on the LibVLC path, a registered media player) exists"*
(`VideoService.cs:2567-2576`). A clip that was asked for and never appeared must not start a
vibration the user cannot account for.

---

## 4. THE PEAK-OF-SUM PROOF

The evidence is a `RecordingHapticSink` that **records and never transforms**. SP-108 is the named
hazard, so the double stores the exact `IReadOnlyList<HapticOutput>` reference it was handed and does
not clamp, round, quantise, coalesce, de-duplicate or drop.
`TheRecordingSinkRecordsRAWAndTransformsNOTHING` exists for nothing but that: it asserts
`Assert.Same` on the stored list and that two identical commands are TWO records, so a future "tidy"
of the double reds there instead of making every level assertion in the file vacuous.

**The fact itself** (`TwoOverlappingSamePriorityTransientsCombineToTheSUMMEDPeak_NotTheMax`): a flash
ladder and a subliminal pulse are commanded at the same instant and the clock is walked to instant
100, where the ladder's first rung is on its plateau (attack 41, hold 250) and the tap is on its own
(attack 8, hold 130). Both are at 0.5.

- **the recorded level is 0.70** — the limb's `LastLevel` and the sink's last recorded level, asserted
  separately so a computed-but-undelivered level cannot pass;
- **the fact also names what MAX would give**, `HapticMix.Finish(Math.Max(0.5, 0.5)) == 0.35`, asserts
  the delivered level is not that, and asserts the ratio is exactly 2. **A degradation to MAX reds
  this fact rather than quietly changing a number nobody reads.**

Four more facts hold the rule's edges: the priority-0 bounce pair sums the same way; a bounce against
a subliminal MAXes and does not sum; three overlapping halves stop at the cap because the group is
clamped at 1 first; and the pure `HapticMix.Transient` is pinned directly on both arms.

---

## 5. THE STOP, AND THE DIVERGENCE FROM D203

Upstream's `StopVideoBackgroundVibeAsync` has exactly one caller in the whole tree — inside
`Cleanup()` (`VideoService.cs:6580`). `ForceCleanup()` is the panic-key path (*"Panic key /
stuck-timer / session-switch teardown routes through ForceCleanup (not Cleanup)"*, `:1970-1971`) and
carries no haptic reference at all, while the comment three lines under the stop asserts the opposite
guarantee (`:6581-6583`). The start passes no `autoZeroMs`, so the layer latches unbounded
(`HapticService.cs:848`). **Panic-key a clip upstream and the toy keeps humming.**

**This port's stop is on BOTH of its teardown paths, and that is a deliberate improvement rather than
a copy.** I enumerated the paths rather than assuming them:

1. `OnClipEnded` (`:416`) — natural end, max-length cap, or the surface letting go.
2. `OnDisarmed` (`:337`) — `OwnedSessionEffect.Disarm` → `SessionEngine.Stop` → the session
   participant's own stop. **This is the port's real stop/panic/teardown funnel**, and it does NOT
   reach (1), because `Effects/VideoSurfacePresenter.cs:466` clears the ended callback rather than
   invoking it.
3. Generation cancellation reaches `ReleaseWork` only (`PacedSessionEffect:188`, sealed) — it drops
   the pending schedule and cannot take a playing clip down, so it is not a stop path for this layer.
   Named so the enumeration is complete rather than convenient.

Three facts hold it: one drives the natural end; one drives disarm mid-clip and asserts the ended
callback never ran (`EndedRaises == 0`); and one drives BOTH in one session and asserts two stops, so
**neither call site can be deleted while the other keeps the file green.**

Above all of it, `HapticParticipant.RunAllStopAsync` now calls `Limb.Clear()` FIRST and then
all-stops once — upstream's gate-close arm exactly (`HapticMixer.cs:264-265`), with the limb kept out
of the sink's stop budget so the one-shot latch still holds.

---

## 6. SITE 4 IS NOT WIRED, AND THAT IS THE PARITY ANSWER

The packet asked for the luminance layer's arithmetic. Reading the source first showed the feature's
own switch ships OFF: `Models/HapticSettings.cs:373` is `_luminanceSyncEnabled = false`, documented
at `:435-439` as *"Off by default until the Phase E UI exposes it. When off the hook is a single bool
test per flash"*, and `FlashService.cs:1603` returns before anything — *"the whole cost when off"*.
`HapticSettings.cs:526-527` says it twice more.

So at shipped defaults a placed flash upstream commands the ladder and nothing else, and upstream
does not even run the pixel scan. I raised it at the plan checkpoint and **the orchestrator overruled
its own instruction**: parity means matching the shipped default, not the available feature. Wiring
it ON would be a divergence upward on the one capability where louder-than-expected is the failure
that matters; wiring it behind an always-false constant would ship a Rec. 601 sampler the product
never runs. **D212.** The `SetLayer(level, autoZeroMs)` verb it would have used is built anyway and IS
reachable, because the video background layer uses it and ships enabled.

---

## 7. THE SWEEP — every predicate, and what it now says

- **The rack dot is unchanged and `Live` is still unreachable.** `HapticParticipant.Dot` is
  `Enabled && LastObservation is { Confirmed: true }`; a limb touches neither conjunct.
  `ABUSYLIMBDoesNotLightTheRackDot_BecauseLiveWouldMeanSomethingIsBeingSENT` enables the toggle,
  entitles the user, commands sixty moments and a video layer, and asserts the dot is `Off`.
  **`Views/**` was not opened.** What changed is only the REASON: from "the modules are silent" to
  "there is nothing to address", and the `Dot` remarks now say that instead of the old sentence.
- **Nothing moves.** `OnTheREALSinkAWholeSessionOfMomentsSendsABSOLUTELYNOTHING` runs the product
  `HapticSinkFactory.Create()` behind the limb: `Moments == 5`, `Refusals == 0`, `Sends == 0`,
  `Evaluations == EvaluationsWithNoDevice`, and the unadmitted sink's `RefusedCalls` is still 0 —
  it was never even touched.
- **The gate is central, both halves.** A transient is refused outright (`HapticMixer.cs:843`); a
  LAYER is stored and simply not delivered, because upstream's `SetLayer` does not consult the gate
  and its TICK does (`:661-678` against `:253-258`). D204 preserved: `Moments` still counts a refused
  moment. A gate that throws is read as CLOSED (`:198-203`).
- **The concurrency cap** evicts by rank and drops a newcomer that does not out-rank the weakest
  (`:894-915`). Upstream's corpse clause is folded into immediate expiry, which is equivalent because
  a corpse contributes 0 and is always evicted first; the equivalence is stated at the method.
- **The soft ramp** is slew-limited on rises and instant on falls, and schedules its own continuation
  so the climb cannot stall for want of a poll.
- **Teardown**: `Clear()` drops everything and cancels every wake without touching the sink; `Dispose`
  makes every verb a no-op; `HapticParticipant.StopAsync` releases the limb BEFORE the sink, so a
  wake can never level-set into a disposed object on a pool thread.
- **No wall-clock waits.** Every timing rides the injected `ISessionClock`; the two new test files
  contain no `Thread.Sleep`, no bare `Task.Delay` and no `DateTime`/`TickCount64` poll.

---

## 8. THE CENSUS EDIT — every number that moved, and why

Granted at the checkpoint as **DATA-ONLY**. No verdict, no quote, no decision row, no upstream
citation and no total was touched; `HapticSiteCensusTests.cs` needed no edit because it parses the
citations out of the document at test time. `HapticSiteCensusTests` is green (13/13).

| citation | from | to | why |
|---|---|---|---|
| `Effects/FlashSurfacePresenter.cs` (site 5's decision quote) | 294 | **303** | 9 lines added above it: the `Haptics` using, the field, the ctor parameter and its doc |
| `Effects/FlashSurfacePresenter.cs` (§4's click-through prose) | 297 | **306** | same |
| `Effects/FlashSurfacePresenter.cs` (sites 1-4's trigger) | 298 | **307** | same |
| `Effects/MandatoryVideoEffect.cs` (sites 6, 7) | 279 | **287** | 8 lines added above: using, field, ctor parameter and its doc |
| `Effects/MandatoryVideoEffect.cs` (site 12's second path, `OnDisarmed`) | 299 | **337** | the above, plus the `VideoStarted` arm and `OnDisarmed`'s D203 remarks. **Two places: the site row's notes AND §3.1's gathered list** |
| `Effects/MandatoryVideoEffect.cs` (sites 10, 11's decision quote) | 363 | **405** | the above |
| `Effects/MandatoryVideoEffect.cs` (site 12's trigger) | 374 | **416** | the above. **Two places: the trigger column AND the site's own notes**, which also carry the handler's DECLARATION line, 372 → **414** |
| `Effects/SubliminalsEffect.cs` (sites 16, 17's decision quote) | 56 | **57** | one `using` line |
| `Effects/SubliminalsEffect.cs` (sites 14, 15's trigger) | 197 | **210** | the using, the field, the ctor parameter and the ctor's param docs |
| `Effects/BouncingTextField.cs` (site 18's trigger) | 223 | **230** | the using, the field, the ctor parameter and its doc. **Two places: the trigger column AND the site's own notes** |
| `Effects/BouncingTextField.cs` (site 18's re-roll, in the notes) | 235 | **251** | the above plus the `BounceHit` call and its comment |
| `Haptics/IHapticSink.cs` (sites 8, 9, 13's decision quote) | 219 | **232** | the SP-126 paragraph rewrite, granted separately |
| `Haptics/IHapticSink.cs` (site 11's decision quote) | 221 | **235** | same |

`Effects/VideoSurfacePresenter.cs:466`, `Effects/FlashFrameSource.cs:111`/`:164`,
`Effects/BubblePopSurfacePresenter.cs:81-83` and `Haptics/HapticSettingsDocument.cs:47` were
re-checked and are unmoved.

**One residual I did NOT edit, because it is outside the grant.** The census's §6 reconciliation table
cites `Haptics/IHapticSink.cs:210-215` as where the superseded "THIRTEEN" figure still lived inside
the closed tree. The orchestrator opened that paragraph for correction and I rewrote it, so the
figure is no longer there — the range now lands on the CORRECTED text, which states in its own words
that the paragraph used to say thirteen and why. That cell is a claim about content rather than a
line number that moved, so it is reported here for whoever next opens the census rather than changed
under a data-only grant. **Review confirmed this was right to leave, with a reason worth keeping:** a
pure line-number edit — the only edit the grant permits — would have made that cell WORSE, because it
would then attribute the thirteen-figure to text that explicitly disclaims carrying it. A proper fix
needs prose, which is outside the grant.

---

## 9. WHAT ELSE I FOUND, THAT NOBODY ASKED FOR

- **The three subliminal durations render IDENTICALLY at the shipped mode.** `Pulse` renders
  `clamp(duration / 200, 1, 40)` taps and 250, 150 and 120 all yield ONE tap of the same shape,
  because 200 ms is the on-time floor. The wording only bites past 400 ms, which no phrase produces.
  The arithmetic is ported exactly anyway, and pinned in BOTH directions: the durations are asserted,
  and so is the fact that they render the same. **D216.**
- **The seam cannot fan across actuators.** `HapticServerObservation` carries device keys and no
  actuator inventory, so the limb addresses `ActuatorIndex 0` where upstream fans one intensity
  across every Vibrate feature (`ButtplugProvider.cs:264-278`). Ledger, not code: `IHapticSink.cs` is
  otherwise byte-identical, and closing this belongs to the packet that admits a provider — the same
  packet that could verify it against a two-motor device. **D217.**
- **Four of upstream's six vibration modes have no reachable caller here** and are absent rather than
  inert, because the mode is a per-event setting this port does not carry. **D214.**

---

## 10. WHAT THIS RECORD DOES NOT CLAIM

**No device moved and nobody felt anything.** No provider route is admitted, `SetOutputsAsync` is
never reached on a product path, and `HapticSinkFactory.DeviceManualGate` is exactly as undischarged
as it was at SP-119 — its last step is not dischargeable by any automated step on any platform at any
depth of API, because a haptic server reports what it believes it commanded over Bluetooth.

Everything above is **pure-logic unit work**. No headless frame was rendered and no headed capture
was taken, so nothing here verifies interaction, rendering, audio, focus, window behaviour or
animation, and `presentation-verified` and `draw-verified` are both untouched. The compile is a
compile. Linux is unchanged and unproven, and for this capability it refuses identically on both.

---

## 11. REVISION AFTER CODE REVIEW

Review returned REVISE on one blocking finding and three non-blocking items. Nothing in the
evaluator, the envelopes, the site set, the stop placement or the floor delta was re-opened, and the
count is unchanged at **+47 / 0**.

### BLOCKING — two ordering claims were asserted by nothing but their own names

`HapticParticipant.RunAllStopAsync` comments that the limb's state goes FIRST and the devices stop
after; `StopAsync` carries five lines about releasing the limb before the sink. **Review moved
`Limb.Clear()` below `Sink.StopAllAsync()` and swapped `Limb.Dispose()` with `Sink.Dispose()`, and
the whole haptic surface stayed green both times** — because both assertions were END-STATE
(`Layer == 0`, `ActivePulses == 0`), which the reversed order satisfies equally. Only the
`AndOnlyStopsThemOnce` half was genuinely pinned, by `Assert.Equal(1, sink.StopAlls)`.

That is not tidiness: **a reversed `Clear()` re-opens the exact window this packet's divergence
exists to close** — a wake firing between the all-stop and the clear would level-set a device the
all-stop had just silenced — and nothing would have said so.

**Fixed by observing the order AT THE INSTANT IT HAPPENS**, in the currency each claim is about,
using the idiom `HapticParticipant`'s own `sequence`/`AllStopSequence` pair already documents:

- `RecordingHapticSink` gained `ObserveAtStopAll` / `StateAtStopAll` and `ObserveAtDispose` /
  `StateAtDispose` — two probes invoked once each, inside `StopAllAsync` and inside
  `Dispose`. **It still records and still transforms nothing**; a probe reads the caller's state and
  changes none of it.
- **Witness 1** records what the limb was holding when the all-stop reached the sink:
  `layer=0;pulses=0`.
- **Witness 2** is behavioural rather than a flag: at the instant the sink is disposed, the probe
  tries to command the limb and reports `limb-still-accepts=False`. That is the hazard itself, not a
  proxy for it.
- The participant in this fact now takes a **`FrozenClock`**, so every wake is held and the fact is
  about the teardown order rather than about whatever a thread-pool timer managed to do behind it.
  The rack-dot fact takes the same clock, for the same reason.

**Both reversals were re-applied and both now RED, at different assertion lines** — line 374 for the
all-stop order and line 393 for the dispose order — so neither half can be deleted while the other
keeps the fact green. Reverted; 116/116 haptic facts green.

The test is renamed `TEARDOWNORDER_TheLimbIsClearedBEFORETheDevicesStopAndReleasedBEFORETheSink`,
which is now a claim the assertions enforce.

### Non-blocking, all three taken

1. **Three stale port line numbers survived in the census**, two of them in rows §8 above claimed
   were moved — each had been moved in ONE of the two places the number appears, and none is
   guard-pinned so nothing caught it. Fixed under the same data-only grant, and §8's rows now name
   both places explicitly: `haptic-limb-census.md:138` notes `:374`→`:416` and the handler's
   declaration `:372`→`:414`; `:144` notes `:223`→`:230`; `:152` §3.1 `(+ :299)`→`(+ :337)`.
   Re-enumerated every port citation in the document afterwards; the upstream shorthands (`:294`
   gaze pop, `:297` Bambi Freeze, and the rest) were confirmed upstream and left alone.
2. **The dangling cref** in `RecordingHapticSink.cs` pointed at `RecordingHapticSinkGuardTests`,
   which exists nowhere. Replaced with the real guard,
   `HapticLimbTests.TheRecordingSinkRecordsRAWAndTransformsNOTHING`, as plain `<c>` rather than a
   `cref` so it cannot rot silently again in a project that generates no documentation.
3. **`BouncingTextField.cs`'s bounce comment** said the 60 ms request widens "to a 130 ms tap"; 130
   is the ON-TIME and the envelope is 158 ms, which `APulseBelowTheOnTimeFloorIsWidenedToIt` already
   pins correctly. Reworded on ONE line, deliberately, so no citation below it moves.

**CORRECTED AT THE LAND (final review):** calling these `read-only` was wrong, and the sentence contradicted its own description four lines later. Witness 2 deliberately **writes** — it commands the limb to prove the limb no longer accepts, which is the hazard itself rather than a proxy for it. The purity guarantee that IS pinned is the sink's: `TheRecordingSinkRecordsRAWAndTransformsNOTHING` holds `Assert.Same` on the recorded list. **Nothing pins that a probe stays non-mutating** — `ObserveAtStopAll` and `ObserveAtDispose` are arbitrary `Func<string>` hooks the purity fact never sets. That gap is real and is filed on the board.
