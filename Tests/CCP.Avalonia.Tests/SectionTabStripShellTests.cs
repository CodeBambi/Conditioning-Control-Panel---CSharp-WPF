using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The section page header through the shell (WPF SectionTabStrip + MainWindow.SectionChrome.cs,
/// nav rework 2026-10-06): hidden on Home, Core's NavSections pills on a section page with the
/// tab on screen lit, a pill click / arrow key navigates, the section word returns to the
/// section's last tab, and Ctrl+K opens the palette whatever has focus (d858d6108).
/// </summary>
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

            var s = CoreSettings.Current;
            var lastBefore = s.NavLastTabBySection;
            MainShellWindow? w = null;
            try
            {
                s.NavLastTabBySection = "";
                w = new MainShellWindow();
                w.Show();
                Dispatcher.UIThread.RunJobs();
                var strip = w.Named<SectionTabStrip>("SectionStrip")!;

                w.ShowTab("settings");               // Home: the dashboard draws no header
                Assert.False(strip.IsVisible);

                w.ShowTab("presets");
                Dispatcher.UIThread.RunJobs();
                Assert.True(strip.IsVisible);
                // Studio's pills in table order; Just Drop has no window on this head, so no pill.
                Assert.Equal(new[] { "studio", "presets", "haptics", "ramp" }, strip.PillKeys);
                Assert.Equal("presets", strip.ActivePillKey);
                Assert.Equal($"{Loc.Get("nav_door_studio")} {Loc.Get("nav_crumb_sep")} {Loc.Get("tab_presets")}", strip.CrumbText);

                strip.PillFor("ramp")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("ramp", w.CurrentTab);
                Assert.Equal("ramp", strip.ActivePillKey);
                Assert.Equal("ramp", MainShellWindow.NavLastTabFor("studio"));

                // Arrow keys wrap and activate pages: Right from the last pill lands on the first.
                strip.PillFor("ramp")!.Focus();
                strip.PillFor("ramp")!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
                Assert.Equal("studio", w.CurrentTab);

                // Zone pages light their zone; the section word returns to the last tab.
                w.ShowTab("lockdown");
                Assert.Equal("playsessions", strip.ActivePillKey);
                w.ShowTab("presets");
                s.NavLastTabBySection = "{\"studio\":\"haptics\"}";
                var crumb = strip.GetLogicalDescendants().OfType<Button>().First(b => b.Name is null);
                crumb.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("haptics", w.CurrentTab);

                // Library launchers stay pills that open, not pages.
                w.ShowTab("assets");
                Assert.Contains("medialog", strip.PillKeys);
                Assert.Contains("folders", strip.PillKeys);

                // Ctrl+K from inside the page, through a control that would otherwise take the key.
                Assert.False(SettingsPaletteWindow.IsOpen);
                strip.PillFor("assets")!.RaiseEvent(new KeyEventArgs
                    { RoutedEvent = InputElement.KeyDownEvent, Key = Key.K, KeyModifiers = KeyModifiers.Control });
                Assert.True(SettingsPaletteWindow.IsOpen);
            }
            finally
            {
                SettingsPaletteWindow.CloseIfOpen();
                w?.Close();
                s.NavLastTabBySection = lastBefore;
                Dispatcher.UIThread.RunJobs();
            }
            return Task.CompletedTask;
        });
    }
}
