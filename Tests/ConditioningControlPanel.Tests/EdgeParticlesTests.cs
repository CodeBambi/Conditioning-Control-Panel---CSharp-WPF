using System.Linq;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish wave 9, particles lane: the section edge's embers. Four 30 px AmbientFxCanvas
/// strips running only the EdgeDrift layer, nothing allocated under Reduced or Off, a retint
/// reaching all four, and the drift keeping every mote inside its band while it moves clockwise.
/// </summary>
public class EdgeParticlesTests
{
    private static readonly Color Lilac = Color.FromRgb(0xB7, 0x9C, 0xFF);
    private static readonly Color Sky = Color.FromRgb(0x5F, 0xB0, 0xFF);

    private static EdgeParticles Mounted(MotionLevel level, bool tierAllows = true)
    {
        var edge = new EdgeParticles { MotionOverride = level, TierAllowsParticlesOverride = tierAllows };
        edge.Mount(Lilac);
        edge.Measure(new Size(1661, 1002));
        edge.Arrange(new Rect(0, 0, 1661, 1002));
        edge.UpdateLayout();
        return edge;
    }

    [Fact]
    public void FullMotionMountsFourFogStripsWithTheEmbers()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var edge = Mounted(MotionLevel.Full);
            Assert.False(edge.IsHitTestVisible);
            Assert.Equal(4, edge.Strips.Count);
            Assert.Equal(4, edge.Children.Count);
            Assert.Equal(new[] { EdgeSide.Top, EdgeSide.Right, EdgeSide.Bottom, EdgeSide.Left },
                edge.Strips.Select(s => s.EdgeSide).ToArray());

            foreach (var s in edge.Strips)
            {
                Assert.False(s.IsHitTestVisible);
                Assert.Equal(AmbientFxLayers.EdgeFog | AmbientFxLayers.EdgeDrift, s.Layers);
                Assert.Equal(Lilac, s.Tint);
            }

            var top = edge.Strips[0];
            Assert.Equal(56, top.Height);
            Assert.Equal(VerticalAlignment.Top, top.VerticalAlignment);
            Assert.Equal(56, top.ActualHeight);
            Assert.Equal(1661, top.ActualWidth);

            var right = edge.Strips[1];
            Assert.Equal(56, right.Width);
            Assert.Equal(HorizontalAlignment.Right, right.HorizontalAlignment);
            Assert.Equal(1002, right.ActualHeight);

            Assert.Equal(VerticalAlignment.Bottom, edge.Strips[2].VerticalAlignment);
            Assert.Equal(56, edge.Strips[2].Height);
            Assert.Equal(HorizontalAlignment.Left, edge.Strips[3].HorizontalAlignment);
            Assert.Equal(56, edge.Strips[3].Width);
        });
    }

    [Theory]
    [InlineData(MotionLevel.Reduced, false)]
    [InlineData(MotionLevel.Off, true)]
    [InlineData(MotionLevel.Full, false)]
    public void NothingIsAllocatedWithoutParticles(MotionLevel level, bool tierAllows)
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var edge = Mounted(level, tierAllows);
            Assert.Empty(edge.Strips);
            Assert.Equal(0, edge.Children.Count);

            // A retint on an unmounted edge only remembers the hue.
            edge.Retint(Sky);
            Assert.Equal(Sky, edge.Hue);
            Assert.Empty(edge.Strips);
        });
    }

    [Fact]
    public void TheGateIsFullMotionAndATierWithParticles()
    {
        Assert.True(EdgeParticles.ShouldMount(MotionLevel.Full, true));
        Assert.False(EdgeParticles.ShouldMount(MotionLevel.Full, false));
        Assert.False(EdgeParticles.ShouldMount(MotionLevel.Reduced, true));
        Assert.False(EdgeParticles.ShouldMount(MotionLevel.Off, true));
    }

    [Fact]
    public void RefreshTearsDownWhenMotionDropsAndRebuildsWhenItReturns()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var edge = Mounted(MotionLevel.Full);
            Assert.Equal(4, edge.Strips.Count);

            edge.MotionOverride = MotionLevel.Off;
            edge.Refresh();
            Assert.Empty(edge.Strips);
            Assert.Equal(0, edge.Children.Count);

            edge.MotionOverride = MotionLevel.Full;
            edge.Refresh();
            Assert.Equal(4, edge.Strips.Count);
        });
    }

    [Fact]
    public void RetintSwapsTheTintOnAllFourStrips()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var edge = Mounted(MotionLevel.Full);
            edge.Retint(Sky);
            Assert.Equal(Sky, edge.Hue);
            Assert.All(edge.Strips, s => Assert.Equal(Sky, s.Tint));
            Assert.All(edge.Strips, s => Assert.Equal(AmbientFxLayers.EdgeFog | AmbientFxLayers.EdgeDrift, s.Layers));

            // Mounting again with a new hue retints in place rather than rebuilding.
            var first = edge.Strips[0];
            edge.Mount(Lilac);
            Assert.Same(first, edge.Strips[0]);
            Assert.All(edge.Strips, s => Assert.Equal(Lilac, s.Tint));
        });
    }

    [Fact]
    public void TheBudgetIsAThirdOfTheLiveBudgetCappedAtSix()
    {
        Assert.Equal(0, EdgeDriftMath.Target(0));
        Assert.Equal(3, EdgeDriftMath.Target(10));
        Assert.Equal(6, EdgeDriftMath.Target(24));   // Balanced: 7.2 rounds to 7, the cap holds 6
        Assert.Equal(6, EdgeDriftMath.Target(60));   // Quality
        Assert.Equal(1 << 6, (int)AmbientFxLayers.EdgeDrift);
    }

    [Theory]
    [InlineData(EdgeSide.Top, 1, 0)]       // left to right
    [InlineData(EdgeSide.Right, 0, 1)]     // top to bottom
    [InlineData(EdgeSide.Bottom, -1, 0)]   // right to left
    [InlineData(EdgeSide.Left, 0, -1)]     // bottom to top
    public void AlongRunsClockwiseAndDepthStaysInTheStrip(EdgeSide side, int dx, int dy)
    {
        var (x0, y0) = EdgeDriftMath.Position(side, 0.3, 0.5);
        var (x1, y1) = EdgeDriftMath.Position(side, 0.3 + EdgeDriftMath.SpeedMin, 0.5);
        Assert.Equal(dx, System.Math.Sign(System.Math.Round(x1 - x0, 6)));
        Assert.Equal(dy, System.Math.Sign(System.Math.Round(y1 - y0, 6)));

        // Depth 0 is the window's outer edge, so the band hugs that edge on every side.
        var (ox, oy) = EdgeDriftMath.Position(side, 0.5, 0.0);
        switch (side)
        {
            case EdgeSide.Top: Assert.Equal(0.0, oy); break;
            case EdgeSide.Right: Assert.Equal(1.0, ox); break;
            case EdgeSide.Bottom: Assert.Equal(1.0, oy); break;
            case EdgeSide.Left: Assert.Equal(0.0, ox); break;
        }
    }

    [Fact]
    public void AlphaFollowsTheSpec()
    {
        // Mid-life, mid-strip, full flicker, intensity 1: the 0.30 peak.
        Assert.Equal(0.30, EdgeDriftMath.Alpha(0.5, 5, 10, System.Math.PI / 2, 1.0), 6);
        // The flicker floor.
        Assert.Equal(0.30 * 0.85, EdgeDriftMath.Alpha(0.5, 5, 10, -System.Math.PI / 2, 1.0), 6);
        // Birth, death and the strip ends are dark.
        Assert.Equal(0.0, EdgeDriftMath.Alpha(0.5, 10, 10, 0, 1.0), 6);
        Assert.Equal(0.0, EdgeDriftMath.Alpha(0.5, 0, 10, 0, 1.0), 6);
        Assert.Equal(0.0, EdgeDriftMath.Alpha(1.0, 5, 10, 0, 1.0), 6);
        Assert.True(EdgeDriftMath.IsSpent(1.0, 3));
        Assert.True(EdgeDriftMath.IsSpent(0.4, 0));
        Assert.False(EdgeDriftMath.IsSpent(0.4, 3));
    }

    [Fact]
    public void TheDriftStepKeepsMotesInTheBandAndMovesThemClockwise()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var strip = new AmbientFxCanvas();
            strip.StartLayers(new AmbientFxConfig
            {
                Layers = AmbientFxLayers.EdgeDrift,
                EdgeSide = EdgeSide.Right,
                Tint = Lilac,
                Intensity = EdgeParticles.StripIntensity,
            });
            strip.PrimeEdgeForTests(60);

            for (int i = 0; i < 8; i++) strip.StepEdge(0.7f);
            Assert.Equal(EdgeDriftMath.MaxPerStrip, strip.EdgeMoteCount);

            for (int round = 0; round < 40; round++)
            {
                var before = strip.EdgeMotesForTests();
                strip.StepEdge(0.05f);
                var after = strip.EdgeMotesForTests();

                foreach (var (along, depth) in after)
                {
                    Assert.InRange(depth, (float)EdgeDriftMath.DepthMin - 1e-4f, (float)EdgeDriftMath.DepthMax + 1e-4f);
                    Assert.InRange(along, 0f, 1f);
                }

                // Depth never changes and is a random float, so it identifies a mote across a step
                // even when a retirement reorders the pool.
                foreach (var (along, depth) in after)
                {
                    var match = before.Where(b => b.Depth == depth).ToArray();
                    if (match.Length != 1) continue;   // spawned this step
                    float moved = along - match[0].Along;
                    Assert.InRange(moved, (float)(EdgeDriftMath.SpeedMin * 0.05) - 1e-5f, (float)(EdgeDriftMath.SpeedMax * 0.05) + 1e-5f);
                }
            }
        });
    }
}
