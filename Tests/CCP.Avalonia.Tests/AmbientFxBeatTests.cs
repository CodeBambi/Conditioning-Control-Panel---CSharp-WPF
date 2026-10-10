using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using CCP.Avalonia.Tests.Board;
using ConditioningControlPanel.Avalonia.Controls;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Mich's ambient glows ride the shared beat (AGENTS.md EFFECT/CACHE RULE + GPU CACHE + 60 Hz TRAP):
/// the Chaster rail chip's rim glow and Circe's mood meter glow are BoxShadow layers whose Opacity
/// moves, never an Effect, and the chip's breath runs whenever its idle does.
/// </summary>
public sealed class AmbientFxBeatTests
{
    [Fact]
    public Task Chaster_chip_and_mood_meter_glow_without_an_Effect() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        BoardHeadTests.EnsureApp();
        var chip = new ChasterRailChip();
        var meter = new CircesMoodMeter();
        var w = new Window { Width = 200, Height = 400, Content = new StackPanel { Children = { chip, meter } } };
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(chip.GetVisualDescendants().Prepend(chip), v => v.Effect != null);
            Assert.DoesNotContain(meter.GetVisualDescendants().Prepend(meter), v => v.Effect != null);
            Assert.True(meter.GlowLayer.BoxShadow.Count > 0);
            Assert.Equal(chip.Idling, chip.Breathing);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });
}
