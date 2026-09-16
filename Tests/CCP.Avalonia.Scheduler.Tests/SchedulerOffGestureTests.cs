using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Studio;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CCP.Avalonia.Scheduler.Tests;

internal static class SchedulerTestProfile
{
    internal static readonly string? PreviousProfile = Environment.GetEnvironmentVariable("CCP_USERDATA_DIR");
    internal static readonly string DirectoryPath = Path.Combine(
        Path.GetTempPath(), "ccp-scheduler-gesture-" + Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Initialize()
    {
        Directory.CreateDirectory(DirectoryPath);
        Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", DirectoryPath);
    }
}

public sealed class SchedulerOffGestureTests
{
    [Fact]
    public async Task SchedulerRightClickOnlyTurnsOffAndDisabledClickSelectsPanel()
    {
        var previousProfile = SchedulerTestProfile.PreviousProfile;
        var previousProvider = CoreSettings.ServiceProvider;
        var previousCloudBackup = CoreSettingsHooks.CloudBackup;
        var previousSettingSink = CoreSettingsHooks.SettingChangedSink;
        var localization = LocalizationManager.Instance;
        var previousLanguage = localization.CurrentLanguage;
        var profile = SchedulerTestProfile.DirectoryPath;
        SettingsService? service = null;

        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                EnsureAvalonia();
                Assert.True(IsTemporaryPath(CorePaths.UserData),
                    "scheduler gesture test would use a non-temporary settings profile");

                var activeService = service = new SettingsService();
                CoreSettings.ServiceProvider = () => activeService;
                CoreSettingsHooks.CloudBackup = null;
                CoreSettingsHooks.SettingChangedSink = null;
                activeService.Current.SchedulerEnabled = false;
                activeService.SaveImmediate();
                var settingsPath = Path.Combine(CorePaths.UserData, "settings.json");

                Window? host = null;
                try
                {
                    var view = new StudioTabView { Width = 1280, Height = 900 };
                    host = new Window
                    {
                        Width = 1280,
                        Height = 900,
                        Content = view,
                    };
                    host.Show();
                    Dispatcher.UIThread.RunJobs();

                    var rack = view.FindControl<StackPanel>("RackList");
                    Assert.NotNull(rack);
                    var schedulerRow = rack!.Children.OfType<RadioButton>()
                        .SingleOrDefault(row =>
                            string.Equals(row.Tag as string, "scheduler", StringComparison.Ordinal));
                    Assert.NotNull(schedulerRow);
                    var schedulerHost = view.FindControl<ScrollViewer>("HostScheduler");
                    var schedulerPanel = view.FindControl<SchedulerRackPanel>("PanelScheduler");
                    Assert.NotNull(schedulerHost);
                    Assert.NotNull(schedulerPanel);
                    var schedulerControl = schedulerPanel!.FindControl<SchedulerFeatureControl>("SchedulerHost");
                    Assert.NotNull(schedulerControl);
                    var enabled = schedulerControl!.FindControl<CheckBox>("ChkEnabled");
                    Assert.NotNull(enabled);
                    Assert.False(activeService.Current.SchedulerEnabled);
                    Assert.False(enabled!.IsChecked == true);

                    // Negative proof: the mounted row receives a real right-button press/release;
                    // disabled Scheduler stays off and the detail panel is selected instead.
                    RightClick(host, schedulerRow!);
                    Assert.False(activeService.Current.SchedulerEnabled);
                    Assert.False(enabled.IsChecked == true);
                    Assert.True(schedulerRow.IsChecked == true,
                        "disabled Scheduler gesture did not select its rack row");
                    Assert.True(schedulerHost!.IsVisible);

                    // Enabling remains the panel checkbox's real pointer/event path, including its save.
                    LeftClick(host, enabled);
                    await WaitForSave();
                    Assert.True(activeService.Current.SchedulerEnabled);
                    Assert.Contains("\"SchedulerEnabled\": true",
                        File.ReadAllText(settingsPath), StringComparison.Ordinal);

                    // Raise the mounted control's routed change event without changing its value.
                    // This proves the equality guard rather than merely setting a private member.
                    var unchangedBytes = File.ReadAllBytes(settingsPath);
                    var unchangedWriteTime = File.GetLastWriteTimeUtc(settingsPath);
                    var eventSeen = false;
                    enabled.IsCheckedChanged += (_, _) => eventSeen = true;
                    enabled.RaiseEvent(new RoutedEventArgs(ToggleButton.IsCheckedChangedEvent, enabled));
                    Dispatcher.UIThread.RunJobs();
                    await WaitForSave();
                    Assert.True(eventSeen, "the unchanged checkbox event was not raised");
                    Assert.Equal(unchangedBytes, File.ReadAllBytes(settingsPath));
                    Assert.Equal(unchangedWriteTime, File.GetLastWriteTimeUtc(settingsPath));

                    // Positive proof: once on, the same mounted right-click turns Scheduler off.
                    RightClick(host, schedulerRow);
                    await WaitForSave();
                    Assert.False(activeService.Current.SchedulerEnabled);
                    Assert.False(enabled.IsChecked == true);
                    Assert.True(schedulerRow.IsChecked == true, "Scheduler row lost selection after turning it off");
                    Assert.True(schedulerHost.IsVisible);
                    Assert.Contains("\"SchedulerEnabled\": false",
                        File.ReadAllText(settingsPath), StringComparison.Ordinal);
                }
                finally
                {
                    host?.Close();
                    Dispatcher.UIThread.RunJobs();
                }
            });
        }
        finally
        {
            try { service?.SaveImmediate(suppressCloudBackup: true); } catch { }
            CoreSettings.ServiceProvider = previousProvider;
            CoreSettingsHooks.CloudBackup = previousCloudBackup;
            CoreSettingsHooks.SettingChangedSink = previousSettingSink;
            Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", previousProfile);
            try
            {
                if (localization.CurrentLanguage != previousLanguage)
                    localization.SetLanguage(previousLanguage);
            }
            catch { }
            try { Directory.Delete(profile, recursive: true); } catch { }
        }
    }

    private static void EnsureAvalonia()
    {
        Assert.True(AvaloniaTestDispatcher.IsDispatcherThread);
        if (Application.Current is null)
        {
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        }
        LocalizationManager.Instance.SetLanguage("en");
    }

    private static async Task WaitForSave()
    {
        await Task.Delay(650);
        Dispatcher.UIThread.RunJobs();
    }

    private static void LeftClick(Window host, Control target)
    {
        var point = PointInHost(host, target);
        host.MouseMove(point, RawInputModifiers.None);
        host.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        host.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static void RightClick(Window host, Control target)
    {
        var point = PointInHost(host, target);
        host.MouseMove(point, RawInputModifiers.None);
        host.MouseDown(point, MouseButton.Right, RawInputModifiers.None);
        host.MouseUp(point, MouseButton.Right, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static Point PointInHost(Window host, Control target)
    {
        Assert.True(target.Bounds.Width > 0 && target.Bounds.Height > 0,
            $"{target.Tag ?? target.GetType().Name} was not laid out");
        var point = target.TranslatePoint(
            new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), host);
        Assert.True(point.HasValue, "could not translate mounted control into host");
        return point!.Value;
    }

    private static bool IsTemporaryPath(string path)
    {
        var root = Path.GetFullPath(Path.GetTempPath())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
