# SP-115 — per-pixel alpha, the blocker SP-106 named and refused to fake

Branch `lane/SP-115-per-pixel-alpha`, base `f3c751c1`. Review Level 3. Plan checkpoint in `plan.md`,
written **before the first product edit** and carrying the raw output of three OS probe rounds.

**Floor:** pin **1938 unit / 117 headless**; observed **2062 unit / 121 headless**; declared
**+124 unit / +4 headless** (`floor-delta.json`, confirmed by
`node client/tests/floor/sum-deltas.mjs --check --packets SP-115-per-pixel-alpha`). The floor run
therefore REPORTS a violation against the shared pin, which is the expected lane shape: the pin is
not this lane's to edit and the orchestrator sums the declared deltas at land.
`client/tests/floor/floor.json` was never opened. Two skips, both pre-existing
(`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`,
`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`); none added, none widened,
no name added to `allowedSkips`. Build and warning gate: **0 warnings, 0 errors**.

---

## 0. THE PROVABLE CHAIN, AND WHERE IT STOPS

I did not design and then hope. Before the first product edit I asked **this** operating system what
it will say about a per-pixel-alpha composite. Three probe rounds; `plan.md` §0 carries the raw
output verbatim, including the round that contaminated itself and was discarded.

| # | Fact | Instrument | Measured |
|---|---|---|---|
| **G1** | the OS accepts a premultiplied BGRA surface for a window this process created layered, and refuses one for a window it did not | `UpdateLayeredWindow` | ok / **err 87** |
| **G2** | **the OS's own copy of the surface carries the frame** — every fully-OPAQUE sample reads back its colour EXACTLY, every fully-TRANSPARENT sample reads back 0 | `PrintWindow(PW_RENDERFULLCONTENT)` | **0 mismatches over 10 colours**, on the first call after the show and every call after |
| **G2-control** | **a ghost cannot pass G2**: a layered window that never received a composite reads back **0 non-zero pixels of 14400** | same call | measured, and **re-measured on every suite run** |
| **G3** | the OS holds the rectangle, the ex-style, the topmost band and the hit test in both polarities, and the surface never takes the foreground | `GetWindowRect` / `GetWindowLongPtr` / `GetTopWindow`+`GetWindow` / `WindowFromPoint` / `GetForegroundWindow` | all yes |
| **G4** | **THE COMPOSITED DESKTOP distinguishes a glyph pixel from the background behind it, and a fully transparent pixel from an opaque black one** | `BitBlt(SRCCOPY\|CAPTUREBLT)` over a KNOWN background | see the table below |
| **G5** | the composite is genuinely PER-PIXEL: a half-alpha sample is exact premultiplied source-over of the frame over the measured background | same | `0xE49485` predicted and read |

### G4 is the packet's central trap, discharged by measurement

One capture, one window, five points, and a control capture with the surface hidden:

| point | glyph UP | glyph DOWN | what it settles |
|---|---|---|---|
| margin (background only) | `0xC8280A` | `0xC8280A` | the capture is live and the background really reached the screen |
| alpha 0 | `0xC8280A` | `0xC8280A` | **a fully transparent pixel shows the background BEHIND it** |
| opaque BLACK | `0x000000` | `0xC8280A` | **opaque black is NOT transparent** — same window, same capture, different value |
| opaque magenta | `0xFF00FF` | `0xC8280A` | **a glyph pixel is distinguished from the background** |
| alpha 128 white | `0xE49485` | `0xC8280A` | the blend is per-pixel and arithmetically exact |

**Four distinct values from one capture**, which one uniform `LWA_ALPHA` over an opaque frame cannot
produce, because that mechanism has one alpha for the whole rectangle and this frame has four.

### WHERE THE CHAIN STOPS — stated plainly

1. **No human watched anything.** `watched-verified` is a named manual gate and **no automated step
   on any platform discharges it, Windows included.**
2. **`PrintWindow` is BLIND to the very distinction the packet names**, and that limit is asserted
   rather than confessed: a fully transparent pixel and an opaque BLACK pixel both read `0x000000`
   there. The window read-back proves the frame reached the OS; it can never prove transparency.
   Only G4 separates them, and G4 is machine-conditional.
3. **A window read-back is not a monitor** — SP-111's measured limit, inherited unchanged.
4. **G4 is machine-conditional and the condition is MEASURED, never skipped.** A foreign topmost
   window can own the region and one did during probing — the shipping WPF product. The arbitration
   (§3) measures it, retries a bounded number of times, and NAMES the intruder if it never wins.
   Every fact asserts unconditionally: a desktop this run cannot win is a RED fact, not a skipped one.
5. **Partial-alpha read-back exactness is not claimed by the product.** Measured stable three times
   in probe round 3, and ALSO measured once at half its expected value in round 1 after a
   `GetDC(hwnd)` + `BitBlt` on the same window. So the product anchors on alpha 255 and alpha 0 only
   and never calls `GetDC` on its own surface. Partial alpha is asserted only in G4, where the
   arithmetic is exact.
6. **Nothing measures cadence, order or timing.** Every frame advance in every fact is driven by hand
   on the injected clock. A logo that moved at half speed, or backwards, satisfies every check.
7. **Linux is unproven**, refuses in type on every operation, and its **five-step** gate is
   undischarged — with step 5 expected to be impossible under Wayland, where a client cannot read
   back the composited output at all.
8. **No headed capture was taken.** `presentation-verified` is untouched.

---

## 1. HOW SP-099's GHOST IS AVOIDED — four answers, and the OS supplies the fourth

SP-099's recorded hazard, **re-measured three times here** and asserted in
`SP099sHAZARDIsREMEASURED_UniformModeRefusesPerPixel_ButTheStyleToggleLetsItThrough`:

> `UpdateLayeredWindow` alone fails with **87**; toggling `WS_EX_LAYERED` alone is harmless;
> **toggle then ULW succeeds and the alpha read-back is gone forever.**

1. **This capability never touches a window it did not create.** `Glyph/**` owns its own window
   class, its own `CreateWindowExW` and its own handle. There is no constructor parameter, no factory
   argument and no P/Invoke in this packet that takes a foreign `hwnd`. **`Overlay/**` is
   byte-identical to base.**
2. **The two lines are never adjacent anywhere.** `WS_EX_LAYERED` is set **in the `CreateWindowExW`
   call itself** and never cleared; the click-through flip re-asserts it in the same word.
3. **`SetLayeredWindowAttributes` is not declared anywhere in this capability.** Measured reason:
   calling it on a ULW window succeeds and then **permanently** refuses every later ULW with 87.
4. **And the door is barred from the other side, by the OS.** ULW on a window that only ever had
   `SetLayeredWindowAttributes` — the overlay's exact shape — returns **FALSE err 87**, with the
   overlay's alpha (153) intact. Asserted.

**The ghost check itself is REPLACED, not reused.** The overlay's is
`GetLayeredWindowAttributes` holding a non-zero `LWA_ALPHA`; a ULW window answers that FALSE by
design, which is byte for byte what a never-composited layered window answers. The replacement is
G2 + G2-control, which is **strictly stronger** — the overlay's check only proves the OS holds a
NUMBER. And a frame that cannot be proven is refused: an all-transparent composite reads back
**0 non-zero of 25600**, exactly the ghost, so `Present` and `Paint` both refuse a frame with no
fully-opaque non-black pixel.

**The coexistence run asserts the overlay's alpha is still 153 at four moments** — the one number
that would go to -1 if anything in `Glyph/**` ever reached the overlay's handle.

---

## 2. BOUNCING TEXT SHIPS. D83 AND D84 BOTH CLOSE

They close together because they were the same missing thing seen twice.

- **D83** — transparency-backed glyphs on a surface that composited one uniform alpha over an opaque
  frame. At the shipped default opacity of **100** that was a black screen with a word on it.
  **Closed:** the surface composites per-pixel alpha whose transparent pixels are measured, on the
  composited desktop over a known background, to show what is behind them.
- **D84** — moving means re-`Present`ing, which walks the whole top-level z-order and momentarily
  clears click-through, sixty times a second. **Closed:** `MoveTo` is one `UpdateLayeredWindow`
  carrying a destination point — no style write, no z-order walk, no hit test — and its `Available`
  is earned from `GetWindowRect` and claims the RECTANGLE only, saying so in its own words.

The module ships as its **MOTION half** and says so three ways, on the precedent SP-109/SP-111/SP-113
set: the row title, a notice the panel **LEADS with positionally**, and `Ready()` returning
`Degraded` with `bouncing-text-transforms-absent` on **every** run however healthy. The six per-frame
transform effects (two of which ship ON upstream), the XP chain, the achievement and haptic hooks,
the second logo, the dual-monitor spread, the pause-during-video coupling and the OCR self-exclusion
rectangles are **not ported and are named** (D160–D167).

**The dot's eighth meaning is COMPOSITE**: the OS's own copy of the surface still carries the frame's
opaque ink. Not MOTION (SP-111's seventh) — a logo that stopped moving is still a picture, where a
video that stopped advancing is a dead one. Not the SCREEN (SP-105's second) — a layered window can
be present, visible, on top and composite NOTHING, and this is the first module in the port whose
surface can be in that state.

---

## 3. COEXISTENCE — five surfaces, and the honest position

SP-113's final review recorded that the coexistence evidence **does not scale past four disjoint
rectangles**. I did not extend `PointerCoexistenceTests` and I did not copy a pair.

**What is still proven by disjointness.** The four landed rectangles keep SP-113's own positions and
the glyph surface takes a fifth that intersects none of them —
`TheFifthRectangleIsDisjointFromAllFour_AssertedRatherThanAssumed` measures it rather than assuming
it. `GlyphCoexistenceTests` reads all four through their OWN instruments (`OverlayWindowProbe`,
`InputWindowProbe`, `PointerWindowProbe`, the video capability's own oracle), unmodified, at four
moments across the glyph surface's whole lifetime.

**What disjointness could NOT have proven, and where it moved to.** This capability's evidence
**requires** an overlap: proving a transparent pixel shows the background behind it means putting the
surface OVER a known background. Disjointness is not merely insufficient — it is incompatible with
the fact. So ownership is **measured**: the z-order is walked from the top, every visible window
strictly between the surface and its background is fetched with `GetWindowRect` and tested for
intersection with the sampled area, and the pair is re-raised a **bounded count** of times (never a
wall-clock wait) until nobody is between them. A run that never wins reports **who** owns the region,
by class name and rectangle. **Both arms were observed:** with the two raises adjacent the list is
empty; with an ordinary interval between them the shipping WPF product sat in the gap and the sampled
"background" pixels were its own.

**What neither proves, plainly.** Five surfaces under **contention**. Every reading is taken at a
quiet moment of this run's own making. A foreign topmost window can still own any point on the one
machine-global screen — the residue the port has recorded since SP-099. Nothing here proves an
ORDERING the OS did not already report, and nothing here is a headed capture.

---

## 4. PROVING IT BITES — 72 mutations, five rounds, **49 caught, 23 survive**

Every mutation was applied to the committed tree, the closed suite run, and the file restored with
`git checkout --`; `git status` was clean after every round. Raw logs beside this record:
`sweep-round1.log` … `sweep-round4.log`, and the driver is `sweep.mjs`.

**A process defect, recorded rather than only fixed.** Round 1 failed to patch **22 of 70** mutations
for a reason with nothing to do with the code: the working tree is CRLF (git normalises on checkout)
and every needle was written with LF, so every multi-line needle missed. The same mismatch produced
the "restore drift" warnings on the first mutation of each file — `git status` was clean, so the
content was identical and only the line endings differed. Round 2 re-ran all 22 with LF-normalised
matching. **A second one:** round 3's `git checkout --` restored the COMMITTED version of a file
whose fix was still uncommitted, silently reverting it; the fix was re-applied and **committed before
re-sweeping**, and the run that proves it is round 4.

**The books:** 72 distinct mutations; **49 caught** (24 + 12 + 4 + 9); **23 survive**; 49 + 23 = 72.

**Round 5 exists because code review returned REVISE and was right.** Nine survivors this record had
classified as unreachable were reachable, and the reasons given for two of them were false. Round 5
re-ran the ten they touched: **9 caught, 1 survives.** What changed is in §4.1.

### 4.1 WHAT CODE REVIEW CAUGHT, AND IT WAS TEST DEPTH THREE TIMES

**(a) The module had no test that ever constructed it.** `BouncingTextModuleTests` covers the
motion, the raster and the presenter's cadence and reaches `BouncingTextEffect` only through a
reflection tripwire and a colour parser. `Engage`, `Ready`, `ReleaseWork` and `WorkIsRunning` were
entirely unpinned — **six survivors on their own** — and, worse, **two of this record's own headline
claims were prose**: that `Ready` returns `Degraded`/`bouncing-text-transforms-absent` on every
healthy run (the constant was referenced by no test at all), and that the dot's eighth meaning rests
on `Running` rather than on `Showing`.

**The excuse this record gave was false.** It said those survivors "need the full session rig, and
the sweep's scope is the unit project only". Three sibling modules build exactly that rig in the same
project (`SpiralOverlayEffectTests`, `BubblePopModuleTests`, `MandatoryVideoModuleTests`).
`BouncingTextEffectTests` now does too, with a surface double that can be **SHOWING and NOT RUNNING**
and **ENGAGED and NOT SHOWING**, because those are the two states the dot and the teardown turn on.
M-bg, M-bj, M-bk, M-bl and M-bm are caught; M-bm no longer depends on the headless suite.

**(b) M-bh was a FALSE EQUIVALENCE — the fifth in four waves, and mine.** This record claimed the
module's invisible-opacity branch was redundant because the presenter checks it too. **The
presenter's copy is reached only from the module's own last line**, i.e. *after* the null-surface
gate and the unbound-signal gate. Delete the module's branch and an opacity-0 engage composed with no
surface answers `effect-no-surface`, and one with an unbound signal answers `effect-no-ui-thread` —
neither of which is the truth, and both of which are first-class states in the module's own
constructor contract. **The claim is withdrawn**, and
`OPACITYZEROIsDegradedTRANSPARENT_EvenWithNoSurfaceAndNoUiThread` asserts all three compositions.

**(c) Three "could not be staged" survivors were reachable with instruments already shipped.**

- **M-bn** — one line. `string.IsNullOrEmpty` does not catch whitespace, so `" "` goes through GDI+,
  draws no glyph and comes back with no provable ink. `Assert.Null(Render(" ", …))` kills it.
- **M-i** — the `glyph-uniform-alpha-mode` refusal is **this packet's central hazard in product
  form**, and calling it unreachable was the worst of the three. It is staged now: a real surface
  composites, is given uniform attributes **from outside the capability** with the probe's own
  declaration, and is asked again. Both entry points are asserted, and they give **different**
  refusals — `Present` names the mode, `Paint` reaches `UpdateLayeredWindow` and reports the OS's own
  87 — and both are true.
- **M-ac** — the record itself said this was "one more differential run and it is named here rather
  than done", on the user-facing dial the product ships at 100. It is done: a second
  composited-desktop capture at opacity 0.5, same frame, same bytes, only the multiplier changed.

**And the half-opacity leg produced a finding of its own — which I then got wrong, and final review
overturned in my favour.**

The prediction came back **exact at three of four sampled points and one unit low in the red channel
of the fourth** (predicting `0xD65E47` where the screen holds `0xD65E48`). I wrote that no reading of
the documented formula reproduced it, refused to change `CompositeOver` on the ground that fitting an
oracle to a single datum makes it a copy of the data, asserted **within one unit** at 128, and
declared the residue in three places.

**The principle was right and the application was wrong, and the reviewer showed the arithmetic.**
`CompositeOver` was rounding **three times** — once for `α`, once for the source term, once for the
background term — and summing the three rounded values. Evaluate the *same* formula exactly and round
**once**, at the end, and it reproduces the screen: `α = 128·128/255 = 64.25` was losing its `.25`
and `10·191/255 = 7.48` was losing its `.48`, and the two discarded fractions sum to `0.73` — enough
to carry `71.73` up to `72`. At constant 255 the two models are identical, which is why the error was
invisible until a second opacity was ever measured.

So this is **not** fitting the oracle to a datum: it is the same formula with the number of roundings
corrected from three to the one that belongs there. Verified before changing anything, over all eight
measured values across both opacities: **per-term reproduces 7/8, single rounding reproduces 8/8.**
The tolerance is **deleted**, `128` is asserted exactly as `255` is, and the differential re-run
against a real screen passes with no allowance anywhere.

**And the tolerance was worse than an inelegance.** `±1` per channel is precisely the size of the
defect this leg exists to catch, so it would have silently absorbed a real one-unit-per-channel
regression at every non-255 setting of the dial. A tolerance sized to one observation is not a safety
margin; it is a hole with a comment on it.

**What remains true:** only the constant-alpha values **255 and 128** have been read off a screen at
all. Every other setting is arithmetic, and the model's own doc comment, the evidence class and §8
all say so.

### THE SHARPEST FINDING — a real defect, in the most dangerous line in the packet

`GlyphTextSource` was **completely uncovered** in round 1: mutating the transparent clear to an
**opaque black** one, the premultiplied pixel format to a straight-alpha one, the rounding repair to
a no-op and the margin to zero all survived. An opaque clear is **D83's black screen restored in one
constant**.

Worse, the fix exposed a defect rather than only a gap. The premultiplication repair clamped **any**
overage, which **silently converts a straight-alpha buffer into a wrong-but-legal premultiplied
one** — which is exactly why the pixel-format mutation was undetectable. The repair is now bounded at
**one unit**, which is what a premultiplying rasteriser's own rounding can produce, and refuses
anything larger. The format mutation is now caught with six red facts. **The comment that claimed the
clamp was a rounding repair was true of its intent and false of its effect**, and it has been
rewritten to say what it does.

### The other holes round 1 found, and closed

| # | mutation | closed by |
|---|---|---|
| M-g | the constant-alpha floor is dropped | a theory row at an opacity that really rounds to zero |
| M-h | `Present` skips the inkless-frame refusal | `PRESENTALSOREFUSESANINKLESSFRAME_NotOnlyPaint` — only `Paint` was ever asked |
| M-al / M-an | the left and bottom wall snaps are deleted | the escape fact now sweeps **twelve** seeds; with one seed only the wall that trajectory reached first was under test |
| M-bo | the transparent clear becomes opaque black | `THEBACKGROUNDISFULLYTRANSPARENT_…` |
| M-bp | the pixel format becomes straight ARGB | the same file's facts, once the repair stopped hiding it |
| M-af | the Linux refusal loses its manual gate | `TheLinuxRefusalNamesTheROUTE_…` (round 1's needle was wrong; re-run) |

### The 23 survivors — every one classified, none papered over

**Three are EQUIVALENT MUTANTS, with the measurement that makes them equivalent.** A fourth claim
(M-bh) was made in the first draft, was FALSE, and is withdrawn above. All three below were
independently enumerated by code review — every consumer of the mutated symbol named — and upheld:

- **M-c** — `Scale(a, 255)` versus `a*255/255`. For every `a` in 0..255 both are exactly `a`
  (`(a·255+127)/255 = a` by integer division), and 255 is the only constant alpha any fact asserts.
  **Equivalent under the measured arm**, and the arm it is not equivalent under (constant < 255) is
  the same hole M-ac names.
- **M-u** — the resize-in-move refusal. Two consumers, both in `Win32GlyphSurface.MoveTo`. Removing
  it does not change the reported OUTCOME: a position-only ULW does not resize, so `GetWindowRect`
  returns the original extents and the move is refused by the rect confirmation with the same code.
  **It is not a no-op, and the first draft's wording elided that**: the mutant still issues the
  `UpdateLayeredWindow` and MOVES the window before refusing, so the surface ends up somewhere the
  caller's own bookkeeping does not have it. Equivalent in the returned state, **not** equivalent on
  screen — which is precisely why the guard is kept.
- **M-z** — dropping `WS_EX_LAYERED` from the click-through re-assert word. The window is created
  layered and the style is never cleared, so the OR is a belt. **The sweep proves it is a belt.**
**Twenty are UNCOVERED, and each names why:**

- **M-m, M-n, M-o, M-p, M-t, M-w, M-y** — the geometry, style, visibility, foreground and
  swallowed-click refusals. Each guards a state that **did not occur on this machine and could not be
  staged**: a window that reports the wrong rectangle, loses a style bit it was given, refuses to
  hide, takes the foreground with `WS_EX_NOACTIVATE` set, or swallows a click with
  `WS_EX_TRANSPARENT` set. Every one of them is a real refusal with a real code and no reachable
  input. **This list used to begin with M-i and that was wrong** — see §4.1(c).
- **M-j, M-k, M-l** — `Present` ignoring the content, z-order or routing refusal. The refusals never
  fire here, so ignoring them is invisible. **Covered from the other side**: M-s (PrintWindow always
  fails) reds 21 facts and M-ad (the window is not created layered) reds 26, which is what proves the
  read-back and the layered style are load-bearing.
- **M-q, M-r** — the ink loop emptied and the transparent loop skipped. Same family: the content is
  always correct here, so weakening the comparison changes no answer. Same coverage from M-s/M-ad.
- **M-x** — skipping the opaque leg of the input differential. The leg exists to make leg two
  non-vacuous, and on a machine where the surface does own its point removing it changes no answer.
  The PROPERTY is covered from the harness side (`CatchesItsOwnPointWhenOpaque`, taken with the
  probe's own style write); the product's own leg has no fact.
- **M-ae** — the z-order predicate weakened. Isolating it needs a foreign topmost window that stays
  above the surface, which is a race rather than a fact — **the same residue SP-111 recorded as its
  M-ac**.
- **M-af2** — the Linux refusal detail losing its compositing-manager clause. The manual gate, which
  is appended to the same string, names it too, so the assertion still passes.
  **Redundant-by-appendix**, and the redundancy is the point: the gate is where a Linux implementer
  reads it.
- **M-az** — the PRESENTER's `Running` dropping its second and third clauses. `WorkIsRunning`
  (M-bg) is now caught by the module lab, but the presenter's own property still needs `showing` true
  while `lastFrameHeld` is false, and `Advance` sets `_lastFrameHeld = false` and then calls
  `Withdraw()` — clearing `_showing` — inside the same synchronous call, so no external observer ever
  sees the interior state. It is the ONE survivor round 5 did not close, and it is the presenter's,
  not the module's.

  **It is the same CLASS of residue SP-111 recorded at its M-be, and not the same thing.** SP-111's
  is `WorkIsRunning` dropping `ScheduleArmed`, blocked by `OwnedSessionEffect.Dot` short-circuiting
  and by `ReleaseWork` being `protected sealed` on a sealed module — a different symbol, in a
  different class, blocked by a different mechanism. The shared property is only that an interior
  conjunct is unobservable from outside. **After five false equivalence claims in four waves, saying
  "the same class as" where the first draft said "the same residue as" is not pedantry**: an identity
  claim between two survivors is exactly the kind of assertion this packet has already had to
  withdraw once.
- **M-ba** — the frame timer not disposed on withdraw. Under the hand clock the next fire returns
  early because the surface is down, so the OUTCOME is identical; on the real
  `System.Threading.Timer` it is a **leak**, not a behaviour, and no fact in this suite can see a
  leak.
- **M-bq, M-bs** — the rounding repair removed entirely, and its bound removed. Both are defensive
  against a GDI+ rounding artefact that **did not occur in any render this suite performs**, so
  neither can be observed. The bound's VALUE is what M-bp now proves is load-bearing.
- **M-br** — the measure dropping the margin. No fact ties the measured size to the margin tightly
  enough: "wider than twice the margin" is true without it for every word tested.

**Scope, stated rather than implied.** Each mutation was run against the packet's own suites plus
`ContinuousEffectSpineTests`, `SecondEffectSpineTests`, `AudioModuleSpineTests`, `SessionSpineTests`,
`StudioSurfaceNoticeTests` and `MovingEffectSpineTests`. That is narrower than a whole-suite
discipline, and the mitigation is that the full unit suite (2062), the full headless suite (121) and
both gates were run green afterwards on the restored tree.

---

## 5. THE FOUR LANDED SURFACES, PROVEN UNHARMED

`Overlay/**`, `Input/**`, `Audio/**`, `Video/**` and `Pointer/**` are **byte-identical to base**
(`git diff --stat f3c751c1 -- <those paths>` is empty). They are CONSUMED, and every reading of them
is taken through their own instruments.

| what | fact |
|---|---|
| the overlay stays click-through at four moments, and its own differential still bites | `TheOverlayStaysCLICKTHROUGH_AtAllFourMoments_IncludingDuringAMove` |
| **the overlay still holds its UNIFORM alpha (153) at four moments** — the one number SP-099's hazard would destroy | `THEOVERLAYKEEPSITSUNIFORMALPHA_WhichIsTheONEThingThisCapabilityCouldHaveDestroYED` |
| it keeps its band, never becomes the foreground, and re-earns its own `Available` | `TheOverlayKeepsItsBand_NeverBecomesTheForeground_AndStillEarnsItsOwnAvailable` |
| the Lock Card keeps the foreground AND the system keyboard focus through a present, a move and a withdraw | `THECARDKEEPSTHEFOREGROUNDANDTHEKEYBOARD_ThroughAPresentAMoveAndAWithdraw` |
| the video surface's own read-back still confirms its picture | `TheVideoSurfaceStillHoldsItsPicture_WithAGlyphSurfaceOnTheDesktopBesideIt` |
| the pointer target still owns its own point | `ThePointerTargetStillOwnsItsOwnPoint` |

The eleven landed modules' facts pass unchanged. Four existing facts were **strengthened, not
weakened**: three rack-order lists and one refusal list grew by one entry (0 count change), and
`RealDesktopCollectionGuardTests` gained three helpers and three bound controls.

---

## 6. DIVERGENCES FROM D152

Full rows with citations in `client/docs/wpf-surface-reachability.md` §SP-115.

| # | In one line |
|---|---|
| **D152** | A plain Win32 layered window composited with `UpdateLayeredWindow` + `AC_SRC_ALPHA`, not a WPF transparency-backed `Window`. **D83 closes** |
| **D153** | The overlay's ghost check could not be inherited and is REPLACED by a read-back of the surface, with a measured negative control |
| **D154** | That read-back cannot distinguish a transparent pixel from an opaque black one, and the surface says so in its own `Available` |
| **D155** | `GlyphFrame` throws on a non-premultiplied buffer, because the OS accepts one silently |
| **D156** | A frame with no fully-opaque non-black pixel is refused: nothing could tell it from the ghost |
| **D157** | `MoveTo` is one call and claims the rectangle only. **D84 closes** |
| **D158** | A 50 Hz cadence on the session clock, in the presenter, where WPF rides the composition clock |
| **D159** | Almost every frame is a MOVE; a re-raster happens only on a bounce |
| **D160** | The six per-frame transform effects are not ported, and the row is the MOTION half in three places |
| **D161** | No XP, no achievements, no haptics, no bark egg — the bounce and the corner hit are counted |
| **D162** | One logo, primary display |
| **D163** | Arial, not Segoe UI |
| **D164** | Offset copies for the shadow and the outline; GDI+'s flat API has neither a blur nor a text-path stroke |
| **D165** | The opacity dial is the surface's uniform multiplier over the frame's own per-pixel alpha, which is also what keeps the evidence anchored |
| **D166** | The phrase pool is a LIST, because order is load-bearing for a seeded run |
| **D167** | The video pause and the OCR self-exclusion coupling are not ported |
| **D168** | The dot's EIGHTH meaning: COMPOSITE |
| **D169** | Disjointness is kept for the five hit-test points and REPLACED for the one deliberate overlap by an occlusion arbitration |
| **D170** | Linux refuses typed with a five-step gate whose step 5 Wayland probably cannot pass |

---

## 7. FILES CHANGED

**Product — new (`client/src/CcpClient.Desktop/Glyph/`):** `GlyphReasonCodes.cs`, `GlyphFrame.cs`,
`GlyphSurfaceRequest.cs` (+ `GlyphBounds`), `IGlyphSurface.cs`, `Win32GlyphInterop.cs`,
`Win32GlyphSurface.cs` (+ `GlyphNativeHandles`), `UnsupportedGlyphSurface.cs`,
`GlyphSurfaceFactory.cs` (+ `LinuxManualGate`, `WaylandNote`).

**Product — new (effects/session):** `Effects/BouncingTextField.cs` (+ `BouncingLogo`),
`Effects/BouncingTextPresentation.cs` (+ `BouncingTextColourMode`), `Effects/GlyphTextSource.cs`,
`Effects/BouncingTextSurfacePresenter.cs` (+ `IBouncingTextSurface`), `Effects/BouncingTextEffect.cs`,
`Session/BouncingTextPresetDocument.cs`.

**Product — changed:** `Session/EffectReasonCodes.cs` (four additive codes),
`Session/SessionParticipant.cs` (the twelfth module, its store, its surface, the rack order),
`Views/Pages/StudioPage.axaml` + `.axaml.cs` (the row after Spiral, the panel, its leading notice and
three dials).

**Tests — new:** `GlyphWindowProbe.cs`, `GlyphSurfaceObservations.cs`, `GlyphCapabilityTests.cs`,
`GlyphAlphaDifferentialTests.cs`, `GlyphCoexistenceTests.cs`, `GlyphTextSourceTests.cs`,
`BouncingTextModuleTests.cs`, `BouncingTextEffectTests.cs` (the module lab, added at review), plus
four headless facts in `StudioRackHeadlessTests.cs`.

**Tests — changed (all strengthenings, 0 count change):** `RealDesktopCollectionGuardTests.cs`,
`ContinuousEffectSpineTests.cs`, `SecondEffectSpineTests.cs`, `AudioModuleSpineTests.cs`.

**Docs:** `client/docs/wpf-surface-reachability.md` (§SP-115, D152–D170),
`client/docs/verification-harness.md` (the glyph evidence class, four classes).

**Not touched:** `client/src/CcpClient.Desktop/{Overlay,Input,Audio,Video,Pointer}/**`,
`client/tests/floor/*`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`,
`docs/constitution.md`, `.spine/**`, `.claude/**`.

---

## 8. WHAT THIS WORK DOES NOT PROVE

- **Nothing here proves a human saw a bouncing word.** `watched-verified` is undischarged and is not
  dischargeable by this suite or by any automated step on any platform.
- **No headed capture was taken**; `presentation-verified` is untouched. The composited-desktop read
  is a screen read from inside the process, not a photograph, and cannot see a Magnifier, a mirror
  driver, an exclusive-fullscreen swap chain or a physically dark monitor.
- **Nothing measures cadence, order or timing.** Every frame advance is driven by hand.
- **Nothing proves five surfaces coexist under CONTENTION** — see §3.
- **Only the constant-alpha values 255 and 128 have been read off a screen at all.** Both are exact
  against the oracle with no tolerance, and every other setting of the uniform dial is arithmetic
  that no display has checked.
- **Concurrency is single-threaded.** The presenter's gate is held across no capability call and the
  cadence is driven inline; two threads racing a frame against a teardown are not covered.
- **The composite cost was not profiled.** A re-raster is a GDI+ text draw plus a full-buffer
  premultiplication pass; measured fast enough in the harness at glyph sizes, not profiled at 300 %
  size on a contended machine.
- **Linux is unproven** on every clause, refuses in type, and its five-step gate is undischarged.

---

## 9. AN INTERMITTENCY, MEASURED ON BOTH SIDES RATHER THAN BLAMED

Late in the run the full unit suite went red twice on
`FlashDrawTests.ARealImageFile_ReachesTheCompositedDesktop_AndLeavesItWhenTheFlashIsHidden`, a
LANDED fact this packet does not touch, and green on its own every time. Rather than assume it was
mine or assume it was not, the base commit was checked out in this same worktree and run repeatedly.

| tree | runs | red | which test |
|---|---|---|---|
| base `f3c751c1` | **7** | **1** | `SpiralOverlayEffectTests.DisarmReleasesTheWorkUNCONDITIONALLY_EvenWhenItThoughtItWasNotArmed` |
| lane `f693325d` | **8** | **2** | `FlashDrawTests.ARealImageFile_ReachesTheCompositedDesktop_…` |

**The base tree is intermittently red on this machine, on a DIFFERENT test.** So the suite has a
pre-existing intermittency here and this packet did not introduce the phenomenon. What this
measurement does **not** establish is that the two share one root cause, or that the lane's rate is
the base's — fifteen runs is not enough to separate one-in-seven from two-in-eight, and I did not run
more. **Both readings are reported and neither is used to dismiss the other.**

Nothing was retried away, quarantined, skipped, or added to `allowedSkips`, and no assertion was
weakened. The gate results quoted at the head of this record are from runs that were green; the red
runs are reported here rather than discarded, and a reviewer should expect the floor to red
occasionally on this machine for a reason older than this packet.

The most likely mechanism, named without being claimed: both failing facts read machine-global state
that a foreign topmost window can perturb — the residue `client/docs/verification-harness.md` already
admits, and the one the shipping WPF product produced unaided during this packet's own probing. This
packet's own real-desktop runs are inside `RealDesktopCollection` and hold the machine-wide lease, so
they cannot contend with the landed ones in-process; what they cannot exclude is a fourth party.
