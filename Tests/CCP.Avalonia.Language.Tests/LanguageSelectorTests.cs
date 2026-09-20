using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CCP.Avalonia.Language.Tests;

internal static class TestProfile
{
    internal static string DirectoryPath { get; } = Path.Combine(
        Path.GetTempPath(), "ccp-language-tests-" + Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Initialize()
    {
        Directory.CreateDirectory(DirectoryPath);
        Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", DirectoryPath);

        // [lang-probe] Unconditional record of the ACTUAL testhost runtime and the profile this
        // process owns, written on every start so a pass (or a failure elsewhere) still has it.
        try
        {
            var recordDir = Environment.GetEnvironmentVariable("CCP_PROBE_LOG_DIR");
            if (!string.IsNullOrEmpty(recordDir))
            {
                Directory.CreateDirectory(recordDir);
                File.AppendAllText(Path.Combine(recordDir, "testhost-runtime.log"),
                    $"{DateTime.UtcNow:O} pid={Environment.ProcessId}"
                    + $" runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}"
                    + $" os={System.Runtime.InteropServices.RuntimeInformation.OSDescription}"
                    + $" arch={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}"
                    + $" owned-profile={DirectoryPath}" + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[lang-probe] runtime record unavailable: {ex.GetType().Name}: {ex.Message}");
        }

        // [lang-probe] Diagnostics only: route the production Serilog statics into an in-memory
        // ring buffer so a persistence timeout can say WHICH of the three things happened —
        // the debounce callback never ran, SaveImmediate threw (sharing violation / HResult),
        // or the write succeeded with the wrong model value. Changes no production behaviour.
        Serilog.Log.Logger = new Serilog.LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(SaveProbeSink.Instance)
            .CreateLogger();
    }
}

/// <summary>[lang-probe] Bounded capture of the settings-save log lines the product already emits.</summary>
internal sealed class SaveProbeSink : Serilog.Core.ILogEventSink
{
    internal static SaveProbeSink Instance { get; } = new();

    private const string MarkPrefix = "---- mutation window: ";

    private readonly System.Collections.Generic.Queue<string> _lines = new();

    /// <summary>[lang-probe] Opens a mutation window so later lines can be attributed to it.</summary>
    internal void Mark(string label)
    {
        lock (_lines)
        {
            _lines.Enqueue($"{MarkPrefix}{label} ({DateTime.Now:HH:mm:ss.fff}) ----");
            while (_lines.Count > 200) _lines.Dequeue();
        }
    }

    public void Emit(Serilog.Events.LogEvent logEvent)
    {
        var message = logEvent.RenderMessage();
        if (message.IndexOf("settings", StringComparison.OrdinalIgnoreCase) < 0) return;
        // The polling reload logs a load per 25ms tick and would push the save lines out of the ring.
        if (message.StartsWith("Settings loaded from", StringComparison.Ordinal)) return;

        var text = $"{logEvent.Timestamp:HH:mm:ss.fff} {logEvent.Level} {message}";
        if (logEvent.Exception is { } ex)
            text += $" || {ex.GetType().Name}: {ex.Message} (HResult=0x{ex.HResult:X8})";

        lock (_lines)
        {
            _lines.Enqueue(text);
            while (_lines.Count > 200) _lines.Dequeue();
        }
    }

    /// <summary>[lang-probe] Only the lines emitted after the most recent mutation marker.</summary>
    internal string[] SinceLastMark()
    {
        lock (_lines)
        {
            var all = _lines.ToArray();
            var mark = Array.FindLastIndex(all, line => line.StartsWith(MarkPrefix, StringComparison.Ordinal));
            return all.Skip(mark + 1).ToArray();
        }
    }
}

/// <summary>Headless proof for the saved language and the two live selector surfaces.</summary>
public sealed class LanguageSelectorTests
{
    private sealed class CatalogueTestApp : App
    {
        private readonly string _customFolder;
        private readonly string _builtInFolder;

        internal CatalogueTestApp(string customFolder, string builtInFolder)
        {
            _customFolder = customFolder;
            _builtInFolder = builtInFolder;
        }

        protected override SessionManager CreateSessionManager() =>
            new(new SessionFileService(_customFolder, _builtInFolder));
    }

    [Fact]
    public void LanguageSelectorsRestoreAndDesktopExitFlushesPendingState()
    {
        var previousLanguage = LocalizationManager.Instance.CurrentLanguage;
        var settingsPath = Path.Combine(TestProfile.DirectoryPath, "settings.json");
        var settingsBeforeStartup = SeedProfile(settingsPath);
        var settingsWriteBeforeStartup = File.GetLastWriteTimeUtc(settingsPath);
        var catalogue = CreateSessionCatalogue();
        try
        {
            AvaloniaTestDispatcher.Run(() =>
            {
                var (shell, lifetime) = StartApp(catalogue.CustomFolder, catalogue.BuiltInFolder);

            try
        {
            Assert.Equal("ja", LocalizationManager.Instance.CurrentLanguage);
            Assert.Equal("ja", SelectedCode(Pill(shell)));
            Assert.Equal("ja", SelectedCode(General(shell)));

            var presets = Assert.IsType<PresetsTabView>(shell.FindControl<PresetsTabView>("PresetsTab"));
            var rows = presets.FindControl<StackPanel>("SessionRackPanel")!.Children.OfType<Border>().ToArray();
            Assert.Equal(2, rows.Length);
            var builtIn = Assert.Single(rows, row => (row.Tag as Session)?.Id == "language_builtin");
            var builtInSession = Assert.IsType<Session>(builtIn.Tag);
            Assert.Equal(SessionSource.BuiltIn, builtInSession.Source);
            Assert.Equal(Path.Combine(catalogue.BuiltInFolder, "language_builtin.session.json"),
                builtInSession.SourceFilePath);
            var customRow = Assert.Single(rows, row => (row.Tag as Session)?.Id == "language_custom");
            var custom = Assert.IsType<Session>(customRow.Tag);
            Assert.Equal(SessionSource.Custom, custom.Source);
            Assert.Equal(Path.Combine(catalogue.CustomFolder, "language_custom.session.json"),
                custom.SourceFilePath);
            Assert.Equal("Language Custom", custom.Name);
            Assert.True(custom.IsAvailable);
            Assert.DoesNotContain(rows, row => (row.Tag as Session)?.Id == "language_unavailable");

            // Navigate through the real shell door before exercising the row's keyboard path.
            shell.FindControl<Button>("BtnPresets")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            customRow.Focus();
            shell.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("🧭 Language Custom", presets.FindControl<TextBlock>("TxtDetailTitle")!.Text);
            Assert.Equal("Raw language description",
                presets.FindControl<TextBlock>("TxtSessionDescription")!.Text);
            Assert.Equal(Loc.GetF("rack_duration", 23),
                presets.FindControl<TextBlock>("TxtSessionDuration")!.Text);
            Assert.Equal(custom.GetDifficultyText(),
                presets.FindControl<TextBlock>("TxtSessionDifficulty")!.Text);

            WaitForDebouncedSave(); // A startup save must not hide behind the first user edit.
            Assert.Equal(settingsBeforeStartup, File.ReadAllText(settingsPath));
            Assert.Equal(settingsWriteBeforeStartup, File.GetLastWriteTimeUtc(settingsPath));

            // This is the real desktop/provider path: the mounted chip writes the actual settings
            // file, and a newly constructed view restores the validated token without another app
            // lifetime or a second profile.
            var sourceChips = presets.FindControl<StackPanel>("RackSourceChips")!
                .Children.OfType<ToggleButton>().ToArray();
            var yours = sourceChips.Single(chip => (string)chip.Tag! == "yours");
            Click(shell, yours);
            Assert.Equal("yours", CoreSettings.Current.SessionRackSourceFilter);
            WaitForPersistedSetting(settingsPath, "SessionRackSourceFilter", "yours");
            Assert.Equal("yours", new SettingsService().Current.SessionRackSourceFilter);

            var sort = presets.FindControl<ComboBox>("CmbRackSort")!;
            sort.SelectedItem = sort.Items.OfType<ComboBoxItem>()
                .Single(item => (item.Tag as string) == "xp");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("xp", RackSortTag(sort));
            WaitForPersistedSetting(settingsPath, "SessionRackSort", "xp");
            Assert.Equal("xp", new SettingsService().Current.SessionRackSort);

            // Search and difficulty are deliberately transient. Type through the mounted control,
            // then hide the selected medium row before constructing a fresh view.
            var search = presets.FindControl<TextBox>("TxtRackSearch")!;
            search.Focus();
            shell.KeyTextInput("language");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("language", search.Text);
            var medium = presets.FindControl<StackPanel>("RackDifficultyChips")!
                .Children.OfType<ToggleButton>()
                .Single(dot => (SessionDifficulty)dot.Tag! == SessionDifficulty.Medium);
            Click(shell, medium);
            Assert.False(medium.IsChecked == true);

            var restoredView = new PresetsTabView();
            var restoredSources = restoredView.FindControl<StackPanel>("RackSourceChips")!
                .Children.OfType<ToggleButton>().ToArray();
            Assert.Single(restoredSources, chip => (string)chip.Tag! == "yours" && chip.IsChecked == true);
            Assert.All(restoredSources.Where(chip => (string)chip.Tag! != "yours"),
                chip => Assert.False(chip.IsChecked == true));
            Assert.Equal("xp", RackSortTag(restoredView.FindControl<ComboBox>("CmbRackSort")!));
            Assert.True(string.IsNullOrEmpty(restoredView.FindControl<TextBox>("TxtRackSearch")!.Text));
            Assert.All(restoredView.FindControl<StackPanel>("RackDifficultyChips")!
                .Children.OfType<ToggleButton>(), dot => Assert.True(dot.IsChecked == true));

            // Leave the profile in its original state for the remainder of this lifecycle test.
            sort.SelectedItem = sort.Items.OfType<ComboBoxItem>()
                .Single(item => (item.Tag as string) == "recent");
            Dispatcher.UIThread.RunJobs();
            WaitForPersistedSetting(settingsPath, "SessionRackSort", "recent");
            var all = sourceChips.Single(chip => (string)chip.Tag! == "all");
            Click(shell, all);
            Assert.Equal("all", CoreSettings.Current.SessionRackSourceFilter);
            Assert.True(all.IsChecked == true, "the All source chip did not stay selected after the click");
            Assert.All(sourceChips.Where(chip => !ReferenceEquals(chip, all)),
                chip => Assert.False(chip.IsChecked == true));
            WaitForPersistedSetting(settingsPath, "SessionRackSourceFilter", "all");
            Assert.Equal("all", new SettingsService().Current.SessionRackSourceFilter);

            Select(Pill(shell), "fr");

            Assert.Equal("fr", CoreSettings.Current.Language);
            Assert.Equal("fr", LocalizationManager.Instance.CurrentLanguage);
            Assert.Equal("fr", SelectedCode(General(shell)));
            Assert.Equal("🧭 Language Custom", presets.FindControl<TextBlock>("TxtDetailTitle")!.Text);
            Assert.Equal("Raw language description",
                presets.FindControl<TextBlock>("TxtSessionDescription")!.Text);
            Assert.Equal(custom.GetDifficultyText(),
                presets.FindControl<TextBlock>("TxtSessionDifficulty")!.Text);
            Assert.Equal(Loc.Get("msg_restart_to_apply"),
                shell.FindControl<TextBlock>("TxtBannerSecondary")?.Text);
            Assert.True(shell.FindControl<TextBlock>("TxtBannerSecondary")?.Opacity > 0);
            WaitForPersistedSetting(settingsPath, "Language", "fr");
            Assert.Equal("fr", new SettingsService().Current.Language);

            Select(General(shell), "de");

            Assert.Equal("de", CoreSettings.Current.Language);
            Assert.Equal("de", LocalizationManager.Instance.CurrentLanguage);
            Assert.Equal("de", SelectedCode(Pill(shell)));
            WaitForPersistedSetting(settingsPath, "Language", "de");
            Assert.Equal("de", new SettingsService().Current.Language);
            Assert.False(RoadmapExists());
            DisposeRoadmapIfCreated();
            Assert.False(RoadmapExists());

            var roadmap = ExistingRoadmap();
            roadmap.StartStep("t1_step1");
            Assert.NotNull(roadmap.GetStepProgress("t1_step1")?.StartedAt);

            var uiThread = Environment.CurrentManagedThreadId;
            var postedThread = 0;
            using var posted = new ManualResetEventSlim();
            var postThread = new Thread(() => CoreDispatch.Post(() =>
            {
                postedThread = Environment.CurrentManagedThreadId;
                posted.Set();
            })) { IsBackground = true };
            postThread.Start();
            Assert.True(postThread.Join(TimeSpan.FromSeconds(5)));
            Dispatcher.UIThread.RunJobs();
            Assert.True(posted.IsSet);
            Assert.Equal(uiThread, postedThread);

            var inlineThread = 0;
            CoreDispatch.Post(() => inlineThread = Environment.CurrentManagedThreadId);
            Assert.Equal(uiThread, inlineThread);

            // Leave the UI dispatcher unpumped. The worker must time out on its own; only then do
            // we pump the queue and prove the canceled callback does not execute late.
            var lateCallback = 0;
            (bool Completed, int? Result) invocation = default;
            var invokeThread = new Thread(() => invocation = CoreDispatch.Invoke(() =>
            {
                Interlocked.Exchange(ref lateCallback, 1);
                return 42;
            }, TimeSpan.FromMilliseconds(50))) { IsBackground = true };
            invokeThread.Start();
            Assert.True(invokeThread.Join(TimeSpan.FromSeconds(5)));
            Assert.False(invocation.Completed);
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, Volatile.Read(ref lateCallback));

            // This callback really starts, outlives the bounded Invoke wait, and faults only after
            // the invoking worker has returned. The continuation in the provider observes that
            // late fault; the entry/fault gates make this an executable proof of the timing.
            using var callbackEntered = new ManualResetEventSlim();
            using var invokeReturned = new ManualResetEventSlim();
            using var releaseFault = new ManualResetEventSlim();
            var callbackExecuted = 0;
            var callbackFaulted = 0;
            var releasedAfterReturn = false;
            var coordinatorSawEntry = false;
            var coordinatorSawReturn = false;
            (bool Completed, int? Result) faultInvocation = default;
            var faultThread = new Thread(() =>
            {
                faultInvocation = CoreDispatch.Invoke<int>(() =>
                {
                    Interlocked.Exchange(ref callbackExecuted, 1);
                    callbackEntered.Set();
                    releasedAfterReturn = releaseFault.Wait(TimeSpan.FromSeconds(10))
                        && invokeReturned.IsSet;
                    Interlocked.Exchange(ref callbackFaulted, 1);
                    throw new InvalidOperationException("dispatcher probe");
                }, TimeSpan.FromSeconds(2));
                invokeReturned.Set();
            }) { IsBackground = true };
            var releaseFaultThread = new Thread(() =>
            {
                coordinatorSawEntry = callbackEntered.Wait(TimeSpan.FromSeconds(10));
                coordinatorSawReturn = invokeReturned.Wait(TimeSpan.FromSeconds(10));
                releaseFault.Set();
            }) { IsBackground = true };
            faultThread.Start();
            releaseFaultThread.Start();
            Assert.True(SpinWait.SpinUntil(() =>
            {
                Dispatcher.UIThread.RunJobs();
                return callbackEntered.IsSet;
            }, TimeSpan.FromSeconds(5)));
            Assert.True(faultThread.Join(TimeSpan.FromSeconds(5)));
            Assert.True(releaseFaultThread.Join(TimeSpan.FromSeconds(5)));
            Assert.True(callbackEntered.IsSet);
            Assert.True(invokeReturned.IsSet);
            Assert.True(coordinatorSawEntry, "Coordinator did not observe callback entry");
            Assert.True(coordinatorSawReturn, "Coordinator did not observe Invoke returning");
            Assert.True(releasedAfterReturn, "Callback faulted before release after Invoke returned");
            Assert.Equal(1, Volatile.Read(ref callbackExecuted));
            Assert.Equal(1, Volatile.Read(ref callbackFaulted));
            Assert.False(faultInvocation.Completed);

            Assert.NotNull(App.Settings);
            var settingsService = App.Settings!;
            Assert.Equal(500, settingsService.SaveDebounceDueTimeMilliseconds);
            var originalSaveDebounceDueTimeMilliseconds = settingsService.SaveDebounceDueTimeMilliseconds;
            try
            {
                // Queue work from a background caller, then perform the final mutation immediately
                // before actual application exit. Hold the real timer past its normal due time so
                // the file must still contain the old value until the Exit handler's SaveImmediate.
                var queuedBeforeExit = 0;
                var queuedPostThread = new Thread(() => CoreDispatch.Post(() =>
                    Interlocked.Exchange(ref queuedBeforeExit, 1))) { IsBackground = true };
                queuedPostThread.Start();
                Assert.True(queuedPostThread.Join(TimeSpan.FromSeconds(5)));
                var diskBeforeExit = File.ReadAllText(settingsPath);
                CoreSettings.Current.Language = "fr";
                CoreSettings.Current.SuppressPerkNotifications = true;
                settingsService.SaveDebounceDueTimeMilliseconds = Timeout.Infinite;
                CoreSettings.Save();
                Assert.Equal(diskBeforeExit, File.ReadAllText(settingsPath));
                Thread.Sleep(650); // Beyond the production 500ms due time, without pumping the UI queue.
                Assert.Equal(diskBeforeExit, File.ReadAllText(settingsPath));
                lifetime.Shutdown();
                var reloaded = new SettingsService();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0, Volatile.Read(ref queuedBeforeExit));

                Assert.Equal("fr", reloaded.Current.Language);
                Assert.True(reloaded.Current.SuppressPerkNotifications);
                using var roadmapReload = new RoadmapService();
                Assert.NotNull(roadmapReload.GetStepProgress("t1_step1")?.StartedAt);

                var afterExit = 0;
                CoreDispatch.Post(() => Interlocked.Exchange(ref afterExit, 1));
                var afterExitInvoke = CoreDispatch.Invoke(() => 7, TimeSpan.FromMilliseconds(50));
                Assert.False(afterExitInvoke.Completed);
                Assert.Equal(0, Volatile.Read(ref afterExit));
            }
            finally
            {
                settingsService.SaveDebounceDueTimeMilliseconds = originalSaveDebounceDueTimeMilliseconds;
            }
        }
        finally
        {
            try { shell.Close(); } catch { }
            try { Dispatcher.UIThread.RunJobs(); } catch { }
            LocalizationManager.Instance.SetLanguage(previousLanguage);
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = null;
            CoreDispatch.PostProvider = null;
                CoreDispatch.InvokeProvider = null;
            }
            });
        }
        finally
        {
            if (Directory.Exists(catalogue.Root))
                Directory.Delete(catalogue.Root, recursive: true);
        }
    }

    private static ComboBox Pill(MainShellWindow shell) =>
        shell.FindControl<ComboBox>("CmbLanguagePill")!;

    private static ComboBox General(MainShellWindow shell) =>
        shell.FindControl<AppSettingsTabView>("AppSettingsTab")!
            .FindControl<GeneralSettingsSection>("SectionGeneral")!
            .FindControl<ComboBox>("CmbLanguageSetting")!;

    private static string? SelectedCode(ComboBox combo) =>
        (combo.SelectedItem as ComboBoxItem)?.Tag as string;

    private static string? RackSortTag(ComboBox combo) =>
        (combo.SelectedItem as ComboBoxItem)?.Tag as string;

    private static void Select(ComboBox combo, string code)
    {
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>()
            .Single(item => (item.Tag as string) == code);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(TopLevel host, Control target)
    {
        var point = target.TranslatePoint(
            new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), host);
        Assert.True(point.HasValue, "could not translate control into host");
        host.MouseMove(point!.Value, RawInputModifiers.None);
        host.MouseDown(point.Value, MouseButton.Left, RawInputModifiers.None);
        host.MouseUp(point.Value, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static void WaitForDebouncedSave()
    {
        Thread.Sleep(650);
        Dispatcher.UIThread.RunJobs();
    }

    private static void WaitForPersistedSetting(string settingsPath, string property, string expected)
    {
        SaveProbeSink.Instance.Mark($"wait {property}={expected}");
        var stopwatch = Stopwatch.StartNew();
        var actual = ReadSetting(settingsPath, property);
        while (!string.Equals(actual, expected, StringComparison.Ordinal)
            && stopwatch.Elapsed < TimeSpan.FromSeconds(5))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(25);
            actual = ReadSetting(settingsPath, property);
        }

        var persisted = string.Equals(actual, expected, StringComparison.Ordinal);
        Assert.True(persisted,
            $"Timed out waiting for {property}={expected}; last disk value was {actual} after {stopwatch.Elapsed.TotalMilliseconds:0}ms"
                // Evaluated only when the wait already failed: a pass adds no read and no service.
                + (persisted ? string.Empty : ProbeDiagnostics(settingsPath, property)));
    }

    /// <summary>[lang-probe] Diagnostics-only detail appended to an existing failure message.</summary>
    private static string ProbeDiagnostics(string settingsPath, string property)
    {
        try
        {
            var inMemory = CoreSettings.Current.GetType().GetProperty(property)?
                .GetValue(CoreSettings.Current)?.ToString() ?? "<no-such-property>";
            // No extra SettingsService here: the polling loop's own disk read is the disk evidence.
            var onDisk = ReadSetting(settingsPath, property);
            var leftoverTemps = Directory.Exists(TestProfile.DirectoryPath)
                ? Directory.GetFiles(TestProfile.DirectoryPath, "settings.json.*.tmp").Length
                : -1;
            var log = SaveProbeSink.Instance.SinceLastMark();
            return "\n[lang-probe] os=" + System.Runtime.InteropServices.RuntimeInformation.OSDescription
                + "; runtime=" + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
                + "; profile=" + TestProfile.DirectoryPath
                + "\n[lang-probe] in-memory " + property + "=" + inMemory
                + "; last disk read " + property + "=" + onDisk
                + "; leftover settings temp files=" + leftoverTemps
                + "\n[lang-probe] settings log lines inside this mutation window (" + log.Length + "):\n  "
                + (log.Length == 0 ? "<none observed>" : string.Join("\n  ", log))
                + "\n[lang-probe] reading these lines: no save entry means SAVE ENTRY NOT OBSERVED in this"
                + " window — NOT proof the debounce callback never executed (the callback can run without"
                + " saving, or stall before its first log); an entry line can come from an immediate save"
                + " as well as a debounce callback; an exception HResult identifies an error CLASS only and"
                + " does not prove File.Move failed because of this test's polling reader; and a successful"
                + " save line with stale disk contents does not uniquely identify a wrong snapshot, since a"
                + " later write could have intervened.";
        }
        catch (Exception ex)
        {
            return $"\n[lang-probe] diagnostics unavailable: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static string ReadSetting(string settingsPath, string property)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (!document.RootElement.TryGetProperty(property, out var value)) return "<missing>";
            return value.ValueKind == System.Text.Json.JsonValueKind.String
                ? value.GetString() ?? "<null>"
                : value.ToString();
        }
        catch (Exception ex)
        {
            return $"<error:{ex.GetType().Name}>";
        }
    }

    private static string SeedProfile(string settingsPath)
    {
        var settings = new SettingsService();
        settings.Current.Language = "ja";
        settings.Current.SessionRackSourceFilter = "all";
        settings.Current.Welcomed = true;
        settings.SaveImmediate();
        return File.ReadAllText(settingsPath);
    }

    private static (string Root, string CustomFolder, string BuiltInFolder) CreateSessionCatalogue()
    {
        var root = Path.Combine(TestProfile.DirectoryPath, "session-catalogue-" + Guid.NewGuid().ToString("N"));
        var customFolder = Path.Combine(root, "custom");
        var builtInFolder = Path.Combine(root, "built-in");
        Directory.CreateDirectory(customFolder);
        Directory.CreateDirectory(builtInFolder);
        var service = new SessionFileService(customFolder, builtInFolder);
        service.ExportSession(new SessionDefinition
        {
            Id = "language_builtin",
            Name = "Language Built In",
            Icon = "🧪",
            Description = "Built-in language fixture",
            DurationMinutes = 11,
            IsAvailable = true
        }, Path.Combine(builtInFolder, "language_builtin.session.json"));
        service.ExportSession(new SessionDefinition
        {
            Id = "language_custom",
            Name = "Language Custom",
            Icon = "🧭",
            Description = "Raw language description",
            DurationMinutes = 23,
            Difficulty = SessionDifficulty.Medium,
            BonusXP = 123,
            IsAvailable = true
        }, Path.Combine(customFolder, "language_custom.session.json"));
        service.ExportSession(new SessionDefinition
        {
            Id = "language_unavailable",
            Name = "Unavailable Language Fixture",
            IsAvailable = false
        }, Path.Combine(customFolder, "language_unavailable.session.json"));
        return (root, customFolder, builtInFolder);
    }

    private static (MainShellWindow Shell, ClassicDesktopStyleApplicationLifetime Lifetime) StartApp(
        string customFolder, string builtInFolder)
    {
        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };

        AppBuilder.Configure<CatalogueTestApp>(() => new CatalogueTestApp(customFolder, builtInFolder))
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithLifetime(lifetime);
        var app = Assert.IsType<CatalogueTestApp>(Application.Current);
        Assert.True(app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime,
            $"lifetime={app.ApplicationLifetime?.GetType().FullName}");
        Assert.NotNull(App.Settings);
        Assert.True(AvaloniaTestDispatcher.IsDispatcherThread, "Avalonia setup did not stay on the test dispatcher");
        if (OperatingSystem.IsWindows())
            Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());

        var shell = Assert.IsType<MainShellWindow>(lifetime.MainWindow);
        shell.Show();
        Dispatcher.UIThread.RunJobs();
        return (shell, lifetime);
    }

    private static bool RoadmapExists() =>
        typeof(MainShellWindow)
            .GetField("_roadmap", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null) is not null;

    private static void DisposeRoadmapIfCreated() =>
        typeof(MainShellWindow)
            .GetMethod("DisposeRoadmapIfCreated", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, null);

    private static RoadmapService ExistingRoadmap() =>
        (RoadmapService)typeof(MainShellWindow)
            .GetProperty("Roadmap", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;
}
