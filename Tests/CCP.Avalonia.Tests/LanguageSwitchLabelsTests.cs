using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Ledger G16 / G17: labels written in code follow a language switch (tray items, the
/// startup video's "(Random)"). Runs alone: it opens a shell and switches the process language.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class LanguageSwitchLabelsTests
{
    [Fact]
    public Task TrayItemsAndTheRandomLabelFollowALanguageSwitch() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        string before = LocalizationManager.Instance.CurrentLanguage;
        string? videoBefore = CoreSettings.Current.StartupVideoPath;
        bool perfBefore = CoreSettings.Current.PerformanceMode;
        CoreSettings.Current.PerformanceMode = true;
        var shell = new MainShellWindow();
        try
        {
            LocalizationManager.Instance.SetLanguage("en");
            CoreSettings.Current.StartupVideoPath = "";
            shell.Show();
            shell.CreateTray();
            shell.TrayHostPresent = () => true;
            shell.ShowTab("appsettings");
            Dispatcher.UIThread.RunJobs();

            string[] keys = { "tray_show", "launcher_back_to_client", CoreSettings.Current.IsBambiMode ? "tray_wake_bambi" : "tray_wake", "leash_cut", "tray_stop_everything", "tray_exit" };
            var items = shell.Tray!.Menu!.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).ToList();
            Assert.Equal(keys.Select(Loc.Get), items.Select(i => i.Header));
            string showEnglish = items[0].Header!;

            var general = shell.GetLogicalDescendants().OfType<GeneralSettingsSection>().Single();
            general.RefreshStartupVideoLabel();
            string randomEnglish = general.TxtStartupVideo.Text!;
            Assert.Equal(Loc.Get("label_random"), randomEnglish);

            LocalizationManager.Instance.SetLanguage("it");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(keys.Select(Loc.Get), items.Select(i => i.Header));
            Assert.NotEqual(showEnglish, items[0].Header);
            Assert.Equal(Loc.Get("label_random"), general.TxtStartupVideo.Text);
            Assert.NotEqual(randomEnglish, general.TxtStartupVideo.Text);
        }
        finally
        {
            LocalizationManager.Instance.SetLanguage(before);
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.Current.StartupVideoPath = videoBefore;
            CoreSettings.Current.PerformanceMode = perfBefore;
            CoreSettings.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}
