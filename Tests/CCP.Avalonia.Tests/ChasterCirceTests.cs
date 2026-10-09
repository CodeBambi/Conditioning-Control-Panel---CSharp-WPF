using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Circe's lines, her mood meter and the chip's pulse/pip, from a real booking on a fake
/// Chaster (ChasterBillTests.Run), on a stepped clock.</summary>
public sealed class ChasterCirceTests
{

    private static HashSet<string> Lines(CirceMoment m) =>
        Enumerable.Range(1, CirceLines.Variants(m)).Select(v => Loc.Get(CirceLines.Key(m, v))).ToHashSet();

    [Fact]
    public Task APopSpeaksBesideThePadlockThenOnThePageAndTheMeterRises() => AvaloniaTestDispatcher.RunAsync(() => ChasterBillTests.Run(async (chaster, shell) =>
    {
        var oldMotion = CoreSettings.Current.MotionLevel;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Off;   // the hush is immediate, no fade to wait for
            CoreSettings.Current.ChasterPrices = new List<string> { NatashasFavourite.EventId, TabDayEnd.HeatId };
            var now = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
            shell.InitializeChasterFlash(chaster);
            shell.CircePicker = new CirceLines(new Random(7));
            shell.CirceNow = () => now;
            var rail = shell.Named<ChasterRailChip>("ChasterRail")!;
            var layer = AdornerLayer.GetAdornerLayer(rail)!;

            Assert.True(chaster.Note(NatashasFavourite.EventId).Booked);
            Dispatcher.UIThread.RunJobs();
            var bubble = layer.Children.OfType<CirceSaysAdorner>().Single().Bubble;
            Assert.True(bubble.IsVisible);
            Assert.Contains(bubble.Text, Lines(CirceMoment.Popped));
            Assert.True(bubble.Hold.IsEnabled);
            var first = bubble.Text;

            now = now.AddSeconds(5);                             // inside the throttle: no new line
            chaster.Note(NatashasFavourite.EventId);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(first, bubble.Text);

            bubble.Hush();                                       // what the hold's tick does
            Assert.Empty(layer.Children.OfType<CirceSaysAdorner>());

            // The tab open: the line lands on the page, not beside the padlock; the meter shows her mood.
            ChasterBillTests.Click(shell, rail);
            var tab = shell.Named<ChasterTabView>("ChasterTab")!;
            Assert.True(tab.IsVisible);
            now = now.AddSeconds(CirceLines.ThrottleSeconds + 1);
            chaster.Note(NatashasFavourite.EventId);
            Dispatcher.UIThread.RunJobs();
            var page = tab.FindControl<CirceSays>("PageSays")!;
            Assert.True(page.IsVisible);
            Assert.Contains(page.Text, Lines(CirceMoment.Popped));
            Assert.Empty(layer.Children.OfType<CirceSaysAdorner>());

            var meter = tab.FindControl<CircesMoodMeter>("MoodMeter")!;
            var mood = chaster.Mood!.Value;
            Assert.True(meter.IsVisible);
            Assert.Equal(Loc.Get(mood.WordKey), meter.Word);
            Assert.Equal(CircesMoodMeter.ColourOf(mood.Level), meter.WordColour);
            Assert.Equal(CircesMoodMeter.CoverHeight(mood), meter.Cover.Height, 3);
            Assert.True(meter.Cover.Height < CircesMoodMeter.TrackHeight);  // three pops warmed her
            Assert.Equal(mood.Level != MoodLevel.Calm, rail.MoodPipShown);

            CoreSettings.Current.ChasterPrices = new List<string> { NatashasFavourite.EventId };  // heat off: no meter
            tab.OnTabShown();
            Assert.False(meter.IsVisible);
        }
        finally { CoreSettings.Current.MotionLevel = oldMotion; }
        await Task.CompletedTask;
    }));
    /// <summary>WPF Pulse: a booking tints the ring in the figure's colour (the tween is not
    /// stepped: Avalonia 12 keeps the animation clock internal); with motion Off it never tints.
    /// </summary>
    [Fact]
    public Task ABookingPulsesTheRingInTheFiguresColour() => AvaloniaTestDispatcher.RunAsync(() => ChasterBillTests.Run(async (chaster, shell) =>
    {
        var oldMotion = CoreSettings.Current.MotionLevel;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            shell.InitializeChasterFlash(chaster);
            var rail = shell.Named<ChasterRailChip>("ChasterRail")!;
            Assert.Null(rail.LastPulse);
            chaster.Note("attention");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Color.FromUInt32(BookedFlashPlan.AddColour), rail.LastPulse);
            Assert.Equal(rail.Idling, rail.Breathing);   // once rendered, the breath runs whenever the idle does

            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            rail.Pulse(Colors.Red);
            Assert.Equal(Color.FromUInt32(BookedFlashPlan.AddColour), rail.LastPulse);

        }
        finally { CoreSettings.Current.MotionLevel = oldMotion; }
        await Task.CompletedTask;
    }));
}
