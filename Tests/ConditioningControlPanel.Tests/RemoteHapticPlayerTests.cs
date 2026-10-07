using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Remote;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Remote Control v2 haptics: the wire shapes (<c>haptic_pattern</c>, <c>haptic_level</c>) and
/// the pure timing core. No toy, no WPF, no clock.
/// </summary>
public class RemoteHapticPlayerTests
{
    private static RemoteHapticPlan Pattern(string json)
        => RemoteHapticPlan.FromPattern(JObject.Parse(json), out _)!;

    // ------------------------------------------------------------------ parsing

    [Fact]
    public void Pattern_reads_levels_step_loop_and_name()
    {
        var p = Pattern("{ levels: [0, 50, 100], step_ms: 200, loop: true, name: '  Slow drop  ' }");
        Assert.Equal(new[] { 0, 50, 100 }, p.Levels);
        Assert.Equal(200, p.StepMs);
        Assert.True(p.Loop);
        Assert.Equal("Slow drop", p.Name);
        Assert.False(p.IsHold);
        Assert.Equal(600, p.PassMs);
    }

    [Fact]
    public void Pattern_clamps_levels_and_step()
    {
        var p = Pattern("{ levels: [-5, 140], step_ms: 5 }");
        Assert.Equal(new[] { 0, 100 }, p.Levels);
        Assert.Equal(RemoteHapticPlan.MinStepMs, p.StepMs);
        Assert.False(p.Loop);
        Assert.Null(p.Name);
    }

    [Fact]
    public void Pattern_is_cut_at_240_steps_and_at_60_seconds()
    {
        var many = new JObject { ["levels"] = new JArray(Enumerable.Repeat(10, 400)), ["step_ms"] = 100 };
        Assert.Equal(240, RemoteHapticPlan.FromPattern(many, out _)!.Levels.Count);

        var slow = new JObject { ["levels"] = new JArray(Enumerable.Repeat(10, 200)), ["step_ms"] = 500 };
        var p = RemoteHapticPlan.FromPattern(slow, out _)!;
        Assert.Equal(120, p.Levels.Count);
        Assert.True(p.PassMs <= RemoteHapticPlan.MaxPassMs);
    }

    [Fact]
    public void Pattern_name_is_cut_at_24()
    {
        var p = Pattern("{ levels: [10], name: 'abcdefghijklmnopqrstuvwxyz0123' }");
        Assert.Equal(24, p.Name!.Length);
    }

    [Theory]
    [InlineData("{ }")]
    [InlineData("{ levels: [] }")]
    [InlineData("{ levels: 'nope' }")]
    [InlineData("{ levels: [10, 'x'] }")]
    public void Pattern_without_numbers_is_refused(string json)
    {
        Assert.Null(RemoteHapticPlan.FromPattern(JObject.Parse(json), out var why));
        Assert.False(string.IsNullOrEmpty(why));
    }

    [Fact]
    public void Level_is_a_one_step_hold_clamped_to_3_seconds()
    {
        var p = RemoteHapticPlan.FromLevel(JObject.Parse("{ level: 70, ms: 9000 }"), out _)!;
        Assert.True(p.IsHold);
        Assert.False(p.Loop);
        Assert.Equal(new[] { 70 }, p.Levels);
        Assert.Equal(RemoteHapticPlan.MaxHoldMs, p.StepMs);
        Assert.Null(RemoteHapticPlan.FromLevel(JObject.Parse("{ ms: 500 }"), out _));
    }

    // ------------------------------------------------------------------ timing

    [Fact]
    public void A_pass_merges_equal_levels_and_skips_silence()
    {
        var player = new RemoteHapticPlayer();
        var runs = player.Start(Pattern("{ levels: [40, 40, 0, 0, 80], step_ms: 100 }"), 1000, 1.0);
        Assert.Equal(2, runs.Count);
        Assert.Equal(new RemoteHapticRun(1000, 200, 0.4, runs[0].Priority), runs[0]);
        Assert.Equal(1400, runs[1].StartMs);
        Assert.Equal(100, runs[1].DurationMs);
        Assert.Equal(0.8, runs[1].Intensity, 6);
    }

    [Fact]
    public void Neighbouring_runs_never_share_a_priority()
    {
        // Equal priorities SUM in the mixer; touching runs must take the max instead.
        var player = new RemoteHapticPlayer();
        var plan = Pattern("{ levels: [10, 20, 30, 40], step_ms: 100, loop: true }");
        var first = player.Start(plan, 0, 1.0);
        var next = player.Tick(400 - RemoteHapticPlayer.LookaheadMs, 1.0).Queue;
        var all = first.Concat(next).ToList();
        Assert.Equal(8, all.Count);
        for (int i = 1; i < all.Count; i++) Assert.NotEqual(all[i - 1].Priority, all[i].Priority);
    }

    [Fact]
    public void Easy_factor_scales_every_run()
    {
        var player = new RemoteHapticPlayer();
        var runs = player.Start(Pattern("{ levels: [100, 50] }"), 0, 0.25);
        Assert.Equal(0.25, runs[0].Intensity, 6);
        Assert.Equal(0.125, runs[1].Intensity, 6);
    }

    [Fact]
    public void A_loop_queues_the_next_pass_ahead_of_its_seam()
    {
        var player = new RemoteHapticPlayer();
        player.Start(Pattern("{ levels: [50, 60], step_ms: 500, loop: true }"), 0, 1.0);
        Assert.Empty(player.Tick(500, 1.0).Queue);
        var next = player.Tick(1000 - RemoteHapticPlayer.LookaheadMs, 1.0).Queue;
        Assert.Equal(1000, next[0].StartMs);
        Assert.Empty(player.Tick(1000 - RemoteHapticPlayer.LookaheadMs + 50, 1.0).Queue);
    }

    [Fact]
    public void A_late_tick_restarts_the_pass_now_instead_of_bunching_it()
    {
        var player = new RemoteHapticPlayer();
        player.Start(Pattern("{ levels: [50, 60], step_ms: 500, loop: true }"), 0, 1.0);
        var next = player.Tick(5000, 1.0).Queue;
        Assert.Equal(5000, next[0].StartMs);
        Assert.Equal(5500, next[1].StartMs);
    }

    [Fact]
    public void A_one_shot_ends_after_its_pass()
    {
        var player = new RemoteHapticPlayer();
        player.Start(Pattern("{ levels: [50, 60], step_ms: 100 }"), 0, 1.0);
        Assert.False(player.Tick(150, 1.0).Ended);
        var t = player.Tick(200, 1.0);
        Assert.True(t.Ended);
        Assert.Equal("done", t.EndReason);
        Assert.False(player.IsPlaying);
    }

    [Fact]
    public void A_loop_ends_itself_ten_minutes_after_the_last_command()
    {
        var player = new RemoteHapticPlayer();
        player.Start(Pattern("{ levels: [50], step_ms: 100, loop: true }"), 0, 1.0);
        player.NoteCommand(60_000);
        Assert.False(player.Tick(60_000 + RemoteHapticPlayer.LoopIdleCapMs - 1, 1.0).Ended);
        var t = player.Tick(60_000 + RemoteHapticPlayer.LoopIdleCapMs, 1.0);
        Assert.True(t.Ended);
        Assert.Equal("idle", t.EndReason);
    }

    [Fact]
    public void A_refresh_over_a_live_run_takes_the_other_priority()
    {
        var player = new RemoteHapticPlayer();
        var hold = RemoteHapticPlan.FromLevel(JObject.Parse("{ level: 60, ms: 1500 }"), out _)!;
        var first = player.Start(hold, 0, 1.0);
        var refresh = player.Start(hold, 1000, 1.0);
        Assert.NotEqual(first[0].Priority, refresh[0].Priority);
        Assert.Equal(1000, refresh[0].StartMs);
    }

    [Fact]
    public void LevelAt_follows_the_plan_and_wraps_on_a_loop()
    {
        var player = new RemoteHapticPlayer();
        Assert.Equal(0, player.LevelAt(0));
        player.Start(Pattern("{ levels: [10, 20, 30], step_ms: 100, loop: true }"), 0, 1.0);
        Assert.Equal(10, player.LevelAt(50));
        Assert.Equal(30, player.LevelAt(250));
        Assert.Equal(20, player.LevelAt(450));
        player.Stop();
        Assert.Equal(0, player.LevelAt(450));
    }

    [Fact]
    public void Labels_name_the_new_commands_and_never_return_nothing()
    {
        Assert.Equal("Toy pattern", RemoteActionLabels.For("haptic_pattern"));
        Assert.Equal("Melt", RemoteActionLabels.For("start_brain_drain"));
        Assert.Equal("some thing", RemoteActionLabels.For("some_thing"));
        Assert.Equal("", RemoteActionLabels.For(null));
    }
    // ------------------------------------------------------------------ the service wiring

    [Fact]
    public void A_remote_can_never_switch_the_panic_key_off()
    {
        var src = ServiceSource();
        var body = Between(src, "case \"disable_panic\":", "case \"enable_panic\":");
        Assert.Contains("ReportCommandRefused(", body);
        Assert.DoesNotContain("PanicKeyEnabled = false", body);
    }

    [Fact]
    public void Every_stop_path_stops_the_remote_haptic()
    {
        var src = ServiceSource();
        Assert.Contains("StopRemoteHaptics();", Between(src, "private void StopAllRemoteEffects(bool force)", "private void SyncPanicKeyUi()"));
        Assert.Contains("StopRemoteHaptics();", Between(src, "private void StopRemoteTriggeredEffects()", "private void EnsureOverlayRunning()"));
        Assert.Contains("StopRemoteHaptics();", Between(src, "case \"haptic_stop\":", "break;"));
        // Both controller-gone branches of the poll: the explicit disconnect and the idle timeout.
        var poll = Between(src, "private async Task PollForCommandsAsync()", "// Execute commands");
        Assert.Equal(2, poll.Split("StopRemoteHaptics();").Length - 1);
    }

    private static string ServiceSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "ConditioningControlPanel", "Services", "RemoteControlService.cs"));
    }

    private static string Between(string source, string start, string end)
    {
        int from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"'{start}' not found");
        int to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"'{end}' not found after '{start}'");
        return source.Substring(from, to - from);
    }
}
