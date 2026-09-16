using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
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
    }
}

/// <summary>Headless proof for the saved language and the two live selector surfaces.</summary>
public sealed class LanguageSelectorTests
{
    [Fact]
    public void LanguageSelectorsRestoreAndDesktopExitFlushesPendingState()
    {
        var settingsPath = Path.Combine(TestProfile.DirectoryPath, "settings.json");
        var settingsBeforeStartup = SeedProfile(settingsPath);
        var settingsWriteBeforeStartup = File.GetLastWriteTimeUtc(settingsPath);
        AvaloniaTestDispatcher.Run(() =>
        {
            var (shell, lifetime) = StartApp();

            try
        {
            Assert.Equal("ja", LocalizationManager.Instance.CurrentLanguage);
            Assert.Equal("ja", SelectedCode(Pill(shell)));
            Assert.Equal("ja", SelectedCode(General(shell)));
            WaitForDebouncedSave(); // A startup save must not hide behind the first user edit.
            Assert.Equal(settingsBeforeStartup, File.ReadAllText(settingsPath));
            Assert.Equal(settingsWriteBeforeStartup, File.GetLastWriteTimeUtc(settingsPath));

            Select(Pill(shell), "fr");

            Assert.Equal("fr", CoreSettings.Current.Language);
            Assert.Equal("fr", LocalizationManager.Instance.CurrentLanguage);
            Assert.Equal("fr", SelectedCode(General(shell)));
            Assert.Equal(Loc.Get("msg_restart_to_apply"),
                shell.FindControl<TextBlock>("TxtBannerSecondary")?.Text);
            Assert.True(shell.FindControl<TextBlock>("TxtBannerSecondary")?.Opacity > 0);
            WaitForDebouncedSave();
            Assert.Equal("fr", new SettingsService().Current.Language);

            Select(General(shell), "de");

            Assert.Equal("de", CoreSettings.Current.Language);
            Assert.Equal("de", LocalizationManager.Instance.CurrentLanguage);
            Assert.Equal("de", SelectedCode(Pill(shell)));
            WaitForDebouncedSave();
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
            CoreSettings.ServiceProvider = null;
            CoreDispatch.PostProvider = null;
                CoreDispatch.InvokeProvider = null;
            }
        });
    }

    private static ComboBox Pill(MainShellWindow shell) =>
        shell.FindControl<ComboBox>("CmbLanguagePill")!;

    private static ComboBox General(MainShellWindow shell) =>
        shell.FindControl<AppSettingsTabView>("AppSettingsTab")!
            .FindControl<GeneralSettingsSection>("SectionGeneral")!
            .FindControl<ComboBox>("CmbLanguageSetting")!;

    private static string? SelectedCode(ComboBox combo) =>
        (combo.SelectedItem as ComboBoxItem)?.Tag as string;

    private static void Select(ComboBox combo, string code)
    {
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>()
            .Single(item => (item.Tag as string) == code);
        Dispatcher.UIThread.RunJobs();
    }

    private static void WaitForDebouncedSave()
    {
        System.Threading.Thread.Sleep(650);
        Dispatcher.UIThread.RunJobs();
    }

    private static string SeedProfile(string settingsPath)
    {
        var settings = new SettingsService();
        settings.Current.Language = "ja";
        settings.Current.Welcomed = true;
        settings.SaveImmediate();
        return File.ReadAllText(settingsPath);
    }

    private static (MainShellWindow Shell, ClassicDesktopStyleApplicationLifetime Lifetime) StartApp()
    {
        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };

        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithLifetime(lifetime);
        var app = Assert.IsType<App>(Application.Current);
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
