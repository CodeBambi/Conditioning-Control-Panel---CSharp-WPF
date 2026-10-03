using System.Linq;
using ConditioningControlPanel.Services.Remote;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Remote Control v2 live preview: the <c>screen</c> object's shape (brief section 4), its
/// privacy rules, its size cap, the push throttle's change key, the event ring and the
/// controller's name.
/// </summary>
public class RemoteScreenStateTests
{
    [Fact]
    public void An_idle_screen_has_the_full_shape()
    {
        var s = RemoteScreenState.Build(new RemoteScreenInputs());
        Assert.Equal(2, s["v"]!.Value<int>());
        foreach (var k in new[] { "spiral", "pink", "flash", "subliminal", "bubbles", "bounce", "brain_drain", "mind_wipe", "duck", "autonomy", "lock_cards" })
            Assert.False(s["on"]![k]!.Value<bool>());
        Assert.Equal(JTokenType.Null, s["video"]!.Type);
        Assert.Equal(JTokenType.Null, s["lock"]!.Type);
        Assert.Equal(JTokenType.Null, s["count"]!.Type);
        Assert.Equal(0, s["haptic"]!["level"]!.Value<int>());
        Assert.Equal(JTokenType.Null, s["haptic"]!["pattern"]!.Type);
        Assert.Equal(1.0, s["easy"]!.Value<double>());
        Assert.Empty((JArray)s["ev"]!);
        Assert.Equal(0, s["attn"]!["idle_s"]!.Value<int>());
    }

    [Fact]
    public void Opacity_is_sent_as_strength_out_of_100()
    {
        var s = RemoteScreenState.Build(new RemoteScreenInputs { SpiralOpacity = 20, PinkOpacity = 50 });
        Assert.Equal(40, s["str"]!["spiral"]!.Value<int>());
        Assert.Equal(100, s["str"]!["pink"]!.Value<int>());
        Assert.Equal(100, RemoteScreenState.Strength(90));
    }

    [Fact]
    public void Niche_names_travel_only_when_shared_and_counts_always()
    {
        var names = new[] { "EroticHypnosis", "HypnoHentai" };
        var shared = RemoteScreenState.Build(new RemoteScreenInputs { OnlineNames = names, ShareOnlineNames = true, Pictures = 214, Videos = 12 });
        Assert.Equal(names, shared["media"]!["online"]!.Values<string>().ToArray());
        Assert.Equal(2, shared["media"]!["online_count"]!.Value<int>());
        Assert.Equal(214, shared["media"]!["pictures"]!.Value<int>());

        var hidden = RemoteScreenState.Build(new RemoteScreenInputs { OnlineNames = names, ShareOnlineNames = false, Pictures = 214, Videos = 12 });
        Assert.Empty((JArray)hidden["media"]!["online"]!);
        Assert.Equal(2, hidden["media"]!["online_count"]!.Value<int>());
        Assert.Equal(12, hidden["media"]!["videos"]!.Value<int>());
    }

    [Fact]
    public void Events_carry_source_and_only_a_controller_word()
    {
        var s = RemoteScreenState.Build(new RemoteScreenInputs
        {
            Events = new[]
            {
                new RemoteScreenEvent("flash", 1, Src: "online"),
                new RemoteScreenEvent("flash", 2, Src: "C:\\private\\pic.jpg"),
                new RemoteScreenEvent("word", 3, Text: "drop"),
                new RemoteScreenEvent("word", 4),
                new RemoteScreenEvent("pop", 5),
            }
        });
        var ev = (JArray)s["ev"]!;
        Assert.Equal("online", ev[0]["src"]!.Value<string>());
        Assert.Equal("local", ev[1]["src"]!.Value<string>());   // anything but "online" is "local", never a path
        Assert.Equal("drop", ev[2]["text"]!.Value<string>());
        Assert.Equal(JTokenType.Null, ev[3]["text"]!.Type);
        Assert.Null(ev[4]["text"]);
        Assert.Equal(5, ev[4]["at"]!.Value<long>());
    }

    [Fact]
    public void Lock_card_position_never_runs_past_its_text()
    {
        var s = RemoteScreenState.Build(new RemoteScreenInputs { LockText = "good and empty", LockPos = 99, LockTypos = 2, LockDone = 1 });
        Assert.Equal("good and empty", s["lock"]!["text"]!.Value<string>());
        Assert.Equal(14, s["lock"]!["pos"]!.Value<int>());
        Assert.Equal(2, s["lock"]!["typos"]!.Value<int>());
        Assert.Equal(1, s["lock"]!["done"]!.Value<int>());
    }

    [Fact]
    public void Video_and_count_follow_their_inputs()
    {
        var s = RemoteScreenState.Build(new RemoteScreenInputs
        {
            VideoKind = "web", VideoElapsedMs = 4200,
            CountRight = false, CountN = 7, CountAnswer = 9,
        });
        Assert.Equal("web", s["video"]!["kind"]!.Value<string>());
        Assert.Equal(4200, s["video"]!["elapsed_ms"]!.Value<long>());
        Assert.Equal(JTokenType.Null, s["video"]!["dur_ms"]!.Type);
        Assert.False(s["count"]!["right"]!.Value<bool>());
        Assert.Equal(7, s["count"]!["n"]!.Value<int>());

        var running = RemoteScreenState.Build(new RemoteScreenInputs { CountActive = true });
        Assert.Equal(JTokenType.Null, running["count"]!["right"]!.Type);
    }

    [Fact]
    public void A_screen_stays_under_the_server_cap_even_with_everything_long()
    {
        var s = RemoteScreenState.Build(new RemoteScreenInputs
        {
            LockText = new string('x', 5000),
            OnlineNames = Enumerable.Range(0, 400).Select(n => new string('n', 200) + n).ToArray(),
            ShareOnlineNames = true,
            Events = Enumerable.Range(0, 50).Select(n => new RemoteScreenEvent("word", n, Text: new string('w', 500))).ToArray(),
        });
        Assert.True(RemoteScreenState.Size(s) <= RemoteScreenState.MaxBytes);
        Assert.Equal(RemoteEventRing.MaxEvents, ((JArray)s["ev"]!).Count);
        Assert.Equal(RemoteScreenState.MaxLockText, s["lock"]!["text"]!.Value<string>()!.Length);
    }

    [Fact]
    public void The_change_key_ignores_what_moves_on_its_own()
    {
        var a = RemoteScreenState.Build(new RemoteScreenInputs { IdleSeconds = 1, VideoKind = "local", VideoElapsedMs = 100, HapticLevel = 10 });
        var b = RemoteScreenState.Build(new RemoteScreenInputs { IdleSeconds = 9, VideoKind = "local", VideoElapsedMs = 9000, HapticLevel = 80 });
        Assert.Equal(RemoteScreenState.ChangeKey(a), RemoteScreenState.ChangeKey(b));

        var away = RemoteScreenState.Build(new RemoteScreenInputs { IdleSeconds = 25, VideoKind = "local" });
        Assert.NotEqual(RemoteScreenState.ChangeKey(a), RemoteScreenState.ChangeKey(away));
        var spiral = RemoteScreenState.Build(new RemoteScreenInputs { IdleSeconds = 1, VideoKind = "local", Spiral = true });
        Assert.NotEqual(RemoteScreenState.ChangeKey(a), RemoteScreenState.ChangeKey(spiral));
    }

    [Fact]
    public void The_ring_keeps_twelve_from_the_last_twenty_seconds_newest_last()
    {
        var ring = new RemoteEventRing();
        for (int n = 0; n < 20; n++) ring.Add(new RemoteScreenEvent("pop", 1000 + n));
        var recent = ring.Recent(1019);
        Assert.Equal(12, recent.Count);
        Assert.Equal(1019, recent[^1].AtUnixMs);
        Assert.Empty(ring.Recent(1019 + RemoteEventRing.WindowMs + 1000));
    }

    [Theory]
    [InlineData("  Mistress K ", "Mistress K")]
    [InlineData("a.b_c-d", "a.b_c-d")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("<script>", null)]
    [InlineData("abcdefghijklmnopqrstuvwxy", null)]
    [InlineData(null, null)]
    public void Controller_names_are_sanitised(string? raw, string? expected)
        => Assert.Equal(expected, RemoteControllerName.Sanitize(raw));

    [Fact]
    public void Caps_name_what_this_desktop_understands()
        => Assert.Equal(new[] { "haptic_pattern", "haptic_level", "signal", "screen", "brain_drain" }, RemoteScreenState.Caps);
}
