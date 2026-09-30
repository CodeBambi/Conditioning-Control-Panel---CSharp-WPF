using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
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
            var prevSettings = CoreSettings.ServiceProvider;
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
                CoreSettings.ServiceProvider = prevSettings;
            }
            return Task.CompletedTask;
        });
    }

    /// <summary>The real head wiring: AccountSeed.Seed() hooks the pass onto the providers it builds,
    /// so a TierChanged that resolves premium re-raises PassStateChanged and refunds a spend charged
    /// before it (WPF IntakePassService.RefundLateResolvedPremium, App.xaml.cs:2937).</summary>
    [Fact]
    public void SeededProvidersTierChangeRefundsThisSessionsSpend()
    {
        var service = new SettingsService();
        var prevSettings = CoreSettings.ServiceProvider;
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        var (week, utc) = (s.IntakePassSpentWeek, s.IntakePassSpentUtc);
        var saved = (CoreAccount.IsLoggedInProvider, CoreAccount.DisplayNameProvider, CoreAccount.IsWhitelistedProvider,
            CoreAccount.HasPremiumAccessProvider, CoreAccount.HasLabAccessProvider, CoreEntitlement.HasPremiumProvider,
            CoreEntitlement.HasLabProvider, CoreProgram.HasPremiumProvider);
        Assert.False(s.HasCachedLabAccess);   // a free account until the TierChanged below
        (s.IntakePassSpentWeek, s.IntakePassSpentUtc) = ("", null);   // seeded here: an unspent week, whatever ran before
        var raised = 0;
        EventHandler onChanged = (_, _) => raised++;
        try
        {
            Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => new AppSettings())));
            AvApp.IntakePass.PassStateChanged += onChanged;

            AvApp.IntakePass.ConsumeForCompletedIntake();
            Assert.Equal(IntakePassService.CurrentWeekKey(), s.IntakePassSpentWeek);

            AccountSeed.Patreon!.SetWhitelistStatus(true);   // a real TierChanged, resolving tier 2
            Assert.Equal("", s.IntakePassSpentWeek);
            Assert.Null(s.IntakePassSpentUtc);
            Assert.Equal(2, raised);
        }
        finally
        {
            AvApp.IntakePass.PassStateChanged -= onChanged;
            (CoreAccount.IsLoggedInProvider, CoreAccount.DisplayNameProvider, CoreAccount.IsWhitelistedProvider,
                CoreAccount.HasPremiumAccessProvider, CoreAccount.HasLabAccessProvider, CoreEntitlement.HasPremiumProvider,
                CoreEntitlement.HasLabProvider, CoreProgram.HasPremiumProvider) = saved;
            (s.IntakePassSpentWeek, s.IntakePassSpentUtc) = (week, utc);
            service.SaveImmediate();
            CoreSettings.ServiceProvider = prevSettings;
        }
    }

    /// <summary>A second Seed() re-points the pass at the new providers: a TierChanged on the
    /// first Patreon provider must no longer reach PassStateChanged (IntakePassService detach).</summary>
    [Fact]
    public void ReseedDetachesThePreviousProviders()
    {
        var service = new SettingsService();
        var prevSettings = CoreSettings.ServiceProvider;
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        var (week, utc) = (s.IntakePassSpentWeek, s.IntakePassSpentUtc);
        var saved = (CoreAccount.IsLoggedInProvider, CoreAccount.DisplayNameProvider, CoreAccount.IsWhitelistedProvider,
            CoreAccount.HasPremiumAccessProvider, CoreAccount.HasLabAccessProvider, CoreEntitlement.HasPremiumProvider,
            CoreEntitlement.HasLabProvider, CoreProgram.HasPremiumProvider);
        var raised = 0;
        EventHandler onChanged = (_, _) => raised++;
        try
        {
            Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => new AppSettings())));
            var first = AccountSeed.Patreon!;
            Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => new AppSettings())));
            Assert.NotSame(first, AccountSeed.Patreon);
            AvApp.IntakePass.PassStateChanged += onChanged;

            first.SetWhitelistStatus(true);   // TierChanged on the stale provider
            Assert.Equal(0, raised);
        }
        finally
        {
            AvApp.IntakePass.PassStateChanged -= onChanged;
            (CoreAccount.IsLoggedInProvider, CoreAccount.DisplayNameProvider, CoreAccount.IsWhitelistedProvider,
                CoreAccount.HasPremiumAccessProvider, CoreAccount.HasLabAccessProvider, CoreEntitlement.HasPremiumProvider,
                CoreEntitlement.HasLabProvider, CoreProgram.HasPremiumProvider) = saved;
            (s.IntakePassSpentWeek, s.IntakePassSpentUtc) = (week, utc);
            service.SaveImmediate();
            CoreSettings.ServiceProvider = prevSettings;
        }
    }

    /// <summary>WPF Patreon.cs:294: sign-in and sign-out (UpdateQuickLoginUI) repaint the intake door.</summary>
    [Fact]
    public async Task SignInOrOutRaisesPassStateChanged()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureAvalonia();
            var raised = 0;
            EventHandler onChanged = (_, _) => raised++;
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                AvApp.IntakePass.PassStateChanged += onChanged;
                shell.UpdateQuickLoginUI(accountChanged: true);
                Assert.Equal(1, raised);
            }
            finally
            {
                AvApp.IntakePass.PassStateChanged -= onChanged;
                shell.Close();
            }
            return Task.CompletedTask;
        });
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
