using System;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Super Vortex safety: the panic key and the emergency exit stop it with the spiral.</summary>
public class VortexPanicTests
{
    [Fact]
    public void Panic_stops_the_vortex_with_the_spiral_it_rides()
    {
        // The layer needs a live compositor, so pin the wiring: every panic / emergency exit path
        // runs OverlayService.StopSpiral, and StopSpiral stops the vortex before anything else.
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var src = System.IO.File.ReadAllText(System.IO.Path.Combine(dir!.FullName,
            "ConditioningControlPanel", "Services", "Notifications", "OverlayService.cs"));
        int stop = src.IndexOf("internal void StopSpiral()", StringComparison.Ordinal);
        Assert.True(stop > 0);
        int brace = src.IndexOf('{', stop);
        int firstStatement = src.IndexOf(';', brace);
        Assert.Contains("_vortexLayer?.Stop()", src.Substring(brace, firstStatement - brace + 1));
        Assert.Contains("SuperAccess.IsOn(Super.SuperEffect.Vortex)", src);
    }
}
