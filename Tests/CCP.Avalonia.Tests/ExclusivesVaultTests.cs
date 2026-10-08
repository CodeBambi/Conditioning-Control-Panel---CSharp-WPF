using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The vault and the Programs cards read the real roster and the live entitlement seam.</summary>
public sealed class ExclusivesVaultTests
{
    [Fact]
    public async Task ShelfFollowsRosterGatesAndDailyFree()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureAvalonia();
            Window? host = null;
            var (premium, free, pass) = (CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider,
                                         CoreEntitlement.IntakePassAvailableProvider);
            CoreEntitlement.HasPremiumProvider = null;
            CoreEntitlement.IsFreeTodayProvider = null;
            CoreEntitlement.IntakePassAvailableProvider = null;
            try
            {
                var view = new ExclusivesTabView { Width = 1400, Height = 900 };
                host = new Window { Width = 1400, Height = 900, Content = view };
                host.Show();
                Dispatcher.UIThread.RunJobs();

                // Unseeded: every premium door is veiled, Just Drop and the Arcademy (no door seeded) are
                // absent, the free doors stay open. Main 2e9080399: Prime first, then Basic, then untiered.
                var rows = Rows(view);
                Assert.DoesNotContain(rows, r => r.Feature.Key is "justdrop" or "arcademy");
                Assert.Equal(new[] { "dtrh", "breakout", "gazeminigame", "focusgaze", "fyp" }, rows.Take(5).Select(r => r.Feature.Key));
                Assert.Equal(rows.Select(r => r.Feature.Tier switch { 2 => 0, 1 => 1, _ => 2 }).OrderBy(t => t),
                             rows.Select(r => r.Feature.Tier switch { 2 => 0, 1 => 1, _ => 2 }));
                ExclusiveFeature.ArcademyDoorProvider = () => true;
                view.RefreshVault();
                Assert.Contains(Rows(view), r => r.Feature.Key == "arcademy");
                ExclusiveFeature.ArcademyDoorProvider = null;
                view.RefreshVault();
                rows = Rows(view);

                // Main bf57cecdf: the cards stretch to fill the row at as many columns as fit.
                var wrap = (WrapPanel)view.FindControl<ItemsControl>("ExclusivesShelf")!.ItemsPanelRoot!;
                var fit = ConditioningControlPanel.Services.UI.ExclusiveShelfFit.For(view.FindControl<ItemsControl>("ExclusivesShelf")!.Bounds.Width);
                Assert.True(fit.Columns >= 3);
                Assert.Equal(fit.Width + 16, wrap.ItemWidth);
                Assert.True(rows.Single(r => r.Feature.Key == "fyp").IsLocked);
                Assert.False(rows.Single(r => r.Feature.Key == "backroom").IsLocked);
                Assert.True(rows.Single(r => r.Feature.Key == "gradedintake").IsLocked);
                Assert.True(rows.Single(r => r.Feature.Key == "haptics").HasArt);
                Assert.True(view.FindControl<Border>("SpotVeil")!.IsVisible);

                // No launcher on this head: visible, inert, and it says so.
                var tip = ConditioningControlPanel.Localization.Loc.Get("exclusives_not_on_this_build");
                Assert.Equal(tip, rows.Single(r => r.Feature.Key == "backroom").UnavailableTip);
                Assert.Null(rows.Single(r => r.Feature.Key == "haptics").UnavailableTip);
                Assert.False(view.FindControl<Button>("BtnSpotOpen")!.IsEnabled);

                // A free account with its weekly pass unspent, and fyp rotated in as today's free one.
                CoreEntitlement.IntakePassAvailableProvider = () => true;
                CoreEntitlement.IsFreeTodayProvider = key => key == "fyp";
                view.RefreshVault();
                rows = Rows(view);
                var intake = rows.Single(r => r.Feature.Key == "gradedintake");
                Assert.Equal(ExclusiveGateState.PassReady, intake.State);
                Assert.True(intake.HasChip);
                var fyp = rows.Single(r => r.Feature.Key == "fyp");
                Assert.False(fyp.IsLocked);
                Assert.True(fyp.BadgeFreeToday);   // tiered: the badge re-stamps, no pill
                Assert.False(view.FindControl<Border>("SpotVeil")!.IsVisible);

                // Premium owns the pool: no gift tag, no veil.
                CoreEntitlement.HasPremiumProvider = () => true;
                view.RefreshVault();
                rows = Rows(view);
                // Main 2e9080399: the collection now lists the Prime (tier 2) doors too, and Basic
                // premium does not open those; the Lab bar does.
                Assert.All(rows.Where(r => r.Feature.Key != "gradedintake" && r.Feature.Tier < 2), r => Assert.False(r.IsLocked));
                Assert.All(rows.Where(r => r.Feature.Tier == 2), r => Assert.True(r.IsLocked));
                Assert.False(rows.Single(r => r.Feature.Key == "fyp").FreeToday);
            }
            finally
            {
                CoreEntitlement.HasPremiumProvider = premium;
                CoreEntitlement.IsFreeTodayProvider = free;
                CoreEntitlement.IntakePassAvailableProvider = pass;
                ExclusiveFeature.ArcademyDoorProvider = null;
                host?.Close();
                Dispatcher.UIThread.RunJobs();
            }

            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ProgramCardsDropThePadlockForPremiumButStayUnenrollable()
    {
        // Brushes are AvaloniaObjects: build the rows on the test dispatcher's thread.
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureAvalonia();
            var library = BuiltInPrograms.All();
            var premium = CoreEntitlement.HasPremiumProvider;
            CoreEntitlement.HasPremiumProvider = null;
            try
            {
                Assert.Contains(MainShellWindow.BuildProgramBrowseItems(library), r => r.IsLocked);
                CoreEntitlement.HasPremiumProvider = () => true;
                var rows = MainShellWindow.BuildProgramBrowseItems(library);
                Assert.All(rows, r => Assert.False(r.IsLocked));
                Assert.All(rows, r => Assert.False(r.IsActionEnabled));
            }
            finally { CoreEntitlement.HasPremiumProvider = premium; }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void RailStarFollowsTheCoreGate()
    {
        var locked = typeof(MainShellWindow).GetMethod("IsNavEntryLocked",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        bool Star(string key) => (bool)locked.Invoke(null, new object[] { key })!;
        var (premium, free) = (CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider);
        CoreEntitlement.HasPremiumProvider = null;
        CoreEntitlement.IsFreeTodayProvider = null;
        try
        {
            // lockdown has no DailyFreeKey, so the live ? box cannot race this assertion.
            Assert.True(Star("lockdown"));
            Assert.False(Star("nosuchkey"));
            CoreEntitlement.IsFreeTodayProvider = key => key == "haptics";
            Assert.False(Star("haptics"));
            CoreEntitlement.HasPremiumProvider = () => true;
            Assert.False(Star("lockdown"));
        }
        finally { CoreEntitlement.HasPremiumProvider = premium; CoreEntitlement.IsFreeTodayProvider = free; }
    }

    private static ExclusiveCardRow[] Rows(ExclusivesTabView view) =>
        view.FindControl<ItemsControl>("ExclusivesShelf")!.Items.Cast<ExclusiveCardRow>().ToArray();

    private static void EnsureAvalonia()
    {
        if (Application.Current is not null) return;
        AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
    }
}
