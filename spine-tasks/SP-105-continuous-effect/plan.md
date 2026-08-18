# SP-105 — plan checkpoint

Branch `lane/SP-105-continuous-effect`, base `252b8509`, worktree
`.claude/worktrees/agent-a01e12274d69597f3`. Nothing product-side is edited yet.

---

## 1. Which continuous effect: **Pink Filter**

WPF's fifth EFFECTS rack row, key `"pinkfilter"`, caption `Pink Filter`
(`Views/Tabs/StudioTabView.xaml.cs:493-494`). Chosen over Spiral Overlay, its neighbour in the
same continuous pair, for one reason that is about the QUESTION and not about the feature:

- Spiral Overlay's payload is an animated GIF or a video played into a `MediaElement`
  (`Services/Notifications/OverlayService.cs:1275-1295`), plus an asset library, a randomiser and
  a per-frame animation loop. Every one of those is a subsystem this port does not have, so the
  module would spend its whole body on content and almost none of it on the spine.
- Pink Filter's payload is one full-screen rectangle of one colour at one opacity
  (`OverlayService.cs:1168-1186`). `OverlayFrame.Solid` already exists. Almost the entire module
  is therefore spine — which is exactly what this packet is trying to measure.

Both are driven by the same mechanism and the same two lines of quick-toggle
(`MainWindow/MainWindow.Presets.cs:1254-1255`), so the answer generalises to the other one.

## 2. WPF semantics, with citations

| Fact | Where |
|---|---|
| **No timer, no tick, no interval.** The quick-toggle flips the flag and calls `RefreshOverlays()`. No `Start`, no `Stop`, no `if (running)` at the call site — where flash/video/subliminal all have one | `MainWindow/MainWindow.Presets.cs:1255` (and `:1254` for spiral, `:1250` for the paced contrast) |
| The engine start calls `App.Overlay.Start()` **unconditionally** (not behind the pink flag) and the SERVICE then reads the flag | `MainWindow/MainWindow.StartStop.cs:192-193`, `OverlayService.cs:362-373` |
| `RefreshOverlays()` returns immediately when the service is not running — this, not the call site, is what makes a stopped engine's toggle invisible | `OverlayService.cs:419-421` |
| Reconcile: flag on and not showing -> start; flag off -> stop; flag on and showing -> update opacity | `OverlayService.cs:423-437` |
| The engine stop tears it down | `MainWindow/MainWindow.StartStop.cs:338` -> `OverlayService.cs:398-409` (`StopPinkFilter`) |
| The layer is one window per resolved screen, full screen bounds, `WS_EX_TOOLWINDOW|NOACTIVATE|TRANSPARENT|LAYERED`, `Topmost`, `ShowActivated=false`, `IsHitTestVisible=false` | `OverlayService.cs:1168-1215` |
| Colour: user hex -> mod retint -> hot pink `(255,105,180)` | `OverlayService.cs:679-686` |
| Alpha: **linear**, `(byte)(opacity/100.0 * 255)`, its own comment says "Linear opacity (no exponential curve)" | `OverlayService.cs:1174-1181` |
| Dials: `PinkFilterEnabled` default **false**; `PinkFilterOpacity` default **10**, `Math.Clamp(value, 0, 50)`; `PinkFilterColor` default `""` | `CCP.Core/Models/AppSettings.cs:3726`, `:3733-3738`, `:3749-3754` |
| A long-lived overlay loses the topmost band and must reclaim it: an unconditional kick every **5 s** (10 x 500 ms), plus a conditional recovery pass every 500 ms | `OverlayService.cs:666-673`, `:633-663` |

**What is NOT ported and will be recorded, not stubbed:** timed/sustained holds and the Deeper
opacity ramp (`OverlayService.cs:900-965`), `PulseOverlays` (`:461-500`), the recreate-after-3s-of-
loss fallback (`:2597-2622`), multi-monitor (`:1149-1157` — the same D66 limit the other two
modules already carry), the per-effect monitor target, the mod retint (`App.Mods` does not exist
in the port), and the 500 ms *conditional* z-order recovery pass — the port's `Reassert()`
deliberately confirms nothing and `IOverlayPresence` has no z-order query to condition on, so only
the unconditional 5 s kick has an honest counterpart.

## 3. Prediction: does `ISessionEffect` fit?

**Prediction, written before the code: `ISessionEffect` FITS UNCHANGED. `PacedSessionEffect` does
NOT, and must not be bent to. `EffectDotState`'s three states survive, but only because the
continuous module derives `Live` from a DIFFERENT AUTHORITY, and that authority does not exist in
the paced base.**

Reasoning, member by member. `ISessionEffect` is `Id`, `Title`, `Enabled`, `Dot`, `Completion`,
`Changed`, `SetEnabled`, `Arm() -> CapabilityState`, `Disarm()`. Not one of those names a clock, an
interval, a firing or a schedule. The spine says "take the session / give it back / say what you
can honestly claim / tell me when that moves" — which is exactly what a continuous module needs.
So I predict the spine is a spine.

`PacedSessionEffect<TFiring>` is where I predict the fight, in four specific places:

1. **`Arm()` is `ScheduleNext()` + `Ready(scheduled)`.** A continuous module has nothing to
   schedule; its arm IS its draw. The paced base's `Available` detail even prints the interval.
2. **`ScheduleArmed` is the dot's liveness input.** There is no pending one-shot to read.
3. **`Compose`/`Stamp`/`Deliver`/`Fire` are the whole body** and none of them exists for a module
   that never fires.
4. **`NextInterval()` is abstract.** A continuous module implementing it at all is the fake timer
   this packet exists to catch.

What I predict IS shared, and will therefore be extracted into a third class (working name
`OwnedSessionEffect`) that both `PacedSessionEffect` and the new `ContinuousSessionEffect` derive
from: the owned generation and its parked completion, the idempotent first-arm, `_armed` under the
gate, `Disarm`'s order (clear -> undo the visible half -> cancel the generation -> raise), the
`Changed` signal boundary, the `Ready(...)` narrowing seat, and the DOT's skeleton — `Off` when the
dial is off, otherwise `Live` iff armed AND the generation is live AND *the module's own work is
really running*. That last clause becomes an abstract predicate; the paced base answers it with
`ScheduleArmed`, the continuous one with "the surface is confirmed up".

**The dot, predicted precisely.** I do NOT predict a fourth enum state. I predict that the three
states keep describing reality and that the DERIVATION had to move, because:

- For a paced module `Live` is a claim about the CLOCK. Subliminals over an empty pool is `Live`
  and correct: a firing really is scheduled, it will simply show nothing.
- For a continuous module there is no clock between "armed" and "on screen". So `Live` can only be
  a claim about the SCREEN, and a Pink Filter whose overlay was refused — Linux, no display, a
  failed present — has literally nothing running. Showing `Live` there is the exact lie the packet
  forbids. It must read `Armed`, and `Arm()` must return the overlay's own typed refusal verbatim.

I therefore predict one genuinely new hazard that no paced module can produce: **a continuous
module's arm result depends on a UI thread**, because its work is a native window. The paced
modules dodge this — scheduling needs no UI, and the draw is a later posted projection that is
skipped when unbound. I predict `Arm()` will have to consult `Signal.IsBound` and refuse honestly
when there is no UI to place a surface on, and that this is a real state rather than a papering-over.

**Second prediction, about opacity 0.** `AppSettings.cs:3737` clamps `PinkFilterOpacity` to
`[0, 50]`, so zero is reachable, and WPF at zero puts a full-screen layered window on the desktop
that composites nothing. `OverlaySurfaceRequest` refuses to be constructed at opacity 0 by design
("Opacity zero is the exact failure this capability exists to make impossible"). I predict this is
a real, recordable divergence with a `Degraded` arm and an `Armed` dot — not an exception, and not
a silent clamp up to something visible.

## 4. Shape to build

**Product (all inside `Effects/**` and `Session/**`):**

- `Session/OwnedSessionEffect.cs` — the extraction above. `PacedSessionEffect` becomes a subclass
  with its schedule intact and NO behaviour change.
- `Session/ContinuousSessionEffect.cs` — arm = engage, disarm = withdraw, dot = "is it up".
  No clock, no interval, no firing type.
- `Session/PinkFilterPresetDocument.cs` — `session_pinkfilter.json`, on the SP-101/D71 precedent
  (`Persistence/**` is out of scope again, and one document per module is the shape that packet
  argued for).
- `Effects/PinkFilterEffect.cs`, `Effects/PinkFilterSurfacePresenter.cs`,
  `Effects/PinkFilterTint.cs` (the colour law: hex parse -> hot-pink default, and the linear alpha).
- `Effects/OverlaySurfaceSet.cs` — ONE change: `Place`'s lifetime becomes nullable, meaning "hold
  until retired". Both landed callers keep passing a lifetime and are untouched behaviourally.
- `Session/SessionParticipant.cs` — composes the third module, third in the engine list (WPF's own
  arm order: flash `:178`, subliminal `:186`, overlay `:193`).
- `Session/EffectReasonCodes.cs` — the new codes.

**Views:**

- Rack rows for **Subliminals** (closes D72) and **Pink Filter**, in WPF's rack order — Flash
  Images, Subliminals, Spiral Overlay, Pink Filter (`StudioTabView.xaml.cs:483-493`, with
  Mandatory Video absent because it is not ported). Each new row: left-click opens its panel,
  right-click quick-toggles through `SessionEngine.QuickToggle`, dot bound to the effect's own
  `Dot`. Spiral Overlay keeps neither, which is still D5/D6 and still correct.
- Module panels for both, with only dials the running effect really reads.
- The stale duplicate `<summary>` at `Views/MainWindow.axaml.cs:207-211` deleted.

**Tests:** `ContinuousEffectSpineTests` (the spine answer), `PinkFilterEffectTests` (dials, tint,
alpha, dot, arm outcomes), `PinkFilterSurfacePresenterTests` (engage/withdraw/refusal/cadence),
plus headless rack facts for the two new rows. Floor delta declared in
`spine-tasks/SP-105-continuous-effect/floor-delta.json`; `client/tests/floor/floor.json` is never
opened.

## 5. Stop conditions I am holding myself to

- If `ISessionEffect` turns out to assume paced firing, I stop and report the split instead of
  implementing `NextInterval()` with a lie in it.
- If the dot cannot be made truthful for a module that is simply on, the row does not ship.
- No landed fact of `FlashImagesEffect` or `SubliminalsEffect` is edited. If one has to move, that
  is a finding.
