using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Spiral "Randomize" (#641) with the personal-folder refusal (#1053): the pool is the
/// configured spiral's own folder or the Spirals library, never the user's Desktop, and the pick
/// is latched for one run.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class SpiralRandomizeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-spiral-" + Guid.NewGuid().ToString("N"));
    private readonly Func<string?, bool> _oldRefusal = SpiralOverlay.IsPersonalFolderRoot;

    public SpiralRandomizeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SpiralOverlay.IsPersonalFolderRoot = _oldRefusal;
        try { Directory.Delete(_root, true); } catch { }
    }

    private string Dir(string name, params string[] files)
    {
        var d = Path.Combine(_root, name);
        Directory.CreateDirectory(d);
        foreach (var f in files) File.WriteAllBytes(Path.Combine(d, f), new byte[] { 1 });
        return d;
    }

    [Fact]
    public void The_pool_is_the_configured_spirals_folder_and_only_its_pictures()
    {
        SpiralOverlay.IsPersonalFolderRoot = _ => false;
        var mine = Dir("mine", "a.gif", "b.png", "notes.txt", "clip.mp4");
        var library = Dir("library", "z.gif");
        string? last = null;
        var seen = new HashSet<string>();
        for (var i = 0; i < 60; i++)
            seen.Add(Path.GetFileName(SpiralOverlay.PickRandomSpiral(Path.Combine(mine, "a.gif"), library, new Random(i), ref last)!));
        Assert.Equal(new HashSet<string> { "a.gif", "b.png" }, seen);
    }

    [Fact]
    public void A_spiral_picked_from_a_personal_folder_never_widens_into_that_folder()
    {
        var desktop = Dir("desktop", "spiral.gif", "family.jpg", "passport.png");
        var library = Dir("library", "one.gif", "two.gif");
        SpiralOverlay.IsPersonalFolderRoot = p => string.Equals(p, desktop, StringComparison.OrdinalIgnoreCase);
        string? last = null;
        for (var i = 0; i < 60; i++)
        {
            var pick = SpiralOverlay.PickRandomSpiral(Path.Combine(desktop, "spiral.gif"), library, new Random(i), ref last)!;
            Assert.Equal(library, Path.GetDirectoryName(pick));
        }
    }

    [Fact]
    public void No_pool_means_the_single_spiral_and_the_same_one_is_never_drawn_twice_running()
    {
        SpiralOverlay.IsPersonalFolderRoot = _ => false;
        string? last = null;
        Assert.Null(SpiralOverlay.PickRandomSpiral(null, Path.Combine(_root, "missing"), new Random(1), ref last));
        Assert.Null(SpiralOverlay.PickRandomSpiral(null, Dir("empty", "readme.txt"), new Random(1), ref last));
        var one = Dir("one", "only.gif");
        Assert.Equal(Path.Combine(one, "only.gif"), SpiralOverlay.PickRandomSpiral(null, one, new Random(1), ref last));

        var two = Dir("two", "a.gif", "b.gif");
        var rng = new Random(4);
        last = null;
        var prev = SpiralOverlay.PickRandomSpiral(null, two, rng, ref last);
        for (var i = 0; i < 30; i++)
        {
            var next = SpiralOverlay.PickRandomSpiral(null, two, rng, ref last);
            Assert.NotEqual(prev, next);
            prev = next;
        }
    }

    [Fact]
    public void Off_is_the_configured_spiral_and_on_is_latched_for_the_run()
    {
        SpiralOverlay.IsPersonalFolderRoot = _ => false;
        var s = CoreSettings.Current;
        var (path, randomize) = (s.SpiralPath, s.SpiralRandomize);
        var mine = Dir("run", "a.gif", "b.gif", "c.gif");
        try
        {
            s.SpiralPath = Path.Combine(mine, "a.gif");
            s.SpiralRandomize = false;
            Assert.Equal(s.SpiralPath, SpiralOverlay.SourcePath());
            Assert.Null(SpiralOverlay.RunPick);

            s.SpiralRandomize = true;
            var first = SpiralOverlay.SourcePath();
            Assert.Equal(mine, Path.GetDirectoryName(first));
            for (var i = 0; i < 10; i++) Assert.Equal(first, SpiralOverlay.SourcePath());   // every refresh of one run

            s.SpiralRandomize = false;
            Assert.Equal(s.SpiralPath, SpiralOverlay.SourcePath());
            Assert.Null(SpiralOverlay.RunPick);
        }
        finally
        {
            s.SpiralPath = path; s.SpiralRandomize = randomize;
            SpiralOverlay.SourcePath();
            CoreSettings.SaveImmediate();
        }
    }
}
