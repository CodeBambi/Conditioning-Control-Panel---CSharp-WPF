# SP-106 — record

Branch `lane/SP-106-moving-effect`, base `61be3b55`.
Floor: pin **1372 unit / 87 headless**; observed **1472 unit / 90 headless**; declared delta
**+100 unit / +3 headless** (`floor-delta.json`). 1472 = 1372 + 100 and 90 = 87 + 3, confirmed by
`node client/tests/floor/sum-deltas.mjs --check --packets SP-106-moving-effect`. Two skips, both
pre-existing (`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`,
`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`); none added.
Build: 0 errors, 0 warnings. `client/tests/floor/floor.json` was never opened.

---

## 0. THE HEADLINE — the module named in the packet could not be drawn, and the one that could gave a cleaner answer

Two findings, in the order they arrived.

**Bouncing Text is a poor fourth, on the packet's own named condition.** Reading
`Services/Subliminal/BouncingTextService.cs` shows three independent blockers, none of them fixable
inside this packet's File Scope — per-pixel alpha this port's overlay refuses by design (D83), a
motion that means re-`Present`ing sixty times a second on a path that momentarily clears
click-through to confirm itself (D84), and a 60 Hz cadence the confirm-everything `Paint` cannot be
spent at. The packet's escape clause was taken, with the evidence written down BEFORE the first
product edit (`plan.md` §1).

**Spiral Overlay was built instead, and the answer to the packet's real question is:**

> **`OwnedSessionEffect` carries per-frame work with NO scheduler in the module — because the
> cadence belongs to the SURFACE, and SP-105 had already put one there.**

`SpiralOverlayEffect` takes no `ISessionClock`, implements no interval and owns no timer. The frame
advance rides the injected clock inside `SpiralSurfacePresenter`, beside the topmost-band cadence the
tint's presenter already carried (`SessionParticipant.cs:116-119` is the one-line precedent). The
distinction that makes this honest rather than a relocation is the one the three modules before it
were each half of:

| | |
|---|---|
| **An INTERVAL that decides when a MODULE is due** | the module's, and `PacedSessionEffect<TFiring>`'s |
| **A CADENCE that keeps a SURFACE correct** | the surface's — reclaiming the topmost band, or turning the spiral |

A spiral is never "due". It is *on*, and what is on screen turns. So it derives from
`OwnedSessionEffect` directly, exactly as Pink Filter does, and nothing about the spine changed.

---

## 1. THE THIRD TEMPLATE VERDICT

**A moving module needed exactly two things neither a paced nor a static one did, and neither of
them was in the spine.**

1. **A frame path that is not a placement** — `OverlaySurfaceSet.Repaint`, one additive method.
2. **A `Live` that is a claim about CHANGE** — and it turned out that "on screen but frozen" is
   **two** states, only one of which is broken.

**The spine itself fits a fourth time, unchanged.** `ISessionEffect`: not one member edited.
`OwnedSessionEffect`: `WorkIsRunning` / `Engage` / `ReleaseWork` are exactly the three questions a
moving module has to answer, and it answers all three. `EffectDotState`: still three states, still no
fourth member. `SessionEngine`, `EffectSignal`, `ScheduledFire`, `PacedSessionEffect`: untouched.

### 1.1 The prediction, and how it held

`plan.md` §4 was written before any product edit:

| Predicted | Outcome |
|---|---|
| `ISessionEffect` fits unchanged (third time) | **Held.** No member edited |
| `OwnedSessionEffect` fits unchanged | **Held.** No member edited *for this module's sake* — see §4 for the one line that changed for a DEFECT it exposed |
| The module takes NO clock; the cadence is the presenter's | **Held.** `SpiralOverlayEffect`'s constructor has no `ISessionClock`, and the tripwire fact pins both halves — no clock in the module, and a clock in the presenter |
| `PacedSessionEffect` does not fit and must not be bent to | **Held.** Its `WorkIsRunning` is `ScheduleArmed`, a clock claim, which would read `Live` for a spiral the OS refused; its `ReleaseWork` drops a one-shot and leaves the screen alone, which is right for a flash and wrong for a layer up all session |
| **Exactly one** additive change to shared code: `OverlaySurfaceSet.Repaint` | **Held**, and it is the whole shared-code diff for the module |
| "I do not think a single boolean survives contact" (the dot) | **Held, and it was the interesting part** — see §1.3 |

**One thing the plan did not predict at all**, and it is the packet's other deliverable: a latent
defect in the shared body, found by a test rather than by reading (§4).

### 1.2 What a MOVING module needed that the other three did not

**1. A frame path.** `OverlaySurfaceSet.Place` always calls `Present`, and `Present` walks the OS's
whole top-level z-order and asks the window manager's hit test in **both polarities**, momentarily
clearing click-through to do it (`Overlay/Win32OverlayPresence.cs:547-576`). Twenty times a second
that is a full-screen window catching the user's clicks twenty times a second. So a frame advance is
a **paint and only a paint** — which is what the capability documented and nothing had used
("A caller shows a surface once and paints it", `Overlay/IOverlayPresence.cs:84-85`).
`OverlaySurfaceSet.Repaint` is that, with `Place`'s failure rule kept identical: a surface the OS
confirms is up and does NOT hold the frame comes down rather than sitting there holding stale pixels.
Fact: `TheLayerIsPresentedONCE_AndEveryFrameAfterThatIsAPaint` — one present, seven paints, and no
`SetClickThrough` anywhere in the call log.

**2. Two cadences on one clock, which must not become one.** The topmost band is WPF's 5 s
(`OverlayService.cs:666-671`) and lives inside `OverlaySurfaceSet`; the frame advance is the GIF's own
delay (`SpiralFrameDelay`, WPF's `_gifFrameTimer` at `:1372-1377`). A test drives both by hand.
Fact: `TheTopmostBandIsStillReAssertedOnWpfsOwnCadence_AlongsideTheFrames`.

**3. A release that is TWO acts.** A paced module drops a pending one-shot and leaves the screen
alone. A static continuous module withdraws a surface and has no timer. This one has both, and the
timer must die with the surface or a stopped session keeps repainting a window nobody can see. The
presenter does both inside `Withdraw` so a caller cannot separate them.

**4. A reason to test `Engaged` rather than `Showing` before releasing.** A spiral whose repaint
failed is no longer showing and still owns a timer. `PinkFilterEffect.ReleaseWork`'s guard
(`Showing`) would have leaked it.

**5. A decoder with a lifetime.** Flash and Subliminals each render ONE frame and are done, so their
frame sources are single calls. A clip that runs for a session needs an OPEN animation, and the
buffer it renders into is **reused** — a full-screen frame is about 8 MB, and allocating one twenty
times a second is 160 MB/s of large-object garbage for the length of a session. That is a documented
contract, not an accident, and it has a fact: `TheBufferIsREUSEDAcrossFrames_…`.

### 1.3 THE DOT'S THIRD MEANING

- Paced `Live` = a claim about the **CLOCK** (a firing is scheduled). SP-101/SP-105.
- Continuous `Live` = a claim about the **SCREEN** (a surface is confirmed up). SP-105.
- **Moving `Live` = a claim about the screen AND that it will be a DIFFERENT screen a moment from
  now.**

```
Running  =  a surface I placed is up
         && the last frame I painted was really held
         && ( this clip has ONE frame  ||  the next advance is scheduled )
```

**The third clause is conditional, and that is the whole subtlety.** WPF starts no frame timer for a
one-frame spiral (`OverlayService.cs:1370`), so a still image sitting there is the module working
exactly as asked. "On screen but frozen" is therefore **two states**:

| Situation | Dot | Panel says |
|---|---|---|
| Animated clip, frames advancing | `Live` | "Running: the spiral is turning on your screen, N frames on a loop…" |
| **One-frame file, motionless** | **`Live`** | "Running: your spiral is a single still frame, so it sits on your screen without turning. That is the file, not a fault." |
| **Animated clip, stopped** | **`Armed`** | "Running, and the spiral is on your screen — but it has **STOPPED TURNING**, so what you are looking at is a frozen frame." |

Demanding motion from the still one would make the dot lie in the *other* direction. Both are pinned,
and their sentences are asserted to share no vocabulary
(`ALayerThatIsOnScreenAndHasStoppedTurning_SaysSO_AndIsNotConfusedWithAStillSpiral`).

**The bound on the third clause, stated rather than glossed.** `_advance is not null` is a HELD
HANDLE — the same grade of evidence `PacedSessionEffect.ScheduleArmed` has carried since SP-098, and
`ISessionClock` exposes nothing to ask. A clock that silently dropped the callback would not be seen.
What covers the real failure is the behavioural clause above it: a frame the decoder cannot produce
drops `Running` while the layer is still up. Both facts exist and the bound is named in the test's own
words (`Running_ThirdClauseIsAHeldHandle_TheSameGradeOfEvidenceAsAPacedModulesScheduleArmed`).

### 1.4 What a FIFTH effect should change

1. **`Views/Pages/StudioPage.axaml.cs` is now the file that will not scale, and SP-105 said so at
   three modules.** It is four now: four dot paints, four `Describe*State` families and four
   `Describe*Surface` switches that differ only in nouns and tense. `DescribeSpiralState` has
   **eleven** arms where the tint's has seven. At four real bodies the extraction SP-105 deferred is
   now justified and should be the fifth module's first act.
2. **`SessionEngine.ArmOutcomes` is still recorded and not rendered**, and it is now worse: TWO of
   the four modules can arm `Degraded` with a cause the row cannot show. This is still the
   highest-value next UI row.
3. **The first-run experience of this module is "nothing happens", by design (D86).** The port
   bundles no art, so a fresh install has an empty spiral library and the panel says where to put a
   file. That is honest and it is also the module's whole visible behaviour out of the box; an owner
   decision about shipping art would change it.
4. **A per-frame module at 60 Hz is still unreachable** and D84 says exactly what would close it: a
   cheap overlay MOVE, or a per-pixel-alpha route that keeps an earned alpha read-back. Bouncing Text,
   Bubble Pop and every other moving-glyph module wait on that one capability.

---

## 2. THE RACK — D5/D6 CLOSE FOR THE WHOLE RACK

Four rows, and for the first time in the port **every one of them carries the full grammar**:
Flash Images, Subliminals, Spiral Overlay, Pink Filter, in WPF's own order
(`StudioTabView.xaml.cs:483-493`) with the unported Mandatory Video row closed up around.

- **Left-click opens the module's panel**, and only that one.
- **Right-click quick-toggles**, through the same one `SessionEngine.QuickToggle` entry the panel's
  Enable checkbox uses.
- **The dot reports what is running**, off each effect's own `Dot`.

Spiral Overlay is the **one ported module that ships ON** (`AppSettings.cs:2645`), so its row starts
lit where the other three start dark and its first right-click turns it OFF. That asymmetry is
upstream's and is asserted rather than normalised away.

Its panel carries the two dials the running effect really reads (enable, spiral opacity), the live
state, the library line naming the folder when there is nothing to draw, and the surface line. The
Loom button stays exactly where it was: that route is hop 2 of the Loom and is unaffected.

**Three landed headless facts changed, and none of them was weakened.** They asserted that this row
had no dot and no toggle, and they were right at the time. `TheSpiralRow_StillHasNoDotAndNoToggle_…`
became `TheSpiralRow_NowCarriesTheGrammarToo_AndD5D6CloseForTheWholeRack` and now asserts every row
has a dot; the loop in `TheRackIsInWpfsOrder_…` gained the fourth row;
`NavigationShellHeadlessTests.RightClickOnTheRackRow_OpensNoMenu_AndSelectsNothing` kept every
assertion it had (a right-click must open no menu, select nothing and navigate nowhere whether or not
there is a dial behind it) and only its comment changed, because the comment had become false.

---

## 3. Divergences

**D83–D91** in `client/docs/wpf-surface-reachability.md`, plus **D5 and D6 CLOSED for the whole
rack**. The four a reader should not skip:

- **D83/D84** — why Bouncing Text is not ported, in two independent capability terms. Both name the
  exact thing that would close them.
- **D88** — the port holds ONE decoded frame where WPF caches the whole clip behind a 1280 px
  downscale, a 120-frame cap and a 300 MB budget it had to add after a bug report. Not cleverness: a
  consequence of the surface being a GDI blit from a raw buffer.
- **D90** — the rack lists Spiral fourth and the overlay service starts it second; the two orders
  disagree upstream, so there is no single order to copy and the rack's is taken.
- **D91** — a port-side behaviour with no upstream counterpart, and the fix in §4.

---

## 4. THE DEFECT THIS MODULE FOUND IN THE SHARED BODY

**`OwnedSessionEffect`'s parked operation could take a NEW session's work down.**

Found the honest way. `TheQuickToggleStartsItAgainMidSession_AndItIsMovingImmediately` went red
inside a full-suite run and green on its own. The cause: `ParkUntilCancelledAsync`'s tail runs on a
thread-pool continuation (`TaskCreationOptions.RunContinuationsAsynchronously`), so it arrives some
time AFTER the `Disarm` that cancelled it — and "some time after" can be after the module has been
switched back on. That dead generation's `ReleaseWork()` then withdrew the LIVE session's surface,
and for a moving module it killed the cadence with it.

**It is not this module's bug.** The hazard is the shared body's and reaches all four: Pink Filter
would lose its tint the same way, and the paced pair would lose a live schedule's pending one-shot. It
was invisible before because nothing had a counting fact that ran a step LATER than the withdraw.

**The fix** is `OwnedSessionEffect.ReleaseIfStillOurs(generation)`: the parked operation carries the
generation it belongs to, and a release whose generation is no longer the module's is skipped. It is
**lock-free** (`Volatile.Read` of an `int` written under the gate), which is what the class's own
cancellation-callback contract requires.

**It leaves a NARROW READ-THEN-RELEASE WINDOW open, and that is recorded rather than closed.** The
read and the release are two steps, so a stale tail can read its own generation, be preempted, and
resume after `Arm()` has written the new generation *and* placed the new work — and then withdraw
it. Much narrower than the window the guard closes, and still open. §4.2 is why it stays open.

### 4.2 Closing that window under `Gate` was attempted, and REVERTED — it inverts lock order

I wrote the compare-and-release as one act under `Gate`, argued it safe, and shipped it to review.
**The second code review rejected it, correctly: it introduces a lock-order inversion in the base
class all four modules inherit.** Verified at the source before reverting:

| Chain | Path |
|---|---|
| **owner lock → effect lock** | `Disarm()` calls `_owner.Cancel()`; `Cancel()` takes the OWNER's lock and calls `_generationCts?.Cancel()` **inside it** (`Lifecycle/OperationRegistry.cs:162-167`), which runs the parked operation's registration synchronously on that thread — and a gate-taking guard would then want the effect's lock. `Arm()` reaches the same cycle through `_owner.Begin()` (`OperationRegistry.cs:148-158`) |
| **effect lock → owner lock** | `Dot` holds `Gate` and calls `_owner.IsLive(_generation)` inside it, which takes the OWNER's lock (`OperationRegistry.cs:177-183`) |

Both threads exist in the product: `Dot` is read on the UI thread by the Studio page's refresh,
while `Disarm()` runs off the UI thread during host teardown. **A dot repaint concurrent with a
teardown stop would hang shutdown** — precisely the class SP-071/SP-073 built the bounded-teardown
machinery to prevent. Trading a microsecond-wide withdraw window for a deadlock is a bad trade, so
the lock is gone and the residual is written down instead.

**Two lessons worth more than the bug.**

1. **My safety argument was sound and answered the wrong question.** It established that nothing
   holds `Gate` while *waiting on a teardown thread* — true, and irrelevant. Lock-order inversion is
   a different failure mode, and the argument never mentioned the owner's lock at all. A proof about
   one hazard reads like a proof about locking in general, and it is not.
2. **The suite could not see it.** Every test in this packet drives arm and disarm on one thread, so
   the green run was fully consistent with the defect being present. The floor said nothing because
   there was nothing for it to say.

**The real fix is the operation owner cancelling outside its own lock** (`OperationRegistry.Cancel`
and `Begin` both cancel while holding it). That is `Lifecycle/**`, outside this packet's File Scope,
and is named here as a follow-up rather than reached for. Hoisting `ReleaseWork()` out of a lock is
not a third option — it is this code with more words.

### 4.1 How it is pinned — deterministically, and the earlier claim that it could not be was WRONG

An earlier draft of this record said forcing the ordering "would need a wall-clock wait (banned) or a
scheduler seam in the shared body". **That was false on this test project's own evidence**, and the
code review caught it: `AsyncLifecycleTests.StaleGenerationCompletion_IsDiscarded_CannotOverwriteNewerState`
(`client/tests/CcpClient.Tests/AsyncLifecycleTests.cs:26-47`) already forces exactly this
stale-generation ordering in the same assembly, with no wall clock, by holding the stale generation's
body open on a `TaskCompletionSource` the test controls.

The pin taken here is the second route the review named, and it is stronger for a guard that all four
modules inherit: **the predicate is asserted directly.** `AsyncOperationOwner.Generation` is public
and `ReleaseIfStillOurs` is `protected`, so `MovingEffectSpineTests.GenerationProbe` — the smallest
possible module over the shared body, with no surface, no clock and no content — can be armed,
disarmed and re-armed, and then asked to release for a dead generation and for the live one:

| Pin | Claim |
|---|---|
| `AReleaseTaggedWithADeadGenerationReleasesNOTHING_AndOneTaggedWithTheLiveGenerationReleases` (2 rows) | a superseded generation's teardown releases **nothing**; the current generation's **does** |
| `TheGuardIsNotADisarmGuard_AReleaseForTheLiveGenerationStillWorksAfterAStopAndStart` | the negative control: after two stop/start cycles the live generation's own teardown still works, so a guard keyed on "has anything ever been superseded" is caught too |

**Proved to bite on EVERY run.** With the guard clause deleted, the dead-generation row failed
**5 times out of 5** consecutive runs and the live-generation row stayed green each time (a guard that
refused every release would have failed the other half). Restored byte-identically —
`OwnedSessionEffect.cs` md5 `93b2c237717227a17b9270e57e14e510` before and after.

**The pin survives the revert unchanged**: `GenerationProbe.ReleaseFor` is single-threaded and does
not depend on any lock in the guard. Re-verified after the revert — 14/14 green with the guard, and
**3 red out of 3** consecutive runs with the clause deleted, on the dead-generation row only.
Restored byte-identically both times (md5 `93b2c237717227a17b9270e57e14e510` before the revert,
`1b9aa4e945e03606af83e366f7727258` after it).

**The end-to-end fact remains sound but not complete**, and that is now a redundancy rather than the
only evidence. `AStaleTeardownArrivingAfterARestart_MustNotTakeTheNewSessionsWorkDown` awaits the old
generations' completions, so it can never red spuriously; but whether the tail lands before or after
the re-arm is the thread pool's choice, so with the guard removed it fails only on the runs where the
bad ordering really happens. It is kept because it is the story a reader understands, and the
predicate pin is the one that always bites.

---

## 5. Proving it bites

**Three mutations, each run against the whole unit suite, each reverted and the file compared by
md5 afterwards.**

| Mutation | Result |
|---|---|
| `SpiralSurfacePresenter.ArmAdvance` no longer schedules — **the motion itself** | **15 FAILED**, every one of them the new module's (10 `SpiralSurfacePresenterTests`, 5 `MovingEffectSpineTests`); **1451 passed, including every `SessionSpineTests`, `FlashEffectTests`, `FlashSurfacePresenterTests`, `SubliminalEffectTests`, `SecondEffectSpineTests`, `SubliminalSurfacePresenterTests`, `PinkFilterEffectTests`, `PinkFilterSurfacePresenterTests` and `ContinuousEffectSpineTests` fact** |
| `OverlaySurfaceSet.Repaint` no longer withdraws a slot whose paint failed — **the one addition to shared code** | **1 FAILED**, the fact written for it. Exactly one, because `Repaint` is reachable only from the moving module: the addition really is additive |
| `OwnedSessionEffect.Disarm` no longer calls `ReleaseWork()` — **the SHARED body** | **12 FAILED, spread across ALL FOUR modules**: 3 `SessionSpineTests` (Flash Images), 3 `SecondEffectSpineTests` (Subliminals), 1 `PinkFilterEffectTests` + 3 `ContinuousEffectSpineTests` (Pink Filter), 2 `SpiralOverlayEffectTests` (Spiral Overlay) |

The third is SP-101's extraction check extended to four modules: **one line of shared code, and at
least one fact reds per module.** It took a new fact to get there —
`DisarmReleasesTheWorkUNCONDITIONALLY_EvenWhenItThoughtItWasNotArmed`, which pins the property WPF
states in as many words for this family of services ("Always close and clear windows, even if we
thought we weren't running", `BouncingTextService.cs:213`).

md5 before and after each mutation:
`OwnedSessionEffect.cs` `ae57d7ff453da3e2ed16670205565afd`,
`OverlaySurfaceSet.cs` `b073ed93fff91462a8f5490bb0b100e1`,
`SpiralSurfacePresenter.cs` `2ca2a907e184e5e433088e3bcaac49a6`.

---

## 6. What this work does NOT prove

- **No headed capture was taken.** Nothing here claims a human has seen a spiral turn.
  `presentation-verified` remains the orchestrator's gate and is not discharged by anything in this
  packet, including the headless facts.
- **Nothing here proves composition.** Every spiral fact asserts what the presenter asked the overlay
  to do — the request's bounds, opacity and click-through, the call ORDER, and the frames' identity —
  not what the OS then held. The `IOverlayPresence` behind them is a recording double in every fact
  in this packet; the real backend's evidence is SP-099/SP-100's, unchanged.
- **The headless rack facts drive no session**, on purpose: a headless test that started a session
  with the spiral on would put a real full-screen always-on-top Win32 window over whoever ran it. So
  **no test anywhere proves a spiral reaches a real screen.**
- **The GIF decoder IS proven end to end on Windows**, against a real two-frame GIF89a — frame count,
  frame delay, that the two frames really differ, that the loop revisits frame 0, and the reused
  buffer. What that does not prove is any real spiral file: no user's GIF is exercised, and the
  pixel assertion is channel DOMINANCE rather than equality because a bicubic upscale of a 2×2 source
  lands the centre at 236 of 255 (measured). On any non-Windows platform the same facts assert the
  decoder returns **null**; nothing is skipped.
- **Linux is unproven and refuses by design.** The overlay backend returns
  `Unavailable(overlay-mechanism-absent)` with its own manual gate, so the module arms, reports that
  refusal verbatim, reads `Armed`, and shows nothing. Asserted as a REFUSAL path, never as support.
- **Multi-monitor is unverified** (D73) — this machine reports one display.
- **The 5 s topmost cadence is proven as a schedule, not as a band.** `Reassert()` is documented as
  confirming nothing.
- **The stale-teardown guard leaves a narrow read-then-release window open** (§4, §4.2). The
  guard's PREDICATE is pinned deterministically; the two-step read-and-release is not atomic, so a
  stale tail preempted between the two can still withdraw a newer generation's work. Closing it
  under the effect gate was attempted and reverted **because it inverts lock order against the
  operation owner** and would hang shutdown. The real fix is in `Lifecycle/**`, outside this
  packet's File Scope.
- **The end-to-end stale-teardown fact is sound, not complete** (§4.1): it can never red spuriously,
  but with the guard removed it reds only on runs where the thread pool produces the bad ordering.
  The predicate pin is the one that always bites.
- **No interaction, focus, window behaviour or animation is verified** beyond the headless input
  routing in the rack tests: no real pointer passes through a spiral, and no capture shows a click
  landing under one.
- **ONE UNEXPLAINED FLAKE, AND ITS NAMES WERE LOST.** A single `check-floor.mjs` run reported
  **3 failures out of 1469** in `CcpClient.Tests` (`Failed: 3, Passed: 1464, Skipped: 2`) — 1469
  because that run predated the three pins the code review added. It
  **post-dated** the `ReleaseIfStillOurs` fix and post-dated the last test added, so it is NOT the
  event in §4 — that one was a single named red I root-caused and fixed. **The three names were lost**
  because that run's output was filtered to the floor lines only, and the run's preserved TRX
  directory was not kept. It has not recurred: five consecutive full unit runs, one clean floor run,
  and the reviewer's own six consecutive clean full suites could neither reproduce nor explain it.
  **It is recorded as unexplained rather than as absent.**

  **The one structural suspect, named rather than guessed at.** `MovingEffectSpineTests`' rig
  dispatches inline (`InlineDispatch`), so a surface teardown posted from the parked operation's tail
  runs **on that pool thread** rather than on a UI thread. That aliases `SpiralSurfacePresenter`'s
  lock-free fields — `_advance`, `_slot`, `_animation`, `_lastFrameHeld` — and the bare
  `List<string>` inside the rig's recording presence against the test thread that is asserting on
  them. Nothing in the product does this: on a real dispatcher the post is delivered on the UI thread
  and every touch of a presence is single-threaded by construction (the affinity rule
  `OverlaySurfaceSet` states). So the suspect is the RIG, not the presenter — but that is a
  hypothesis with three lost names behind it, not a diagnosis, and a lane that says otherwise would
  be inventing evidence.

- **Bouncing Text's motion law is READ, not ported.** Its formulas, clamps and bounce arithmetic are
  cited in `plan.md` and implemented nowhere; nothing in this packet is evidence about that module.

---

## 7. Files changed

**Product — new**
`Session/SpiralPresetDocument.cs`, `Effects/SpiralOverlayEffect.cs`,
`Effects/SpiralSurfacePresenter.cs`, `Effects/SpiralFrameSource.cs`, `Effects/SpiralPresentation.cs`,
`Effects/SpiralLibrary.cs`.

**Product — changed**
`Effects/OverlaySurfaceSet.cs` (`Repaint` — the one addition),
`Session/OwnedSessionEffect.cs` (`ReleaseIfStillOurs` — §4),
`Session/EffectReasonCodes.cs` (three codes),
`Session/SessionParticipant.cs` (composes the fourth module, its store and its surface),
`Views/Pages/StudioPage.axaml` + `.axaml.cs` (the spiral row's dot, its quick-toggle, its panel and
its three lines).

**Tests — new**
`SpiralOverlayEffectTests.cs` (**38** TRX results), `SpiralSurfacePresenterTests.cs` (**20**),
`MovingEffectSpineTests.cs` (**14**), `SpiralFrameSourceTests.cs` (**12**).

**Tests — changed**
`StudioSurfaceNoticeTests.cs` (**+16**: an 11-row theory and 5 facts for the moving module's lines),
`ContinuousEffectSpineTests.cs` (the rack-order expectation gains a fourth member),
`SessionSpineTests.cs` (the unknown-rack-key theory's `"spiral"` row becomes `"visuals"`, a real WPF
rack key with no toggle — the row stopped being unknown),
`CcpClient.HeadlessTests/StudioRackHeadlessTests.cs` (**+3** and one fact rewritten),
`CcpClient.HeadlessTests/NavigationShellHeadlessTests.cs` (one comment corrected, no count change).

**38 + 20 + 14 + 12 + 16 = 100 unit**, and **90 − 87 = +3 headless** — measured per file with
`--filter`. `SessionSpineTests.cs` contributes **0**: its `"spiral"` theory row was replaced by a
`"visuals"` row, one for one. (An earlier draft of this section wrote the two changed-file numbers as
"+21" and "−5"; both were wrong and the errors cancelled, so `floor-delta.json` was right for the
wrong reason. The truth is +16 and 0.)

**Docs** `client/docs/wpf-surface-reachability.md` (D83–D91, D5 and D6 closed).

---

## 8. Three things a reviewer should check first

1. **`SpiralOverlayEffect` has no `ISessionClock` in its constructor** and
   `SpiralSurfacePresenter` does. That one line is the packet's whole answer, and
   `TheMovingModulesConstructorAndBaseClass_CarryNoClockAndNoPacedBase` pins both halves — including
   that the clock IS in the presenter, so moving it into the module is the change the fact exists to
   catch. Its own doc comment says what reflection is worth (SP-105's wording, kept): the guard
   really lives in the counting facts, and this one earns its keep by failing at the line a future
   author is editing.
2. **§4, §4.1 and §4.2.** A shared-body defect was found and fixed; the guard is pinned
   deterministically against a probe subclass (both arms, red on every run with the clause
   deleted). It leaves a **narrow read-then-release window** open, and closing that under the
   module's gate was attempted and **reverted because it inverts lock order against the operation
   owner** — written up at the method, in D91 and in §4.2 specifically so a future reader does not
   try the same fix again. The real fix is in `Lifecycle/**`, out of scope, and is named as a
   follow-up.
3. **§6's unexplained flake.** One `check-floor.mjs` run produced 3 failures whose names were lost,
   after the fix and after the last test added, never reproduced in eleven clean suites since. The
   one structural suspect is the rig's inline dispatch, not the presenter, and the reasoning is
   written out rather than asserted.
