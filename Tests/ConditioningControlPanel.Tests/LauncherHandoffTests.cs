using System;
using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The second-instance surface handoff, both directions. A game shortcut clicked while the app is
/// already running writes one line through the "Open with CCP" handoff file; the primary decodes
/// it and routes. Both halves are string mapping, so every row is here.
/// </summary>
public class LauncherHandoffTests
{
    [Fact]
    public void Args_without_a_surface_encode_to_null()
    {
        Assert.Null(LauncherHandoff.Encode(null));
        Assert.Null(LauncherHandoff.Encode(new string[0]));
        Assert.Null(LauncherHandoff.Encode(new[] { "--play", @"C:\clip.mp4" }));
        Assert.Null(LauncherHandoff.Encode(new[] { "--startup" }));
    }

    [Fact]
    public void Panel_flag_encodes_panel_and_wins_over_a_game()
    {
        Assert.Equal("panel", LauncherHandoff.Encode(new[] { "--panel" }));
        Assert.Equal("panel", LauncherHandoff.Encode(new[] { "--game", "race", "--panel" }));
    }

    [Fact]
    public void Game_flag_encodes_game_with_id_in_both_forms()
    {
        Assert.Equal("game:race", LauncherHandoff.Encode(new[] { "--game", "race" }));
        Assert.Equal("game:backroom", LauncherHandoff.Encode(new[] { "--game=BackRoom" }));
    }

    [Fact]
    public void Launcher_and_client_flags_encode_launcher()
    {
        Assert.Equal("launcher", LauncherHandoff.Encode(new[] { "--launcher" }));
        Assert.Equal("launcher", LauncherHandoff.Encode(new[] { "--client" }));
    }

    [Fact]
    public void Decode_reads_panel_and_launcher()
    {
        Assert.Equal(("panel", (string?)null), LauncherHandoff.Decode("panel"));
        Assert.Equal(("panel", (string?)null), LauncherHandoff.Decode(" Panel \r"));
        Assert.Equal(("launcher", (string?)null), LauncherHandoff.Decode("launcher"));
    }

    [Fact]
    public void Decode_reads_a_game_id_lowercased()
    {
        Assert.Equal(("game", (string?)"race"), LauncherHandoff.Decode("game:race"));
        Assert.Equal(("game", (string?)"dtrh"), LauncherHandoff.Decode("game:DTRH\r"));
    }

    [Fact]
    public void Decode_falls_back_to_launcher_on_garbage()
    {
        Assert.Equal(("launcher", (string?)null), LauncherHandoff.Decode(null));
        Assert.Equal(("launcher", (string?)null), LauncherHandoff.Decode(""));
        Assert.Equal(("launcher", (string?)null), LauncherHandoff.Decode("game:"));
        Assert.Equal(("launcher", (string?)null), LauncherHandoff.Decode(@"C:\not\a\surface.mp4"));
    }

    [Fact]
    public void Encode_then_decode_round_trips_every_surface()
    {
        foreach (var (args, kind, id) in new[]
                 {
                     (new[] { "--panel" }, "panel", (string?)null),
                     (new[] { "--launcher" }, "launcher", null),
                     (new[] { "--game", "goon" }, "game", "goon"),
                 })
        {
            var payload = LauncherHandoff.Encode(args);
            Assert.NotNull(payload);
            Assert.Equal((kind, id), LauncherHandoff.Decode(payload));
        }
    }

    [Fact]
    public void Hide_delay_is_clamped_to_the_exit_beat_ceiling()
    {
        Assert.Equal(0, LauncherHost.ClampHideDelay(-40));
        Assert.Equal(450, LauncherHost.ClampHideDelay(450));
        Assert.Equal(LauncherHost.MaxHideDelayMs, LauncherHost.ClampHideDelay(90_000));

        LauncherHost.HideDelayMs = 90_000;
        Assert.Equal(LauncherHost.MaxHideDelayMs, LauncherHost.HideDelayMs);
        LauncherHost.HideDelayMs = 0;
        Assert.Equal(0, LauncherHost.HideDelayMs);
    }

    [Fact]
    public void An_armed_exit_beat_is_spent_by_its_first_consumer()
    {
        Assert.False(LauncherHost.ConsumeArmedBeat());
        LauncherHost.ArmExitBeat();
        Assert.True(LauncherHost.ConsumeArmedBeat());
        Assert.False(LauncherHost.ConsumeArmedBeat());
    }

    [Fact]
    public void The_fade_is_the_tail_of_the_exit_beat_not_added_to_it()
    {
        Assert.Equal(450 - LauncherHost.FadeOutMs, LauncherHost.FadeLeadMs(450));
        Assert.Equal(0, LauncherHost.FadeLeadMs(LauncherHost.FadeOutMs));
        Assert.Equal(0, LauncherHost.FadeLeadMs(100));
        Assert.Equal(0, LauncherHost.FadeLeadMs(0));
        Assert.Equal(LauncherHost.MaxHideDelayMs - LauncherHost.FadeOutMs, LauncherHost.FadeLeadMs(90_000));
    }

    [Fact]
    public void Without_a_fade_hook_or_with_one_that_declines_the_hide_runs_at_once()
    {
        try
        {
            int ran = 0;
            LauncherHost.FadeOut = null;
            LauncherHost.FadeThenHide(() => ran++);
            Assert.Equal(1, ran);

            LauncherHost.FadeOut = _ => false;
            LauncherHost.FadeThenHide(() => ran++);
            Assert.Equal(2, ran);

            LauncherHost.FadeOut = _ => throw new InvalidOperationException("no clock");
            LauncherHost.FadeThenHide(() => ran++);
            Assert.Equal(3, ran);
        }
        finally { LauncherHost.FadeOut = null; }
    }

    [Fact]
    public void A_fade_that_starts_defers_the_hide_to_its_continuation_once()
    {
        try
        {
            int ran = 0;
            Action? continuation = null;
            LauncherHost.FadeOut = then => { continuation = then; return true; };

            LauncherHost.FadeThenHide(() => ran++);
            Assert.NotNull(continuation);
            Assert.Equal(0, ran);

            continuation!();
            continuation();   // the guard timer and Completed may both fire; only one hide
            Assert.Equal(1, ran);
        }
        finally { LauncherHost.FadeOut = null; }
    }

    [Fact]
    public void A_hide_no_longer_wanted_at_the_end_of_the_fade_stands_down()
    {
        try
        {
            int ran = 0;
            bool wanted = true;
            Action? continuation = null;
            LauncherHost.FadeOut = then => { continuation = then; return true; };

            LauncherHost.FadeThenHide(() => ran++, () => wanted);
            wanted = false;
            continuation!();
            Assert.Equal(0, ran);
        }
        finally { LauncherHost.FadeOut = null; }
    }
}
