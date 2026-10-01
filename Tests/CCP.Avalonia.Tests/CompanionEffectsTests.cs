using System.Linq;
using Avalonia;
using Avalonia.Headless;
using ConditioningControlPanel.Avalonia.Views.Windows;
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

    /// <summary>The AI's lock card is strict and reports "fired" only when a card really came up: a
    /// second request while one is open never stacks, so it is reported as not fired.</summary>
    [Fact]
    public void LockCardSurfaceReportsOnlyACardThatShowed()
    {
        var (provider, lab) = (CoreSettings.ServiceProvider, CoreAccount.HasLabAccessProvider);
        AvaloniaTestDispatcher.Run(() =>
        {
            if (global::Avalonia.Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            service.Current.CompanionPrompt.AllowAiToControlEffects = true;
            CoreAccount.HasLabAccessProvider = () => true;
            try
            {
                CompanionEffects.Seed();
                Assert.True(MantraLockScreenCommand.Surface!("good girls obey", 1));
                Assert.True(LockCardWindow.IsAnyOpen());
                Assert.False(MantraLockScreenCommand.Surface!("again", 1));
                LockCardWindow.ForceCloseAll();
                AiCommandService.CancelAll();                       // a panic: a deferred start is dropped
                service.Current.CompanionPrompt.AllowAiToControlEffects = false;
                Assert.False(MantraLockScreenCommand.Surface!("no consent", 1));
                Assert.False(LockCardWindow.IsAnyOpen());
            }
            finally
            {
                LockCardWindow.ForceCloseAll();
                (CoreSettings.ServiceProvider, CoreAccount.HasLabAccessProvider) = (provider, lab);
            }
        });
    }
}
