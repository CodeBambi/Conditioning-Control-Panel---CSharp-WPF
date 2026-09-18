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
}
