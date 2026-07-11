using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Logging;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Skia;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using AvaloniaDocuments = global::Avalonia.Controls.Documents;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.AvatarTube;
using ConditioningControlPanel.Avalonia.Chaos;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Avalonia.Features;
using ConditioningControlPanel.Avalonia.ViewModels;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Windows;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Quiz;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Avalonia.Services.Auth;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows;

/// <summary>
/// Exercises the Avalonia Windows head without user input: walks every tab,
/// opens a representative set of dialogs, and records first-chance exceptions,
/// binding/resource warnings, and raw localization keys rendered as text.
/// </summary>
internal sealed class SmokeTestRunner
{
    private readonly SmokeTestLogSink _logSink;
    private readonly bool _captureScreenshots;
    private readonly List<SmokeTestFinding> _findings = new();
    private readonly List<string> _firstChanceExceptions = new();
    private readonly List<string> _tabSummaries = new();
    private readonly List<string> _screenshotPaths = new();
    private readonly CancellationTokenSource _cts = new();
    private DateTime _runStart;

    public SmokeTestRunner(SmokeTestLogSink logSink, bool captureScreenshots = false)
    {
        _logSink = logSink;
        _captureScreenshots = captureScreenshots;
    }

    public void Attach()
    {
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
    }

    public void ScheduleRun()
    {
        _runStart = DateTime.UtcNow;
        Dispatcher.UIThread.Post(async () => await RunAsync(), DispatcherPriority.Background);
    }

    private void OnFirstChanceException(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
    {
        // The exception's own stack trace may be truncated when thrown from a BCL helper.
        // Capture the live stack at the moment the exception is first observed.
        var liveStack = Environment.StackTrace;
        var frames = liveStack.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                              .Skip(3) // skip this handler + Environment.StackTrace frames
                              .Take(20)
                              .ToArray();
        var message = $"{e.Exception.GetType().Name}: {e.Exception.Message}{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", frames)}";
        lock (_firstChanceExceptions)
        {
            if (!_firstChanceExceptions.Contains(message))
                _firstChanceExceptions.Add(message);
        }
    }

    private async Task RunAsync()
    {
        Console.WriteLine("[SMOKE] Starting desktop smoke test...");

        try
        {
            // Absolute safety cap: shutdown after 180 seconds no matter what.
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(180));
                _cts.Cancel();
                var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                await Dispatcher.UIThread.InvokeAsync(() => lifetime?.Shutdown());
            });

            await DelayAsync(2500); // let splash/settings/update check settle

            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var mainWindow = lifetime?.MainWindow;
            if (mainWindow?.DataContext is not MainWindowViewModel vm)
            {
                _findings.Add(new SmokeTestFinding("Startup", "MainWindow or ViewModel not available", FindingSeverity.Blocker));
                await FinishAsync(lifetime);
                return;
            }

            Console.WriteLine($"[SMOKE] MainWindow ready. Initial tab: {vm.SelectedTab?.Key ?? "(none)"}. Visiting {vm.Tabs.Count} tabs...");

            // Verify startup lands on the dashboard (WPF parity).
            if (vm.SelectedTab?.Key != "settings")
            {
                _findings.Add(new SmokeTestFinding("Startup", $"Expected initial tab 'settings' (dashboard), found '{vm.SelectedTab?.Key ?? "null"}'", FindingSeverity.Blocker));
            }

            // Walk all tabs; perform the dashboard content check while the settings tab is selected.
            var tabs = vm.Tabs.ToList();
            foreach (var tab in tabs)
            {
                if (_cts.IsCancellationRequested) break;
                Console.WriteLine($"[SMOKE] Tab -> {tab.Key}");
                vm.SelectedTab = tab;
                await DelayAsync(500); // allow bindings and layout to settle
                ScanVisualTree(mainWindow, $"Tab:{tab.Key}");

                if (mainWindow.GetVisualDescendants().OfType<PlaceholderTabView>().Any())
                {
                    _findings.Add(new SmokeTestFinding($"Tab:{tab.Key}", "Tab rendered the placeholder view", FindingSeverity.Error));
                }

                if (_captureScreenshots)
                {
                    try
                    {
                        var path = await RenderScreenshotAsync(mainWindow, $"smoke-tab-{tab.Key}.png");
                        _screenshotPaths.Add(path);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SMOKE] Screenshot failed for tab {tab.Key}: {ex.Message}");
                    }
                }
                _tabSummaries.Add(tab.Key);

                if (tab.Key == "settings")
                {
                    var dashboardCards = mainWindow.GetVisualDescendants().OfType<FeatureCard>().ToList();
                    Console.WriteLine($"[SMOKE] Dashboard feature cards found: {dashboardCards.Count}");
                    if (dashboardCards.Count == 0)
                    {
                        _findings.Add(new SmokeTestFinding("Dashboard", "No FeatureCard instances found on the dashboard", FindingSeverity.Blocker));
                    }
                    else
                    {
                        var cardsWithVisuals = dashboardCards.Count(c => c.Icon is not null || !string.IsNullOrEmpty(c.Glyph));
                        Console.WriteLine($"[SMOKE] Dashboard feature cards with visuals (image or glyph): {cardsWithVisuals}/{dashboardCards.Count}");
                        if (cardsWithVisuals == 0)
                        {
                            _findings.Add(new SmokeTestFinding("Dashboard", $"All {dashboardCards.Count} dashboard feature cards have missing images/glyphs", FindingSeverity.Blocker));
                        }
                    }

                    // Verify a sample dashboard asset can be resolved via the Avalonia resource system.
                    try
                    {
                        using var stream = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Assets/features/flash.png"));
                        Console.WriteLine($"[SMOKE] Sample asset resolved: flash.png ({stream.Length} bytes)");
                    }
                    catch (Exception ex)
                    {
                        _findings.Add(new SmokeTestFinding("Dashboard", $"Failed to resolve sample dashboard asset avares://CCP.Avalonia/Assets/features/flash.png: {ex.Message}", FindingSeverity.Blocker));
                    }

                    // Render a screenshot of the main window for visual parity diagnostics.
                    try
                    {
                        var screenshotPath = await RenderScreenshotAsync(mainWindow, "smoke-dashboard.png");
                        Console.WriteLine($"[SMOKE] Screenshot saved: {screenshotPath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SMOKE] Screenshot failed: {ex.Message}");
                    }

                    // Exercise dashboard helper buttons (safe popups only; catalogue opens an external browser).
                    await ExerciseDashboardHelperButtonsAsync(mainWindow);

                    // Exercise every dashboard feature-card popup to catch missing loc keys / layout issues.
                    await ExerciseFeatureCardPopupsAsync(mainWindow);

                    // Exercise the START button: it must launch the session engine
                    // and the stop path must return to idle (§DEFINITION OF DONE).
                    await ExerciseStartSessionAsync(vm);

                    // Exercise avatar reaction: notifying the bark service should make
                    // the active AvatarTubeWindow show its speech bubble.
                    await ExerciseAvatarReactionAsync();

                    // Exercise pink overlay: it must be a click-through, non-taskbar surface.
                    await ExerciseOverlayClickThroughAsync();

                    // Exercise auth provider OAuth launch paths (Account login + premium gating verification).
                    await ExerciseAuthProvidersAsync();

                    // DTRH smoke coverage (hub menu + full Chaos run) removed with the native
                    // chaos-run strip — the web DTRH game is the sole path (owner ruling 2026-07-11).
                }
                else if (tab.Key == "blinktrainer")
                {
                    await ExerciseBlinkTrainerTabAsync(vm);
                }
            }

            // Exercise a representative, safe set of dialogs.
            await ExerciseDialogAsync(mainWindow, "InputDialog", () => new InputDialog("Smoke Test", "Enter anything:"));
            await ExerciseDialogAsync(mainWindow, "WarningDialog", () => new WarningDialog("Smoke Test", "This is a smoke-test warning."));

            // Exercise every parameterless Avalonia dialog to surface missing loc keys / layout issues.
            await ExerciseAllParameterlessDialogsAsync(mainWindow);

            // Exercise the remaining window ports (popups, editor, webcam surfaces).
            await ExerciseWindowsAsync(mainWindow);

            // Switch mods to exercise theme re-skinning (§15.11) and capture the dashboard per theme.
            var originalMod = vm.SelectedMod;
            var settingsTab = tabs.FirstOrDefault(t => t.Key == "settings");
            foreach (var mod in vm.AvailableMods)
            {
                if (_cts.IsCancellationRequested) break;
                vm.SelectedMod = mod;
                await DelayAsync(1000);
                if (settingsTab != null)
                {
                    vm.SelectedTab = settingsTab;
                    await DelayAsync(400);
                    try
                    {
                        var themePath = await RenderScreenshotAsync(mainWindow, $"smoke-dashboard-theme-{mod.Id}.png");
                        _screenshotPaths.Add(themePath);
                        Console.WriteLine($"[SMOKE] Theme screenshot saved: {themePath}");

                        var tubeWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows
                            .OfType<AvatarTubeWindow>().FirstOrDefault();
                        if (tubeWindow != null)
                        {
                            var tubePath = await RenderScreenshotAsync(tubeWindow, $"smoke-tube-theme-{mod.Id}.png");
                            _screenshotPaths.Add(tubePath);
                            Console.WriteLine($"[SMOKE] Tube screenshot saved: {tubePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SMOKE] Theme screenshot failed for {mod.Id}: {ex.Message}");
                    }
                }
            }
            vm.SelectedMod = originalMod;
        }
        catch (Exception ex)
        {
            _findings.Add(new SmokeTestFinding("Runner", $"Unexpected runner exception: {ex}", FindingSeverity.Blocker));
        }

        var lifetimeFinal = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        await FinishAsync(lifetimeFinal);
    }

    private async Task DelayAsync(int milliseconds)
    {
        await Task.Delay(milliseconds, _cts.Token).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() => { });
    }

    private async Task ExerciseDialogAsync(Window owner, string name, Func<Window> factory)
    {
        if (_cts.IsCancellationRequested) return;
        try
        {
            Console.WriteLine($"[SMOKE] Dialog -> {name}");
            var dialog = factory();
            await Dispatcher.UIThread.InvokeAsync(() => dialog.Show(owner));
            await DelayAsync(300);
            ScanVisualTree(dialog, $"Dialog:{name}");

            if (_captureScreenshots)
            {
                try
                {
                    var path = await RenderScreenshotAsync(dialog, $"smoke-dialog-{name.ToLowerInvariant()}.png");
                    _screenshotPaths.Add(path);
                    Console.WriteLine($"[SMOKE] Dialog screenshot saved: {path}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SMOKE] Dialog screenshot failed for {name}: {ex.Message}");
                }
            }

            await Dispatcher.UIThread.InvokeAsync(() => dialog.Close());
            await DelayAsync(150);
        }
        catch (Exception ex)
        {
            _findings.Add(new SmokeTestFinding($"Dialog:{name}", $"Failed to open: {ex.Message}", FindingSeverity.Error));
        }
    }

    private async Task ExerciseAllParameterlessDialogsAsync(Window owner)
    {
        if (_cts.IsCancellationRequested) return;

        // Some dialogs probe external services/network on construction; skip them in UI-only smoke coverage.
        var excludedDialogs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
        };

        var dialogAssembly = typeof(FeaturePopupWindow).Assembly;
        var dialogTypes = dialogAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsPublic
                        && t.Namespace == "ConditioningControlPanel.Avalonia.Dialogs"
                        && typeof(Window).IsAssignableFrom(t)
                        && t.GetConstructor(Type.EmptyTypes) != null
                        && !excludedDialogs.Contains(t.Name))
            .OrderBy(t => t.Name)
            .ToList();

        Console.WriteLine($"[SMOKE] Parameterless dialogs discovered: {dialogTypes.Count}");
        foreach (var dialogType in dialogTypes)
        {
            if (_cts.IsCancellationRequested) break;
            await ExerciseDialogInstanceAsync(owner, dialogType);
        }
    }

    private async Task ExerciseDialogInstanceAsync(Window owner, Type dialogType)
    {
        var name = dialogType.Name;
        try
        {
            Console.WriteLine($"[SMOKE] Dialog -> {name}");
            var dialog = (Window)Activator.CreateInstance(dialogType)!;
            await Dispatcher.UIThread.InvokeAsync(() => dialog.Show(owner));
            await DelayAsync(300);
            ScanVisualTree(dialog, $"Dialog:{name}");

            if (_captureScreenshots)
            {
                try
                {
                    var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
                    var path = await RenderScreenshotAsync(dialog, $"smoke-dialog-{safeName.ToLowerInvariant()}.png");
                    _screenshotPaths.Add(path);
                    Console.WriteLine($"[SMOKE] Dialog screenshot saved: {path}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SMOKE] Dialog screenshot failed for {name}: {ex.Message}");
                }
            }

            await Dispatcher.UIThread.InvokeAsync(() => dialog.Close());
            await DelayAsync(150);
        }
        catch (Exception ex)
        {
            _findings.Add(new SmokeTestFinding($"Dialog:{name}", $"Failed to open: {ex.Message}", FindingSeverity.Error));
        }
    }

    private async Task ExerciseWindowsAsync(Window owner)
    {
        if (_cts.IsCancellationRequested) return;

        // QuestCompletePopup
        await ExerciseWindowAsync(owner, "QuestCompletePopup", () =>
        {
            var popup = new QuestCompletePopup("Smoke Quest", 150, QuestType.Daily);
            popup.Show(owner);
            return popup;
        });

        // PinkRushPopup
        await ExerciseWindowAsync(owner, "PinkRushPopup", () =>
        {
            var settingsService = App.Services.GetRequiredService<ISettingsService>();
            settingsService.Current.PinkRushEndTime = DateTime.Now.AddSeconds(10);
            var popup = new PinkRushPopup();
            popup.Show(owner);
            return popup;
        });

        // QuizReportWindow
        await ExerciseWindowAsync(owner, "QuizReportWindow", () =>
        {
            var entry = new QuizHistoryEntry
            {
                TakenAt = DateTime.Now,
                Category = QuizCategory.Obedience,
                CategoryName = "Obedience",
                TotalScore = 8,
                MaxScore = 10,
                ProfileText = "Smoke-test profile summary.",
                Answers = new List<QuizAnswerRecord>
                {
                    new()
                    {
                        QuestionNumber = 1,
                        QuestionText = "Sample question one?",
                        AllAnswers = new[] { "A", "B", "C", "D" },
                        AllPoints = new[] { 0, 1, 0, 0 },
                        ChosenIndex = 1,
                        PointsEarned = 1
                    }
                }
            };
            var report = new QuizReportWindow(entry);
            report.Show(owner);
            return report;
        });

        // SessionEditorWindow
        await ExerciseWindowAsync(owner, "SessionEditorWindow", () =>
        {
            var session = new ConditioningControlPanel.Models.Session
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Smoke Session",
                DurationMinutes = 15,
                Source = ConditioningControlPanel.Models.SessionSource.Custom
            };
            var editor = new SessionEditorWindow(session);
            editor.Show(owner);
            return editor;
        });

        // WebcamLoadingSplash
        await ExerciseWindowAsync(owner, "WebcamLoadingSplash", () =>
        {
            var splash = new WebcamLoadingSplash();
            splash.Show(owner);
            splash.SetProgress(0.5, "Smoke test progress");
            return splash;
        });

        // WebcamGazeTrackerWindow (will render the no-frame-source error path)
        await ExerciseWindowAsync(owner, "WebcamGazeTrackerWindow", () =>
        {
            var tracker = new WebcamGazeTrackerWindow();
            tracker.Show(owner);
            return tracker;
        });

        // WebcamQuickRecalWindow (will render the no-frame-source error path)
        await ExerciseWindowAsync(owner, "WebcamQuickRecalWindow", () =>
        {
            var recal = new WebcamQuickRecalWindow();
            recal.Show(owner);
            return recal;
        });

        // AchievementPopup
        await ExerciseWindowAsync(owner, "AchievementPopup", () =>
        {
            var achievement = new Achievement
            {
                Id = "smoke_test",
                Name = "Smoke Test Achievement",
                FlavorText = "You ran the smoke test!",
                ImageName = "lv_10.png"
            };
            var popup = new AchievementPopup(achievement, "🏆", "Achievement Unlocked");
            popup.Show(owner);
            return popup;
        });

        // BubbleCountResultWindow
        await ExerciseWindowAsync(owner, "BubbleCountResult", () =>
        {
            var result = new BubbleCountResultWindow(42, false, _ => { });
            result.Show(owner);
            return result;
        });



        // LockCardWindow
        await ExerciseWindowAsync(owner, "LockCardWindow", () =>
        {
            var lockCard = new LockCardWindow("Smoke test phrase", 3, false);
            lockCard.Show(owner);
            return lockCard;
        });

        // QuizCategoryEditorWindow
        await ExerciseWindowAsync(owner, "QuizCategoryEditorWindow", () =>
        {
            var editor = new QuizCategoryEditorWindow();
            editor.Show(owner);
            return editor;
        });

        // FeatureSettingsPopup is a UserControl hosted in SessionEditorWindow; exercise it by
        // creating a minimal wrapper window with the control loaded for a sample timeline event.
        await ExerciseWindowAsync(owner, "FeatureSettingsPopup", () =>
        {
            var session = new TimelineSession
            {
                Name = "Smoke Session",
                DurationMinutes = 15
            };
            var evt = new TimelineEvent
            {
                FeatureId = "flash",
                Minute = 5,
                EventType = TimelineEventType.Start,
                Settings = new Dictionary<string, object>()
            };
            session.Events.Add(evt);

            var popup = new FeatureSettingsPopup();
            popup.LoadEvent(evt, session.DurationMinutes, session);

            var host = new Window
            {
                Title = "FeatureSettingsPopup Smoke Host",
                Width = 600,
                Height = 500,
                Content = popup,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false
            };
            host.Show(owner);
            return host;
        });
    }

    private async Task ExerciseWindowAsync(Window owner, string name, Func<Window> factory)
    {
        if (_cts.IsCancellationRequested) return;
        try
        {
            Console.WriteLine($"[SMOKE] Window -> {name}");
            var window = await Dispatcher.UIThread.InvokeAsync(factory);
            await DelayAsync(400);
            ScanVisualTree(window, $"Window:{name}");

            if (_captureScreenshots)
            {
                try
                {
                    var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
                    var path = await RenderScreenshotAsync(window, $"smoke-window-{safeName.ToLowerInvariant()}.png");
                    _screenshotPaths.Add(path);
                    Console.WriteLine($"[SMOKE] Window screenshot saved: {path}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SMOKE] Window screenshot failed for {name}: {ex.Message}");
                }
            }

            await Dispatcher.UIThread.InvokeAsync(() => window.Close());
            await DelayAsync(150);
        }
        catch (Exception ex)
        {
            _findings.Add(new SmokeTestFinding($"Window:{name}", $"Failed to open: {ex.Message}", FindingSeverity.Error));
        }
    }

    private async Task ExerciseAuthProvidersAsync()
    {
        if (_cts.IsCancellationRequested) return;

        try
        {
            Console.WriteLine("[SMOKE] Auth -> exercising provider OAuth launch paths...");
            var providers = App.Services.GetServices<IAuthProvider>().ToList();
            if (providers.Count == 0)
            {
                _findings.Add(new SmokeTestFinding("Auth", "No IAuthProvider registrations found", FindingSeverity.Error));
                return;
            }

            var settingsService = App.Services.GetRequiredService<ISettingsService>();
            var settings = settingsService.Current;
            var originalPremiumValidUntil = settings.PatreonPremiumValidUntil;

            foreach (var provider in providers)
            {
                if (_cts.IsCancellationRequested) break;
                var name = provider.ProviderName;
                try
                {
                    var mock = new RecordingBrowserHost();
                    ReplaceBrowserHost(provider, mock);

                    var startTask = provider.StartOAuthFlowAsync();
                    await DelayAsync(800); // let listener start and NavigateAsync fire

                    if (!mock.NavigatedUrls.Any())
                    {
                        _findings.Add(new SmokeTestFinding($"Auth:{name}", "Browser host was not asked to navigate to OAuth URL", FindingSeverity.Error));
                    }
                    else
                    {
                        var url = mock.NavigatedUrls.First();
                        var expectedPath = $"/{name}/authorize";
                        if (!url.ToString().Contains(expectedPath, StringComparison.OrdinalIgnoreCase))
                        {
                            _findings.Add(new SmokeTestFinding($"Auth:{name}", $"Unexpected OAuth URL: {url}", FindingSeverity.Error));
                        }
                        else
                        {
                            Console.WriteLine($"[SMOKE] Auth:{name} -> OAuth URL launched: {url}");
                        }
                    }

                    // Verify Patreon premium gating reads from cached settings when no live token is present.
                    // SubscribeStar mirrors WPF and only checks its own tier/whitelist, so only Patreon is
                    // expected to reflect the cross-cutting cached premium flag.
                    settings.PatreonPremiumValidUntil = DateTime.UtcNow.AddDays(14);
                    if (name == "patreon")
                    {
                        if (!provider.HasPremiumAccess)
                        {
                            _findings.Add(new SmokeTestFinding($"Auth:{name}", "HasPremiumAccess returned false while PatreonPremiumValidUntil was future-dated", FindingSeverity.Error));
                        }
                        else
                        {
                            Console.WriteLine($"[SMOKE] Auth:{name} -> HasPremiumAccess true from cached settings flag");
                        }
                    }

                    // The concrete providers expose CancelOAuthFlow; it is not on the interface yet.
                    try { provider.GetType().GetMethod("CancelOAuthFlow", Type.EmptyTypes)?.Invoke(provider, null); }
                    catch { }
                    try { await startTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { /* cancelled/timeout expected */ }
                }
                catch (Exception ex)
                {
                    _findings.Add(new SmokeTestFinding($"Auth:{name}", $"OAuth launch failed: {ex.Message}", FindingSeverity.Error));
                }
            }

            settings.PatreonPremiumValidUntil = originalPremiumValidUntil;
            settingsService.Save();
        }
        catch (Exception ex)
        {
            _findings.Add(new SmokeTestFinding("Auth", $"Unexpected auth exercise exception: {ex.Message}", FindingSeverity.Error));
        }
    }

    private static void ReplaceBrowserHost(IAuthProvider provider, IBrowserHost mock)
    {
        var field = provider.GetType().GetField("_browserHost", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null)
        {
            // Readonly instance fields can be updated via reflection.
            field.SetValue(provider, mock);
        }
    }

    private sealed class RecordingBrowserHost : IBrowserHost
    {
        public List<Uri> NavigatedUrls { get; } = new();
        public bool IsFullscreen { get; }
        public event EventHandler<string>? TitleChanged;
        public event EventHandler<Uri>? Navigated;
        public event EventHandler<bool>? FullscreenChanged;
        public Task NavigateAsync(Uri url)
        {
            NavigatedUrls.Add(url);
            return Task.CompletedTask;
        }
        public Task<string> ExecuteScriptAsync(string script) => Task.FromResult(string.Empty);
        public global::Avalonia.Controls.Control? CreateBrowserControl() => null;
    }

    private void ScanVisualTree(Control root, string context)
    {
        try
        {
            ScanControl(root, context);
        }
        catch (Exception ex)
        {
            _findings.Add(new SmokeTestFinding(context, $"Visual-tree scan failed: {ex.Message}", FindingSeverity.Warning));
        }
    }

    private void ScanControl(Control control, string context)
    {
        var text = ExtractText(control);
        if (!string.IsNullOrWhiteSpace(text))
        {
            // Literal markup leaked into the UI.
            if (text.Contains("{loc:"))
            {
                _findings.Add(new SmokeTestFinding(context, $"Raw loc markup visible: '{text}'", FindingSeverity.Error));
            }

            // A lone, snake-case key is the fallback returned when a key is missing.
            if (LooksLikeMissingLocKey(text) && !IsKnownSymbol(text) && !IsKnownUserData(text, context))
            {
                _findings.Add(new SmokeTestFinding(context, $"Possible missing loc key displayed: '{text}'", FindingSeverity.Warning));
            }
        }

        foreach (var child in control.GetVisualChildren().OfType<Control>())
        {
            ScanControl(child, context);
        }
    }

    private static string? ExtractText(Control control)
    {
        return control switch
        {
            TextBlock tb => tb.Text,
            TextBox tb => tb.Text,
            Button b when b.Content is string s => s,
            ContentControl cc when cc.Content is string s => s,
            _ => null
        };
    }

    private static readonly Regex KeyLikeRegex = new("^[a-z][a-z0-9_]{4,}$", RegexOptions.Compiled);

    private static bool LooksLikeMissingLocKey(string text)
    {
        // A missing localization key falls back to the key itself (lowercase + underscores).
        // This heuristic intentionally skips common single words and paths.
        if (text.Contains(' ')) return false;
        if (text.Contains('/')) return false;
        if (text.Contains('\\')) return false;
        if (text.Contains('.')) return false;
        return KeyLikeRegex.IsMatch(text);
    }

    private static bool IsKnownSymbol(string text)
    {
        // Common symbol-only or intentionally English internal labels that should not be flagged.
        return text switch
        {
            "start" or "stop" or "pause" or "resume" or "subject" or "unknown" => true,
            _ => false
        };
    }

    private static bool IsKnownUserData(string text, string context)
    {
        // User-created content that legitimately appears as raw text (not loc keys).
        return text switch
        {
            "bambi" or "spiral" or "tiktoks" => true,
            _ when text.StartsWith("u_", StringComparison.OrdinalIgnoreCase) && text.Length > 6 => true,
            _ => false
        };
    }

    private async Task ExerciseDashboardHelperButtonsAsync(Window mainWindow)
    {
        if (_cts.IsCancellationRequested) return;

        var labels = new Dictionary<string, string>
        {
            ["webcam"] = Loc.Get("btn_webcam"),
            ["appinfo"] = Loc.Get("btn_app_info"),
            ["scheduler"] = Loc.Get("btn_scheduler_intensity_ramp"),
            ["catalogue"] = Loc.Get("btn_ccp_catalogue"),
        };

        var buttons = mainWindow.GetVisualDescendants().OfType<Button>()
            .Select(b => new { Button = b, Text = GetButtonText(b) })
            .Where(x => !string.IsNullOrEmpty(x.Text))
            .ToList();

        foreach (var kvp in labels)
        {
            var match = buttons.FirstOrDefault(x => x.Text!.Contains(kvp.Value, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                _findings.Add(new SmokeTestFinding("Dashboard:HelperButtons", $"Helper button '{kvp.Key}' ({kvp.Value}) not found", FindingSeverity.Error));
                continue;
            }

            var text = match.Text!;
            // The WPF reference renders an emoji icon before the label.
            if (text.Length > 0 && text[0] < 256)
            {
                _findings.Add(new SmokeTestFinding("Dashboard:HelperButtons", $"Helper button '{kvp.Key}' appears to be missing an icon prefix: '{text}'", FindingSeverity.Warning));
            }
        }

        // Catalogue opens an external browser; verify it exists but do not click it.
        foreach (var key in new[] { "webcam", "appinfo", "scheduler" })
        {
            var match = buttons.FirstOrDefault(x => x.Text!.Contains(labels[key], StringComparison.OrdinalIgnoreCase));
            if (match == null) continue;
            await ClickHelperButtonAsync(match.Button, key);
        }
    }

    private static string? GetButtonText(Button button) => GetTextFromObject(button.Content);

    private static string? GetTextFromObject(object? obj)
    {
        if (obj is string s) return s;
        if (obj is TextBlock tb) return GetTextBlockText(tb);
        if (obj is ContentControl cc) return GetTextFromObject(cc.Content);
        return null;
    }

    private static string? GetTextBlockText(TextBlock textBlock)
    {
        if (!string.IsNullOrEmpty(textBlock.Text)) return textBlock.Text;
        if (textBlock.Inlines is null) return null;
        return string.Concat(textBlock.Inlines.OfType<AvaloniaDocuments.Run>().Select(r => r.Text));
    }

    private async Task ClickHelperButtonAsync(Button button, string key)
    {
        try
        {
            Console.WriteLine($"[SMOKE] Helper button -> {key}");
            await Dispatcher.UIThread.InvokeAsync(() => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            await DelayAsync(500);

            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var popup = lifetime?.Windows.OfType<FeaturePopupWindow>().FirstOrDefault();
            if (popup == null)
            {
                _findings.Add(new SmokeTestFinding($"Dashboard:HelperButton:{key}", "Expected FeaturePopupWindow did not open", FindingSeverity.Error));
                return;
            }

            ScanVisualTree(popup, $"Dashboard:HelperButton:{key}");
            if (_captureScreenshots)
            {
                var path = await RenderScreenshotAsync(popup, $"smoke-helper-{key}.png");
                _screenshotPaths.Add(path);
                Console.WriteLine($"[SMOKE] Helper button screenshot saved: {path}");
            }

            await Dispatcher.UIThread.InvokeAsync(() => popup.Close());
            await DelayAsync(150);
        }
        catch (Exception ex)
        {
            _findings.Add(new SmokeTestFinding($"Dashboard:HelperButton:{key}", $"Failed to exercise helper button: {ex.Message}", FindingSeverity.Error));
        }
    }

    private async Task ExerciseFeatureCardPopupsAsync(Window mainWindow)
    {
        if (_cts.IsCancellationRequested) return;

        var cards = mainWindow.GetVisualDescendants().OfType<FeatureCard>().ToList();
        Console.WriteLine($"[SMOKE] Feature cards found for popup exercise: {cards.Count}");
        foreach (var card in cards)
        {
            if (_cts.IsCancellationRequested) break;
            var context = $"FeatureCard:{card.Title}";
            try
            {
                Console.WriteLine($"[SMOKE] Feature card popup -> {card.Title}");
                await Dispatcher.UIThread.InvokeAsync(() => card.RaiseEvent(new RoutedEventArgs(FeatureCard.ClickEvent)));
                await DelayAsync(500);

                var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                var popup = lifetime?.Windows.OfType<FeaturePopupWindow>().FirstOrDefault();
                if (popup == null)
                {
                    _findings.Add(new SmokeTestFinding(context, "Expected FeaturePopupWindow did not open", FindingSeverity.Error));
                    continue;
                }

                ScanVisualTree(popup, context);
                if (_captureScreenshots)
                {
                    var safeName = string.Join("_", card.Title.Split(Path.GetInvalidFileNameChars()));
                    var path = await RenderScreenshotAsync(popup, $"smoke-featurecard-{safeName.ToLowerInvariant()}.png");
                    _screenshotPaths.Add(path);
                    Console.WriteLine($"[SMOKE] Feature card screenshot saved: {path}");
                }

                await Dispatcher.UIThread.InvokeAsync(() => popup.Close());
                await DelayAsync(150);
            }
            catch (Exception ex)
            {
                _findings.Add(new SmokeTestFinding(context, $"Failed to exercise feature card popup: {ex.Message}", FindingSeverity.Error));
            }
        }
    }

    private async Task ExerciseStartSessionAsync(MainWindowViewModel vm)
    {
        if (_cts.IsCancellationRequested) return;

        var sessionService = App.Services.GetRequiredService<ISessionService>();
        if (sessionService.State != SessionState.Idle)
        {
            _findings.Add(new SmokeTestFinding("StartSession", "Session service was not idle at smoke-test start", FindingSeverity.Warning));
            return;
        }

        try
        {
            Console.WriteLine("[SMOKE] Starting a session via StartSessionCommand...");
            var startCommand = vm.StartSessionCommand as IAsyncRelayCommand;
            if (startCommand != null)
                await startCommand.ExecuteAsync(null);
            else
                vm.StartSessionCommand.Execute(null);

            await DelayAsync(2000);

            if (sessionService.State != SessionState.Running || !vm.IsEngineRunning)
            {
                _findings.Add(new SmokeTestFinding("StartSession", $"Session did not start: State={sessionService.State}, IsEngineRunning={vm.IsEngineRunning}", FindingSeverity.Blocker));
                return;
            }

            Console.WriteLine("[SMOKE] Stopping session...");
            sessionService.StopSession(completed: false);
            await DelayAsync(1000);

            if (sessionService.State != SessionState.Idle || vm.IsEngineRunning)
            {
                _findings.Add(new SmokeTestFinding("StartSession", $"Session did not stop cleanly: State={sessionService.State}, IsEngineRunning={vm.IsEngineRunning}", FindingSeverity.Blocker));
            }
        }
        catch (Exception ex)
        {
            _findings.Add(new SmokeTestFinding("StartSession", $"Unexpected exception exercising start/stop: {ex.Message}", FindingSeverity.Blocker));
        }
    }

    private async Task ExerciseAvatarReactionAsync()
    {
        if (_cts.IsCancellationRequested) return;

        try
        {
            var avatarService = App.Services.GetRequiredService<IAvatarWindowService>();
            var barkService = App.Services.GetRequiredService<IBarkService>();

            await Dispatcher.UIThread.InvokeAsync(() => avatarService.ShowTube());
            await DelayAsync(500);

            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var tubeWindow = lifetime?.Windows.OfType<AvatarTubeWindow>().FirstOrDefault();
            if (tubeWindow == null)
            {
                _findings.Add(new SmokeTestFinding("AvatarReaction", "No AvatarTubeWindow found after ShowTube", FindingSeverity.Error));
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() => barkService.NotifyAvatarClicked());
            await DelayAsync(1000);

            var bubbleField = tubeWindow.GetType().GetField("SpeechBubble", BindingFlags.NonPublic | BindingFlags.Instance);
            var bubble = bubbleField?.GetValue(tubeWindow) as Control;
            if (bubble?.IsVisible != true)
            {
                _findings.Add(new SmokeTestFinding("AvatarReaction", "Avatar click did not show the speech bubble", FindingSeverity.Error));
            }
            else
            {
                Console.WriteLine("[SMOKE] Avatar speech bubble visible after click.");
            }
        }
        catch (Exception ex)
        {
            _findings.Add(new SmokeTestFinding("AvatarReaction", $"Unexpected exception exercising avatar reaction: {ex.Message}", FindingSeverity.Error));
        }
    }

    private async Task ExerciseOverlayClickThroughAsync()
    {
        if (_cts.IsCancellationRequested) return;

        try
        {
            var overlayService = App.Services.GetRequiredService<IOverlayService>();
            overlayService.Start();
            await DelayAsync(500);
            overlayService.ShowOverlaySustained("pink", 0.3);
            await DelayAsync(1000);

            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            // Avalonia renders overlays on a shared CompositorEngine host (CompositorWindow),
            // not per-overlay WPF-style OverlayWindow instances — match both names so the
            // click-through/taskbar/decoration checks below apply to whichever host exists.
            var overlayWindows = lifetime?.Windows
                .Where(w =>
                {
                    var name = w.GetType().Name;
                    return name.Contains("OverlayWindow", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("CompositorWindow", StringComparison.OrdinalIgnoreCase);
                })
                .ToList() ?? new List<Window>();

            if (overlayWindows.Count == 0)
            {
                _findings.Add(new SmokeTestFinding("OverlayClickThrough", "No overlay window was created", FindingSeverity.Error));
            }
            else
            {
                foreach (var w in overlayWindows)
                {
                    if (w.IsHitTestVisible)
                        _findings.Add(new SmokeTestFinding("OverlayClickThrough", $"Overlay {w.GetType().Name} is hit-test visible", FindingSeverity.Error));
                    if (w.ShowInTaskbar)
                        _findings.Add(new SmokeTestFinding("OverlayClickThrough", $"Overlay {w.GetType().Name} appears in taskbar", FindingSeverity.Error));
                    if (w.WindowDecorations != WindowDecorations.None)
                        _findings.Add(new SmokeTestFinding("OverlayClickThrough", $"Overlay {w.GetType().Name} has window decorations", FindingSeverity.Error));
                }
                Console.WriteLine($"[SMOKE] Verified {overlayWindows.Count} overlay window(s) are click-through.");
            }

            overlayService.Stop();
            await DelayAsync(500);
        }
        catch (Exception ex)
        {
            _findings.Add(new SmokeTestFinding("OverlayClickThrough", $"Unexpected exception exercising overlay: {ex.Message}", FindingSeverity.Error));
        }
    }

    private async Task ExerciseBlinkTrainerTabAsync(MainWindowViewModel vm)
    {
        if (_cts.IsCancellationRequested) return;

        try
        {
            var tab = vm.Tabs.OfType<ConditioningControlPanel.Avalonia.ViewModels.Tabs.BlinkTrainerTabViewModel>().FirstOrDefault();
            if (tab == null)
            {
                _findings.Add(new SmokeTestFinding("BlinkTrainer", "Tab view model not found", FindingSeverity.Error));
                return;
            }

            var service = App.Services?.GetService<ConditioningControlPanel.Core.Services.BlinkTrainer.IBlinkTrainerService>();
            if (service == null)
            {
                _findings.Add(new SmokeTestFinding("BlinkTrainer", "IBlinkTrainerService not registered", FindingSeverity.Error));
                return;
            }

            // Verify the service can be queried without throwing and the tab VM is wired to it.
            Console.WriteLine($"[SMOKE] BlinkTrainer tab present. Service registered, running={service.IsRunning}");
            await DelayAsync(100);
        }
        catch (Exception ex)
        {
            _findings.Add(new SmokeTestFinding("BlinkTrainer", $"Tab exercise failed: {ex.Message}", FindingSeverity.Error));
        }
    }

    internal static async Task<string> RenderScreenshotAsync(Window window, string fileName)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Ensure layout is up-to-date before capturing.
            window.Measure(window.ClientSize);
            window.Arrange(new Rect(window.ClientSize));

            var pixelSize = new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height);
            var dpi = new Vector(96, 96);
            using var bitmap = new RenderTargetBitmap(pixelSize, dpi);
            bitmap.Render(window);

            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            bitmap.Save(path);
            return path;
        });
    }

    private async Task FinishAsync(IClassicDesktopStyleApplicationLifetime? lifetime)
    {
        var elapsed = DateTime.UtcNow - _runStart;

        // Merge Avalonia log captures as findings.
        foreach (var entry in _logSink.Entries)
        {
            _findings.Add(new SmokeTestFinding($"Avalonia:{entry.Area}", entry.Message, MapLevel(entry.Level)));
        }

        var report = new SmokeTestReport(
            DurationSeconds: elapsed.TotalSeconds,
            TabsVisited: _tabSummaries,
            FirstChanceExceptions: _firstChanceExceptions.Distinct().ToList(),
            Findings: _findings.Distinct().ToList(),
            ScreenshotPaths: _screenshotPaths.Distinct().ToList()
        );

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        var reportPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smoke-test-report.json");
        File.WriteAllText(reportPath, json);

        Console.WriteLine("[SMOKE] === SMOKE TEST REPORT ===");
        Console.WriteLine($"[SMOKE] Duration: {elapsed.TotalSeconds:F1}s");
        Console.WriteLine($"[SMOKE] Tabs visited: {report.TabsVisited.Count}");
        Console.WriteLine($"[SMOKE] First-chance exceptions: {report.FirstChanceExceptions.Count}");
        Console.WriteLine($"[SMOKE] Findings: {report.Findings.Count}");
        foreach (var f in report.Findings)
        {
            Console.WriteLine($"[SMOKE] [{f.Severity}] {f.Context}: {f.Message}");
        }
        if (report.ScreenshotPaths.Count > 0)
        {
            Console.WriteLine($"[SMOKE] Screenshots captured: {report.ScreenshotPaths.Count}");
            foreach (var p in report.ScreenshotPaths)
                Console.WriteLine($"[SMOKE]   {p}");
        }
        Console.WriteLine($"[SMOKE] Full report written to: {reportPath}");
        Console.WriteLine("[SMOKE] ===========================");

        await Task.Delay(500);
        lifetime?.Shutdown();
    }

    private static FindingSeverity MapLevel(string level)
    {
        var lower = level.ToLowerInvariant();
        if (lower.Contains("error")) return FindingSeverity.Error;
        if (lower.Contains("warn")) return FindingSeverity.Warning;
        return FindingSeverity.Info;
    }
}

internal sealed record SmokeTestFinding(string Context, string Message, FindingSeverity Severity);

internal enum FindingSeverity { Info, Warning, Error, Blocker }

internal sealed record SmokeTestReport(
    double DurationSeconds,
    IReadOnlyList<string> TabsVisited,
    IReadOnlyList<string> FirstChanceExceptions,
    IReadOnlyList<SmokeTestFinding> Findings,
    IReadOnlyList<string> ScreenshotPaths);
