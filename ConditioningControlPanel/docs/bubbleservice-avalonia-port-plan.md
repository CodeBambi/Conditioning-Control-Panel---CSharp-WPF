# §19.3 BubbleService → Avalonia Implementation Plan

Generated during the §19.3 main-merge backlog sprint.

## 1. What `Services/BubbleService.cs` does today

**Public API surface**
- Lifecycle: `Start/Stop/Dispose`, `PauseAndClear/Resume`, `RefreshFrequency`, `SpawnOnce`.
- State/events: `IsRunning`, `IsPaused`, `ActiveBubbles`, `ActiveFreezeBubbles`, `EStimChargesLeft`, `OnBubblePopped`, `OnBubbleMissed`.
- Ambient pop game: `SpawnBubble`, `PopAllBubbles`, `DrainAudioDevicePool`.
- Chaos run API: `BeginChaosMode`, `EndChaosMode`, `SpawnChaosBubble`, `SpawnChaosChaperone`, `SpawnChaosBoundPair`, `SetChaosFrozen`, `VibrateAllForFreeze`, `SetChaosTimeScale`, `SetChaosInputLocked`, `SetVibePop`, `PopNearestBenign`, `DefuseAllLive`, `PopAllChaosPaid`, `PopBubblesInRect`, `BringAllToFront`, `TriggerEStimShockwave`, `TriggerChaosRipple`, `AddChaosResidue`, `TriggerPlayerRipple`, `MinChaosFuseSec`, `IsCursorOverLiveChaosBubble`, `AnyDarterIntersects`, `HideChaosHints`, `PlayCue`, `PlayChime`, `OnSharedHostLeftDown`.

**Internal responsibilities**
- Owns the ambient bubble spawn loop and a single shared ~30fps animation timer.
- Maintains a list of live `Bubble` instances and drives their motion/pop/detonate lifecycle.
- Loads/tints the bubble image and plays pop/chime/SFX cues.
- Pooled per-bubble WPF layered windows **or** shared-host `Canvas` mode.
- Chaos effect-bubble machinery: effect specs, fuses, hold-to-defuse channels, E-Stim arcs, chain reaction, field ripples/residue, Tail-Plug trails, Bound pairs, Chaperone orbit, Tease/Brittle variants, VibePop sweeps, freeze/time-scale/input-lock.
- Publishes immutable cursor/hit-disc snapshots for the low-level mouse hook.
- Integrates with legacy `App.*` statics: `Settings`, `Logger`, `Haptics`, `Progression`, `Achievements`, `DiscordRpc`, `Audio`, `Video`, `Flash`, `Overlay`, `Bark`, etc.

## 2. WPF/Windows-only dependencies to remove

| Category | WPF/Windows APIs used |
|---|---|
| UI framework | `System.Windows.*` (`Application`, `Window`, `FrameworkElement`, `Image`, `Grid`, `Canvas`, `Ellipse`, `TextBlock`, `Border`, `Polyline`, `Brushes`, `SolidColorBrush`, `RadialGradientBrush`, `LinearGradientBrush`, `Color`, `Point`, `Rect`, `Stretch`, `DropShadowEffect`, `RenderOptions`, `BitmapImage`, `BitmapCacheOption`, `DrawingImage`, `DrawingGroup`, `TransformGroup`, `ScaleTransform`, `RotateTransform`, `TranslateTransform`, `UIElement`, `DispatcherPriority`, `Cursors`, `MouseButtonEventHandler`, `MessageBox` in caller) |
| Threading | `System.Windows.Threading.DispatcherTimer`, `DispatcherHelper` (WPF `Application.Current.Dispatcher`) |
| Screens | `System.Windows.Forms.Screen` (`.PrimaryScreen`, `.AllScreens`, `.FromPoint`, `.WorkingArea`, `.Bounds`, `.DeviceName`) |
| DPI | Win32 `MonitorFromPoint` + `GetDpiForMonitor` (via shcore.dll), `System.Drawing.Graphics.FromHwnd` fallback |
| Assets | `pack://application:,,,/Resources/bubble.png` URI |
| Audio | `NAudio.Wave.WaveOutEvent`, `AudioFileReader`, `PlaybackState` |
| Input | Win32 `GetCursorPos`, `GetAsyncKeyState`; legacy `GlobalKeyboardHook`/`GlobalMouseHook` in `ChaosModeService` |
| Window styles | Win32 `SetWindowPos`, `GetWindowLong`, `SetWindowLong` (`WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE`, `WS_EX_TRANSPARENT`, `HWND_TOPMOST`) |
| GIF support | `XamlAnimatedGif.AnimationBehavior` for the Tease face |
| Interop | `System.Windows.Interop.WindowInteropHelper` |

## 3. `Bubble` and `EffectBubbleSpec`

- **`Bubble`** (`Services/BubbleService.cs:1680-4200`) is a large internal class that owns the per-bubble WPF visual tree, pooled window, motion physics, pop animation, chaos overlays (tint, fuse ring, freeze aura, brittle cracks, tease face, prism ghost, darter telegraph, hint pill), sparkle particles, click/gaze handling, and shared-host positioning. It is the most visual-heavy part of the port.
- **`EffectBubbleSpec`** (`Services/Chaos/ChaosBubbleVariants.cs:29-107`) is a plain data object describing a chaos bubble: variant id, payload, size, tint, label, live/fuse flags, motion, and behavioral flags (freeze, golden, heart, droplet, prism, brittle, sweeper, echo, chaperone, escort, tease, bound, darter). `ChaosBubbleVariants` builds specs and is already largely data-driven; it can be reused/shared.

## 4. How `ChaosModeService` drives it

`Services/Chaos/ChaosModeService.cs` calls `App.Bubbles.*` for:
- `BeginChaosMode` (registers ~20 callbacks/knobs).
- `SpawnChaosBubble`, `SpawnChaosChaperone`, `SpawnChaosBoundPair`.
- `SetChaosFrozen`, `SetChaosInputLocked`, `SetVibePop`, `SetChaosTimeScale`, `VibrateAllForFreeze`.
- `DefuseAllLive`, `PopAllChaosPaid`, `PopBubblesInRect`, `PopAllBubbles`.
- `TriggerEStimShockwave`, `TriggerChaosRipple`, `AddChaosResidue`, `TriggerPlayerRipple`.
- `BringAllToFront`, `MinChaosFuseSec`, `IsCursorOverLiveChaosBubble`, `AnyDarterIntersects`, `EStimChargesLeft`, `PlayCue`, `PlayChime`.
- Reads static fields `BubbleService.ChaosLastPopXDip/YPx` for floating text/droplet spawn anchors.
- Installs a global mouse hook whose `LeftDown` calls `OnSharedHostLeftDown` and whose `RightDown` casts the Ripple.

In Avalonia, `AvaloniaChaosService` is currently a stub, so no run currently reaches `BeginChaosMode`. The ported `BubbleService` must keep the same surface so a future `ChaosModeService` port (or the WPF engine running under a compatibility bridge) can slot in.

## 5. Existing Avalonia / Core seams that can help

Already available:
- `IUiDispatcher` / `AvaloniaUiDispatcher` → UI thread dispatch.
- `IScreenProvider` / `AvaloniaScreenProvider` → screen enumeration + `Scaling`.
- `IAssetLoader` / `AvaloniaAssetLoader` → embedded asset streams (`avares://`).
- `IScheduler` / `AvaloniaScheduler` → periodic/one-shot timers.
- `IOverlaySurface` / `AvaloniaOverlaySurface` → borderless topmost overlay window base.
- `IAudioPlayer` / `AvaloniaAudioPlayer` (LibVLC-backed) → music/long audio.
- `IAudioDeviceService` / `AvaloniaAudioDeviceService` → output device enumeration.
- `IInputHook` / `AvaloniaInputHook` → keyboard hook on Windows; mouse is stubbed.
- `IAppLogger`, `ISettingsService`, etc.
- Avalonia Chaos overlays already ported: `ChaosBubbleHostOverlay`, `ChaosSkiaFxOverlay`, `ChaosFieldFxOverlay` (via `ChaosSkiaFxOverlay` fallback), `ChaosDvdHostOverlay`, `ChaosPopText`, `AvaloniaChaosWindowZ`, `AvaloniaChaosArt`.
  - **STALE 2026-07-05:** most of these are now COMPOSITOR LAYERS; the window classes `ChaosSkiaFxOverlay`, `ChaosDvdHostOverlay`, `ChaosPopText`, `ChaosFieldFxOverlay` (etc.) were DELETED. Wire chaos FX through the `AvaloniaChaosService` layer seams, NOT these deleted windows.
- `AvaloniaChaosCompat.cs` already exposes `IAvaloniaBubbleService` and `AvaloniaChaosEnv.Bubbles` as stubs.

**Gaps**
- `IAudioPlayer` is a single LibVLC player and cannot overlap rapid pop sounds. A pooled one-shot SFX abstraction is needed.
- `IInputHook` has no global cursor position or mouse-button state. A pointer-state seam is needed.
- Cross-platform click-through and `WS_EX_TRANSPARENT` are not available in Avalonia core; Windows needs a platform-specific window subclass, and Linux/macOS will have to degrade gracefully.
- `XamlAnimatedGif` is WPF-only; the Tease GIF needs an Avalonia equivalent or fallback to stills.
- `DropShadowEffect` exists in Avalonia but behaves differently from WPF; some glows may need Skia fallback.

## 6. `IChaosService` and DI registration

- `IChaosService` lives in `CCP.Core/App.cs:202` and is registered as `AvaloniaChaosService` in `CCP.Avalonia/ServiceCollectionExtensions.cs:137`.
- `AvaloniaChaosService` is a stub and does not call `BubbleService`; porting `BubbleService` does not require finishing the `ChaosModeService` port, but it does require that the new bubble service be registered and reachable.
- Introduce a new `IBubbleService` interface and register it as a singleton in Avalonia DI. Legacy callers that use `App.Bubbles` can be satisfied by assigning the singleton to `CoreApp.Bubbles` at startup.

## 7. Concrete implementation plan

### Target namespace / class names

| Layer | Type | Namespace | Purpose |
|---|---|---|---|
| Core abstraction | `IBubbleService` | `ConditioningControlPanel.Core.Services.Chaos` | Public contract used by ChaosModeService and Avalonia head. |
| Core engine | `BubbleEngine` | `ConditioningControlPanel.Core.Services.Chaos` | Platform-agnostic state, timers, spawn logic, physics, field hazards, snapshots. |
| Core model | `BubbleState` | `ConditioningControlPanel.Core.Services.Chaos` | Per-bubble state data (position, velocity, fuse, flags) used by engine. |
| Avalonia service | `AvaloniaBubbleService` | `ConditioningControlPanel.Avalonia.Services` | Hosts `BubbleEngine`, creates Avalonia visuals, handles assets/audio. |
| Avalonia visual | `AvaloniaBubble` | `ConditioningControlPanel.Avalonia.Chaos` | Avalonia control for one bubble (replaces internal `Bubble` visual tree). |
| Avalonia window | `AvaloniaBubbleWindow` | `ConditioningControlPanel.Avalonia.Chaos` | Per-bubble borderless window with platform-specific click-through. |
| Pointer seam | `IPointerState` / `AvaloniaPointerState` | `ConditioningControlPanel.Core.Platform` / `.Avalonia.Platform` | Global cursor + button sampling. |
| SFX seam | `ISfxPlayer` / `AvaloniaSfxPlayer` | `ConditioningControlPanel.Core.Platform` / `.Avalonia.Platform` | Pooled one-shot sound playback. |
| Mouse hook | `AvaloniaMouseHook` (extend `IInputHook`) | `ConditioningControlPanel.Avalonia.Platform` | Global left/right click for shared-host/Ripple. |

### File-by-file breakdown

**New files**
- `CCP.Core/Services/Chaos/IBubbleService.cs`
- `CCP.Core/Services/Chaos/BubbleEngine.cs`
- `CCP.Core/Services/Chaos/BubbleState.cs`
- `CCP.Core/Platform/IPointerState.cs`
- `CCP.Core/Platform/ISfxPlayer.cs`
- `CCP.Avalonia/Services/AvaloniaBubbleService.cs`
- `CCP.Avalonia/Chaos/AvaloniaBubble.cs`
- `CCP.Avalonia/Chaos/AvaloniaBubbleWindow.cs`
- `CCP.Avalonia/Chaos/AvaloniaBubbleWindow.Windows.cs`
- `CCP.Avalonia/Platform/AvaloniaPointerState.cs`
- `CCP.Avalonia/Platform/AvaloniaSfxPlayer.cs`
- `CCP.Avalonia/Platform/AvaloniaMouseHook.cs` (or extend `AvaloniaInputHook`)

**Files to modify**
- `CCP.Avalonia/ServiceCollectionExtensions.cs`
  - Register `IBubbleService` → `AvaloniaBubbleService` singleton.
  - Register `IPointerState` → `AvaloniaPointerState`.
  - Register `ISfxPlayer` → `AvaloniaSfxPlayer`.
  - Replace/register `IInputHook` implementation that includes mouse (or add `AvaloniaMouseHook` separately).
- `CCP.Avalonia/App.axaml.cs`
  - After `Services` is built, assign `CoreApp.Bubbles = Services.GetRequiredService<IBubbleService>()`.
  - Assign `AvaloniaChaosEnv.Bubbles` to the service so existing overlays can call it.
- `CCP.Avalonia/Chaos/AvaloniaChaosCompat.cs`
  - Replace stub `IAvaloniaBubbleService` with the real surface needed by overlays (or just have `AvaloniaBubbleService` implement it).
  - Remove no-op `AvaloniaChaosSfx`/`AvaloniaChaosArt` wiring once `ISfxPlayer`/`IAssetLoader` are used.

### WPF / Avalonia API mapping

| WPF/Windows | Avalonia/Core replacement |
|---|---|
| `DispatcherTimer` (UI/render priority) | Avalonia `DispatcherTimer` with `DispatcherPriority.Render`; `IScheduler` for non-render timers |
| `Application.Current.Dispatcher` | `IUiDispatcher` |
| `System.Windows.Forms.Screen` | `IScreenProvider` → `ScreenInfo`/`PixelRect`/`Scaling` |
| Win32 DPI | `ScreenInfo.Scaling` (no P/Invoke needed) |
| `pack://application:,,,/Resources/bubble.png` | `IAssetLoader.Open(new Uri("avares://.../bubble.png"))` |
| `BitmapImage` | `Avalonia.Media.Imaging.Bitmap` |
| `Image`/`Grid`/`Canvas`/`Ellipse`/`TextBlock`/`Border`/`Polyline` | Avalonia equivalents |
| `DropShadowEffect` | Avalonia `DropShadowEffect` / Skia glow fallback |
| `TransformGroup`/`ScaleTransform`/`RotateTransform` | Avalonia transforms |
| `NAudio.WaveOutEvent`/`AudioFileReader` | `ISfxPlayer` with pooled LibVLC `MediaPlayer`s (or platform sound player) |
| `App.Audio.ApplyPreferredDevice` | `IAudioDeviceService` + pass device id to `ISfxPlayer` |
| `GetCursorPos`/`GetAsyncKeyState` | `IPointerState` (Win32 on Windows; stub/no-op elsewhere) |
| `GlobalMouseHook` | `AvaloniaMouseHook` (Win32 LL hook on Windows) |
| `WS_EX_TRANSPARENT`/`SetWindowPos` | Platform partials on `AvaloniaBubbleWindow`; shared-host mode uses `ChaosBubbleHostOverlay` |
| `XamlAnimatedGif` | Avalonia GIF player or still-frame fallback |

### DI registration changes (Avalonia head)

Add to `ServiceCollectionExtensions.ConfigureCoreServices`:

```text
services.AddSingleton<IPointerState, AvaloniaPointerState>();
services.AddSingleton<ISfxPlayer, AvaloniaSfxPlayer>();
services.AddSingleton<IBubbleService, AvaloniaBubbleService>();
services.AddSingleton<IInputHook, AvaloniaMouseHook>();   // or extend existing
```

Then in `App.axaml.cs` after `BuildServiceProvider`:

```text
CoreApp.Bubbles = Services.GetRequiredService<IBubbleService>();
AvaloniaChaosEnv.Bubbles = (IAvaloniaBubbleService)CoreApp.Bubbles;
```

## 8. Staged approach

### Stage 1 — Ambient clickable bubbles
- Extract `BubbleEngine` and `BubbleState` for non-chaos logic: spawn loop, float-up physics, miss/destroy, pooled-window/shared-host management, ambient pop sound, gaze bounds.
- Implement `AvaloniaBubble` + `AvaloniaBubbleWindow` with the bubble image, basic tint/transform, and click-to-pop.
- Implement `AvaloniaPointerState` (Windows) and `AvaloniaSfxPlayer`.
- Wire `Start/Stop/SpawnOnce/PopAllBubbles` and the settings-driven frequency.
- Target: build passes, 20s smoke test runs, ambient bubbles appear and pop on Windows and Linux/macOS (with degraded click-through where needed).

### Stage 2 — Chaos effect bubbles
- Port `BeginChaosMode`/`EndChaosMode` and `SpawnChaosBubble`.
- Implement effect visuals: tint overlay, fuse ring, freeze aura, labels, darter telegraph, brittle cracks, tease face, prism ghost, chaperone shield, echo outline, hint pill.
- Wire chaos callbacks to the `AvaloniaChaosService` compositor-layer seams (the `ChaosSkiaFxOverlay`/`ChaosPopText` windows this referenced were DELETED 2026-07-05; `ChaosBubbleHostOverlay` remains, gated by the experimental `ChaosBubbleSharedHost` setting).
- Implement field hazards: ripples, residue, player ripple, chain reaction.
- Test via a temporary debug harness or the future `ChaosModeService` port.

### Stage 3 — Full boon synergy
- Hold-to-defuse channel, cursor pull/Cam Girl, VibePop, E-Stim arcs, Tail-Plug trails, Spanker, Bound pairs, Chaperone orbit.
- Global mouse hook for shared-host left-click swallow and the Ripple right-click.
- Freeze/time-scale/input-lock knobs.
- Rabbit Caller cursor glow, Snap Field, Porn DVD collider (`PopBubblesInRect`), `BringAllToFront`.
- Validate parity against the WPF build: same spawn density, same pop scoring, same audio/visual timing.

## 9. Risky areas & validation

| Risk | Mitigation |
|---|---|
| Cross-platform click-through / global hooks | Implement Windows path first with native interop; Linux/macOS degrade to non-clickable preview or use host-overlay input events. Gate shared-host mode behind `OperatingSystem.IsWindows()` if needed. |
| Single LibVLC player cannot overlap pops | Build `AvaloniaSfxPlayer` with a small pool of `MediaPlayer`s or use a platform one-shot API. |
| Per-window performance on dense chaos fields | Default to `ChaosBubbleSharedHost` on Avalonia; keep per-window mode as a fallback. |
| DPI/screen scaling differences | Use `ScreenInfo.Scaling` consistently; test mixed-DPI multi-monitor setups. |
| Avalonia effects/GIF limitations | Provide still-frame fallbacks for Tease; use Skia FX layer for complex glows where Avalonia effects fall short. |
| Thread-safety of static snapshots | Keep snapshot arrays rebuilt on the UI timer; the hook only reads the immutable reference. Match the WPF contract exactly. |
| Legacy `App.*` statics | Assign the DI singleton to `CoreApp.Bubbles` at startup; do not chase every static call site in Stage 1. |

**Validation checklist**
1. `dotnet build` for `CCP.Avalonia.Desktop` and `CCP.Avalonia.Desktop.Windows`.
2. `dotnet test tests/CCP.Core.Tests` still 95/95.
3. 20s Avalonia desktop smoke test passes.
4. Manual ambient-bubble smoke: Start → bubbles float → click pops → sound plays → Stop clears.
5. Chaos debug smoke (Stage 2+): `BeginChaosMode` → spawn treat/live → click/defuse → overlays animate.
6. Memory/perf sanity: run 2-minute chaos field; watch native memory and confirm no window-leak growth.
7. Linux/macOS build smoke (click-through may be limited; confirm no crashes).
