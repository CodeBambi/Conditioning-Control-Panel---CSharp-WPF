using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Depth;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Depth;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Nav;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The Quests tab's depth layer (WPF QuestsTabView.xaml depth wave 10 + PaintDepthQuests): the
/// shadows wear the section hue through DepthRules, a finished seat drops in, a plank presses by the
/// rule's travel, and nothing in the layer is an Effect. The look is owed a desk run.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class QuestsDepthTests
{
    private static void OnTab(Action<QuestsTabView, Window> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.MotionLevel = MotionLevel.Off;   // the travel lands at once: no clock to wait for
        var tab = new QuestsTabView();
        var w = new Window { Width = 1200, Height = 900, Content = tab };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            body(tab, w);
        }
        finally { w.Close(); CoreSettings.ServiceProvider = oldProvider; }
    });

    private static Color Contact(Border band) => ((IGradientBrush)band.Background!).GradientStops[0].Color;

    [Fact]
    public void TheShadowsWearTheSectionHue_NeverAHandPickedShade() => OnTab((tab, _) =>
    {
        uint you = NavStripRules.Accent(NavSections.You);
        Assert.Equal(you, tab.DepthHue);                                   // right before the shell ever paints it
        string[] drops = { "DailyCounterDrop", "WeeklyXpDrop", "WeeklyRerollDrop", "FixStreakDrop" };
        string[] floats = { "DailySeatBand0", "DailySeatBand1", "DailySeatBand2", "WeeklyFloatBand" };
        Assert.All(drops, n => Assert.Equal(DepthPaint.ToColor(DepthRules.ShadowColor(you)), Contact(tab.FindControl<Border>(n)!)));
        Assert.All(floats, n => Assert.Equal(DepthPaint.ToColor(DepthRules.ShadowColor(you, DepthRules.FloatAlpha)), Contact(tab.FindControl<Border>(n)!)));

        uint other = NavStripRules.Accent(NavSections.Social);
        Assert.NotEqual(you, other);
        tab.PaintDepthQuests(other);
        Assert.All(drops, n => Assert.Equal(DepthPaint.ToColor(DepthRules.ShadowColor(other)), Contact(tab.FindControl<Border>(n)!)));
        Assert.All(floats, n => Assert.Equal(DepthPaint.ToColor(DepthRules.ShadowColor(other, DepthRules.FloatAlpha)), Contact(tab.FindControl<Border>(n)!)));
        // a band fades to nothing below the contact edge
        Assert.All(drops.Concat(floats), n => Assert.Equal(0, ((IGradientBrush)tab.FindControl<Border>(n)!.Background!).GradientStops[1].Color.A));

        // no Effect in the depth layer: bevels are borders, shadows are bands
        foreach (var name in new[] { "DailyWell", "WeeklyRerollFace", "FixStreakFace", "WeeklyXpChip", "DailyQuestCounterBadge" })
        {
            var part = tab.FindControl<Control>(name)!;
            Assert.Null(part.Effect);
            if (name != "DailyWell") Assert.All(part.GetVisualDescendants(), v => Assert.Null(v.Effect));
        }
        Assert.All(new[] { "DailySeat0", "DailySeat1", "DailySeat2", "WeeklySeat" }, n => Assert.Null(tab.FindControl<Panel>(n)!.Effect));
    });

    [Fact]
    public void AFinishedSeatDropsIntoTheWell_AndComesBackUp() => OnTab((tab, _) =>
    {
        var seat = tab.FindControl<Panel>("WeeklySeat")!;
        var band = tab.FindControl<Border>("WeeklyFloatBand")!;
        var card = tab.FindControl<Border>("WeeklyQuestCard")!;
        var overlay = tab.FindControl<Border>("WeeklyCompletedOverlay")!;
        overlay.IsVisible = false;
        Assert.Equal(0, ((TranslateTransform)seat.RenderTransform!).Y);
        Assert.Equal(1, band.Opacity);
        var floatRim = card.BorderBrush;
        Assert.NotNull(floatRim);

        overlay.IsVisible = true;                                          // ON IS PRESSED IN
        Assert.Equal(DepthRules.ActiveSinkPx, ((TranslateTransform)seat.RenderTransform!).Y);
        Assert.Equal(0, band.Opacity);
        Assert.NotSame(floatRim, card.BorderBrush);
        overlay.IsVisible = false;
        Assert.Equal(0, ((TranslateTransform)seat.RenderTransform!).Y);
        Assert.Equal(1, band.Opacity);

        var daily = tab.FindControl<Panel>("DailySeat1")!;
        var done = tab.FindControl<ConditioningControlPanel.Avalonia.Views.Controls.DailyQuestCard>("DailyCard1")!.FindControl<Border>("CompletedOverlay")!;
        done.IsVisible = true;
        Assert.Equal(DepthRules.ActiveSinkPx, ((TranslateTransform)daily.RenderTransform!).Y);
        Assert.Equal(0, tab.FindControl<Border>("DailySeatBand1")!.Opacity);
        done.IsVisible = false;
        Assert.Equal(0, ((TranslateTransform)daily.RenderTransform!).Y);
    });

    [Fact]
    public void ThePlankPressesByTheRulesTravel_AndItsShadowFollows() => OnTab((tab, w) =>
    {
        var button = tab.FindControl<Button>("BtnRerollWeekly")!;
        var drop = tab.FindControl<Border>("WeeklyRerollDrop")!;
        button.IsEnabled = true;
        button.BringIntoView();
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var at = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), w)!.Value;
        w.MouseMove(new Point(2, 2));
        Assert.Equal(DepthRules.TravelFor(true, false, false, false), tab.RerollFaceY);
        double rest = DepthRules.ShadowFor(true, false, false, false);
        Assert.Equal(new Thickness(1, rest, 1, -rest), drop.Margin);

        w.MouseMove(at);                                                   // hover lifts
        Assert.True(button.IsPointerOver);
        Assert.Equal(DepthRules.TravelFor(true, false, false, true), tab.RerollFaceY);
        w.MouseDown(at, global::Avalonia.Input.MouseButton.Left);          // press drops, the shadow shortens
        Assert.True(button.IsPressed);
        Assert.Equal(DepthRules.TravelFor(true, true, false, true), tab.RerollFaceY);
        double down = DepthRules.ShadowFor(true, true, false, true);
        Assert.Equal(new Thickness(1, down, 1, -down), drop.Margin);
        Assert.Equal(down > 0 ? 1 : 0, drop.Opacity);
        w.MouseMove(new Point(2, 2));                                      // released off the plank: nothing rerolls
        w.MouseUp(new Point(2, 2), global::Avalonia.Input.MouseButton.Left);
        Assert.False(button.IsPressed);
        Assert.Equal(DepthRules.TravelFor(true, false, false, false), tab.RerollFaceY);
    });

    [Fact]
    public void TheShellRepaintsTheTabWithEachSectionsHue() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.Welcomed = true;
        CoreSettings.Current.HasAcceptedAgeVerification = true;
        CoreSettings.Current.PerformanceMode = true;                       // no Forever loops under RunJobs
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var tab = shell.FindControl<QuestsTabView>("QuestsTab")!;
            foreach (var key in new[] { "quests", "settings", "quests" })
            {
                shell.ShowTab(key);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(NavStripRules.Accent(NavSections.SectionForTab(key)), tab.DepthHue);
            }
        }
        finally { shell.Close(); CoreSettings.ServiceProvider = oldProvider; }
    });
}
