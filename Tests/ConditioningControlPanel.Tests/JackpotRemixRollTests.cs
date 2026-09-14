using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Remix;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The pure rules of the Jackpot Remix director: eligibility, the 1-in-N roll, the
/// carried flag, the gates, source selection and the centred geometry.</summary>
public class JackpotRemixRollTests
{
    [Theory]
    [InlineData(false, true, MotionLevel.Full, 3)]
    [InlineData(true, false, MotionLevel.Full, 3)]
    [InlineData(true, true, MotionLevel.Off, 3)]
    [InlineData(true, true, MotionLevel.Full, 0)]
    public void Not_eligible_when_unowned_disabled_motion_off_or_no_gifs(bool owned, bool enabled, MotionLevel level, int gifs)
        => Assert.False(JackpotRemixRoll.Eligible(owned, enabled, level, gifs));

    [Fact]
    public void Eligible_when_owned_enabled_moving_and_one_gif()
    {
        Assert.True(JackpotRemixRoll.Eligible(true, true, MotionLevel.Full, 1));
        Assert.True(JackpotRemixRoll.Eligible(true, true, MotionLevel.Reduced, 1));
    }

    [Fact]
    public void Ineligible_flash_never_touches_the_state()
    {
        var s = new JackpotRemixRollState { Carried = true, BuildRequested = true };
        Assert.Equal(JackpotRemixStep.None, JackpotRemixRoll.Step(s, eligible: false, hit: true, hasBuiltFile: true, seed: 1));
        Assert.True(s.Carried);
    }

    [Fact]
    public void Odds_are_one_in_n_with_a_seeded_random()
    {
        var rng = new Random(12345);
        int hits = 0;
        for (int i = 0; i < 100_000; i++) if (JackpotRemixRoll.Hit(rng, 100)) hits++;
        Assert.InRange(hits, 800, 1200);
        Assert.True(JackpotRemixRoll.Hit(new Random(7), 1));
        Assert.True(JackpotRemixRoll.Hit(new Random(7), 0));
    }

    [Theory]
    [InlineData(null, 100)]
    [InlineData("", 100)]
    [InlineData("abc", 100)]
    [InlineData("0", 100)]
    [InlineData("-5", 100)]
    [InlineData("1", 1)]
    [InlineData("25", 25)]
    public void Env_override_parses_positive_integers_only(string? env, int want)
        => Assert.Equal(want, JackpotRemixRoll.ResolveOdds(env));

    [Fact]
    public void Miss_with_nothing_carried_is_an_ordinary_flash()
    {
        var s = new JackpotRemixRollState();
        Assert.Equal(JackpotRemixStep.None, JackpotRemixRoll.Step(s, true, hit: false, hasBuiltFile: true, seed: 1));
        Assert.False(s.Carried);
    }

    [Fact]
    public void Hit_with_a_built_file_plays_at_once()
    {
        var s = new JackpotRemixRollState();
        Assert.Equal(JackpotRemixStep.Play, JackpotRemixRoll.Step(s, true, hit: true, hasBuiltFile: true, seed: 1));
        Assert.False(s.Carried);
    }

    [Fact]
    public void Hit_without_a_file_is_carried_and_builds_once_then_the_next_file_consumes_it()
    {
        var s = new JackpotRemixRollState();
        Assert.Equal(JackpotRemixStep.RequestBuild, JackpotRemixRoll.Step(s, true, hit: true, hasBuiltFile: false, seed: 42));
        Assert.True(s.Carried);
        Assert.True(s.BuildRequested);
        Assert.Equal(42, s.Seed);

        // Later flashes, still nothing built: carried, no second build request (a hit or a miss).
        Assert.Equal(JackpotRemixStep.Wait, JackpotRemixRoll.Step(s, true, hit: false, hasBuiltFile: false, seed: 43));
        Assert.Equal(JackpotRemixStep.Wait, JackpotRemixRoll.Step(s, true, hit: true, hasBuiltFile: false, seed: 44));
        Assert.Equal(42, s.Seed);

        // The file lands: the next flash consumes it and the carry clears.
        Assert.Equal(JackpotRemixStep.Play, JackpotRemixRoll.Step(s, true, hit: false, hasBuiltFile: true, seed: 45));
        Assert.False(s.Carried);
        Assert.False(s.BuildRequested);
        Assert.Equal(JackpotRemixStep.None, JackpotRemixRoll.Step(s, true, hit: false, hasBuiltFile: false, seed: 46));
    }

    [Fact]
    public void Clear_spends_the_carry_after_a_failed_load()
    {
        var s = new JackpotRemixRollState();
        JackpotRemixRoll.Step(s, true, hit: true, hasBuiltFile: false, seed: 1);
        JackpotRemixRoll.Clear(s);
        Assert.False(s.Carried);
        Assert.False(s.BuildRequested);
    }

    [Fact]
    public void Quiet_needs_no_flash_and_no_video()
    {
        Assert.True(JackpotRemixRoll.Quiet(flashShowing: false, videoPlaying: false));
        Assert.False(JackpotRemixRoll.Quiet(true, false));
        Assert.False(JackpotRemixRoll.Quiet(false, true));
    }

    [Fact]
    public void Memory_pressure_is_below_the_floor()
    {
        Assert.True(JackpotRemixRoll.MemoryPressure(JackpotRemixRoll.MemoryFloorBytes - 1));
        Assert.False(JackpotRemixRoll.MemoryPressure(JackpotRemixRoll.MemoryFloorBytes));
        Assert.False(JackpotRemixRoll.MemoryPressure(ulong.MaxValue));
        Assert.Equal(1536UL * 1024 * 1024, JackpotRemixRoll.MemoryFloorBytes);
    }

    [Theory]
    [InlineData(true, false, false, true, false, true)]
    [InlineData(false, false, false, true, false, false)]
    [InlineData(true, true, false, true, false, false)]
    [InlineData(true, false, true, true, false, false)]
    [InlineData(true, false, false, false, false, false)]
    [InlineData(true, false, false, true, true, false)]
    public void Build_only_when_eligible_uncached_idle_quiet_and_memory_is_free(
        bool eligible, bool cached, bool inFlight, bool quiet, bool pressure, bool want)
        => Assert.Equal(want, JackpotRemixRoll.ShouldBuild(eligible, cached, inFlight, quiet, pressure));

    [Fact]
    public void Sources_drop_non_gifs_filter_size_shuffle_by_seed_and_cycle_to_eight()
    {
        var pool = new[] { "a.gif", "still.png", "b.GIF", "clip.mp4", "c.gif", "big.gif", "gone.gif", "d.gif" };
        long SizeOf(string p) => p switch
        {
            "big.gif" => JackpotRemixPlan.MaxSourceBytes + 1,
            "gone.gif" => -1,
            _ => 1024,
        };
        var got = JackpotRemixRoll.SelectSources(pool, 7, SizeOf);
        Assert.Equal(8, got.Count);
        var distinct = got.Distinct().ToList();
        Assert.Equal(4, distinct.Count);
        Assert.Equal(new[] { "a.gif", "b.GIF", "c.gif", "d.gif" }, distinct.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(distinct, got.Take(4));
        Assert.Equal(got.Take(4), got.Skip(4).Take(4));

        Assert.Equal(got, JackpotRemixRoll.SelectSources(pool, 7, SizeOf));
        var other = JackpotRemixRoll.SelectSources(pool, 8, SizeOf);
        Assert.Equal(4, other.Distinct().Count());
    }

    [Fact]
    public void Sources_shuffle_is_a_draw_not_the_first_eight()
    {
        var pool = Enumerable.Range(0, 40).Select(i => $"g{i:00}.gif").ToList();
        var seen = new HashSet<string>();
        for (int seed = 0; seed < 20; seed++)
            foreach (var s in JackpotRemixRoll.SelectSources(pool, seed, _ => 1)) seen.Add(s);
        Assert.True(seen.Count > 8);
        Assert.Equal(8, JackpotRemixRoll.SelectSources(pool, 3, _ => 1).Distinct().Count());
    }

    [Fact]
    public void No_gifs_means_no_remix()
    {
        Assert.Empty(JackpotRemixRoll.SelectSources(new[] { "a.png", "b.mp4" }, 1, _ => 1));
        Assert.Empty(JackpotRemixRoll.SelectSources(new[] { "a.gif" }, 1, _ => JackpotRemixPlan.MaxSourceBytes + 1));
        Assert.Empty(JackpotRemixRoll.SelectSources(Array.Empty<string>(), 1, _ => 1));
    }

    [Fact]
    public void Centred_geometry_is_about_two_thirds_of_the_short_side_and_centred()
    {
        var (x, y, w, h) = JackpotRemixRoll.CentredGeometry(0, 0, 1920, 1080, 480, 270);
        Assert.Equal(702, h);
        Assert.Equal(1248, w);
        Assert.Equal((1920 - w) / 2, x);
        Assert.Equal((1080 - h) / 2, y);

        // Second monitor offset carries into the position.
        var (x2, y2, _, _) = JackpotRemixRoll.CentredGeometry(1920, 200, 1920, 1080, 480, 270);
        Assert.Equal(1920 + x, x2);
        Assert.Equal(200 + y, y2);
    }

    [Fact]
    public void Centred_geometry_never_leaves_a_portrait_monitor()
    {
        var (x, y, w, h) = JackpotRemixRoll.CentredGeometry(0, 0, 1080, 1920, 480, 270);
        Assert.True(w <= 1080 - 2 * JackpotRemixRoll.EdgePadding);
        Assert.True(x >= 0 && x + w <= 1080);
        Assert.True(y >= 0 && y + h <= 1920);
        Assert.Equal(Math.Round(w * 270.0 / 480), h, 0);
    }
}
