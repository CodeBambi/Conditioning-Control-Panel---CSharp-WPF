using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaster;
using Xunit;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace CCP.Avalonia.Tests;

/// <summary>Circe's Tab, the motion (WPF ChasterTabView.Fx.cs and LockTitle.cs): what a headless test can
/// see. A loop starts and stops with visibility and the motion level, a burst removes itself, opacity
/// stays inside 0..1, nothing on the page wears an Effect. The look itself is owed a desk run.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class ChasterTabFxTests
{
    private static void Motion(MotionLevel level)
    {
        CoreSettings.Current.MotionLevel = level;
        CoreSettings.Current.PerformanceMode = false;
        Env.RaiseMotionGateChanged();
    }

    /// <summary>A linked tab in its own shown window, at the given motion level, after OnTabShown.</summary>
    private static Task OnTab(MotionLevel level, Action<ChasterTabView, Window, ChasterService> body) =>
        AvaloniaTestDispatcher.RunAsync(() => ChasterBillTests.Run(async (chaster, _) =>
        {
            Motion(level);
            FxTrack.ManualClock = true;   // every finite beat is stepped by the test, never by the wall clock
            var tab = new ChasterTabView();
            var w = new Window { Width = 1200, Height = 1000, Content = tab };
            w.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                tab.OnTabShown();
                Dispatcher.UIThread.RunJobs();
                w.UpdateLayout();
                body(tab, w, chaster);
            }
            finally { tab.FxFinish(); tab.FindControl<LockTitle>("HeroTitle")!.EntryRun?.Finish(); w.Close(); FxTrack.ManualClock = false; Motion(MotionLevel.Full); }
            await Task.CompletedTask;
        }));

    // ---------------------------------------------------------------- the title

    [Fact]
    public void TheEntryDropsEachGlyphInOnTheThudAndEndsAtRest()
    {
        // before its turn a glyph is invisible above the line
        var first = LockTitle.EntryPose(0, 0, false);
        Assert.Equal(0, first.Opacity);
        Assert.Equal(LockTitle.DropFromPx, first.DropY, 6);
        Assert.Equal(0, LockTitle.EntryPose(79, 2, true).Opacity);          // third in the order: 80 ms later
        Assert.InRange(LockTitle.EntryPose(80 + 35, 2, true).Opacity, 0.4, 0.6);

        // the thud overshoots the line a little before it settles (BackEase out)
        Assert.Contains(Enumerable.Range(0, 34).Select(i => LockTitle.EntryPose(i * 10, 0, false).DropY), y => y > 0.5);
        // the squash: flat and wide on landing, then one small rebound
        double land = LockTitle.DropMs * 0.55;
        var hit = LockTitle.EntryPose(land + (LockTitle.SquashMs * 0.3), 0, false);
        Assert.Equal(0.82, hit.ScaleY, 3);
        Assert.Equal(1.12, hit.ScaleX, 3);
        var rebound = LockTitle.EntryPose(land + (LockTitle.SquashMs * 0.7), 0, false);
        Assert.Equal(1.05, rebound.ScaleY, 3);
        // a padlock swings from its shackle: 14 degrees out at the first beat, less each time, letters never
        Assert.Equal(14, LockTitle.EntryPose(land + (LockTitle.SwingMs * 0.125), 0, true).Swing, 3);
        Assert.Equal(-14 * 0.55, LockTitle.EntryPose(land + (LockTitle.SwingMs * 0.375), 0, true).Swing, 3);
        Assert.Equal(0, LockTitle.EntryPose(land + (LockTitle.SwingMs * 0.125), 0, false).Swing);

        // every sampled pose: opacity inside 0..1; the end: at rest
        for (double ms = 0; ms <= LockTitle.EntryMs(9); ms += 7)
            for (int i = 0; i < 9; i++) Assert.InRange(LockTitle.EntryPose(ms, i, i > 5).Opacity, 0, 1);
        var end = LockTitle.EntryPose(LockTitle.EntryMs(9), 8, true);
        Assert.Equal((1, 0, 1, 1), (end.Opacity, Math.Round(end.DropY, 6), Math.Round(end.ScaleX, 6), Math.Round(end.ScaleY, 6)));
        Assert.Equal(0, end.Swing, 6);
    }

    [Fact]
    public void TheIdleWobblesSwaysAndBreathesInsideItsNumbers()
    {
        for (double t = 0; t < 30; t += 0.11)
            for (int i = 0; i < 6; i++)
            {
                var pose = LockTitle.IdlePose(t, i, padlock: true);
                Assert.InRange(pose.Wobble, -LockTitle.WobbleDegrees - 1e-9, LockTitle.WobbleDegrees + 1e-9);
                Assert.InRange(pose.Sway, -LockTitle.SwayPx - 1e-9, LockTitle.SwayPx + 1e-9);
                Assert.InRange(pose.Breath, 1, LockTitle.BreathTo + 1e-9);
            }
        Assert.Equal(1, LockTitle.IdlePose(5, 0, padlock: false).Breath);                // letters never breathe
        Assert.Equal(LockTitle.WobbleDegrees, LockTitle.IdlePose(LockTitle.WobbleSeconds, 0, true).Wobble, 6);
        Assert.Equal(0, LockTitle.IdlePose(0.3, 1, true).Wobble);                         // the second glyph starts 370 ms later
        Assert.Equal(LockTitle.BreathTo, LockTitle.IdlePose(LockTitle.BreathSeconds, 0, true).Breath, 6);
    }

    [Fact]
    public Task TheTitlesIdleRunsOnlyWhileShownAtFullMotion_AndTheEntryLandsEveryGlyph() => OnTab(MotionLevel.Full, (tab, w, _) =>
    {
        var title = tab.FindControl<LockTitle>("HeroTitle")!;
        Assert.True(title.IsVisible);
        Assert.True(title.PadlockCount > 0);
        Assert.True(title.IsIdling);
        Assert.True(title.IsEntering);                                                   // OnTabShown played the entry
        Assert.Contains(title.Glyphs, g => g.Opacity < 1);
        title.PlayEntry();                                                                // a second door: ignored
        var run = title.EntryRun!;
        run.Seek(run.LengthMs / 2);
        Assert.All(title.Glyphs, g => Assert.InRange(g.Opacity, 0, 1));
        run.Seek(run.LengthMs);
        Assert.False(title.IsEntering);
        Assert.All(title.Glyphs, g => Assert.Equal(1, g.Opacity));
        Assert.All(title.GetSelfAndVisualDescendants(), v => Assert.Null(v.Effect));

        tab.IsVisible = false;                                                            // the page hid: parked at rest
        Assert.False(title.IsIdling);
        Assert.All(title.Padlocks, p => Assert.All(((TransformGroup)p.RenderTransform!).Children.OfType<ScaleTransform>(), s => Assert.Equal(1, s.ScaleX)));
        tab.IsVisible = true;
        tab.OnTabShown();
        Dispatcher.UIThread.RunJobs();
        Assert.True(title.IsIdling);

        Motion(MotionLevel.Reduced);                                                      // decoration parks below Full
        Assert.False(title.IsIdling);
        Motion(MotionLevel.Full);
        Assert.True(title.IsIdling);
    });

    // ---------------------------------------------------------------- the ground

    [Fact]
    public Task TheGroundLoopsStartAndStopWithThePageAndTheMotionLevel() => OnTab(MotionLevel.Full, (tab, w, _) =>
    {
        Assert.True(tab.AmbientBeatRunning);
        Assert.NotNull(tab.FindControl<Path>("SpiralPath")!.Data);
        Assert.Same(tab.SpiralTurn, tab.FindControl<Panel>("SpiralLayer")!.RenderTransform);
        Assert.True(tab.HeroSheenOn);
        Assert.All(tab.GetSelfAndVisualDescendants(), v => Assert.Null(v.Effect));        // glows are BoxShadows here

        tab.IsVisible = false;
        Assert.False(tab.AmbientBeatRunning);
        Assert.False(tab.HeroSheenOn);
        Assert.Equal(1, tab.HeroArtScale.ScaleX);
        Assert.Equal((ChasterTabView.TagSwingFrom + ChasterTabView.TagSwingTo) / 2, tab.TagSwing.Angle);
        Assert.Equal(0, tab.LiveFxRuns);

        tab.IsVisible = true;
        tab.OnTabShown();
        Dispatcher.UIThread.RunJobs();
        Assert.True(tab.AmbientBeatRunning);
        Assert.True(tab.HeroSheenOn);

        Motion(MotionLevel.Reduced);
        Assert.False(tab.AmbientBeatRunning);
        Assert.False(tab.HeroSheenOn);
        Motion(MotionLevel.Full);
        Assert.True(tab.AmbientBeatRunning);
        Assert.True(tab.HeroSheenOn);
    });

    [Fact]
    public Task TheKeysWearABreathingGlowOnlyWhileNothingCounts_AndItDropsWithThePage() => OnTab(MotionLevel.Full, (tab, w, chaster) =>
    {
        var glow = tab.KeyGlow!;
        Assert.False(tab.NudgingKeys);
        Assert.False(glow.IsVisible);

        CoreSettings.Current.ChasterPrices = new List<string>();                          // tab on, nothing switched on
        tab.RefreshHero();
        if (!tab.NudgingKeys) return;                                                     // the fake lock is still loading: nothing to nudge yet
        Assert.True(glow.IsVisible);
        Assert.All(glow.Children.OfType<Border>(), b =>
        {
            Assert.Equal(ChasterTabView.JackpotColour, b.BoxShadow[0].Color);
            Assert.Equal(Math.Min(26, Env.MaxGlowBlurRadius(Env.CurrentTier)), b.BoxShadow[0].Blur);
        });
        Assert.InRange(glow.Opacity, 0, 1);
        Assert.Null(tab.FindControl<UniformGrid>("PresetRow")!.Effect);

        tab.IsVisible = false;                                                            // a glow drops when its target hides
        Assert.False(glow.IsVisible);
        tab.IsVisible = true;
        tab.OnTabShown();
        Dispatcher.UIThread.RunJobs();
        Assert.True(glow.IsVisible);

        CoreSettings.Current.ChasterPrices = new List<string> { "attention" };
        tab.RefreshHero();
        Assert.False(glow.IsVisible);
    });

    // ---------------------------------------------------------------- the events

    [Fact]
    public Task ABookingThrowsARingAndSparks_AndEveryBeatRemovesItself() => OnTab(MotionLevel.Full, (tab, w, _) =>
    {
        tab.FxFinish();                                                                   // past the arrival stagger
        var canvas = tab.ShockwaveLayer;
        Assert.Empty(canvas.Children);
        tab.FxBooked(60, "attention");
        var rings = canvas.Children.OfType<Ellipse>().ToList();
        Assert.Equal(2, rings.Count);                                                     // the bright ring and its echo
        Assert.Equal(new[] { 2.2, 1.5 }, rings.Select(r => r.StrokeThickness));
        Assert.All(rings, r => Assert.Equal(ChasterTabView.CostColour, ((ISolidColorBrush)r.Stroke!).Color));
        Assert.All(rings, r => Assert.InRange(r.Opacity, 0, 1));
        Assert.NotNull(tab.LastBurst);
        Assert.True(tab.LiveFxRuns > 0);
        var balance = tab.FindControl<TextBlock>("TxtBalance")!;
        Assert.NotNull(ChasterTabView.EnsureScale(balance));

        tab.FxFinish();
        Assert.Empty(canvas.Children);                                                    // the OUT: nothing left on the canvas
        Assert.Equal(0, tab.LiveFxRuns);
        Assert.Equal(1, ChasterTabView.EnsureScale(balance)!.ScaleX);

        // an earned minute is in the earn colour, the jackpot in gold
        tab.FxBooked(-60, "attention");
        Assert.Equal(ChasterTabView.EarnColour, ((ISolidColorBrush)canvas.Children.OfType<Ellipse>().First().Stroke!).Color);
        tab.FxFinish();
        tab.FxBooked(600, CircesTab.JackpotEventId);
        Assert.Equal(ChasterTabView.JackpotColour, ((ISolidColorBrush)canvas.Children.OfType<Ellipse>().First().Stroke!).Color);
    });

    [Fact]
    public Task AKeyASwitchAndARowEachAnswerInTheirOwnColour() => OnTab(MotionLevel.Full, (tab, w, _) =>
    {
        tab.FxFinish();
        var strict = tab.FindControl<ToggleButton>("BtnPresetStrict")!;
        ChasterBillTests.Click(w, strict);
        Assert.Equal((strict, ChasterTabView.PresetColour(TabPresets.Strict), 44), (tab.LastBurst!.Value.Anchor, tab.LastBurst.Value.Colour, tab.LastBurst.Value.Count));
        Assert.Equal(2, tab.ShockwaveLayer.Children.OfType<Ellipse>().Count());
        tab.FxFinish();
        Assert.Empty(tab.ShockwaveLayer.Children.OfType<Ellipse>());

        var custom = tab.FindControl<ToggleButton>("BtnPresetCustom")!;                   // the mirror key only answers
        ChasterBillTests.Click(w, custom);
        Assert.Equal(ChasterTabView.CustomColour, tab.LastBurst!.Value.Colour);
        tab.FxFinish();

        var row = tab.RowFor(tab.RowIds.First())!;
        bool was = row.IsChecked == true;
        row.BringIntoView();
        w.UpdateLayout();
        row.IsChecked = !was;
        row.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        if (!was) Assert.Equal((row, 16), (tab.LastBurst!.Value.Anchor, tab.LastBurst.Value.Count));
        Assert.NotNull(ChasterTabView.EnsureScale(row));
        tab.FxFinish();
        Assert.Equal(1, ChasterTabView.EnsureScale(row)!.ScaleX);
    });

    [Fact]
    public Task WithMotionOffNothingMovesAndNothingIsLeftBehind() => OnTab(MotionLevel.Off, (tab, w, _) =>
    {
        Assert.False(tab.AmbientBeatRunning);
        Assert.False(tab.HeroSheenOn);
        Assert.False(tab.FindControl<LockTitle>("HeroTitle")!.IsEntering);
        Assert.Equal(0, tab.LiveFxRuns);
        tab.FxBooked(60, "attention");
        Assert.Empty(tab.ShockwaveLayer.Children);
        Assert.Equal(0, tab.LiveFxRuns);
        Assert.All(new[] { "HeroCard", "StatTab", "StatRun", "CostBoard" }, n => Assert.Equal(1, tab.FindControl<Control>(n)!.Opacity));

        ChasterBillTests.Click(w, tab.FindControl<Border>("StatRun")!);                   // the bill: the end state at once
        var host = tab.FindControl<Border>("ReceiptHost")!;
        Assert.True(host.IsVisible);
        Assert.True(double.IsNaN(host.Height));
        Assert.Equal(180, ((RotateTransform)tab.FindControl<TextBlock>("TxtRunChevron")!.RenderTransform!).Angle);
    });

    // ---------------------------------------------------------------- the numbers

    [Fact]
    public Task TheBillPrintsDownAndRollsBackUp() => OnTab(MotionLevel.Full, (tab, w, _) =>
    {
        tab.FxFinish();
        var host = tab.FindControl<Border>("ReceiptHost")!;
        var chevron = (RotateTransform)tab.FindControl<TextBlock>("TxtRunChevron")!.RenderTransform!;
        ChasterBillTests.Click(w, tab.FindControl<Border>("StatRun")!);
        Assert.True(host.IsVisible);
        Assert.Equal(0, host.Height);                                                     // grows from nothing, clipped
        Assert.InRange(host.Opacity, 0, 1);
        Assert.True(host.ClipToBounds);
        tab.FxFinish();
        Assert.True(double.IsNaN(host.Height));                                           // the bill can change length while open
        Assert.Equal(1, host.Opacity);
        Assert.Equal(0, ((TranslateTransform)host.RenderTransform!).Y);
        Assert.Equal(180, chevron.Angle);
        Assert.InRange(ChasterTabView.EnsureScale(tab.FindControl<Border>("StatRun")!)!.ScaleX, 1, 1.02);   // at rest (lifted while the pointer is still on it)

        w.UpdateLayout();
        ChasterBillTests.Click(w, tab.FindControl<Border>("StatRun")!);
        Assert.True(host.IsVisible);                                                      // still rolling up
        tab.FxFinish();
        Assert.False(host.IsVisible);
        Assert.Equal(1, host.Opacity);
        Assert.Equal(0, chevron.Angle);
    });

    [Fact]
    public Task TheCapMeterBloomsAtItsTipAndGoesDark() => OnTab(MotionLevel.Full, (tab, w, chaster) =>
    {
        tab.FxFinish();
        var bloom = tab.FindControl<Border>("CapBloom")!;
        Assert.Equal(0, bloom.Opacity);
        Assert.True(chaster.Note("attention").Booked);
        tab.RefreshNumbers();
        Assert.Equal(tab.FindControl<Panel>("CapTrack")!.Bounds.Width * TabPageText.CapFraction(chaster.TodayAddedSeconds, chaster.Caps.DailySeconds), bloom.Width, 3);
        Assert.InRange(bloom.Opacity, 0, 1);
        tab.FxFinish();
        Assert.Equal(0, bloom.Opacity);
    });

    // ---------------------------------------------------------------- the calendar

    [Fact]
    public Task TheMarkerDrawsTheCrossesIn_TheSheetFlutters_AndACometRunsTheKeyIntoTonight() => OnTab(MotionLevel.Full, (tab, w, _) =>
    {
        tab.FxFinish();
        var today = new DateTime(2026, 10, 10, 12, 0, 0);
        tab.BuildCalendar((today.AddDays(-3), today.AddDays(3)), today);
        var grid = tab.FindControl<UniformGrid>("Calendar")!;
        var strokes = grid.GetVisualDescendants().OfType<Polyline>().ToList();
        var splats = grid.GetVisualDescendants().OfType<Canvas>().SelectMany(c => c.Children.OfType<Ellipse>()).ToList();
        Assert.Equal(6, strokes.Count);                                                   // three served days, two strokes each
        Assert.All(strokes, s => { Assert.NotNull(s.StrokeDashArray); Assert.True(s.StrokeDashOffset > 0); });   // nothing drawn yet
        Assert.All(splats, s => Assert.Equal(0, s.Opacity));
        tab.FxFinish();
        Assert.All(strokes, s => { Assert.Null(s.StrokeDashArray); Assert.Equal(0, s.StrokeDashOffset); });
        Assert.All(splats, s => Assert.Equal(0.9, s.Opacity));

        w.UpdateLayout();
        tab.FxSheetFlutter();
        Assert.Equal(1, tab.LiveFxRuns);
        tab.FxFinish();
        Assert.Equal(ChasterTabView.SheetTiltDegrees, tab.SheetTilt.Angle);

        tab.CalendarComet();
        var comet = Assert.Single(tab.ShockwaveLayer.Children.OfType<Border>(), b => Equals(b.Tag, "calendar-comet"));
        Assert.Equal(0, comet.Opacity);
        Assert.Null(comet.Effect);
        tab.FxFinish();                                                                   // it lands, pops the ring, and is gone
        tab.FxFinish();
        Assert.Empty(tab.ShockwaveLayer.Children);
    });

    [Fact]
    public Task ReducedMotionGetsTheFinishedCalendar() => OnTab(MotionLevel.Reduced, (tab, w, _) =>
    {
        var today = new DateTime(2026, 10, 10, 12, 0, 0);
        tab.BuildCalendar((today.AddDays(-3), today.AddDays(3)), today);
        Assert.All(tab.FindControl<UniformGrid>("Calendar")!.GetVisualDescendants().OfType<Polyline>(), s => Assert.Null(s.StrokeDashArray));
        tab.CalendarComet();
        Assert.Empty(tab.ShockwaveLayer.Children);
    });

    // ---------------------------------------------------------------- the trailer

    [Fact]
    public Task TheTrailersFigureTicksOnlyWhileTheCardIsUp() => OnTab(MotionLevel.Full, (tab, w, _) =>
    {
        var row = tab.RowFor("attention")!;
        tab.OpenTrailer(row);
        Assert.Equal("attention", tab.TrailerId);
        Assert.True(tab.TrailerFigureTicking);
        tab.HideTrailer();
        Assert.False(tab.TrailerFigureTicking);
        Assert.Equal(1, tab.TrailerArtScale.ScaleX);
        Assert.Equal(0, tab.TrailerArtPan.X);

        tab.OpenTrailer(row);
        tab.IsVisible = false;                                                            // a page that leaves takes its trailer with it
        Assert.False(tab.TrailerFigureTicking);
    });

    // ---------------------------------------------------------------- the tool

    [Fact]
    public void AnFxTrackRunEndsOnItsEndStateOnceAndLeavesItsGroup()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            var group = new List<FxTrack.Run>();
            var seen = new List<double>();
            int done = 0;
            var run = FxTrack.Play(100, seen.Add, () => done++, group);
            Assert.Single(group);
            run.Seek(40);
            run.Seek(250);
            run.Seek(300);
            run.Finish();
            Assert.Equal(new[] { 0d, 40, 100 }, seen);
            Assert.Equal(1, done);
            Assert.True(run.IsDone);
            Assert.Empty(group);
        });
        Assert.Equal(1.06, FxTrack.Pop(0.35, 1.06), 6);
        Assert.Equal(1, FxTrack.Pop(1, 1.06), 6);
        Assert.True(FxTrack.BackOut(0.6, 0.55) > 1);                                      // the thud overshoots
        Assert.Equal(1, FxTrack.BackOut(1, 0.55), 6);
    }
}
