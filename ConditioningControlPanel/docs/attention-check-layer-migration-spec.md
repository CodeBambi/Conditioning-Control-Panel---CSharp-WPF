# Attention-Check → Compositor Layer migration spec

**Status:** READY TO EXECUTE (fully investigated 2026-07-05; not yet implemented).
**Owner claim:** see `avalonia-migration-task-board.md` Active Claims Ledger.
**Goal alignment:** this is the **last live window-based passive effect** in the Avalonia
head. Migrating it completes the UCE window-migration lane (`skia-rebuild-goal.md`:
"every visual effect renders through the Unified Compositor Engine").

> **Why a spec instead of a direct commit:** the co-agent is live-committing to the shared
> `feat/crossplatform` working tree; this migration touches two shared files
> (`CompositorLayers.cs`, `LayerVerification.cs`). Execute in ONE disciplined burst at a
> low-collision window (co-agent working tree clean of code, no commit in the last few min),
> re-running the `git status --short` + `git log --oneline` guard immediately before the
> single commit, staging explicit paths only.

## 1. Confirmed classification — PASSIVE, correct to migrate

`AvaloniaAttentionCheckService.Fire()` builds a `Window` with `IsHitTestVisible = false`,
`Focusable = false`, `ShowActivated = false` (`:217-219`). The target is resolved by
**webcam gaze dwell** (`_webcam.OnGazeMove += HandleGazeMove`, dwell accrues in `OnTick`),
never by a click. It is a "just draws" overlay → compositor layer per the UCE doctrine.
(Contrast: the `SpawnTarget` "CLICK ME" `FloatingText` in `AvaloniaVideoService` is
INTERACTIVE — clicked — and correctly stays windowed.)

## 2. Visual contract (from `Controls/AttentionCheckControl.axaml`)

Intrinsic **84×84 DIP**, centered composition (draw in PHYSICAL px = DIP × screen.Scaling):

| Element | Geometry (DIP) | Paint |
|---|---|---|
| Background ring | ellipse Ø84, stroke 3 | `#33FFFFFF` (translucent white) |
| Foreground progress ring | ellipse Ø84, stroke 4, round cap | `#FFFF69B4` (hot pink); arc from **top (−90°) clockwise**, sweep = `progress × 360°` |
| Soft glow | filled Ø60 | `#50FF69B4` |
| Center dot | filled Ø44 | radial gradient: `#FFFFB6E1` @0 → `#FFFF69B4` @0.6 → `#FFC71585` @1 |

**Pulse:** only the **foreground ring** scales `1.0 ↔ 1.18` about center, **840 ms**,
`SineEaseInOut`, infinite (`AttentionCheckControl.StartPulse`). Drive this intrinsically in
`Render` from elapsed time — no external start/stop needed while the layer is active.

**Progress mechanic** (WPF `SetProgress`): clockwise fill via stroke-dash. In Skia use
`canvas.DrawArc(oval, startAngle:-90, sweepAngle: progress×360, useCenter:false, ringPaint)`
with `StrokeCap.Round`. Clamp progress to [0,1].

## 3. Service driving contract to PRESERVE (`AvaloniaAttentionCheckService`)

Everything below stays; only the **Window/control** is swapped for a layer handle:

- **Fire()** (`:154-265`): scope/webcam/active-guard checks unchanged. Position: random point
  in the **primary screen** WorkingArea (margin 120 DIP, size 84 DIP → physical via scaling);
  `_activeBounds` = target rect + 30 DIP slack (gaze hit-test in virtual px). Replace
  `new AttentionCheckControl()` + `new Window{…}` + `Show()` + `StartPulse()` +
  `SetProgress(0)` with `_layer.Show(targetPhysicalRect)` (layer auto-pulses, progress 0).
  Keep gaze subscribe, tick timer (33 ms), `TryPlayPing()`.
- **OnTick** (`:272-309`): dwell accrual unchanged; replace `_activeControl?.SetProgress(x)`
  with `_layer.SetProgress(x)`. Pass at dwell ≥ 1000 ms → `ResolveActive(true)`; grace
  timeout → `ResolveActive(false)`.
- **ResolveActive** (`:311-378`): replace `StopPulse()` + 180 ms window opacity fade + `Close()`
  with `_layer.Hide(fadeMs:180)` (layer ramps its own alpha to 0 over 180 ms then deactivates).
  XP/penalty/`ScheduleNext` unchanged.
- Drop `_activeWindow`/`_activeControl` fields; keep an `_activeBounds`/`_active` flag so the
  `_activeWindow != null` "already active" guard (`:182`) becomes `_layerActive`.

## 4. New layer — `Compositor/Layers/AttentionCheckLayer.cs`

Mirror `ChaosWaveTimerLayer`/`ChaosFxLayer` discipline:
- `: BaseLayer`; `ZIndex => CompositorLayers.AttentionCheck`; all mutable state under one
  `_sync` lock; **Skia paints/shaders built ONCE** in ctor (bg-ring stroke, fg-ring stroke,
  glow fill, dot radial-gradient shader on a unit oval scaled per-frame), zero per-frame alloc.
- API (all lock-guarded, called from UI thread but render reads under lock):
  `Show(PixelRect targetPhysical)` → set center+radius, `progress=0`, `_shownAt=now`, activate;
  `SetProgress(double)` → clamp+store; `Hide(int fadeMs=180)` → start alpha ramp then deactivate.
- `IsActive` gate: active while shown OR fading.
- `Render(SKCanvas, PixelRect bounds, ScreenInfo?, TimeSpan)`: **gate** — draw only if the
  target center ∈ `bounds` (naturally restricts to the containing/primary monitor; matches
  `ChaosWaveTimerLayer`'s primary-monitor gate). Compute pulse scale from
  `(now-_shownAt)` sine (840 ms). Draw order: glow → dot → bg ring → fg arc (scaled by pulse).
  Apply fade alpha to all paints during the Hide ramp.

## 5. Registration — SELF-REGISTER from the service (AVOID `AvaloniaHeadStubs`)

Do **not** touch `AvaloniaHeadStubs.cs` (co-agent chaos file). Inject the compositor engine
into `AvaloniaAttentionCheckService` (constructor param, like `AvaloniaVideoService` receives
`compositor`) and register the layer once on first `Fire()` (or in ctor): `engine.RegisterLayer(_layer)`.
Update the DI registration in `ServiceCollectionExtensions.cs:230` to pass the engine.
If the engine is null (headless/fallback), keep the old Window path OR no-op (dormant feature —
acceptable to skip when no compositor, but prefer graceful layer-absent → no target).

## 6. Z-order — `CompositorLayers.AttentionCheck = 160`

Above `ChaosWaveTimer` (155) so the "look here" target is never occluded (WPF used a Topmost
window). Add the constant to `CompositorLayers.cs` (additive one-liner; shared file — see burst note).

## 7. Verification — `LayerVerification.cs` Stage 4l

Add a stage after 4k: resolve `AttentionCheckLayer`, `Show()` a synthetic target rect on the
primary screen, `SetProgress(0.5)`, assert DIFFER, `Hide()`, assert clean teardown. Mirror the
Stage 4j/4k structure.

## 8. Gate plan (per commit, UCE recipe)

`CCP.Desktop.slnf` 0 err · WPF 0 err · Core floor 542 · `--verify-layers` exit 0 (new Stage 4l
PASS) · `--smoke-test` Findings 5 / 44 tabs. One cohesive commit:
`feat(av): migrate attention-check gaze target to compositor layer (UCE)`.

## 9. Files touched (collision surface)

| File | Change | Collision |
|---|---|---|
| `Compositor/Layers/AttentionCheckLayer.cs` | NEW | none |
| `Compositor/CompositorLayers.cs` | +1 z-constant | shared — additive, burst-guard |
| `Services/AttentionCheck/AvaloniaAttentionCheckService.cs` | rewire Window→layer + engine ctor param | attention-check lane (not co-agent's current) |
| `ServiceCollectionExtensions.cs:230` | pass engine to service | shared DI — small, burst-guard |
| `tests/…Smoke/LayerVerification.cs` | +Stage 4l | shared test — additive, burst-guard |

After landing: mark UCE-coverage-audit §D item #4 DONE; flip the verdict's "only live
remaining window-based passive effect" note to "none — window-migration lane complete."
