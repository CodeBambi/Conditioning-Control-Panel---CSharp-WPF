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

    private static string Services()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel", "Services");
    }

    private static string Body(string src, string signature)
    {
        int at = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, signature);
        int open = src.IndexOf('{', at), depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(open, i - open + 1);
        }
        throw new InvalidOperationException(signature);
    }

    /// <summary>Visual only: a released kid must not pay XP, feed achievements (Sparkle Points), quests,
    /// the companion or a leash task's pop count. Only the bloom itself, which stands in for one
    /// ordinary spawn, pays one ordinary pop.</summary>
    [Fact]
    public void Released_kids_pay_nothing_and_count_for_nothing()
    {
        var svc = Services();
        var main = File.ReadAllText(Path.Combine(svc, "BubbleService.cs"));
        var onPop = Body(main, "private void OnPop(Bubble bubble)");
        Assert.Contains("if (bubble.IsBloomKid) PopBloomKidQuietly(bubble);", onPop);
        Assert.Contains("else AwardAmbientPop(bubble);", onPop);
        var onMiss = Body(main, "private void OnMiss(Bubble bubble)");
        Assert.Contains("if (!bubble.IsBloomKid) OnBubbleMissed?.Invoke();", onMiss);

        var glue = File.ReadAllText(Path.Combine(svc, "BubbleService.InnerBloom.cs"));
        var quiet = Body(glue, "private void PopBloomKidQuietly(Bubble b)");
        foreach (var banned in new[] { "AddXP", "OnBubblePopped", "TrackBubblePopped", "AwardAmbientPop",
                                       "TakeFromAmbientBubbleBucket", "NoteNatashaEnd", "SkillPoints" })
            Assert.DoesNotContain(banned, quiet);
    }

    /// <summary>MotionFx decides the film's shimmer too, not only the kids' orbit (Off = still).</summary>
    [Fact]
    public void Film_shimmer_runs_on_motion_scaled_time()
    {
        var layer = File.ReadAllText(Path.Combine(Services(), "Compositor", "BubbleLayer.Bloom.cs"));
        var node = Body(layer, "private void DrawNode(");
        Assert.Contains("double ft = t * m.Speed;", node);
        Assert.DoesNotContain("DrawFilm(c, bul, t,", node);
        Assert.DoesNotContain("new SKRoundRect", layer);   // no native object per leaf per frame
    }
}
