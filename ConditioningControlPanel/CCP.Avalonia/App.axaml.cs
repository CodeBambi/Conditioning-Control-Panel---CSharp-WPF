using System;
using System.IO;
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
            CoreApp.Services = Services;
            CoreApp.SkillTree.Start();

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
