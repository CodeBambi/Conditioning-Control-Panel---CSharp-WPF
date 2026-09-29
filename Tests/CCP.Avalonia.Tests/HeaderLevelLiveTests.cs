using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The shell header's level chip, LVL label, XP readout and bar follow Core progression
/// (WPF MainWindow.UpdateLevelDisplay / OnXPChanged / OnLevelUp), not the XAML's "Lvl 1" / "0 / 70 XP".</summary>
public sealed class HeaderLevelLiveTests
{
    [Fact]
    public Task HeaderPaintsSettingsAndFollowsAwardsAndLevelUps() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.PlayerLevel = 10;
        s.PlayerXP = 5;
        s.OfflineMode = true;             // ProgressionBank's login gate
        s.OfflineUsername = "header-test";
        s.MotionLevel = MotionLevel.Off;  // instant widths, so the bar is assertable
        double Need(int level) => XpCurve.GetXPForLevel(level, XpCurve.EpochOf(s));

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            string Text(string name) => shell.FindControl<TextBlock>(name)!.Text!;

            Assert.Equal("Lvl 10", Text("TxtLevel"));
            Assert.Equal("LVL 10", Text("TxtLevelLabel"));
            Assert.Equal($"5 / {(int)Need(10)} XP", Text("TxtXP"));
            var bar = shell.FindControl<Border>("XPBar")!;
            var track = (Control)bar.Parent!;
            Assert.True(track.Bounds.Width > 0);
            Assert.Equal(5 / Need(10) * track.Bounds.Width, bar.Width, 3);

            ProgressionBank.Add(Need(10), "Quest");   // crosses one level
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(11, s.PlayerLevel);
            Assert.Equal("Lvl 11", Text("TxtLevel"));
            Assert.Equal("LVL 11", Text("TxtLevelLabel"));
            Assert.Equal($"5 / {(int)Need(11)} XP", Text("TxtXP"));
            Assert.Equal($"{Loc.Get("label_level")} 11", Text("ProfileMenuLevel"));   // the bubble rail agrees

            s.MotionLevel = MotionLevel.Full;          // the fill tweens instead of snapping
            ProgressionBank.Add(1, "Quest");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal($"6 / {(int)Need(11)} XP", Text("TxtXP"));
            Assert.NotNull(bar.Transitions);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            s.OfflineMode = false;
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}
