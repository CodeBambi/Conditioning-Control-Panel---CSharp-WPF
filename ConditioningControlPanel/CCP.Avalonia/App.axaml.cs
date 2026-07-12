using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Infrastructure;
using ConditioningControlPanel.Avalonia.Services.Content;
using ConditioningControlPanel.Avalonia.Services.Overlays;
using ConditioningControlPanel.Avalonia.ViewModels;
using ConditioningControlPanel.Avalonia.Views;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Roadmap;
using ConditioningControlPanel.Core.Services.Moderation;
using ConditioningControlPanel.Core.Services.Scheduler;
using ConditioningControlPanel.Avalonia.Chaos;
using ConditioningControlPanel.Avalonia.Services.Theme;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using CoreApp = ConditioningControlPanel.CoreApp;

namespace ConditioningControlPanel.Avalonia;

public partial class App : Application
{
    /// <summary>
    /// Global service provider for the Avalonia head. Populated during
    /// <see cref="OnFrameworkInitializationCompleted"/> before any window is created.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// Global tutorial service used by interactive Deeper editor walkthroughs.
    /// </summary>
    public static Avalonia.Services.Tutorial.AvaloniaTutorialService Tutorial { get; private set; } = null!;

    /// <summary>
    /// Optional override for the media assets path. Applied to
    /// <see cref="AppSettings.CustomAssetsPath"/> at startup so headless/benchmark
    /// runs can resolve media without opening the settings UI.
    /// </summary>
    public static string? OverrideAssetsPath { get; set; }

    /// <summary>
    /// Optional head-specific DI tweak. Set before starting the app.
    /// </summary>
    public static Action<IServiceCollection>? ConfigurePlatformServices { get; set; }

    /// <summary>
    /// Guards the global crash dialog so a repeating fault logs every occurrence
    /// but only ever shows one message box (WPF App.xaml.cs:1155 errorDialogShown).
    /// </summary>
    private bool _errorDialogShown;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ConfigureLogging();

        // Global exception handling before any window is created.
        Dispatcher.UIThread.UnhandledException += (s, e) =>
        {
            e.Handled = true;
            // LOG every occurrence, but only surface the dialog once: a repeating
            // exception would otherwise spawn a message box per fault (WPF parity,
            // App.xaml.cs:1155 errorDialogShown).
            Log.Logger?.Error(e.Exception, "Unhandled UI thread exception");
            if (_errorDialogShown) return;
            _errorDialogShown = true;
            try
            {
                var dialog = Services?.GetService<IDialogService>();
                _ = dialog?.ShowMessageAsync(Loc.Get("title_error"), string.Format(Loc.Get("msg_unexpected_error_fmt"), e.Exception?.Message));
            }
            catch { }
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Log.Logger?.Error(e.ExceptionObject as Exception, "Unhandled app domain exception");
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Log.Logger?.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        try
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.ConfigureCoreServices();
            serviceCollection.AddSingleton<IChaosEnvironment, ChaosEnvironment>();
            serviceCollection.AddSingleton<IChaosModeState, ChaosModeState>();
            serviceCollection.AddSingleton<IChaosMetaService, ChaosMetaService>();
            serviceCollection.AddSingleton<IRevealService, RevealServiceImpl>();
            ConfigurePlatformServices?.Invoke(serviceCollection);
            Services = serviceCollection.BuildServiceProvider();
            Tutorial = new Avalonia.Services.Tutorial.AvaloniaTutorialService();

            // Back the Core secure-store seams (AppSettings.AuthToken / OpenRouterApiKey)
            // with ISecretStore BEFORE anything resolves ISettingsService; unwired they
            // are no-op stubs and login/API-key values silently fail to persist.
            var secretStore = Services.GetRequiredService<ISecretStore>();
            ConditioningControlPanel.Core.Services.SecureAuthTokenStore.Wire(
                () => secretStore.Retrieve("auth_token") is { Length: > 0 } b ? System.Text.Encoding.UTF8.GetString(b) : null,
                v =>
                {
                    if (string.IsNullOrEmpty(v)) secretStore.Delete("auth_token");
                    else secretStore.Store("auth_token", System.Text.Encoding.UTF8.GetBytes(v));
                });
            ConditioningControlPanel.Core.Services.SecureApiKeyStore.Wire(
                () => secretStore.Retrieve("openrouter_api_key") is { Length: > 0 } b ? System.Text.Encoding.UTF8.GetString(b) : null,
                v =>
                {
                    if (string.IsNullOrEmpty(v)) secretStore.Delete("openrouter_api_key");
                    else secretStore.Store("openrouter_api_key", System.Text.Encoding.UTF8.GetBytes(v));
                });

            // Allow command-line/benchmark runs to point at the user's media folder
            // without opening the settings UI.
            if (!string.IsNullOrWhiteSpace(OverrideAssetsPath))
            {
                try
                {
                    var settingsService = Services.GetRequiredService<ISettingsService>();
                    settingsService.Current.CustomAssetsPath = OverrideAssetsPath;
                    settingsService.Save();
                    Log.Information("[BENCH] CustomAssetsPath overridden to {Path}", OverrideAssetsPath);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[BENCH] Failed to apply --assets-path override");
                }
            }

            // Fix P0 data-path split: migrate any Avalonia data written to Roaming
            // into the legacy Local folder before Core services read/write it.
            AvaloniaAppEnvironment.MigrateFromLegacyRoamingPath();

            // Privacy: crash-recovery sweep of decrypted pack media. Per-session cleanup
            // handles the happy path, but a crash leaves plaintext adult content in the
            // media temp dir; this removes it at startup (mirrors WPF App.CleanupStaleTempFiles).
            // Runs after the data-path migration so it targets the same UserDataPath/media_tmp
            // directory the decryptor writes to.
            try
            {
                var tempEnv = Services.GetRequiredService<IAppEnvironment>();
                AvaloniaContentPackService.CleanupStaleTempFiles(tempEnv.UserDataPath);
            }
            catch (Exception ex)
            {
                Log.Logger?.Warning(ex, "Stale temp-file sweep failed (non-fatal)");
            }

            // Wire the static Core App stub so copied model code can reach settings.
            // Note: no launch-time SkillTree.Start() - WPF calls it per engine start only
            // (MainWindow.StartStop.cs:162), and a launch call would skew the time-of-day
            // usage counters that feed the night_shift/early_bird secret skills.
            CoreApp.Services = Services;

            // One-shot settings migrations WPF runs at startup (App.xaml.cs). Must run
            // before anything reads the migrated fields (Flash UI, GazeFocusService).
            try
            {
                var migrationSettings = Services.GetRequiredService<ISettingsService>();
                migrationSettings.Current.RunFlashClickableDecouplingMigration();
                migrationSettings.Save();
            }
            catch (Exception ex)
            {
                Log.Logger?.Warning(ex, "Settings migration failed (non-fatal, defaults apply)");
            }

            // Initialize localization before any UI is created so {loc:Str} bindings resolve.
            LocalizationManager.Instance.Initialize(CoreApp.Settings.Current?.Language ?? "en");

            // Wire the Avalonia bubble service into the legacy static facade.
            AvaloniaChaosEnv.Bubbles = (IAvaloniaBubbleService)CoreApp.Bubbles;

            // Load persistent Chaos Mode meta-progression once at startup.
            try
            {
                var env = Services.GetRequiredService<IAppEnvironment>();
                AvaloniaChaosEnv.EffectiveAssetsPath = env.EffectiveAssetsPath;
                ChaosMeta.Init(env);
            }
            catch (Exception ex)
            {
                Log.Logger?.Error(ex, "Failed to initialize Chaos meta state");
            }

            // Report any previous abnormal chaos session termination.
            Services.GetRequiredService<ChaosCrashSentinel>().ConsumeAndReport();

            // Hydrate the persisted moderation counter so escalation carries across launches.
            try { Services.GetRequiredService<IModerationCounter>().LoadFromDisk(); }
            catch (Exception ex) { Log.Logger?.Debug("ModerationCounter.LoadFromDisk failed: {Error}", ex.Message); }

            // Initialize the mod service (loads built-ins + user mods, restores active mod)
            // before any UI that depends on ActiveMod is created. The file I/O is run on a
            // background thread and awaited so cross-platform startup order is deterministic.
            var modService = Services.GetRequiredService<IModService>();
            var themeService = Services.GetRequiredService<AvaloniaThemeService>();
            try
            {
                Task.Run(() => modService.Initialize(CoreApp.Settings.Current.ActiveModId)).GetAwaiter().GetResult();
                themeService.ApplyCurrentTheme();
            }
            catch (Exception ex)
            {
                Log.Logger?.Error(ex, "Failed to initialize mod service or apply theme");
            }

            // BARK-1 slice 3: start the bark rule engine and wire its Core triggers (the awareness
            // pair closes AI-10; session/video/progression/achievement/quest; ModChanged reload).
            // Runs after mod init so the rule loader sees the merged active-mod manifest. Start() is
            // idempotent and the engine self-guards its gates; failures are non-fatal (bark is cosmetic).
            ConditioningControlPanel.Core.Services.Bark.BarkTriggerWiring? barkWiring = null;
            try
            {
                barkWiring = Services.GetService<ConditioningControlPanel.Core.Services.Bark.BarkTriggerWiring>();
                barkWiring?.Start();
            }
            catch (Exception ex)
            {
                Log.Logger?.Warning(ex, "BarkTriggerWiring start failed (non-fatal)");
            }

            // Subscribe to achievement unlocks, quest completions, and Pink Rush so the
            // Avalonia head can show popup toasts.
            var achievements = Services.GetRequiredService<IAchievementService>();
            achievements.AchievementUnlocked += OnAchievementUnlocked;

            var quests = Services.GetRequiredService<IQuestService>();
            quests.QuestCompleted += OnQuestCompleted;

            var skillTree = Services.GetRequiredService<ISkillTreeService>();
            skillTree.PinkRushStarted += OnPinkRushStarted;

            // ProfileSync live wiring (slice 7, plan §5): event-driven sync triggers. WPF pushes
            // on level-up (ProgressionService.cs:120) and quest completion (MainWindow.Quests.cs:78);
            // the service's own sync gate + 30s cooldown make extra triggers harmless.
            var profileSync = Services.GetRequiredService<IProfileSyncService>();
            quests.QuestCompleted += (_, _) => FireAndForgetProfileSync(profileSync, "quest-complete");
            Services.GetRequiredService<IProgressionService>().LevelUp +=
                (_, _) => FireAndForgetProfileSync(profileSync, "level-up");

            // Restored session (WPF App.xaml.cs:1860-1861 / :1953-1954 pulls the cloud profile and
            // starts the heartbeat after auth init). Avalonia has no startup OAuth validation flow;
            // a restored session is a persisted UnifiedId + AuthToken. The token value is only
            // checked for presence — never logged.
            var authSettings = Services.GetRequiredService<ISettingsService>().Current;
            if (!string.IsNullOrEmpty(authSettings?.UnifiedId) && !string.IsNullOrEmpty(authSettings?.AuthToken))
            {
                _ = Task.Run(async () =>
                {
                    try { await profileSync.LoadProfileAsync(); }
                    catch (Exception ex) { Log.Logger?.Warning(ex, "Startup cloud profile load failed"); }
                });
                profileSync.StartHeartbeat();
            }

            // If another instance is launched, bring this one to the foreground.
            var singleInstance = Services.GetRequiredService<ISingleInstanceService>();
            singleInstance.ArgumentsReceived += (_, _) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        RestoreMainWindow(desktop.MainWindow);
                    }
                });
            };

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Centralized persistence flush. Every exit path funnels through
                // desktop.Shutdown() (Exit menu, tray Exit, panic-key double press,
                // OS logoff), but only the Exit menu flushed settings, and nothing
                // disposed the achievement/quest services, losing up to 30s of dirty
                // counters that WPF preserves via OnExit disposal.
                desktop.Exit += (_, _) =>
                {
                    FlushPersistentState();
                    try { barkWiring?.Dispose(); } catch { /* best-effort bark unsubscribe on exit */ }
                };

                desktop.MainWindow = desktop.Args switch
                {
#if DEBUG
                    // Dev spike harness windows are Debug-only, like the smoke harness
                    // (WS0 lot 4 R1-10: they must not ship reachable in Release builds).
                    var a when a != null && a.Contains("--audio-spike") => new AudioSpikeWindow(),
                    var a when a != null && a.Contains("--inline-loop-spike") => new InlineLoopSpikeWindow(),
                    var a when a != null && a.Contains("--video-spike") => new VideoSpikeWindow(),
#endif
                    _ => new MainWindow
                    {
                        DataContext = Services.GetRequiredService<MainWindowViewModel>()
                    }
                };

                // Wire desktop tray icon.
                BenchmarkContext.Attach(desktop.MainWindow, desktop);

                var tray = Services.GetRequiredService<ITrayIcon>();
                tray.SetTooltip("Conditioning Control Panel");
                tray.Menu.AddItem("Show Dashboard", () => RestoreMainWindow(desktop.MainWindow));
                tray.Menu.AddItem("separator", () => { }, isSeparator: true);
                tray.Menu.AddItem("Exit", () => desktop.Shutdown());

                if (tray is Avalonia.Platform.AvaloniaTrayIcon avaloniaTray)
                {
                    avaloniaTray.Clicked += () => RestoreMainWindow(desktop.MainWindow);
                }

                tray.Show();

                // Launch behaviors, WPF MainWindow.xaml.cs:2102-2121: StartMinimized ->
                // AutoStartEngine -> ForceVideoOnLaunch, in that order, once the window
                // has opened. Spike windows are excluded (real dashboard only), and so are
                // harness runs (smoke/benchmark own the start/stop lifecycle themselves;
                // an auto-started engine would skew their assertions and measurements).
                var isHarnessRun = desktop.Args?.Any(a =>
                    a is "--smoke-test" or "--benchmark" or "--max-benchmark"
                    or "--verify-spiral" or "--verify-video" or "--verify-layers" or "--verify-visible" or "--verify-avatartube") == true;
                if (!isHarnessRun && desktop.MainWindow is MainWindow dashboardWindow)
                {
                    EventHandler? applyLaunchBehaviors = null;
                    applyLaunchBehaviors = async (_, _) =>
                    {
                        dashboardWindow.Opened -= applyLaunchBehaviors;
                        try
                        {
                            var launchSettings = Services.GetRequiredService<ISettingsService>().Current;
                            if (launchSettings == null) return;

                            if (launchSettings.StartMinimized)
                            {
                                // Let the window fully render before hiding (WPF waits 100ms
                                // to avoid black-window artifacts). Tray restore already exists.
                                await Task.Delay(100);
                                dashboardWindow.Hide();
                            }

                            if (launchSettings.AutoStartEngine &&
                                dashboardWindow.DataContext is MainWindowViewModel vm)
                            {
                                // WPF AutoStartEngine calls StartEngine(): an engine-only run,
                                // never a timed preset session.
                                vm.StartEngineOnlyRun();
                            }

                            if (launchSettings.ForceVideoOnLaunch)
                            {
                                await Task.Delay(200);
                                TriggerStartupVideo();
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Logger?.Warning(ex, "Launch behaviors failed");
                        }
                    };
                    dashboardWindow.Opened += applyLaunchBehaviors;

                    // Arm the scheduler engine: 30s window checks after a 60s grace
                    // period (WPF MainWindow.xaml.cs:443-465). Auto start/stop decisions
                    // are handled by MainWindowViewModel.WireSchedulerAndRampEvents.
                    // Guarded by the same isHarnessRun check as the launch behaviors:
                    // smoke/benchmark own the start/stop lifecycle, and a scheduler
                    // auto-start would skew their assertions and measurements.
                    if (dashboardWindow.DataContext is MainWindowViewModel schedulerVm)
                    {
                        Services.GetService<ISchedulerService>()
                            ?.Start(() => schedulerVm.IsEngineRunning);
                    }
                }
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                singleViewPlatform.MainView = new MainView
                {
                    DataContext = Services.GetRequiredService<MainWindowViewModel>()
                };
            }

            // The gaze-dwell attention-check mechanic was scrapped pre-ship in WPF per
            // design call (Core AppSettings.AttentionCheckEnabled comment: "disabled by
            // default and has no UI surface in this release"). WPF constructs the service
            // but never Start()s it. Parity: do NOT auto-start it here (WS0 lot 4 T1-1).
            // The service, control, and dialogs stay in the codebase for a future revival.

            // Activate click-driven gaze drift correction (constructs the head's implementation, which
            // self-starts). No-op on heads without a real webcam tracker.
            try { Services.GetService<ConditioningControlPanel.Core.Services.Webcam.IGazeDriftCorrectionService>(); }
            catch { }

            // Start Awareness Engine keyword triggers (premium-gated) and screen OCR if enabled.
            try
            {
                var settings = Services.GetRequiredService<ISettingsService>().Current;
                if (settings?.KeywordTriggersEnabled == true)
                {
                    Services.GetRequiredService<IKeywordTriggerService>().Start();
                }
                if (settings?.ScreenOcrEnabled == true)
                {
                    // Defer OCR engine initialization so startup stays light; the model load
                    // is heavy and is not needed in the first seconds after the window opens.
                    _ = Task.Run(async () =>
                    {
                        // Inner guard: an unguarded throw here becomes an unobserved
                        // task exception (the outer try/catch cannot see it because the
                        // task is fire-and-forget).
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10));
                            Services.GetService<IScreenOcrService>()?.Start();
                        }
                        catch (Exception ex)
                        {
                            Log.Logger?.Warning(ex, "Deferred screen-OCR start failed");
                        }
                    });
                }
            }
            catch { }

            // AI-1: window-awareness engine (free feature, consent-gated). Start iff BOTH flags
            // are set (WPF Services/UI/WindowAwarenessService.cs:336-342) and keep reacting to
            // the two settings toggles changing from any surface (awareness tab master switch
            // mirrors WPF MainWindow.Patreon.cs:1142-1143 by auto-granting consent on enable).
            try
            {
                var settingsService = Services.GetRequiredService<ISettingsService>();
                var awareness = Services.GetRequiredService<global::ConditioningControlPanel.Core.Services.Awareness.IAwarenessService>();

                void SyncAwareness()
                {
                    var s = settingsService.Current;
                    if (s?.AwarenessModeEnabled == true && s.AwarenessConsentGiven)
                        awareness.Start(); // no-ops on heads without a foreground-title provider
                    else
                        awareness.Stop();
                }

                void HookAwarenessSettings()
                {
                    var s = settingsService.Current;
                    if (s == null) return;
                    s.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName is nameof(global::ConditioningControlPanel.Models.AppSettings.AwarenessModeEnabled)
                            or nameof(global::ConditioningControlPanel.Models.AppSettings.AwarenessConsentGiven))
                        {
                            SyncAwareness();
                        }
                    };
                }

                HookAwarenessSettings();
                // Settings instance can be swapped (cloud restore/reset) - re-bind or the toggle goes dead.
                settingsService.CurrentReplaced += () => { HookAwarenessSettings(); SyncAwareness(); };
                SyncAwareness();
            }
            catch { }

            base.OnFrameworkInitializationCompleted();
        }
        catch (Exception ex)
        {
            Log.Logger?.Error(ex, "Startup failed");
            try
            {
                var dialog = Services?.GetService<IDialogService>();
                _ = dialog?.ShowMessageAsync(Loc.Get("title_error"), string.Format(Loc.Get("msg_startup_failed_fmt"), ex.Message));
            }
            catch { }
        }
    }

    private static bool _stateFlushed;

    /// <summary>
    /// Flush all debounce/interval-saved persistent state. Runs on desktop lifetime
    /// Exit so every shutdown path is covered; safe to call more than once.
    /// </summary>
    private static void FlushPersistentState()
    {
        if (_stateFlushed) return;
        _stateFlushed = true;

        // Parity with WPF App.xaml.cs OnExit → ChaosModeService.ForceShutdown (WPF
        // ChaosModeService.cs:3085): a mid-run exit must hard-tear-down the run so its crash
        // sentinel is CLEARED (P0-5) — otherwise a clean shutdown mid-descent false-positives a
        // native crash at the next launch. Best-effort, null-guarded (no-op when idle or unregistered).
        try { Services?.GetService<ConditioningControlPanel.IChaosService>()?.ForceShutdown(); }
        catch (Exception ex) { Log.Logger?.Warning(ex, "Chaos force-shutdown on exit failed"); }

        try { Services?.GetService<ISettingsService>()?.SaveImmediate(); }
        catch (Exception ex) { Log.Logger?.Warning(ex, "Settings flush on exit failed"); }

        // Exit sync (WPF App.xaml.cs:3071): settings are saved FIRST (above) so cloud sync cannot
        // overwrite the final local state, then the profile is pushed with a bounded ~2s wait so
        // shutdown is never blocked. Disposing the service also stops the 120s heartbeat timer
        // and releases its HttpClient.
        try
        {
            var profileSync = Services?.GetService<IProfileSyncService>();
            if (profileSync?.IsSyncEnabled == true)
            {
                Log.Logger?.Information("Syncing profile to cloud before exit...");
                profileSync.SyncProfileAsync().Wait(TimeSpan.FromSeconds(2));
            }
            (profileSync as IDisposable)?.Dispose();
        }
        catch (Exception ex) { Log.Logger?.Warning(ex, "Profile sync on exit failed"); }

        try { (Services?.GetService<IAchievementService>() as IDisposable)?.Dispose(); }
        catch (Exception ex) { Log.Logger?.Warning(ex, "Achievement flush on exit failed"); }

        try { (Services?.GetService<IQuestService>() as IDisposable)?.Dispose(); }
        catch (Exception ex) { Log.Logger?.Warning(ex, "Quest flush on exit failed"); }

        // Parity with WPF OnExit (App.xaml.cs:3035): release the hardware + trigger
        // sources that hold OS handles or child windows so they don't linger past
        // shutdown. Best-effort and null-guarded - GetService (not GetRequiredService)
        // so a head that never registered a seam degrades to a no-op, and only
        // IDisposable implementations are touched (IAsyncDisposable-only seams are
        // released by their own finalization path).
        DisposeServiceIfPossible(Services?.GetService<ConditioningControlPanel.Core.Services.Webcam.IWebcamService>(), "Webcam");
        DisposeServiceIfPossible(Services?.GetService<IHapticsService>(), "Haptics");
        DisposeServiceIfPossible(Services?.GetService<ConditioningControlPanel.Core.Services.Video.IVideoService>(), "Video");
        DisposeServiceIfPossible(Services?.GetService<IRemoteControlService>(), "RemoteControl");
        DisposeServiceIfPossible(Services?.GetService<IScreenOcrService>(), "ScreenOcr");
        DisposeServiceIfPossible(Services?.GetService<IKeywordTriggerService>(), "KeywordTrigger");

        // Parity with WPF OnExit: drop decrypted secrets from memory.
        ConditioningControlPanel.Core.Services.SecureAuthTokenStore.ClearMemoryCache();
        ConditioningControlPanel.Core.Services.SecureApiKeyStore.ClearMemoryCache();
    }

    /// <summary>
    /// Fire-and-forget profile sync trigger. The sync service already guards every failure
    /// mode internally; this wrapper only ensures a fault can never surface as an unobserved
    /// task exception. Never logs auth material.
    /// </summary>
    private static void FireAndForgetProfileSync(IProfileSyncService profileSync, string trigger)
    {
        _ = Task.Run(async () =>
        {
            try { await profileSync.SyncProfileAsync(); }
            catch (Exception ex) { Log.Logger?.Debug("Profile sync trigger '{Trigger}' failed: {Error}", trigger, ex.Message); }
        });
    }

    /// <summary>
    /// Best-effort synchronous disposal of a resolved service. No-op when the service
    /// is null or not <see cref="IDisposable"/>; never throws.
    /// </summary>
    private static void DisposeServiceIfPossible(object? service, string name)
    {
        if (service is not IDisposable disposable) return;
        try { disposable.Dispose(); }
        catch (Exception ex) { Log.Logger?.Warning(ex, "{Service} dispose on exit failed", name); }
    }

    /// <summary>
    /// Plays the configured startup video, or a random one when none is set.
    /// WPF MainWindow.UiUpdates.cs:1421 TriggerStartupVideo parity.
    /// </summary>
    private static void TriggerStartupVideo()
    {
        try
        {
            var settings = Services?.GetService<ISettingsService>()?.Current;
            var video = Services?.GetService<ConditioningControlPanel.Core.Services.Video.IVideoService>();
            if (settings == null || video == null) return;

            var startupPath = settings.StartupVideoPath;
            if (!string.IsNullOrEmpty(startupPath) && File.Exists(startupPath))
            {
                Log.Information("Playing startup video: {Path}", startupPath);
                video.PlaySpecificVideo(startupPath, settings.StrictLockEnabled);
            }
            else
            {
                Log.Information("Playing random startup video");
                video.TriggerVideo();
            }
        }
        catch (Exception ex)
        {
            Log.Logger?.Warning(ex, "Startup video failed");
        }
    }

    private static void RestoreMainWindow(Window? window)
    {
        if (window is null) return;
        window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        EnsureOnScreen(window);
        window.Activate();
    }

    /// <summary>
    /// Mirrors WPF TrayIconService.EnsureOnScreen (Services/Notifications/TrayIconService.cs:191, #475):
    /// MinimizeToTray keeps the window's last position, so restoring after a monitor is unplugged or a
    /// resolution change leaves it off-screen and invisible while the tray icon looks broken. If less
    /// than 60x30 px of the window overlaps any screen's working area (not enough to grab the title bar),
    /// re-center on primary. Best-effort and fully guarded.
    /// Coordinate note: unlike WPF (Left/Top in DIPs), Avalonia Window.Position is already physical px,
    /// while Width/Height (here Bounds, the rendered size) are DIPs and are converted via RenderScaling.
    /// screen.WorkingArea is physical px.
    /// </summary>
    private static void EnsureOnScreen(Window window)
    {
        try
        {
            var screens = window.Screens;
            var all = screens?.All;
            if (all is null || all.Count == 0) return;

            double scaling = window.RenderScaling <= 0 ? 1.0 : window.RenderScaling;
            int w = (int)(window.Bounds.Width * scaling);
            int h = (int)(window.Bounds.Height * scaling);
            if (w <= 0 || h <= 0) return; // size not laid out yet; don't risk a bad re-center

            int rx = window.Position.X, ry = window.Position.Y;
            int rr = rx + w, rb = ry + h;
            foreach (var screen in all)
            {
                var wa = screen.WorkingArea; // physical px
                int iw = Math.Min(wa.Right, rr) - Math.Max(wa.X, rx);
                int ih = Math.Min(wa.Bottom, rb) - Math.Max(wa.Y, ry);
                if (iw >= 60 && ih >= 30) return; // enough to grab the title bar
            }

            var primaryWa = (screens!.Primary ?? all[0]).WorkingArea;
            int cx = primaryWa.X + (primaryWa.Width - w) / 2;
            int cy = primaryWa.Y + (primaryWa.Height - h) / 2;
            window.Position = new PixelPoint(cx, cy);
            Log.Logger?.Information("Tray restore: window was off-screen, re-centered on primary");
        }
        catch (Exception ex)
        {
            Log.Logger?.Debug("EnsureOnScreen failed: {Error}", ex.Message);
        }
    }

    private static void ConfigureLogging()
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConditioningControlPanel",
            "logs");

        try
        {
            Directory.CreateDirectory(logPath);
        }
        catch
        {
            logPath = Path.Combine(Path.GetTempPath(), "ConditioningControlPanel", "logs");
            try { Directory.CreateDirectory(logPath); } catch { }
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(Path.Combine(logPath, "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .WriteTo.Console()
            .CreateLogger();

        // Route the shared (Core) display-change spawn-suppress diagnostic through Serilog.
        ConditioningControlPanel.Core.Services.DisplayChangeCoordinator.DebugLog = msg => Log.Logger.Debug(msg);
    }

    private static void OnAchievementUnlocked(object? sender, ConditioningControlPanel.Models.Achievement achievement)
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime)
            {
                // Achievement pop-ups are window-based; skip on mobile lifetimes.
                return;
            }

            var popup = new Windows.AchievementPopup(achievement);
            popup.Show();
        }
        catch (Exception ex)
        {
            App.Services?.GetRequiredService<ILogger<App>>().LogError(ex, "Failed to show achievement popup");
        }
    }

    private static void OnQuestCompleted(object? sender, QuestCompletedEventArgs e)
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime)
                return;

            var popup = new Windows.QuestCompletePopup(e.QuestName, e.XpAwarded, e.QuestType);
            popup.Show();
        }
        catch (Exception ex)
        {
            App.Services?.GetRequiredService<ILogger<App>>().LogError(ex, "Failed to show quest complete popup");
        }
    }

    private static void OnPinkRushStarted(object? sender, EventArgs e)
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime)
                return;

            var popup = new Windows.PinkRushPopup();
            popup.Show();
        }
        catch (Exception ex)
        {
            App.Services?.GetRequiredService<ILogger<App>>().LogError(ex, "Failed to show pink rush popup");
        }
    }
}
