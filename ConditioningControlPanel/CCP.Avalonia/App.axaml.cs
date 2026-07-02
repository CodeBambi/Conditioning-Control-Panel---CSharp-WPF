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
            Log.Logger?.Error(e.Exception, "Unhandled UI thread exception");
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

            // Subscribe to achievement unlocks, quest completions, and Pink Rush so the
            // Avalonia head can show popup toasts.
            var achievements = Services.GetRequiredService<IAchievementService>();
            achievements.AchievementUnlocked += OnAchievementUnlocked;

            var quests = Services.GetRequiredService<IQuestService>();
            quests.QuestCompleted += OnQuestCompleted;

            var skillTree = Services.GetRequiredService<ISkillTreeService>();
            skillTree.PinkRushStarted += OnPinkRushStarted;

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
                desktop.Exit += (_, _) => FlushPersistentState();

                desktop.MainWindow = desktop.Args switch
                {
                    var a when a != null && a.Contains("--audio-spike") => new AudioSpikeWindow(),
                    var a when a != null && a.Contains("--inline-loop-spike") => new InlineLoopSpikeWindow(),
                    var a when a != null && a.Contains("--video-spike") => new VideoSpikeWindow(),
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
                    a is "--smoke-test" or "--benchmark" or "--max-benchmark") == true;
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

            // Start attention-check scheduler if the user has it enabled.
            try
            {
                var settings = Services.GetRequiredService<ISettingsService>().Current;
                if (settings?.AttentionCheckEnabled == true)
                {
                    Services.GetRequiredService<IAttentionCheckService>().Start();
                }
            }
            catch { }

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
                        await Task.Delay(TimeSpan.FromSeconds(10));
                        Services.GetService<IScreenOcrService>()?.Start();
                    });
                }
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

        try { Services?.GetService<ISettingsService>()?.SaveImmediate(); }
        catch (Exception ex) { Log.Logger?.Warning(ex, "Settings flush on exit failed"); }

        try { (Services?.GetService<IAchievementService>() as IDisposable)?.Dispose(); }
        catch (Exception ex) { Log.Logger?.Warning(ex, "Achievement flush on exit failed"); }

        try { (Services?.GetService<IQuestService>() as IDisposable)?.Dispose(); }
        catch (Exception ex) { Log.Logger?.Warning(ex, "Quest flush on exit failed"); }

        // Parity with WPF OnExit: drop decrypted secrets from memory.
        ConditioningControlPanel.Core.Services.SecureAuthTokenStore.ClearMemoryCache();
        ConditioningControlPanel.Core.Services.SecureApiKeyStore.ClearMemoryCache();
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
        window.Activate();
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
