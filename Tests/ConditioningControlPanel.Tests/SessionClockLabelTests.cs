using System;
using System.IO;
using System.Text.Json;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The two "look away from the clock" options: HideLockdownTimer masks every lockdown readout,
/// ShowSessionCountdown drops the MM:SS from the START buttons. The label work is pure
/// (Services/SessionClockLabel.cs); the templates are passed in resolved, so these run without
/// a language file.
/// </summary>
public class SessionClockLabelTests
{
    private const string StopWithClock = "STOP SESSION ({0}:{1})";
    private const string StopPlain = "STOP SESSION";
    private const string StartWithClock = "{0} {1}:{2}{3}";

    [Fact]
    public void Defaults_are_clock_shown_and_countdown_on()
    {
        var s = new AppSettings();
        Assert.False(s.HideLockdownTimer);
        Assert.True(s.ShowSessionCountdown);
    }

    [Theory]
    [InlineData(5 * 60, "05:00")]
    [InlineData(9 * 60 + 41, "09:41")]
    [InlineData(2 * 3600, "2:00:00")]
    public void Lockdown_clock_shows_the_digits_when_not_hidden(int seconds, string expected)
    {
        Assert.Equal(expected, SessionClockLabel.LockdownClock(TimeSpan.FromSeconds(seconds), hidden: false));
    }

    [Theory]
    [InlineData(5 * 60)]
    [InlineData(2 * 3600)]
    [InlineData(0)]
    public void Lockdown_clock_hides_the_digits_behind_the_same_placeholder(int seconds)
    {
        var shown = SessionClockLabel.LockdownClock(TimeSpan.FromSeconds(seconds), hidden: true);
        Assert.Equal(SessionClockLabel.HiddenLockdownClock, shown);
        Assert.DoesNotMatch("[0-9]", shown);
        // Same width class as "00:00" so the page (and the exit handle) does not move.
        Assert.Equal("00:00".Length, shown.Length);
    }

    [Fact]
    public void Rail_chip_masks_too_and_keeps_total_minutes_otherwise()
    {
        var ninety = TimeSpan.FromMinutes(90);
        Assert.Equal("90:00", SessionClockLabel.RailClock(ninety, hidden: false));
        Assert.Equal(SessionClockLabel.HiddenLockdownClock, SessionClockLabel.RailClock(ninety, hidden: true));
    }

    [Fact]
    public void Stop_button_carries_the_clock_only_when_the_countdown_is_on()
    {
        var t = new TimeSpan(0, 12, 5);
        Assert.Equal("STOP SESSION (12:05)", SessionClockLabel.StopButton(t, true, StopWithClock, StopPlain));
        Assert.Equal("STOP SESSION", SessionClockLabel.StopButton(t, false, StopWithClock, StopPlain));
    }

    [Fact]
    public void Start_button_keeps_name_and_pause_mark_without_the_clock()
    {
        var t = new TimeSpan(0, 3, 7);
        Assert.Equal("Deep Dive 03:07 [PAUSED]",
            SessionClockLabel.StartButton("Deep Dive", t, true, " [PAUSED]", StartWithClock));
        Assert.Equal("Deep Dive [PAUSED]",
            SessionClockLabel.StartButton("Deep Dive", t, false, " [PAUSED]", StartWithClock));
        Assert.Equal("Deep Dive",
            SessionClockLabel.StartButton("Deep Dive", t, false, "", StartWithClock));
    }

    [Fact]
    public void A_broken_translation_shows_the_template_rather_than_throwing()
    {
        Assert.Equal("STOP {0:", SessionClockLabel.StopButton(TimeSpan.Zero, true, "STOP {0:", StopPlain));
    }

    // ---- the four new keys, in all nine files ---------------------------------------------

    private static readonly string[] Languages =
        { "en", "de", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-CN" };

    private static readonly string[] NewKeys =
    {
        "lockdown_hide_timer",
        "lockdown_hide_timer_tip",
        "set2_general_show_countdown",
        "set2_general_show_countdown_tip",
    };

    [Fact]
    public void Every_language_carries_the_two_toggles_copy()
    {
        foreach (var lang in Languages)
        {
            var path = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages", lang + ".json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var key in NewKeys)
            {
                Assert.True(doc.RootElement.TryGetProperty(key, out var value), lang + ".json is missing " + key);
                Assert.False(string.IsNullOrWhiteSpace(value.GetString()), lang + ".json has " + key + " blank");
            }
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
