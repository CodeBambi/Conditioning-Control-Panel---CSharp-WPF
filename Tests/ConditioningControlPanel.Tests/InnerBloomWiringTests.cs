using System;
using System.IO;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Super Inner Bloom wiring guards: it stops where the bubble field stops.</summary>
public class InnerBloomWiringTests
{
    /// <summary>Panic, the emergency exit, Stop and the minigame pause all clear the field through
    /// PopAllBubbles; Inner Bloom must stop on that same path, and the switch going off must tear it down.</summary>
    [Fact]
    public void Blooms_stop_on_the_bubble_fields_own_clear_path()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var svc = Path.Combine(dir!.FullName, "ConditioningControlPanel", "Services");
        var main = File.ReadAllText(Path.Combine(svc, "BubbleService.cs"));
        int popAll = main.IndexOf("public void PopAllBubbles()", StringComparison.Ordinal);
        Assert.True(popAll > 0);
        int clear = main.IndexOf("ClearBloomState();", popAll, StringComparison.Ordinal);
        int firstDestroy = main.IndexOf("ForceDestroy()", popAll, StringComparison.Ordinal);
        Assert.InRange(clear, popAll, firstDestroy);
        var glue = File.ReadAllText(Path.Combine(svc, "BubbleService.InnerBloom.cs"));
        Assert.Contains("SuperAccess.Changed += OnSuperChanged", glue);
        Assert.Contains("SuperAccess.IsOn(SuperEffect.InnerBloom)", glue);
        var layer = File.ReadAllText(Path.Combine(svc, "Compositor", "BubbleLayer.cs"));
        Assert.Contains("ClearBlooms();", layer);
    }
}
