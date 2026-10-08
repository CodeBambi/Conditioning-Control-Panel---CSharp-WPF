using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The ring the desk opens: Core EmiRingLayout places it, cards wear their art, it sounds.</summary>
public sealed class EmiRingFanTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task DeskOpenedRingFitsTheWorkAreaWearsArtAndSounds() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var played = new List<string>();
        var oldProvider = CoreAudio.PlayOneShotProvider;
        int oldVolume = CoreSettings.Current.MasterVolume;
        var clock = new SteppedClock();
        var st = EmiState.Current;
        var pins = st.Pins.ToList();
        int streak = st.RingIgnoreStreak, opens = st.RingOpens;
        try
        {
            CoreAudio.PlayOneShotProvider = (path, _, tag, _, _) => played.Add(tag + ":" + System.IO.Path.GetFileName(path));
            CoreSettings.Current.MasterVolume = 50;
            EmiSfx.Clock = clock;
            st.Pins.Clear();

            var desk = new EmiDeskWindow();
            desk.Show();
            desk.ToggleRing();                     // the user's gesture: the desk opens the ring
            Dispatcher.UIThread.RunJobs();
            Assert.True(desk.RingOpen);
            Assert.Equal(new[] { "emi-sfx-ring:cards_in.mp3" }, played);

            // Park her in the bottom-right corner of the work area: the old fixed-radius circle
            // put half the fan off screen there; the solver keeps every card inside.
            var ring = (EmiRingWindow)typeof(EmiDeskWindow)
                .GetField("_ring", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(desk)!;
            var work = (ring.Screens?.Primary?.WorkingArea) ?? new PixelRect(0, 0, 1920, 1080);
            ring.SetWidgetGeometry(new PixelRect(work.Right - 230, work.Bottom - 240, 220, 230),
                                   new PixelPoint(work.Right - 120, work.Bottom - 125));
            ring.Relayout();
            Assert.NotEmpty(ring.Cards);
            foreach (var card in ring.Cards)
            {
                double x = ring.Position.X + Canvas.GetLeft(card), y = ring.Position.Y + Canvas.GetTop(card);
                Assert.True(x >= work.X && y >= work.Y && x + EmiRingWindow.CardW <= work.Right && y + EmiRingWindow.CardH <= work.Bottom,
                            $"card at {x},{y} leaves work area {work}");
            }

            // Loom's card wears features/loom.png, not the flat hue tile.
            Assert.Contains(ring.Cards, c => c.GetLogicalDescendants().OfType<Image>().Any(i => i.Source != null));

            clock.Now += TimeSpan.TicksPerSecond;   // past the 130 ms throttle
            desk.ToggleRing();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("emi-sfx-ring:ui_unequip.mp3", played.Last());
            desk.Close();
        }
        finally
        {
            CoreAudio.PlayOneShotProvider = oldProvider;
            CoreSettings.Current.MasterVolume = oldVolume;
            EmiSfx.Clock = TimeProvider.System;
            st.Pins.Clear(); st.Pins.AddRange(pins);
            st.RingIgnoreStreak = streak; st.RingOpens = opens;
        }
        return Task.CompletedTask;
    });

    private sealed class SteppedClock : TimeProvider
    {
        public long Now = TimeSpan.TicksPerDay;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(Now, TimeSpan.Zero);
    }
}
