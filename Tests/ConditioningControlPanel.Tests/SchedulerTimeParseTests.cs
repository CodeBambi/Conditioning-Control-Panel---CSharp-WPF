using System;
using ConditioningControlPanel.Helpers;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #984/#985/#999 - "Scheduler: Could not parse end time '2.5' / '22:0'". The scheduler used a
/// bare <c>TimeSpan.TryParse</c> on its free-text fields, so anything it disliked silently became
/// the 22:00 default and the user's configured window never ran. <see cref="SchedulerTime"/>
/// replaces it: invariant culture, hour-only / H:m / H.m / HH:mm, and clamping instead of failure.
/// </summary>
public class SchedulerTimeParseTests
{
    public static TheoryData<string, int, int> Accepted() => new()
    {
        { "2.5",    2,  5 },    // #999 - the dot is a separator, NOT "two and a half hours"
        { "22:0",  22,  0 },    // #984/#985 - single-digit minute
        { "22:00", 22,  0 },    // the canonical form must obviously still work
        { "7",      7,  0 },    // hour only. TimeSpan.TryParse read this as SEVEN DAYS
        { "25:00", 23, 59 },    // over-range hour clamps to the end of the day, never fails
        { "0",      0,  0 },
        { "00:00",  0,  0 },
        { "23:59", 23, 59 },
        { " 9:30 ", 9, 30 },    // surrounding whitespace
        { "09.05",  9,  5 },    // zero-padded dot form
        { "22:",   22,  0 },    // mid-typing an hour is not an error
        { "8:99",   8, 59 },    // over-range minute clamps
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void ParsesAndClamps(string input, int hours, int minutes)
    {
        Assert.True(SchedulerTime.TryParse(input, out var t), input + " should parse");
        Assert.Equal(new TimeSpan(hours, minutes, 0), t);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("evening")]
    [InlineData("-1")]
    [InlineData("-3:00")]
    [InlineData("1:2:3")]     // more than one separator: not a time-of-day this control accepts
    [InlineData("1:2.3")]
    [InlineData(":30")]       // no hour at all
    [InlineData("1e3")]
    public void RejectsUnusableInput(string? input)
    {
        Assert.False(SchedulerTime.TryParse(input, out _));
    }

    [Fact]
    public void ParsedTimesAreAlwaysWithinASingleDay()
    {
        foreach (var raw in new[] { "2.5", "22:0", "22:00", "7", "25:00", "99", "8:99" })
        {
            Assert.True(SchedulerTime.TryParse(raw, out var t));
            Assert.InRange(t, TimeSpan.Zero, new TimeSpan(23, 59, 0));
        }
    }

    [Fact]
    public void ParseOrDefaultFallsBackOnlyForUnusableInput()
    {
        var fallback = new TimeSpan(22, 0, 0);
        Assert.Equal(new TimeSpan(2, 5, 0), SchedulerTime.ParseOrDefault("2.5", fallback));
        Assert.Equal(fallback, SchedulerTime.ParseOrDefault("nonsense", fallback));
    }
}
