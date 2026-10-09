using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The Play wall from the shell (WPF PlayTabView.xaml + MainWindow.PlayTab.cs): the GAMES
/// zone, the Graded Intake card's four pass states, and the two doors that land on a section.</summary>
public sealed class PlayWallTests
{
    [Fact]
    public void IntakeCardFollowsThePass_GamesAreHonest_DoorsLandOnTheirSection()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            var prevSettings = CoreSettings.ServiceProvider;
            CoreSettings.ServiceProvider = () => service;
            var (week, utc) = (service.Current.IntakePassSpentWeek, service.Current.IntakePassSpentUtc);
            var (loggedIn, lab) = (CoreAccount.IsLoggedInProvider, CoreAccount.HasLabAccessProvider);
            bool isLoggedIn = false, isLab = false;
            CoreAccount.IsLoggedInProvider = () => isLoggedIn;
            CoreAccount.HasLabAccessProvider = () => isLab;
            (service.Current.IntakePassSpentWeek, service.Current.IntakePassSpentUtc) = ("", null);
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                shell.ShowTab("play");
                Dispatcher.UIThread.RunJobs();
                var play = shell.Named<Control>("PlayTab")!;
                T C<T>(string n) where T : Control => play.FindControl<T>(n)!;
                var band = C<Border>("PlayLockIntake");
                var line = C<TextBlock>("TxtPlayIntakeState");
                var where = C<Button>("BtnPlayIntakePassHome");

                // Signed out: band + the sign-in copy, no "where" button.
                Assert.True(band.IsVisible);
                Assert.True(line.IsVisible);
                Assert.Equal(Loc.Get("intake_gate_login_body"), line.Text);
                Assert.False(where.IsVisible);

                // Signed in, week unspent: no band, the announcement and the "where" button.
                isLoggedIn = true;
                AvApp.IntakePass.RaiseChanged();
                Dispatcher.UIThread.RunJobs();
                Assert.False(band.IsVisible);
                Assert.Equal(Loc.Get("pl6_intake_state_available"), line.Text);
                Assert.True(where.IsVisible);

                // A completed run spends the week; the card repaints from the event alone.
                AvApp.IntakePass.ConsumeForCompletedIntake();
                Dispatcher.UIThread.RunJobs();
                Assert.True(band.IsVisible);
                var days = IntakePassService.DaysUntilNextPass;
                Assert.Equal(days == 1 ? Loc.Get("intake_gate_spent_body_one_day") : Loc.GetF("intake_gate_spent_body", days), line.Text);
                Assert.False(where.IsVisible);

                // Tier 2: no band, no line.
                isLab = true;
                AvApp.IntakePass.RaiseChanged();
                Dispatcher.UIThread.RunJobs();
                Assert.False(band.IsVisible);
                Assert.False(line.IsVisible);

                // GAMES: four cards, no host on this head, so the Play button refuses honestly.
                foreach (var n in new[] { "BtnPlayBreakoutDemo", "BtnPlayBreakout", "BtnPlayGoon", "BtnPlayChess" })
                {
                    var b = C<Button>(n);
                    Assert.False(b.IsEnabled, n);
                    Assert.Equal(Loc.Get("exclusives_not_on_this_build"), ToolTip.GetTip(b));
                }
                Assert.NotNull(C<Image>("PlayBreakoutArt").Source);

                // "Configure in settings" lands on the Devices section (WPF OpenDeviceSettings).
                C<Button>("BtnOpenDeviceSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("appsettings", shell.CurrentTab);
                Assert.True(shell.AppSettingsPage!.FindControl<RadioButton>("SectionPillDevices")!.IsChecked);

                // Loom opens the Studio on the Spiral module (WPF OpenStudioModule("spiral")).
                shell.ShowTab("play");
                C<Button>("BtnPlayLoom").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("studio", shell.CurrentTab);
                Assert.Equal("spiral", shell.StudioRack!.SelectedRackKey);
            }
            finally
            {
                shell.Close();
                CoreAccount.IsLoggedInProvider = loggedIn;
                CoreAccount.HasLabAccessProvider = lab;
                (service.Current.IntakePassSpentWeek, service.Current.IntakePassSpentUtc) = (week, utc);
                service.SaveImmediate();
                CoreSettings.ServiceProvider = prevSettings;
            }
        });
    }
}
