# Unified Compositor Engine (UCE) — Plan & Goals

> Status: **in progress / not yet at parity.** This doc is the working plan to finish the UCE so
> the Avalonia head renders all video + overlays through one Skia compositor and the legacy
> multi-window / multi-monitor services can be deleted. It is the status tracker the design skill
> (`.pi/skills/unified-compositor-engine/SKILL.md`) is not.
>
> The ground-truth migration doc (`crossplatform-rebuild-plan.md` §1A) does **not** currently track
> the compositor — the only real status lives here and in the code.

## Goal (north star)

One `CompositorEngine` driving **one full-screen, click-through, topmost `CompositorWindow` per
monitor**, into which every visual effect renders as an `IAvaloniaLayer` on a single Skia surface:
video (regular + mandatory), flash, subliminal, bouncing text, bubbles, brain-drain, spiral, pink
tint, lock card, and (later) chaos overlays.

**Done means:**
1. Every overlay/video effect renders through the UCE with **1:1 behavioral parity** with the WPF head.
2. No effect creates its own `Window`; no per-service `Topmost` / `SetWindowPos` z-fighting.
3. The legacy `AvaloniaMultiMonitorVideoService` and per-overlay `*Window` classes are **deleted**.
4. It is **at least as fast and lighter** than the multi-window approach (the whole reason for the
   rewrite: bounded memory, no per-effect window/compositor overhead).

**Non-goals (for now):** replacing LibVLC as the decoder (Phase 3b in the skill — stays interim),
the unified audio mixer (skill Phase 5), Android.

## Current state

| Piece | State |
|---|---|
| `CompositorEngine` 60 Hz loop + per-monitor windows | ✅ working |
| `CompositorControl` + `ICustomDrawOperation` render-thread path | ✅ wired (engine invalidates each tick) |
| Click-through (`WS_EX_TRANSPARENT` + `WM_NCHITTEST` subclass) | ✅ working; `WS_EX_LAYERED` correctly removed |
| Spiral, pink tint, brain-drain layers | ✅ render (spiral full-screen fix landed) |
| Full-screen coverage (window uses `screen.Bounds`, taskbar incl.) | ✅ landed |
| **Mandatory / regular video layer** | ❌ **does not show** — under diagnosis (loggers just wired) |
| Video audio: volume / output-device / mute control | ❌ missing on UCE path (only `Mute` at start) |
| Mandatory-video attention checks / duration / safety timer / segment mode | ❌ bypassed — tied to legacy `VideoOverlayWindow`, not the layer |
| Legacy `AvaloniaMultiMonitorVideoService` | ⚠️ still the only *working* video path; **keep as fallback until UCE video is proven** |
| Flash / subliminal / bouncing-text / bubbles / lock-card on UCE | ⚠️ verify (per skill Phase 2) |
| Chaos overlays | ❌ not migrated (skill Phase 4) |

## Avalonia v12 idiomatic confirmation (researched, not from memory)

Confirmed against the official docs + Avalonia repo (Avalonia 12.x):

1. **Custom Skia rendering is `ICustomDrawOperation` + `ISkiaSharpApiLeaseFeature`.** Inside
   `Render(ImmediateDrawingContext)`: `context.TryGetFeature<ISkiaSharpApiLeaseFeature>()` →
   `using var lease = feature.Lease()` → `lease.SkCanvas`. This is exactly what `CompositorDrawOp`
   does — the current approach is correct. ([custom-rendering docs](https://docs.avaloniaui.net/docs/graphics-animation/custom-rendering))

2. **`Control.Render` runs on the UI thread; `InvalidateVisual()` is the documented way to request a
   redraw.** A UI-thread `DispatcherTimer` calling `InvalidateVisual()` each frame (current engine)
   is valid but couples the frame loop to the UI thread. There is a known issue where
   `ICustomDrawOperation.Render` isn't re-invoked without an explicit invalidate
   ([#12247](https://github.com/AvaloniaUI/Avalonia/issues/12247)) — our per-tick invalidate is the
   workaround.

3. **The more idiomatic continuous-render primitive is `CompositionCustomVisualHandler`.** Its
   `OnRender` runs **on the render thread**, self-drives via `sender.RequestNextFrameRendering()`,
   and receives UI→render data via `SendHandlerMessage()` / `OnMessage`. This decouples the
   compositor frame clock from the UI thread — the right long-term target for a 60 Hz video
   compositor. ([custom-rendering docs](https://docs.avaloniaui.net/docs/graphics-animation/custom-rendering), [examples](https://github.com/wieslawsoltes/CustomDrawingAvaloniaExamples))

4. **`SKCanvas.DrawBitmap(SKBitmap, …)` recreates an `SKImage` on every call** (the shim added after
   the old `DrawBitmap` was removed; Avalonia fixed the equivalent in
   [PR #18164](https://github.com/AvaloniaUI/Avalonia/pull/18164)). `VideoLayer.Render` currently
   does exactly this per frame → draw a **persistent `SKImage`** instead, and stop allocating a new
   `SKBitmap` per frame in `OnRenderTick`.

5. **Click-through transparency has no built-in Avalonia support — native P/Invoke is required**, and
   `WS_EX_LAYERED` + `UpdateLayeredWindow` is **incompatible with GPU rendering on Windows**. This
   validates `CompositorWindow.ApplyNativeTransparency` keeping `WS_EX_TRANSPARENT` and dropping
   `WS_EX_LAYERED`. Linux/X11 needs the **XShape** extension; Wayland is compositor-specific
   (`crossplatform-rebuild-plan.md` §870 already flags this). ([#11911](https://github.com/AvaloniaUI/Avalonia/discussions/11911), [#13827](https://github.com/AvaloniaUI/Avalonia/discussions/13827))

## Plan — phased, parity-first

Order matters: **prove UCE video → reach parity → flip default → delete legacy.** Do not delete the
fallback before the replacement is proven (deleting now = no working video).

### Phase A — Make UCE video render (unblock everything)
- [x] Wire loggers into `VideoLayer` / `MandatoryVideoLayer` (done) + `EncounteredError` + first-frame log.
- [x] Reproduce a mandatory video; read `VideoLayer:` log lines to bisect. **DONE 2026-07-04 via the new `--verify-video <path>` harness** (`VideoVerification.cs`, mirrors `--verify-spiral`; Program.cs sets `CCP_UCE_VIDEO=1` pre-DI). Bisect verdict: **no broken stage remains** — layer registers (Z=15), LibVLC vmem callbacks fire, first frame copies (1920x1080), engine composites, frames advance 49→70 across 700ms (live 30fps), exit 0. The "does not render" premise was stale (docs lagged the code; earlier logger-wiring fixes evidently resolved it and it was never re-tested).
- [x] Fix the identified root cause. **No root cause exists** — the path is functional (see harness PASS above). Probes added for regression-proofing: `VideoLayer.HasRenderedFrame` + `FramesCopied` (monotonic counter).
- [~] **Acceptance:** a mandatory video plays full-screen through the compositor, no legacy `VideoOverlayWindow` — **proven by harness** (frame pipeline + engine windows + no legacy window creation; legacy skip guards code-verified at `AvaloniaVideoService.cs:846/:862`). `[~]` not `[x]`: "VISIBLY" (pixel-level on-screen check) is deferred to Phase B's side-by-side WPF parity runs — the harness asserts everything except literal screen pixels. Re-run anytime: `dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj -c Debug -- --verify-video "<local .mp4>"` (exit 0 = pass).

### Phase B — Video parity with the legacy path
Match what `AvaloniaMultiMonitorVideoService` + `VideoOverlayWindow` do today:
- [ ] **Audio:** volume (`LibVlcAudioHelper.GetEffectiveVolume`), output-device selection, mute — route `UpdateVolume()` to the layer, not just `_currentWindow` / `_multiMonitor`.
- [ ] **Attention checks:** decouple `IsPlaying` / `SetupAttention` / `CheckSpawnTargets` / duration / safety timer / segment-arming from `VideoOverlayWindow` so they fire on the UCE layer (`OnVideoWindowStarted`'s body must run for the layer).
- [ ] **Dual-monitor + strict mode + segment (random-slice) mode** behave as WPF.
- [ ] `VideoAboutToStart` / `VideoStarted` / `VideoEnded` fire with correct timing.

### Phase C — Verify the other migrated layers (skill Phase 2)
- [ ] Exercise flash, subliminal, bouncing-text, bubbles, lock-card, pink/spiral/brain-drain end-to-end vs WPF (z-order, opacity, timing, multi-monitor). Mark rows in `avalonia-ui-parity-matrix.md`.

### Phase D — Performance pass (separate edits, after parity)
- [ ] `VideoLayer`: reuse a persistent `SKBitmap`/`SKImage` (kill the ~480 MB/s per-frame alloc); draw a cached `SKImage`, not `DrawBitmap`.
- [ ] Fold `VideoLayer._renderTimer` into the engine `Update()` pass (drop the second 60 Hz timer).
- [ ] Evaluate moving the engine loop to `CompositionCustomVisualHandler` (render-thread, self-driven) — research-backed; do as an isolated, benchmarked change.
- [ ] Dirty-rect / opaque-cull (skill Phase 7) only if profiling shows a need.

### Phase E — Flip default to UCE-only, then delete legacy (skill Phase 6)
- [ ] Remove the `_mandatoryVideoLayer == null` / `_videoLayer == null` fallback guards in `AvaloniaVideoService.PlayFile` / `PlayUrlCore`.
- [ ] Audit and rehome the **9 references** to `IMultiMonitorVideoService` (video service, overlay service, autonomy, remote command executor, app-info tab, `MainWindowViewModel`, head stubs, DI). Confirm none rely on it for playback *status* before deleting.
- [ ] Delete `AvaloniaMultiMonitorVideoService`, `VideoOverlayWindow`, and the per-overlay `*Window` classes listed in the skill's Phase 6.
- [ ] Remove their DI registrations + dead `using`s.
- [ ] **Acceptance:** `CCP.Desktop.slnf` builds 0 errors; every video/overlay feature works UCE-only; memory under heavy load is ≤ the legacy path.

## Risks / open questions
- **Render-thread bitmap draw:** is drawing a UI-thread-allocated `SKBitmap` onto the leased GPU
  canvas the cause of the no-show? Phase A bisect answers this; Phase D's persistent-`SKImage` is the
  likely correct+faster shape regardless.
- **Cross-platform click-through:** Windows is solved; Linux (XShape) / macOS / Wayland are
  open and out of scope until desktop-Windows parity holds.
- **Chaos migration (skill Phase 4)** is a large separate effort; not blocking video/overlay parity.

## Verification commands
```bash
dotnet build ConditioningControlPanel/CCP.Desktop.slnf -c Debug
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj
```
Drive each feature in the running app (parity is proven by exercising, not by reading) and check
`ConditioningControlPanel/ccp-run.log` for `VideoLayer:` / `CompositorEngine` lines.

## Sources
- [Avalonia — Custom rendering](https://docs.avaloniaui.net/docs/graphics-animation/custom-rendering)
- [ICustomDrawOperation.Render not re-invoked (#12247)](https://github.com/AvaloniaUI/Avalonia/issues/12247)
- [Don't re-create SKImage on every WriteableBitmap draw (PR #18164)](https://github.com/AvaloniaUI/Avalonia/pull/18164)
- [Custom drawing examples (wieslawsoltes)](https://github.com/wieslawsoltes/CustomDrawingAvaloniaExamples)
- [Pass-through transparency v11 (#11911)](https://github.com/AvaloniaUI/Avalonia/discussions/11911) · [Click-through transparent window (#13827)](https://github.com/AvaloniaUI/Avalonia/discussions/13827)
