# Unified Overlay Host (WPF mini-compositor)

Branch `feat/wpf-compositor`. Flag: `Settings.UnifiedOverlayHost` (default OFF).
One shared fullscreen click-through Skia window per monitor (`CompositorHostWindow`)
renders passive effects as z-ordered `IWpfLayer`s (`CompositorEngine`), replacing the
one-window-per-effect model that causes the session-lag / mouse-stutter cluster.
The seam (member names, z-values, rules) deliberately MIRRORS the Avalonia port's
compositor (ObviouslyNotMich fork, `CCP.Avalonia/Compositor/`) so effect code converges.

## Why (the lag mechanism)
Bubbles + subliminals + spiral + pink + braindrain concurrently = 4-6 fullscreen
`AllowsTransparency` windows redrawing + per-bubble windows + N `DispatcherTimer`s.
DWM/UI-thread saturation starves the WH_MOUSE_LL hook -> system-wide mouse stutter.

## House prototypes this generalizes (copy their scars, not their windows)
- `Chaos/ChaosSkiaFxOverlay.cs` - SKElement + CompositionTarget.Rendering + keep-alive
  click-through window + idle auto-hide (~:550) + px->DIP `Local()` (~:607).
- `Chaos/ChaosBubbleHostOverlay.cs` - shared Canvas host, physical-px `Place()`,
  ref-counted keep-alive, `RefreshMetrics` DPI cache (~:183).

## Extracted legacy contracts (recon 2026-07-13, line numbers drift)

### OverlayService (Services/Notifications/OverlayService.cs)
- One window per screen via `App.GetAllScreensCached()`; shell = borderless transparent
  topmost non-activating click-through; ex-styles in SourceInitialized (~:855/:1281/:1721).
- Pink filter: `Border` + `SolidColorBrush(FromArgb(opacity*255, GetFilterRgb()))` -
  opacity lives in the BRUSH ALPHA (~:821). No image.
- Spiral: GIF frames decoded ONCE shared across screens (`LoadSpiralGifFrames`), animated
  by swapping `Image.Source` on `_gifFrameTimer` (Render priority, ~:952,:1183);
  `Stretch.UniformToFill` + ClipToBounds. Non-GIF spiral = `MediaElement` VIDEO loop
  (~:1301) - VIDEO SPIRAL STAYS LEGACY for now (layer handles GIF path only).
  **Spiral opacity always gets x0.1 reduction** (~:314,:729,:1306) - replicate exactly.
- Brain drain: GDI BitBlt screen capture (P/Invokes ~:103) -> BitmapSource -> WPF
  BlurEffect(Gaussian, radius intensity*0.4/downscale) on a timer (fps<=30/60 by perf
  tier, ~:1489); window is WDA_EXCLUDEFROMCAPTURE (~:1724) to avoid self-capture.
- Ramps: `_rampPinkOpacity/_rampSpiralOpacity/_rampBrainDrainOpacity` (nullable, ~:61)
  OWN the opacity while set; `Update*Opacity()` early-returns during a ramp (~:894).
- `PulseOverlays()` (~:282): 1s boost then restore; #535 fix = restore to RAMP value via
  `Apply*OpacityDirect` when ramp active, else settings (~:333-361).
- Topmost decays: re-pin HWND_TOPMOST every ~10 ticks of the 500ms `_updateTimer` (~:73,
  :1928). Compositor host needs equivalent re-pin cadence.
- Public API to keep working in compositor mode: Start/Stop, RefreshOverlays,
  RefreshForDualMonitorChange, PulseOverlays, ShowOverlayTimed(kind,...),
  Show/HideOverlaySustained(kind,...), SetSustainedOverlayOpacity, ReleaseOpacityRampHolds,
  Start/StopBrainDrainBlur, UpdateBrainDrainBlurOpacity.

### SubliminalService (Services/Subliminal/SubliminalService.cs)
- One persistent window per screen (`_screenWindows`, keyed DeviceName, ~:844); idles at
  Opacity=0, NEVER Hide() (hidden layered windows re-present a stale frame, ~:1221).
- Card = outlined text: Arial Bold 120, 8 offset outline copies + main text (~:887-945);
  colors from SubTextColor/SubBackgroundColor settings. 50ms fade-in / hold / 50ms
  fade-out storyboard with `_showGeneration` guard (~:1178).
- WDA_NONE deliberately applied (~:978) - subliminals STAY in capture; OCR avoids them
  via `GetActiveTextScreenRects()` rects instead.
- `SubliminalStealsFocus` clears WS_EX_NOACTIVATE + Activate() (~:959-965,:640) -
  focus-steal CANNOT come from the shared host; that variant stays on legacy windows
  (same reason solid mode falls back, ~:619).

### FlashService (Services/Flash/FlashService.cs)
- Window pool (`_windowPool`, cap ~:2397); fade driven by CompositionTarget.Rendering
  heartbeat (~:1503), per-window lifetime CTS.
- GIF/WebP decode: `Services/Media/AnimatedWebp.DecodeFrames` (SKCodec) behind a GLOBAL
  `SemaphoreSlim(2,2)` decode gate (AnimatedWebp.cs:70, crash family d05d5ae4 0xc0000374).
  ANY compositor decode fan-out must honor the same gate.
- `ApplyClickability` (~:2319): clickable flashes clear WS_EX_TRANSPARENT per spawn.
  FlashLayer will need hook hit-testing instead (like bubbles).

### BubbleService shared-host mode (Services/BubbleService.cs)
- Snapshots rebuilt per anim tick (~:510): `ChaosBubbleCentersSnapshot` (Point[] px),
  `ChaosClickDiscsSnapshot` ((X,Y,R,Hold)[] px, host-rendered clickable bubbles only);
  static immutable arrays swapped atomically, hook thread reads lock-free.
- `OnSharedHostLeftDown(px)` on HOOK THREAD (~:546): hit -> BeginInvoke PopTopmostAt,
  return `!needsHold` - **hold-to-defuse clicks are never swallowed** (GetAsyncKeyState
  cannot see swallowed clicks). Preserve exactly.
- `GlobalMouseHook` (Services/Input/GlobalMouseHook.cs): `Func<Point,bool>` callbacks,
  synchronous on hook thread, return true = swallow; touch only immutable snapshots.

### Screens / DPI
- App manifest is PerMonitorV2: WPF DIP bounds are computed at the PRIMARY scale and are
  WRONG on mixed-DPI secondaries - always stamp physical px via SetWindowPos after the
  handle exists and on DpiChanged (#457/#539 lesson). `CompositorHostWindow` does this.
- Enumerate monitors ONLY via `App.GetAllScreensCached()` (App.xaml.cs:465); invalidated
  on display change; can be empty during transitions.

## Migration order + routing pattern
Effect services keep ALL state/math (ramps, pulses, holds, timers-for-logic); layers
only render state. Routing: each service checks `UseCompositor` (flag + App.Compositor
!= null) at effect-start; legacy windows when off. Order:
1. PinkTintLayer (z70) + SpiralLayer (z60, GIF path) - DONE when this doc is committed.
2. BrainDrainLayer (z55, ExcludeFromCapture=true, GDI capture -> SKImage -> Skia blur).
3. SubliminalLayer (z40, capture-visible; focus-steal variant stays legacy).
4. BubbleLayer (z45) + FlashLayer (z30) - hook hit-testing via existing snapshots.
5. Measure vs legacy (frame pacing + mouse-hook latency), then flip default ON.

## Open questions
- Video spiral (MediaElement) as a layer needs a frame source - defer, maybe post-libmpv
  discussions with the Avalonia port.
- Subliminal focus-steal: keep legacy per-screen window path forever, or drop feature
  when host mode on? Owner decision pending.
- Engine topmost re-pin cadence vs OverlayService's 5s repin - decide when wiring video
  interaction (video windows raise above overlays deliberately during playback).
