using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using ConditioningControlPanel.Controls.Depth;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Depth wave 10, lane E: the Quests tab and the Programs task cards wear the shared lamp. The
/// seats and the weekly card FLOAT over a SUNKEN well, the bars are grooves with a TUBE fill,
/// the counter badge and the reward chip are RAISED coins, the two action buttons are planks
/// that press, and a finished quest DROPS (no float band, ActiveSinkPx down, pressed bevel) and
/// the weekly takes the DONE stamp.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class QuestDepthTests
{
    private static void Realize(FrameworkElement element, double width, double height)
    {
        var host = new Grid { Width = width, Height = height };
        host.Children.Add(element);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(new Point(0, 0), new Size(width, height)));
        host.UpdateLayout();
    }

    private static object Res(string key) => Application.Current.FindResource(key);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    private static double TravelY(UIElement e) => (e.RenderTransform as TranslateTransform)?.Y ?? 0;

    private static Color FirstStop(Brush b) => ((GradientBrush)b).GradientStops[0].Color;

    [Fact]
    public void EveryElementWearsItsHeight() => WpfRenderHarness.OnStaThread(() =>
    {
        var tab = new QuestsTabView();
        Realize(tab, 1400, 1000);

        // FLOATING: the weekly card's rim and band, a band under every daily seat.
        Assert.Same(Res("DepthFloatRim"), tab.WeeklyQuestCard.BorderBrush);
        Assert.Equal(Visibility.Visible, tab.WeeklyFloatBand.Visibility);
        foreach (var band in new[] { tab.DailySeatBand0, tab.DailySeatBand1, tab.DailySeatBand2 })
            Assert.Equal(Visibility.Visible, band.Visibility);

        // SUNKEN: the list is a well, the weekly bar is a groove.
        Assert.Same(Res("DepthWellFloorBrush"), tab.DailyWell.Background);
        Assert.Same(Res("DepthWellFloorBrush"), tab.WeeklyProgressTrack.Background);
        Assert.Same(Res("DepthWellTop"), tab.WeeklyGrooveTop.Background);
        Assert.Equal(QuestsTabView.GrooveTopPx, tab.WeeklyGrooveTop.Height);
        Assert.True(QuestsTabView.GrooveTopPx < tab.WeeklyProgressTrack.Height,
            "the groove's inner band must leave floor showing under it");

        // TUBE: the shared gloss and bead from Depth.xaml, never a copied gradient.
        Assert.Same(Res("DepthTubeGloss"), tab.WeeklyTubeGloss.Background);
        Assert.Same(Res("DepthTubeBead"), tab.WeeklyTubeBead.Fill);
        Assert.Equal(QuestsTabView.TubeBeadPx, tab.WeeklyTubeBead.Width);
        Assert.True(QuestsTabView.TubeBeadPx < tab.WeeklyProgressTrack.Height);

        // RAISED: the counter badge and the reward chip.
        Assert.Same(Res("DepthRaisedBevel"), tab.DailyQuestCounterBadge.BorderBrush);
        Assert.Same(Res("DepthRaisedBevel"), tab.WeeklyXpChip.BorderBrush);
        Assert.Equal(DepthRules.RaisedPx, tab.DailyCounterDrop.Margin.Top);
        Assert.Equal(DepthRules.RaisedPx, tab.WeeklyXpDrop.Margin.Top);

        // Pixel budget: the groove keeps the 10 px the fill tween measures against.
        Assert.Equal(10, tab.WeeklyProgressTrack.ActualHeight);
    });

    [Fact]
    public void TheBeadRidesOnlyAFillWiderThanItself() => WpfRenderHarness.OnStaThread(() =>
    {
        var tab = new QuestsTabView();
        Realize(tab, 1400, 1000);
        Assert.Equal(Visibility.Collapsed, tab.WeeklyTubeBead.Visibility);

        tab.WeeklyProgressFill.Width = 120;
        tab.UpdateLayout();
        Assert.Equal(Visibility.Visible, tab.WeeklyTubeBead.Visibility);

        tab.WeeklyProgressFill.Width = 4;
        tab.UpdateLayout();
        Assert.Equal(Visibility.Collapsed, tab.WeeklyTubeBead.Visibility);
    });

    [Fact]
    public void AFinishedQuestDropsAndTakesTheStamp() => WpfRenderHarness.OnStaThread(() =>
    {
        var tab = new QuestsTabView();
        Realize(tab, 1400, 1000);
        Assert.Equal(0, TravelY(tab.WeeklySeat));

        tab.WeeklyCompletedOverlay.Visibility = Visibility.Visible;
        Assert.Equal(DepthRules.ActiveSinkPx, TravelY(tab.WeeklySeat));
        Assert.NotEqual(Visibility.Visible, tab.WeeklyFloatBand.Visibility);
        Assert.Same(Res("DepthPressedBevel"), tab.WeeklyQuestCard.BorderBrush);

        // The stamp: rotated -6, mint, static.
        var rotate = Assert.IsType<RotateTransform>(tab.WeeklyDoneStamp.RenderTransform);
        Assert.Equal(-6, rotate.Angle);
        Assert.Contains(tab.WeeklyDoneStamp.Children.OfType<Border>(),
            b => b.BorderBrush is SolidColorBrush s && s.Color == Color.FromRgb(0x00, 0xE6, 0x76));

        tab.WeeklyCompletedOverlay.Visibility = Visibility.Collapsed;
        Assert.Equal(0, TravelY(tab.WeeklySeat));
        Assert.Equal(Visibility.Visible, tab.WeeklyFloatBand.Visibility);
        Assert.Same(Res("DepthFloatRim"), tab.WeeklyQuestCard.BorderBrush);

        // A daily seat drops the same way when its card stamps itself.
        tab.DailyCard1.CompletedOverlay.Visibility = Visibility.Visible;
        Assert.Equal(DepthRules.ActiveSinkPx, TravelY(tab.DailySeat1));
        Assert.NotEqual(Visibility.Visible, tab.DailySeatBand1.Visibility);
        Assert.Equal(0, TravelY(tab.DailySeat0));
    });

    [Fact]
    public void ThePlanksFollowTheTravelRule() => WpfRenderHarness.OnStaThread(() =>
    {
        var tab = new QuestsTabView();
        Realize(tab, 1400, 1000);

        // At rest: raised, its drop band RaisedPx down, the raised bevel.
        Assert.True(tab.BtnRerollWeekly.IsEnabled);
        Assert.Equal(DepthRules.ShadowFor(true, false, false, false), tab.WeeklyRerollDrop.Margin.Top);
        Assert.Equal(1, tab.WeeklyRerollDrop.Opacity);
        Assert.Same(Res("DepthRaisedBevel"), tab.WeeklyRerollBevel.BorderBrush);
        Assert.Equal(DepthRules.TravelFor(true, false, false, false), TravelY(tab.WeeklyRerollFace));

        // Disabled (the weekly is done): flat, no shadow, never moves.
        tab.BtnRerollWeekly.IsEnabled = false;
        Assert.Equal(0, tab.WeeklyRerollDrop.Opacity);
        Assert.Equal(0, TravelY(tab.WeeklyRerollFace));

        // The Fix day plank's shadow and bevel follow the button out of sight.
        Assert.Equal(Visibility.Collapsed, tab.BtnFixStreak.Visibility);
        Assert.Equal(Visibility.Collapsed, tab.FixStreakDrop.Visibility);
        Assert.Equal(Visibility.Collapsed, tab.FixStreakBevel.Visibility);
    });

    [Fact]
    public void ShadowsTakeTheSectionHue() => WpfRenderHarness.OnStaThread(() =>
    {
        var tab = new QuestsTabView();

        // Built in the You hue before the window wires the live one.
        Assert.Equal(DepthRules.ShadowColor(NavStripRules.Accent("you")), FirstStop(tab.DailyCounterDrop.Background));

        var sky = NavStripRules.Accent("social");
        tab.PaintDepthQuests(sky);
        foreach (var drop in new[] { tab.DailyCounterDrop, tab.WeeklyXpDrop, tab.WeeklyRerollDrop, tab.FixStreakDrop })
            Assert.Equal(DepthRules.ShadowColor(sky), FirstStop(drop.Background));
        foreach (var band in new[] { tab.DailySeatBand0, tab.DailySeatBand1, tab.DailySeatBand2, tab.WeeklyFloatBand })
            Assert.Equal(DepthRules.ShadowColor(sky, DepthRules.FloatAlpha), FirstStop(band.Background));
        Assert.Equal(0, ((GradientBrush)tab.WeeklyFloatBand.Background).GradientStops[1].Color.A);
    });

    [Fact]
    public void ProgramTaskCardsFloatAndTheirBarsAreTubes() => WpfRenderHarness.OnStaThread(() =>
    {
        var tab = new ProgramsTabView();
        var card = new ContentPresenter
        {
            ContentTemplate = tab.TodayTaskList.ItemTemplate,
            Content = new ProgramTaskItem { TaskId = "t", Description = "Pop 50 bubbles", BarVisibility = Visibility.Visible }
        };
        Realize(card, 380, 300);

        var borders = Descendants(card).OfType<Border>().ToList();
        Assert.Contains(borders, b => ReferenceEquals(b.Background, Res("DepthFloatBand")));
        Assert.Contains(borders, b => ReferenceEquals(b.BorderBrush, Res("DepthFloatRim")));
        Assert.Contains(borders, b => ReferenceEquals(b.Background, Res("DepthTubeGloss")));
        Assert.Contains(borders, b => ReferenceEquals(b.Background, Res("DepthWellFloorBrush")));

        // The hero bar: the same tube, the bead hidden while the bar is empty.
        var bar = new ProgressBar { Style = (Style)tab.FindResource("ProgramHeroBar"), Width = 300, Value = 0 };
        Realize(bar, 300, 16);
        var bead = Descendants(bar).OfType<Ellipse>().Single();
        Assert.Same(Res("DepthTubeBead"), bead.Fill);
        Assert.Equal(Visibility.Collapsed, bead.Visibility);
        bar.Value = 40;
        bar.UpdateLayout();
        Assert.Equal(Visibility.Visible, bead.Visibility);
    });
}
