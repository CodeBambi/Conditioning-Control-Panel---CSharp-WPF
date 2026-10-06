using System;
using ConditioningControlPanel.RemoteHud;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The pure half of the Remote Control v2 HUD pill (Windows/RemoteHud/RemoteHudRules).</summary>
public class RemoteHudRulesTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(9, "0:09")]
    [InlineData(65, "1:05")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3725, "1:02:05")]
    [InlineData(36000 + 61, "10:01:01")]
    public void Elapsed_reads_m_ss_then_h_mm_ss(int seconds, string expected)
        => Assert.Equal(expected, RemoteHudRules.FormatElapsed(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Elapsed_never_goes_negative_and_drops_fractions()
    {
        Assert.Equal("0:00", RemoteHudRules.FormatElapsed(TimeSpan.FromSeconds(-5)));
        Assert.Equal("0:59", RemoteHudRules.FormatElapsed(TimeSpan.FromMilliseconds(59_999)));
    }

    [Fact]
    public void Elapsed_prefers_the_service_connect_time_and_falls_back_to_first_seen()
    {
        var now = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
        var seen = now.AddSeconds(-10);
        Assert.Equal(TimeSpan.FromSeconds(90), RemoteHudRules.Elapsed(now.AddSeconds(-90), seen, now));
        Assert.Equal(TimeSpan.FromSeconds(10), RemoteHudRules.Elapsed(null, seen, now));
    }

    [Theory]
    [InlineData(1.0, RemoteHudRules.StrengthTag.None)]
    [InlineData(1.5, RemoteHudRules.StrengthTag.None)]
    [InlineData(0.5, RemoteHudRules.StrengthTag.Half)]
    [InlineData(0.75, RemoteHudRules.StrengthTag.Half)]
    [InlineData(0.25, RemoteHudRules.StrengthTag.Quarter)]
    [InlineData(0.1, RemoteHudRules.StrengthTag.Quarter)]
    [InlineData(double.NaN, RemoteHudRules.StrengthTag.None)]
    public void Strength_tag_follows_the_easy_factor(double easy, RemoteHudRules.StrengthTag expected)
        => Assert.Equal(expected, RemoteHudRules.StrengthFor(easy));

    [Theory]
    [InlineData("mistress", "M")]
    [InlineData("  kitten", "K")]
    [InlineData("_doll", "D")]
    [InlineData("42", "4")]
    [InlineData("..", RemoteHudRules.NoNameInitial)]
    [InlineData("", RemoteHudRules.NoNameInitial)]
    [InlineData(null, RemoteHudRules.NoNameInitial)]
    public void Initial_is_the_first_letter_or_digit(string? name, string expected)
        => Assert.Equal(expected, RemoteHudRules.InitialFrom(name));

    [Fact]
    public void Display_name_is_trimmed_capped_and_null_when_blank()
    {
        Assert.Null(RemoteHudRules.DisplayName(null));
        Assert.Null(RemoteHudRules.DisplayName("   "));
        Assert.Equal("Sam", RemoteHudRules.DisplayName("  Sam "));
        Assert.Equal(24, RemoteHudRules.DisplayName(new string('a', 40))!.Length);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void Pill_shows_only_while_a_session_runs_and_a_controller_is_connected(bool active, bool connected, bool expected)
        => Assert.Equal(expected, RemoteHudRules.ShouldShow(active, connected));

    [Fact]
    public void A_second_stop_inside_the_debounce_is_swallowed()
    {
        var t0 = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(RemoteHudRules.StopPressAccepted(null, t0));
        Assert.False(RemoteHudRules.StopPressAccepted(t0, t0.AddMilliseconds(400)));
        Assert.False(RemoteHudRules.StopPressAccepted(t0, t0.AddMilliseconds(2499)));
        Assert.True(RemoteHudRules.StopPressAccepted(t0, t0 + RemoteHudRules.StopDebounce));
        // A clock that stepped back never locks the button.
        Assert.True(RemoteHudRules.StopPressAccepted(t0, t0.AddSeconds(-30)));
    }

    [Fact]
    public void The_stop_is_never_more_permissive_than_the_panic_key()
    {
        Assert.True(RemoteHudRules.LocalPanicAllowed(panicKeyEnabled: true));
        Assert.False(RemoteHudRules.LocalPanicAllowed(panicKeyEnabled: false));
    }
}
