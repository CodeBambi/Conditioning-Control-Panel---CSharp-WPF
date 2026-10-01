using System;
using System.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Super Creep: the safety wiring that a unit test of the maths cannot see.</summary>
public class CreepPanicTests
{
    /// <summary>Panic always wins: the fog goes down on the engine stop (panic with a session,
    /// Lockdown's end, the emergency exit) AND on the ad-hoc panic pass. A source scan, because a
    /// lost line here is a fog the player cannot clear and no unit test can see it.</summary>
    [Fact]
    public void PanicAndTheEngineStopTakeTheFogDown()
    {
        static string Body(string file, string signature)
        {
            var text = System.IO.File.ReadAllText(System.IO.Path.Combine(SourceRoot(), file));
            var i = text.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(i >= 0, $"{signature} not found in {file}");
            var end = text.IndexOf("\n    }", i, StringComparison.Ordinal);
            var end8 = text.IndexOf("\n        }", i, StringComparison.Ordinal);
            var stop = new[] { end, end8 }.Where(x => x > i).DefaultIfEmpty(text.Length).Min();
            return text.Substring(i, stop - i);
        }
        Assert.Contains("CreepController.Stop", Body("Services/Notifications/OverlayService.cs", "public void Stop()"));
        Assert.Contains("CreepController.Stop", Body("MainWindow/MainWindow.xaml.cs", "private void StopAdHocEffects()"));
    }

    /// <summary>Click anywhere is notification only: the hook callback must never swallow, or the
    /// fog would eat the click aimed at the app underneath (the panel's own panic button included).</summary>
    [Fact]
    public void TheClickHookNeverSwallows()
    {
        var onDown = typeof(ConditioningControlPanel.Services.Super.CreepController)
            .GetMethod("OnDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.Equal(false, onDown.Invoke(null, new object[] { new System.Windows.Point(10, 10) }));
    }

    /// <summary>A lapsed tier or a silent switch flip still takes the fog down: the layer asks the
    /// seam again once a second while it shows.</summary>
    [Fact]
    public void TheLayerRechecksTheSeamWhileItShows()
    {
        var text = System.IO.File.ReadAllText(System.IO.Path.Combine(SourceRoot(), "Services/Compositor/CreepLayer.cs"));
        Assert.Contains("SuperAccess.IsOn(SuperEffect.Creep)", text);
        Assert.Contains("CreepController.Sync", text);
    }

    private static string SourceRoot()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj");
            if (System.IO.File.Exists(candidate)) return System.IO.Path.GetDirectoryName(candidate)!;
            dir = dir.Parent;
        }
        throw new System.IO.DirectoryNotFoundException("ConditioningControlPanel project not found");
    }
}
