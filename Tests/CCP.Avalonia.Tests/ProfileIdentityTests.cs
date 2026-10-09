using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Headless;
using Avalonia.Interactivity;
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

namespace CCP.Avalonia.Tests;

/// <summary>
/// Fix wave f4-you (2026-10-09): the Trainer Card's Patreon plates, your own name and the "next up" line, and the
/// Achievements patron lock's Visit Patreon button. Same class as the other cloud tests: they share the
/// CoreSettings / CoreAccount statics.
/// </summary>
public sealed partial class AccountSeedTests
{
    private static void EnsureHeadlessApp()
    {
        if (global::Avalonia.Application.Current is null)
            global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public async Task TrainerCard_PatreonPlatesFollowTheTier_OwnAndViewed()
    {
        var s = CoreSettings.Current;
        var old = (s.OfflineMode, s.UserDisplayName, s.PatreonTier);
        try
        {
            (s.OfflineMode, s.UserDisplayName, s.PatreonTier) = (true, "plate_owner", 2);
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                EnsureHeadlessApp();
                var tab = new DiscordTabView();
                Image Img(string n) => tab.FindControl<Image>(n)!;

                // Your own card: the settings tier draws both plates (WPF Browser.cs:2060).
                await tab.ViewMyProfileAsync();
                Assert.True(Img("ProfilePatreonBadge").IsVisible && Img("ProfilePatreonBadge").Source != null);
                Assert.True(Img("ProfilePatreonTierBadge").IsVisible && Img("ProfilePatreonTierBadge").Source != null);

                // Someone else's row: a tier 3 patron wears a different picture; a free player wears none.
                var two = Img("ProfilePatreonBadge").Source;
                tab.DisplayProfileEntry(new LeaderboardRow { DisplayName = "Nyx", IsPatreon = true, PatreonTier = 3, Level = 4 });
                Assert.True(Img("ProfilePatreonBadge").IsVisible && Img("ProfilePatreonTierBadge").IsVisible);
                Assert.NotSame(two, Img("ProfilePatreonBadge").Source);
                tab.DisplayProfileEntry(new LeaderboardRow { DisplayName = "Free", IsPatreon = false, PatreonTier = 0, Level = 4 });
                Assert.False(Img("ProfilePatreonBadge").IsVisible);
                Assert.False(Img("ProfilePatreonTierBadge").IsVisible);
                Assert.False(tab.FindControl<Border>("ProfilePatreonTierBanner")!.IsVisible);
                // A stale flag with tier 0 is not a patron (WPF: IsPatreon && tier >= 1).
                tab.DisplayProfileEntry(new LeaderboardRow { DisplayName = "Stale", IsPatreon = true, PatreonTier = 0, Level = 4 });
                Assert.False(Img("ProfilePatreonTierBadge").IsVisible);

                // The tier plate is hidden whenever its picture is: whitelisted with tier 0 gets tier 1's plate, no level badge.
                tab.ApplyPatreonPlates(0, hasPatreon: true);
                Assert.False(Img("ProfilePatreonBadge").IsVisible);
                Assert.True(Img("ProfilePatreonTierBadge").IsVisible);
            });
            Assert.Equal(("Patreon tier1.png", "Patreon tier2.png", "Patreon tier3.png", "Patreon tier1.png"),
                (DiscordTabView.PatreonBadgeFile(1), DiscordTabView.PatreonBadgeFile(2), DiscordTabView.PatreonBadgeFile(3), DiscordTabView.PatreonBadgeFile(9)));
        }
        finally
        {
            (s.OfflineMode, s.UserDisplayName, s.PatreonTier) = old;
            CoreSettings.SaveImmediate();
        }
    }

    [Fact]
    public void TrainerCard_OwnNameFallsThroughTheProviders_BeforeYou()
    {
        var s = CoreSettings.Current;
        var oldName = s.UserDisplayName;
        var discord = AccountSeed.Discord;
        var oldCustom = discord?.CustomDisplayName;
        try
        {
            s.UserDisplayName = "unified";
            Assert.Equal("unified", DiscordTabView.OwnCardName());

            s.UserDisplayName = null;
            if (discord != null)
            {
                discord.CustomDisplayName = "from_discord";
                Assert.Equal("from_discord", DiscordTabView.OwnCardName());
                discord.CustomDisplayName = null;
            }
            Assert.Equal(AccountSeed.Patreon?.DisplayName ?? "You", DiscordTabView.OwnCardName());
        }
        finally
        {
            s.UserDisplayName = oldName;
            if (discord != null) discord.CustomDisplayName = oldCustom;
            CoreSettings.SaveImmediate();
        }
    }

    [Fact]
    public void Showcase_NextUpNamesTheFirstLockedFreeAchievement()
    {
        var open = Achievement.All.Values.Where(a => !a.IsHidden && !a.IsExclusive).ToList();
        Assert.Equal(CoreMods.MakeModAware(open[0].Name), MainShellWindow.FindNextAchievementName(new HashSet<string>()));
        Assert.Equal(CoreMods.MakeModAware(open[1].Name), MainShellWindow.FindNextAchievementName(new HashSet<string> { open[0].Id }));
        Assert.Null(MainShellWindow.FindNextAchievementName(open.Select(a => a.Id).ToHashSet()));
    }

    [Fact]
    public async Task AchievementsPatronLock_VisitPatreonOpensThePatreonPage()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureHeadlessApp();
            var tab = new AchievementsTabView();
            var asked = new List<string>();
            tab.OpenLink = url => { asked.Add(url); return Task.FromResult(true); };
            var button = tab.FindControl<Button>("BtnVisitPatreon")!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
            Assert.Equal(new[] { "https://www.patreon.com/CodeBambi" }, asked);
            // The lock title glows from a BoxShadow line, never an Effect.
            Assert.All(tab.FindControl<Border>("PatronAchievementsOverlay")!.GetLogicalDescendants().OfType<global::Avalonia.Visual>(),
                v => Assert.Null(v.Effect));
        });
    }
}
