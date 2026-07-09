# Cross-Platform Rebuild Plan — Conditioning Control Panel

> **Status banner (2026-07-05):** this is the historical master plan. **`docs/skia-rebuild-goal.md` is the active autonomous driver** — read it for current goals, workstreams, and the up-to-date "Current state". This document is kept for its still-useful REFERENCE sections (§5 LibVLC migration, §19 mainline-sync, §20 multi-agent swarm protocol, §21 Avalonia v12 gotchas, §23 official-doc links). Its **status/progress snapshots (§1A "Implementation Status Snapshot 2026-06-21", §8 phase roadmap) are HISTORICAL and no longer current** — do not treat their checkboxes or 🚧 markers as live status; live status lives in `skia-rebuild-goal.md`, `avalonia-ui-parity-matrix.md`, and `avalonia-migration-task-board.md`.

**Goal:** Rebuild Conditioning Control Panel (CCP) for deployment on Windows, Linux, macOS, and Android using **Avalonia UI v12** and **LibVLCSharp**.

**Current state:** .NET 8 WPF/WinForms desktop app, Windows-only (`net8.0-windows10.0.19041.0`), single-file publish for `win-x64`.

**Target state:** Multi-head Avalonia v12 solution with a shared .NET 8/10 Core, per-platform desktop and Android heads, and LibVLCSharp for cross-platform media.

---

## 1. Executive Summary

The migration is not a retarget-and-rebuild. It is a **UI rewrite plus platform abstraction**. The largest cost is the WPF UI layer (~121 XAML files, ~483 C# files, ~133k LOC of UI-related code). The engine logic (models, AI orchestration, gamification, sessions, networking) is largely portable once the right seams are introduced.

The recommended strategy is **engine-first extraction**:

1. Extract a cross-platform `CCP.Core` class library.
2. Introduce platform seams (`IUiDispatcher`, `IVideoSurface`, `IAudioPlayer`, `ISecretStore`, `IScreenProvider`, etc.).
3. Keep the WPF app running during migration by implementing seams with Windows shims.
4. Build a new Avalonia UI shell on top of `CCP.Core`.
5. Add mobile heads only after desktop parity is reached.

**Biggest risks:** WPF/Win32 windowing (HWND, layered overlays, z-order), NAudio/WASAPI audio, WebView2 browser, global input hooks, DPAPI-encrypted secrets, GDI/desktop capture, and desktop wallpaper override.

**Easiest wins:** LibVLCSharp core is already cross-platform; only the WPF `VideoView` binding must be replaced.

---

## 1A. Implementation Status Snapshot (2026-06-21)

> This section is the **ground truth** for anyone (or any agent) picking up the work. The phase
> roadmap in §8 was written before implementation started; the project is now much further along
> than a linear reading of §8 implies. Update this snapshot whenever a phase materially changes.

**Headline:** the architecture is fully stood up and the entire desktop solution builds with **0 errors**
(`dotnet build CCP.Desktop.slnf` → 0 errors, ~1 warning from the kept-alive WPF head).
The dominant remaining cost is **UI parity** (Phase 4: AvatarTube depth, Chaos overlay animations/z-order/boons,
remaining hard-coded dialogs) plus mobile polish — not scaffolding.

> **The bar for "done": every feature ports 1:1 — identical behavior to the WPF app — and should be *smoother and
> faster*.** The WPF baseline is the **floor, not the ceiling**: every function should feel at least as snappy on
> Avalonia/Skia, and the expectation is *better* — never slower, never janky. No permanent stubs, no "good enough"
> approximations, no degraded behavior on Windows. If WPF does X, the Avalonia port does X, at least as fast.
> "Better" means *faster/smoother*, not *changed* — match the WPF behavior, then beat its performance with
> Avalonia/Skia composition, async startup, pooling, and off-UI-thread work (§14.2). The only acceptable
> degradation is on Linux/macOS/Android where a feature is inherently platform-limited (global hooks, wallpaper,
> etc.) — gate those per §6/§7; **never** degrade on the Windows parity target.

> **Performance is the prime directive — lightweight & fast on the lowest-end systems.** The *biggest* reason for
> this rebuild was to make CCP lighter and faster: Avalonia v12 + its Skia/LibVLC media stack was chosen
> specifically so the app runs **smoothly on low-end / older machines**. So a low CPU/GPU/RAM footprint and zero lag
> are a **hard requirement equal in weight to functional parity**, not a nice-to-have — optimize for the weakest
> target machine, not the dev box. A port that is heavier, laggier, or needs more horsepower than WPF is a **defect
> even if it "works."** Levers: §14.2 (Skia composition, async startup, off-UI-thread decode, surface/audio pooling,
> gating concurrent effects), the §13.4 perf gate, and **§14.4 (actively hunt the web for faster solutions / adopt
> faster libraries — a standing behavior, not just when stuck)**.

> **Build principle — lazy senior dev / YAGNI ("ponytail"):** build the *simplest thing that works*. Framework and
> stdlib first, no unrequested abstractions, delete over add, shortest working diff. **A platform seam earns its
> place only when there's a real cross-platform divergence with a real implementation behind it — never a one-line
> wrapper over a framework API.** This is not in tension with the bar above: 1:1-or-better behavior is the *what*;
> ponytail is the *how* — fewer moving parts is faster and more maintainable. The ponytail-audit
> (`docs/avalonia-ponytail-audit-queue.md`) already applied this and **removed needless wrappers/stubs** —
> `IUiDispatcher`, `IScheduler`, custom `IAppLogger`, hand-rolled `LibVLCNativeDiscovery`, the `AvaloniaFrameSource`
> throw-stub, mobile stubs — in favor of using `Dispatcher.UIThread`, `DispatcherTimer`, `ILogger<T>`, LibVLCSharp's
> own discovery, and `AssetLoader` directly. Hold new work to that bar; before adding a seam/abstraction, check it
> against this principle and §3.3.
>
> **Pruning is ongoing (keep doing it — it makes the app faster), but each prune is a refactor.** Deleting an
> abstraction can break whatever quietly leaned on it, so treat every cut like a code change: build, then
> **re-exercise the affected features end-to-end (§13.6)** — don't assume "removed wrapper, still works." Because
> of this, **`docs/avalonia-ui-parity-matrix.md` was reset (2026-06-23) to all-unverified** — every item is
> re-checked from scratch by exercising it; prune unneeded code during that pass. Trust the running app, not old
> marks.

> **Execution model:** the remaining work is highly parallel and is meant to be run as a **multi-agent
> swarm**. Before assigning or starting work, read **§20 — Multi-Agent Swarm Execution & Context Discipline**
> for lane partitioning, the claim→work→integrate protocol, conflict-avoidance rules, and **when to compact
> context**. The live work queue is `docs/avalonia-migration-task-board.md`; the current cross-merge backlog
> is §19.3.

> **⚠️ `main` 6.1.7 merged (2026-06-23):** branch is caught up. The on-branch WPF 6.1.7 code (Chaos "Down the
> Rabbit Hole" main menu, quest-pool refresh, auth browser-launch fallback, subliminal/avatar fixes) now needs
> **porting to Core/Avalonia** — see §19.3 and the goal's STEP 0. ⚠️ The `Quest.cs`/`AppSettings.cs` deltas were
> **dropped** in conflict resolution (modify/delete → kept deleted), so they're NOT in Core — re-apply them.
> (Merge also left a stale `using` that broke the Core build; fixed — Core + desktop build green.)

| Phase | Status | Evidence / Notes |
|---|---|---|
| 0 — Cleanup | ✅ done | The 3 dead deps (`SharpDX`, `OpenAI-DotNet`, `OllamaSharp`) are removed from **all** projects (verified: 0 references); `NAudio`/`OpenCvSharp`/WebView2 are confined to the Windows head. The WPF head still carries the packages it genuinely needs to keep running (MahApps, XamlAnimatedGif, etc.) **by design**. |
| 1 — Carve out `CCP.Core` | ✅ substantially done | 156 `.cs` files: 53 models, the full platform-seam interface set (26 interface files in `CCP.Core/Platform/`), plus services for Sessions, Moderation, Deeper, Bark, Chaos, AIService, Progression, Quests, Settings, Content, Commands. Core builds clean on `net8.0`. |
| 2 — Prove Core off-Windows | 🚧 partial | CI (`.github/workflows/build.yml`) builds Core + heads on `ubuntu-latest`/`macos-latest` and runs `CCP.Core.Tests`. Needs ongoing "no Windows leaks" auditing as Core grows. |
| 3 — Avalonia solution | ✅ done | All heads exist and build (see §3.1, **updated**). |
| 4 — XAML/UI migration | 🚧 renders, but **functionally incomplete** | 149 `.axaml` + 279 `.cs` ported (Dialogs 67, Windows 59, Views/Tabs 58 + 33 VMs, Features 52, Chaos 46, AvatarTube 10); the shell, chrome, drag-drop/resize, XP bar, and banner render. **But** a 2026-06-22 audit + maintainer testing found many features **don't actually work** (login dead, START/avatar/overlays/Chaos/content-packs/feature-card-editors stubbed). **The parity matrix's ✅ means "renders," not "works"** — the authoritative not-done list is `docs/avalonia-migration-task-board.md` → **Known Functional Gaps** (reported #1–#5 + audit A–K). `docs/avalonia-ui-parity-matrix.md` tracks per-screen rendering. |
| 5 — Media & audio | 🚧 partial | `AvaloniaVideoSurface`, `AvaloniaAudioPlayer`, `AvaloniaDualMonitorVideoService`, and `LibVLCNativeDiscovery` exist. Mandatory-video attention checks, strict mode, post-play penalty/mercy loop, and dual-monitor secondary windows are ported. Windows system-audio ducking is implemented via `WindowsSystemAudioDucker`; Linux/macOS use best-effort `pactl`/`osascript`. Remaining: mobile `IVideoSurface`, GIF/SVG finalize. |
| 6 — OS-shell features | ✅ structurally done | Per-head implementations exist for tray, hotkeys, input hook, wallpaper, browser host (WebView2 / WebKitGTK / WebKit), window chrome, frame source, audio device + ducker. Wired via `App.ConfigurePlatformServices` overrides per head. |
| 7 — Build & publish | 🚧 desktop done | RIDs defined per head; CI publishes single-file desktop artifacts for win/linux/macOS. The Android job currently only `dotnet build`s the head — **AAB packaging is not yet wired**. Remaining: Android AAB publish, code signing/notarization, Android keystore. |
| 8 — Mobile gating | 🚧 structural only | Android head builds; capability gating is in place via `IPlatformCapabilities` + `OperatingSystem.IsAndroid()` branch in DI. Remaining: mobile-first shell, touch UX, native camera. |

**Replaced static service locator (was §15.1):** ✅ already implemented. `App.xaml.cs`'s **88** static
service properties (in a 2,810-LOC file) are gone in the Avalonia heads — everything is `Microsoft.Extensions.DependencyInjection`
in `CCP.Avalonia/ServiceCollectionExtensions.cs` with per-head overrides in
`*/DesktopServiceCollectionExtensions.cs` and each head's `Program.cs` (`App.ConfigurePlatformServices`).

**Deviations from the original plan — the plan body below has been corrected to match these:**

1. **Desktop is four projects, not one** (see §3.1). A shared `CCP.Avalonia.Desktop` carries
   cross-desktop logic and native LibVLC discovery; thin `.Windows`/`.Linux`/`.macOS` executable heads
   own the `Program.cs` entry point and platform package references.
2. **`Microsoft.WindowsAppSDK` is pinned, not removed** — it is dragged in transitively by
   LibVLCSharp and must be pinned with `ExcludeAssets="all" PrivateAssets="all"` to avoid a WebView2
   version-downgrade (`NU1605`) error. See §5.1 / §6 / Phase 0 (corrected).
3. **Models are *copied* into Core, not referenced from a single source.** This is the biggest
   ongoing maintenance hazard — see the new §19 *Mainline Sync & Dual-Maintenance*.

---

## 2. Current State Analysis

### 2.1 Project Profile

| Property | Current Value | Migration Note |
|---|---|---|
| `OutputType` | `WinExe` | Change to `Exe` for desktop heads; mobile heads use their own templates. |
| `TargetFramework` | `net8.0-windows10.0.19041.0` | Use `net8.0`/`net10.0` for shared Core; platform TFMs only in head projects. |
| `UseWPF` / `UseWindowsForms` | `true` / `true` | Remove from Core and Avalonia heads. |
| `RuntimeIdentifier` | `win-x64` | Use `RuntimeIdentifiers` per head. |
| `PublishSingleFile` / `SelfContained` | `true` / `true` | Supported for Avalonia desktop; not for mobile. Native libs must remain on disk. |
| `NoWarn` | `CA1416` | Remove once platform-specific code is properly gated. |
| Custom `Main` + `STAThread` | `App.xaml.cs` | Replace with Avalonia `BuildAvaloniaApp` / platform lifecycles. |

### 2.2 Code Volume

| Layer | Files | Approx. LOC | Verdict |
|---|---|---|---|
| XAML markup | 121 `.xaml` | ~30,400 | Rewrite as `.axaml`. |
| XAML code-behind | 115 `.xaml.cs` | ~78,400 | Rewrite against Avalonia APIs. |
| Non-XAML WPF partials | ~20 | ~25,000 | Rewrite. |
| Models / JSON DTOs | ~66 | ~17,800 | **Portable** — move to Core. |
| Localization runtime | 2 | ~180 | **Portable** — move to Core. |
| ViewModels | 1 | 201 | Minor fixes (`Visibility` → `bool`). |

### 2.3 P/Invoke Surface Area

There are **~200 `DllImport` declarations** across the codebase, concentrated in:

| DLL | Approx. Imports | Used For |
|---|---|---|
| `user32.dll` | 108 | Window styles, z-order, focus, hooks, cursor, keys, display |
| `gdi32.dll` | 10+ | Regions, bitmap blitting, device contexts |
| `dwmapi.dll` | 4 | Dark title bar, rounded corners, window attributes |
| `shcore.dll` | 1+ | Per-monitor DPI |
| `shell32.dll` | 1 | Shell thumbnails |
| `kernel32.dll` | 7 | Thread IDs, module handles, memory copy |
| `dbghelp.dll` | 1 | Crash minidumps |

Every one of these must move behind a platform interface with Windows, Linux, macOS, and mobile implementations.

### 2.4 Key Windows-Only Dependencies

| Package | Purpose | Migration |
|---|---|---|
| `LibVLCSharp.WPF` | Video surface | `LibVLCSharp.Avalonia` (desktop) or platform-specific mobile surfaces |
| `VideoLAN.LibVLC.Windows` | Native VLC engine | Keep for Windows; add per-platform native packages |
| `Microsoft.Web.WebView2` / `WebView2.Wpf` | Embedded browser | Abstract `IBrowserHost`; use Avalonia webview wrappers or native webviews |
| `NAudio` / `NAudio.Wasapi` | Audio playback / ducking | Abstract `IAudioPlayer`; use LibVLC, ManagedBass, or platform audio APIs |
| `Hardcodet.NotifyIcon.Wpf` / `System.Windows.Forms.NotifyIcon` | System tray | Avalonia built-in `TrayIcon` + `NativeMenu` |
| `MahApps.Metro` / `IconPacks` | UI theme / icons | Avalonia Fluent/Simple theme + `Material.Icons.Avalonia` / custom icons |
| `XamlAnimatedGif` | Animated GIFs | `AvaloniaGif` or custom SkiaSharp/ImageSharp frame animation |
| `SharpVectors` | SVG → WPF | `Svg.Skia` / `Avalonia.Svg.Skia` |
| `OpenCvSharp4.runtime.win` | OpenCV native | Add Linux/macOS runtimes; mobile uses native camera APIs |
| `System.Security.Cryptography.ProtectedData` | DPAPI secrets | Abstract `ISecretStore` (Keychain on macOS, libsecret on Linux, DPAPI on Windows) |
| `SharpDX.*` | Direct3D/DXGI | Dead dependency — remove |

---

## 3. Target Architecture

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
├── ConditioningControlPanel.csproj # the original WPF app — kept runnable during migration
├── ConditioningControlPanel.slnx   # full solution (all heads + WPF + tests)
└── CCP.Desktop.slnf                # solution filter: desktop heads + Core + tests, excludes Android
```

> **Note (updated):** The single "`CCP.Avalonia.Desktop` (Win/Linux/Mac)" head from the original draft
> was split into a **shared** `CCP.Avalonia.Desktop` library plus three thin executable heads
> (`.Windows`, `.Linux`, `.macOS`). Rationale: only the Windows head can carry the
> `net8.0-windows*` TFM and Win32/WebView2/NAudio references; keeping that out of the Linux/macOS heads
> avoids polluting them with Windows-only assets. Each head's `Program.cs` sets
> `App.ConfigurePlatformServices` to override the shared DI registrations with its native implementations.
> The Avalonia v12 Android head targets `net10.0-android`; desktop heads target `net8.0` (Windows head
> uses `net8.0-windows10.0.19041.0`).
> Use `CCP.Desktop.slnf` for fast desktop-only builds (it excludes the Android head, which needs the
> Android workload).

### 3.2 Project Responsibilities

| Project | Responsibility |
|---|---|
| `CCP.Core` | Models, settings, session/gamification logic, AI/LLM orchestration, networking, mod/catalogue logic, JSON contracts, localization runtime. No UI framework references. |
| `CCP.Avalonia` | `App.axaml`, Views, UserControls, ViewModels, converters, platform-agnostic styles, **and the Avalonia implementations of the platform seams** (`Platform/Avalonia*.cs`) plus shared DI (`ServiceCollectionExtensions.ConfigureCoreServices`). References `CCP.Core` and Avalonia packages. Exposes `App.ConfigurePlatformServices` so heads can override seam registrations. |
| `CCP.Avalonia.Desktop` | **Shared** desktop library: `LibVLCNativeDiscovery`, `DesktopServiceCollectionExtensions` (secret store, single-instance, wallpaper, LibVLC), shared `BuildAvaloniaApp`. Not an entry point. |
| `CCP.Avalonia.Desktop.Windows` | Windows executable head: `Program.cs` entry point, `net8.0-windows*` TFM, WebView2/NAudio/Win32 seam implementations registered via `App.ConfigurePlatformServices`. |
| `CCP.Avalonia.Desktop.Linux` | Linux executable head: `Program.cs`, WebKitGTK browser host, relies on system `libvlc`. |
| `CCP.Avalonia.Desktop.macOS` | macOS executable head: `Program.cs`, WKWebView host, `VideoLAN.LibVLC.Mac` (x64) / extracted dylib (ARM64). |
| `CCP.Avalonia.Android` | `MainActivity.cs`, Android lifecycle, mobile seam implementations (`Mobile*.cs`), reduced feature set. |
| `CCP.WindowsOnly` | Win32 P/Invoke helpers, WebView2 host, NAudio implementation, DWM chrome (WPF/WinForms). Referenced for Windows parity; some implementations now live directly in the Windows desktop head instead. |

### 3.3 Platform Seams (Interfaces)

Introduce these interfaces in `CCP.Core` and implement per platform:

| Interface | Replaces |
|---|---|
| `IUiDispatcher` | `Application.Current.Dispatcher` |
| `IScheduler` / `IUiTimer` | `DispatcherTimer` |
| `IVideoSurface` | `LibVLCSharp.WPF.VideoView` |
| `IAudioPlayer` / `IAudioDeviceService` / `ISystemAudioDucker` | NAudio/WASAPI |
| `IScreenProvider` | `System.Windows.Forms.Screen` |
| `IWindowChrome` / `IOverlaySurface` | Win32 DWM, layered windows, z-order |
| `IHotkeyProvider` / `IInputHook` | `RegisterHotKey`, `SetWindowsHookEx` |
| `ITrayIcon` | `NotifyIcon` |
| `IBrowserHost` | WebView2 |
| `ISecretStore` | DPAPI |
| `IWallpaperProvider` | `SystemParametersInfo` wallpaper |
| `IUpdateInstaller` | Inno Setup updater |
| `IThumbnailProvider` | `IShellItemImageFactory` |
| `IFrameSource` / `ICaptureService` | GDI desktop capture |
| `IImageDecoder` / `IImageSourceFactory` | `BitmapImage`, `pack://` URIs |

> **Status (implemented — read before adding a seam).** This table is the *original design intent*. The seam
> set now lives in `CCP.Core/Platform/` as **26 interface files** and is the authoritative list. A few entries above
> were **consolidated or deferred**, so do **not** create them:
> - `ICaptureService` → folded into **`IFrameSource`**.
> - `IImageDecoder` / `IImageSourceFactory` → folded into **`IAssetLoader`**.
> - `IUiTimer` → folded into **`IScheduler`**.
> - `IThumbnailProvider` → **deferred / not created** (Phase 6 item 9 still references it; treat as a future seam,
>   not an existing one).
> - `IScreenProvider` **does** exist — it is declared in `IScreenInfo.cs`.
>
> Interfaces added during implementation that aren't in this table: `IAppEnvironment`, `IDialogService`,
> `IFilePickerService`, `IHapticsService`, `ILockdownService`, `IPlatformCapabilities`, `IRemoteControlService`.
>
> **Removed as needless wrappers (ponytail-audit, §1A build principle):** `IUiDispatcher` and `IScheduler` were
> one-line wrappers over `Dispatcher.UIThread` / `DispatcherTimer` — deleted; call the framework APIs directly.
> Likewise the custom `IAppLogger` (→ `ILogger<T>`), `LibVLCNativeDiscovery` (→ LibVLCSharp's own discovery), and
> the `AvaloniaFrameSource` throw-stub (register `IFrameSource` only where it's actually implemented). Don't
> recreate these. **A seam earns its place only with a real cross-platform divergence + a real implementation —
> not indirection for its own sake.** When you genuinely need one, add the interface + a safe shared fallback in
> `ConfigureCoreServices` first (see §21), then implement it per head.

---

## 4. Avalonia UI v12 Migration

### 4.1 Package Map

| Current (WPF) | Avalonia v12 Replacement | Version |
|---|---|---|
| WPF SDK (`UseWPF=true`) | Avalonia SDK packages | — |
| `MahApps.Metro` | `Avalonia.Themes.Fluent` or `Avalonia.Themes.Simple` | 12.0.x |
| `MahApps.Metro.IconPacks` | `Material.Icons.Avalonia`, `FluentIcons.Avalonia`, or custom SVG | — |
| `Hardcodet.NotifyIcon.Wpf` | Built-in `TrayIcon` + `NativeMenu` | 12.0.x |
| `XamlAnimatedGif` | `AvaloniaGif` or SkiaSharp animation | — |
| `SharpVectors` | `Svg.Skia` / `Avalonia.Svg.Skia` | — |
| WPF `DataGrid` | `Avalonia.Controls.DataGrid` | 12.0.x |
| WPF `Behavior` / `Interaction` | `Avalonia.Xaml.Interactions` | — |

Core Avalonia packages to add:

```xml
<PackageReference Include="Avalonia" Version="12.0.4" />
<PackageReference Include="Avalonia.Desktop" Version="12.0.4" />
<PackageReference Include="Avalonia.Themes.Fluent" Version="12.0.4" />
<PackageReference Include="Avalonia.Fonts.Inter" Version="12.0.4" />
<PackageReference Include="Avalonia.Skia" Version="12.0.4" />
<PackageReference Include="Avalonia.HarfBuzz" Version="12.0.4" />
<PackageReference Include="Avalonia.Diagnostics" Version="12.0.4" Condition="'$(Configuration)' == 'Debug'" />
```

Mobile heads:

```xml
<!-- Android -->
<PackageReference Include="Avalonia.Android" Version="12.0.4" />
```

### 4.2 Major v12 Changes Affecting the Migration

| Feature | Change | Impact |
|---|---|---|
| Compiled bindings | Enabled by default | Every XAML binding needs `x:DataType`; use `{ReflectionBinding ...}` only for dynamic paths. |
| `IBinding` removed | Use `BindingBase` | Custom markup extensions (e.g., `{loc:Str}`) must be rewritten. |
| Clipboard / drag-drop | `IDataObject` removed | File-open handoff and drag-drop must move to async typed APIs. |
| `SystemDecorations` renamed | Now `WindowDecorations` | Custom chrome must be reimplemented. |
| Window state from styles | Cannot set `WindowState` from styles | Set in code or initialization only. |
| Dispatcher model | `Dispatcher.CurrentDispatcher`, `Yield`, `Resume` added | WPF-like dispatcher code is easier, but timers must be created on the intended UI thread. |
| `Avalonia.Diagnostics` | Removed from core | Use `Avalonia.Diagnostics` package and `AttachDeveloperTools()`. |
| .NET Standard dropped | No `netstandard2.0` assets | Class libraries must target `net8.0`/`net10.0`. |

### 4.3 XAML Namespace & File Changes

| WPF | Avalonia |
|---|---|
| `.xaml` | `.axaml` |
| `xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"` | `xmlns="https://github.com/avaloniaui"` |
| `Window`, `UserControl`, `Page` | `Window`, `UserControl` (no `Page`/`Frame` by default) |
| `ResourceDictionary.MergedDictionaries` | `Application.Styles`, `ResourceDictionary` |
| `DynamicResource` / `StaticResource` | Same names, but lookup semantics differ |
| `pack://application:,,,/...` | `avares://CCP.Avalonia/...` |
| `<Resource Include="..." />` | `<AvaloniaResource Include="..." />` |

### 4.4 Control Replacements

| WPF Pattern | Avalonia Equivalent |
|---|---|
| `WindowChrome` custom chrome | `ExtendClientAreaToDecorationsHint` + `ExtendClientAreaChromeHints` + `WindowDecorations` (the v12 name; `SystemDecorations` no longer exists) |
| `WindowStyle="None" AllowsTransparency="True"` | `WindowDecorations="None"`, `TransparencyLevelHint="Transparent"`, `Background="Transparent"`. In code, `TransparencyLevelHint` is an `IReadOnlyList<WindowTransparencyLevel>` → set `new[] { WindowTransparencyLevel.Transparent }`. |
| `Topmost`, `ShowInTaskbar`, `ResizeMode`, `WindowState` | Similar properties; behavior varies on Linux/macOS WMs |
| `Viewbox Stretch="Fill"` | Avalonia `Viewbox` supports `Stretch`; test HiDPI |
| WPF `Style`, `Trigger`, `DataTrigger`, `EventSetter`, `Storyboard` | Avalonia style selectors (`:pointerover`, `:checked`) + `Avalonia.Animation` |
| `FocusVisualStyle`, `Cursor="Hand"` | `:focus` selector, `Cursor` enum |
| `ToolTip HasDropShadow="False"` | No direct equivalent; Avalonia `ToolTip` has no `HasDropShadow` |
| `CommandBinding`, `RoutedCommand`, `InputBinding` | Avalonia commands/bindings; routed-event model differs |
| `System.Windows.Shapes` | `Avalonia.Controls.Shapes` |
| `System.Windows.Media.Effects.DropShadowEffect` | `BoxShadow` |
| `System.Windows.Media.Imaging.BitmapImage` / `WriteableBitmap` | `Avalonia.Media.Imaging.Bitmap` / `WriteableBitmap` |
| `Visibility` (`Visible`/`Collapsed`/`Hidden`) | **`IsVisible`** (bool) covers Visible/Collapsed; for WPF `Hidden` (invisible but still occupies layout) use `Opacity="0"` |
| `ListView` + `GridView` | `ListBox` + `ItemTemplate` (or `DataGrid` for columns) |
| `HierarchicalDataTemplate` | `TreeDataTemplate` |
| `LayoutTransform` | wrap the child in `LayoutTransformControl` |
| `DataTemplateSelector` | a `DataTemplate` with `DataType` matching (interface/derived-type aware) — no selector class needed |

> Mappings above are confirmed against the official **WPF → Avalonia cheat sheet** (see §23). When a WPF
> construct isn't listed here, check that page before improvising.

**Layout quick-wins (migration guide's layout page, §23):** `StackPanel` has a `Spacing` property (drop the
per-child margins); use inline `ColumnDefinitions="Auto,*,200"` / `RowDefinitions="…"` instead of verbose
`<Grid.ColumnDefinitions>` blocks (the WPF app has ~83 of those); and prefer a bare `Panel` over a defs-less
`Grid` for pure layering — it's lighter and sidesteps the WPF layering/airspace hacks called out in §14.1.
`UseLayoutRounding` behaves the same; `ScrollViewer` visibility modes match, but default scroll feel can differ
slightly per platform.

### 4.5 Dispatcher & Threading

| WPF | Avalonia |
|---|---|
| `Application.Current.Dispatcher.BeginInvoke(...)` | `Avalonia.Threading.Dispatcher.UIThread.Post(...)` |
| `Dispatcher.Invoke(...)` | `Dispatcher.UIThread.Invoke(...)` |
| `DispatcherTimer` | `Avalonia.Threading.DispatcherTimer` |
| `DispatcherPriority` | `Avalonia.Threading.DispatcherPriority` |

### 4.6 Localization Markup Extension

Current (`Localization/LocExtension.cs`):

```csharp
public override object ProvideValue(IServiceProvider serviceProvider)
{
    var binding = new Binding($"[{Key}]") { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay };
    return binding.ProvideValue(serviceProvider);
}
```

Avalonia rewrite: create an Avalonia `MarkupExtension` returning a `Binding`, or replace usages with `{Binding [Key], Source={x:Static loc:LocalizationManager.Instance}}`.

> **✅ Implemented.** This is done as `StrExtension` in `CCP.Avalonia/Localization/LocExtension.cs`, used in XAML
> as `{loc:Str btn_cancel}`. Its `ProvideValue` returns a `OneWay` `Binding` to
> `LocalizationManager.Instance[Key]`, so strings update live on a language change. Register the namespace with
> `xmlns:loc="clr-namespace:ConditioningControlPanel.Avalonia.Localization;assembly=CCP.Avalonia"`. New views must
> use `{loc:Str …}` rather than hard-coded text (§17.2).

### 4.7 Binding Syntax & Custom Properties

Two WPF idioms that appear throughout the original XAML/controls and have **no mechanical 1:1** — port them
deliberately (mappings per the official cheat sheet, §23). The original project uses `ElementName` in ~6 XAML
files, `RelativeSource` in ~7, and `DependencyProperty.Register` in ~3 code files (e.g. `Controls/HelpPopover.cs`);
the Avalonia tree already uses `StyledProperty` in ~9 files, so the patterns are established — just apply them
consistently.

**Binding syntax:**

| WPF | Avalonia |
|---|---|
| `{Binding Prop, ElementName=foo}` | `{Binding #foo.Prop}` |
| `{Binding Prop, RelativeSource={RelativeSource AncestorType=Grid}}` | `{Binding $parent[Grid].Prop}` |
| `{Binding Prop, RelativeSource={RelativeSource Self}}` | `{Binding $self.Prop}` |
| `{Binding Prop, RelativeSource={RelativeSource TemplatedParent}}` | `{TemplateBinding Prop}` (or `$parent[ControlType]`) |
| `{Binding}` against an untyped `DataContext` | add `x:DataType` to the scope, or use `{ReflectionBinding}` for dynamic paths (compiled bindings are on by default — §4.2) |

**Custom dependency properties** (in custom controls under `Controls/`, `Windows/`, etc.):

| WPF | Avalonia |
|---|---|
| `DependencyProperty.Register(...)` (stylable/animatable) | `StyledProperty<T>` via `AvaloniaProperty.Register<TOwner, T>(...)` |
| `DependencyProperty.Register(...)` (fast, CLR-backed, no styling) | `DirectProperty<TOwner, T>` via `AvaloniaProperty.RegisterDirect<TOwner, T>(...)` |
| `RegisterAttached(...)` | `AvaloniaProperty.RegisterAttached<TOwner, THost, T>(...)` |
| `PropertyChangedCallback` | override `OnPropertyChanged(AvaloniaPropertyChangedEventArgs)` or subscribe via `.Changed` |

### 4.8 Events & Input

The original app is **extremely event-heavy in code-behind** — `MouseLeftButtonDown` appears in ~138 files,
`MouseEnter` in ~32, `MouseMove` in ~14, `PreviewKeyDown` in ~9. This is one of the largest mechanical surfaces
of the UI port. WPF mouse events become **pointer** events (Avalonia also supports touch/pen, which matters for
the Android head), and there are **no `Preview*` events** — you opt into the tunnel phase explicitly. Mappings
per the migration guide's events page (§23):

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
> add the `PointerUpdateKind`/`IsLeftButtonPressed` check — especially in the drag/click-heavy code (AvatarTube,
> Chaos overlays, BlinkTrainer, bubble minigames).

---

## 5. LibVLCSharp Cross-Platform Media Migration

### 5.1 Package Changes

Remove:

```xml
<PackageReference Include="LibVLCSharp.WPF" Version="3.8.5" />
```

> **Correction (do NOT remove `Microsoft.WindowsAppSDK`).** The original draft listed it for removal.
> In practice LibVLCSharp pulls `Microsoft.WindowsAppSDK` in transitively, and leaving it unpinned causes
> a WebView2 **`NU1605` version-downgrade** build error. It must be **pinned and neutralized** in
> `CCP.Avalonia` and the Linux/macOS heads:
> ```xml
> <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.8.251106002" ExcludeAssets="all" PrivateAssets="all" />
> ```
> On the Windows head, also set `<WebView2EnableCsWinRTProjection>false</WebView2EnableCsWinRTProjection>`
> so the managed WinForms WebView2 control (not the WinRT projection) is used.

Add managed packages:

```xml
<PackageReference Include="LibVLCSharp" Version="3.9.7.1" />
<PackageReference Include="LibVLCSharp.Avalonia" Version="3.9.7.1" />
```

Add native engine packages per head:

| Platform | Package | Notes |
|---|---|---|
| Windows x64/x86/ARM64 | `VideoLAN.LibVLC.Windows` 3.0.23.1 | Drop-in replacement |
| Windows GPL | `VideoLAN.LibVLC.Windows.GPL` | Only if GPL codecs needed |
| macOS x64 | `VideoLAN.LibVLC.Mac` 3.1.3.1 | Old; no ARM64 build |
| macOS ARM64 | **Manual** | Extract `libvlc.dylib` + plugins from modern VLC.app |
| Linux | **No official NuGet** | Install `libvlc`/`libvlccore` via package manager or ship custom `.so` |
| Android | `VideoLAN.LibVLC.Android` 3.6.5 / 3.7.0-beta | Add `LibVLCSharp.Android.AWindowModern` |

> **Important:** `LibVLCSharp.Avalonia` officially supports Windows, macOS, and Linux. For Android, use the platform-specific video surface.

### 5.2 VideoView Migration

WPF XAML:

```xml
xmlns:vlc="clr-namespace:LibVLCSharp.WPF;assembly=LibVLCSharp.WPF"
<vlc:VideoView x:Name="VideoView" />
```

Avalonia XAML (the codebase uses the idiomatic `using:` namespace form, e.g. in
`Views/VideoSpikeWindow.axaml` and `Views/Deeper/EnhancementPlayerWindow.axaml`):

```xml
xmlns:vlc="using:LibVLCSharp.Avalonia"
<vlc:VideoView x:Name="VideoView" />   <!-- MediaPlayer can be bound or set in code-behind -->
```

Code-behind remains conceptually identical:

```csharp
var player = new MediaPlayer(libVLC);
videoView.MediaPlayer = player;
player.Play(media);
```

### 5.3 Memory-Render Surfaces

`DualMonitorVideoService` and `InlineLoopVideo` use LibVLC memory callbacks (`SetVideoCallbacks` / `SetVideoFormat` with `RV32`) and WPF `WriteableBitmap`.

Avalonia equivalent:

```csharp
using var framebuffer = bitmap.Lock();
unsafe
{
    Buffer.MemoryCopy(_frameBuffer.ToPointer(), framebuffer.Address.ToPointer(), size, size);
}
```

Use `Avalonia.Media.Imaging.WriteableBitmap`, and drive per-frame invalidation from a ~16 ms `DispatcherTimer`
(which is exactly what the implemented `CCP.Avalonia/Services/Video/AvaloniaDualMonitorVideoService.cs` does) or
from `TopLevel.RequestAnimationFrame`. **Do not use WPF's `CompositionTarget.Rendering`** — it does not exist in
Avalonia.

### 5.4 Native Library Packaging

- Reference per-RID native packages and let NuGet/build place them in output.
- For `PublishSingleFile`, mark native libraries with `<ExcludeFromSingleFile>true</ExcludeFromSingleFile>`.
- Remove or gate the manual `CopyLibVLCAfterPublish` target to Windows only.
- For Linux/macOS, use explicit `Core.Initialize(path)` derived from `AppContext.BaseDirectory` + RID.

### 5.5 Audio Abstraction

NAudio is Windows-only. Strategy:

1. Define `IAudioPlayer`, `IAudioCapture`, `IAudioDeviceEnumerator`, `ISystemAudioDucker`.
2. On Windows, keep NAudio behind the interface as an optional implementation.
3. On Linux/macOS/mobile, use:
   - LibVLC for simple file playback.
   - `ManagedBass` / `Bass.Net` for lower-level mixing.
   - `OpenAL` / `Silk.NET.OpenAL` for playback/capture.
   - Platform APIs for ducking (PulseAudio/PipeWire on Linux, CoreAudio on macOS, AudioManager on Android).

### 5.6 WebView2 Video in Deeper

`Views/Deeper/EnhancementPlayerWindow.xaml` uses WebView2 for video. Replace with the same LibVLC `VideoView` used everywhere else, unifying the video stack.

---

## 6. Dependency Migration Matrix

| Current Package | Current Version | Action | Replacement / Notes |
|---|---|---|---|
| `Buttplug` | 3.0.1 | Keep | Test on Linux/mac; likely desktop-only. |
| `Buttplug.Client.Connectors.WebsocketConnector` | 3.0.1 | Keep | Same as above. |
| `CommunityToolkit.Mvvm` | 8.2.2 | Keep | Fully portable. |
| `DiscordRichPresence` | 1.6.1.70 | Keep | Cross-platform transport. |
| `Hardcodet.NotifyIcon.Wpf` | 1.1.0 | Remove | Avalonia `TrayIcon`. |
| `LibVLCSharp.WPF` | 3.8.5 | Remove | `LibVLCSharp` + `LibVLCSharp.Avalonia`. |
| `MahApps.Metro` | 2.4.10 | Remove | Avalonia Fluent/Simple theme. |
| `MahApps.Metro.IconPacks` | 5.0.0 | Remove | `Material.Icons.Avalonia` or custom icons. |
| `Microsoft.ML.OnnxRuntime` | 1.20.1 | Keep + add runtimes | Add Linux/mac/mobile runtime packages. |
| `Microsoft.Web.WebView2` | 1.0.2535.41 | Remove from shared | Keep only in Windows-only head; abstract `IBrowserHost`. |
| `Microsoft.WindowsAppSDK` | 1.8.x (excluded) | **Pin, don't remove** | Transitive via LibVLCSharp; must be pinned with `ExcludeAssets="all" PrivateAssets="all"` to avoid a WebView2 `NU1605` downgrade. Present in `CCP.Avalonia` + Linux/macOS heads. |
| `NAudio` / `NAudio.Wasapi` | 2.2.1 | Abstract | Replace with `IAudioPlayer`; use LibVLC/ManagedBass/OpenAL. |
| `Newtonsoft.Json` | 13.0.3 | Keep | Portable. |
| `OllamaSharp` | (removed) | ✅ Done | Already removed from all projects. |
| `OpenAI-DotNet` | (removed) | ✅ Done | Already removed from all projects. |
| `OpenCvSharp4` | 4.9.0.20240103 | Keep + add runtimes | Add Linux/mac/mobile native runtimes. |
| `OpenCvSharp4.runtime.win` | 4.9.0.20240103 | Move to Windows head | |
| `QRCoder` | 1.6.0 | Keep | Portable. |
| `Serilog` + sinks | 3.1.1 / 5.0.0 | Keep | Portable. |
| `SharpDX` / `.DXGI` / `.Direct3D11` | (removed) | ✅ Done | Already removed from all projects (zero references). |
| `SharpVectors` | 1.8.4.2 | Remove | `Svg.Skia` / `Avalonia.Svg.Skia`. |
| `System.Security.Cryptography.ProtectedData` | 8.0.0 | Abstract | `ISecretStore` with DPAPI/Keychain/libsecret. |
| `VideoLAN.LibVLC.Windows` | 3.0.21 | Keep + add runtimes | Add Mac/Android packages; Linux via system or custom. |
| `XamlAnimatedGif` | 2.3.0 | Remove | `AvaloniaGif` or custom frame animation. |

---

## 7. Subsystem Migration Plan

### 7.1 Application Bootstrap

Current: custom `[STAThread] Main`, `Mutex`, `EventWaitHandle`, WPF `Dispatcher`, `System.Windows.MessageBox`.

Target:
- Avalonia `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)` on desktop.
- Platform lifecycle for Android.
- Cross-platform single-instance via file lock or platform-specific service.
- Replace `Environment.SpecialFolder.LocalApplicationData` assumptions with proper paths (`XDG_DATA_HOME` on Linux, `~/Library/Application Support` on macOS).
- Replace `MessageBox.Show` with `IDialogService`. **Implemented** as `AvaloniaDialogService` (`CCP.Avalonia/Platform/AvaloniaDialogService.cs`) on the **`MessageBox.Avalonia`** NuGet package (note: the package id is `MessageBox.Avalonia`; the API namespace is `MsBox.Avalonia`, e.g. `MessageBoxManager`).

### 7.2 System Tray

Current (`Services/Notifications/TrayIconService.cs`): `System.Windows.Forms.NotifyIcon`, `ContextMenuStrip`, `ToolStripMenuItem`, `SetForegroundWindow`.

Target: Avalonia built-in `TrayIcon` + `NativeMenu` in desktop head. Not applicable on mobile.

```xml
<TrayIcon Icon="/Assets/app-icon.ico" ToolTipText="CCP">
  <TrayIcon.Menu>
    <NativeMenu>
      <NativeMenuItem Header="Show Dashboard" Command="{Binding ShowWindowCommand}" />
      <NativeMenuItemSeparator />
      <NativeMenuItem Header="Exit" Command="{Binding QuitCommand}" />
    </NativeMenu>
  </TrayIcon.Menu>
</TrayIcon>
```

### 7.3 Global Input Hooks

Current: `SetWindowsHookEx` low-level keyboard/mouse hooks, `RegisterHotKey`, `GetAsyncKeyState`.

Target: abstract `IHotkeyProvider` / `IInputHook`. Windows keeps Win32 implementation. Linux uses X11/wayland evdev. macOS uses `CGEventTap` + accessibility permission. Mobile: impossible; disable.

Lockdown mode system-key suppression (`Alt+Tab`, `Win`, `Esc`, `Ctrl+Shift+Esc`) is **impossible on macOS/Android** and requires root/udev on Linux.

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

Current: `System.Windows.Forms.Screen.AllScreens`, `SystemParameters.WorkArea`, `GetDpiForMonitor`, `SetDisplayConfig`.

Target: Avalonia `Window.Screens` / `TopLevel.GetTopLevel(this).Screens`. Abstract `IScreenProvider` for headless Core.

Display mirroring (`SetDisplayConfig`) has no cross-platform API; gate as Windows-only.

**Multi-monitor (N screens) — REQUIRED, not "dual".** Treat "dual monitor" as a special case of **arbitrary N
monitors**. Every screen-spanning feature — mandatory video, flash, subliminal, spiral, pink fill, brain-drain,
bouncing text, bubbles, Chaos overlays, mind-wipe, etc. — must render correctly across **all** monitors at once,
**unless that feature is configured to a single display** (respect the per-feature display setting — don't span
when the user picked one screen).

Rules (the data is already on `AvaloniaScreenProvider`: per-monitor `Bounds`, `WorkingArea`, `Scaling`):
- **Iterate `Screens.All`, never `[0]`/`[1]`.** No assumption of exactly two monitors and no hard-coded
  primary+secondary. The `AvaloniaDualMonitorVideoService` name is a tell — generalize it to N (one surface per
  target monitor).
- **Size each surface to *its own* monitor.** A window/overlay on monitor M uses `M.Bounds` (position `M.Bounds.X/Y`,
  size from `M.Bounds`), so it fills exactly that screen. Because `Bounds` already encodes orientation, a **portrait**
  monitor (Height > Width) and a **landscape** monitor each get the right aspect automatically — a 3-monitor mix of
  1 landscape + 2 portrait (or vice-versa) must each look correct.
- **Scale per-monitor.** Use **each** monitor's own `Scaling` (DPI) for sizing/positioning math — never the primary's
  scale for a secondary. Video frame buffers/`WriteableBitmap`s are allocated per target surface; letterbox/scale
  the video to each monitor's aspect rather than stretching.
- **Optimize:** spawn a surface **only** on monitors the feature targets (all, or the chosen single display); pool
  and reuse surfaces across runs instead of create/dispose per effect; one decoder where the same frame is mirrored,
  blitted per surface. Don't allocate full-screen layered windows you don't need (ties to the GDI/heap exhaustion
  risk in §14.1).
- **React to display changes at runtime.** Handle monitor hotplug, resolution, and orientation changes
  (`Screens.Changed`) — re-layout/re-create surfaces; don't leave a window stranded on a removed monitor.

### 7.6 Desktop Wallpaper

Current: `SystemParametersInfo` (`SPI_SETDESKWALLPAPER`).

Target: abstract `IWallpaperProvider`. Windows only. macOS can use AppleScript/NSWorkspace; Linux uses `gsettings`/`feh` per DE.

### 7.7 Embedded Browser

Current: `Microsoft.Web.WebView2` in `Services/Browser/BrowserService.cs`.

Target options:
1. **Avalonia.Controls.WebView** — official cross-platform WebView (WebView2 on Windows, WPE WebKit on Linux, Android WebView). macOS uses WKWebView.
2. **CEF wrapper** (`CefGlue.Avalonia`, `CefNet.Avalonia`) for desktop Linux/macOS.
3. **System browser** launch via `xdg-open`/`open` where an embedded browser is unnecessary.
4. Keep WebView2 only in `CCP.WindowsOnly` for Windows parity.

Introduce `IBrowserHost` abstraction.

### 7.8 Imaging / Computer Vision

Current: OpenCvSharp4 + `runtime.win`, DirectShow/WinRT enumerators, ONNX Runtime CPU x64.

Target:
- Add platform runtimes: `OpenCvSharp4.runtime.ubuntu.*`, `OpenCvSharp4.runtime.osx.*`.
- On mobile, OpenCvSharp does not ship runtimes; use Android Camera2 APIs.
- Replace DirectShow/WinRT enumerators with V4L2 on Linux; macOS uses AVFoundation.
- Add ONNX Runtime mobile runtimes.
- Replace `System.Drawing` with SkiaSharp / ImageSharp.

### 7.9 Secure Storage

Current: `ProtectedData.Protect/Unprotect` with `DataProtectionScope.CurrentUser`.

Target: `ISecretStore` abstraction.
- Windows: DPAPI (keep existing).
- macOS: Keychain (`Security` framework).
- Linux: libsecret/secret-tool or encrypted file with user-only permissions.

Existing encrypted tokens will not decrypt on other OSs; plan re-authentication or migration path.

### 7.10 File Dialogs

Current: `Microsoft.Win32.OpenFileDialog` / `SaveFileDialog`, `System.Windows.Forms.FolderBrowserDialog`.

Target: Avalonia `IStorageProvider` (`OpenFilePickerAsync`, `SaveFilePickerAsync`, `OpenFolderPickerAsync`).

### 7.11 Updates

Current: Inno Setup installer, Registry path discovery, `wmic` to kill WebView2 processes.

Target: `IUpdateInstaller` abstraction.
- Windows: keep installer path discovery (without `wmic`).
- macOS: Sparkle or manual DMG.
- Linux: AppImage/snap/flatpak.


---

## 8. Phase-by-Phase Migration Roadmap

> **Historical sequencing — see §1A for current status.** This roadmap captures the *original* plan-of-record
> and its effort estimates. Most of Phases 0–3 and large parts of 4–8 are already done, and a few specifics
> here never happened as written (e.g. the "temporary `CCP.WpfShim`" in Phase 1 was replaced by
> `CCP.WindowsOnly` + the live WPF head; Phase 3's single `CCP.Avalonia.Desktop` became four heads per §3.1).
> Use §1A + `docs/avalonia-migration-task-board.md` as the live to-do list; read this section for rationale and
> remaining-phase shape, not as an unstarted checklist.

### Phase 0 — Cleanup (Days)

1. Remove dead packages: `SharpDX.*`, `OpenAI-DotNet`, `OllamaSharp`.
2. Verify `MahApps.Metro` / `IconPacks` usage; remove if unused.
3. ~~Remove `Microsoft.WindowsAppSDK` exclusion hack.~~ **Corrected:** keep and pin it (`ExcludeAssets="all" PrivateAssets="all"`) — it is a required transitive of LibVLCSharp and prevents a WebView2 `NU1605` downgrade. See §5.1.
4. Delete `CopyLibVLCAfterPublish` and `IncludeWebView2LoaderInPublish` from shared project; move to Windows head later.
5. Add platform analyzers; remove `NoWarn$(NoWarn);CA1416`.
6. Document feature matrix: mark features as portable vs. Windows-only.

### Phase 1 — Carve Out `CCP.Core` (1–2 Weeks)

1. Create `CCP.Core` class library targeting `net8.0`.
2. Move into Core:
   - All POCO models (`Models/`).
   - Localization runtime (`Loc.cs`, `LocalizationManager.cs`, language JSON as content).
   - AI orchestration (`Services/AIService/*`).
   - Gamification, Chaos economy rules, session engine.
   - Networking/auth transport, crypto, moderation.
   - Content/mod system logic.
   - JSON contracts and settings POCOs.
3. Introduce platform seams (see section 3.3).
4. Replace `DispatcherTimer` with `IScheduler`/`IUiTimer` abstraction.
5. Replace `MessageBox.Show` with `IDialogService`.
6. Replace `pack://` URI loading with `IAssetLoader`.
7. Replace DPAPI with `ISecretStore`.
8. Keep WPF app compiling by implementing seams with WPF/Windows shims in a temporary `CCP.WpfShim`.

### Phase 2 — Prove Core Off-Windows (1 Week)

1. Build `CCP.Core` on Linux and macOS in CI.
2. Run headless unit/integration tests for session engine, AI parsing, gamification, mod loading.
3. Ensure DPAPI, screen, audio, and UI dispatch seams are the only Windows leaks.

### Phase 3 — Create Avalonia Solution (1 Week)

1. Create projects:
   - `CCP.Avalonia` (shared UI, `net8.0` or `net10.0`)
   - `CCP.Avalonia.Desktop`
   - `CCP.Avalonia.Android`
2. Add Avalonia v12 + LibVLCSharp packages per head.
3. Implement `App.axaml`, `MainWindow.axaml`, platform `Program.cs` / `MainActivity.cs`.
4. Set up solution build for all heads.

### Phase 4 — Migrate XAML & UI (4–8 Weeks)

1. Convert `Window`/`UserControl` files from WPF to Avalonia namespaces.
2. Replace MahApps controls with Avalonia equivalents.
3. Convert `ResourceDictionary` entries to Avalonia `Styles`/`ResourceDictionary` syntax.
4. Replace `pack://application:,,,/...` with `avares://CCP.Avalonia/...`.
5. Mark assets as `AvaloniaResource` where appropriate.
6. Port `MainWindow.xaml` (13,333 LOC) by splitting into views/view models.
7. Port dialogs, feature controls, avatar window, chaos overlays.
8. Implement `IUiDispatcher`, `IOverlaySurface`, `IWindowChrome` for Avalonia.

### Phase 5 — Replace Media & Audio (2–3 Weeks)

1. Replace `LibVLCSharp.WPF.VideoView` with `LibVLCSharp.Avalonia.VideoView` in desktop UI.
2. Implement `IVideoSurface` for mobile using native `VideoView` wrappers.
3. Replace NAudio with `IAudioPlayer` + cross-platform implementation.
4. Port `DualMonitorVideoService` memory-render path.
5. Replace WebView2 video in `EnhancementPlayerWindow` with LibVLC `VideoView`.
6. Replace `XamlAnimatedGif` with `AvaloniaGif` or SkiaSharp animation.
7. Replace `SharpVectors` with `Svg.Skia`.

### Phase 6 — Replace OS-Shell Features (2–3 Weeks)

1. `ITrayIcon` → Avalonia `TrayIcon` on desktop, no-op on mobile.
2. `IHotkeyProvider` / `IInputHook` → Windows-only Win32 hooks, limited macOS/Linux, no-op mobile.
3. `IWallpaperProvider` → Windows-only.
4. `IBrowserHost` → WebView2 on Windows, Avalonia WebView or system browser elsewhere.
5. `IWindowChrome` / `IOverlaySurface` → Avalonia window styling + platform click-through.
6. `IThumbnailProvider` → SkiaSharp decoding + optional OS thumbnails.
7. `IFrameSource` / `ICaptureService` → Windows GDI, Linux/macOS platform APIs.

### Phase 7 — Build & Publish Pipeline (1–2 Weeks)

1. Define `RuntimeIdentifiers` per head:
   ```xml
   <RuntimeIdentifiers>win-x64;win-arm64;linux-x64;linux-arm64;osx-x64;osx-arm64</RuntimeIdentifiers>
   ```
2. Desktop: keep `PublishSingleFile` + `SelfContained` optional.
3. Android: standard `net10.0-android` build; no single-file; enable trimming with Avalonia trimming roots.
4. Add CI matrix builds for each RID and mobile simulator tests.
5. Set up code signing and notarization for macOS.
6. Set up Android keystore and app bundle publishing.

### Phase 8 — Mobile Feature Gating & Adaptation (2–4 Weeks)

1. Disable on Android:
   - Overlays (full-screen effects, bubbles, subliminals).
   - Global hooks and system-key suppression.
   - System tray.
   - Desktop wallpaper override.
   - GDI/desktop capture.
   - Screen OCR.
   - Multi-monitor video mirroring.
2. Design mobile-first shell:
   - Tabs or single-window navigation.
   - Touch-optimized controls.
   - Native camera + LibVLC for Lab/webcam features.
3. Adapt background audio/execution restrictions on Android.
4. Address Google Play content policies early.

---

## 9. Platform-Specific Deployment Notes

### Windows

- Easiest path: keep many Win32 features behind `CCP.WindowsOnly`.
- Can retain WebView2, global hooks, DWM tinting, wallpaper override, NAudio.
- Use `net8.0-windows10.0.19041.0` for the Windows head/shim.
- Single-file publish with native libs excluded works as today.

### Linux

- No official `VideoLAN.LibVLC.Linux` NuGet; rely on system `libvlc` or bundle native `.so` files.
- Tray icon works via `StatusNotifierItem`/AppIndicator but requires `libdbusmenu` on some distros.
- Window transparency and click-through depend on compositor (X11/Wayland); may be limited.
- Global hooks are not reliable; lockdown/security features must degrade gracefully.
- Use `xdg-open` for system browser fallback.

### macOS

- `VideoLAN.LibVLC.Mac` provides native libvlc for x64.
- For Apple Silicon (ARM64), ship a custom `libvlc.dylib` + plugins extracted from VLC.app.
- Title-bar theming and transparent overlays require native NSWindow interop.
- Global hotkeys possible via `NSEvent.AddGlobalMonitorForEventsMatchingMask`; system-key suppression is not.
- Secrets: Keychain.

### Android

- Avalonia Android head requires `Avalonia.Android` and `MainActivity`.
- No system tray, no global hooks, no wallpaper override.
- Use `VideoLAN.LibVLC.Android`.
- Camera/ML inference via native bindings or platform APIs.

---

## 10. Risk Register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| ~~`LibVLCSharp.Avalonia` not yet compatible with Avalonia v12~~ **RESOLVED** | — | — | `LibVLCSharp.Avalonia` 3.9.7.1 builds and runs against Avalonia 12.0.4; the desktop solution builds clean. No longer a risk. |
| 133k LOC UI rewrite is larger than estimated | High | High | Extract Core first; port screen-by-screen; keep WPF app alive during migration. |
| NAudio audio ducking has no cross-platform equivalent | High | Medium | Abstract `IAudioDucker`; disable or implement per-platform. |
| WebView2 browser features break on Linux/macOS | High | High | Abstract `IBrowserHost`; use Avalonia WebView or system browser; feature-gate if needed. |
| Global hooks / lockdown suppression impossible on macOS/Linux/mobile | High | High | Document limitations; gate lockdown as Windows-only. |
| DPAPI-encrypted tokens fail on other OSs | High | Medium | Force re-authentication or migrate to OS keychain abstraction. |
| Native library discovery fails on Linux/macOS | Low (mitigated) | High | **Implemented:** `LibVLCNativeDiscovery.Initialize()` (in `CCP.Avalonia.Desktop`) runs before `new LibVLC()` and searches system paths; CI installs `libvlc-dev vlc` on Linux. Still test on clean VMs and verify macOS ARM64 dylib bundling. |
| macOS ARM64 lacks official LibVLC NuGet | Medium | High | Ship custom ARM64 native build extracted from VLC.app. |
| Single-file + native libs incompatible with mobile | Medium | Medium | Use normal publish for mobile; exclude native libs from single-file on desktop. |
| Mobile app-store content policies | Medium | High | Review policies early; prepare feature gating and age-gating. |
| Layered/click-through windows fail on Linux/Wayland | Medium | Medium | Gate overlay transparency as desktop-only; provide degraded mode. |
| Trimming breaks Avalonia XAML/reflection | Medium | Medium | Add trimming roots/AOT configuration; test per platform. |

---

## 11. Quick Win Checklist

> **Historical — most of these are done (see §1A).** Dead-package removal, `CCP.Core`, `IUiDispatcher`,
> `ISecretStore`, the `LibVLCSharp.Avalonia` swap, and `App.axaml` are all complete. Retained for the original
> effort/value rationale; the live to-do is `docs/avalonia-migration-task-board.md`.

| Task | Effort | Value |
|---|---|---|
| Remove SharpDX / OpenAI-DotNet / OllamaSharp packages | Hours | Cleans native footprint. |
| Create `CCP.Core` and move portable models + AI + gamification | Days–1 week | Biggest leverage; enables headless tests. |
| Introduce `IUiDispatcher` + scheduler abstraction | Days | Unlocks nearly every mixed service. |
| Abstract `ISecretStore` away from DPAPI | 1–2 days | Unlocks Linux/macOS auth. |
| Replace `LibVLCSharp.WPF` with `LibVLCSharp.Avalonia` in a spike | 1–2 days | Proves cross-platform video. |
| Rewrite `App.xaml`/`App.xaml.cs` for Avalonia | Days | Foundation of new UI. |
| Port `MainWindow.xaml` to Avalonia (split into views) | Weeks | Largest piece of work. |

---

## 12. Recommended First Steps

> **Historical.** Steps 2–6 below are **already done** — see §1A. They are retained to show the original
> bootstrapping order. If you are picking the project up *today*, start instead from the §19.3 sync backlog
> and the Phase-4 parity work in `docs/avalonia-migration-task-board.md`.

1. **Read existing portability specs:** `openspec/PORTABILITY_REPORT.md` and `openspec/PORTABILITY_RUBRIC.md` already identify exact seams.
2. **Remove dead dependencies:** `SharpDX.*`, `OpenAI-DotNet`, `OllamaSharp`.
3. **Create `CCP.Core`:** move engine/models and introduce platform seams.
4. **Compile Core on Linux/macOS:** cheapest proof of portability.
5. **Build an Avalonia spike:** single window playing a LibVLC video in a transparent topmost window to validate overlay assumptions.
6. **Decide browser strategy early:** WebView2 on Windows only vs. true cross-platform control affects Deeper/auto-discovery heavily.

---

## 13. Build & Test Strategy

Every phase must end with a **build checkpoint** and a **test checkpoint**. The goal is to never have a "big bang" integration; the WPF app must stay runnable until the Avalonia desktop app can fully replace it.

### 13.1 Testing Pyramid

| Layer | Purpose | Tools / Approach |
|---|---|---|
| Unit | Models, parsers, gamification rules, session state machines, JSON contracts | xUnit / NUnit + `CCP.Core` only |
| Integration | Service orchestration, AI pipeline, mod loading, asset resolution | TestHost or custom harness in `CCP.Core.Tests` |
| UI / Functional | Window creation, navigation, control binding, media playback | Avalonia.Headless, Appium, or manual QA |
| Performance | Startup time, memory usage, effect frame rates, LibVLC memory callbacks | BenchmarkDotNet, dotMemory, custom telemetry |
| Platform | Native lib discovery, screen/DPI, audio device enumeration, file dialogs | CI matrix on Windows/Linux/macOS + simulators |

### 13.2 Build Checkpoints by Phase

| Phase | Build Checkpoint | Success Criteria |
|---|---|---|
| Phase 0 | WPF app builds with dead packages removed. | No build warnings from removed packages; all existing tests pass. |
| Phase 1 | `CCP.Core` + WPF shim build; legacy WPF app still runs. | `CCP.Core` compiles on Windows, Linux, and macOS; WPF app launches and plays a video. |
| Phase 2 | `CCP.Core` builds on Linux/macOS CI with no Windows-only references. | `dotnet build` passes on `ubuntu-latest`, `macos-latest`, and `windows-latest`. |
| Phase 3 | Avalonia solution builds for all heads. | `CCP.Avalonia`, `CCP.Avalonia.Desktop`, `CCP.Avalonia.Android` all compile. |
| Phase 4 | Avalonia desktop app builds and launches. | Main window renders; navigation/tabs work; no runtime XAML exceptions. |
| Phase 5 | Media/audio pipeline builds on all desktop RIDs. | LibVLC initializes; video plays; audio SFX plays on Windows/Linux/macOS. |
| Phase 6 | OS-shell feature stubs build on all platforms. | Tray icon on desktop; hooks on Windows; no crashes on Linux/macOS without hooks. |
| Phase 7 | CI produces signed/publishable artifacts for all desktop RIDs and Android. | `dotnet publish` succeeds for `win-x64`, `linux-x64`, `osx-arm64` (done). Android: CI `dotnet build`s the head today; **AAB packaging still to wire** (`dotnet publish -f net10.0-android`). |
| Phase 8 | Android head builds and deploys to simulators/devices. | Android app launches; reduced feature set loads; camera + LibVLC work. |

### 13.3 Test Checkpoints by Phase

| Phase | Test Checkpoint | What to Verify |
|---|---|---|
| Phase 0 | Regression test baseline | Run existing manual QA script on WPF app; capture startup time and memory baseline. |
| Phase 1 | Core unit tests | Session engine, AI command parsing, mod manifest parsing, settings serialization. |
| Phase 2 | Cross-platform Core build | No `System.Windows` leaks; all Core tests pass on Linux/macOS. |
| Phase 3 | Avalonia head smoke test | `Program.Main` runs; `App.axaml` parses; `MainWindow` shows without crash. |
| Phase 4 | UI parity matrix | For every ported screen: navigation, data binding, validation, theme, localization. |
| Phase 5 | Media matrix | Play MP4/MKV/WebM, audio-only files, dual-monitor mirror, GIF animations, SVG emojis. |
| Phase 6 | OS integration matrix | Single instance, file dialogs, tray menu, hotkeys (Windows), wallpaper (Windows), browser host. |
| Phase 7 | Publish & deploy matrix | Single-file desktop starts; native libs load; mobile apps install and launch. |
| Phase 8 | Mobile acceptance | Touch navigation, camera permission, background audio behavior, reduced feature set. |

### 13.4 Continuous Quality Gates

- **Build gate:** every PR must build all heads on Windows, Linux, and macOS.
- **Test gate:** Core unit/integration tests must pass on all three desktop OSs.
- **Static analysis gate:** enable nullable reference types in Core; enable `CA1416` again and prove no cross-platform leaks.
- **Performance gate:** startup time, working-set memory, and effect/animation frame rates must **match or beat**
  the WPF baseline — never regress (the bar is 1:1-or-better, §1A). Capture the WPF baseline first (§13.3 Phase 0),
  then hold each ported feature to it; treat a regression as a defect, and prefer beating it via the §14.2 levers
  (Skia composition, async startup, overlay/audio pooling, off-UI-thread decode) or §14.4 (actively research / adopt
  a faster library — a default behavior, not just when a path is heavy). The bar is **low-end-machine smoothness**,
  not just dev-box FPS.
- **Accessibility gate:** keyboard navigation and screen-reader labels for every new view.

### 13.5 Visual Parity Verification (reference screenshots)

The ported UI must *look* like the original, not just compile. Early Avalonia ports shipped small-but-real visual
mistakes that a build can't catch — most visibly **raw localization keys rendered as text** (`tab_dashboard`,
`lbl_…​` instead of "Dashboard"), **missing or unstyled feature cards**, a missing central emblem and avatar tube,
wrong fonts, and broken spacing/theme. Verify by eye against reference images — **not necessarily every iteration,
but at least once per tab/screen before marking it ✅.**

**Reference assets** live in the repo-root folder **`img state/`**. The dashboard *layout* is shared across all
themes; the *palette/avatar/card art* differ **per theme** (the top-left mod switcher — §15.11). There are **five
themes**: **CCP Default, Bambi, Sissy Hypno, Droneification, Circe Lock**. Compare each ported screen against the
reference for the **matching active theme**:

| File | Theme it shows | Use |
|---|---|---|
| `default good view.jpg` | **CCP Default** | Target look for the default theme. |
| `good view.png` | **Sissy Hypno** (pink) | Target look for the Sissy theme. |
| `bambi sleep good view.jpg` | **Bambi Sleep** | Target look for the Bambi theme. |
| `drone good view.jpg` | **Droneification** | Target look for the Drone theme. |
| `circe lock good view.jpg` | **Circe Lock** | Target look for the Circe Lock theme. |
| `bad view.jpg`, `bad view2.png` | — | Known-wrong examples — treat their defects as an explicit "do NOT ship" checklist (raw loc keys, empty/missing card grid, unstyled wrapped tab list, broken layout). |
| `avalonia_screenshot_*.png` | — | Captures of the current Avalonia state for before/after comparison. |

> All **five** theme references now exist (maintainer-supplied). Filenames have spaces — quote them.

> **Prerequisite:** the `img state/` folder is currently **untracked** in git — commit it so worktree-isolated
> swarm agents (§20.3) actually have the references. The folder name contains a space, so **quote the path**
> (`"img state/good view.png"`). Consider renaming to `img-state/` or `docs/reference-screenshots/` to avoid the
> space entirely.

**Procedure per tab/screen:**
1. Run the **Avalonia Windows head** and screenshot the tab you ported.
2. Compare against (a) the reference image **for the active theme** — all five exist now: `default good view.jpg`
   (CCP Default), `good view.png` (Sissy Hypno), `bambi sleep good view.jpg` (Bambi), `drone good view.jpg`
   (Droneification), `circe lock good view.jpg` (Circe Lock) — and (b) a screenshot of the **same tab in the same
   theme in the running WPF app** — the WPF app is the visual + behavioral
   source of truth. Launch it and capture, or drop OG captures into `img state/` and reuse them.
3. Check the recurring failure modes specifically:
   - **No raw `{loc:Str}` keys leaking as text** — every label localized (§17.2); tab headers translated.
   - **All cards/controls present and themed** — accents match the **active theme** (pink for Sissy Hypno, as in the
     reference), correct fonts (incl. Fredoka), icons present, central emblem + avatar tube rendered. Colors come
     from theme resources, not hard-coded hex (§15.11).
   - **Correct grid/spacing/margins/alignment** vs. the reference.
   - **Theme switching works** — switch the top-left mod selector to at least one other mod (e.g. Drone = green)
     and confirm the whole UI re-skins; the look must be right for *every* theme, not just Sissy Hypno (§15.11).
4. Log every mismatch as a row in `docs/avalonia-ui-parity-matrix.md` and fix it before closing the lane.

**Self-serve references — you can launch the WPF app yourself.** Don't block on the maintainer, and **don't guess**.
Whenever you're unsure how *any* tab, dialog, popup, or view looks or should look/behave — not just a missing theme
reference — go look at it in the running WPF app. It runs on Windows today and is the visual + behavioral source of
truth, so capture whatever you need:
- Build & run it (from the `ConditioningControlPanel/` dir, same cwd as the other build commands):
  `dotnet run --project ConditioningControlPanel.csproj` — the WPF head; it stays runnable throughout the migration
  (§13). Run the Avalonia head side-by-side (`dotnet run --project CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj`)
  to compare directly.
- Use the **top-left mod switcher** to select the theme you're verifying (CCP Default / Bambi / Sissy Hypno /
  Droneification / Circe Lock), open the tab/dialog/popup in question (in whatever state you're unsure about —
  hover, expanded, error, populated vs. empty), and **screenshot it**. This is the answer to "how should this look?"
  for any view, not only the dashboard.
- Treat that capture as the reference (the 5 dashboard theme references already exist in `img state/`; this is for
  *other* views/states you're unsure about). Save keepers into `img state/` so the swarm and later runs reuse them.
- This is a **local Windows** step (needs a desktop session + screenshot tooling); it isn't available in headless
  CI, so it's part of per-lane local verification, not the build gate.

### 13.6 Functional / Behavioral Parity (exercise it — don't just render it)

**A tab that *looks* right can still *do* nothing — "builds + looks right" is NOT done.** Real defects from the
first port: the **START button doesn't launch the mode**, the **left avatar is inert**, "Down the Rabbit Hole"
progression doesn't run, and color **overlays (pink fill, spiral) block input** instead of being click-through.
These pass a build and a screenshot but fail the user. Every interactive element must actually work, verified by
*using* it.

**The standard is 1:1-or-better (§1A):** the ported feature must do *exactly* what the WPF feature does — same
inputs, same outputs, same edge cases — at **equal-or-better performance**. Not a simplified or approximate
version. When in doubt about expected behavior, run the WPF app and match it (§13.5 self-serve). A stub that
"renders but no-ops" is a defect, not a milestone.

Per lane, before ✅:
- **Drive the primary action end-to-end** in the running Avalonia head and confirm it does what the WPF app does:
  START actually launches the mode (flashes/video/overlays/avatar begin), Save/Exit work, every feature card
  toggles **and activates its service**, the avatar reacts, dialogs commit and persist.
- **Confirm commands are really wired** — controls bound to live `ICommand`s on the ViewModel that call the Core/
  platform service, not empty stubs or `TODO`s. A fully-styled button bound to a missing/no-op command is the
  single most common failure here; grep the lane for stub commands and `NotImplemented`.
- **Compare behavior side-by-side** with the WPF app (§13.5 self-serve), not just pixels.
- **Overlays must be input-transparent** (pink fill, spiral, subliminal, flash, brain-drain): they are *visual
  only* and must never capture input — not the app's own buttons, and **not other applications behind them**. See
  the hardened spec in §7.4 and the gotcha in §21.
- Anything inert / input-blocking → a parity-matrix row (or task-board item), fixed before closing the lane.

> The maintainer reported a batch of these (START, avatar, progression, overlay click-through, "and a few more"),
> and a code-audit sweep found many more that the code itself marks as stubbed — including **account login being
> entirely no-op (so all premium/Patreon-gated features are locked)**, the Chaos/"Rabbit Hole" run economy being
> largely placeholder, content-pack management, several feature-card editors, and webcam tracking. The full,
> grouped list is `docs/avalonia-migration-task-board.md` → **Known Functional Gaps** (treat as P0). Find more with
> `grep -rinE "TODO|stub|not ported|not wired|placeholder|NotImplemented|No-?op" CCP.Avalonia --include=*.cs`, and
> by actually exercising every feature — the markers are a floor, not a ceiling.

---

## 14. Quality & Improvement Goals

The rebuild is not just a port. It must leave the codebase faster, more stable, more testable, and more maintainable than the WPF original.

### 14.1 Stability Improvements

| Current Pain Point | Root Cause | Migration Fix |
|---|---|---|
| Render-thread deadlocks (Application Hang 1002) | Layered WPF popups + avatar tube share single render thread | Avalonia/Skia render model; separate effect surfaces; remove ComboBox/tooltip layering hacks. |
| GDI/desktop heap quota exhaustion | Too many full-screen layered windows | Pool and reuse overlay surfaces; limit concurrent surfaces; gate heavy effects. |
| Dispatcher hang crashes | UI thread blocked by synchronous service init | Move all service initialization off the UI thread; use async startup with progress reporting. |
| Cascading crash dialogs | `MessageBox.Show` on failing dispatcher | Replace with async `IDialogService`; no nested dispatcher pumps during error handling. |
| Memory leaks from static services | 88 static service references in `App.xaml.cs` (2,810 LOC) | Replace static locator with scoped DI container; implement `IAsyncDisposable` for services. |
| WPF airspace issues | Native video HWND behind WPF controls | Avalonia `VideoView` integrates into the scene graph; no HWND airspace. |

### 14.2 Performance Improvements

| Area | Current | Target |
|---|---|---|
| Startup time | Custom `Main` synchronously initializes Serilog, services, Patreon, etc. | Async startup pipeline; lazy service initialization; splash screen with real progress. |
| UI render thread | Single WPF render thread can deadlock | Avalonia/Skia composition; 60 FPS independent render thread. |
| GIF animation | XamlAnimatedGif decodes on UI thread | SkiaSharp/ImageSharp decode on thread pool; upload frames to GPU. |
| Audio SFX | `WaveOutEvent` per sound can exhaust devices | Pool audio players; use LibVLC for short SFX on all platforms. |
| Screen enumeration | `System.Windows.Forms.Screen.AllScreens` cached with Win32 P/Invoke | Avalonia `Screens` API with built-in change notifications. |
| Asset loading | `pack://` URI resolution + embedded resources | `avares://` + `AvaloniaResource`; lazy load large assets. |
| Webcam tracking | DirectShow/WinRT enumerator + WPF dispatcher | Cross-platform capture abstraction; frame processing on background thread. |

### 14.3 Maintainability Improvements

- **Dependency injection:** replace static service locator with `Microsoft.Extensions.DependencyInjection`.
- **MVVM:** split 13,333 LOC `MainWindow.xaml`/`MainWindow.xaml.cs` into small Views + ViewModels.
- **Async/await:** replace fire-and-forget tasks with `CancellationToken` propagation and `IAsyncDisposable`.
- **Configuration:** move hard-coded paths and constants to `appsettings.json` + options pattern.
- **Logging:** keep Serilog but add structured logging with correlation IDs across async operations.
- **Feature flags:** gate experimental/Windows-only features so they can be toggled per platform or user.

### 14.4 Research & Library Adoption (actively hunt for faster solutions)

**Actively seeking the fastest/lightest approach is a standing behavior, not a last resort.** For *every* feature —
even one that already works — ask "is there a faster or lighter way?" and **research the web** to find out *before*
settling on a first cut, and again whenever a path feels laggy or heavy. The implementer is **not limited to prior
knowledge**: default to checking the idiomatic, modern, performant Avalonia v12 way — Avalonia docs (§23 verified
links), LibVLCSharp, SkiaSharp, GitHub issues/discussions, release notes, benchmarks, blog posts. Don't ship a slow
hand-rolled path when a clean fast one is documented.

**Actively look for, and adopt, new libraries that make the app faster or lighter** (lower CPU/GPU/RAM, less lag) or
that replace a heavy/fragile hand-rolled path — this is encouraged and expected. It is consistent with the perf prime
directive (§1A) and is not a ponytail violation: ponytail forbids *needless* abstraction, not *useful* dependencies.
Guardrails:

- The lib must **earn its weight** — adopt one to remove a slow/fragile path or measurably cut the footprint, not to
  save a few lines (that stays stdlib/framework per §3.3).
- Prefer **well-maintained, cross-platform, permissively-licensed, actively-released** packages; **pin** the version.
- Keep the total dependency set lean; **never regress Windows behavior** or bloat startup/working-set.
- Record each new/changed dependency **and the reason** (what it speeds up or replaces, with a before/after number
  where measurable) in the task board, so the swarm doesn't add overlapping deps.

---

## 15. Missed Architectural Concerns

The following concerns were not covered in the subsystem list but must be addressed during the rebuild.

### 15.1 Static Service Locator in `App.xaml.cs` — ✅ DONE (Avalonia heads)

> Resolved in the Avalonia tree (see §1A). The 88 statics are replaced by
> `Microsoft.Extensions.DependencyInjection` in `CCP.Avalonia/ServiceCollectionExtensions.cs` with per-head
> overrides via `App.ConfigurePlatformServices`. The notes below remain the design target; the only open item
> is `IHostedService` for long-running background services.

Current: **88** `public static` service properties on the `App` class (e.g., `App.Video`, `App.Audio`, `App.Chaos`), in a 2,810-LOC `App.xaml.cs`.

Target:
- Register all services in `Microsoft.Extensions.DependencyInjection`.
- Use constructor injection in ViewModels and platform services.
- Keep a small `IServiceProvider` facade only where code-behind cannot easily accept DI.
- Implement `IHostedService` for long-running background services (Autonomy, Session engine, Remote control).

### 15.2 Mod & Asset Pipeline

Current:
- Built-in mods (`DroneMod/drone-mode.ccpmod`, `LockedMod/locked-resources.ccpmod`) are extracted to `%LOCALAPPDATA%` on first launch.
- Assets resolved from `AppContext.BaseDirectory\Assets\Chaos\...` with Windows-style paths.

Target:
- Keep `.ccpmod` format; extract to platform-appropriate user data folder.
- Use `Path.Combine` and `AppContext.BaseDirectory` consistently; no hard-coded backslashes.
- Mark mod assets as `AvaloniaResource` or `Content` depending on whether they ship in the bundle or are downloaded.
- Validate mod manifests against JSON schema after extraction.

### 15.3 Settings & Data Backward Compatibility

Current settings stored as JSON in `%LOCALAPPDATA%\ConditioningControlPanel\settings.json` (or similar).

Target:
- Read existing WPF settings format on first launch and migrate to new schema.
- Version settings schema; provide migration classes (`ISettingsMigration`).
- Preserve user data path across updates; do not move files unnecessarily.
- Back up old settings before migration.
- **One user-data folder, matching legacy.** All user data must resolve to the legacy WPF location
  `%LOCALAPPDATA%\ConditioningControlPanel` (Local), not a new one — the assembly rename
  (`ConditioningControlPanel` → `CCP.Desktop.*`) must **not** move it. ⚠️ **Known bug (task board #L):**
  `AvaloniaAppEnvironment.ApplicationDataPath` resolves to **Roaming** (`%APPDATA%`) while `UserDataPath` and
  legacy use **Local** — so session logs / custom sessions / moderation counter land in the wrong folder and look
  lost. Collapse to one Local path (ponytail: don't keep two path properties that point at two folders).

### 15.4 Logging, Crash Reporting, and Telemetry

Current:
- Serilog file sink with daily rolling.
- `UiHangWatchdog` writes minidumps via `dbghelp.dll`.
- Crash dialogs log details locally.

Target:
- Keep file logging; add OS-native crash reporting where appropriate (e.g., App Center, Sentry, or plist crash logs on macOS).
- Replace `dbghelp.dll` with cross-platform crash handler or platform-specific minidump APIs.
- Ensure no PII in logs; continue security practice of logging at `Information` level.
- Add telemetry for startup time, feature usage, and crash-free sessions (opt-in).

### 15.5 Network, Cache, and Offline Behavior

Current:
- Patreon/Discord auth, profile sync, leaderboard, remote control, catalogue lookup require cloud identity.
- Offline mode exists but may assume Windows paths.

Target:
- Abstract `IHttpClientFactory` and `IConnectivityService`.
- Cache catalogue and enhancement library metadata locally with expiration.
- Ensure offline mode works on all platforms without cloud identity.
- Handle certificate/network errors gracefully on mobile.

### 15.6 Webcam Privacy Contract

Current contract: frames stay on device, never transmitted, never written to disk.

Target:
- Preserve contract; document it in `CCP.Core`.
- Use platform camera APIs on mobile; avoid cloud ML APIs.
- Add permission handling for camera/microphone on all platforms.

### 15.7 AI Service Thread Safety

Current AI services (`AIService`, `AiCommandService`, `CompanionService`, `PersonalityService`) may dispatch to UI thread synchronously.

Target:
- Make AI pipeline fully async with `CancellationToken`.
- Run inference/HTTP calls on thread pool; marshal results to UI via `IUiDispatcher`.
- Add rate limiting and queueing to prevent UI flooding.

### 15.8 Chaos Effects Performance

Current Chaos overlays spawn many windows and use WPF animation/GDI.

Target:
- Pool overlay windows instead of creating/disposing per effect.
- Use Avalonia `CompositionCustomVisual` or SkiaSharp for particle/spiral effects instead of many WPF shapes.
- Limit concurrent effects based on available GPU memory.

### 15.9 File Paths and Case Sensitivity

Current code uses Windows path separators and case-insensitive assumptions.

Target:
- Use `Path.Combine`, `Path.DirectorySeparatorChar`, and `StringComparison.Ordinal` consistently.
- Test asset loading on case-sensitive Linux file systems.
- Normalize locale/emoji file names before saving.

### 15.10 Single-Instance Behavior

Current: named `Mutex` + `EventWaitHandle` + handoff file in `%LOCALAPPDATA%`.

Target:
- Abstract `ISingleInstanceService`.
- Windows: keep named mutex/event.
- Linux/macOS: use file lock (`FileStream.Lock`) + Unix domain socket or signal file.
- Mobile: not applicable (OS controls single instance).

### 15.11 Per-Mod Theming / Dynamic Palette (the top-left mod switcher re-skins the app)

**This was being overlooked and is the root cause of several "UI looks wrong" reports.** The top-left selector
(`ModSelectorCombo`) is the **mod switcher**, and *each mod is a theme/skin* with its own palette (and avatar/card
art). The shipped themes are **CCP Default, Bambi, Sissy Hypno, Droneification, and Circe Lock** — each looks
distinct. The app's accent palette is **not fixed** — it is driven by the active mod's manifest and applied at
runtime:

- Each `.ccpmod`/mod manifest carries a `"theme"` block (e.g. `drone_mod.json` → `"accentColor": "#00FF41"` — Drone
  is **green**). Sissy Hypno is the default **pink** (`#FF69B4`), which is what `img state/good view.png` shows.
- WPF reads it via `App.Mods.GetAccentColorHex()` / `…DarkColorHex()` / `…LightColorHex()` / `GetSecondaryColorHex()`
  and `MainWindow.RefreshThemeAwareElements()` (`MainWindow.xaml.cs:1128`) **rewrites `Application.Current.Resources`**
  Color/Brush entries so the entire UI re-skins on every mod switch.

**Port requirement:**
- Reproduce the apply step: a small theme applier (e.g. an `IThemeService` / mod-theme bridge) that, on mod change,
  pushes the active mod's accent set into the Avalonia `Application.Current.Resources` (the same keys defined in
  `App.axaml`: `PinkColor`/`DarkPinkColor`/accents → and their `*Brush` forms).
- **Views must consume accents via `DynamicResource`, never hard-coded hex**, so they re-skin live. Audit ported
  XAML/code-behind for literal `#FF69B4`/`#FF1493` etc. and replace with the resource keys.
- **Current gap:** `CCP.Avalonia/App.axaml` hard-codes the palette as fixed `<Color>` keys and there is no Avalonia
  equivalent of `RefreshThemeAwareElements()` — so the port currently renders only the Sissy/pink skin and does
  **not** re-skin on mod switch. Closing this is required for parity. See also the visual-parity note in §13.5.

---

## 16. Feature Flags & Gradual Rollout

> **Reality check — the implemented approach diverged from this section.** Cross-platform UI ships as
> **separate executable heads** (`CCP.Avalonia.Desktop.Windows/.Linux/.macOS`, `CCP.Avalonia.Android`), not as a
> runtime toggle inside the WPF app, so **`UseAvaloniaUI` is moot** and none of the flags below exist in code
> (verified: zero references). Platform branching is done with `IPlatformCapabilities` + `OperatingSystem.IsX()`
> in DI, and per-head DI overrides. Treat the table below as *optional* future flags for the few cases that need
> a runtime toggle (e.g. validating `IAudioPlayer` or an Avalonia WebView on Windows); wire any you actually want
> through `AppSettings` + `IPlatformCapabilities`. The rollout *phases* (alpha → desktop beta → mobile beta → GA)
> still hold; just driven by which head you ship, not by `UseAvaloniaUI`.

| Flag (proposed, not yet implemented) | Purpose | Default |
|---|---|---|
| `UseAvaloniaUI` | Launch Avalonia desktop shell instead of WPF | `false` until parity |
| `EnableLinuxSupport` | Enable Linux-specific native service implementations | `false` during alpha |
| `EnableMacSupport` | Enable macOS-specific native service implementations | `false` during alpha |
| `EnableMobileSupport` | Enable Android head | `false` until beta |
| `EnableCrossPlatformAudio` | Use new `IAudioPlayer` abstraction | `false` on Windows until validated |
| `EnableAvaloniaWebView` | Use Avalonia WebView instead of WebView2 | `false` on Windows until validated |
| `EnableLockdownOnNonWindows` | Allow lockdown mode on Linux/macOS (degraded) | `false` |
| `EnableMobileOverlays` | Allow overlay effects on mobile | `false` |

Rollout plan:
1. **Internal alpha:** WPF app with `CCP.Core` + `UseAvaloniaUI=false`.
2. **Avalonia alpha:** `UseAvaloniaUI=true` for testers on Windows.
3. **Desktop beta:** Avalonia on Windows/Linux/macOS.
4. **Mobile beta:** reduced feature set on Android.
5. **General availability:** deprecate WPF head.

---

## 17. Accessibility & Localization

### 17.1 Accessibility

Current WPF app has limited automation properties and custom chrome that confuses screen readers.

Target:
- Set `AutomationProperties.Name` and `AutomationProperties.HelpText` on every interactive control.
- Ensure keyboard navigation (Tab order, access keys) for every view.
- Test with NVDA (Windows), Orca (Linux), and VoiceOver (macOS).
- Respect system high-contrast and reduce-motion settings.

### 17.2 Localization

Current: JSON language files + `LocExtension` markup extension.

Target:
- Keep JSON language files; move to `CCP.Core` as content.
- Rewrite `LocExtension` for Avalonia binding syntax.
- Add localization testing: ensure every new XAML string uses the markup extension.
- Consider ICU message formatting for pluralization and gender.

---

## 18. Code Signing & Distribution Checklist

| Platform | Artifact | Signing / Notarization |
|---|---|---|
| Windows | `win-x64` single-file EXE + MSI/INNO | Code signing certificate (EV recommended); sign installer and executable. |
| Linux | `linux-x64` self-contained folder or AppImage | GPG sign AppImage or package; no OS-level code signing. |
| macOS | `osx-x64` / `osx-arm64` app bundle + DMG | Apple Developer ID; notarize with `notarytool`; staple ticket. |
| Android | AAB/APK | Upload key + signing key; Google Play App Signing; Play Console. |

Additional distribution notes:
- Keep update channels separate per platform.
- Provide SHA256 checksums for all downloadable artifacts.
- Host native dependencies (Linux libvlc, macOS ARM64 libvlc) on a CDN or in release assets.

---

## 19. Mainline Sync & Dual-Maintenance

The WPF app on `main` is **kept runnable and keeps shipping features** during the migration. Because
`CCP.Core` holds *copies* of models and `CCP.Avalonia` holds *reimplementations* of services, every
merge from `main` into `feat/crossplatform` introduces drift that must be triaged by hand. This section
makes that process explicit — it is the practical answer to "where do the changes I just pulled go?".

### 19.1 Auto-synced vs. manual-sync map

| Surface | Sync behaviour |
|---|---|
| `Localization/Languages/*.json` | **Auto-synced.** `CCP.Core.csproj` links them (`<Content Include="..\Localization\Languages\*.json">`) and the desktop heads copy them. New loc keys from `main` appear with no porting. *Caveat:* a new Avalonia view must still reference the new key via `{loc:Str NewKey}`. |
| `Models/*.cs`, JSON DTOs | **Manual.** Copied into `CCP.Core/Models/`. Highest-frequency drift surface. |
| Portable service logic | **Manual.** Reimplemented under `CCP.Core/Services/*`. |
| WPF UI (`*.xaml(.cs)`, Chaos, AvatarTube, windows) | **Manual,** and only when that screen is already ported; otherwise it joins the Phase-4 parity backlog. |
| WPF-head-only infra (`installer.iss`, `build-installer.bat`, `App.xaml.cs` plumbing) | **No action** in the cross-platform tree. |

### 19.2 Per-merge triage workflow

1. `git diff --stat <prev-main>..<new-main> -- ConditioningControlPanel/` to list changed files.
2. Bucket each file using the map above (Model → Core; portable service → Core; UI → Avalonia-if-ported-else-backlog; infra/loc → no action).
3. Port, then `dotnet build CCP.Desktop.slnf -clp:ErrorsOnly` and run `CCP.Core.Tests`.
4. Record anything deferred in `docs/avalonia-migration-task-board.md` so it isn't lost.

### 19.3 Current sync backlog — `main` 6.1.7 update (2026-06-23)

`main` **6.1.7 is merged** (2026-06-23; branch caught up). The on-branch WPF code now needs porting to Core/Avalonia.
⚠️ The `Models/Quest.cs` / `Models/AppSettings.cs` deltas were **dropped** in the merge (modify/delete resolved
keep-deleted — confirmed: no patron pool / StoryMode / FIELD_PACE in Core), so **re-apply them to `CCP.Core/Models/`
by hand.** (The merge also left a stale `using ConditioningControlPanel.Localization` in Core `Quest.cs` → fixed to
`.Core.Localization`; Core + desktop build green.) Not yet in Core/Avalonia:

| Item | Landed in (WPF) | Port to | Notes |
|---|---|---|---|
| `Quest` model + pool refresh (20 free + 20 patron) | `Models/Quest.cs` (+114), `Services/Progression/QuestService.cs` (+88), `QuestDefinitionService.cs` | `CCP.Core/Models/Quest.cs` + Core quest services | Model delta applied by hand to Core (file deleted on our branch). |
| 20 quest art PNGs (bundled in-app, no CDN) | `Resources/quests/*.png` | `CCP.Avalonia` `AvaloniaResource`/Content | Wire into Quests tab/cards. |
| **Chaos "Down the Rabbit Hole" main menu** | `Chaos/ChaosHubWindow.xaml` (+321) + `.xaml.cs` (+1036) | existing `CCP.Avalonia/Chaos/ChaosHubWindow.*` | Biggest item: neon logo, How-to-Play tutorial overlay, menu soundtrack, pink fog, intro reveal, FX crossfade. |
| Chaos backdrop glint FX | `Chaos/ChaosBackdropService.cs` (+405), `Services/Chaos/ChaosArt.cs` (+38), `ChaosTuning.cs` (+14) | `CCP.Avalonia/Chaos` + Core | Authored glint FX on gameplay depth backdrops + main menu. |
| ~70 Chaos menu/backdrop assets + soundtrack | `assets/Chaos/backdrops/*`, `menu*.png`, `*_fx.json`, `sounds/chaos/menu_theme.mp3` | `CCP.Avalonia` `AvaloniaResource`/Content | Needed by the menu/backdrop port. |
| **Auth graceful browser-launch fallback** | new `Helpers/BrowserLauncher.cs` (+101); `Account/DiscordService` (+10), `PatreonService` (+9), `SubscribeStarService` (+9) | Core auth + `IBrowserHost`/system-open | **Ties to dead-auth gap A** — implement cross-platform (xdg-open/open on Linux/macOS). |
| Subliminal double-flash fixes | `Services/Subliminal/SubliminalService.cs` (+47), `Features/SubliminalFeatureControl.xaml.cs` (+16) | Avalonia subliminal service/control | Stop prev phrase flashing + drop stale timer tick. |
| Avatar focus-steal fix | `AvatarTube/AvatarTubeWindow.xaml` (+1) | `CCP.Avalonia/AvatarTube` | Companion window no longer steals focus / cancels typing. |
| `UpdateService` rework (+89) | `Services/Update/UpdateService.cs` | Core update logic | Reconcile against the already-ported Core `UpdateService`. |
| `AppSettings` +7 (Story re-lock / FIELD_PACE etc.) | `Models/AppSettings.cs` | `CCP.Core/Models/AppSettings.cs` | Apply by hand to Core. |
| Small deltas | `BubbleService` (FIELD_PACE knob), `BlinkTrainerService`, `KeywordTriggerService`, `Progression/AchievementService`, `MainWindow.UiUpdates`, `LabTabView.xaml` (+108) | matching Core/Avalonia | Verify each. |
| Localization (+2 keys ×9 langs) | `Localization/Languages/*.json` | — (auto-synced, §19.1) | No manual port; reference new keys in ported views. |

> **Also re-verify** anything these WPF fixes touched (subliminal flashing, avatar focus, bubble pace) against the
> new 6.1.7 behavior — existing matrix rows for those may now be wrong.

---

### 19.3-2 Current sync backlog — `main` merge `5ce70de6` (2026-07-03, WPF ~6.2.7)

`main` merged into `feat/crossplatform` (range `f9348d7e..5ce70de6`; 51 code files under the asset re-compression).
**Full prioritized backlog lives in `avalonia-migration-task-board.md` → "Sync-from-main: merge 5ce70de6"** (source of
truth; 4-agent parity analysis + first-hand P0 verification). Headlines:

- **Two P0s re-open closed WS0 lots** — lot 2: shared Core session ramp writes ramped pink/spiral opacity into
  auto-saving `settings.Current` (`SessionService.cs:400,408` / `IntensityRampService.cs:122,128`) → crash data-loss
  (WPF #471/#476 `SetSustainedOverlayOpacity` + `ReleaseOpacityRampHolds` direct-drive unported); lot 1:
  `ProfileSyncService` entirely absent → prestige/season-reset policy missing.
- **Ditzy Data PRO / Prestige** (skill-tree analytics): 5 Tier-6 nodes + `IsPermanent`/`PermanentIds`, schema-2
  `SeasonRecap`, monotonic `LifetimeSkillPointsSpent` — Core models auto-flow; the prestige service logic + 4 analytics
  panels + 3 charts are unported (lot 7).
- **#462 interaction-race cluster** (async trigger dispatch, ForceReset ordering, LockCard slot guard, session-summary
  defer, BubbleCount panic bail) — lots 2/3.
- **Integration lags**: #463 companion busy-retry, #465 linked-Patreon +14d + `HasPremiumAccess` gate audit, #455
  logout clears `AuthToken` (lot 8).
- **Animated `.webp`**: ~70% already covered by Avalonia `SkiaImageDecoder`/SKCodec — broaden extension gates only,
  **no `AnimatedWebp.cs` port, no new dep** (lot 3/6). `SubliminalSolidMode` #461 shared-host render path (lot 3).
- **Auto-flows (no action)**: Core-model deltas + the new loc keys (WPF-folder `Localization/Languages/*.json` is the
  built Content source; do NOT touch the dead `CCP.Core/Localization` copy).
- **WPF-only infra (no port action)**: `UiHangWatchdog`, `SplashScreen` STA overhaul, `App` single-instance/minidump,
  `UpdateService` version 6.2.7 — Windows-head seams; lot-11 note only.

> **Merge-trap check done:** this merge had no modify/delete conflicts on `CCP.Core/Models` (unlike the 6.1.7 sync);
> the 4 shared model files (SkillTree/SeasonRecap/AchievementProgress/AppSettings) merged cleanly and auto-flow.

---

### 19.3a Previously completed — 6.1.6 sync (commit `22caaab4`, 2026-06-21)

These `main` changes landed in the WPF head and have been ported (kept for record):

| Item | Landed in (WPF) | Port to | Notes |
|---|---|---|---|
| ✅ `AppSettings` new fields (+66 LOC) | `Models/AppSettings.cs` | `CCP.Core/Models/AppSettings.cs` | Done: ported Chaos Skia/SharedHost/AvatarOwnThread/MemTelemetry/PinOnTop flags to Core; namespace aligned. |
| ✅ `ChaosSkiaFxOverlay` (pop bursts, ripples, rim-shine, multiplier HUD) | `Chaos/ChaosSkiaFxOverlay.cs` (644 LOC) | ~~`CCP.Avalonia/Chaos/ChaosSkiaFxOverlay.cs`~~ DELETED 2026-07-05 | Done then SUPERSEDED: the Avalonia `ChaosSkiaFxOverlay` window was unwired and is now DELETED; its effects render via the compositor layers (`ChaosEStimArcLayer`, `ChaosCursorGlowLayer`, `ChaosFieldFxLayer`). |
| ✅ `ChaosBoonColors`, `ChaosBubbleHostOverlay`, `ChaosDvdHostOverlay` | `Chaos/*.cs` | `CCP.Avalonia/Chaos` | Done: ported host overlays (`ChaosBubbleHostOverlay`, `ChaosDvdHostOverlay`) and color map (`ChaosBoonColors`) to Avalonia. Wiring into a running Chaos engine is pending the engine port. |
| ✅ `ChaosCrashSentinel` | `Services/Chaos/ChaosCrashSentinel.cs` | Core service + Avalonia startup | Done: ported to `CCP.Core/Services/Chaos/ChaosCrashSentinel.cs` using `IAppEnvironment`/`IAppLogger`, registered in DI, `ConsumeAndReport` called at startup. `Mark`/`Clear` to be wired when the Chaos engine is ported. |
| ✅ `BubbleService` overhaul (popping minigame, +464 LOC) | `Services/BubbleService.cs` | `CCP.Core/Services/Chaos/BubbleEngine.cs` + `CCP.Avalonia/Services/AvaloniaBubbleService.cs` | Done: ambient bubbles, chaos effect bubbles (live, treats, darters, Chaperone, Bound, Echo, Tease, Brittle, Prism), chain reaction, field hazards (ripples/residue/Tail-Plug trails), shared-host renderer, and global low-level mouse hook ported. Stage 3 boon synergy (VibePop/E-Stim/Spanker) awaits a ported Avalonia ChaosModeService engine. |
| ✅ `UpdateService` rework (~120 LOC) | `Services/Update/UpdateService.cs` | Core update logic + `IUpdateInstaller` heads | Done: created `IUpdateService` + `UpdateService` in Core (GitHub check, skip-file logic, asset resolution), registered in Avalonia DI, wired `MainWindowViewModel` to auto/manual check and `UpdateNotificationDialog`, bumped all Avalonia project versions to 6.1.6. |
| ✅ `ModService` / `FlashService` / `GlobalMouseHook` deltas | `Services/*.cs` | matching Core/Avalonia services | Verified: `ModService.cs` unchanged; `FlashService.cs` moved to `Services/Flash/` and gained decode counters + `RaiseAllToFront()` (WPF-specific, no Avalonia action); `GlobalMouseHook.cs` moved to `Services/Input/` (Windows-only). |
| ✅ `Fredoka.ttf` | `Fonts/Fredoka.ttf` | `CCP.Avalonia` `AvaloniaResource` + `FontFamily` | Done: copied to `Assets/Fonts/`, registered `FredokaFont` resource in `App.axaml`, wired in `ChaosHudWindow.axaml` multiplier labels. |

### 19.4 Strategic fix — collapse the duplication (highest-leverage move)

**Status: completed 2026-06-22.** The WPF head (`ConditioningControlPanel.csproj`) now references `CCP.Core`
directly and the WPF `Models/` duplicate folder has been deleted. `CCP.Core/Models/` is the single source of
truth for all model/DTO types.

**What changed:**

1. ✅ Added `ProjectReference` from WPF to `CCP.Core/CCP.Core.csproj`.
2. ✅ Pinned `Microsoft.WindowsAppSDK` in WPF with `ExcludeAssets="all" PrivateAssets="all"` and added
   `NoWarn="NU1605"` to resolve the transitive downgrade warning introduced by referencing Core's `LibVLCSharp`
   dependency.
3. ✅ Deleted the WPF `Models/` folder (47 duplicate `.cs` files).
4. ✅ Removed duplicate type definitions that had been kept in WPF service files and redirected them to Core:
   - `ConditioningControlPanel.Services.CatalogueEntry` → `ConditioningControlPanel.Models.CatalogueEntry`
   - `ConditioningControlPanel.Services.Haptics.HapticProviderType` → `ConditioningControlPanel.Models.HapticProviderType`
   - `ConditioningControlPanel.Services.XPSource` → `ConditioningControlPanel.Models.XPSource`
   - `ConditioningControlPanel.Services.Content.PackFileEntry` → `ConditioningControlPanel.Models.PackFileEntry`
5. ✅ Made `AppSettings.MigrateFromContentModeToMod()` `public` (was `internal`) so WPF's `SettingsService` can call it from another assembly.
6. ✅ Qualified `LibVLCSharp.Shared.Core.Initialize(...)` in WPF video services so the unqualified `Core` name does not resolve to `ConditioningControlPanel.Core`.

**Validation:** `dotnet build ConditioningControlPanel.sln -clp:ErrorsOnly` → 0 errors, 0 warnings;
`dotnet test tests/CCP.Core.Tests/CCP.Core.Tests.csproj` → 95/95 passed; Avalonia Windows head `--smoke-test` →
44 tabs, 0 first-chance exceptions, 0 findings.

**Ongoing maintenance:** With WPF `Models/` gone, the §19.2 merge triage no longer needs to diff `Models/`. Any
future model changes should be made in `CCP.Core/Models/` and will be picked up by both WPF and Avalonia.

---

## 20. Multi-Agent Swarm Execution & Context Discipline

This migration is too large for one agent or one context window (133k LOC of UI, 156 Core files, 279
Avalonia files, several 4000-line source files). The remaining work — especially Phase 4 UI parity — is
**embarrassingly parallel**: most screens are self-contained (`XxxView.axaml` + `.axaml.cs` + `XxxViewModel`).
This section defines how to run a **swarm of agents in parallel** without them colliding, and how each agent
keeps its context small. Read this before fanning out work.

### 20.1 Parallelization model — lanes vs. chokepoints

The unit of parallel work is a **lane**: a directory subtree one agent owns end-to-end. Lanes have near-zero
cross-coupling, so many run at once. A small set of **shared/serial files** are touched by almost everything —
those are *chokepoints* and must be owned by a single integrator, never edited by porters directly.

**Parallel-safe lanes (assign one agent each; high fan-out):**

| Lane | Owned subtree (example) | Conflict risk |
|---|---|---|
| One tab (view + VM) | `CCP.Avalonia/Views/Tabs/XxxTabView.*` + `ViewModels/Tabs/XxxTabViewModel.cs` | Low |
| Dialog cluster | `CCP.Avalonia/Dialogs/Xxx*.axaml(.cs)` | Low |
| Feature control cluster | `CCP.Avalonia/Features/Xxx*` | Low |
| Chaos overlays | `CCP.Avalonia/Chaos/*` (incl. §19.3 `ChaosSkiaFx*`/host overlays) | Low–Med (shared Chaos host) |
| AvatarTube | `CCP.Avalonia/AvatarTube/*` | Low |
| Per-head platform seams | `CCP.Avalonia.Desktop.Windows/` · `.Linux/` · `.macOS/` (separate projects) | Near-zero |
| Core service port | one `CCP.Core/Services/<Area>/*` | Low |

**Serial chokepoints (integrator-only; do NOT edit from a porter lane):**

- `CCP.Avalonia/ServiceCollectionExtensions.cs` — every new VM/service registers here; highest contention.
- `CCP.Core/Models/AppSettings.cs` and other 4000-line single files — serialize edits.
- `App.axaml` / `App.axaml.cs` (shared styles/resources).
- Any `*.csproj` (asset `Include`s, package refs) and `*.slnx` / `*.slnf`.
- The `MainWindow` shell.
- `Localization/Languages/*.json` (JSON merges conflict badly — see §20.5).
- `main` → `feat/crossplatform` syncs (§19): one owner per merge.
- The tracker docs are append-mostly; the orchestrator reconciles them between waves.

### 20.2 Roles

- **Orchestrator (1):** partitions work into lanes, assigns them, owns every chokepoint file (DI, csproj,
  `App.axaml`, `MainWindow`, loc merge), runs the integration build/test between waves, and performs §19
  syncs. Holds no porting lane itself.
- **Porters (N, parallel):** each takes one lane, ports it end-to-end, and reports back: files changed, the
  **one-line DI registration** to add, new loc keys, and parity notes. Porters never touch chokepoints.
- **Verifier (1+):** runs the parity matrix and the §13 build/test matrix per platform; files defects as new
  tracker items rather than fixing in place.

### 20.3 Isolation — one worktree per agent

Give every porter its **own git worktree** (or branch) so working trees never collide; integration happens at
merge time, not edit time. Each worktree builds independently with `dotnet build CCP.Desktop.slnf -clp:ErrorsOnly`.
The orchestrator merges completed lanes in small batches and runs the integration build after each batch. Keep
**wave size bounded** to what the orchestrator can merge + build in one cycle (a good default is 3–6 porters per
wave), then integrate, then launch the next wave.

### 20.4 Coordination protocol (claim → work → integrate)

The tracker docs are the shared blackboard. Each item carries a status and an owner.

1. **Claim:** before starting, append a row to the **Active Claims Ledger** in
   `docs/avalonia-migration-task-board.md` (`🚧 wip @agentN`, your lane, your worktree/branch) and commit it
   *first* (a cheap "claim commit") so concurrent agents see it. Appending a row conflicts far less than editing
   scattered list items. Never start an item that already shows a `🚧`/`🔵` row.
2. **Work:** stay inside the owned subtree. If you discover you must change a chokepoint, **do not edit it** —
   record the needed change (e.g. the exact DI line) in your hand-off notes for the orchestrator.
3. **Integrate:** mark the item `✅`, list changed files + required DI line + new loc keys + parity notes, and
   hand the worktree/branch to the orchestrator. The orchestrator applies the DI line, merges, and builds.

### 20.5 Conflict-avoidance rules (concrete)

- **One file, one agent — always.** Two agents never edit the same file in the same wave.
- **DI registrations are requested, not applied.** Porters hand the orchestrator the line to add to
  `ServiceCollectionExtensions.cs`; only the orchestrator edits that file. (Batching also keeps the diff readable.)
- **Localization is merged, never hand-edited in parallel.** Each agent appends its new keys (nested by area)
  to `tools/new-localization-keys.json`, then the orchestrator runs `python tools/merge-localization-keys.py`
  once per wave to fold them into `Localization/Languages/*.json` conflict-free. (Loc JSON auto-syncs into Core
  per §19.1.)
- **csproj asset includes are batched.** Porters list assets they added (fonts, images); the orchestrator adds
  the `AvaloniaResource`/`Content` entries.
- **Respect the seam contract.** New behaviour goes behind an existing `CCP.Core/Platform` interface; if a new
  seam is genuinely needed, the orchestrator adds the interface + shared fallback first, then porters implement
  per head (§21 per-head DI pattern).

### 20.6 When to compact context (every agent, every role)

This migration will exhaust any single context if you let it. Compact aggressively at these boundaries:

- **After every claimed item.** Finish a lane → build green → update tracker → commit → **compact**, keeping
  only the trackers and the outcome. Do not carry item N's source into item N+1.
- **After each green build/test checkpoint.** Once `CCP.Desktop.slnf` + Core tests pass, the file contents that
  got you there are dead weight.
- **After any large one-shot dump.** A full read of a 4000-line file (`AppSettings`, `BubbleService`,
  `MainWindow`) should be distilled to the members you touched, then dropped.
- **At ~50–60% of the window,** or the moment you notice you're re-reading something — compact instead of
  pushing on.
- **Orchestrator-specific:** compact after each integration *wave*, preserving only tracker state, outstanding
  claims, and the last known-good build hash.

### 20.7 Durable working set to preserve across compaction

- This plan + `docs/avalonia-migration-task-board.md` + `docs/avalonia-ui-parity-matrix.md` — the persistent
  trackers. **Write progress into these, not the transcript**, so a fresh agent resumes from the docs alone.
- Your single active item, its owned subtree, and the last known-good build/test state.
- Decisions already made live in this plan (§19/§21) — keep a pointer, don't re-derive them.

### 20.8 Habits that keep context small

- Prefer `Grep`/`Glob` + targeted line-range reads over whole-file reads.
- Build with `-clp:ErrorsOnly` (or pipe through `grep -E 'error|warning'`) so MSBuild doesn't dump thousands of
  lines into context.
- Don't re-establish the project layout each session — it's in §1A and §3.1.
- Treat the tracker docs as external memory; update them at the end of every item.

### 20.9 The per-agent task loop

`read tracker → claim next lane (claim commit) → targeted reads of just that subtree → port → build
(ErrorsOnly) + Core tests → **visual-parity check vs the active theme's `img state/` reference + the WPF app's same tab (§13.5)**
→ update tracker (✅ + hand-off notes: files, DI line, loc keys) → commit/hand off → compact (keep only trackers
+ outcome) → repeat.`

Orchestrator loop: `assign wave → wait for hand-offs → apply DI lines + loc merge + csproj assets → merge batch
→ integration build/test → reconcile trackers → compact → next wave.`

### 20.10 Concrete lane map — derived from the original project

The remaining UI work decomposes **exactly along the original project's own structure**, so the orchestrator can
seed the ledger mechanically rather than inventing lanes. The legacy `MainWindow` is ~33k LOC split across ~38
**feature-named partial classes** (`MainWindow/MainWindow.<Feature>.cs`) — each is already a self-contained
feature boundary, which is the natural unit of one porter lane. Seed lanes with
`find MainWindow -name "MainWindow.*.cs"` and bucket them:

| Bucket | Original source | Target (Avalonia) | Parallel? | Owner |
|---|---|---|---|---|
| **Feature tabs** — Achievements, Animations, Assets, Autonomy, Awareness, BlinkTrainer, CatalogueSubmissions, CloudBackup, Companion, DeeperHub, DeeperSubmissions, DeeperTab, Enhancements, Haptics, KeywordTriggers, Lab, Leaderboard, LevelFeatures, Marquee, Patreon, Presets, Quests, RemoteControl, Roadmap, SessionIO, Settings, SubscribeStar | `MainWindow.<Feature>.cs` (+ matching `Services/<Area>` for logic) | one `Views/Tabs/<Feature>TabView.*` + `ViewModels/Tabs/<Feature>TabViewModel.cs` | ✅ one lane each (high fan-out) | Porter |
| **Shell / infra backbone** — the `MainWindow.xaml` shell, `UiUpdates`, `TabNavigation`, `WindowChrome`, `Browser`, `StartStop`, `AccountShell`, `Login` | `MainWindow.<Infra>.cs` | `MainWindow.axaml(.cs)` + shell services | ⛔ serial — these are the spine every tab hangs off | **Orchestrator only** |
| **Portable engine** — `Services/AIService`, `Commands`, `Moderation`, `Progression`, `Session`, `Content`, `Bark`, `Deeper` (logic), `Quiz`, `Account`, `Auth`, `Settings` | `Services/<Area>/*` (207 files total) | `CCP.Core/Services/<Area>/*` | ✅ one lane per area | Porter (Core) |
| **Platform/UI services** — `Chaos` (26 files), `Video`, `Audio`, `Haptics`, `Webcam`, `Tracking`, `Input`, `Notifications`, `Update`, `Flash`, `Subliminal`, `UI`, `LockCard` | `Services/<Area>/*` | `CCP.Avalonia` + a `CCP.Core/Platform` seam | ✅ one lane per area (Chaos is big — sub-split) | Porter |

Rules that follow from this map:

- **Do the shell backbone first / keep it single-owner.** Tabs can't be smoke-tested until `MainWindow.axaml` +
  `TabNavigation` + `WindowChrome` exist; and these are the files most likely to collide, so the orchestrator owns
  them. Everything else fans out behind them.
- **A tab lane owns both ends:** the WPF source is `MainWindow.<Feature>.cs` *and* the `Services/<Area>` it drives;
  port the portable logic into Core and the view into Avalonia in the same lane to keep the seam contract honest.
- **`Chaos` is the one oversized lane** (26 service files + the §19.3 Skia overlays) — sub-split it (overlays vs.
  economy/mode service vs. host pooling) rather than handing it to one agent.
- **Cross-check against the parity matrix.** `docs/avalonia-ui-parity-matrix.md` already tracks per-screen parity;
  reconcile the lane map against it so a "done" tab isn't re-claimed.
- **Eyeball every tab before ✅ (§13.5).** A clean build is *not* visual parity — earlier ports leaked raw
  `{loc:Str}` keys and dropped whole card grids. Screenshot the ported tab and compare it to `img state/good
  view.png` and the same tab in the running WPF app; fix any mismatch before closing the lane.

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

## 22. Conclusion

Avalonia UI v12 + LibVLCSharp is a capable cross-platform target for Conditioning Control Panel. The dominant cost is the WPF UI rewrite, not the engine. The safest path is:

1. **Engine-first extraction** into `CCP.Core` behind clean platform seams.
2. **Avalonia desktop parity** on Windows first, then Linux/macOS.
3. **Mobile heads** with a reduced, feature-gated companion experience.

This plan intentionally gates or redesigns Windows-only features (global hooks, system-key suppression, desktop wallpaper, WebView2 on non-Windows, NAudio/WASAPI ducking, GDI capture) rather than pretending they can be mechanically ported. With the added build/test checkpoints, quality goals, and architectural concerns, the rebuild should produce a codebase that is not only cross-platform but also faster, more stable, more testable, and more maintainable than the original WPF application.

---

## 23. References — Official Avalonia Docs (v12)

The plan's technical claims are validated against the **Avalonia v12** documentation. Use these as the canonical
source while porting; if this plan and the docs ever disagree, the docs win — fix the plan.

> **Version note:** `docs.avaloniaui.net` documents **Avalonia 12** (what this project uses). The previous line is
> archived at `v11.docs.avaloniaui.net` — don't follow v11 links for v12-specific API (e.g. `WindowDecorations`).

| Topic | Backs plan § | URL |
|---|---|---|
| Docs home / platform support (this project targets Win, Linux X11+Wayland, macOS, Android) | §1, §3 | https://docs.avaloniaui.net/docs/welcome |
| **WPF → Avalonia migration guide** (hub) | §4 | https://docs.avaloniaui.net/docs/migration/wpf |
| **WPF → Avalonia cheat sheet** (XAML, bindings, styles, controls, events, properties, threading) | §4.3–§4.8 | https://docs.avaloniaui.net/docs/migration/wpf/cheat-sheet |

The migration guide is a hub with six topic sub-pages — go to the one that matches the file you're porting:

| Sub-page | Backs plan § | URL |
|---|---|---|
| Styling (selectors/pseudo-classes) | §4.4 | https://docs.avaloniaui.net/docs/migration/wpf/styling |
| Controls (renames, packages) | §4.4 | https://docs.avaloniaui.net/docs/migration/wpf/controls |
| Data templates (DataType matching) | §4.4, §14.3 | https://docs.avaloniaui.net/docs/migration/wpf/data-templates |
| Properties (`StyledProperty`/`DirectProperty`) | §4.7 | https://docs.avaloniaui.net/docs/migration/wpf/properties |
| Events (pointer/tunnel/routed) | §4.8 | https://docs.avaloniaui.net/docs/migration/wpf/events |
| Layout (Spacing, Grid shorthand, Panel) | §4.4 | https://docs.avaloniaui.net/docs/migration/wpf/layout |
| Data binding & compiled bindings (`x:DataType`, `x:CompileBindings`) | §4.2, §4.7 | https://docs.avaloniaui.net/docs/basics/data/data-binding |
| MVVM pattern (Views/ViewModels, DataTemplates) | §14.3, §20.10 | https://docs.avaloniaui.net/docs/concepts/the-mvvm-pattern |
| Styling — selectors, classes, pseudo-classes (replaces triggers) | §4.4 | https://docs.avaloniaui.net/docs/styling/styles |
| Deployment — macOS bundle/notarize (other platforms in the same section's nav) | §7, §18 | https://docs.avaloniaui.net/docs/deployment/macos |

> **Scope note:** Avalonia ships more targets than this project pursues (e.g. Browser/WebAssembly, embedded
> Linux). This plan targets **Windows, Linux, macOS, and Android** only — see §1/§3.
