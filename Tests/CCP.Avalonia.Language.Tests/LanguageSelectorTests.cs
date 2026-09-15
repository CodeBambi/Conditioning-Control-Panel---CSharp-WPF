using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
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
        Path.GetTempPath(), "ccp-language-tests-" + Environment.ProcessId);

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
    public void LanguageSelectorsRestoreSynchronizeAndPersist()
    {
        var settingsPath = Path.Combine(TestProfile.DirectoryPath, "settings.json");
        var settingsBeforeStartup = SeedProfile(settingsPath);
        var settingsWriteBeforeStartup = File.GetLastWriteTimeUtc(settingsPath);
        var shell = StartApp();

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
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = null;
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

    private static MainShellWindow StartApp()
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
        Assert.True(Dispatcher.UIThread.CheckAccess(), "Avalonia setup did not stay on the test thread");

        var shell = Assert.IsType<MainShellWindow>(lifetime.MainWindow);
        shell.Show();
        Dispatcher.UIThread.RunJobs();
        return shell;
    }
}
