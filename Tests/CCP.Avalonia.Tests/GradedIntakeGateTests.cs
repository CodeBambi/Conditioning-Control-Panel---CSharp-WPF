using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The Graded Intake gate paints off the Core weekly pass, as WPF MainWindow.Lab.cs:343.</summary>
public sealed class GradedIntakeGateTests
{
    [Fact]
    public async Task GateFollowsThePassThroughLoginRunAndPremium()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureAvalonia();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var (week, utc) = (service.Current.IntakePassSpentWeek, service.Current.IntakePassSpentUtc);
            var (loggedIn, lab) = (CoreAccount.IsLoggedInProvider, CoreAccount.HasLabAccessProvider);
            bool isLoggedIn = false, isLab = false;
            CoreAccount.IsLoggedInProvider = () => isLoggedIn;
            CoreAccount.HasLabAccessProvider = () => isLab;
            service.Current.IntakePassSpentWeek = "";
            service.Current.IntakePassSpentUtc = null;
            var view = new GradedIntakeTabView();
            var host = new Window { Width = 1400, Height = 900, Content = view };
            try
            {
                host.Show();
                Dispatcher.UIThread.RunJobs();
                var gate = view.FindControl<Border>("GradedIntakeGate")!;
                var banner = view.FindControl<Border>("GradedIntakePassBanner")!;
                var gated = view.FindControl<Border>("GradedIntakeGatedContent")!;
                var headline = view.FindControl<TextBlock>("TxtGradedIntakeGateHeadline")!;

                // Signed out: the door asks for a sign-in and the launch zone is dead.
                Assert.True(gate.IsVisible);
                Assert.False(gated.IsEnabled);
                Assert.False(banner.IsVisible);
                Assert.Equal(Loc.Get("intake_gate_login_headline"), headline.Text);

                // Signed in, week unspent: open, with the pass banner.
                isLoggedIn = true;
                AvApp.IntakePass.RaiseChanged();
                Dispatcher.UIThread.RunJobs();
                Assert.False(gate.IsVisible);
                Assert.True(gated.IsEnabled);
                Assert.True(banner.IsVisible);

                // A completed run spends the week and the page repaints from the event alone.
                AvApp.IntakePass.ConsumeForCompletedIntake();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(IntakePassService.CurrentWeekKey(), service.Current.IntakePassSpentWeek);
                Assert.True(gate.IsVisible);
                Assert.False(banner.IsVisible);
                Assert.Equal(Loc.Get("intake_gate_spent_headline"), headline.Text);

                // Tier 2: no door, no banner, whatever the week says.
                isLab = true;
                AvApp.IntakePass.RaiseChanged();
                Dispatcher.UIThread.RunJobs();
                Assert.False(gate.IsVisible);
                Assert.False(banner.IsVisible);
                Assert.True(gated.IsEnabled);
            }
            finally
            {
                host.Close();
                CoreAccount.IsLoggedInProvider = loggedIn;
                CoreAccount.HasLabAccessProvider = lab;
                service.Current.IntakePassSpentWeek = week;
                service.Current.IntakePassSpentUtc = utc;
                service.SaveImmediate();
                CoreSettings.ServiceProvider = null;
            }
            return Task.CompletedTask;
        });
    }

    /// <summary>WPF IntakePassService.RefundLateResolvedPremium: a run charged before premium
    /// resolved is handed back when the provider's TierChanged lands.</summary>
    [Fact]
    public void LateResolvedPremiumRefundsThisSessionsSpend()
    {
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var (week, utc) = (service.Current.IntakePassSpentWeek, service.Current.IntakePassSpentUtc);
        var (loggedIn, lab) = (CoreAccount.IsLoggedInProvider, CoreAccount.HasLabAccessProvider);
        var isLab = false;
        CoreAccount.IsLoggedInProvider = () => true;
        CoreAccount.HasLabAccessProvider = () => isLab;
        try
        {
            EventHandler<PatreonTier>? tierChanged = null;
            using var pass = new IntakePassService();
            pass.AttachEntitlementSources(h => tierChanged += h, h => tierChanged -= h);
            var raised = 0;
            pass.PassStateChanged += (_, _) => raised++;

            pass.ConsumeForCompletedIntake();
            Assert.Equal(IntakePassState.Spent, pass.State);

            isLab = true;
            tierChanged!.Invoke(null, PatreonTier.Level2);
            Assert.Equal("", service.Current.IntakePassSpentWeek);
            Assert.Null(service.Current.IntakePassSpentUtc);
            Assert.Equal(2, raised);

            isLab = false;
            Assert.Equal(IntakePassState.Available, pass.State);
        }
        finally
        {
            CoreAccount.IsLoggedInProvider = loggedIn;
            CoreAccount.HasLabAccessProvider = lab;
            service.Current.IntakePassSpentWeek = week;
            service.Current.IntakePassSpentUtc = utc;
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
    }

    private static void EnsureAvalonia()
    {
        if (Application.Current is not null) return;
        AppBuilder.Configure<AvApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
    }
}
