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

    [Fact]
    public void Dispose_releases_the_vortex_and_its_static_subscription()
    {
        // SuperAccess.Changed is static: a lambda subscription would pin the service and could
        // restart the vortex (and its global mouse hook) after the service was disposed.
        var src = OverlaySource();
        Assert.Contains("SuperAccess.Changed += OnSuperChanged", src);
        int dispose = src.IndexOf("public void Dispose()", StringComparison.Ordinal);
        Assert.True(dispose > 0);
        var body = src.Substring(dispose, Math.Min(4000, src.Length - dispose));
        Assert.Contains("SuperAccess.Changed -= OnSuperChanged", body);
        Assert.Contains("_vortexLayer?.Stop()", body);
    }

    [Fact]
    public void The_reconciler_resyncs_the_vortex_so_a_lapsed_tier_tears_it_down()
    {
        var src = OverlaySource();
        int at = src.IndexOf("Self-heal the Super add-on", StringComparison.Ordinal);
        Assert.True(at > 0);
        Assert.Contains("SyncVortex();", src.Substring(at, 400));
    }

    private static string OverlaySource()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return System.IO.File.ReadAllText(System.IO.Path.Combine(dir!.FullName,
            "ConditioningControlPanel", "Services", "Notifications", "OverlayService.cs"));
    }
}
