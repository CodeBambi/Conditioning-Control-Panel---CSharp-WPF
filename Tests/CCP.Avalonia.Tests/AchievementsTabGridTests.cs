using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The card grid, unlock refresh and filters (WPF MainWindow.AchievementsTab.cs PopulateAchievementGrid,
/// RefreshAchievementTile, ApplyAchievementFilter), driven by a level-up through the Core engine.</summary>
public sealed class AchievementsTabGridTests
{
    [Fact]
    public async Task GridListsEveryVisibleAchievementAndFollowsALevelUnlock()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var dir = Directory.CreateTempSubdirectory("ccp-ach-grid-").FullName;
            Window? host = null;
            try
            {
                var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
                var view = new AchievementsTabView(engine);
                host = new Window { Width = 1200, Height = 900, Content = view };
                host.Show();
                Dispatcher.UIThread.RunJobs();

                var visible = Achievement.All.Values.Where(a => !a.IsHidden).ToArray();
                Assert.Equal(visible.Where(a => !a.IsExclusive).Select(a => a.Id),
                    view.FindControl<WrapPanel>("AchievementGrid")!.Children.Select(c => (string)((Control)c).Tag!));
                Assert.Equal(visible.Where(a => a.IsExclusive).Select(a => a.Id),
                    view.FindControl<WrapPanel>("PatronAchievementGrid")!.Children.Select(c => (string)((Control)c).Tag!));

                // Level 10 is the first milestone (AchievementRules.LevelMilestones); 20 is the next.
                // One unlock redraws one tile in place: same card objects, one Apply (WPF AchievementsTab.cs:886).
                var before = view.Cards.ToArray();
                var applies = view.TileApplies;
                engine.CheckLevelAchievements(10);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, view.TileApplies - applies);
                Assert.Equal(before, view.Cards.ToArray());

                ToggleButton Card(string id) => view.Cards.Single(c => (string)c.Tag! == id);
                string[] Texts(Control c) => c.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
                var earned = Card("plastic_initiation");
                Assert.Contains(Loc.Get("achievement_plastic_initiation_name"), Texts(earned));
                Assert.Null(earned.GetVisualDescendants().OfType<Image>().First().Effect);
                Assert.DoesNotContain(earned.GetVisualDescendants().OfType<StackPanel>(), p => p.Name == "Meter");
                var locked = Card("dumb_bimbo");
                Assert.Contains(Loc.Get("achv_card_locked_name"), Texts(locked));
                Assert.NotNull(locked.GetVisualDescendants().OfType<Image>().First().Effect);
                Assert.Contains(locked.GetVisualDescendants().OfType<StackPanel>(), p => p.Name == "Meter");
                Assert.Equal(Loc.GetF("label_0_1_achievements_unlocked", 1, engine.GetTotalCount(false)),
                    view.FindControl<TextBlock>("TxtAchievementCount")!.Text);

                // Reward band, automation name and counter off the Core WardrobeCatalog (WPF BuildRewardBand,
                // ApplyAchievementCardTooltip, UpdateRewardCount).
                var rewards = WardrobeCatalog.AchievementRewards();
                Assert.Contains("Bubblegum Scarf", Texts(earned));
                Assert.Equal(Loc.GetF("achv_automation_unlocked", Loc.Get("achievement_plastic_initiation_name"), "Bubblegum Scarf"),
                    global::Avalonia.Automation.AutomationProperties.GetName(earned));
                Assert.NotNull(ConditioningControlPanel.Avalonia.Helpers.ModArt.Wardrobe("bambi_bubblegum_scarf"));
                Assert.Equal(2, earned.GetVisualDescendants().OfType<Image>().Count()); // badge + reward art
                var gatedLocked = view.Cards.First(c => rewards.ContainsKey((string)c.Tag!) && (string)c.Tag! != "plastic_initiation");
                Assert.DoesNotContain(rewards[(string)gatedLocked.Tag!].Name, Texts(gatedLocked));
                var gates = WardrobeCatalog.AchievementGates()!;
                var rewardCount = view.FindControl<TextBlock>("TxtRewardCount")!;
                Assert.True(rewardCount.IsVisible);
                Assert.Equal(Loc.GetF("achv_reward_count", gates.Count(g => g.Value == "plastic_initiation"), gates.Count), rewardCount.Text);
                view.SelectFilter(AchievementsTabView.FilterRewards);
                Assert.Equal(view.Cards.Where(c => rewards.ContainsKey((string)c.Tag!)).Select(c => (string)c.Tag!),
                    view.Cards.Where(c => c.IsVisible).Select(c => (string)c.Tag!));
                Assert.Contains(view.Cards, c => c.IsVisible);

                // The reward toast paints the item's art (WPF ItemUnlockedPopup.LoadItemArt), not the gift fallback.
                var toast = new ConditioningControlPanel.Avalonia.Views.Windows.ItemUnlockedPopup(rewards["plastic_initiation"]);
                Assert.True(toast.FindControl<Image>("ItemImage")!.IsVisible);
                Assert.Equal("Bubblegum Scarf", toast.FindControl<TextBlock>("TxtItemName")!.Text);

                view.SelectFilter(AchievementsTabView.FilterUnlocked);
                Assert.Equal(new[] { "plastic_initiation" }, view.Cards.Where(c => c.IsVisible).Select(c => (string)c.Tag!));
                view.SelectFilter(AchievementsTabView.FilterLocked);
                Assert.Equal(visible.Length - 1, view.Cards.Count(c => c.IsVisible));
                Assert.False(Card("plastic_initiation").IsVisible);
                Assert.Single(view.FindControl<WrapPanel>("AchievementFilters")!.Children.OfType<ToggleButton>(), c => c.IsChecked == true);

                // A burst of unlocks coalesces into one pass over just those tiles.
                applies = view.TileApplies;
                var passes = view.Passes;
                engine.TryUnlock("dumb_bimbo");
                engine.TryUnlock("fully_synthetic");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(2, view.TileApplies - applies);
                Assert.Equal(1, view.Passes - passes);
                Assert.Equal(before, view.Cards.ToArray());

                // Showing the tab refreshes every tile in place (WPF TabNavigation.cs:374), no rebuild.
                view.IsVisible = false;
                applies = view.TileApplies;
                view.IsVisible = true;
                Assert.Equal(visible.Length, view.TileApplies - applies);
                Assert.Equal(before, view.Cards.ToArray());
            }
            finally
            {
                host?.Close();
                Directory.Delete(dir, recursive: true);
            }
            return Task.CompletedTask;
        });
    }
}
