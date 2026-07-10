---
name: unified-compositor-engine
description: "Work on the Unified Compositor Engine (UCE): CCP's single Skia render surface that draws video, flash images, subliminals, bouncing text, spiral, brain drain, pink tint, and bubbles as z-ordered layers inside one topmost window per monitor with PER-REGION click-through (team review 2026-07-09: only theme-color-filter/spiral regions pass input, every other active layer captures over its painted region; see overlay-clickthrough). The engine ALREADY EXISTS and is adopted; UCE scope = ambient/session conditioning ONLY — DTRH/Chaos game mode went web-only in a dedicated WebView window (owner ruling 2026-07-10, board row #6; run-only chaos layers are decommission candidates with ambient carve-outs). Use this skill whenever you touch anything under CCP.Avalonia/Compositor/, any *Layer class, video rendering, overlay z-order, or overlay flicker/lag. Also use it when someone asks why an overlay effect renders wrong, out of order, or not at all in the Avalonia head."
---

# unified-compositor-engine

## Current reality (read this first; older docs describe a world that no longer exists)

The compositor is **built and wired in**. Do not scaffold it again.

What already renders through it: flash, subliminal, bouncing text, pink tint, spiral, brain drain, bubbles, and mandatory video (layer exists). The effect services register layers instead of creating windows:

- `AvaloniaFlashService` registers `FlashLayer`
- `AvaloniaSubliminalService` registers `SubliminalLayer`
- `AvaloniaBouncingTextService` registers `BouncingTextLayer`
- `AvaloniaOverlayService` registers `PinkTintLayer`, `SpiralLayer`, `BrainDrainLayer` (it owns no windows anymore)
- `AvaloniaBubbleService` and `AvaloniaVideoService` also take the engine

What still uses separate windows: ~23 Chaos overlay/window classes, AvatarTube, secondary-monitor video (`VideoOverlayWindow` nested in `AvaloniaVideoService.cs` around line 1156), dialogs, and secondary windows (~101 `Window` subclasses in `CCP.Avalonia` total). That is intentional for now.

**Status tracker:** `ConditioningControlPanel/docs/unified-compositor-engine-plan.md` is the ONLY UCE doc — it holds both the status tracker and the loop driver (one unchecked task per iteration, in phase order); the former `unified-compositor-engine-goal.md` was folded into it on 2026-07-10. Read it before doing UCE work, and check the git log since its last update; the doc can lag the code.

## Architecture map (verified paths)

| Piece | Path | Notes |
|---|---|---|
| Engine | `ConditioningControlPanel/CCP.Avalonia/Compositor/CompositorEngine.cs` | DI singleton (registered in `ServiceCollectionExtensions.cs`). One topmost transparent `CompositorWindow` per monitor, ~60Hz `DispatcherTimer` (16ms), renders `IAvaloniaLayer`s sorted by `ZIndex`. Window creation is deliberately STAGGERED (~250ms apart) |
| Window | `Compositor/CompositorWindow.axaml(.cs)` | Per-monitor, stays `WS_EX_TRANSPARENT\|LAYERED` (re-applied on Opened/Activated/WindowState change). Input is PER-REGION (team review 2026-07-09): the mouse hook swallows clicks inside the compositor's capture mask (union of non-ambient layer regions), passes ambient-only (color-filter/spiral) regions. See `overlay-clickthrough` skill |
| Draw op | `Compositor/CompositorControl.cs` | `CompositorDrawOp` custom draw operation (Skia lease) |
| Core seam | `ConditioningControlPanel/CCP.Core/Services/Compositor/ILayer.cs` | Portable: `ZIndex`, `IsActive`, `OnActivated()`, `OnDeactivated()` |
| Avalonia seam | `Compositor/IAvaloniaLayer.cs` | Adds `Update(TimeSpan)` and `Render(SKCanvas, PixelRect, TimeSpan)` |
| Z constants | `Compositor/CompositorLayers.cs` | Authoritative, overrides any legacy z-ordering |
| Layers | `Compositor/Layers/` | `BaseLayer`, `VideoLayer`, `MandatoryVideoLayer`, `FlashLayer`, `SubliminalLayer`, `BubbleLayer`, `BouncingTextLayer`, `BrainDrainLayer`, `SpiralLayer`, `PinkTintLayer`, `PlaceholderLayer` |

### Z-layer constants (from `CompositorLayers.cs`, verified)

```
Video=10  MandatoryVideo=15  LockCard=20  Flash=30  Subliminal=40
Bubbles=45  BouncingText=50  BrainDrain=55  Spiral=60  PinkTint=70
```

Lower renders first (behind). Note: `LockCard=20` is defined but no `LockCardLayer` exists yet; the lock card is still a window.

## Capture affinity (dual-surface split)

The engine maintains TWO surfaces per monitor, split by `IAvaloniaLayer.ExcludeFromCapture`:

- **Main surface** (`_windows`): all normal layers. It must NEVER get a `SetWindowDisplayAffinity` call — subliminals (and flash/spiral/pink tint) are visible in screenshots/streams BY DESIGN (WPF `SubliminalService` deliberately sets `WDA_NONE`). This is a never-regress guardrail.
- **Excluded surface** (`_excludedWindows`): layers with `ExcludeFromCapture => true` (today only `BrainDrainLayer`). Its `CompositorWindow` is constructed with `excludeFromCapture: true` and applies `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE /*0x11*/)` inside `ApplyNativeTransparency`, after the `SWP_FRAMECHANGED` flush, re-asserted on Opened/Activated/WindowState like the ex-styles. WPF parity: brain-drain windows are excluded "so we don't capture ourselves" — the exclusion is what breaks the blur's self-capture feedback loop and keeps the effect out of streams and the app's own OCR captures.

Lifecycle: the excluded surface is created lazily on the first tick an excluded layer is active (staggered, same v12 native-race rule), and torn down after ~500ms of excluded-idle. Inter-surface z caveat: two sibling topmost windows cannot interleave layers, so the excluded surface is shown LAST and sits above every main-surface layer (brain drain z55 renders above spiral z60/pink z70); WPF's inter-window z was show-order based too, so this is accepted and documented in `CompositorEngine`.

Adding a new must-not-capture layer = override `ExcludeFromCapture => true`, nothing else. Never move `SubliminalLayer` (or any capture-visible layer) to the excluded surface, and never "fix" a capture bug by excluding the main surface.

## Remaining work (the actual job)

Per `unified-compositor-engine-plan.md`, phases A-E: prove video renders, reach behavior parity, verify every layer, performance, then flip the default and delete legacy. The headline blockers:

1. **Video does not render through the UCE yet.** `VideoLayer`/`MandatoryVideoLayer` exist but the frame path is broken or unproven. The legacy `AvaloniaMultiMonitorVideoService` is the only working video path. See `references/video-pipeline.md` in this skill for the frame-delivery patterns and known allocation traps.
2. Audio volume/device/mute are missing on the UCE video path.
3. Attention checks are tied to the legacy `VideoOverlayWindow` and bypass the UCE.
4. Chaos overlays are not migrated to layers.

## Critical rules (each one is a scar; violating it reintroduces a fixed bug)

1. **Never delete or break `AvaloniaMultiMonitorVideoService` or the legacy `*Window` video path until Phase E**, and only after UCE video is proven by actually running the app. There are ~9 `IMultiMonitorVideoService` references to rehome first.
2. **Never modify the WPF head** for UCE work. It stays runnable as the behavior reference.
3. **Never call `SetWindowSubclass` on an Avalonia v12 HWND.** It races Avalonia's window-proc management and intermittently crashes with native `0xC0000005` (comment in `CompositorWindow.axaml.cs`). `WS_EX_TRANSPARENT` + `WS_EX_NOACTIVATE` already cover what the subclass did.
4. **Keep the staggered per-monitor window creation** in `CompositorEngine`. Creating several transparent topmost windows in one frame races the v12 Win32 backend (same `0xC0000005`). Do not "simplify" it into a plain loop.
5. **Draw persistent `SKImage`s, not `SKBitmap`s.** `SKCanvas.DrawBitmap(SKBitmap, ...)` recreates an `SKImage` every call (~480 MB/s of allocations measured in `VideoLayer`). Convert once, cache, draw the `SKImage`. `VideoLayer` was fixed in plan Phase D.1/D.2 (2026-07-04): triple-buffered pinned decoder buffers + long-lived zero-copy `SKImage.FromPixels` wrappers, presentation folded into the engine `Update` tick — see the protocol comment block in `VideoLayer.cs` before touching it.
6. **One invalidation per frame, owned by the engine.** `ICustomDrawOperation.Render` is not re-invoked without an explicit `InvalidateVisual` (Avalonia issue #12247); the engine's tick is the only invalidator. Layers never invalidate themselves. If moving to a render-thread model, the idiomatic v12 primitive is `CompositionCustomVisualHandler` (self-drives via `RequestNextFrameRendering`).
7. **Services own state; layers only render it.** A service tells its layer what to show; the layer holds no business logic. Note the engine is a nullable ctor dependency in effect services: without DI the layer is never created (or, in `AvaloniaOverlayService`'s case, created but never registered) and the effect silently renders nothing.
8. **Thread safety:** decoded frames arrive on background threads. Hand them over under a lock or queue; never touch `SKCanvas` off the render path.
9. **Z-order comes from `CompositorLayers` only.** No `Topmost` toggling, no `SetWindowPos` for layer ordering.
10. **Click-through is the `overlay-clickthrough` skill's domain.** The compositor window stays always-`WS_EX_TRANSPARENT|LAYERED`, but input is PER-REGION (team review 2026-07-09, polarity flipped from opt-in-capture to opt-out-ambient): the engine unions every non-ambient active layer's painted region into a per-frame **capture mask** (immutable snapshot), and the global mouse hook swallows clicks inside the mask and passes clicks over ambient-only (color-filter/spiral) or bare-desktop regions. Interactive layers (`FlashLayer.HitTest`, `BubbleLayer.HitTest`) still hit-test their geometry for on-click behavior.

## Verification

```bash
# from repo root
dotnet build ConditioningControlPanel/CCP.Desktop.slnf -c Debug     # 0 errors (check git status first; parallel WIP can break the tree)
dotnet test ConditioningControlPanel/tests/CCP.Core.Tests/CCP.Core.Tests.csproj
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj
```

- Watch `ccp-run.log` for `VideoLayer:` / `CompositorEngine` lines when diagnosing the video path.
- `--verify-spiral` (Debug builds only) validates spiral rendering; `--smoke-test` sweeps the whole UI; `--benchmark` / `--max-benchmark` measure startup, memory, and FPS.
- Perf reference points (from `docs/benchmark-optimized.json`): idle ~124 FPS, active session ~185 FPS, 3-minute max-intensity average ~178 FPS, startup ~2.0s, working set ~561MB at 10s. A UCE change that regresses these is a defect. Targets from `benchmark-baseline.json`: effect FPS floor 30, aim 60.
- Behavior parity means side-by-side with the WPF head, per feature. "Builds and looks right" is not done.

## Report template

After each work session report: phase/task from the plan doc, files created/modified/deleted, validation results (build/test/smoke/log evidence), behavioral differences vs WPF (should be none), and what remains. Then update the checkboxes and current-state table in `unified-compositor-engine-plan.md`. Commit as `feat(av): ...` (one task per commit).

## Constraints

- Do not broaden the webcam privacy contract: camera frames are never written to disk or sent over the network.
- Do not weaken deeper-enhancement validation (NaN, Infinity, UNC paths, control characters, bounds).
- Do not accept UNC or extended-length paths for `--play`/`--edit` CLI arguments.
- Keep `LibVLCSharp` packages until the plan says otherwise; `Microsoft.WindowsAppSDK` stays pinned with `ExcludeAssets="all"` (WebView2 NU1605 downgrade guard), never removed.

## Related skills

- `overlay-clickthrough` - ex-styles, hooks, hit-testing, topmost rules for the compositor window
- `avalonia-research` - verify any v12 rendering API online before using it
- `wpf-parity` - extracting the WPF behavior a layer must reproduce
- `port-audit` - the wider health sweep after UCE milestones
