using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.NavRail;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The section page header through the shell (Controls/NavRail/SectionTabStrip + the chrome in
/// MainShellWindow.TabNavigation.cs): hidden on Home, the tab on screen lit on a section page, a
/// pill press navigates, a zone page lights its zone, and Ctrl+K opens the palette whatever has
/// focus (WPF EnsurePaletteShortcut, d858d6108; MainShellWindow.SectionChrome.cs).
/// Repointed from the sync6 strip to the 7.1.5 parity strip in the 2026-10-10 merge; the pill
/// table, the crumb and the first-pill rule are pinned by NavRailFirstTabTests and the Core
/// parity tests.
/// </summary>
[Collection(RunsAloneCollection.Name)]
public sealed class SectionTabStripShellTests
{
    [Fact]
    public async Task StripFollowsShowTabAndItsPillsNavigate()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            MainShellWindow? w = null;
            try
            {
                w = new MainShellWindow();
                w.Show();
                Dispatcher.UIThread.RunJobs();
                var strip = w.Named<SectionTabStrip>("SectionStrip")!;

                w.ShowTab("settings");               // Home: the dashboard draws no header
                Dispatcher.UIThread.RunJobs();
                Assert.False(strip.IsVisible);

                w.ShowTab("presets");
                Dispatcher.UIThread.RunJobs();
                Assert.True(strip.IsVisible);
                Assert.Contains("haptics", strip.PillKeys);
                Assert.Equal("presets", strip.ActivePillKey);

                strip.ChooseForTests("haptics");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("haptics", w.CurrentTab);
                Assert.Equal("haptics", strip.ActivePillKey);

                // Zone pages light their zone.
                w.ShowTab("lockdown");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("playsessions", strip.ActivePillKey);

                // Ctrl+K from inside the page, through a control that would otherwise take the key.
                w.ShowTab("presets");
                Dispatcher.UIThread.RunJobs();
                Assert.False(SettingsPaletteWindow.IsOpen);
                strip.PillFor("presets")!.RaiseEvent(new KeyEventArgs
                    { RoutedEvent = InputElement.KeyDownEvent, Key = Key.K, KeyModifiers = KeyModifiers.Control });
                Assert.True(SettingsPaletteWindow.IsOpen);
            }
            finally
            {
                SettingsPaletteWindow.CloseIfOpen();
                w?.Close();
                Dispatcher.UIThread.RunJobs();
            }
            return Task.CompletedTask;
        });
    }
}
