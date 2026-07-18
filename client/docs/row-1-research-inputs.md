# Row-1 research inputs (executor-agnostic)

**Date:** 2026-07-18

> **Research inputs only. No architecture decisions, no package selections, no scaffolding. Row 1 (Bootstrap discovery and architecture proposal) consumes these; the proposal itself is row 1's deliverable behind the owner checkpoint.**

---

## 1. Current Avalonia v12 facts

Access date for all sources: **2026-07-18**. Official sources only.

### Current stable version
- **Avalonia 12 is released and stable.** 12.0 announced April 7, 2026 ("Today, we're pleased to share that we've released Avalonia 12"). — https://avaloniaui.net/blog/avalonia-12/ (2026-07-18)
- **Latest = 12.1.0** (GitHub `[Latest]` tag); 12.1 blog post published **July 8, 2026** (native Wayland backend, rendering perf, new control). — https://github.com/AvaloniaUI/Avalonia/releases · https://avaloniaui.net/blog/release-12-1 (2026-07-18)
- Servicing train: 12.0.1–12.0.5 (12.0.5 on Jun 23, 2026); latest 11.x is **11.3.18**. — https://github.com/AvaloniaUI/Avalonia/releases (2026-07-18)

### v12 breaking changes vs 11 (greenfield-relevant)
- **.NET support:** .NET Framework/.NET Standard dropped; **.NET 8+ only**, recommended .NET 10; Android/iOS require .NET 10. — https://docs.avaloniaui.net/docs/avalonia12-breaking-changes (2026-07-18)
- **Compiled bindings on by default:** `<AvaloniaUseCompiledBindingsByDefault>` now `true`; XAML `{Binding}` maps to `CompiledBinding`. — same URL
- **Startup:** text shaper is decoupled from renderer — if you call `UseSkia()` explicitly you must add package `Avalonia.HarfBuzz` + `.UseHarfBuzz()` or startup throws "No text shaping system configured" (transparent under `UsePlatformDetect()`). — same URL
- **Windowing:** `TopLevel` (incl. `Window`) is no longer necessarily visual-tree root — use `TopLevel.GetTopLevel(Visual)`; `IInputRoot`/`IRenderRoot`/`ILayoutRoot`/etc. removed, new `IPresentationSource`. Decorations overhauled: new `WindowDrawnDecorations`; `TitleBar`, `CaptionButtons`, `ChromeOverlayLayer`, `ExtendClientAreaChromeHints` removed; use `WindowDecorations` + `ExtendClientAreaToDecorationsHint`. `Window.WindowState` is now a **direct property** (cannot be set from a style). `Screen` is abstract — obtain via `Screens.All`/`Primary`/`ScreenFromWindow`; `Screen.PixelDensity`→`Scaling`. — same URL
- **Input:** `Gestures.*` attached events moved to `InputElement` (`Gestures` class no longer public); `GotFocus`/`LostFocus` use `FocusChangedEventArgs`; `KeyboardNavigationHandler.GetNext`→`FocusManager.GetNextElement`; access keys are string-based symbol triggers; touch/pen selection now fires on pointer **release**, handled at container level. — same URL
- **Rendering:** **Direct2D1 backend removed** — Skia is the only/recommended backend (`Avalonia.Skia`, `UseSkia()`); render-target/platform-surface interfaces reworked (custom backends only). Animations stop on invisible controls by default (opt out: `Animation.PlaybackBehavior=Always`). No Vello renderer shipped in 12.0/12.1 — Skia remains the backend. — same URL · https://avaloniaui.net/blog/release-12-1 (2026-07-18)
- **Theming-adjacent:** no FluentTheme/ThemeVariant breaking-change headings exist; relevant items are the WindowState-direct-property change, `ResourcesChangedEventArgs`→struct, `IStyleable`→`StyledElement`. Data validation now on by default for custom controls. — https://docs.avaloniaui.net/docs/avalonia12-breaking-changes (2026-07-18)
- **Other:** `IDataObject`/`DataObject` removed → `IAsyncDataTransfer`/`TryGetTextAsync`; `Avalonia.Diagnostics` package removed → `AvaloniaUI.DiagnosticsSupport`, `AttachDevTools()`→`AttachDeveloperTools()`; `IBinding`/`InstancedBinding` removed (XAML `{Binding}` unaffected); file dialogs → `IStorageProvider` pickers; `TextBox.Watermark`→`PlaceholderText`; `Window.SystemDecorations`→`WindowDecorations`; legacy Type 1 fonts unsupported. — same URL

### Lifetime / startup / shutdown
- Entry point: `AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args)` from `Main`; main window assigned in `Application.OnFrameworkInitializationCompleted()` via `IClassicDesktopStyleApplicationLifetime.MainWindow`. `ApplicationLifetime` is **null in design mode** (previewer). — https://docs.avaloniaui.net/docs/fundamentals/application-lifetimes/ (2026-07-18)
- `IClassicDesktopStyleApplicationLifetime` (inherits `IControlledApplicationLifetime`): `ShutdownMode` = `OnLastWindowClose` | `OnMainWindowClose` | `OnExplicitShutdown` (latter requires explicit `Shutdown()` call). — same URL
- `IControlledApplicationLifetime`: `Startup`/`Exit` events + `Shutdown()` method. — same URL
- **Manual teardown/cancellation:** pass a delegate to `BuildAvaloniaApp().Start(AppMain, args)`, then run the main loop yourself with `app.Run(cts.Token)` — a `CancellationTokenSource` stops the loop. — same URL
- v12 change: `IApplicationPlatformEvents` removed → `Application.Current.TryGetFeature<IActivatableLifetime>()`. — https://docs.avaloniaui.net/docs/avalonia12-breaking-changes (2026-07-18)

### UI thread / dispatcher
- Single-threaded UI model; all UI access on the UI thread. `Dispatcher.UIThread` is the global accessor. — https://docs.avaloniaui.net/docs/app-development/threading (2026-07-18)
- APIs: `Post()` (fire-and-forget), `InvokeAsync()` (awaitable `Task`; **v12: captures caller `ExecutionContext`**), `CheckAccess()`, `VerifyAccess()` (throws off-thread); also `AvaloniaObject.Dispatcher`, `Dispatcher.CurrentDispatcher`, `Dispatcher.FromThread`. Priorities incl. `Send`, `Normal`, `Render`, `Loaded`, `Input`, `Background`, `SystemIdle`. — same URL
- v12: **multiple dispatchers supported** — `DispatcherTimer`/`SynchronizationContext` bind to the *current* dispatcher. — https://docs.avaloniaui.net/docs/avalonia12-breaking-changes (2026-07-18)
- `DispatcherTimer` ticks on the UI thread (recommended for periodic UI updates). `await` resumes on UI thread via Avalonia's `SynchronizationContext`. — https://docs.avaloniaui.net/docs/app-development/threading (2026-07-18)

### Compiled bindings
- **Default-on in v12** (`AvaloniaUseCompiledBindingsByDefault=true`); `x:CompileBindings="[True|False]"` toggles per subtree; `x:DataType` set on root (`Window`/`UserControl`) or per-binding (`{Binding X, DataType={x:Type vm:T}}`); `DataTemplate.DataType` for templates. — https://docs.avaloniaui.net/docs/data-binding/compiled-bindings · https://docs.avaloniaui.net/docs/avalonia12-breaking-changes (2026-07-18)
- Named-element and ancestor syntax (with compiler type inference since 11.3): `{Binding #MyWindow.DataContext.Prop}` and `{Binding $parent[Window].DataContext.Prop}`; explicit cast fallback `{Binding #W.((vm:T)DataContext).Prop}`. — https://docs.avaloniaui.net/docs/data-binding/compiled-bindings (2026-07-18)
- Opt-out per binding: `{ReflectionBinding ...}`; from C#: `CompiledBinding.Create(...)`. Binding plugins no longer configurable; data-annotations validation plugin disabled by default. — same URL · breaking-changes URL

### Selectors / pseudo-classes (vs WPF triggers)
- No WPF-style `Trigger`s: Avalonia styles use **CSS-like selector strings** in XAML matching on type, `StyleClass`es, `:pseudoclasses`, name, ancestry, template parts. — https://docs.avaloniaui.net/docs/styling/selectors (2026-07-18)
- Pseudoclasses = control-state keywords (CSS-like). Built-in on every `Control` (from `InputElement`): `:disabled`, `:pointerover`, `:focus`, `:focus-within`, `:focus-visible`; per-control e.g. `:checked` (CheckBox), `:pressed` (Button). Custom controls can declare custom pseudoclasses, styled via nested selectors; inherited from base classes. — https://docs.avaloniaui.net/docs/styling/pseudoclasses (2026-07-18)

### Assets
- Include via MSBuild item `<AvaloniaResource Include="Assets\**"/>` (stored internally as .NET resources; Avalonia calls them "Assets"). — https://docs.avaloniaui.net/docs/fundamentals/including-assets (2026-07-18)
- Reference in XAML by relative path (`icon.png`, `images/icon.png`) or rooted path (`/Assets/icon.png`); cross-assembly via **`avares://` URI**: `avares://MyAssembly/Assets/icon.png`. Code loading: `AssetLoader.Open(new Uri("avares://..."))`. — same URL
- No `file://`/`http(s)://` support in the asset system (community `AsyncImageLoader.Avalonia` for that). — same URL

### Windowing: multi-monitor, scaling, Linux
- Multi-monitor: `Window.Screens` → `Screens.All` (`IReadOnlyList<Screen>`), `Screens.Primary`, `ScreenCount`, `Changed` event; lookup via `ScreenFromWindow/Bounds/Point/TopLevel/Visual`. `Screen` (now abstract in v12) exposes `Bounds`, `WorkingArea`, `Scaling` (OS scale factor), `IsPrimary`, `DisplayName`, `CurrentOrientation`. — https://docs.avaloniaui.net/api/avalonia/controls/screens · https://docs.avaloniaui.net/api/avalonia/platform/screen · breaking-changes URL (2026-07-18)
- **Linux:** X11 is the default backend (Wayland desktops run via XWayland by default). **Native Wayland backend shipped in 12.1.0, experimental, opt-in only** — not picked up by `UsePlatformDetect()`; requires `Avalonia.Wayland` package + `.UseWayland()`, with **no automatic fallback** if no compositor present (select conditionally, e.g. via `WAYLAND_DISPLAY`). Wayland renders through EGL (optional dmabuf); mouse/touch/keyboard/clipboard/DnD work; some KDE features pending. — https://docs.avaloniaui.net/docs/platform-specific-guides/linux · https://avaloniaui.net/blog/release-12-1 (2026-07-18)
- 12.1 rendering notes: X11 now matches monitor max refresh rate (no 60 FPS cap); same for non-`WinUIComposition` modes on Windows (`WinUIComposition` stays default); stencil buffers on by default. — https://avaloniaui.net/blog/release-12-1 (2026-07-18)

### Persistence conventions
- **No built-in settings framework.** Official how-to recommends rolling your own: JSON file (`System.Text.Json`) under `Environment.SpecialFolder.ApplicationData`, wrapped in a `SettingsService`; try/catch on load (corrupt/schema drift → defaults); debounce high-frequency saves. — https://docs.avaloniaui.net/docs/how-to/data-persistence-how-to (2026-07-18)

### Packaging / publish
- Standard `dotnet publish -r <rid> -c Release` per OS. macOS documented with single-file self-contained: `dotnet publish … -f net10.0 -r osx-x64 --self-contained true -p:PublishSingleFile=true`; requires `UseAppHost=true`; `.app` bundle layout rules (Mach-O only in `/MacOS`, dylibs in `/Frameworks`, dlls in `/Resources`, symlinks). — https://docs.avaloniaui.net/docs/deployment/native-aot/ · https://docs.avaloniaui.net/docs/deployment/macos (2026-07-18)
- **Native AOT:** officially documented, produces self-contained native executables; reflection/trimming issues resolved via `TrimmerRootAssembly` entries; macOS universal binaries via `lipo`. Official repo hosts an "Avalonia-AOT-SingleFile-Template" discussion covering Linux x64 single-file self-contained NativeAOT publish. — https://docs.avaloniaui.net/docs/deployment/native-aot/ · https://github.com/AvaloniaUI/Avalonia/discussions/21660 (2026-07-18)
- 12.1.0 ships an SBOM (EU CRA prep) — packaging metadata consideration. — https://avaloniaui.net/blog/release-12-1 (2026-07-18)

### .NET version requirements for v12
- **Minimum .NET 8; recommended .NET 10.** Android/iOS targets: **.NET 10 only.** Headless testing: xUnit.net **v3**, NUnit **v4**. — https://docs.avaloniaui.net/docs/avalonia12-breaking-changes (2026-07-18)

---

## 2. capability-inventory.md staleness report

**Source doc:** `client/docs/capability-inventory.md` · **Reference tree:** `ConditioningControlPanel/` at HEAD · **Method:** existence check of every file/dir path cited in the doc's evidence sections.

| Metric | Count |
|---|---|
| Internal file/dir citations checked | **42** |
| Still valid (exist at HEAD) | **42** |
| STALE / unverifiable (internal) | **0** |

**No stale citations.** Every WPF evidence path, every first-attempt Avalonia/Core path, and every directory citation resolves; `AI_AUDIT.md` (repo root) is present. External web references (2 Ollama doc URLs; ONNX Runtime / execution-providers / OpenCvSharp nuget / XDG Camera portal URLs) were not verified — live-doc links, out of codebase scope. The doc cites **paths only** — no per-symbol signature claims; inline property names (`Extent`/`Viewport`/`Offset`, `Visuals`, `System`, protocol "version 1") are behavior contracts, not cited symbols, so no symbol-level drift is detectable from the doc text.

### Coverage gap: major WPF features ABSENT from the inventory
The inventory currently covers only sound/companion, AI, webcam, dashboard popups, window behavior, AvatarTube, video, DTRH. Missing:

**Core conditioning/automation:** `Services/Flash/` (flash-image conditioning); `Services/Session/` (session engine); `Services/Subliminal/`; `Services/Deeper/` (`.ccpenh.json`, `EnhancementValidator`); `Services/MantraService.cs` / `MantraVoiceService.cs`; `Services/AutonomyService.cs` (+ `.Voice`, `.VoiceCommands`); `Services/Compositor/` (WPF compositor not cited as evidence — only first-attempt Avalonia `Compositor/` is; top-level `Spirals/` and `Overlays/` also unmentioned); `Services/KeywordTriggerService.cs` / `KeywordHighlightService.cs` / `KeywordTriggerPresetService.cs`.

**Gamification / mini-games:** `Services/Progression/` + `GamificationBridge.cs`; `Services/BubbleService.cs` / `BubbleCountService.cs`; `Services/BlinkTrainerService.cs` (+ asset pool); `Services/FocusGameService.cs`; `Services/Quiz/`.

**Platform / integration / economy:** `Services/Auth/`, `Services/Account/`, `SubscribeStarService.cs` (premium gating); `Services/CatalogueService.cs` / `CatalogueLookupService.cs`; `Services/ModService.cs` / `ModResourceResolver.cs`; `Services/RemoteControlService.cs`; `Services/Haptics/` (Buttplug); `Services/Media/`, `Services/Content/`; `Services/Update/`; `Services/Settings/`.

**Misc product surfaces:** `Services/TutorialService.cs` (+ `TutorialEventBus.cs`); `Services/Notifications/`; `Services/Input/`, `Services/Commands/`; `Services/ScreenOcrService.cs`, `Services/BugReportService.cs`, `Services/LogScrubber.cs`; top-level `Dialogs/`, `Windows/`, `Views/`, `Overlays/`, `Lab/`.

**Bottom line:** existing evidence citations are 100% intact at HEAD; the gap is *coverage*, not staleness.

---

## 3. WPF archaeology for rows 2–4 (startup/shutdown, persistence, threading)

All paths relative to `ConditioningControlPanel/`.

### 3.1 App.xaml.cs — startup, teardown, single-instance, crash handlers (3,352 lines)

**OnStartup order of operations** (`OnStartup` begins line 898):
1. **Dump-writer early exit** — `--write-hang-dump <pid> <path>` writes a minidump of a wedged sibling and `Environment.Exit`s before touching splash/mutex/services — `App.xaml.cs:900-909`
2. **ComboBox de-layer class handler** — render-thread deadlock guard (Application Hang 1002); sets `Popup.AllowsTransparency=false` on every ComboBox — `App.xaml.cs:916-930`
3. **Splash on its own STA thread** — `SplashScreen.ShowOnOwnThread()`; same-thread splash "froze mid-bar" because everything below runs synchronously on the UI thread — `App.xaml.cs:938-940`
4. **Parse `--play`/`--edit` file-open args** — before single-instance check so a second instance can write its handoff file — `App.xaml.cs:945-947`
5. **Single-instance mutex + ack handshake** — `new Mutex(true, MutexName, out createdNew)` at `App.xaml.cs:950`; full second-instance protocol at 951-1045
6. **Show-signal wait handles + listener thread** — `_showSignal`/`_showAckSignal` EventWaitHandles at `App.xaml.cs:1050-1053`; background thread `ShowWindowSignalListener` at 1054-1105; acks from listener thread during `_startupPhase`, from dispatcher callback afterwards (1061-1066, 1093-1098)
7. `base.OnStartup(e)` — `App.xaml.cs:1109`
8. **30 FPS animation cap** — `Timeline.DesiredFrameRateProperty.OverrideMetadata(...30)` — `App.xaml.cs:1111-1115`
9. **Serilog init** — rolling daily file `app-.log`, 7 retained, `flushToDiskInterval: 1s` ("so the LAST lines survive a hard process death") — `App.xaml.cs:1123-1142`; fallback to temp dir if UserDataPath fails (1125-1131)
10. **Runtime version + working-set baseline log** — `App.xaml.cs:1147-1149`
11. **crash.log rotation per version** — `RotateCrashLogForVersion(logPath)` at `App.xaml.cs:1156` (impl 2859-2900: rotates to `crash.log.prev` when version marker differs)
12. **Stale-instance takeover log** — `App.xaml.cs:1160-1161`
13. **Crash sentinels consumed** — `ChaosCrashSentinel.ConsumeAndReport`, `EngineCrashSentinel.ConsumeAndReport` — `App.xaml.cs:1166-1167`
14. **DisplaySettingsChanged hook** — drops screen cache + pauses layered spawns — `App.xaml.cs:1172`
15. **UiHangWatchdog.Start(Dispatcher)** — one minidump per session after 10s dispatcher unresponsiveness — `App.xaml.cs:1177`
16. **Three global crash handlers** — `App.xaml.cs:1182-1254` (see below)
17. Background `UpdateService.CleanupOldPackages()` — `App.xaml.cs:1257-1266`
18. Directory creation (assets + Resources) — `App.xaml.cs:1270-1279`
19. **DPAPI secure-store seams wired BEFORE settings load** — `SecureAuthTokenStore.Wire`/`SecureApiKeyStore.Wire`; comment: no-op stubs "silently broke token/API-key persistence" — `App.xaml.cs:1284-1291`
20. **`Settings = new SettingsService()`** — `App.xaml.cs:1294`
21. **One-shot migration + immediate save** — `RunFlashClickableDecouplingMigration(); Settings.Save();` — `App.xaml.cs:1296-1307`
22. Background asset migration (must be after Settings — guard flag persistence bug) — `App.xaml.cs:1309-1314`
23. UnifiedUserId restore — `App.xaml.cs:1316-1325`; installer assets path 1327; custom-assets dirs 1333; temp cleanup 1336
24. **Localization** — `App.xaml.cs:1339`; **ModService** — 1342-1343; mod title-bar class handler + `ModChanged` recolor — 1345-1360
25. **Service wiring (~0.3→0.95 splash progress)** — Audio 1363-1364, Flash 1367, Video + `PreloadLibVLC()` 1370-1371, SessionLog 1374, Progression/ActivityTracker 1377-1378, Companion (+legacy migration) 1381-1382, CommunityPrompts 1383, Personality 1386-1387, Subliminal 1389, **CompositorEngine** 1393, **OverlayService** 1394 (continues past 1405; SubscribeStar 1480, async init 1628)
26. **MainWindow created + shown at splash 0.95** — `App.xaml.cs:1676-1678`; stable `MainWindowRef` static (Current.MainWindow is null when tray-hidden) — 1689-1692
27. Dev flags: `--stress` 1700, `--overlay-host` 1705, `--overlay-ulw` 1713, `--dtrh*` 1734-1741
28. **Compositor prewarm** at Background priority — `App.xaml.cs:1722-1725`
29. **Deferred voice-model warm** — Vosk/KWS warmed off-UI thread, `RefreshVoiceInputModes` back on UI thread at ApplicationIdle (models loaded on UI thread blocked startup) — `App.xaml.cs:1753-1766`
30. First-instance file-open replay after MainWindow loaded — `App.xaml.cs:1771-1785`
31. Splash fade + `ForceWindowToFront` (ForegroundLockTimeout workaround) — `App.xaml.cs:1802-1809`
32. **`_startupPhase = false` via `Dispatcher.BeginInvoke`** — "First dispatcher pump = startup is over" — `App.xaml.cs:1812`
33. Age-verification gate, deferred — `App.xaml.cs:1814+`

**OnExit / teardown** (`OnExit` at line 3177): SystemEvents unhook (3180) → crash sentinels cleared "a clean shutdown is NOT a crash" (3184-3185) → DtrhHostService close (3188) → avatar own-thread dispatcher `InvokeShutdown` (3195-3203) → **`Settings?.SaveImmediate()` FIRST, before cloud sync** (3208) → cloud profile sync with 2s timeout (3211-3220) → trigger sources disposed first "so no new effects get queued" (3223-3226) → **~60 service disposes in reverse init order** (3228-3287; `VideoEnhanceBridge` before `Video` — dangling-subscription note 3233-3235; `Compositor` after effect services "so their layers deactivate first" 3241) → stop spawned `ollama serve` (3291-3292) → clear in-memory secrets (3295-3296) → `Log.CloseAndFlush()` (3299) → show-signal dispose unblocks listener thread (3302-3305) → ack-signal dispose (3308-3310) → **mutex released only if `_mutexOwned`** (3313-3322) → `base.OnExit` (3330) → **`TerminateProcess(GetCurrentProcess(), 0)`, deliberately NOT `Environment.Exit`** — DirectWriteForwarder CRT-teardown throws `DllNotFoundException` during `AppDomain.ProcessExit` on a half-shut-down runtime ("crash on close", WER dumps 5/28-6/28) — `App.xaml.cs:3332-3342`

**Single-instance mutex + second-instance handoff:**
- Kernel names: mutex `ConditioningControlPanel_SingleInstance_Mutex`, signals `..._ShowWindow_Signal`, `..._ShowAck_Signal` — `App.xaml.cs:43-54`
- **Ack handshake rationale** (comment 45-52): a wedged/headless primary keeps the mutex forever; old "signal then Shutdown()" made every relaunch a silent no-op. Second instance waits `ShowAckTimeoutMs = 10000` (line 61) for the primary's UI thread to ack; no ack → `_recoveredFromStaleInstance = true`, `KillStaleInstances()` (impl at 1846), claim mutex via `WaitOne` (`AbandonedMutexException` still means acquired) — `App.xaml.cs:1029-1044`
- **`_startupPhase` volatile flag** (63-65): during OnStartup the dispatcher isn't pumping (cold start 13s+), so the signal-listener thread acks directly; a dump-suspended process has that thread frozen too, so takeover still catches real zombies — `App.xaml.cs:1056-1066`
- **Legacy-primary path** (no ack handle = pre-handshake build mid-upgrade, #466): poke show-signal, wait up to 8s for mutex to free, take over; exit only if a live legacy primary keeps it — `App.xaml.cs:976-1002`
- **File-open handoff**: second instance writes `%LOCALAPPDATA%\ConditioningControlPanel\fileopen.pending` (action + path, two lines) BEFORE signaling — `App.xaml.cs:70-78, 118-123, 960-963`; primary's dispatcher callback consumes it and calls `mainWin.HandlePendingFileOpen` — `App.xaml.cs:1076-1088`; path validation rejects UNC/`\\?\` prefixes, requires rooted local existing file with whitelisted media extension — `App.xaml.cs:80-87, 98-115`

**Crash handlers:**
- `DispatcherUnhandledException` at `App.xaml.cs:1185-1243`: always `LogCrashDetails("DISPATCHER", ...)`; **swallows GDI/desktop-heap quota Win32Exceptions** (native 1816/1450, #394/#395) as recoverable dropped frames (1193-1202); **render-thread failure/OOM → immediate `Environment.Exit(1)`** with `Interlocked` guard — MessageBox would run a nested pump inside which the render thread keeps crashing (2026-05-25 crash storm: 10,251 cascading reports) (1205-1224); one-shot error dialog otherwise, `args.Handled = true` (1226-1242)
- `AppDomain.CurrentDomain.UnhandledException` → log only — `App.xaml.cs:1245-1248`
- `TaskScheduler.UnobservedTaskException` → log + `SetObserved()` — `App.xaml.cs:1250-1253`
- `LogCrashDetails(source, ex)` at `App.xaml.cs:2905-2943`: Serilog error + appends full report to `%LOCALAPPDATA%\ConditioningControlPanel\logs\crash.log`; rotation per build version at 2859-2900

### 3.2 Services/Settings/SettingsService.cs — persistence (648 lines)

- **Path**: `%LOCALAPPDATA%\ConditioningControlPanel\settings.json` (via `App.UserDataPath`) — `SettingsService.cs:59-60`; one-time migration from install dir at 64-87
- **Atomic write** — `SaveImmediate` at `SettingsService.cs:524-584`: serialize `Formatting.Indented` (551), then `File.WriteAllText(tempPath, json); File.Move(tempPath, _settingsPath, overwrite: true)` — **552-554** (comment 549-550: "so a crash mid-write can't corrupt the settings file (prevents save state reversion bug)")
- **Save triggers**: `Save(bool suppressCloudBackup = false)` is a **500ms debounce** via `System.Threading.Timer` coalescing rapid calls — `SettingsService.cs:498-518` (fields 17-19); `SaveImmediate` cancels pending debounce and flushes now (526-530). On-exit flush: `App.xaml.cs:3208`. Fire-and-forget cloud backup after each save, gated to ≥30s via `Interlocked.CompareExchange` (561-577)
- **Daily rolling backups before first write of the day** — `RotateDailyBackupBeforeWrite()` at `SettingsService.cs:603-635`: `bak-1..bak-3` rotation, mtime re-stamped so it doesn't re-rotate (623-625); rationale 537-542 (atomic write protects mid-write crash, not external destruction)
- **Load/corruption behavior** — `Load()` at `SettingsService.cs:96-225`:
  - **Interrupted-write recovery**: tmp exists + main missing → `File.Move(temp, main)`; tmp + main both exist → stale tmp deleted — 100-112
  - **Per-member error tolerance**: `JsonSerializerSettings.Error` handler collects bad member paths, marks `Handled = true`, keeps everything that parsed ("my lock card phrases / subliminals get wiped every time I update" fix); `ObjectCreationHandling.Replace` so lists replace, not merge — 126-140
  - Post-load migrations: auth token→DPAPI (154), keyword-trigger action synth (163), built-in awareness presets (166), ContentMode→mod (169), loudness threshold (172), unified-overlay-host re-enable (177)
  - Total parse failure → `PreserveCorruptSettingsFile()`: renames to `settings.corrupt-<timestamp>.json` (move, not copy) — 190-193, 271-288
  - Fallback chain: corrupt → `TryLoadFromDailyBackup()` (`bak-1..3` newest first, same migrations) 208-216, 230-263 → else factory defaults with `WasSettingsFileMissing`/`WasSettingsFileCorrupt` flags 218-224
- `RestoreFrom`/`Reset` fire `CurrentReplaced` before `SaveImmediate` so listeners re-bind before persist — `SettingsService.cs:641-657`

### 3.3 Threading / dispatcher

- **CLAUDE.md "Known scars"** — `CLAUDE.md:58-61`: "UI-thread work uses the Dispatcher; some timers must be `DispatcherTimer`", points to `CCP.Core/Services/Deeper/IActionDispatcher.cs` threading notes and `docs/crossplatform-rebuild-plan.md` §21 (v12 gotchas). Crash-logging scar at 63-64 names the three global handlers.
- **WPF `Services/Deeper/IActionDispatcher.cs:32-38`** — `IActionDispatcher.DispatchAsync` contract: engine-owned `CancellationToken` "fires when the engine that owns the dispatcher is stopped; used so long-running multi-step dispatches (haptic patterns, audio) abort instead of running on after the user pressed stop." No DispatcherTimer/Task.Delay usage in the WPF file itself.
- **Core counterpart** `CCP.Core/Services/Deeper/IActionDispatcher.cs:488-496` — bubble-burst stop uses an Avalonia `DispatcherTimer` (one-shot, self-unsubscribing handler), min 50ms interval.
- **`Services/Notifications/OverlayService.cs`** — `DispatcherTimer` fields `_updateTimer`/`_gifLoopTimer` at 49-50, `_gifFrameTimer` at 150; comment 144-146: "re-decoding the GIF on the UI thread froze everything for ~1s each time chaos re-showed the spiral. Frames are frozen → safe to reuse"; entry points marshal via `DispatcherHelper.RunOnUISync` (247).
- **`Windows/WebcamCalibrationWindow.xaml.cs`** — TCS with `TaskCreationOptions.RunContinuationsAsynchronously` at 74-82: without it "TrySetResult continues the awaiting state machine inline on the dispatcher thread … which can re-enter the multicast delegate while it's still being invoked"; extensive `Task.Delay` choreography (320, 326, 359, 826-1023, 1577, 1661, 1778) with a **post-delay dispatcher-shutdown guard** at 1023-1030 (`HasShutdownStarted` check, mirrors CLAUDE.md fire-and-forget pattern); `Dispatcher.InvokeAsync(..., DispatcherPriority.Loaded)` yield at 257; `_verifyCountdownTimer` DispatcherTimer at 746-760.
- **App-level threading facts**: splash runs on dedicated STA thread (`App.xaml.cs:932-940`); show-signal listener is a background `Thread` polling `WaitOne(1000)` (1054-1105); avatar may own a second UI thread whose dispatcher is shut down in OnExit (3195-3203); voice-model warm explicitly moved off UI thread after startup hitch (1753-1766).

### 3.4 Focused git history

`git log --oneline -15 -- ConditioningControlPanel/App.xaml.cs` (newest first): `0b0bfbc9` fix(compositor): #550 — stop the unified host lagging the UI thread · `431f4a19` perf(overlays): keep effect host windows up through an idle grace instead of hiding instantly · `d05d5ae4` fix(stability): bound native fan-out from flash decodes + chaos SFX, surface silent crashes · `e8f827b2` feat: Discord share prompt + **settings daily backups** + DtRH boot deadline · `d4873c93` fix: lockdown key hook install + bubble preset props + gaze re-enable migration · `83f18eb1` fix(compositor): pre-merge review batch — z-order, present gating, leaks, ghost click

Over SettingsService/IActionDispatcher/OverlayService/WebcamCalibrationWindow: `a192438a` fix(deeper): overlay bands no longer freeze, stomp, or nuke base overlays (#563) · `c1ca37e1` fix(overlay): pulse no longer strands pink/spiral/braindrain opaque during a ramp (#535) · `a32cec92` perf(compositor): decode spiral frames off the UI thread on the layer route · `c1a3c571` feat(compositor): unified overlay host default ON + settings toggle · `e1e6ef43` fix: review pass on the support-chat batch (8 confirmed findings)

**Signal:** recent churn clusters on (a) UI-thread/render-thread stalls from overlay + decode work, (b) settings durability (daily backups landed in `e8f827b2`), (c) single-instance/startup robustness (#466 legacy-primary path, zombie takeover — visible in current code comments).

---

## 4. Open questions this research surfaces for row 1 (list only, no answers)

1. Target .NET version for the greenfield client: .NET 8 (current repo baseline) vs .NET 10 (Avalonia 12 recommendation, required for future mobile heads)?
2. Which Avalonia package version to pin: 12.0.5 servicing train vs 12.1.0 latest — and what upgrade cadence?
3. Linux backend strategy: X11-only for now, or conditional opt-in to the experimental 12.1 native Wayland backend (`Avalonia.Wayland` + `WAYLAND_DISPLAY` detection, no automatic fallback)?
4. `UsePlatformDetect()` vs explicit `UseSkia()` + `Avalonia.HarfBuzz` — does any startup requirement force the explicit path?
5. Which WPF startup responsibilities survive into the greenfield bootstrap (splash, single-instance handshake, crash sentinels, hang watchdog, crash-log rotation) and which are Windows-era workarounds to drop or redesign?
6. Cross-platform single-instance mechanism: what replaces the Windows named mutex + EventWaitHandle ack handshake on Linux?
7. Shutdown model: `ShutdownMode` choice, and what replaces WPF's `TerminateProcess`-on-exit workaround — does Avalonia/.NET 8+ have an equivalent CRT-teardown hazard?
8. Manual main-loop (`app.Run(cts.Token)`) vs `StartWithClassicDesktopLifetime` — does the teardown ordering requirement (settings flush before service disposal, reverse-order disposal) need the manual path?
9. Settings persistence design: `System.Text.Json` (official how-to) vs Newtonsoft (WPF behavior parity — per-member error tolerance, `ObjectCreationHandling.Replace`) — which serializer can reproduce the partial-load contract?
10. Which of the WPF settings durability behaviors are contract for the new client: atomic temp+rename, 500ms debounce, daily `bak-1..3` rotation, corrupt-file preservation, interrupted-write recovery, backup fallback chain?
11. Secret storage seam: what replaces DPAPI (`SecureAuthTokenStore`/`SecureApiKeyStore`) on Linux, and must it be wired before settings load as in WPF?
12. Crash handling architecture: what are the Avalonia equivalents of the three global handlers, and does the "render-thread failure → immediate exit, no dialog" rule translate?
13. Dispatcher discipline: how do WPF scars (DispatcherTimer requirements, post-delay shutdown guards, `RunContinuationsAsynchronously` TCS) map onto Avalonia v12's dispatcher (multiple-dispatcher support, `InvokeAsync` ExecutionContext capture)?
14. Does the avatar's second-UI-thread pattern carry forward under v12 multiple-dispatcher support, or is it redesigned away?
15. File-open handoff (`--play`/`--edit` + `fileopen.pending`): is this contract in scope for the greenfield client, and what is the cross-platform IPC design space?
16. Capability inventory coverage: when/how do the missing feature families (flash, sessions, subliminal, deeper, progression, mods, auth/premium, haptics, overlays/spirals, autonomy, etc.) get inventoried — before or alongside row-1 architecture proposal?
17. Packaging path: per-OS `dotnet publish` self-contained vs Native AOT — do any known dependencies (LibVLC, OpenCV, ONNX) constrain the choice?
18. Animation policy: does the WPF 30 FPS cap have any analogue given v12's stop-animations-when-invisible default and 12.1's uncapped refresh rates?
