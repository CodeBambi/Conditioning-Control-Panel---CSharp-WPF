using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Descent;
using Xunit;
using AppA = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Fix wave f4-you (2026-10-09): the Descent's two second doors (WPF MainWindow.ProfileSpiral.cs). The plate on
/// your own Trainer Card and the account menu row follow the block and the withheld rule, the row carries its
/// "Day N · stage" summary, and its click opens the Spiral Room. Also the Showcase "next up" line on the real card.
/// Opens a shell and swaps the shell's static block provider, so it runs alone.
/// </summary>
[Collection(RunsAloneCollection.Name)]
public sealed class ProfileSpiralDoorsTests
{
    [Fact]
    public async Task PlateAndMenuRowFollowTheBlock_AndTheRowOpensTheRoom()
    {
        var s = CoreSettings.Current;
        var old = (s.UserDisplayName, s.OfflineMode, MainShellWindow.ProfileSpiralBlock, MainShellWindow.ProfileSpiralWithheld);
        try
        {
            (s.UserDisplayName, s.OfflineMode) = (null, true);
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<AppA>()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                LocalizationManager.Instance.SetLanguage("en");

                var block = new DescentBlock { DevotionDays = 12, Stage = new DescentStage { N = 3, NextAt = 30 } };
                Assert.Equal("Day 12 · " + DescentStageCopy.Name(3), MainShellWindow.BuildSpiralSummary(block));

                var shell = new MainShellWindow();
                shell.Show();
                try
                {
                    shell.ShowTab("discord");
                    await Task.Delay(20);
                    Dispatcher.UIThread.RunJobs();
                    var page = shell.ProfilePage!;
                    var plate = page.FindControl<Border>("ProfileSpiralPlate")!;
                    var row = shell.Named<Button>("ProfileMenuSpiralRow")!;

                    // Your own card names the next free achievement still locked (was always hidden).
                    var nextUp = page.FindControl<TextBlock>("TxtProfileNextUp")!;
                    var expected = MainShellWindow.FindNextAchievementName(
                        ConditioningControlPanel.Avalonia.App.Achievements?.Progress?.UnlockedAchievements ?? new());
                    if (ConditioningControlPanel.Avalonia.App.Achievements?.Progress?.UnlockedAchievements != null && expected != null)
                    {
                        Assert.True(nextUp.IsVisible);
                        Assert.Equal(Loc.GetF("profile_showcase_next_up", expected), nextUp.Text);
                    }

                    // No block (every account outside the rollout): both doors stay shut.
                    MainShellWindow.ProfileSpiralBlock = null;
                    shell.OnSpiralBlockChanged();
                    Assert.False(plate.IsVisible);
                    Assert.False(row.IsVisible);

                    // A block: both doors open and the row reads its summary.
                    MainShellWindow.ProfileSpiralBlock = () => block;
                    shell.OnSpiralBlockChanged();
                    Assert.True(plate.IsVisible);
                    Assert.True(row.IsVisible);
                    Assert.Equal("Day 12 · " + DescentStageCopy.Name(3), shell.Named<TextBlock>("ProfileMenuSpiralSummary")!.Text);

                    // Somebody else's card never wears your spiral; back to you and it returns.
                    shell.SetProfileViewingSelf(false);
                    Assert.False(plate.IsVisible);
                    Assert.True(row.IsVisible);
                    shell.SetProfileViewingSelf(true);
                    Assert.True(plate.IsVisible);

                    // Withheld by the migration: both shut, block or not.
                    MainShellWindow.ProfileSpiralWithheld = () => true;
                    shell.OnSpiralBlockChanged();
                    Assert.False(plate.IsVisible);
                    Assert.False(row.IsVisible);
                    MainShellWindow.ProfileSpiralWithheld = null;
                    shell.OnSpiralBlockChanged();

                    // The row is a door into the Spiral Room.
                    row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(SpiralRoom.TabKey, shell.CurrentTab);
                }
                finally { shell.Close(); }
            });
        }
        finally
        {
            (s.UserDisplayName, s.OfflineMode, MainShellWindow.ProfileSpiralBlock, MainShellWindow.ProfileSpiralWithheld) = old;
            CoreSettings.SaveImmediate();
        }
    }
}
