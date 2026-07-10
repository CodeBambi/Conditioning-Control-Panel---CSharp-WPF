# Cross-Platform Rebuild Plan — Conditioning Control Panel

> **What this document is (re-crowned 2026-07-10 by the docs rework).** This is the port's
> **architecture/seam reference**, the canonical **Avalonia v12 gotchas list (§21)**, the **per-region
> click-through spec (§7.4)**, and the **Linux bring-up mechanism catalogue**. The active driver is
> [`skia-rebuild-goal.md`](skia-rebuild-goal.md); the single live work tracker is
> [`avalonia-migration-task-board.md`](avalonia-migration-task-board.md); the doc map is
> [`docs-index.md`](docs-index.md). Read order for a port session: `docs-index.md` → `skia-rebuild-goal.md` →
> task board (claim ONE row) → this doc's §21 / §7.4 / Linux section only as your claimed row demands.
>
> **What is preserved VERBATIM here (skills hard-depend on them — do not rewrite):** §7.4 (per-region
> click-through, team review 2026-07-09) and §21 (Avalonia v12 gotchas). Their content is canonical; only
> their section *number* is load-bearing (`avalonia-research`, `overlay-clickthrough`, `wpf-parity`,
> `port-plan`, `port-audit`, `unified-compositor-engine-plan`, and the task board cite them by number).
>
> **What is current and what is not.** Live status (HEAD, gates, percent-ported, open items) lives in
> `skia-rebuild-goal.md` (Current state + Shipped ledger + Open workstreams) and the task board — never
> trust phase checkboxes or snapshots here as live status. This doc's §1A is a compressed phase ledger
> only. Nothing is currently in flight (post-crash reconciliation 2026-07-09); any "claimed/WIP/co-agent"
> note found anywhere is historical debris.
>
> **Acceptance framing throughout = the spirit's perf gate:** a ported feature is accepted only when it is
> at least as fast and smooth as the WPF head — preferably measurably improved (startup, memory, FPS,
> reliability, security). Big changes are encouraged when they win on merit; what/why is recorded in the
> board. **Windows never degrades to enable Linux; Linux degrades gracefully with a recorded gap** where the
> platform genuinely cannot do a thing. The WPF head is the **behavior reference ONLY — never modify its
> behavior.**

**Goal (unchanged intent):** rebuild Conditioning Control Panel for Windows, Linux, macOS, and Android using
**Avalonia UI v12** and **LibVLCSharp**. The current .NET 8 WPF/WinForms desktop app
(`net8.0-windows10.0.19041.0`, single-file `win-x64`) is the behavior reference; the target is a multi-head
Avalonia v12 solution with a shared .NET 8/10 Core, per-platform desktop and Android heads, and LibVLCSharp
for cross-platform media. **Functionality is the contract; the implementation underneath is not** — old code,
old dependencies, and old architectural choices carry zero sentimental weight.

---

## 1. Executive Summary

The migration is a **UI rewrite plus platform abstraction**, not a retarget-and-rebuild. The largest cost is
the WPF UI layer (~121 XAML files, ~483 C# files, ~133k LOC of UI-related code); the engine logic (models, AI
orchestration, gamification, sessions, networking) is portable once the right seams exist. The proven strategy
is **engine-first extraction**:

1. Extract a cross-platform `CCP.Core` class library (DONE).
2. Introduce platform seams behind real cross-platform divergences (DONE — see §3.3).
3. Keep the WPF app runnable during migration as the behavior reference (DONE — it references `CCP.Core`).
4. Build the Avalonia UI shell on top of `CCP.Core` (Windows ~92%, Linux ~45% — see goal Current state).
5. Add mobile heads only after desktop parity is reached (out of scope for the current goal).

**Biggest risks (remaining):** Win32 windowing for layered overlays + PER-REGION click-through on Linux
(X11/Wayland), global input hooks off-Windows, NAudio/WASAPI audio ducking, DPAPI-encrypted secrets, GDI/desktop
capture, and desktop wallpaper override. Each is gated per §6/§7 and the Linux catalogue below — work on
Windows, degrade gracefully with a recorded gap on Linux.

---

## 1A. Phase Status Ledger (compressed; live status lives in the goal + board)

> Phase roadmap §8 was the *original* plan-of-record; it is folded into this ledger. A few specifics never
> happened as written (the temporary `CCP.WpfShim` → replaced by `CCP.WindowsOnly` + the live WPF head; the
> single `CCP.Avalonia.Desktop` → four heads per §3.1). Update this ledger only when a phase materially
> changes; for day-to-day status use the task board.

| Phase | Status | One-line evidence (hashes from `progress.md`) |
|---|---|---|
| 0 — Cleanup | ✅ done | Dead deps: `SharpDX.*` + `OpenAI-DotNet` fully removed (0 references, verified 2026-07-10). CORRECTED 2026-07-10: `OllamaSharp` is STILL a `<PackageReference>` in the legacy WPF head (`ConditioningControlPanel.csproj:100`, v5.4.16, no `.cs` usage — dead weight; remove on the next WPF-head csproj touch). `NAudio`/`OpenCvSharp`/WebView2 confined to the Windows head. |
| 1 — Carve out `CCP.Core` | ✅ done | **302** `.cs` in Core (**91** models + **33** platform-seam interfaces in `CCP.Core/Platform/` + portable services) — live count 2026-07-10; the long-cited "156 (53 models + 26 seams)" was stale by ~2× and is FALSIFIED. Builds clean on `net8.0`. |
| 2 — Prove Core off-Windows | ✅ done | CI builds Core + heads on `ubuntu-latest`/`macos-latest` and runs `CCP.Core.Tests` (542/542 live 2026-07-10). |
| 3 — Avalonia solution | ✅ done | All four desktop heads + Android head exist and build (§3.1). |
| 4 — XAML/UI migration | 🚧 Windows ~92% / Linux ~45% | Shell, chrome, tabs, dialogs ported; 22-layer UCE surface complete; chaos run engine S1–S9 done. Open: per-region mask + hook swallow, completion sweep, Linux feature sweep. Parity evidence: `avalonia-ui-parity-matrix.md`. |
| 5 — Media & audio | ✅ done (Windows) | Video runs through the compositor (`VideoLayer`/`MandatoryVideoLayer`); legacy video path DELETED (`8069cfb7`); audio ducking on Windows via `WindowsSystemAudioDucker`; Linux/macOS best-effort `pactl`/`osascript`. Open: Linux system-libvlc verify; optional libmpv spike (board row). |
| 6 — OS-shell features | ✅ structurally done (Windows) | Tray, hotkeys, input hook, wallpaper, browser host, window chrome, frame source, audio device + ducker wired via per-head `App.ConfigurePlatformServices`. Open: Linux equivalents (see catalogue). |
| 7 — Build & publish | 🚧 desktop done | CI publishes single-file desktop artifacts for win/linux/macOS; Android job `dotnet build`s the head only (AAB packaging not wired). |
| 8 — Mobile gating | 🚧 structural only | Android head builds; capability gating via `IPlatformCapabilities` + `OperatingSystem.IsAndroid()`. Mobile feature work is OUT OF SCOPE for the current goal (builds stay green). |

**Static service locator (was §15.1): DONE.** The 88 static service properties on WPF `App.xaml.cs` are gone in
the Avalonia heads — everything is `Microsoft.Extensions.DependencyInjection` in
`CCP.Avalonia/ServiceCollectionExtensions.cs` with per-head overrides in `*/DesktopServiceCollectionExtensions.cs`
and each head's `Program.cs`.

**Ponytail / YAGNI build principle (still binding).** Build the simplest thing that works; framework and stdlib
first; no unrequested abstractions; delete over add. A platform seam earns its place ONLY with a real
cross-platform divergence and a real implementation behind it — never a one-line wrapper over a framework API.
Needless wrappers/stubs were already removed (`IUiDispatcher`, `IScheduler`, custom `IAppLogger`, hand-rolled
`LibVLCNativeDiscovery`, the `AvaloniaFrameSource` throw-stub, mobile stubs) in favor of `Dispatcher.UIThread`,
`DispatcherTimer`, `ILogger<T>`, LibVLCSharp's own discovery, and `AssetLoader` directly. Hold new work to that
bar; before adding a seam/abstraction, check it against this principle and §3.3. **Pruning is ongoing (it makes
the app faster), but each prune is a refactor** — build, then re-exercise the affected features end-to-end
(§13.6); don't assume "removed wrapper, still works."

---

## 2. Current State Analysis (reference)

### 2.1 Project Profile

| Property | Current (WPF ref) | Migration Note |
|---|---|---|
| `OutputType` | `WinExe` | `Exe` for desktop heads; mobile uses its own templates. |
| `TargetFramework` | `net8.0-windows10.0.19041.0` | `net8.0`/`net10.0` for shared Core; platform TFMs only in head projects. |
| `UseWPF` / `UseWindowsForms` | `true` / `true` | Removed from Core and Avalonia heads. |
| `RuntimeIdentifier` | `win-x64` | `RuntimeIdentifiers` per head. |
| `PublishSingleFile` / `SelfContained` | `true` / `true` | Supported for Avalonia desktop; not mobile. Native libs stay on disk. |
| Custom `Main` + `STAThread` | `App.xaml.cs` | Avalonia `BuildAvaloniaApp` / platform lifecycles. |

### 2.2 P/Invoke Surface Area

There are **~200 `DllImport` declarations** concentrated in `user32.dll` (~108: window styles, z-order, focus,
hooks, cursor, keys, display), `gdi32.dll` (regions, blitting, DCs), `dwmapi.dll` (dark title bar, rounded
corners), `shcore.dll` (per-monitor DPI), `shell32.dll` (shell thumbnails), `kernel32.dll` (thread/module/mem),
`dbghelp.dll` (crash minidumps). Every one moves behind a platform interface with Windows, Linux, macOS, and
mobile implementations (or a recorded graceful degrade).

### 2.3 Key Windows-Only Dependencies

| Package | Purpose | Migration |
|---|---|---|
| `LibVLCSharp.WPF` | Video surface | `LibVLCSharp.Avalonia` (desktop) or platform mobile surfaces |
| `VideoLAN.LibVLC.Windows` | Native VLC engine | Keep for Windows; system `libvlc` on Linux; per-platform native packages elsewhere |
| `Microsoft.Web.WebView2` / `WebView2.Wpf` | Embedded browser | Abstract `IBrowserHost`; WebView2 only in Windows head (also backs the DTRH epic — board) |
| `NAudio` / `NAudio.Wasapi` | Audio playback / ducking | Abstract `IAudioPlayer`/`ISystemAudioDucker`; LibVLC/ManagedBass/OpenAL or platform APIs |
| `Hardcodet.NotifyIcon.Wpf` / `System.Windows.Forms.NotifyIcon` | System tray | Avalonia built-in `TrayIcon` + `NativeMenu` |
| `MahApps.Metro` / `IconPacks` | UI theme / icons | Avalonia Fluent/Simple theme + `Material.Icons.Avalonia` / custom icons |
| `XamlAnimatedGif` | Animated GIFs | `AvaloniaGif` or custom SkiaSharp/ImageSharp frame animation |
| `SharpVectors` | SVG → WPF | `Svg.Skia` / `Avalonia.Svg.Skia` |
| `OpenCvSharp4.runtime.win` | OpenCV native | Add Linux/macOS runtimes; mobile uses native camera APIs |
| `System.Security.Cryptography.ProtectedData` | DPAPI secrets | `ISecretStore` (Keychain on macOS, libsecret on Linux, DPAPI on Windows) |
| `SharpDX.*` | Direct3D/DXGI | Dead dependency — removed |

---

## 3. Target Architecture (the canonical seam map)

### 3.1 Solution Layout

```
ConditioningControlPanel/
├── CCP.Core/                       # net8.0 — engine, models, portable services
├── CCP.Avalonia/                   # net8.0 — shared Avalonia UI, Views, ViewModels, platform seams
├── CCP.Avalonia.Desktop/           # net8.0 — SHARED desktop logic (LibVLC discovery, DI, secret store)
├── CCP.Avalonia.Desktop.Windows/   # net8.0-windows10.0.19041.0 — Windows head (WebView2, NAudio, Win32)
├── CCP.Avalonia.Desktop.Linux/     # net8.0 — Linux head (system libvlc, WebKitGTK)
├── CCP.Avalonia.Desktop.macOS/     # net8.0 — macOS head (VideoLAN.LibVLC.Mac, WKWebView)
├── CCP.Avalonia.Android/           # net10.0-android — Android head
├── CCP.WindowsOnly/                # net8.0-windows — Windows-specific managed helpers (WPF/WinForms)
├── tests/CCP.Core.Tests/           # net8.0 — headless Core unit tests
├── ConditioningControlPanel.csproj # the original WPF app — behavior reference ONLY, kept runnable
├── ConditioningControlPanel.slnx   # full solution (all heads + WPF + tests)
└── CCP.Desktop.slnf                # solution filter: desktop heads + Core + tests, excludes Android
```

The single "`CCP.Avalonia.Desktop` (Win/Linux/Mac)" head from the original draft was split into a **shared**
`CCP.Avalonia.Desktop` library plus three thin executable heads (`.Windows`, `.Linux`, `.macOS`): only the
Windows head can carry the `net8.0-windows*` TFM and Win32/WebView2/NAudio references. Each head's `Program.cs`
sets `App.ConfigurePlatformServices` to override the shared DI registrations with its native implementations
(last registration wins). The Android head targets `net10.0-android`; desktop heads target `net8.0` (Windows
head uses `net8.0-windows10.0.19041.0`). Use `CCP.Desktop.slnf` for fast desktop-only builds (excludes Android).

### 3.2 Project Responsibilities

| Project | Responsibility |
|---|---|
| `CCP.Core` | Models, settings, session/gamification logic, AI/LLM orchestration, networking, mod/catalogue logic, JSON contracts, localization runtime. No UI framework references. |
| `CCP.Avalonia` | `App.axaml`, Views, UserControls, ViewModels, converters, platform-agnostic styles, **and the Avalonia implementations of the platform seams** (`Platform/Avalonia*.cs`) plus shared DI (`ServiceCollectionExtensions.ConfigureCoreServices`). References `CCP.Core` and Avalonia packages. Exposes `App.ConfigurePlatformServices` so heads override seam registrations. |
| `CCP.Avalonia.Desktop` | **Shared** desktop library: `LibVLCNativeDiscovery`, `DesktopServiceCollectionExtensions` (secret store, single-instance, wallpaper, LibVLC), shared `BuildAvaloniaApp`. Not an entry point. |
| `CCP.Avalonia.Desktop.Windows` | Windows executable head: `Program.cs`, `net8.0-windows*` TFM, WebView2/NAudio/Win32 seam implementations. |
| `CCP.Avalonia.Desktop.Linux` | Linux executable head: `Program.cs`, WebKitGTK browser host, relies on system `libvlc`. |
| `CCP.Avalonia.Desktop.macOS` | macOS executable head: `Program.cs`, WKWebView host, `VideoLAN.LibVLC.Mac` (x64) / extracted dylib (ARM64). |
| `CCP.Avalonia.Android` | `MainActivity.cs`, Android lifecycle, mobile seam implementations (`Mobile*.cs`), reduced feature set. |
| `CCP.WindowsOnly` | Win32 P/Invoke helpers, WebView2 host, NAudio implementation, DWM chrome (WPF/WinForms). Referenced for Windows parity; some implementations now live directly in the Windows desktop head. |

### 3.3 Platform Seams (Interfaces)

The seam set lives in `CCP.Core/Platform/` as **26 interface files** — that is the authoritative list. The table
below is the *original design intent*; a few entries were consolidated or deferred during implementation, so do
**not** recreate them blindly:

| Interface | Replaces |
|---|---|
| `IVideoSurface` | `LibVLCSharp.WPF.VideoView` |
| `IAudioPlayer` / `IAudioDeviceService` / `ISystemAudioDucker` | NAudio/WASAPI |
| `IOverlaySurface` / `IWindowChrome` | Win32 DWM, layered windows, z-order (click-through — §7.4) |
| `IHotkeyProvider` / `IInputHook` | `RegisterHotKey`, `SetWindowsHookEx` |
| `ITrayIcon` | `NotifyIcon` |
| `IBrowserHost` | IMPLEMENTED 11-member in-app WebView seam (corrected 2026-07-10; was wrongly "planned, not yet implemented"): `WebView2BrowserHost` (Windows) / `WebKitBrowserHost` (macOS) / `WebKitGtkBrowserHost` (Linux, stub) / `MobileBrowserHost` (Android); hosts the Chaos three.js tunnel (`ChaosTunnelService`) — DTRH epic context on board row #6 |
| `ISecretStore` | DPAPI |
| `IWallpaperProvider` | `SystemParametersInfo` wallpaper |
| `IUpdateInstaller` | Inno Setup updater |
| `IFrameSource` | GDI desktop capture (also folds the old `ICaptureService`) |
| `IAssetLoader` | `BitmapImage`, `pack://` URIs (folds the old `IImageDecoder`/`IImageSourceFactory`) |
| `IScreenInfo` | `System.Windows.Forms.Screen` (declared in `IScreenInfo.cs`) |

**Consolidated/deferred — do NOT create:** `ICaptureService` → folded into `IFrameSource`; `IImageDecoder` /
`IImageSourceFactory` → folded into `IAssetLoader`; `IUiTimer` → folded into framework `DispatcherTimer`;
`IThumbnailProvider` → deferred (future seam, not existing).

**Interfaces added during implementation (not in the original table):** `IAppEnvironment`, `IDialogService`,
`IFilePickerService`, `IHapticsService`, `ILockdownService`, `IPlatformCapabilities`, `IRemoteControlService`.

**Removed as needless wrappers (ponytail-audit — don't recreate):** `IUiDispatcher` and `IScheduler` (one-line
wrappers over `Dispatcher.UIThread` / `DispatcherTimer` — call the framework APIs directly); custom `IAppLogger`
(→ `ILogger<T>`); hand-rolled `LibVLCNativeDiscovery` (→ LibVLCSharp's own discovery); the `AvaloniaFrameSource`
throw-stub (register `IFrameSource` only where actually implemented).

> **A seam earns its place only with a real cross-platform divergence + a real implementation — not indirection
> for its own sake.** When you genuinely need one, add the interface + a safe shared fallback in
> `ConfigureCoreServices` first (§21 per-head DI pattern), then implement it per head.

---

## 4. Avalonia UI v12 Migration (reference)

> Avalonia v12 is brand-new (2026); LLM training data about it is stale or actively wrong. Invoke the
> `avalonia-research` skill before ANY Avalonia API use, new dependency, or unexplained exception. The official
> docs (§23) are canonical; if this plan and the docs disagree, the docs win.

### 4.1 Package Map

Core Avalonia packages (desktop heads): `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`,
`Avalonia.Fonts.Inter`, `Avalonia.Skia`, `Avalonia.HarfBuzz`, and `Avalonia.Diagnostics`
(`Debug`-conditional — see §21). Android head: `Avalonia.Android`. Replacement map: `MahApps.Metro` →
`Avalonia.Themes.Fluent`/`Simple`; `MahApps.Metro.IconPacks` → `Material.Icons.Avalonia`/custom SVG;
`Hardcodet.NotifyIcon.Wpf` → built-in `TrayIcon`+`NativeMenu`; `XamlAnimatedGif` → `AvaloniaGif`/SkiaSharp
animation; `SharpVectors` → `Svg.Skia`/`Avalonia.Svg.Skia`; WPF `DataGrid` → `Avalonia.Controls.DataGrid`;
WPF `Behavior`/`Interaction` → `Avalonia.Xaml.Interactions`.

### 4.2 Major v12 Changes Affecting the Migration

| Feature | Change | Impact |
|---|---|---|
| Compiled bindings | Enabled by default (`AvaloniaUseCompiledBindingsByDefault=true`) | Every XAML binding needs `x:DataType`; use `{ReflectionBinding ...}` only for dynamic paths. |
| `IBinding` removed | Use `BindingBase` | Custom markup extensions (e.g. `{loc:Str}`) must be rewritten. |
| Clipboard / drag-drop | `IDataObject` removed | File-open handoff and drag-drop move to async typed APIs. |
| `SystemDecorations` renamed | Now `WindowDecorations` | Custom chrome reimplemented against the v12 name. |
| Window state from styles | Cannot set `WindowState` from styles | Set in code or initialization only. |
| Dispatcher model | `Dispatcher.CurrentDispatcher`, `Yield`, `Resume` added | WPF-like dispatcher code is easier; timers must be created on the intended UI thread. |
| `Avalonia.Diagnostics` | Removed from core | Use the `Avalonia.Diagnostics` package and `AttachDeveloperTools()`. |
| .NET Standard dropped | No `netstandard2.0` assets | Class libraries target `net8.0`/`net10.0`. |

### 4.3 XAML Namespace & File Changes

`.xaml` → `.axaml`; `xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"` →
`xmlns="https://github.com/avaloniaui"`; `Page`/`Frame` → none by default; `ResourceDictionary.MergedDictionaries`
→ `Application.Styles`/`ResourceDictionary`; `pack://application:,,,/...` → `avares://CCP.Avalonia/...`;
`<Resource Include="..." />` → `<AvaloniaResource Include="..." />`. `DynamicResource`/`StaticResource` keep
their names but lookup semantics differ.

### 4.4 Control Replacements

| WPF Pattern | Avalonia Equivalent |
|---|---|
| `WindowChrome` custom chrome | `ExtendClientAreaToDecorationsHint` + `ExtendClientAreaChromeHints` + `WindowDecorations` (v12 name; `SystemDecorations` no longer exists) |
| `WindowStyle="None" AllowsTransparency="True"` | `WindowDecorations="None"`, `TransparencyLevelHint="Transparent"`, `Background="Transparent"`. In code, `TransparencyLevelHint` is an `IReadOnlyList<WindowTransparencyLevel>` → set `new[] { WindowTransparencyLevel.Transparent }`. |
| `Topmost`, `ShowInTaskbar`, `ResizeMode`, `WindowState` | Similar properties; behavior varies on Linux/macOS WMs |
| `Viewbox Stretch="Fill"` | Avalonia `Viewbox` supports `Stretch`; test HiDPI |
| WPF `Style`, `Trigger`, `DataTrigger`, `EventSetter`, `Storyboard` | Avalonia style selectors (`:pointerover`, `:checked`) + `Avalonia.Animation` |
| `FocusVisualStyle`, `Cursor="Hand"` | `:focus` selector, `Cursor` enum |
| `CommandBinding`, `RoutedCommand`, `InputBinding` | Avalonia commands/bindings; routed-event model differs |
| `System.Windows.Shapes` | `Avalonia.Controls.Shapes` |
| `System.Windows.Media.Effects.DropShadowEffect` | `BoxShadow` |
| `System.Windows.Media.Imaging.BitmapImage` / `WriteableBitmap` | `Avalonia.Media.Imaging.Bitmap` / `WriteableBitmap` |
| `Visibility` (`Visible`/`Collapsed`/`Hidden`) | **`IsVisible`** (bool) covers Visible/Collapsed; for WPF `Hidden` (invisible but occupies layout) use `Opacity="0"` |
| `ListView` + `GridView` | `ListBox` + `ItemTemplate` (or `DataGrid` for columns) |
| `HierarchicalDataTemplate` | `TreeDataTemplate` |
| `LayoutTransform` | wrap the child in `LayoutTransformControl` |
| `DataTemplateSelector` | a `DataTemplate` with `DataType` matching (interface/derived-type aware) — no selector class needed |

Mappings are confirmed against the official **WPF → Avalonia cheat sheet** (§23). **Layout quick-wins:**
`StackPanel` has a `Spacing` property (drop per-child margins); use inline `ColumnDefinitions="Auto,*,200"` /
`RowDefinitions="…"`; prefer a bare `Panel` over a defs-less `Grid` for pure layering (lighter; sidesteps the
WPF layering/airspace hacks in §14.1).

### 4.5 Dispatcher & Threading

`Application.Current.Dispatcher.BeginInvoke(...)` → `Avalonia.Threading.Dispatcher.UIThread.Post(...)`;
`Dispatcher.Invoke(...)` → `Dispatcher.UIThread.Invoke(...)`; `DispatcherTimer` →
`Avalonia.Threading.DispatcherTimer`; `DispatcherPriority` → `Avalonia.Threading.DispatcherPriority`.

### 4.6 Localization Markup Extension

Done as `StrExtension` in `CCP.Avalonia/Localization/LocExtension.cs`, used in XAML as `{loc:Str btn_cancel}`.
Its `ProvideValue` returns a `OneWay` `Binding` to `LocalizationManager.Instance[Key]`, so strings update live
on a language change. Register with
`xmlns:loc="clr-namespace:ConditioningControlPanel.Avalonia.Localization;assembly=CCP.Avalonia"`. New views must
use `{loc:Str …}` rather than hard-coded text.

### 4.7 Binding Syntax & Custom Properties

| WPF | Avalonia |
|---|---|
| `{Binding Prop, ElementName=foo}` | `{Binding #foo.Prop}` |
| `{Binding Prop, RelativeSource={RelativeSource AncestorType=Grid}}` | `{Binding $parent[Grid].Prop}` |
| `{Binding Prop, RelativeSource={RelativeSource Self}}` | `{Binding $self.Prop}` |
| `{Binding Prop, RelativeSource={RelativeSource TemplatedParent}}` | `{TemplateBinding Prop}` (or `$parent[ControlType]`) |
| `{Binding}` against untyped `DataContext` | add `x:DataType`, or `{ReflectionBinding}` for dynamic paths |
| `DependencyProperty.Register(...)` (stylable/animatable) | `StyledProperty<T>` via `AvaloniaProperty.Register<TOwner, T>(...)` |
| `DependencyProperty.Register(...)` (fast, CLR-backed) | `DirectProperty<TOwner, T>` via `AvaloniaProperty.RegisterDirect<TOwner, T>(...)` |
| `RegisterAttached(...)` | `AvaloniaProperty.RegisterAttached<TOwner, THost, T>(...)` |
| `PropertyChangedCallback` | override `OnPropertyChanged(...)` or subscribe via `.Changed` |

### 4.8 Events & Input

The original app is **extremely event-heavy in code-behind** (`MouseLeftButtonDown` ~138 files, `MouseEnter`
~32, `MouseMove` ~14, `PreviewKeyDown` ~9) — one of the largest mechanical surfaces of the port. WPF mouse
events become **pointer** events (Avalonia also supports touch/pen — matters for Android); there are **no
`Preview*` events** — opt into the tunnel phase explicitly.

| WPF | Avalonia |
|---|---|
| `MouseLeftButtonDown` / `…ButtonUp` | `PointerPressed` / `PointerReleased` — read the button from `e.GetCurrentPoint(ctl).Properties` (`PointerUpdateKind` / `IsLeftButtonPressed`) |
| `MouseMove` | `PointerMoved` |
| `MouseWheel` | `PointerWheelChanged` |
| `MouseEnter` / `MouseLeave` | `PointerEntered` / `PointerExited` |
| `Preview*` tunneling events | no `Preview*`; `AddHandler(InputElement.KeyDownEvent, h, RoutingStrategies.Tunnel)` (combine `Tunnel \| Bubble` for both phases) |
| `EventManager.RegisterRoutedEvent(...)` | `RoutedEvent.Register<TOwner, TArgs>("Name", RoutingStrategies.Bubble)` |
| `AddHandler(evt, h, handledEventsToo: true)` | `AddHandler(evt, h, RoutingStrategies.Bubble, handledEventsToo: true)` |

> **Watch out:** the "which button" check moves *into* the `PointerPressed` args, so a blind
> `MouseLeftButtonDown` → `PointerPressed` rename silently drops the left-button filter. Audit every handler and
> add the `PointerUpdateKind`/`IsLeftButtonPressed` check — especially in drag/click-heavy code (AvatarTube,
> Chaos overlays, BlinkTrainer, bubble minigames).

---

## 5. LibVLCSharp Cross-Platform Media Migration

### 5.1 Package Changes

Remove `LibVLCSharp.WPF`. **Do NOT remove `Microsoft.WindowsAppSDK`** — LibVLCSharp pulls it in transitively, and
leaving it unpinned causes a WebView2 **`NU1605` version-downgrade** build error. Pin and neutralize it in
`CCP.Avalonia` and the Linux/macOS heads:

```xml
<PackageReference Include="Microsoft.WindowsAppSDK" Version="1.8.251106002" ExcludeAssets="all" PrivateAssets="all" />
```

On the Windows head also set `<WebView2EnableCsWinRTProjection>false</WebView2EnableCsWinRTProjection>` so the
managed WinForms WebView2 control (not the WinRT projection) is used. Add managed
`LibVLCSharp` + `LibVLCSharp.Avalonia` (3.9.7.1). Native engine per head: Windows `VideoLAN.LibVLC.Windows`
3.0.23.1 (or `.GPL`); macOS x64 `VideoLAN.LibVLC.Mac` 3.1.3.1, ARM64 manual (extract `libvlc.dylib` + plugins
from VLC.app); Linux **no official NuGet** — system `libvlc`/`libvlccore` or custom `.so`; Android
`VideoLAN.LibVLC.Android` 3.6.5/3.7.0-beta + `LibVLCSharp.Android.AWindowModern`. `LibVLCSharp.Avalonia`
officially supports Windows/macOS/Linux; Android uses the platform-specific surface.

### 5.2 VideoView Migration

```xml
xmlns:vlc="using:LibVLCSharp.Avalonia"
<vlc:VideoView x:Name="VideoView" />   <!-- MediaPlayer bound or set in code-behind -->
```

```csharp
var player = new MediaPlayer(libVLC);
videoView.MediaPlayer = player;
player.Play(media);
```

### 5.3 Memory-Render Surfaces

`DualMonitorVideoService` / `InlineLoopVideo` use LibVLC memory callbacks (`SetVideoCallbacks`/`SetVideoFormat`
with `RV32`) and WPF `WriteableBitmap`. Avalonia equivalent uses `Avalonia.Media.Imaging.WriteableBitmap` with
per-frame invalidation from a ~16 ms `DispatcherTimer` (what
`CCP.Avalonia/Services/Video/AvaloniaDualMonitorVideoService.cs` does) or `TopLevel.RequestAnimationFrame`.
**Do not use WPF's `CompositionTarget.Rendering`** — it does not exist in Avalonia.

### 5.4 Native Library Packaging

Reference per-RID native packages and let NuGet/build place them in output. For `PublishSingleFile`, mark native
libs `<ExcludeFromSingleFile>true</ExcludeFromSingleFile>`. Gate the manual `CopyLibVLCAfterPublish` target to
Windows only. For Linux/macOS use explicit `Core.Initialize(path)` derived from `AppContext.BaseDirectory` + RID.

### 5.5 Audio Abstraction

NAudio is Windows-only. Define `IAudioPlayer`, `IAudioCapture`, `IAudioDeviceEnumerator`,
`ISystemAudioDucker`. On Windows keep NAudio behind the interface; on Linux/macOS/mobile use LibVLC for simple
playback, `ManagedBass`/`Bass.Net` for lower-level mixing, `OpenAL`/`Silk.NET.OpenAL` for playback/capture, and
platform APIs for ducking (PulseAudio/PipeWire on Linux, CoreAudio on macOS, AudioManager on Android).

### 5.6 WebView2 Video in Deeper

`Views/Deeper/EnhancementPlayerWindow.xaml` used WebView2 for video. Replace with the same LibVLC `VideoView`
used everywhere else, unifying the video stack.

---

## 6. Dependency Migration Matrix

| Package | Action | Replacement / Notes |
|---|---|---|
| `Buttplug` / `.WebsocketConnector` | Keep | Test on Linux/mac; likely desktop-only. |
| `CommunityToolkit.Mvvm` | Keep | Fully portable. |
| `DiscordRichPresence` | Keep | Cross-platform transport. |
| `Hardcodet.NotifyIcon.Wpf` | Remove | Avalonia `TrayIcon`. |
| `LibVLCSharp.WPF` | Remove | `LibVLCSharp` + `LibVLCSharp.Avalonia`. |
| `MahApps.Metro` / `.IconPacks` | Remove | Avalonia Fluent/Simple + `Material.Icons.Avalonia`. |
| `Microsoft.ML.OnnxRuntime` | Keep + add runtimes | Add Linux/mac/mobile runtime packages. |
| `Microsoft.Web.WebView2` | Remove from shared | Windows head only; abstract `IBrowserHost`. |
| `Microsoft.WindowsAppSDK` | **Pin, don't remove** | Transitive via LibVLCSharp; `ExcludeAssets="all" PrivateAssets="all"` prevents a WebView2 `NU1605` downgrade. Present in `CCP.Avalonia` + Linux/macOS heads. |
| `NAudio` / `.Wasapi` | Abstract | `IAudioPlayer`; LibVLC/ManagedBass/OpenAL. |
| `Newtonsoft.Json` | Keep | Portable. |
| `OpenCvSharp4` / `.runtime.win` | Keep + add runtimes | Linux/mac/mobile native runtimes; `.runtime.win` moves to Windows head. |
| `QRCoder` | Keep | Portable. |
| `Serilog` + sinks | Keep | Portable. |
| `SharpVectors` | Remove | `Svg.Skia` / `Avalonia.Svg.Skia`. |
| `System.Security.Cryptography.ProtectedData` | Abstract | `ISecretStore` (DPAPI/Keychain/libsecret). |
| `VideoLAN.LibVLC.Windows` | Keep + add runtimes | Mac/Android packages; Linux via system or custom. |
| `XamlAnimatedGif` | Remove | `AvaloniaGif` or custom frame animation. |

(`SharpDX.*` + `OpenAI-DotNet` removed — zero references; `OllamaSharp` still lingers UNUSED in the legacy WPF head only, `ConditioningControlPanel.csproj:100` — corrected 2026-07-10.)

---

## 7. Subsystem Migration Plan

> Per-region click-through (§7.4) is the canonical, verbatim spec. The rest of this section is condensed
> reference; live per-feature status is the parity matrix, live work is the task board.

### 7.1 Application Bootstrap

Avalonia `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)` on desktop; platform lifecycle for Android;
cross-platform single-instance via file lock or platform-specific service; replace
`Environment.SpecialFolder.LocalApplicationData` assumptions with proper paths (`XDG_DATA_HOME` on Linux,
`~/Library/Application Support` on macOS); `MessageBox.Show` → `IDialogService`. **Implemented** as
`AvaloniaDialogService` (`CCP.Avalonia/Platform/AvaloniaDialogService.cs`) on the **`MessageBox.Avalonia`**
NuGet package (package id `MessageBox.Avalonia`; API namespace `MsBox.Avalonia`, e.g. `MessageBoxManager`).

### 7.2 System Tray

Avalonia built-in `TrayIcon` + `NativeMenu` in the desktop head. Not applicable on mobile.

### 7.3 Global Input Hooks

Abstract `IHotkeyProvider` / `IInputHook`. Windows keeps Win32 (`SetWindowsHookEx`, `RegisterHotKey`,
`GetAsyncKeyState`). Linux uses X11/Wayland evdev (see Linux catalogue). macOS uses `CGEventTap` + accessibility
permission. Mobile: impossible; disable. Lockdown mode system-key suppression (`Alt+Tab`, `Win`, `Esc`,
`Ctrl+Shift+Esc`) is **impossible on macOS/Android** and requires root/udev on Linux.

### 7.4 Window Chrome / Overlays

Current: `WindowChrome`, `dwmapi.dll` for dark title bars, `SetWindowLong`/`SetWindowPos` for tool windows and z-order, `AllowsTransparency` for layered overlays.

Target:
- Avalonia `WindowDecorations="None"`, `TransparencyLevelHint="Transparent"`, `Topmost="True"` (note: `SystemDecorations` was renamed to `WindowDecorations` in v12 — see §4.2).
- Click-through/input passthrough requires platform-specific code on Linux/macOS.
- DWM tinting is Windows-only; use Avalonia client-side decorations for cross-platform chrome.

**Overlays use PER-REGION click-through (team review 2026-07-09 — SUPERSEDES the old "all overlays are pure
passive click-through" spec).** Only the **theme color filter (pink/color tint)** and the **spiral** are ambient
*tinted glass*: a screen region covered by **only** those two layers is paint-only — you *see* it (rendered
smoothly) but can click, type, and use your whole PC normally through it. **Every other active layer** (video,
flash, subliminal, brain-drain, bouncing text, keyword highlight, bubbles, chaos FX) **captures pointer input over
the region it paints** while active. The window mechanism is unchanged — the per-monitor CompositorWindow stays
`WS_EX_TRANSPARENT|WS_EX_LAYERED`; the compositor derives a per-frame **capture mask** = union of the non-ambient
active layers' painted regions (immutable snapshot), and the global mouse hook **swallows** clicks inside the mask
and passes the rest. For a region that is ambient-only (color filter and/or spiral), the overlay must:
- **not capture input** — mouse and keyboard pass straight through to whatever is underneath (the app's own
  buttons *and* other applications);
- **not steal focus or activate** — the focused window stays focused; the overlay never becomes foreground;
- **not appear in Alt-Tab / the task switcher**, and not show in the taskbar;
- **not interfere with the behavior or performance** of apps behind it (no input grabs, no global hooks tied to the
  overlay, minimal GPU/CPU cost — see the perf bar in §1A);
- **render its own visual smoothly** — the ambient layers (spiral, color tint) spin/pulse at a fluid frame rate
  while their ambient-only regions stay click-through. "See the ambient glass smoothly and use your PC through it;
  every other effect owns (captures) its painted region" is the requirement.

> **Implementation status (2026-07-10, annotation — the spec above is verbatim):** the per-region
> capture mask + hook swallow are TARGET scope (board row #1), not yet implemented. Today the
> per-monitor `CompositorWindow` is globally `WS_EX_TRANSPARENT` (all-or-nothing click-through,
> `CompositorWindow.axaml.cs:107`) and `AvaloniaMouseHook` passes all clicks (`AvaloniaMouseHook.cs:156-159`).

This needs input-transparency at **both** levels, and the reported bug is shipping only one:
1. **Avalonia level:** the overlay's content/root must be `IsHitTestVisible="false"` so clicks fall through to the
   app's own controls beneath it.
2. **OS window level:** the overlay's top-level window must be made click-through so clicks reach *other apps*. On
   Windows that is `WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE` on the window handle, applied **after the
   handle exists** (e.g. on `Opened`/`HandleCreated`) — exactly what `IOverlaySurface`'s Windows implementation
   (`WindowsOverlaySurface` / `AvaloniaOverlayService`) already does. **Every** color/effect overlay must route
   through that path; the reported failure is that some (pink fill, spiral) bypass it or apply it to the wrong
   window/too early.
- Linux/macOS: input passthrough is compositor/native-specific — gate as desktop-only and degrade gracefully
  (don't ship an overlay that traps input).
- **Verify** by clicking the app's buttons *through* an active overlay, and by clicking a second app placed behind
  the overlay. See §13.6.

### 7.5 Screen / Monitor APIs

Avalonia `Window.Screens` / `TopLevel.GetTopLevel(this).Screens`; abstract `IScreenInfo` for headless Core.
Display mirroring (`SetDisplayConfig`) has no cross-platform API — gate Windows-only. **Multi-monitor (N screens)
— REQUIRED, not "dual".** Treat "dual monitor" as a special case of arbitrary N monitors. Every screen-spanning
feature (mandatory video, flash, subliminal, spiral, pink fill, brain-drain, bouncing text, bubbles, Chaos
overlays, mind-wipe) must render across **all** monitors at once **unless configured to a single display**. Rules
(data is on `AvaloniaScreenProvider`: per-monitor `Bounds`, `WorkingArea`, `Scaling`):
- **Iterate `Screens.All`, never `[0]`/`[1]`** — no hard-coded primary+secondary; generalize
  `AvaloniaDualMonitorVideoService` to N (one surface per target monitor).
- **Size each surface to its own monitor** (`M.Bounds` position + size) — a portrait monitor (Height > Width) and
  a landscape monitor each get the right aspect automatically.
- **Scale per-monitor** — use each monitor's own `Scaling`; allocate frame buffers/`WriteableBitmap` per target
  surface; letterbox/scale video to each monitor's aspect rather than stretching.
- **Optimize** — spawn a surface only on targeted monitors; pool/reuse surfaces; one decoder where the same frame
  is mirrored, blitted per surface; don't allocate unneeded full-screen layered windows (§14.1 heap risk).
- **React to display changes at runtime** — handle hotplug/resolution/orientation (`Screens.Changed`).

### 7.6 Desktop Wallpaper

Abstract `IWallpaperProvider`. Windows only. macOS AppleScript/NSWorkspace; Linux `gsettings`/`feh` per DE —
degrade with a recorded gap.

### 7.7 Embedded Browser

Introduce `IBrowserHost`. Options: Avalonia.Controls.WebView (WebView2 on Windows, WPE WebKit on Linux, Android
WebView; macOS WKWebView); CEF wrapper (`CefGlue.Avalonia`/`CefNet.Avalonia`); system browser via
`xdg-open`/`open`. Keep WebView2 in `CCP.WindowsOnly` for Windows parity. (The DTRH web roguelite epic —
dollhouse rewrite, web-only per the 2026-07-10 owner ruling — is NOT seam-blocked on Windows: the seam +
`WebView2BrowserHost` exist; only the LINUX leg waits on a real `WebKitGtkBrowserHost` — board row #6.)

### 7.8 Imaging / Computer Vision

Add platform runtimes: `OpenCvSharp4.runtime.ubuntu.*`, `.osx.*`; mobile uses Android Camera2 APIs. Replace
DirectShow/WinRT enumerators with V4L2 (Linux) / AVFoundation (macOS). Add ONNX Runtime mobile runtimes. Replace
`System.Drawing` with SkiaSharp/ImageSharp.

### 7.9 Secure Storage

`ISecretStore`: Windows DPAPI (keep); macOS Keychain (`Security` framework); Linux libsecret/secret-tool or
encrypted file with user-only perms. Existing encrypted tokens will not decrypt on other OSs — plan
re-authentication or migration.

### 7.10 File Dialogs

Avalonia `IStorageProvider` (`OpenFilePickerAsync`, `SaveFilePickerAsync`, `OpenFolderPickerAsync`).

### 7.11 Updates

`IUpdateInstaller`: Windows keep installer path discovery (without `wmic`); macOS Sparkle or manual DMG; Linux
AppImage/snap/flatpak. (Update-restart #499 is N/A until an Avalonia installer — board row.)

---

## Linux Bring-Up Mechanism Catalogue (WS4)

> This is the mechanism detail for board epic #5 / WS4. The board is the **tracker** (claim rows there); this
> section is the **mechanism reference**. The Linux head currently builds and launches in a VM but has **ZERO
> click-through code** (`SupportsClickThrough = IsWindows`), no input hooks, and no verified feature sweep
> (~45% overall). Every mechanism below maps to **work on Windows / work-or-degrade-with-recorded-gap on Linux**
> per the spirit: **Windows never degrades to enable Linux; Linux degrades gracefully where the platform genuinely
> cannot do a thing.** Use the `avalonia-research` skill (Linux-specific mechanisms) and the `overlay-clickthrough`
> skill (Linux click-through design) before implementing any of these. Verification runbook:
> [`linux-vm-testing.md`](linux-vm-testing.md).

| Mechanism | Windows (reference) | Linux approach | Work-or-degrade |
|---|---|---|---|
| **Overlay click-through** | `WS_EX_TRANSPARENT\|LAYERED\|NOACTIVATE` via `IOverlaySurface.SetClickThrough` (§7.4) | **X11: `XShapeCombineRectangles` + XFixes input region** — derive the per-frame capture mask (non-ambient active layers' painted regions) and punch an X11 input region that passes clicks in ambient-only regions and captures elsewhere; route through `IOverlaySurface.SetClickThrough`. **Wayland: best-effort** — no generic input-region API; layer-shell where available, else degrade (recorded gap). | Work on X11; degrade with recorded gap on Wayland. The `AvaloniaMouseHook` click-swallow is Windows-only today — the Linux path needs an equivalent input-capture mechanism (XGrab/XFixes) or a recorded graceful degrade. |
| **Global mouse hook** | `SetWindowsHookEx` WH_MOUSE_LL (swallows clicks in the capture mask) | **evdev** (root/udev, `/dev/input/event*`), **XInput2** (`XIQueryPointer`, passive grabs), or **XRecord** (record extension; read-only). | Degrade: without root, global mouse capture is limited — record the gap; the per-region mask may need a XInput2-grab fallback instead of a true hook. |
| **Global keyboard hook / hotkeys** | `SetWindowsHookEx` WH_KEYBOARD_LL, `RegisterHotKey` | X11 grab keys (`XGrabKey`); evdev for low-level. | Work for hotkeys; **system-key suppression (lockdown) requires root/udev** — degrade with a recorded gap. |
| **Native video engine** | `VideoLAN.LibVLC.Windows` NuGet | **System `libvlc`** — install `libvlc-dev`/`libvlccore-dev`/`vlc` (CI does this; `build-linux.sh` installs it); `Core.Initialize(path)` from `AppContext.BaseDirectory`. | Work (system package). macOS ARM64 still needs a manual dylib from VLC.app. |
| **Embedded browser (`IBrowserHost`)** | WebView2 | **WebKitGTK** (`webkit2gtk-4.1`) via a CEF/WebKit binding, or **system browser** `xdg-open`. | Work (WebKitGTK) or degrade (system browser launch). Blocks only the DTRH epic's LINUX leg (corrected 2026-07-10: the `IBrowserHost` seam + Windows `WebView2BrowserHost` exist and host the Chaos tunnel; the Linux `WebKitGtkBrowserHost` is the remaining stub — `CreateBrowserControl()` returns null, navigation shells out to `xdg-open`). |
| **Audio ducking (`ISystemAudioDucker`)** | NAudio/WASAPI (`WindowsSystemAudioDucker`) | **PulseAudio/PipeWire** (`pactl` load-module/module-role-ducking, or PipeWire session manager). | Best-effort (`pactl`/`osascript`-equivalent); degrade with a recorded gap where the DE lacks a ducking API. |
| **Desktop wallpaper (`IWallpaperProvider`)** | `SystemParametersInfo` (`SPI_SETDESKWALLPAPER`) | `gsettings` (GNOME), `feh`/`nitrogen` (X11), or **layer-shell** overlay under Wayland. | Per-DE; degrade with a recorded gap where no API exists. |
| **Tray icon** | Avalonia `TrayIcon` | `StatusNotifierItem`/AppIndicator (needs `libdbusmenu` on some distros). | Work, with per-distro dependency. |
| **Window transparency / topmost** | `TransparencyLevelHint="Transparent"`, `Topmost` | X11 compositor-dependent; Wayland layer-shell where available. | Compositor-specific; may be limited — degrade gracefully (don't ship an overlay that traps input). |
| **Secrets (`ISecretStore`)** | DPAPI | **libsecret**/`secret-tool`, or encrypted file with user-only perms. | Work (libsecret) or degrade (encrypted file). Existing DPAPI tokens won't decrypt — re-auth. |
| **File dialogs** | `IStorageProvider` | `IStorageProvider` (GTK backend). | Work. |
| **GDI/desktop capture (`IFrameSource`)** | GDI `BitBlt` | X11 `XGetImage`/XComposite + XDamage, or PipeWire screen-cast (Wayland portal). | Degrade with a recorded gap; webcam privacy contract stays (frames never disk/network). |

**Linux sweep** (MECHANICAL, board epic #5 sweep half): once the mechanisms above are in place, run the full
per-feature sweep per `linux-vm-testing.md` and record each feature's Linux status into the parity matrix's Linux
section (every row starts `[ ] linux`; the sweep earns them). Run inside the VM via `./build-linux.sh`
(from `ConditioningControlPanel/`).

---

## 9. Platform-Specific Deployment Notes

- **Windows:** keep many Win32 features behind `CCP.WindowsOnly`; retain WebView2, global hooks, DWM tinting,
  wallpaper override, NAudio; `net8.0-windows10.0.19041.0`; single-file publish with native libs excluded.
- **Linux:** no official `VideoLAN.LibVLC.Linux` NuGet (system `libvlc` or bundled `.so`); tray via
  `StatusNotifierItem`/AppIndicator (`libdbusmenu` on some distros); window transparency/click-through depend on
  compositor (X11/Wayland) — may be limited; global hooks not reliable (lockdown degrades gracefully); `xdg-open`
  for system browser fallback.
- **macOS:** `VideoLAN.LibVLC.Mac` for x64; ARM64 ships a custom `libvlc.dylib` + plugins from VLC.app; title-bar
  theming and transparent overlays need native NSWindow interop; global hotkeys via
  `NSEvent.AddGlobalMonitorForEventsMatchingMask` (system-key suppression not possible); Keychain for secrets.
- **Android:** `Avalonia.Android` + `MainActivity`; no tray/global hooks/wallpaper; `VideoLAN.LibVLC.Android`;
  camera/ML via native bindings or platform APIs. (Feature work out of scope for the current goal; builds green.)

---

## 13. Build & Test Strategy

Every phase ends with a **build checkpoint** and a **test checkpoint**; never a "big bang" integration — the WPF
app stays runnable until the Avalonia desktop app fully replaces it. **The binding gates block EVERY commit**
(copy-paste block in `skia-rebuild-goal.md`): slnf build 0 errors; WPF sln build 0 errors; Core tests all pass
with the count NEVER decreasing (floor 542/542, verified live 2026-07-10); `--smoke-test` → 44 tabs + 0 unhandled + findings ⊆ the recorded benign drift set (task-board smoke-drift row; logged-out baseline = Findings 5);
`--verify-layers`/`--verify-video` when touching compositor/video; `--benchmark`/`--max-benchmark` before/after on
hot paths — not worse than `benchmark-optimized.json` (re-baseline caveat: board row #2).

### 13.1–13.3 Testing pyramid & checkpoints (summary)

Unit (xUnit, `CCP.Core` only — models, parsers, gamification, session state machines, JSON contracts) ·
Integration (service orchestration, AI pipeline, mod loading) · UI/Functional (Avalonia.Headless/manual QA) ·
Performance (startup, memory, effect frame rates, LibVLC callbacks) · Platform (CI matrix on Windows/Linux/macOS
+ simulators). Each phase's build+test checkpoint is: does it build on all target RIDs, do Core tests pass, does
the head launch without crash, does the feature actually work end-to-end (§13.6).

### 13.4 Continuous Quality Gates

Build gate (all heads on Windows/Linux/macOS) · Test gate (Core tests on all three desktop OSs) · Static analysis
(nullable in Core; `CA1416` on, no cross-platform leaks) · **Performance gate:** startup, working-set, and
effect/animation frame rates must **match or beat the WPF baseline** — never regress (the bar is
1:1-or-better). Capture the WPF baseline first, then hold each ported feature to it; treat a regression as a
defect; prefer beating it via §14.2 levers or researching/adopting a faster library. The bar is
**low-end-machine smoothness**, not just dev-box FPS. Accessibility gate: keyboard nav + screen-reader labels for
every new view.

### 13.5 Visual Parity Verification (reference screenshots)

The ported UI must *look* like the original, not just compile. Early ports shipped raw localization keys as text
(`tab_dashboard`, `lbl_…`), missing/unstyled cards, wrong fonts, broken spacing/theme. Verify by eye against
reference images at least once per tab/screen before ✅. Reference assets live in **`img state/`** (quote the path
— it has a space). The dashboard *layout* is shared across themes; the *palette/avatar/card art* differ **per
theme**. There are **five themes**: **CCP Default, Bambi, Sissy Hypno, Droneification, Circe Lock** — compare each
ported screen against the reference for the **matching active theme** (`default good view.jpg`, `good view.png`,
`bambi sleep good view.jpg`, `drone good view.jpg`, `circe lock good view.jpg`; `bad view*.png` are known-wrong
examples). Procedure: run the Avalonia Windows head, screenshot the tab, compare against (a) the reference for the
active theme and (b) the same tab in the running WPF app (the visual + behavioral source of truth — launch it
yourself via `dotnet run --project ConditioningControlPanel.csproj`; don't guess, don't block on the maintainer).
Check: no raw `{loc:Str}` keys leaking; all cards/controls present and themed (accents via DynamicResource, never
hard-coded hex — §15.11); correct grid/spacing/margins; theme switching re-skins the whole UI. Log mismatches as
parity-matrix rows and fix before closing the lane.

### 13.6 Functional / Behavioral Parity (exercise it — don't just render it)

**A tab that *looks* right can still *do* nothing — "builds + looks right" is NOT done.** Real defects from the
first port: START doesn't launch the mode, the left avatar is inert, "Down the Rabbit Hole" progression doesn't
run, color overlays block input instead of being click-through. These pass a build and a screenshot but fail the
user. **The standard is 1:1-or-better:** the ported feature must do *exactly* what the WPF feature does — same
inputs, same outputs, same edge cases — at **equal-or-better performance**. When in doubt, run the WPF app and
match it (§13.5). A stub that "renders but no-ops" is a defect, not a milestone.

Per lane, before ✅: drive the primary action end-to-end (START launches the mode, Save/Exit work, every feature
card toggles **and activates its service**, the avatar reacts, dialogs commit and persist); confirm controls are
bound to live `ICommand`s on the ViewModel that call the Core/platform service (grep for stub/`NotImplemented`);
compare behavior side-by-side with WPF; overlays must be input-transparent per §7.4 (click the app's buttons
*through* an active overlay, and click a second app behind it). Anything inert/input-blocking → a parity-matrix
row or task-board item, fixed before closing the lane. Find more inert UI with
`grep -rinE "TODO|stub|not ported|not wired|placeholder|NotImplemented|No-?op" CCP.Avalonia --include=*.cs` and by
exercising every feature — the markers are a floor, not a ceiling.

---

## 14. Quality & Improvement Goals

The rebuild must leave the codebase faster, more stable, more testable, and more maintainable than the WPF
original.

### 14.1 Stability Improvements

| Pain Point | Root Cause | Migration Fix |
|---|---|---|
| Render-thread deadlocks (Application Hang 1002) | Layered WPF popups + avatar tube share single render thread | Avalonia/Skia render model; separate effect surfaces (UCE layers); remove ComboBox/tooltip layering hacks. |
| GDI/desktop heap quota exhaustion | Too many full-screen layered windows | Pool/reuse overlay surfaces; limit concurrent surfaces; gate heavy effects. (UCE: one topmost window per monitor, layers not windows.) |
| Dispatcher hang crashes | UI thread blocked by synchronous service init | Move all service init off the UI thread; async startup with progress. |
| Cascading crash dialogs | `MessageBox.Show` on failing dispatcher | Async `IDialogService`; no nested dispatcher pumps during error handling. |
| Memory leaks from static services | 88 static service refs in WPF `App.xaml.cs` | Scoped DI container (DONE in Avalonia heads); `IAsyncDisposable`. |
| WPF airspace issues | Native video HWND behind WPF controls | Avalonia `VideoView` integrates into the scene graph; no HWND airspace. |

### 14.2 Performance Improvements

| Area | Current (WPF) | Target |
|---|---|---|
| Startup time | Custom `Main` sync-inits Serilog/services/Patreon | Async startup pipeline; lazy service init; splash with real progress. **Recorded: ~2.0s Avalonia (`benchmark-optimized.json` `MainWindowShownMs` 1976.9). UNVERIFIED (2026-07-10): "~4.2s (WPF)" — evidence gap: no recorded WPF benchmark artifact exists in the repo; re-measure before citing.** |
| UI render thread | Single WPF render thread can deadlock | Avalonia/Skia composition; 60 FPS independent render thread. |
| GIF animation | XamlAnimatedGif decodes on UI thread | SkiaSharp/ImageSharp decode on thread pool; upload frames to GPU. |
| Audio SFX | `WaveOutEvent` per sound can exhaust devices | Pool audio players; LibVLC for short SFX on all platforms. |
| Screen enumeration | `Screen.AllScreens` cached with Win32 | Avalonia `Screens` API with change notifications. |
| Asset loading | `pack://` URI + embedded resources | `avares://` + `AvaloniaResource`; lazy load large assets. |
| Webcam tracking | DirectShow/WinRT + WPF dispatcher | Cross-platform capture abstraction; frame processing on background thread. |

### 14.3 Maintainability Improvements

DI over static locator (DONE) · MVVM (split the 13k-LOC `MainWindow` into small Views + ViewModels) ·
`async/await` with `CancellationToken` + `IAsyncDisposable` · config to `appsettings.json` + options pattern ·
Serilog structured logging with correlation IDs · feature flags for experimental/Windows-only features.

### 14.4 Research & Library Adoption (actively hunt for faster solutions)

**Actively seeking the fastest/lightest approach is a standing behavior, not a last resort.** For every feature —
even one that already works — ask "is there a faster or lighter way?" and **research the web** before settling,
and again whenever a path feels laggy or heavy. The implementer is not limited to prior knowledge: default to the
idiomatic, modern, performant Avalonia v12 way (docs §23, LibVLCSharp, SkiaSharp, GitHub issues, release notes,
benchmarks). Actively adopt new libraries that make the app faster/lighter — this is encouraged and is **not** a
ponytail violation (ponytail forbids needless abstraction, not useful dependencies). Guardrails: the lib must earn
its weight (remove a slow/fragile path or measurably cut the footprint, not save a few lines); prefer
well-maintained, cross-platform, permissively-licensed, actively-released packages; pin versions; keep the dep set
lean; never regress Windows or bloat startup/working-set; record each new/changed dependency + the reason on the
board.

---

## 15. Missed Architectural Concerns

Open items only — completed concerns (static service locator, duplication collapse) are in §1A / §19.4 ledger.

- **15.2 Mod & asset pipeline:** keep `.ccpmod`; extract to the platform-appropriate user data folder; use
  `Path.Combine`/`AppContext.BaseDirectory` consistently (no hard-coded backslashes); mark assets
  `AvaloniaResource`/`Content`; validate mod manifests after extraction.
- **15.3 Settings & data backward compatibility — known bug (board row #L):** read existing WPF settings on first
  launch and migrate; version the schema; back up before migration. **One user-data folder, matching legacy**
  (`%LOCALAPPDATA%\ConditioningControlPanel`, Local). ⚠️ `AvaloniaAppEnvironment.ApplicationDataPath` resolves to
  **Roaming** while `UserDataPath`/legacy use **Local** — session logs/custom sessions/moderation counters land in
  the wrong folder and look lost. Collapse to one Local path (ponytail: don't keep two path properties pointing at
  two folders).
- **15.5 Network/cache/offline:** abstract `IHttpClientFactory`/`IConnectivityService`; cache catalogue/enhancement
  metadata locally with expiration; ensure offline mode works without cloud identity; handle cert/network errors
  on mobile.
- **15.6 Webcam privacy contract (non-negotiable):** frames stay on device, never transmitted, never written to
  disk; document in `CCP.Core`; use platform camera APIs on mobile; never cloud ML; permission handling on all
  platforms. **Never regress.**
- **15.7 AI service thread safety:** make the pipeline fully async with `CancellationToken`; run inference/HTTP on
  the thread pool; marshal results to UI via `Dispatcher.UIThread`; add rate limiting/queueing.
- **15.8 Chaos effects performance:** pool overlay surfaces (UCE layers, not windows); use
  `CompositionCustomVisual`/SkiaSharp for particles/spiral; limit concurrent effects by GPU memory.
- **15.9 File paths & case sensitivity:** `Path.Combine`, `Path.DirectorySeparatorChar`, `StringComparison.Ordinal`
  consistently; test asset loading on case-sensitive Linux FS; normalize locale/emoji filenames before saving.
- **15.10 Single-instance:** abstract `ISingleInstanceService`; Windows named mutex/event; Linux/macOS file lock +
  Unix domain socket/signal file; mobile N/A.
- **15.11 Per-mod theming / dynamic palette (root cause of "UI looks wrong"):** the top-left selector is the **mod
  switcher**; each mod is a theme/skin with its own palette + avatar/card art (CCP Default, Bambi, Sissy Hypno,
  Droneification, Circe Lock). Each `.ccpmod`/manifest carries a `"theme"` block (e.g. Drone `#00FF41` green;
  Sissy Hypno `#FF69B4` pink). Port requirement: reproduce the apply step — a theme applier (`IThemeService` /
  mod-theme bridge) that, on mod change, pushes the active mod's accent set into Avalonia
  `Application.Current.Resources` (the keys in `App.axaml`: `PinkColor`/`DarkPinkColor`/accents + their `*Brush`
  forms). **Views must consume accents via `DynamicResource`, never hard-coded hex** (audit for literal
  `#FF69B4`/`#FF1493`). Current gap (largely closed by swarm hard-coded-hex audits — see parity matrix): ensure no
  residual literal hex remains and the re-skin path fires on every mod switch.

---

## 16. Feature Flags & Gradual Rollout

**Reality:** cross-platform UI ships as **separate executable heads** (`CCP.Avalonia.Desktop.Windows/.Linux/.macOS`,
`CCP.Avalonia.Android`), not a runtime toggle inside the WPF app — so `UseAvaloniaUI` is moot and none of the
proposed runtime flags exist in code. Platform branching is `IPlatformCapabilities` + `OperatingSystem.IsX()` in DI
+ per-head DI overrides. Wire any genuinely-needed runtime toggle through `AppSettings` + `IPlatformCapabilities`.
The rollout *phases* (alpha → desktop beta → mobile beta → GA) are driven by which head you ship, not by a flag.
(macOS/iOS and Android feature work are out of scope for the current goal; their builds stay green.)

---

## 17. Accessibility & Localization

**Accessibility:** set `AutomationProperties.Name`/`HelpText` on every interactive control; ensure keyboard nav
(Tab order, access keys) for every view; test with NVDA (Windows), Orca (Linux), VoiceOver (macOS); respect
system high-contrast and reduce-motion. **Localization:** keep JSON language files (auto-synced into Core per
§19.1); use `{loc:Str …}` (§4.6) for every new XAML string; consider ICU message formatting for pluralization.

---

## 18. Code Signing & Distribution

| Platform | Artifact | Signing / Notarization |
|---|---|---|
| Windows | `win-x64` single-file EXE + MSI/INNO | Code signing cert (EV recommended); sign installer + EXE. |
| Linux | `linux-x64` self-contained folder or AppImage | GPG sign AppImage/package; no OS-level signing. |
| macOS | `osx-x64`/`osx-arm64` app bundle + DMG | Apple Developer ID; `notarytool`; staple ticket. |
| Android | AAB/APK | Upload key + signing key; Google Play App Signing. |

Keep update channels separate per platform; provide SHA256 checksums; host native deps (Linux libvlc, macOS ARM64
libvlc) on a CDN or in release assets.

---

## 19. Mainline Sync & Dual-Maintenance

The WPF app on `main` is **kept runnable and keeps shipping features** during the migration. Because `CCP.Core`
holds models (now the single source of truth — §19.4) and `CCP.Avalonia` holds service reimplementations, every
merge from `main` into `feat/crossplatform` introduces drift that must be triaged by hand.

### 19.1 Auto-synced vs. manual-sync map

| Surface | Sync behaviour |
|---|---|
| `Localization/Languages/*.json` | **Auto-synced.** `CCP.Core.csproj` links them; desktop heads copy them. New keys from `main` appear with no porting. *Caveat:* a new Avalonia view must still reference the new key via `{loc:Str NewKey}`. |
| `Models/*.cs`, JSON DTOs | **Auto (single source).** Since §19.4, WPF references `CCP.Core/Models/` directly — model changes flow to both heads. |
| Portable service logic | **Manual.** Reimplemented under `CCP.Core/Services/*`. |
| WPF UI (`*.xaml(.cs)`, Chaos, AvatarTube, windows) | **Manual,** only when that screen is already ported; otherwise it joins the Phase-4 parity backlog (board). |
| WPF-head-only infra (`installer.iss`, `build-installer.bat`, `App.xaml.cs` plumbing) | **No action** in the cross-platform tree. |

### 19.2 Per-merge triage workflow

1. `git diff --stat <prev-main>..<new-main> -- ConditioningControlPanel/` to list changed files.
2. Bucket each file (Model → auto-flows; portable service → Core; UI → Avalonia-if-ported-else-backlog;
   infra/loc → no action).
3. Port, then `dotnet build CCP.Desktop.slnf -clp:ErrorsOnly` and run `CCP.Core.Tests`.
4. Record anything deferred as a task-board row so it isn't lost.

### 19.3 Sync backlog ledger (compressed; live backlog = task board)

Completed mainline syncs (kept as record; detail rows live on the board):

- **6.1.7 sync (merged 2026-06-23):** Chaos "Down the Rabbit Hole" main menu, quest-pool refresh, auth
  browser-launch fallback, subliminal/avatar fixes, Fredoka font, 20 quest art PNGs. ⚠️ `Quest.cs`/`AppSettings.cs`
  deltas were dropped in merge conflict (modify/delete → kept deleted) and re-applied to Core by hand. (Chaos run
  engine S1–S9 and the DTRH epic are now board rows.)
- **6.1.6 sync (commit `22caaab4`, 2026-06-21):** `AppSettings` new fields; `ChaosSkiaFxOverlay` (ported then
  SUPERSEDED — the Avalonia window was DELETED 2026-07-05; effects render via compositor layers
  `ChaosEStimArcLayer`/`ChaosCursorGlowLayer`/`ChaosFieldFxLayer`); `ChaosBoonColors` + host overlays;
  `ChaosCrashSentinel`; `BubbleService` overhaul (popping minigame → `BubbleEngine` + `AvaloniaBubbleService`);
  `UpdateService` rework → Core `IUpdateService`; Fredoka font.
- **merge `5ce70de6` (2026-07-03, WPF ~6.2.7):** two P0s re-opened then re-closed in WS0 (lot 2 session-ramp
  crash/data-loss; lot 1 `ProfileSyncService` absence); Ditzy Data PRO/Prestige analytics (deferred — board);
  #462 interaction-race cluster (hardened `fb704a6d`); #463/#465/#455 integration lags; animated `.webp` (covered
  by `SkiaImageDecoder` — broaden gates only, no new dep). No modify/delete conflicts on Core models this merge.

### 19.4 Strategic fix — collapse the duplication (completed 2026-06-22)

The WPF head (`ConditioningControlPanel.csproj`) now references `CCP.Core/CCP.Core.csproj` directly; the WPF
`Models/` duplicate folder was deleted (47 files). `CCP.Core/Models/` is the single source of truth for all
model/DTO types. `Microsoft.WindowsAppSDK` pinned in WPF with `ExcludeAssets="all" PrivateAssets="all"` +
`NoWarn="NU1605"`; duplicate type definitions removed; `AppSettings.MigrateFromContentModeToMod()` made `public`;
`LibVLCSharp.Shared.Core.Initialize(...)` qualified in WPF video services. Validated: sln build 0 errors;
Core tests pass; Avalonia smoke clean. **Ongoing:** with WPF `Models/` gone, §19.2 no longer needs to diff
`Models/` — future model changes go in `CCP.Core/Models/` and flow to both heads.

---

## 20. Multi-Agent Execution & Context Discipline

> **Execution model has moved to the pi-dynamic-workflows `workflow` tool** (see `skia-rebuild-goal.md` →
> "Workflow execution model"): `agent()`/`parallel()`/`pipeline()`/`phase()`, journaled resume, git-worktree
> isolation, `verify()`/`judgePanel()` quality patterns; three model tiers (small/MECHANICAL, medium/STANDARD,
> big/JUDGMENT); project agentTypes `wpf-archaeologist`, `port-slice-executor`, `port-parity-auditor`. The lane
> partitioning and conflict-avoidance rules below are still the concrete guidance for HOW work is sliced when
> fanned out. **Nothing is currently in flight** (post-crash reconciliation 2026-07-09); the task board is the
> only claim ledger — append ONE claim row per session, never start an item that already shows a claim.

### 20.1 Parallelization model — lanes vs. chokepoints

The unit of parallel work is a **lane** (a directory subtree one agent owns end-to-end). A small set of
shared/serial files are **chokepoints** owned by a single integrator, never edited by porters directly.

**Parallel-safe lanes (high fan-out):** one tab (view + VM); a dialog cluster; a feature-control cluster; Chaos
overlays; AvatarTube; per-head platform seams (separate projects); one Core service area.

**Serial chokepoints (integrator-only):** `CCP.Avalonia/ServiceCollectionExtensions.cs` (highest contention);
`CCP.Core/Models/AppSettings.cs` and other large single files; `App.axaml`/`App.axaml.cs`; any `*.csproj` and
`*.slnx`/`*.slnf`; the `MainWindow` shell; `Localization/Languages/*.json` (JSON merges conflict badly);
`main`→`feat/crossplatform` syncs (§19, one owner per merge); tracker docs (append-mostly; orchestrator
reconciles between waves).

### 20.2 Roles

- **Orchestrator (1):** partitions into lanes, assigns, owns every chokepoint (DI, csproj, `App.axaml`,
  `MainWindow`, loc merge), runs integration build/test between waves, performs §19 syncs. Holds no porting lane.
- **Porters (N, parallel):** each takes one lane, ports end-to-end, reports files changed + the one-line DI
  registration + new loc keys + parity notes. Never touch chokepoints.
- **Verifier (1+):** runs the parity matrix and build/test matrix per platform; files defects as new tracker
  items rather than fixing in place.

### 20.3 Isolation — one worktree per agent

Give every porter its own git worktree/branch so working trees never collide; integration happens at merge time.
Each worktree builds independently with `dotnet build CCP.Desktop.slnf -clp:ErrorsOnly`. Bound wave size to what
the orchestrator can merge + build in one cycle (3–6 porters/wave default), then integrate, then next wave.

### 20.4 Coordination protocol (claim → work → integrate)

The tracker docs are the shared blackboard. 1) **Claim:** append a row to the task board's claim ledger (your
lane, your worktree/branch) and commit it *first* (a cheap "claim commit"). 2) **Work:** stay inside the owned
subtree; if you need a chokepoint change, record the exact line in hand-off notes — don't edit it. 3) **Integrate:**
mark the item done, list changed files + required DI line + new loc keys + parity notes, hand the worktree to the
orchestrator who applies DI lines, merges, builds.

### 20.5 Conflict-avoidance rules

One file, one agent — always. DI registrations are *requested* (porter hands the orchestrator the line), not
applied. Localization is merged, never hand-edited in parallel (append new keys to `tools/new-localization-keys.json`;
orchestrator runs `python tools/merge-localization-keys.py` once per wave). csproj asset includes are batched.
Respect the seam contract: new behaviour goes behind an existing `CCP.Core/Platform` interface; if a new seam is
genuinely needed, the orchestrator adds the interface + shared fallback first, then porters implement per head
(§21 per-head DI pattern).

### 20.6–20.8 Context discipline (every agent, every role)

Compact aggressively: after every claimed item (build green → update tracker → commit → compact, keeping only
trackers + outcome); after each green build/test checkpoint; after any large one-shot dump (distill to the members
you touched, drop the rest); at ~50–60% of the window, unconditionally. **Durable working set to preserve across
compaction:** this plan + the task board + the parity matrix (write progress into these, not the transcript);
your single active item + owned subtree + last known-good build/test state; decisions live in §19/§21 — keep a
pointer. **Token hygiene:** Grep + targeted line-range reads over whole-file reads; build with `-clp:ErrorsOnly`;
don't re-establish the project layout each session (§3.1); treat tracker docs as external memory.

### 20.9 Per-agent task loop

`read tracker → claim next lane (claim commit) → targeted reads of just that subtree → port → build (ErrorsOnly)
+ Core tests → visual-parity check vs the active theme's reference + the WPF app's same tab (§13.5) → update
tracker (done + hand-off notes) → commit/hand off → compact → repeat.` Orchestrator: `assign wave → wait for
hand-offs → apply DI lines + loc merge + csproj assets → merge batch → integration build/test → reconcile
trackers → compact → next wave.`

### 20.10 Concrete lane map — derived from the original project

The UI work decomposes **along the original project's own structure**. The legacy `MainWindow` is ~33k LOC split
across ~38 feature-named partials (`MainWindow/MainWindow.<Feature>.cs`) — each is already a self-contained
feature boundary and the natural unit of one porter lane. Seed lanes with `find MainWindow -name "MainWindow.*.cs"`
and bucket them:

| Bucket | Original source | Target (Avalonia) | Parallel? | Owner |
|---|---|---|---|---|
| **Feature tabs** — Achievements, Animations, Assets, Autonomy, Awareness, BlinkTrainer, CatalogueSubmissions, CloudBackup, Companion, DeeperHub, DeeperSubmissions, DeeperTab, Enhancements, Haptics, KeywordTriggers, Lab, Leaderboard, LevelFeatures, Marquee, Patreon, Presets, Quests, RemoteControl, Roadmap, SessionIO, Settings, SubscribeStar | `MainWindow.<Feature>.cs` (+ matching `Services/<Area>`) | one `Views/Tabs/<Feature>TabView.*` + `ViewModels/Tabs/<Feature>TabViewModel.cs` | ✅ one lane each (high fan-out) | Porter |
| **Shell / infra backbone** — the `MainWindow.xaml` shell, `UiUpdates`, `TabNavigation`, `WindowChrome`, `Browser`, `StartStop`, `AccountShell`, `Login` | `MainWindow.<Infra>.cs` | `MainWindow.axaml(.cs)` + shell services | ⛔ serial — the spine every tab hangs off | Orchestrator only |
| **Portable engine** — `Services/AIService`, `Commands`, `Moderation`, `Progression`, `Session`, `Content`, `Bark`, `Deeper` (logic), `Quiz`, `Account`, `Auth`, `Settings` | `Services/<Area>/*` | `CCP.Core/Services/<Area>/*` | ✅ one lane per area | Porter (Core) |
| **Platform/UI services** — `Chaos` (26 files), `Video`, `Audio`, `Haptics`, `Webcam`, `Tracking`, `Input`, `Notifications`, `Update`, `Flash`, `Subliminal`, `UI`, `LockCard` | `Services/<Area>/*` | `CCP.Avalonia` + a `CCP.Core/Platform` seam | ✅ one lane per area (Chaos is big — sub-split) | Porter |

Rules: do the shell backbone first / single-owner (tabs can't be smoke-tested until `MainWindow.axaml` +
`TabNavigation` + `WindowChrome` exist); a tab lane owns both the WPF source and the `Services/<Area>` it drives;
`Chaos` is the one oversized lane (sub-split: overlays vs. economy/mode vs. host pooling); cross-check against the
parity matrix so a "done" tab isn't re-claimed; eyeball every tab before ✅ (§13.5) — a clean build is *not*
visual parity.

---

## 21. Implementation Lessons & Avalonia v12 Gotchas

Concrete things hit during implementation that the original draft did not anticipate:

- **`Microsoft.WindowsAppSDK` must be pinned, not removed** (transitive via LibVLCSharp; prevents a
  WebView2 `NU1605` downgrade). See §5.1. On the Windows head also set
  `WebView2EnableCsWinRTProjection=false` to get the managed WinForms control instead of the WinRT
  projection.
- **`WindowDecorations`, not `SystemDecorations`** (v12 rename). In code `TransparencyLevelHint` is an
  `IReadOnlyList<WindowTransparencyLevel>` → `new[] { WindowTransparencyLevel.Transparent }`. See §4.4.
- **Compiled bindings are on** (`AvaloniaUseCompiledBindingsByDefault=true`): every `.axaml` needs
  `x:DataType`; dynamic paths need `{ReflectionBinding}` (or `{CompiledBinding}` will fail to resolve).
- **Native LibVLC discovery is explicit.** `LibVLCSharp.Shared.Core.Initialize()` is called path-less in
  shared DI, then overridden per desktop head via `LibVLCNativeDiscovery.Initialize()` (`AddDesktopLibVLC`).
  Linux has no official NuGet (system `libvlc`); macOS ARM64 needs a dylib extracted from VLC.app. The CI
  installs `libvlc-dev vlc` on the Linux runner. This is the concrete realization of §5.4.
- **`IVideoSurface` is intentionally NOT DI-registered** — it needs a `VideoView` at construction, so
  consumers `new AvaloniaVideoSurface(videoView)` directly. Don't "fix" this by registering it globally.
- **Per-head DI override pattern:** register every seam in the shared `ConfigureCoreServices` with a safe
  fallback, then specialize via `App.ConfigurePlatformServices` in each head's `Program.cs` (last
  registration wins). Mobile vs. desktop branches on `OperatingSystem.IsAndroid()`.
- **`Avalonia.Controls.DataGrid` is at `12.0.0` while the rest of Avalonia is `12.0.4`** — align these
  (verify the matching DataGrid tag exists before bumping) to avoid subtle behaviour mismatches.
- **`Avalonia.Diagnostics` is recommended (§4.1) but not yet referenced** by any head. Add it as a
  `Debug`-conditional package and call `AttachDeveloperTools()` (F12 DevTools) — it materially speeds up the
  Phase-4 binding/parity work. Low effort, high leverage for the swarm.
- **"Builds + looks right" ≠ works.** The first port shipped inert UI: START didn't launch the mode, the avatar
  did nothing, overlays blocked input. Wire every control to a live `ICommand` → Core/platform service, and
  **exercise each feature in the running app** before calling it done (§13.6). Grep ported lanes for stub/no-op
  commands and `NotImplementedException`.
- **Overlays use per-region click-through (team review 2026-07-09, see §7.4):** only color-filter + spiral regions
  pass input; every other active layer captures over its painted region via the compositor capture-mask + mouse-hook
  swallow. The window mechanism is unchanged (`WS_EX_TRANSPARENT|WS_EX_LAYERED|WS_EX_NOACTIVATE` after the handle
  exists); ambient-only regions must not block the app's own buttons or other apps behind them.
- **Accent colors are theme-driven — never hard-code them.** The top-left mod switcher re-skins the whole app
  (Sissy = pink, Drone = green, …). Bind to the accent **resource keys via `DynamicResource`**, not literal hex,
  and port the per-mod re-skin path. The current `App.axaml` hard-codes the palette with no re-skin path — see
  §15.11. This is the cause of "looks wrong after switching mods."
- **Models are duplicated into Core** — the single largest drift hazard; see §19.4.

---

## 23. References — Official Avalonia Docs (v12)

The plan's technical claims are validated against the **Avalonia v12** documentation — canonical while porting;
if this plan and the docs disagree, the docs win (fix the plan). `docs.avaloniaui.net` documents **Avalonia 12**
(what this project uses); the previous line is archived at `v11.docs.avaloniaui.net` — don't follow v11 links for
v12-specific API (e.g. `WindowDecorations`). Project targets: **Windows, Linux X11+Wayland, macOS, Android** only.

| Topic | URL |
|---|---|
| Docs home / platform support | https://docs.avaloniaui.net/docs/welcome |
| **WPF → Avalonia migration guide** (hub) | https://docs.avaloniaui.net/docs/migration/wpf |
| **WPF → Avalonia cheat sheet** (XAML, bindings, styles, controls, events, properties, threading) | https://docs.avaloniaui.net/docs/migration/wpf/cheat-sheet |
| Styling (selectors/pseudo-classes) | https://docs.avaloniaui.net/docs/migration/wpf/styling |
| Controls (renames, packages) | https://docs.avaloniaui.net/docs/migration/wpf/controls |
| Data templates (DataType matching) | https://docs.avaloniaui.net/docs/migration/wpf/data-templates |
| Properties (`StyledProperty`/`DirectProperty`) | https://docs.avaloniaui.net/docs/migration/wpf/properties |
| Events (pointer/tunnel/routed) | https://docs.avaloniaui.net/docs/migration/wpf/events |
| Layout (Spacing, Grid shorthand, Panel) | https://docs.avaloniaui.net/docs/migration/wpf/layout |
| Data binding & compiled bindings (`x:DataType`, `x:CompileBindings`) | https://docs.avaloniaui.net/docs/basics/data/data-binding |
| MVVM pattern (Views/ViewModels, DataTemplates) | https://docs.avaloniaui.net/docs/concepts/the-mvvm-pattern |
| Styling — selectors, classes, pseudo-classes (replaces triggers) | https://docs.avaloniaui.net/docs/styling/styles |
| Deployment — macOS bundle/notarize | https://docs.avaloniaui.net/docs/deployment/macos |

> **Adding a new gotcha?** When you hit an Avalonia v12 surprise during implementation, add it to §21 (one
> concise bullet, with the source URL or issue number) — that is the canonical gotcha list the
> `avalonia-research` skill points agents at.
