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
            try
            {
                var view = new ExclusivesTabView { Width = 1400, Height = 900 };
                host = new Window { Width = 1400, Height = 900, Content = view };
                host.Show();
                Dispatcher.UIThread.RunJobs();

                // Unseeded: every premium door is veiled, Just Drop is absent, the free doors stay open.
                var rows = Rows(view);
                Assert.Equal(ExclusiveFeature.All.Where(f => f.Key != "justdrop").Select(f => f.Key),
                             rows.Select(r => r.Feature.Key));
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
                Assert.All(rows.Where(r => r.Feature.Key != "gradedintake"), r => Assert.False(r.IsLocked));
                Assert.False(rows.Single(r => r.Feature.Key == "fyp").FreeToday);
            }
            finally
            {
                CoreEntitlement.HasPremiumProvider = null;
                CoreEntitlement.IsFreeTodayProvider = null;
                CoreEntitlement.IntakePassAvailableProvider = null;
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
            try
            {
                Assert.Contains(MainShellWindow.BuildProgramBrowseItems(library), r => r.IsLocked);
                CoreEntitlement.HasPremiumProvider = () => true;
                var rows = MainShellWindow.BuildProgramBrowseItems(library);
                Assert.All(rows, r => Assert.False(r.IsLocked));
                Assert.All(rows, r => Assert.False(r.IsActionEnabled));
            }
            finally { CoreEntitlement.HasPremiumProvider = null; }
            return Task.CompletedTask;
        });
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
