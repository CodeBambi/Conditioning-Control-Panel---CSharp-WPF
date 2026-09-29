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
                engine.CheckLevelAchievements(10);
                Dispatcher.UIThread.RunJobs();

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

                view.SelectFilter(AchievementsTabView.FilterUnlocked);
                Assert.Equal(new[] { "plastic_initiation" }, view.Cards.Where(c => c.IsVisible).Select(c => (string)c.Tag!));
                view.SelectFilter(AchievementsTabView.FilterLocked);
                Assert.Equal(visible.Length - 1, view.Cards.Count(c => c.IsVisible));
                Assert.False(Card("plastic_initiation").IsVisible);
                Assert.Single(view.FindControl<WrapPanel>("AchievementFilters")!.Children.OfType<ToggleButton>(), c => c.IsChecked == true);
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
