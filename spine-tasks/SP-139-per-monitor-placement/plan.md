# SP-139 — DEFERRED ON HARDWARE (design complete and approved; NOT pending work — see §9)

Branch `feat/crossplatform` in worktree `.claude/worktrees/agent-a765c43b5ebde2a96`, base `2508b39c4`.
Nothing under `client/src/**` or `client/tests/**` has been edited at the time this file was written.

---

## 0. The packet's three premises, re-verified against source

| Packet claim | Verdict | What I read |
|---|---|---|
| `OverlayDisplays.Enumerate()` returns every display, primary first, `[]` off Windows | **TRUE** | `client/src/CcpClient.Desktop/Overlay/OverlayDisplays.cs:35-73` — `EnumDisplayMonitors` + `GetMonitorInfoW`, sorted primary-first at `:71` |
| `OverlaySurfaceSet` already holds many | **TRUE** | `Effects/OverlaySurfaceSet.cs:39` `List<Slot> _slots`, `:107` `LiveRects()` |
| `PrimaryDisplayPlacement.PrimaryBounds()` deliberately reduces to one | **TRUE** | `Effects/PrimaryDisplayPlacement.cs:35-54` |
| "nine call sites asking for one rectangle … into a container that already holds many" | **HALF TRUE, and this is the packet's own stale premise** | Only **four** of the nine go through `OverlaySurfaceSet` at all. The other five run on four *different* capabilities — `Video/`, `Input/`, `Pointer/`, `Glyph/` — **none of which is inside this packet's File Scope.** See §3. |

### The premise correction that changes the shape of the packet

`Effects/**` + `Overlay/**` is the whole write zone. The five non-overlay consumers are:

- `VideoSurfacePresenter.cs:515` → `CcpClient.Desktop.Video` (`IVideoPresence`)
- `LockCardEffect.cs:579` → `CcpClient.Desktop.Input` (`InputBounds`, a keyboard-owning surface)
- `BubbleCountEffect.cs:849` → `CcpClient.Desktop.Input` (same)
- `BubblePopSurfacePresenter.cs:157` → `CcpClient.Desktop.Pointer` (`PointerBounds`)
- `BouncingTextSurfacePresenter.cs:136` → `CcpClient.Desktop.Glyph` (`IGlyphSurface`)

Three of those five would additionally need machinery that does not exist (input mirroring, glyph
clipping across a monitor seam, a second decoder), and one of them (video) is an **open owner
question on the board**. So the deliverable is *not* "nine consumers go multi-monitor". It is: the
four overlay consumers get upstream's real per-effect behaviour, and the other five get an
evidence-backed row saying why they do not.

### A second, larger correction: upstream's per-monitor behaviour is a USER SETTING

Every multi-monitor decision in the shipping app routes through `AppSettings.DualMonitorEnabled`
(`ConditioningControlPanel/CCP.Core/Models/AppSettings.cs:1917-1927`, **default `true`**, comment:
*"When enabled, content displays on ALL connected monitors (2, 3, or more). When disabled, content
only appears on the primary monitor."*), and five effects further route through a per-effect
`<Effect>TargetMonitor` sentinel resolved by `App.ScreenResolver.cs:30-71`
(`-1` follow-global, `-2` all, `0..N` one index, out-of-range falls back to `-1` without mutating).

The port has neither setting. Porting **upstream's default** (dual on, follow-global) is therefore
the correct outcome, and the absent controls are a divergence (D311), not a silent choice.

---

## 1. The nine consumers, decided per effect from upstream's behaviour for THAT effect

"All displays" below means *every enumerated display*. "One rolled display" means *one display
chosen at random per item*, which is a different upstream behaviour and not a lesser one.

| # | Port consumer | Upstream's own behaviour, cited | This packet |
|---|---|---|---|
| 1 | `FlashSurfacePresenter.cs:163` (Flash Images) | **ONE ROLLED DISPLAY PER IMAGE.** `PickMonitor` (`Services/Flash/FlashService.cs:2187-2203`) ends `return candidates[_random.Next(candidates.Count)]`; candidates come from `GetMonitors(DualMonitorEnabled)` (`:2205-2251`). It is called **per image** at `:493` and `:687`, stored on `data.Monitor`, and each window spawns on its own image's monitor (`:1110-1140`). **A flash is never mirrored to every screen.** | **CHANGE — to one rolled display per image.** This is the packet's "no blanket rule" case: applying place-on-every here would have been a *regression against upstream*, not a fix. |
| 2 | `SubliminalSurfacePresenter.cs:96` (Subliminals) | **ALL DISPLAYS.** `SubliminalService.cs:628-630` takes every screen when dual is on; `:649-670` loops and does `GetOrCreateScreenWindow(screen)` + `ApplyWindowStyles(win, screen.Bounds, …)` per screen. Confirms existing D66. | **CHANGE — one card per display, rasterised at that display's own size.** |
| 3 | `PinkFilterSurfacePresenter.cs:102` (Pink Filter) | **ALL RESOLVED DISPLAYS.** `OverlayService.cs:1149-1157`: `App.ResolveScreens(settings.PinkFilterTargetMonitor)` then one `CreatePinkFilterForScreen` per screen. Confirms existing D73. | **CHANGE — one tint surface per display.** |
| 4 | `SpiralSurfacePresenter.cs:133` (Spiral) | **ALL RESOLVED DISPLAYS, frames decoded ONCE and shared.** `OverlayService.cs:1347-1364` (GIF: `LoadSpiralGifFrames()` once, then a window per screen) and `:1408-1416` (video path). | **CHANGE — one surface per display, one open animation per DISTINCT display size, one shared frame index.** |
| 5 | `BouncingTextSurfacePresenter.cs:136` (Bouncing Text) | **ALL DISPLAYS, BUT NOT ONE LOGO EACH.** `BouncingTextService.cs:143` → `:316-329` computes `_minX/_minY/_maxX/_maxY` as the **union** of every screen, and `:332-343` makes one window per screen. **One logo roams the whole virtual desktop and crosses monitor seams**; the N windows are viewports, not N logos. | **NO CHANGE.** Porting the outcome needs a glyph frame clipped to each display's slice of a union-space logo — that is `Glyph/**`, outside File Scope, and it is a different problem from placement. Filed as **D315**. |
| 6 | `BubblePopSurfacePresenter.cs:157` (Bubble Pop) | **ONE ROLLED DISPLAY PER BUBBLE.** `BubbleService.cs:877-885` and `:925-935`: `var screen = screens[_random.Next(screens.Length)]` per spawn. | **NO CHANGE in this packet.** Reachable in principle from `Effects/` (`BubblePopField` owns spawn geometry and `Win32PointerSurface` already places anywhere on the virtual desktop), but it is a fifth distinct shape on a *different capability* with its own `LastPlacement`, and it adds nothing to the partial-failure question this packet exists for. Filed as **D314** with the exact route named. |
| 7 | `BubbleCountEffect.cs:849` (Bubble Count question card) | **ALL DISPLAYS — BUT ONLY THE PRIMARY OWNS INPUT.** `Windows/BubbleCountWindow.xaml.cs:332-385`: primary window `withAudio: true` and `Activate()`d last "so it has focus for keyboard input"; every other screen gets a **muted secondary**. Separately `BubbleCountService.cs:441-470` puts its fullscreen message on every screen. | **NO CHANGE — and the primary-only card is CORRECT, not a shortfall.** The port's card *is* upstream's primary window: the single keyboard owner. What is missing is the muted mirror, which needs `Input/**`. Filed as **D313**. |
| 8 | `LockCardEffect.cs:579` (Lock Card) | **ALL DISPLAYS, UNCONDITIONALLY — not even gated on `DualMonitorEnabled`.** `Windows/LockCardWindow.xaml.cs:1550-1600`: `var screens = App.GetAllScreensCached()` with no dual check at all, one card per screen, `isPrimary = screen.Primary && primaryWindow == null` so **exactly one card owns the keyboard and the rest are read-only mirrors**, plus the `#618` hardening that promotes a card when no screen reports Primary. | **NO CHANGE — and the packet's own hypothetical is disproved here.** "A modal card on four monitors may be wrong" is **false for this effect**: upstream deliberately covers every monitor, harder than anywhere else in the app, because a lock that leaves a monitor uncovered is not a lock. The port cannot reach it inside this File Scope — it needs N input surfaces with a single-writer input mirror, which is `Input/**`. Filed as **D312**, and it is the largest honest gap this packet leaves. |
| 9 | `VideoSurfacePresenter.cs:515` (Mandatory Video) | **PRIMARY ALWAYS; SECONDARIES CONDITIONALLY.** `VideoService.cs:2010-2028` and `:2492-2506` gate every secondary decoder on `ShouldFillSecondaryMonitors` (`:2040-2046`): `false` when dual is off; `true` at ≤2 screens; at **3+ screens it needs `FillAllMonitorsWithVideo`, which defaults OFF** (`AppSettings.cs:1955-1966`, "#389 … 3+ independent decoders lag high-res rigs"). | **NO CHANGE.** Three independent reasons, any one sufficient: (a) `Video/**` is outside File Scope; (b) board **line 250** makes the port's video monitor policy an **OPEN OWNER QUESTION** ("Confirm that unified fullscreen video always targets every connected monitor, superseding old `DualMonitorEnabled` and `FillAllMonitorsWithVideo` limits") — changing it now would pre-empt an owner decision; (c) board **line 119** is the P0 spike that owns it. Filed as **D316**. |

**Score: 4 change, 5 do not**, and two of the five (Lock Card, Bubble Count) are *correct as they
stand for the part the port has*, which is a finding rather than a shortfall — exactly as the packet
asked me to check for.

---

## 2. THE PARTIAL-FAILURE DECISION (the packet's real subject)

### What upstream does, and what upstream can say

Upstream's loops **do not abort on a failed screen**. `CreatePinkFilterForScreen` returns `null` on
an exception and the loop simply continues (`OverlayService.cs:1153-1157`); the spiral loop is the
same (`:1360-1364`); `LockCardWindow` shows what it can and warns (`:1594-1600`). It then reports in
two ways:

- a **boolean**: `PinkShowing => _pinkFilterWindows.Count > 0` (`OverlayService.cs:163`),
  `SpiralShowing => _spiralWindows.Count > 0` (`:165`) — **true when ANY window is up**;
- a **count**: *"Pink filter started on {Count} screens"* (`:1163`), *"Spiral started on {Count}
  screens"*, *"Created {Count} video windows"*.

So upstream's *screen* outcome under partial failure is **keep what came up**, and upstream's
*reported* outcome is a boolean that cannot distinguish 1-of-3 from 3-of-3 plus a log line that can.

### What this port will do

**Screen outcome: identical to upstream.** A display that refuses does not take the others down.
Nothing that came up is withdrawn because a sibling failed.

**Reported outcome: `CapabilityState.Degraded`, and nothing else.** The type already exists and its
XML doc is already exactly this case (`Capabilities/CapabilityState.cs:65`): *"Part of the capability
holds. `SurvivingSemantics` names exactly what survives; `Reason` names what does not and why."*

| Coverage | `LastPlacement` / `Engage` result | Why |
|---|---|---|
| `asked == 0` (OS enumerated none, incl. all of Linux) | unchanged `RecordNoDisplay()` → typed `overlay-no-display` | Nothing guessed. Linux stays `[]` and refuses typed. |
| `covered == 0` | the backend's **verbatim** refusal, unchanged | Today's behaviour byte-for-byte. Folding this into `Degraded` would claim something survived. |
| `0 < covered < asked` | **`Degraded(surviving: "<noun> is on {covered} of {asked} displays", reason: overlay-display-partially-covered + the first refusing display's OWN verbatim detail)`** | **`Available` here is precisely the fake-available shape the port bans**: true on one monitor, false on another. |
| `covered == asked` | **unchanged — the OS's own last `Available`, verbatim, NOT overwritten** | The surface's `Available.Detail` is load-bearing (`OverlayCapabilityTests`, `UserFacingClaimTests` read those exact words). Synthesising a coverage sentence over it would be a regression on a single-display machine, which is every machine that runs the floor today. Coverage is exposed as *counts* instead. |

**Why this is honest and not merely defensive**: the claim `Available` in this port means "the probe's
declared evidence confirmed support". With N displays the evidence is per display. A state that says
`Available` while one monitor holds nothing is a claim the evidence does not carry, and it is the
same defect class as the first attempt's `void Show()`. `Degraded` says *what survives* and *what did
not and why*, and it keeps the OS's own words for the failure rather than paraphrasing them.

**Why it costs nothing user-visible**: every panel switch in `Views/**` already has a
`CapabilityState.Degraded d => $"Partly drawn: {d.SurvivingSemantics}. {d.Reason.Detail}"` arm —
`StudioPage.axaml.cs:1601`, `:1620`, `:1639`, `:1759`, `:1823`, `VisualsPanelNotices.cs:78`. Nothing
in `Views/**` needs to be touched, which is fortunate because `Views/**` is outside File Scope.

**And the dots must NOT darken.** The row dots derive from `OwnedSessionEffect.WorkIsRunning`
(`Session/OwnedSessionEffect.cs:128-152`), which for these modules is a **screen** fact —
`PinkFilterEffect.cs:99` `_surface is { Showing: true }`, `SpiralOverlayEffect.cs:120`
`{ Running: true }` — not the engage state. Under partial coverage `Showing` stays true
(`LiveSurfaces > 0`), which is upstream's `PinkShowing` exactly. **This is pinned by a test** (§4
guard 10), because "returns Degraded" and "reports the module as not running" are two different
things and only the first is wanted.

---

## 3. The edits (smallest diff that carries the outcome)

All inside `client/src/CcpClient.Desktop/{Effects,Overlay}/**`.

1. **`Overlay/OverlayDisplays.cs`** — add `AllBounds()`, one line over the existing `Enumerate()`.
   The enumeration itself is **not touched** (packet: leave it alone). One helper, not four copies,
   because four copies of a display walk is the exact finding `PrimaryDisplayPlacement` was created
   for (SP-112).
2. **`Overlay/OverlayReasonCodes.cs`** — one new code, `overlay-display-partially-covered`.
3. **`Effects/OverlaySurfaceSet.cs`** —
   - `maxSurfaces` becomes `Func<int>` so a per-display module's cap can be *the display count*
     rather than an invented constant. Four production call sites; **zero test call sites** (no test
     constructs `OverlaySurfaceSet` directly — verified).
   - new `DisplaysAsked` / `DisplaysCovered` counters and one `RecordCoverage(...)` fold that
     implements the table in §2 and writes `LastPresent` **only** in the partial case.
   - The per-display *loop* is deliberately **not** shared: the four modules differ in frame
     construction, slot reuse and lifetime, and sharing the part that is not shared is what
     `PrimaryDisplayPlacement.cs:15-23` already refuses by name.
4. **The four presenters** — each gains **one optional constructor parameter**
   `Func<IReadOnlyList<OverlayBounds>>? everyDisplay = null`, defaulting to
   `() => display() is { } one ? [one] : []`.
   **This is forced by File Scope**: twelve existing test call sites construct these presenters with
   a single-display `Func<OverlayBounds?>` and **I may not edit a single one of them**. An additive
   optional parameter keeps every one of them compiling and green (a one-display list behaves
   exactly as today); a type change would break all twelve and would be an out-of-scope edit.
   `Product()` passes the real `AllBounds`.
   - **Flash**: `ShowOne` picks `displays[_random.Next(count)]`, short-circuited to `displays[0]`
     when there is one — deliberately, so the injected seeded `Random` streams in the existing
     `FlashSurfacePresenterTests` / `VisualsModuleTests` / `HapticLimbSiteTests` rigs do not shift.
     Marked with a `ponytail:` comment naming that as the reason.
   - **Pink Filter / Subliminals**: one slot and one frame **per display**, built at that display's
     own size, then `RecordCoverage`.
   - **Spiral**: `_slot` → one slot per display; `_animation` → keyed by `(Width, Height)` so
     identical monitors share ONE decode (upstream's "load frames once and share across all
     screens", `OverlayService.cs:1350-1364`) while a differently-sized monitor gets its own — the
     port's `Paint` refuses a frame whose size is not the surface's (`overlay-frame-size-mismatch`),
     so a shared bitmap across mismatched screens would be a refusal, not a stretch. One frame index
     for all, advanced once per tick, painted to every live surface from a single `Render(i)` per
     size (legal: `Paint` copies into the DIB before returning — `ISpiralAnimation` remarks).
5. **`client/docs/wpf-surface-reachability.md`** — a new `## SP-139` section, divergences only,
   **D311..D318**, in the file's existing 4-cell / 5-pipe row shape, plus its "What SP-139 does NOT
   establish" paragraph.

**Not touched, deliberately:** `PrimaryDisplayPlacement.PrimaryBounds()` keeps its four remaining
callers (Video, Lock Card, Bubble Count, Bubble Pop) — all four are *correct* to take one display
per §1, so the function is not deprecated and its doc gains no new claim.

---

## 4. The guards, and the exact edit each one reds on

New file `client/tests/CcpClient.Tests/PerMonitorPlacementTests.cs` (pure logic, no Avalonia, no
wall-clock wait — `ManualClock` + `TestWait` only). Every guard will be watched red at the committed
head with its SHA before the packet reports.

| # | Fact | Reds on this exact edit |
|---|---|---|
| 1 | `OverlayDisplays.AllBounds()` returns one entry per enumerated display, same order, primary first; `[]` when the OS enumerates none | reducing `AllBounds` to `[PrimaryBounds()]`, or reordering |
| 2 | Pink Filter given 3 displays places 3 surfaces at those 3 rects, all click-through | reverting `Engage` to `var display = _display();` and a single `Place` |
| 3 | Subliminals given 3 displays of DIFFERENT sizes renders each card at **its own** display's size | rendering once at `displays[0]`'s size and reusing it (which the overlay would refuse as `overlay-frame-size-mismatch`) |
| 4 | Spiral given 2 same-size displays opens **1** animation; given 2 different-size displays opens **2** | per-display `Open()` (a needless second decode), or one shared animation at the wrong size |
| 5 | Spiral's advance paints **the same frame index** to every live display | giving each display its own `FrameIndex` |
| 6 | **Flash places one image on ONE display, not on all of them**, and across many images uses more than one display | applying the blanket "place on every display" rule to Flash |
| 7 | **Partial failure**: 3 displays, display #1's presence refuses `Present`. Then: 2 surfaces live, **survivors not withdrawn**, `DisplaysCovered == 2`, `DisplaysAsked == 3`, and `LastPlacement is CapabilityState.Degraded` whose `Reason.Code == overlay-display-partially-covered` and whose `Reason.Detail` still contains the refusing backend's **own** words | returning `Available` because something came up (the banned fake-available shape), **or** tearing the working monitors down because one failed |
| 8 | **Zero coverage** keeps the backend's verbatim refusal and is **not** `Degraded` | folding `covered == 0` into the partial arm |
| 9 | **Full coverage** leaves the OS's own `Available.Detail` untouched | synthesising a coverage sentence over every success |
| 10 | Under partial coverage the module still reports **running** — `PinkFilterSurfacePresenter.Showing` true and `PinkFilterEffect.Dot == Live` — matching `PinkShowing => _pinkFilterWindows.Count > 0` (`OverlayService.cs:163`) | deriving the dot from the engage state, which would report a working two-monitor tint as "armed, nothing running" |
| 11 | Zero displays still returns the typed `overlay-no-display` refusal and attempts nothing (the Linux path) | guessing a display list off Windows |

Declared floor delta: **unit +11, headless 0** (final integer confirmed against the written file).
Pin is 2616 / 152, so the observed total is expected to be **2627 / 152**.
`spine-tasks/SP-139-per-monitor-placement/floor-delta.json` carries it; `floor.json` is never opened.

---

## 5. Evidence class — stated before the work, not after

**`draw-verified` at best, and in fact weaker than that: unit-level only.**

`[System.Windows.Forms.Screen]::AllScreens` on this machine, run in this session, reports **exactly
one display**: `\\.\DISPLAY1`, primary, `1646x1029`. `client/port.txt:134` names `DISPLAY3` as the
headed-evidence monitor, but it is **not attached in this session**, so:

- every multi-display fact in §4 will be proven against a **synthetic display list** injected through
  the presenters' display seam;
- that is a legitimate unit fixture and **a worthless parity claim**, and the difference is stated
  here rather than discovered at review;
- **no headed multi-monitor capture will be taken, and none of board line 119 is discharged.**
  `client/docs/verification-harness.md` governs: a headless frame never discharges a headed gate,
  and this packet does not even produce a headless frame.

What this will NOT prove, plainly: that a real second monitor takes a real layered window; any
composited pixel, geometry, DPI scaling, occlusion or z-order across a monitor seam; negative X/Y,
monitors above/below, vertical stacks, gaps, mixed scaling, portrait or flipped orientation,
hot-plug, rotation or rearrangement — **the entire board line 119 matrix, which stays OPEN**; any
Linux display backend; interaction, focus, audio or animation.

**Hot-plug / rotation, as they will actually behave** (packet: say what happens, do not fix it): the
display list is read **at each engage**, so a paced module (Flash, Subliminals) picks up a changed
topology on its next item, while a continuous module (Pink Filter, Spiral) keeps the surfaces it
placed at engage time until something re-engages it — a monitor unplugged mid-session leaves a slot
whose bounds no longer exist, and a monitor plugged in mid-session gets nothing. Upstream reconciles
both (`OverlayService.cs:585-599` `RefreshOverlaysForDualMonitorChange`, and
`Services/UI/DisplayChangeCoordinator`). **The port will not, and D318 says so.**

---

## 6. Divergences to be filed (D311 onward; D311 is free, SP-138 filed none)

D311 `DualMonitorEnabled` + the five `<Effect>TargetMonitor` controls are not ported; the port hard-codes upstream's default.
D312 Lock Card: upstream covers every screen unconditionally with one input owner + mirrors; port shows one card on the primary.
D313 Bubble Count: upstream's secondaries are muted mirrors; the port has the primary (input-owning) window only.
D314 Bubble Pop: upstream rolls a screen per bubble; the port's play area is the primary display.
D315 Bouncing Text: upstream bounces ONE logo over the UNION of every screen through N viewport windows; the port's logo cannot cross a monitor seam.
D316 Mandatory Video: upstream's `ShouldFillSecondaryMonitors` formula; the port stays primary-only, and the port's policy is an open owner question (board line 250) under a P0 spike (board line 119).
D317 Partial coverage reports `Degraded` where upstream reports a boolean that is true when any window is up.
D318 Hot-plug / rotation / rearrangement are not reconciled; upstream reconciles both.

Every row will be written with exactly five unescaped pipes, any `|` inside a code span escaped as
`\|`, and **verified by counting delimiters** rather than by reading.

---

## 7. Open question for the reviewer

**Is Spiral in or out?** It is the heaviest of the four (slot-per-display + animation-per-size +
shared frame index) and it is the one I would cut if this packet should be smaller. Cutting it leaves
the tint on every monitor and the spiral on one, which is visibly incoherent, so my recommendation is
**in** — but it is the only item here whose cost is materially above the rest, and the reviewer
should have the choice before I spend it.

---

## 8. State at this checkpoint, measured

- Base build: **0 warnings / 0 errors, 4 projects**, forced non-incremental (`check-warnings.mjs`).
- **Baseline floor, taken at `2508b39c4` before any edit:**
  - `CcpClient.Tests` **total = 2616**, passed 2600, **failed 14**
  - `CcpClient.HeadlessTests` **total = 152**, passed 152, failed 0
  - Both totals match the pin (2616 / 152) exactly.
- **The packet's prediction about the contended desktop names the wrong class.** It says three
  `PointerCoexistenceTests` facts are red. At this base **zero `PointerCoexistenceTests` are red**;
  the 14 reds are **all `GlyphCapabilityTests`**, and they share one symptom
  (`glyph-nothing-presented`, plus z-order and hit-test reads returning `False`) — the same
  *environmental* class the packet describes, arriving on the per-pixel layered-window capability
  instead of the pointer one. Named so the after-run is compared against **this** set, not the
  packet's:

  `AMOVEIsOneCall_ItEarnsAvailableFromGetWindowRect_AndTheContentSurvivesIt`,
  `AMismatchedFrameIsRefusedRatherThanStretched`,
  `AMoveThatWouldRESIZEIsRefused_BecauseTheLayeredSurfaceISTheFrame`,
  `ANDTHECAPABILITYNAMESTHEMODE_OnBothEntryPoints`,
  `ANINKLESSFRAMEIsREFUSED_BecauseNothingCouldTellItFromAGhost`,
  `AndTheMOVEsAvailableSaysExactlyWhatItDidNotReask`,
  `PaintReplacesTheContent_AndTheOSsCopyREALLYCHANGED`,
  `PresentEarnsAvailableONLYWhereTheMachineHasADesktop`,
  `PresentNAMESWhatItDoesNotClaim_TheTransparentPixelAndTheHumanEye`,
  `THEUNIFORMALPHAREFUSALISSTAGEDOnARealSurface_NotClassedAsUnreachable`,
  `TheExtendedStyleReadBackCarriesEveryBitThatWasWritten`,
  `TheOSsOwnZOrderPutsTheSurfaceAboveEveryOrdinaryWindow`,
  `TheWindowManagerRoutesThePointPASTIt_AndTOItWhenMomentarilyMadeOpaque`,
  `WithdrawTakesItOffTheScreenAndOutOfTheHitTest_AndKeepsTheComposite`
  (all `CcpClient.Tests.GlyphCapabilityTests.*`).

  These will not be chased and nothing will be added to `allowedSkips` for them.
  **Note the coincidence worth flagging to the reviewer:** `GlyphCapabilityTests` is the capability
  behind consumer #5 (Bouncing Text), which this plan does **not** touch — so this baseline red is
  not evidence for or against anything in §1, but a lane that later takes D315 will meet it.
- **No product file and no test file has been edited.** `plan.md` is the only artefact written, and
  it is committed (`b5d5f2cbb`) so the worktree is not empty at the checkpoint.

---

## 9. VERDICT — DEFERRED ON HARDWARE. Design complete and APPROVED. Archaeology landed.

**This plan is NOT pending work. Do not resume it as written; resume it from the evidence.**

Owner decision, 2026-08-22: **a second monitor is not available. Do not implement.** The plan was
reviewed and approved first — all three upstream claims survived adversarial checking, and the Spiral
recommendation in §3 was independently confirmed sound (`Effects/SpiralFrameSource.cs:30-36`
guarantees the frame buffer is valid until the next `Render` and that `Paint` copies into the DIB
before returning, so one `Render(i)` painted to every same-size surface is legal). The deferral is
about **evidence, not design**.

**Nothing under `client/src/**` was changed and no test was added.** The floor moved by zero
(`floor-delta.json` declares `0` / `0`). The archaeology this plan produced is now durable in
`client/docs/wpf-surface-reachability.md` as **D311–D322**, and `record.md` carries the verdict, the
baseline and the deviations. Those two documents, not this section, are what a later lane reads.

### The reason the work must wait rather than land unverified

Not "the fixture would be synthetic" — that alone is survivable. The decisive reason is that **the
hardware limitation reaches the guards themselves**:

- The display seam must grow **additively** (§3, D319), because twelve existing test call sites
  construct the four overlay presenters with a single-display `Func<OverlayBounds?>` and none was in
  File Scope. So an every-display source defaults to the single-display fallback — and **a
  `Product()` factory that forgot to pass it would leave ALL ELEVEN guards in §4 green. On a
  one-monitor machine no test can tell the difference.** The packet could ship completely wired to
  nothing and be undetectable here.
- **Guard 1 could not have been watched red at all.** An `OverlayDisplays.AllBounds()` helper is a
  static over USER32 with no seam, and with one attached display it is byte-identical to
  `[PrimaryBounds()]` (`Effects/PrimaryDisplayPlacement.cs:35-54`), so the revert it was written to
  catch would have PASSED. The other ten route through the injected seam and would have reddened.

### Two findings the review sharpened, which supersede §1's wording

- **Lock Card (§1 row 8) is a LIVE PARITY DEFECT, not a neutral difference.** Upstream has no
  `DualMonitorEnabled` gate there at all, so place-on-every **is** parity: a user of the shipping app
  today gets every monitor covered and this port covers one. §1 called it "correctly do not change";
  that is right about **scope** and understates the finding. The scope fact does not soften the
  defect. Authoritative wording is **D313**.
- **Mandatory Video (§1 row 9) would be a DIVERGENCE, not a regression fix.** The `#389` rationale
  behind upstream's 3-plus-monitor primary-only default exists because each secondary is its own full
  decode pass; **this port's one-decoder fan-out removes that cost**, and the gate is LibVLC-path
  only — the MediaElement fallback has none (`VideoService.cs:2514-2521`). So all-monitors video is a
  deliberate divergence the owner has not confirmed (board line 250), not a repair. Authoritative
  wording is **D318**.

### §7's open question is answered and moot

Spiral would have been **in**, for exactly the reason §7 gave. Nothing was spent on it.

### Preconditions for a future lane to resume

1. A second physical display attached and enumerated (`client/port.txt:134` names `DISPLAY3`).
2. A File Scope that either says "the four overlay consumers only" or deliberately carries `Video/`,
   `Input/`, `Pointer/` and `Glyph/` (D319).
3. The twelve existing presenter test call sites inside scope, **or** the additive seam plus a guard
   that can detect an unwired `Product()` — which needs the second display to exist (D322).
4. Board line 250 answered by the owner before video is touched at all (D318).
