# SP-139 — record

**Verdict: DEFERRED ON HARDWARE. Design complete and approved; archaeology landed; no product code
and no test written.**

Base `2508b39c4`. Branch `worktree-agent-a765c43b5ebde2a96`. Owner decision 2026-08-22: a second
monitor is not available, so the implementation was not attempted and the evidence was written down
instead. `client/src/**` and `client/tests/**` are byte-identical to base.

---

## 1. What was produced

| Artefact | What it carries |
|---|---|
| `client/docs/wpf-surface-reachability.md` — new `## SP-139` section, **D311–D322** | The durable archaeology. Twelve divergence rows, every one bound to an upstream line, plus a "does NOT establish" section. **44 insertions, 0 deletions** — purely additive, no line-ending churn. |
| `spine-tasks/SP-139-per-monitor-placement/plan.md` | The full design, the eleven guards it would have carried, the measured baseline, and (§9) the deferral verdict. |
| `spine-tasks/SP-139-per-monitor-placement/record.md` | This file. |
| `spine-tasks/SP-139-per-monitor-placement/floor-delta.json` | `0` / `0`. See §7 — this is a deliberate deviation from one instruction, stated rather than silent. |

---

## 2. Per-effect upstream evidence — all nine consumers

Full text and citations are in D311–D319. The summary, with the two that invert the obvious reading
first:

| Consumer | Upstream | Verdict |
|---|---|---|
| **Flash Images** `Effects/FlashSurfacePresenter.cs:163` | **ONE RANDOM monitor PER IMAGE.** `FlashService.cs:2187-2203` ends `candidates[_random.Next(candidates.Count)]`; called per image at `:493` and `:687`; each window spawns on its own image's monitor (`:1110-1140`) | **Place-on-every would be a REGRESSION.** D312 |
| **Lock Card** `Effects/LockCardEffect.cs:579` | **EVERY screen, UNCONDITIONALLY** — no `DualMonitorEnabled` gate anywhere in the file (`LockCardWindow.xaml.cs:1550`); one keyboard owner (`:1561`), the rest read-only mirrors; `#618` promotes a card so the lock stays solvable (`:1592-1600`) | **The port's primary-only card is a LIVE PARITY DEFECT**, not a neutral difference: place-on-every *is* parity here. Not fixable in this File Scope (needs `Input/**` mirroring). D313 |
| **Subliminals** `:96` | Every screen when dual is on (`SubliminalService.cs:628-630`, `:649-670`) | All-displays. Reachable from `Effects/` + `Overlay/`. D314 |
| **Pink Filter** `:102` | Every resolved screen (`OverlayService.cs:1149-1157`) | All-displays. Reachable. D314 |
| **Spiral** `:133` | Every resolved screen, frames decoded **once** and shared (`OverlayService.cs:1347-1364`, `:1408-1416`) | All-displays. Reachable, with the frame-size constraint in D314. |
| **Bubble Pop** `:157` | **ONE random screen PER BUBBLE** (`BubbleService.cs:877-885`, `:925-935`) | Flash's shape, not Pink Filter's. Different capability. D315 |
| **Bubble Count** `:849` | Every screen, but **only the primary owns input**; the rest are muted mirrors (`BubbleCountWindow.xaml.cs:332-385`) | The port's card **is** upstream's primary window — correct for the part that exists. The mirror is `Input/**`. D316 |
| **Bouncing Text** `:136` | Every screen gets a window, but **ONE logo roams their UNION** (`BouncingTextService.cs:316-329`, `:332-343`) | Not a placement problem — a `Glyph/**` rendering one. D317 |
| **Mandatory Video** `:515` | Primary always; secondaries gated by `ShouldFillSecondaryMonitors` on the LibVLC path only (`VideoService.cs:2040-2046`, `:2015`, `:2492`); the MediaElement fallback has **no** such gate (`:2514-2521`) | **Not a lane's to choose**: board line 250 is an open owner question, and the `#389` rationale does not transfer to a one-decoder fan-out, so all-monitors would be a *divergence*, not a regression fix. D318 |

**Score: 3 all-displays and reachable, 1 all-displays and a parity defect out of reach, 2 rolled-per-item, 2 correct-as-they-stand, 1 owner question.**

**And the finding that outranks all nine (D311):** upstream's per-monitor behaviour is a **user
setting** — `DualMonitorEnabled` (default true) plus five `<Effect>TargetMonitor` sentinels resolved
by `App.ScreenResolver.cs:30-71`. The port has neither the setting nor any control, so it does not
merely place on one monitor: **it has no way for a user to express the choice at all.**

---

## 3. Partial failure — the decision, and why it is honest

Recorded in full as D320. In short:

- **Screen outcome = upstream's.** A refusing display never takes the others down
  (`OverlayService.cs:1153-1157` returns `null` and the loop continues).
- **Reported state**, written to `OverlaySurfaceSet.LastPresent` (`Effects/OverlaySurfaceSet.cs:98`,
  surfaced to panels as each presenter's `LastPlacement`):
  `asked == 0` → `RecordNoDisplay()`; `covered == 0` → the verbatim refusal;
  `0 < covered < asked` → **`Degraded`** carrying the refusing display's own detail;
  `covered == asked` → the OS's own `Available` left **verbatim**.
- **Why honest:** `Available` that is true on one monitor and false on another is the fake-available
  shape this port bans by name. `Capabilities/CapabilityState.cs:65` already means exactly this case.
  Upstream cannot say it — `PinkShowing` is a boolean true when any window is up
  (`OverlayService.cs:163`) plus a count in the log — so this is a tightening, and D320 says so.
- **Two measured cost findings for whoever implements it:** `Views/**` needs **no edit** (every panel
  switch already has a `Degraded` arm), and the row dots **will not darken**, because they read
  `WorkIsRunning`, a screen fact, not the engage state.

---

## 4. Evidence class actually reached

**None.** Not `presentation-verified`, not `draw-verified`, not even a headless frame. Nothing was
rendered, composited, placed, focused, animated or observed. The only executions were a build and
two floor runs over unmodified product code.

**The hardware fact, dated (D322):** `[System.Windows.Forms.Screen]::AllScreens`, run in this lane
session on 2026-08-22, reports **exactly one display** — `\\.\DISPLAY1`, primary, bounds
`1646x1029`. `client/port.txt:134` names `DISPLAY3` as the headed-evidence monitor and it is **not
attached**.

**The strongest reason this had to wait, and it is about the guards rather than the fixture.** The
display seam must grow **additively**, because twelve existing test call sites construct the four
overlay presenters with a single-display `Func<OverlayBounds?>` and none of them was inside File
Scope. An optional every-display source therefore defaults to the single-display fallback — and **a
`Product()` factory that forgot to pass the every-display source would leave all eleven designed
guards green. On a one-monitor machine no test can tell the difference.** The packet could have
shipped completely wired to nothing and been undetectable here.

A second, concrete instance of the same limitation: **guard 1 could not have been watched red at
all.** An `OverlayDisplays.AllBounds()` helper is a static over USER32 with no seam, and with one
attached display it is byte-identical to `[PrimaryBounds()]` (`Effects/PrimaryDisplayPlacement.cs:35-54`),
so the revert it was supposed to catch would have PASSED. The other ten route through the injected
seam and would have reddened.

---

## 5. Measured baseline, and a correction to the packet's contention briefing

Taken at `2508b39c4` **before any edit**, and unchanged afterwards because no product or test file
was touched.

- Build: **0 warnings / 0 errors** across 4 projects, forced non-incremental (`check-warnings.mjs`).
- `CcpClient.Tests`: **total 2616**, passed 2600, **failed 14**.
- `CcpClient.HeadlessTests`: **total 152**, passed 152, failed 0.
- Both totals **match the pin exactly** (2616 / 152). Declared delta is 0 / 0, so pin + delta = the
  observed total on both projects. `client/tests/floor/floor.json` was never opened.

**The packet's briefing named the wrong class.** It said three `PointerCoexistenceTests` facts are
red for a proven environmental cause. At this base **zero `PointerCoexistenceTests` are red.** All 14
failures are `GlyphCapabilityTests`, sharing one symptom (`glyph-nothing-presented`, plus z-order and
hit-test reads returning `False`) — the same *environmental* class arriving on the per-pixel
layered-window capability instead of the pointer one. The full set, so the next lane compares against
a real baseline rather than a description:

```
CcpClient.Tests.GlyphCapabilityTests.AMOVEIsOneCall_ItEarnsAvailableFromGetWindowRect_AndTheContentSurvivesIt
CcpClient.Tests.GlyphCapabilityTests.AMismatchedFrameIsRefusedRatherThanStretched
CcpClient.Tests.GlyphCapabilityTests.AMoveThatWouldRESIZEIsRefused_BecauseTheLayeredSurfaceISTheFrame
CcpClient.Tests.GlyphCapabilityTests.ANDTHECAPABILITYNAMESTHEMODE_OnBothEntryPoints
CcpClient.Tests.GlyphCapabilityTests.ANINKLESSFRAMEIsREFUSED_BecauseNothingCouldTellItFromAGhost
CcpClient.Tests.GlyphCapabilityTests.AndTheMOVEsAvailableSaysExactlyWhatItDidNotReask
CcpClient.Tests.GlyphCapabilityTests.PaintReplacesTheContent_AndTheOSsCopyREALLYCHANGED
CcpClient.Tests.GlyphCapabilityTests.PresentEarnsAvailableONLYWhereTheMachineHasADesktop
CcpClient.Tests.GlyphCapabilityTests.PresentNAMESWhatItDoesNotClaim_TheTransparentPixelAndTheHumanEye
CcpClient.Tests.GlyphCapabilityTests.THEUNIFORMALPHAREFUSALISSTAGEDOnARealSurface_NotClassedAsUnreachable
CcpClient.Tests.GlyphCapabilityTests.TheExtendedStyleReadBackCarriesEveryBitThatWasWritten
CcpClient.Tests.GlyphCapabilityTests.TheOSsOwnZOrderPutsTheSurfaceAboveEveryOrdinaryWindow
CcpClient.Tests.GlyphCapabilityTests.TheWindowManagerRoutesThePointPASTIt_AndTOItWhenMomentarilyMadeOpaque
CcpClient.Tests.GlyphCapabilityTests.WithdrawTakesItOffTheScreenAndOutOfTheHitTest_AndKeepsTheComposite
```

Nothing was chased, nothing was added to `allowedSkips`, and no assertion was weakened. Worth
flagging: `GlyphCapabilityTests` is the capability behind Bouncing Text (D317), so a lane that later
takes that row will meet this red first.

---

## 6. What remains of board line 119, and of line 250

**Line 119 is untouched in every one of its terms.** One decoder presenting the same frame identity
on Windows, Linux X11 and Linux Wayland; negative X/Y; monitors above and below; vertical stacks;
gaps; mixed scaling and resolution; portrait, landscape and flipped orientation; hot-plug; rotation;
rearrangement. **Not one of these was exercised, and this packet closes none of it.**

**Line 250 is unanswered.** D318 records only what would make an all-monitors video policy a
deliberate divergence rather than a regression fix, so the owner has the distinction when they
answer.

**Hot-plug and rotation** are recorded, not fixed (D321), including the exact post-change behaviour a
future lane would inherit.

**Linux** is untouched: `OverlayDisplays.Enumerate()` still returns `[]` off Windows by design, and
nothing was guessed or fabricated.

---

## 7. Deviations, discrepancies and blockers, stated

1. **`floor-delta.json` was written `0` / `0`, contrary to one coordinator instruction.** The
   instruction's reason — *"declaring a delta for tests you did not write is a false declaration"* —
   is right about a nonzero delta and does not apply to zero: `0` / `0` is exactly true, and the
   standing rule says *"omitting the file is not the same as declaring zero"*. Measured fact so the
   choice is not guesswork: `FloorWrapperGuardTests` checks the packet's `floorDelta` **row in
   `PROMPT.md`** (`:261`, `:281-290`), not the file's presence on disk, so the guard is satisfied
   either way and an accurate zero costs the orchestrator's summation nothing.
2. **`OverlaySurfaceSet.LastPresent` is at `:98`, not `:93`.** `:93` is `SurfacesShown`. The
   coordinator's line number was off by one property; the substantive point (use the real name,
   surfaced as `LastPlacement`) was right and is applied throughout.
3. **The packet's "nine call sites into a container that already holds many" was half stale.** Only
   four go through `OverlaySurfaceSet`; five are on `Video/`, `Input/` ×2, `Pointer/` and `Glyph/`,
   none of which was in File Scope. D319 records it so a future packet scopes deliberately.
4. **The packet's contention briefing named the wrong test class.** See §5.
5. **No blocker was hit in the work that was done.** The one blocker is hardware, and it is the
   reason the rest was not attempted.

---

## 8. What this record does NOT prove

Nothing about a screen. No second monitor took anything; no pixel was composited; no geometry, DPI
scaling, occlusion or z-order was observed across a monitor seam; no interaction, focus, audio,
window behaviour or animation was exercised; no Linux session was run; no headed capture was taken
and no headless frame was produced. Every claim above is either a reading of source with a cited
line, or the output of a build and a test run over **unmodified** product code.
