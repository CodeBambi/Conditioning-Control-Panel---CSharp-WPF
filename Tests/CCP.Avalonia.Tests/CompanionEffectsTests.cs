using System.Linq;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Commands;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>CompanionBrain slice 4 on this head: the brain's commands reach Core's gate, a surface
/// this head lacks is refused (never faked), and the capability list holds only what opens here.</summary>
public sealed class CompanionEffectsTests
{
    [Fact]
    public void SeedWiresTheGateAndLeavesAMissingSurfaceRefused()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            CompanionEffects.Seed();
            Assert.NotNull(CompanionBrain.CommandExecutor);
            Assert.NotNull(CompanionBrain.ActivitiesProvider);
            Assert.Null(SpiralCommand.Surface);                     // no spiral overlay port
            // No main window / click-through here: the overlay surface refuses instead of claiming a flash.
            Assert.False(FlashImageCommand.Surface!(3, 1000, 100));
        });
    }

    [Fact]
    public void ActivitiesOfferOnlyWhatOpensHere()
    {
        var (provider, signedIn, lab) = (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider);
        try
        {
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            CoreEntitlement.HasLabProvider = () => true;
            CoreAccount.IsLoggedInProvider = () => true;
            var all = CompanionEffects.Activities();
            Assert.Equal(new[] { "game.intake", "page.studio", "page.presets", "page.quests", "page.assets" },
                all.Select(a => a.Id));
            Assert.True(all[0].Allowed);
            CoreAccount.IsLoggedInProvider = () => false;          // the tile would ask to sign in
            Assert.False(all[0].Allowed);
            CoreAccount.IsLoggedInProvider = () => true;
            service.Current.AudioOnlySession = true;
            Assert.False(all[0].Allowed);
        }
        finally
        {
            (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider) = (provider, signedIn, lab);
        }
    }
}
