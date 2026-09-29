using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.Chaster;
using Xunit;
using static ConditioningControlPanel.Services.Chaster.NatashasFavourite;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Natasha's favourite as a choice (2026-09-29): a quick click on the red bubble pops it (+5:00),
/// a press held for 1.2 s resists it (-1:00), sliding off or letting it float away books nothing,
/// and only a pop the player caused ever books.
/// </summary>
public class NatashaHoldTests
{
    /// <summary>Drive the hold machine the way the 31 Hz tick does: step every 32 ms until it ends.
    /// <paramref name="releaseAtMs"/> and <paramref name="slideAtMs"/> are when the button comes up
    /// and when the pointer leaves the bubble (null = never).</summary>
    private static (HoldStep Step, double AtMs) Run(double? releaseAtMs, double? slideAtMs = null, double untilMs = 5000)
    {
        for (double t = 0; t <= untilMs; t += 32)
        {
            var step = StepHold(t, releaseAtMs is { } r && t >= r, !(slideAtMs is { } s && t >= s));
            if (step != HoldStep.Holding) return (step, t);
        }
        return (HoldStep.Holding, untilMs);
    }

    [Fact]
    public void A_quick_click_pops_it()
    {
        var (step, at) = Run(releaseAtMs: 60);
        Assert.Equal(HoldStep.Popped, step);
        Assert.True(at < HoldMs);
        Assert.Equal(EventId, RowFor(true, PopCause.Player));
    }

    [Fact]
    public void Holding_until_the_ring_fills_resists_it()
    {
        var (step, at) = Run(releaseAtMs: null);
        Assert.Equal(HoldStep.Resisted, step);
        Assert.True(at >= HoldMs);
        Assert.Equal(HeldEventId, RowFor(true, PopCause.Resisted));
    }

    [Fact]
    public void Letting_go_just_before_the_ring_fills_still_pops()
    {
        Assert.Equal(HoldStep.Popped, StepHold(HoldMs - 1, released: true, onBubble: true));
        Assert.Equal(HoldStep.Popped, Run(releaseAtMs: 1100).Step);
    }

    [Fact]
    public void A_full_ring_wins_on_the_tick_the_button_comes_up()
    {
        Assert.Equal(HoldStep.Resisted, StepHold(HoldMs, released: true, onBubble: true));
        Assert.Equal(HoldStep.Resisted, StepHold(HoldMs + 40, released: true, onBubble: false));
    }

    [Fact]
    public void Sliding_off_while_holding_books_nothing()
    {
        var (step, _) = Run(releaseAtMs: null, slideAtMs: 400);
        Assert.Equal(HoldStep.SlidOff, step);
        // The bubble floats on; if it then floats away nothing ever ended it, so nothing books.
        Assert.Null(RowFor(true, PopCause.Programmatic));
    }

    [Fact]
    public void Still_down_and_on_it_keeps_filling()
    {
        Assert.Equal(HoldStep.Holding, StepHold(0, false, true));
        Assert.Equal(HoldStep.Holding, StepHold(HoldMs - 1, false, true));
    }

    [Fact]
    public void The_ring_fills_from_empty_to_full_and_no_further()
    {
        Assert.Equal(0, HoldProgress(0));
        Assert.Equal(0.5, HoldProgress(HoldMs / 2.0), 6);
        Assert.Equal(1, HoldProgress(HoldMs));
        Assert.Equal(1, HoldProgress(HoldMs * 3));
        Assert.Equal(0, HoldProgress(-50));
        Assert.Equal(0, HoldProgress(double.NaN));
        Assert.Equal(1200, HoldMs);
    }

    [Theory]
    [InlineData(false, PopCause.Player)]
    [InlineData(false, PopCause.Resisted)]
    [InlineData(false, PopCause.Programmatic)]
    [InlineData(true, PopCause.Programmatic)]
    public void Only_a_red_bubble_the_player_ended_books(bool natasha, PopCause cause)
    {
        Assert.Null(RowFor(natasha, cause));
    }

    [Fact]
    public void The_held_row_is_a_free_minute_off_next_to_natasha()
    {
        var row = TabPrices.Find("natasha_held");
        Assert.NotNull(row);
        Assert.Equal(-60, row!.Seconds);
        Assert.Equal(HeldSeconds, row.Seconds);
        Assert.Equal(TabPriceGate.Free, row.Gate);
        Assert.False(row.PerUnit);

        var ids = TabPrices.All.Select(p => p.Id).ToList();
        Assert.Equal(ids.IndexOf("natasha") + 1, ids.IndexOf("natasha_held"));

        Assert.Equal(0, TabPrices.Resolve("natasha_held", new HashSet<string>()));
        Assert.Equal(-60, TabPrices.Resolve("natasha_held", new HashSet<string> { "natasha_held" }));
    }

    [Fact]
    public void Every_preset_that_carries_natasha_carries_the_hold()
    {
        foreach (var preset in TabPresets.All)
            Assert.Equal(preset.PriceIds.Contains("natasha"), preset.PriceIds.Contains("natasha_held"));
        Assert.Contains("natasha_held", TabPresets.Find(TabPresets.Strict)!.PriceIds);
        Assert.Contains("natasha_held", TabPresets.Find(TabPresets.Circe)!.PriceIds);
    }

    [Fact]
    public void The_menu_dresses_the_held_row_like_the_red_one()
    {
        Assert.Equal(TabMenuCopy.ArtFor("natasha"), TabMenuCopy.ArtFor("natasha_held"));
        Assert.Equal("bubbles", TabMenuCopy.VignetteFor("natasha_held"));

        var dir = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages");
        var files = Directory.GetFiles(dir, "*.json");
        Assert.Equal(9, files.Length);
        foreach (var file in files)
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));
            foreach (var key in new[]
            {
                TabPageText.NameKey("natasha_held"), TabMenuCopy.ShortKey("natasha_held"), TabMenuCopy.WhereKey("natasha_held"),
                TabMenuCopy.FlavourKey("natasha_held"), TabMenuCopy.WhyKey("natasha_held"),
            })
                Assert.False(string.IsNullOrWhiteSpace((string?)json[key]), $"{Path.GetFileName(file)} lacks {key}");
        }
    }

    /// <summary>The rule "auto pops never book" lives in the source, not in a runtime path a test
    /// can drive (the pops are WPF), so hold the source to it: the red rows book in exactly one
    /// place, gated on who ended the bubble, and only the click and the stare call it the player's.</summary>
    [Fact]
    public void Auto_pops_never_book_the_red_rows()
    {
        var app = Path.Combine(RepoRoot(), "ConditioningControlPanel");
        var bubbles = File.ReadAllText(Path.Combine(app, "Services", "BubbleService.cs"));

        Assert.Equal(1, Count(bubbles, "NoteAt(\"natasha\""));
        Assert.Equal(1, Count(bubbles, "NoteAt(\"natasha_held\""));
        var end = bubbles.IndexOf("internal static void NoteNatashaEnd(", StringComparison.Ordinal);
        Assert.True(end > 0);
        Assert.InRange(bubbles.IndexOf("NoteAt(\"natasha\"", StringComparison.Ordinal) - end, 0, 800);
        Assert.Contains("NatashasFavourite.RowFor(bubble.IsNatasha, bubble.NatashaCause)", bubbles);

        // Only PopByClick and PopByGaze mark a pop as the player's; CompleteResist marks the hold.
        Assert.Equal(2, Count(bubbles, "_natashaCause = Chaster.NatashasFavourite.PopCause.Player;"));
        Assert.Equal(1, Count(bubbles, "_natashaCause = Chaster.NatashasFavourite.PopCause.Resisted;"));
        var avatar = Body(bubbles, "internal void PopByAvatar(");
        Assert.DoesNotContain("PopByClick", avatar);
        Assert.DoesNotContain("PopCause", avatar);

        // Every stare-to-pop is the player's, and goes through PopByGaze.
        var gaze = File.ReadAllText(Path.Combine(app, "Services", "Tracking", "GazeFocusService.cs"));
        Assert.DoesNotContain("b.Pop()", gaze);
        Assert.Equal(3, Count(gaze, "b.PopByGaze()"));
    }

    private static int Count(string haystack, string needle) =>
        Regex.Matches(haystack, Regex.Escape(needle)).Count;

    private static string Body(string source, string signature)
    {
        var at = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, signature);
        var open = source.IndexOf('{', at);
        int depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
        }
        return source.Substring(open);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Localization"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
