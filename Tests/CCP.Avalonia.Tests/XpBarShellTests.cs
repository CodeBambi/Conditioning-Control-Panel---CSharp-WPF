using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Ledger H11 (page wave k2): the XP bar's signed-out overlay (WPF UpdateXPBarLoginState,
/// MainWindow.UiUpdates.cs:412) and the active bonus chips (RefreshXPBarBonuses, :641).</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class XpBarShellTests
{
    [Fact]
    public Task SignedOutShowsTheOverlayAndBonusChipsFollowTheSkills() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var oldIn = CoreAccount.IsLoggedInProvider;
        CoreAccount.IsLoggedInProvider = null;
        var s = CoreSettings.Current;
        s.UnlockedSkills = new List<string>();
        s.PinkRushActive = false;
        s.CurrentStreak = 0;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            T Find<T>(string n) where T : Control => shell.FindControl<T>(n)!;

            // Signed out: the "log in" overlay over a greyed bar.
            shell.RefreshAccountIdentity();
            Assert.True(Find<Border>("XPBarLoginOverlay").IsVisible);
            Assert.Equal(0.3, Find<Grid>("XPBarContent").Opacity, 3);

            CoreAccount.IsLoggedInProvider = () => true;
            shell.RefreshAccountIdentity();
            Assert.False(Find<Border>("XPBarLoginOverlay").IsVisible);
            Assert.Equal(1.0, Find<Grid>("XPBarContent").Opacity, 3);

            // No skill, no chip. A Sparkle Boost buys one, with its tooltip.
            var list = Find<StackPanel>("XPBarBonusList");
            shell.UpdateStatPills();
            Assert.Empty(list.Children);

            s.UnlockedSkills = new List<string> { "sparkle_boost_1" };
            shell.UpdateStatPills();
            var chip = Assert.IsType<Border>(Assert.Single(list.Children));
            var text = Assert.IsType<TextBlock>(chip.Child).Text!;
            Assert.StartsWith("+", text);
            Assert.Contains("10", text);
            Assert.NotNull(ToolTip.GetTip(chip));

            // The same state paints the same chips (no rebuild on the pill tick).
            shell.UpdateStatPills();
            Assert.Same(chip, list.Children.Single());

            s.UnlockedSkills = new List<string>();
            shell.UpdateStatPills();
            Assert.Empty(list.Children);
        }
        finally
        {
            CoreAccount.IsLoggedInProvider = oldIn;
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            service.SealForReset();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}
