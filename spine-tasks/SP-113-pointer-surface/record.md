# SP-113 — record

Branch `lane/SP-113-pointer-surface`, base `431e424a`.
Floor: pin **1830 unit / 112 headless**; observed **1927 unit / 117 headless**; declared
**+97 unit / +5 headless** (`floor-delta.json`). 1830 + 97 = 1927 and 112 + 5 = 117, confirmed by
`node client/tests/floor/sum-deltas.mjs --check --packets SP-113-pointer-surface`. The floor run
therefore REPORTS a violation against the pin, which is the expected shape: the orchestrator sums the
deltas and applies one bump. Two skips, both pre-existing
(`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`,
`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`); none added, none widened.
Build: 0 errors, 0 warnings. `client/tests/floor/floor.json` was never opened.

> **This document's real subject is one sentence: a click arrived at a window while the foreground
> did not move.** §2 is the chain that says so and where it stops. Everything else — the module, the
> dot, the race — hangs off that.

---

## 1. SP-112's CENSUS, VERIFIED RATHER THAN TRUSTED

Every claim checked against the code before a line was written
(`spine-tasks/SP-113-pointer-surface/plan.md` §1).

| SP-112's claim | my read | verdict |
|---|---|---|
| `Win32InputPresence.WindowProc` handles five messages, **no mouse message** | `:818-877` — `WmPaint`, `WmKeydown`, `WmSyskeydown`, `WmChar`, `WmClose`, then `DefWindowProcW`. Exactly five. | **holds** |
| `Win32InputInterop` declares none | `:54-58` declares those five constants and no `WM_LBUTTON*`, no `WM_MOUSEACTIVATE`, no `WM_NCHITTEST` | **holds** |
| one `nint _window` | `:73`, the only window field; `:120/385/429/501/623/953/993` all read it | **holds** |
| **one `SetWindowPos` — no move seam** | one call in the folder, `:206`, inside `Prompt`, failure text at `:212` | **holds** |
| `Confirmed` requires foreground + system focus | `IInputPresence.cs:182-184` | **holds** |

**The census is correct in every particular.** A pointer capability is a second capability, not a
second consumer.

### The one citation that is exact and about the wrong window

The packet cites `BubbleCountWindow.xaml.cs:1823-1824` for `WM_MOUSEACTIVATE → MA_NOACTIVATE`.
**Those lines really are those two constants** (`:1823` `WM_MOUSEACTIVATE = 0x0021`, `:1824`
`MA_NOACTIVATE = 3`, answered by `NoClickRaiseHook` at `:1831-1839`) — but they belong to Bubble
**Count**'s window, SP-112's module. **Bubble Pop's own bubbles contain no `WM_MOUSEACTIVATE` at
all**: `BubbleService.cs` has none. Its non-activation is a STYLE, `exStyle | WS_EX_TOOLWINDOW |
WS_EX_NOACTIVATE` (`:4887`, constant `:4899`), plus `SWP_NOACTIVATE` on every reposition (`:4785`,
`:4807`) and `ShowActivated = false` (`:2158`). `IsHitTestVisible = _isClickable` (`:2960`, `:2988`,
`:3103`) and `MouseLeftButtonDown` (`:2966`, `:3018`, `:3113`) are exact.

**Resolution: both are ported, and D141 says why.** The style is Bubble Pop's own and is what the OS
is asked to confirm; the message answer is upstream's belt-and-braces on a sibling surface and is
taken because **it is the only one of the two observable at the instant of a click**.

**Second, minor:** the packet's trap 3 says *"Eight exist: clock, screen, change, custody, reach,
demand, motion"* over a list of **seven**, and the same paragraph then says "not a licence for an
eighth". SP-112 §4 says "the seven landed meanings" over the identical list. Seven is right.

---

## 2. THE PROVABLE CHAIN, AND WHERE IT STOPS

SP-099 proved click-**through**. SP-110 proved a window TAKES the keyboard, leaning on
`GetForegroundWindow` and `GetGUIThreadInfo(0)`. **Neither is available here**: for this capability
"we are the foreground" is a BUG, so every link SP-110 leaned on is one this packet asserts the
negation of.

### 2a. What the PRODUCT earns — `Available` rests on exactly six things

| # | fact | API | measured |
|---|---|---|---|
| **P1** | the window exists, is visible, and the OS holds the EXACT rectangle | `IsWindow` / `IsWindowVisible` / `GetWindowRect` | held == asked |
| **P2** | the OS holds `WS_EX_NOACTIVATE` and **not** `WS_EX_TRANSPARENT` — upstream's own two bits (`BubbleService.cs:4887`, `:4891-4892`) read BACK | `GetWindowLongPtrW` | both |
| **P3** | the OS's own z-order walk puts it above every ORDINARY window | `GetTopWindow` + `GetWindow` | index 6, first ordinary 7 |
| **P4** | **the window manager routes a hit test at the target's own centre TO it** — the inverse of SP-099, per target | `WindowFromPoint` | ours |
| **P5** | **the foreground is the SAME window before and after, and is never the target** | `GetForegroundWindow` ×2 | unchanged |
| **P6** | the OS holds ink in the target's own client area, differentially (four corner control points read EXACTLY the fill) | `GetDC` + `GetPixel` | inked over a confirmed background |

**The product never synthesises input.** `SendInput` is the harness's, as at SP-110.

### 2b. What the HARNESS adds — **the fact worth having**

`ACLICKARRIVESATTHETARGETSOWNWINDOWPROCEDURE_WHILETHEFOREGROUNDDOESNOTMOVE` asserts, in one fact:

| # | fact | measured |
|---|---|---|
| **T1** | a click synthesised at OS level arrives as `WM_LBUTTONDOWN` **and** `WM_LBUTTONUP` in **that target's own window procedure** | 1 and 1 |
| **T2** | `WM_MOUSEACTIVATE` arrived during that click and was answered `MA_NOACTIVATE` | refusals +1 |
| **T3** | **`GetForegroundWindow()` is byte-identical either side of the click, and is not the target** | unchanged |
| **T4** | the callback named the handle the OS chose | opened == pressed |
| **T5** | with the target closed, the same point delivers nothing to it — and a probe-owned **decoy** under the point CAUGHT the click, so the negative leg is not satisfied by an injection the OS refused | decoy saw the down; the count did not move |

Plus, from round 2: **a press with no listener is COUNTED** (`PressesDropped == 2`), never queued;
and **a style something else CLEARED is caught on the next `Move`** — the harness clears
`WS_EX_NOACTIVATE` on the target's own hwnd from outside the capability and the next move refuses
with `pointer-target-style-wrong`, which is the only way to construct on a healthy machine the state
the read-back exists for (upstream met it with recycled pooled shells, `:4880-4884`).

### 2c. THE INSTRUMENT'S OWN CONTROL, and the thing it measured that shaped the file

Before the product is touched, the probe builds its OWN non-activating window, clicks it, and asserts
it saw both halves, answered `WM_MOUSEACTIVATE`, and did not move the foreground. Without that leg
every reading below is a test of nothing happening.

> **Measured while `PointerWindowProbe` was being written:** a scratch target at (343,214,160,160)
> was visible, held its exact rectangle, and sat at z-index **6** with the first ordinary window at
> **7** — and `WindowFromPoint` at its own centre answered
> `HwndWrapper[ConditioningControlPanel;...]`, **the shipping WPF product**, which is topmost too and
> re-asserts `HWND_TOPMOST` on a cadence (`FlashService.cs:206-243`). **Being above every ORDINARY
> window is not the same as owning a point.** The instrument therefore re-asserts the band before
> taking a routing answer — which is what the product does, what the overlay does, and what
> upstream's own bubbles do (`BubbleService.cs:4778-4787`) — and the foreign-topmost residue is named
> rather than hidden.

### 2d. WHERE THE CHAIN STOPS — six places

1. **No human clicked anything.** Every press is `SendInput`. **`popped-verified` is a named manual
   gate and no automated step on any platform discharges it, Windows included.**
2. **Injected input is not physical input.** UIPI refuses injection into a higher-integrity window,
   the secure desktop takes it away, a locked workstation swallows it. The fixture DETECTS those
   (`SendInput` returns 0) and every expectation flips with the machine — never a skip.
3. **`Available` cannot include T2, and this is the sharpest stop.** `WM_MOUSEACTIVATE` exists only
   when a click really happens, and the product must never synthesise one. So the product claims the
   STYLE plus "the foreground is not me and did not move"; *"this window will never activate"* is not
   provable without clicking it. **And the sweep proved the counter had been over-claiming:** M-am
   (return `MA_ACTIVATE` instead) survived round 1, because on Windows `WS_EX_NOACTIVATE` alone
   already stops the activation, so the returned value changed nothing observable. The counter now
   increments only when the answer IS the refusal, and M-am is caught. **The honest reading is that
   the message answer is redundant with the style on Windows** — it is kept because it is upstream's
   own answer and because it is the only thing that can be observed with a click in hand.
4. **The routing answer is momentary, and a FOREIGN topmost window can own the point** (§2c). Every
   claim is "what the OS said when it was asked".
5. **Avalonia's own message loop is not proven to deliver mouse messages for this window.** All
   evidence rides the surface's own bounded `Pump` — SP-110's L5 residue, unchanged.
6. **No headed capture.** `presentation-verified` is untouched: the ink read-back is an OS query
   about pixels the OS holds for a window, not a photograph, and says nothing about whether a bubble
   is legible, aimable, or on a screen anybody is looking at.

---

## 3. THE RACE — mostly DESIGNED OUT, and the residue MEASURED in both directions

SP-110 named it: *"the hit test's answer is a function of a position that changes between asking and
clicking."*

### 3a. The product has no ask-then-act gap at all

Upstream's hosted paths really do have the race: a global mouse hook reads an immutable
`ChaosClickDiscsSnapshot` rebuilt once per UI tick and decides in USER SPACE which bubble was hit
(primer §4c/§4e, gotcha 4). **The port gives each target its own non-activating window, so the
arbiter at click time is the window manager, at the instant of the click, over the rectangle the
operating system itself holds.** Nothing in `Win32PointerSurface` hit-tests and then acts on the
answer; `WindowFromPoint` appears only where `Available` is earned. That is upstream's own per-window
path (`:3113`) and the port takes its cost with it: **three concurrent bubbles** (`MAX_BUBBLES = 3`,
`:25`, *"SetWindowPos-bound — keep small"*), not the shared host's forty.

### 3b. The residue, bounded by arithmetic over upstream's own constants

`Open`/`Move` return `Available` on a routing answer that can be up to **one animation step** old.

- step `STEP_MS = 30` ms (`:53`)
- vertical `_posY -= _speed`, `_speed ∈ [1,2)` (`:2825`), boosted up to 6× (`:2831-2836`) → **≤ 12 px**
- horizontal: the steepest of upstream's four wobbles is case 1, `30·sin(7.5t)` over `t += 0.02`
  (`:3458-3464`, `:3399`) → **4.5 px** (case 3 gives 3.6, case 0 3.0, case 2 2.7)
- worst case `sqrt(12² + 4.5²)` = **12.816 px**; smallest legal radius `ClickableFloorDip/2` = **30 px**
  (`BubbleSizing.cs:70`)

**The bound holds by a factor of 2.34**, and the fact asserts the factor as well as the inequality,
so a change that halves the margin reddens instead of merely passing.

### 3c. And it is MEASURED, with real windows and real clicks, in both directions

| leg | what happens | result |
|---|---|---|
| the bound, exercised | hit test at the centre → `Move` by one worst-case step (13 px) → click the ORIGINAL point | **the click still arrives at that same target** |
| the bound, falsified | `Move` by 200 px (> a full 160 px side) → click the original point, with a probe-owned decoy under it | **the click does NOT arrive**, and the decoy CAUGHT it |
| the arithmetic | the inequality over the module's own constants | holds, factor 2.34 |

**The second leg is what makes the first mean anything.** Without it, "the click still arrived" is
equally true of a window that never moved — the SP-099 fake in a new costume.

### 3d. WHAT FREEZING COSTS, said plainly

**The delivery facts hold the field still between the injection and the pump.** Nothing steps the
field in that window. What that buys is a deterministic delivery fact. **What it costs is exactly
this: nothing here proves delivery to a target that moved DURING the flight of a click already in
the system's input stream.** 3c covers the ask/act gap either side of a move and does not cover a
move interleaved with the OS's own delivery. That gap is a property of the machine's input timing, no
fixture can pin it without a wall-clock wait (banned), and it is named rather than papered over.

---

## 4. THE DOT — no eighth meaning, and the reason is a finding about DEMAND

```
Live = a firing is on the clock
    && the OS says this process can put a pointer target on a display   (station channel)
    && ( no target of MINE is up  OR  the OS routes a click at one of them TO this process )
```

**DEMAND fits, and the finding is that DEMAND was never "foreground".** SP-110 defined it as *"a
claim on the user's ATTENTION, which the operating system grants and can revoke without this process
doing anything at all"* — foreground-plus-focus was its INSTRUMENT. A bubble the OS will route a
click to is the same class of fact: granted by the OS, contested by every other topmost window,
revocable while this process does nothing (something covers the point and the bubble becomes
decoration — on screen, unhittable). Same meaning, different instrument. **The seven stand: clock,
screen, change, custody, reach, demand, motion.**

**Why the third clause is a DISJUNCTION.** Between two spawns there is legitimately nothing on the
desktop — at the bottom of the dial that is fifty-nine seconds in every sixty (`60000/1` ms, `:189`)
— and a dot that went dark there would report the module broken for almost the whole session. It
darkens for the state that is really wrong: targets ARE up and the window manager routes none of them
here.

**Why "at least one" and not "all".** Upstream's spawn band is a random x per bubble with no
separation rule (`:2853`), so one bubble covering another's centre is an ordinary state of the game.
A field with one hittable bubble in it is a game.

| situation | arm result | dot | why |
|---|---|---|---|
| no window station / no display (and Linux) | `Unavailable` / `pointer-surface-unavailable` | **Armed** | the whole channel is gone — the Pink Filter answer |
| targets up, the OS routes a click at NONE of them | `Degraded` / `pointer-field-not-routable` | **Armed** | **a third kind of degradation** — see below |
| between spawns, nothing on screen | (nothing new) | **Live** | the gap is most of every session |
| one of several targets covered | (nothing new) | **Live** | an ordinary state of upstream's own field |

**`pointer-field-not-routable` is a THIRD answer the port did not have** (D150). It is not the Pink
Filter answer (`Unavailable`/`Armed`), because the CHANNEL is intact and the next step may route
again; and it is not the Subliminals answer (`Degraded`/`Live`), because nothing is missing from the
CONTENT — the bubbles are there and drawn, and the user simply cannot hit any of them.

**The "of MINE" qualifier is not a stored flag** and it needed no guard, because
`IPointerSurface` is **keyed from birth**: every operation names a target. SP-112 §2.3 F5 found the
video and input capabilities single-tenant and every ownership guard written in the module; SP-109's
audio presence solved it with keyed slots. This capability takes the keyed answer at design time
rather than at the second consumer.

---

## 5. WHAT THE MODULE IS, and what is not ported

Small always-on-top windows float up the screen and pop when clicked, at upstream's own sizes, speeds
and spawn rate.

**Upstream's arithmetic is ported verbatim** with per-line citations in `BubblePopField`: the spawn
interval `60000/frequency` (`:189`) with the dial clamped 1..60 (`AppSettings.cs:2210`); size drawn
from 150..250 scaled by the user's 50..150 % and railed into 60..500
(`BubbleSizing.cs:41/48/52/59/70/82`); speed `1.0 + rand` px/step (`:2825`) times the 0..500 % boost
(`:2831-2836`); the four wobbles (`:3458-3464`) over `_timeAlive += 0.02` (`:3399`); FloatUp's
`_posY -= _speed`, `_posX = _startX + offset` (`:3496-3497`); the exit at `area.Y - size - 50`
(`:2846`); the pop's `_scale += 0.04`, `_fadeAlpha -= 0.066` (`:3227-3228`) with destruction deferred
to the animation completing (gotcha 6); the spawn band `random.Next(50, …)` (`:2853`); and the
per-window cap of three (`:25`).

**Not ported, declared rather than stubbed** (D145/D146, and in the code beside the dials that would
have carried it): the pop SOUND and its pooled devices (`:1839-1905`); XP, the lucky roll,
achievements, haptics and Discord (`:941-968`); Trigger Bubbles and the seven payloads a pop can fire
(`:973-1053`); the whole Chaos Mode roguelite this service is 90 % of (`:1096-1675`); gaze-pop
(`:81`); the companion easter egg (`:592-680`); the shared-host and compositor render paths (primer
§5); multi-monitor spawning (`:851-857`, D66); and the sprite art (D86).

---

## 6. PROVING IT BITES — 99 mutations, three rounds, **15 survivors**

Every predicate this packet added was mutated one at a time by
`spine-tasks/SP-113-pointer-surface/sweep.mjs`, which lives inside this packet's folder and writes
only inside it. Raw logs beside this record (`sweep-round1.log`, `sweep-round2.log`,
`sweep-round3.log`); every count below is taken from them. Each mutation ran
`BubblePopModuleTests`, `PointerCapabilityTests`, `PointerCoexistenceTests` and the three spine
suites. The driver restores each file byte-identically and asserts `git status --porcelain client/src`
is empty at the end.

**The books: 99 distinct mutations; 71 (round 1) + 12 (round 2) + 1 (round 3) = 84 caught; 15
survive; 84 + 15 = 99.** The driver carries a `--check` mode that verifies every needle without
running anything, and it reported **0 not-patched on every round** — SP-112's round-1 CRLF defect is
not repeated (the driver normalises for matching and writes back in the file's own line endings).

### Round 1 — 99 mutations, 71 caught, 28 survived

### Round 2 — 14 of the 28 re-run against new facts: **12 caught, 2 survived**

Twelve were real holes and are now closed by facts that isolate the clause:

| # | closed by |
|---|---|
| M-j | `ANotAskedObservationClaimsNOTHING_NotEvenARoutingAnswerAboutAWindowItDoesNotHave` — `0 == 0` is the trap, and it is SP-110's own M-l lesson arriving again |
| M-o, M-p, M-q | `EveryClauseOfTheSTATIONObservationIsLoadBearing_BecauseEachOneAloneReachesNobody` |
| M-t | `ATargetsRadiusIsHalfItsSHORTERSide_BecauseThatIsTheSideAMoveCanCrossFirst` — a square target hid the distinction |
| M-ag | `ASTYLESOMETHINGELSECLEAREDIsCaughtOnTheNextMove_WhichIsWhyTheReadBackIsAReadBack` — the harness clears `WS_EX_NOACTIVATE` from outside |
| **M-am** | the product changed: the refusal is counted only when the answer IS `MA_NOACTIVATE` (see §2d item 3) |
| M-av | `APRESSNOBODYISLISTENINGFORIsCOUNTED_NeverQueuedAndNeverSilentlyDiscarded` |
| M-bo | `BUBBLESSpawnAcrossTheWidthOfThePlayArea_AtUpstreamsOwnBand` |
| **M-bs** | `APRESSREACHESTHEFIELDOnlyBecauseTheStepPUMPSItFirst` — **and the test DOUBLE had to change to catch it.** It invoked the callback inline, which made the presenter's pump-before-you-move ordering invisible; it now queues presses and drains them on `Pump`, mirroring a real message queue. That is SP-110 §8b's lesson: a double that diverges from the product where the bug lives is blind in exactly the state that matters |
| M-bt | the routable count re-derived ACROSS A STEP, not carried forward from the placement |
| M-ci | `DRAGGINGTHEDIALPASTITSOWNCEILINGDoesNotRetimeTheLiveField` |

### Round 3 — the two round 2 left: **1 caught, 1 survived**

**M-ca** — `Withdraw` disposing both cadence handles. Round 2's assertion was placed AFTER a
five-minute advance, which let the orphaned timers fire and retire themselves; moved to immediately
after the withdraw, it catches. The assertion's own comment now says why the position is
load-bearing.

### THE FIFTEEN SURVIVORS — **five equivalent with proofs, ten uncovered with reasons**

**FIVE ARE EQUIVALENT MUTANTS, and each proof is discharged here rather than asserted:**

- **M-b — `Confirmed` drops its own `Window != 0`.** `Confirmed` requires `HitTestRoutesHere`, and
  `HitTestRoutesHere => Window != 0 && HitTestWinner == Window`. So `Confirmed ⇒ Window != 0` with
  or without the clause, for every possible input. **Redundant, not unpinned** — and the guard it is
  redundant WITH is M-j, which round 2 closed.
- **M-aq — three of the four ink control points.** Equivalent iff all four corners always read the
  same value. The painter fills the whole client rect with `Fill` and then draws ONE filled ellipse
  (NULL pen) inscribed in `DiscBox(w,h) = [inset, w-inset] × [inset, h-inset]` with
  `inset = max(ControlMargin+1, min(w,h)/10) ≥ 4`. The control points are at `x ∈ {2, w-3}`,
  `y ∈ {2, h-3}`. `2 < 4 ≤ inset`, and `w-3 > w-inset ⟺ inset > 3` ✓. **All four lie strictly
  outside the disc's bounding box**, hence outside the ellipse, hence exactly `Fill`. No input
  distinguishes the mutant.
- **M-au — the disc's inset away from the control points.** `inset = 0` makes the ellipse span the
  whole client rect, and an ellipse inscribed in `[0,w] × [0,h]` still does not reach a corner: at
  `(2,2)` the normalised radius is `(1 - 4/w)² + (1 - 4/h)²`, which for any side ≥ 60 is
  `≥ 2 × 0.871 = 1.742 > 1`. **The corner control points are insensitive to the inset**, so the
  mutant is equivalent — **and the finding is that the inset is not what protects them; the corner
  geometry is.** The inset stays because it is the rim the drawing wants, not because the
  differential needs it, and that is now the honest statement of what it does.
- **M-bn — the race bound being an inequality at all.** `OneStepNeverCarriesABubbleOffItsOwnCentre`
  is a static expression over `const` inputs only: `sqrt(12² + 4.5²) = 12.816 < 30` evaluates to
  `true` with no free variables, so `=> true` returns the same value for every call that can ever be
  made. **Equivalent by construction.** What the property guards is a FUTURE edit to those
  constants, and each constituent constant is mutated separately and caught (M-bb the boost, M-bj/bk
  the wobble amplitude and rate, M-bm the time increment, M-ay the size floor).
- **M-ch — the `Showing` guard in front of the posted withdraw.** `BubblePopSurfacePresenter.Withdraw`
  is idempotent under its own gate: after the first call the target map is empty, both cadence handles
  are null and `_engaged` is false, so a triple post produces one teardown and two no-ops that touch
  the surface not at all. **The guard here is a cost saving and not a correctness rule** — which is
  DIFFERENT from `PinkFilterEffect`'s identical-looking guard, whose own comment says it is not an
  optimisation. `BubblePopEffect.ReleaseWork`'s doc repeated Pink Filter's stronger wording; **it is
  corrected in this diff rather than defended**, and it names this mutant.

**TEN ARE UNCOVERED, and each names why:**

- **M-v, M-ad, M-ae, M-af, M-ah, M-ai, M-aj — seven refusal branches inside `Open`/`Classify` that no
  deterministic state on a healthy machine reaches.** They are the station gate, the on-screen gate,
  the exact-rectangle gate, the non-activation gate, the z-order gate, the routing gate and the
  blank-target `Degraded`. **The PREDICATES each of them reads are pinned independently** by
  `EveryClauseOfConfirmed_IsLoadBearing`, `BOTHHalvesOfTheNonActivationClaimAreLoadBearing` and
  `EveryClauseOfTheSTATIONObservationIsLoadBearing`, all of which bite; what is uncovered is the
  BRANCH that reads them, and reaching it needs a state this machine cannot be put into. One of the
  seven WAS reachable and is now closed (M-ag, the style gate, via hostile external modification);
  the attempt to do the same for M-af is itself a finding — forcing our own `WS_EX_NOACTIVATE`
  window to the foreground is not a deterministic operation from a process that may not own the
  foreground, so the fact would have been a coin toss dressed as evidence. **Constructing the other
  six would mean building a rig that races the product's own topmost re-assertion**, which is a fact
  about scheduling rather than about this capability.
- **M-ap — the raise loop's own re-ask** (`if (true)`: take the first answer). Survives because the
  first re-assertion wins on this desktop. It is CONTENTION-conditional: §2c measured a foreign
  topmost window owning a point, which is exactly when the loop earns its keep, but nothing can make
  that happen on demand. Named rather than faked with a rig that re-raises in a loop from another
  thread.
- **M-as, M-at — `Close`'s own confirmation and its non-activation check.** After
  `ShowWindow(SW_HIDE)` the OS never routes to the window again and the foreground does not move, so
  no reachable state makes either clause false. Same disposition and same reason as SP-110's M-w.
  The OUTCOME they guard is pinned independently: `ClosingATargetTakesItOffTheDesktop_...` asserts
  through the PROBE that the hit test no longer answers the target and that the foreground is
  unchanged, which is the same claim taken through a second code path.

---

## 7. THE THREE LANDED SURFACES, PROVEN UNHARMED RATHER THAN ASSUMED

`Overlay/**`, `Input/**`, `Audio/**` and `Video/**` were **not edited** — byte-identical to base.
They were CONSUMED: `PointerSurfaceObservations.RunCoexistence` builds a real `Win32OverlayPresence`
presenting a real click-through surface, a real `Win32InputPresence` card that really takes the
foreground and the system keyboard focus, and a real `Win32VideoPresence` holding a real decoded
picture — then opens, MOVES and closes a real pointer target beside all three. The overlay is
measured through `OverlayWindowProbe` and the card through `InputWindowProbe`, SP-099's and SP-110's
own instruments, unmodified. All four rectangles are disjoint, so no surface's hit-test point is
occluded by another.

Six facts in `PointerCoexistenceTests`:

1. **The positive control:** the overlay really presented, the card really took the foreground AND
   the system keyboard focus, the video surface really held a decoded picture, and the pointer
   surface really earned `Available` beside all three. Without this leg every reading below is a test
   of nothing happening.
2. The overlay is still click-**through** at **four** moments — before, during the open, **during the
   move**, and after — and `WS_EX_TRANSPARENT` survives the whole lifecycle. The differential is
   re-run on the overlay itself, so "the point went elsewhere" cannot be satisfied by an overlay that
   quietly stopped existing.
3. The overlay keeps its band above every ordinary window and its `LWA_ALPHA` of 153 at all four
   moments, never becomes the foreground, and its own `Present` still earns `Available` afterwards.
4. **The card keeps the foreground AND the system keyboard focus through an open, a MOVE and a
   close.** This is the trap-2 fact and the one this capability could most plausibly break: the move
   is an operation `Input/**` does not have and therefore never had to survive.
5. The video surface's own read-back still confirms its picture with a pointer target up beside it.
6. The MOVE itself earns `Available` with all three of the others on the desktop.

Every expectation **flips with the machine** rather than being skipped: on a machine with no
interactive desktop each leg is asserted false, which is why this file carries no early return and no
entry in the vacuous-shape ledger.

**The ten landed modules' facts are unchanged in SEMANTICS.** Three rack-order/refusal lists and two
headless rack lists grew by one entry each, exactly as they did at SP-105, SP-106, SP-108, SP-109,
SP-111 and SP-112. No landed assertion was relaxed, reworded to be weaker, or deleted.

---

## 8. THE FLOOR

Pin **1830 unit / 112 headless**; observed **1927 unit / 117 headless**, **0 failures in either
suite**; declared **+97 unit / +5 headless** (`floor-delta.json`). 1830 + 97 = 1927 and 112 + 5 = 117,
confirmed by `node client/tests/floor/sum-deltas.mjs --check --packets SP-113-pointer-surface`. The
floor run therefore REPORTS a violation against the pin, which is the expected shape. Two skips, both
pre-existing; none added, none widened. `client/tests/floor/floor.json` was never opened.

---

## 9. Files changed

**Product — new (`Pointer/`):** `IPointerSurface.cs`, `PointerReasonCodes.cs`,
`Win32PointerInterop.cs`, `Win32PointerSurface.cs`, `UnsupportedPointerSurface.cs`,
`PointerSurfaceFactory.cs`.

**Product — new (effects/session/views):** `Effects/BubblePopField.cs`,
`Effects/BubblePopSurfacePresenter.cs`, `Effects/BubblePopEffect.cs`,
`Session/BubblePopPresetDocument.cs`, `Views/Pages/PointerPanelNotices.cs`.

**Product — changed:** `Session/EffectReasonCodes.cs` (three additive codes),
`Session/SessionParticipant.cs` (the eleventh module, its store, its surface and its teardown),
`Views/Pages/StudioPage.axaml` + `.axaml.cs` (the row FIRST in GAMES & CARDS, the panel, three dials
and an evidence notice).

**`Overlay/**`, `Input/**`, `Audio/**` and `Video/**` are byte-identical to base.**

**Tests — new.** Counts are TEST CASES, each `[Theory]` row counted individually.

| file | cases | what it is |
|---|---|---|
| `PointerWindowProbe.cs` | — | the new instrument: mouse `SendInput`, the raising hit test, the style clear |
| `PointerSurfaceObservations.cs` | — | the three real-desktop runs |
| `PointerCapabilityTests.cs` | **22** | the chain, the race in both directions, the refusals, and the observation records |
| `PointerCoexistenceTests.cs` | **6** | four surfaces, one desktop |
| `BubblePopModuleTests.cs` | **67** | the arithmetic, the presenter, the module, the dot, the panel's sentences and the Linux refusal |

**Tests — changed:** `RealDesktopCollectionGuardTests.cs` (the helper census gains the three pointer
helpers and the bound controls gain the two new real-desktop classes — a STRENGTHENING),
`AudioModuleSpineTests.cs`, `ContinuousEffectSpineTests.cs`, `SecondEffectSpineTests.cs` (rack-order
and refusal lists grow by one), `StudioRackHeadlessTests.cs` (the rack lists grow by one, the
GAMES & CARDS order fact now pins all three ported rows, **+5** new facts).

`InputWindowProbe.cs`, `OverlayWindowProbe.cs`, `InputCaptureObservations.cs`,
`OverlayObservations.cs` and `VideoSurfaceObservations.cs` are unmodified.

**Docs:** `client/docs/verification-harness.md` (the pointer evidence class),
`client/docs/wpf-surface-reachability.md` (D141–D151).

---

## 10. What this work does NOT prove

- **Nothing here proves a human clicked anything.** `popped-verified` is a named manual gate and no
  automated step on any platform discharges it, Windows included.
- **No headed capture was taken.** `presentation-verified` is untouched. The ink read-back is an OS
  query about pixels the OS holds FOR A WINDOW; nothing here says a bubble is visible on a monitor,
  legible, or big enough to be worth aiming at.
- **`Available` never means a click was delivered**, and it never means the window will not activate
  — only that the OS holds the style and that the foreground has not moved. §2d item 3.
- **The delivery facts freeze the field.** A move interleaved with the OS's own delivery of a click
  already in flight is not covered by anything here (§3d).
- **A foreign topmost window can own a point at any instant**, and nothing in this process can
  exclude one. It was MEASURED doing exactly that (§2c).
- **Concurrency is single-threaded.** The surface's presses, the field's steps and the module's dot
  are exercised on one thread. Two threads racing a press against a `Move` are not covered, and the
  product's own affinity rule is stated rather than enforced.
- **No second instance of anything was exercised.** One pointer surface, one process. What two would
  do to one z-order band is untested.
- **Nothing measures cadence or smoothness.** Every step in every fact is driven by hand on the
  injected clock, so a field whose bubbles moved twice as fast as intended satisfies every check here.
- **Nothing profiles the cost.** Three `SetWindowPos` calls plus three `GetWindowRect`, an ex-style
  read, a z-order walk over every visible top-level window, a `WindowFromPoint` and a ~400-pixel
  `GetPixel` read PER TARGET PER 30 ms STEP is a real per-frame cost that was never measured on a
  contended machine. **This is the single most likely place the port's Bubble Pop will need work**,
  and it is upstream's own reason for capping the per-window path at three.
- **Linux is unproven** and refuses in type with a five-step gate that is run separately on X11 and
  Wayland because they answer differently; nothing in this packet discharges it, and the gate names
  `_NET_ACTIVE_WINDOW` being unchanged across a click as the step most likely to fail because
  click-to-focus is the window manager's policy rather than the client's.
