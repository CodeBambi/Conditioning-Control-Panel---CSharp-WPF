using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Settings ▸ Performance ▸ do-not-disturb "pick app" (WPF PerformanceSettingsSection
/// :172 BtnDndPickApp_Click / :223 AddDndProcess): the menu lists window owners, already-listed ones
/// ticked and inert, and a pick appends to what is IN THE BOX (an unblurred edit survives).</summary>
public sealed class PerformanceDndPickerTests
{
    [Fact]
    public Task PickAppAppendsToTheBoxAndTicksListedOnes() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var oldApps = PerformanceSettingsSection.RunningApps;
        Window? w = null;
        try
        {
            CoreSettings.Current.DndProcessList = new List<string> { "vlc" };
            var apps = new List<string> { "mpv", "vlc" };
            PerformanceSettingsSection.RunningApps = () => apps;

            var section = new PerformanceSettingsSection();
            w = new Window { Content = section, Width = 700, Height = 1400 };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var box = section.FindControl<TextBox>("TxtDndProcesses")!;
            var pick = section.FindControl<Button>("BtnDndPickApp")!;
            Assert.Equal("vlc", box.Text);
            box.Text = "vlc, potplayer";   // typed, not yet blurred out of

            pick.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var items = section.PickerMenu!.Items.Cast<MenuItem>().ToList();
            Assert.Equal(new[] { "mpv", "vlc" }, items.Select(i => ((TextBlock)i.Header!).Text));
            Assert.True(items[1].IsChecked);
            Assert.False(items[1].IsEnabled);
            Assert.True(items[0].IsEnabled);

            items[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(new[] { "vlc", "potplayer", "mpv" }, CoreSettings.Current.DndProcessList);
            Assert.Equal(ConditioningControlPanel.Services.UI.DndProcessList.Format(new[] { "vlc", "potplayer", "mpv" }), box.Text);

            // Nothing to offer: one inert line says so.
            apps.Clear();
            pick.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var only = Assert.Single(section.PickerMenu!.Items.Cast<MenuItem>());
            Assert.Equal(Loc.Get("set2_dnd_pick_empty"), only.Header);
            Assert.False(only.IsEnabled);
            section.PickerMenu.Close();
        }
        finally
        {
            w?.Close();
            PerformanceSettingsSection.RunningApps = oldApps;
            CoreSettings.ServiceProvider = oldSettings;
        }
        return Task.CompletedTask;
    });
}
