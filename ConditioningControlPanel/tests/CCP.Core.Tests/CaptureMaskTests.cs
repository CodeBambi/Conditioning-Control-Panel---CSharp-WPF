using System;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Compositor;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Unit tests for the per-region click-through capture mask (the union of every non-ambient
/// active layer's painted region that the global mouse hook hit-tests to decide swallow vs pass).
/// Pure, allocation-light, no Avalonia surface required.
/// </summary>
public class CaptureMaskTests
{
    [Fact]
    public void Empty_Mask_Contains_Nothing()
    {
        Assert.Equal(0, CaptureMask.Empty.Count);
        Assert.False(CaptureMask.Empty.Contains(0, 0));
        Assert.False(CaptureMask.Empty.Contains(100, 100));
        Assert.False(CaptureMask.Empty.Contains(-50, -50));
    }

    [Fact]
    public void Builder_With_Nothing_Added_Returns_Empty_Singleton()
    {
        var b = new CaptureMaskBuilder();
        Assert.Same(CaptureMask.Empty, b.Build());
    }

    [Fact]
    public void Single_Rect_Captures_Inside_And_Edges_Passes_Outside()
    {
        var mask = new CaptureMaskBuilder()
            .Tap(b => b.Add(100, 100, 200, 150))
            .Build();

        Assert.True(mask.Contains(150, 150));   // inside
        Assert.True(mask.Contains(100, 100));   // top-left corner (edge-inclusive)
        Assert.True(mask.Contains(300, 250));   // bottom-right corner
        Assert.False(mask.Contains(99, 150));   // just left
        Assert.False(mask.Contains(301, 150));  // just right
        Assert.False(mask.Contains(150, 99));   // just above
        Assert.False(mask.Contains(150, 251));  // just below
    }

    [Fact]
    public void Multiple_Rects_Form_A_Union()
    {
        var mask = new CaptureMaskBuilder()
            .Tap(b =>
            {
                b.Add(0, 0, 100, 100);
                b.Add(1000, 500, 200, 200);
            })
            .Build();

        Assert.Equal(2, mask.Count);
        Assert.True(mask.Contains(50, 50));         // first rect
        Assert.True(mask.Contains(1100, 600));      // second rect
        Assert.False(mask.Contains(500, 300));      // gap between
    }

    [Fact]
    public void Negative_Origin_Rects_Hit_Test_Correctly()
    {
        // Multi-monitor setups have negative virtual-desktop coords for left/above monitors.
        var mask = new CaptureMaskBuilder()
            .Tap(b => b.Add(-1920, -1080, 1920, 1080))
            .Build();

        Assert.True(mask.Contains(-1920, -1080));   // top-left of secondary monitor
        Assert.True(mask.Contains(-1000, -500));    // inside it
        Assert.True(mask.Contains(0, 0));           // bottom-right corner (edge-inclusive)
        Assert.False(mask.Contains(10, 10));        // primary monitor (outside the secondary rect)
    }

    [Fact]
    public void Degenerate_Rects_Are_Skipped()
    {
        var b = new CaptureMaskBuilder();
        b.Add(0, 0, 0, 100);        // zero width
        b.Add(0, 0, 100, 0);        // zero height
        b.Add(0, 0, -10, 100);      // negative width
        b.Add(0, 0, 100, -10);      // negative height
        Assert.Same(CaptureMask.Empty, b.Build());
    }

    [Fact]
    public void Add_Raw_Coords_Skips_Degenerate()
    {
        var b = new CaptureMaskBuilder();
        b.Add(0, 0, 0, 0);
        Assert.Equal(0, b.Count);
        Assert.Same(CaptureMask.Empty, b.Build());
    }

    [Fact]
    public void Reset_Clears_Accumulated_Regions()
    {
        var b = new CaptureMaskBuilder();
        b.Add(0, 0, 100, 100);
        b.Add(200, 200, 50, 50);
        Assert.Equal(2, b.Count);
        b.Reset();
        Assert.Equal(0, b.Count);
        Assert.Same(CaptureMask.Empty, b.Build());
    }

    [Fact]
    public void Build_Resets_For_Reuse_Across_Ticks()
    {
        var b = new CaptureMaskBuilder();
        // Frame 1: two regions.
        b.Add(0, 0, 100, 100);
        b.Add(200, 200, 50, 50);
        var frame1 = b.Build();
        Assert.Equal(2, frame1.Count);

        // Frame 2: the engine reuses the builder; nothing added this tick.
        Assert.Same(CaptureMask.Empty, b.Build());
    }

    [Fact]
    public void CaptureMaskState_Publishes_And_Reads_Immutably()
    {
        var state = new CaptureMaskState();
        Assert.Same(CaptureMask.Empty, state.CurrentMask);

        var populated = new CaptureMaskBuilder().Tap(b => b.Add(10, 10, 90, 90)).Build();
        var previous = state.Publish(populated);
        Assert.Same(CaptureMask.Empty, previous);              // returns the prior mask
        Assert.True(ReferenceEquals(state.CurrentMask, populated));
        Assert.True(state.CurrentMask.Contains(50, 50));

        var previous2 = state.Publish(CaptureMask.Empty);
        Assert.Same(populated, previous2);                     // returns what was published
        Assert.Same(CaptureMask.Empty, state.CurrentMask);
    }

    [Fact]
    public void CaptureMaskState_Publish_Null_Normalizes_To_Empty()
    {
        var state = new CaptureMaskState();
        state.Publish(new CaptureMaskBuilder().Tap(b => b.Add(0, 0, 10, 10)).Build());
        state.Publish(null!); // defensive: a caller passing null must never store null
        Assert.Same(CaptureMask.Empty, state.CurrentMask);
    }
}

internal static class CaptureMaskTestExtensions
{
    // Tiny fluent tap so the builder chain reads top-to-bottom in tests.
    public static T Tap<T>(this T target, Action<T> action)
    {
        action(target);
        return target;
    }
}
