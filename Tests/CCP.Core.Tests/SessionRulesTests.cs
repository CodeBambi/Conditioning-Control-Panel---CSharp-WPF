using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>SessionClock / SessionXp / SessionTimeline / DeferredStartQueue / SessionSettingsSnapshot
/// against WPF SessionEngine.cs (ElapsedTime :103, XP :416-426, CheckPhaseTransition :664,
/// CheckDelayedFeatures :818, SaveCurrentSettings :1053).</summary>
public sealed class SessionRulesTests
{
    [Theory]
    [InlineData(1, 1.0)] [InlineData(29, 1.0)] [InlineData(30, 1.0)] [InlineData(79, 1.49)]
    [InlineData(80, 1.5)] [InlineData(124, 2.38)] [InlineData(125, 2.4)] [InlineData(149, 3.12)]
    [InlineData(150, 3.15)] [InlineData(211, 4.98)] [InlineData(213, 5.0)] [InlineData(999, 5.0)]
    public void Multiplier_TableEdges(int level, double expected) =>
        Assert.Equal(expected, SessionXp.Multiplier(level), 6);

    [Theory]
    [InlineData(3000, 0, 1, 10.0, 2565)]   // 2500 cap, then 8 min x 8.15 = 65
    [InlineData(2500, 0, 1, 2.0, 2500)]
    [InlineData(500, 2, 1, 2.0, 300)]      // 100 XP per pause
    [InlineData(150, 2, 1, 0.0, 0)]        // penalty never goes negative
    [InlineData(100, 0, 1, 1.5, 100)]      // under 2 min: no duration bonus
    [InlineData(100, 0, 1, 3.0, 108)]      // 1 min over x 8.15
    [InlineData(1000, 0, 80, 2.0, 1500)]
    [InlineData(1000, 1, 150, 2.0, 2835)]  // (1000-100) x 3.15
    public void Compute_MatchesWpfFormula(int bonus, int pauses, int level, double minutes, int expected) =>
        Assert.Equal(expected, SessionXp.Compute(bonus, pauses, level, TimeSpan.FromMinutes(minutes)));

    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0);

    [Fact]
    public void Clock_AgreeingClocks_UseWallClockPlusPaused() =>
        Assert.Equal(TimeSpan.FromMinutes(12),
            SessionClock.Elapsed(TimeSpan.FromMinutes(2), T0, T0.AddMinutes(10), TimeSpan.FromMinutes(12).Add(TimeSpan.FromSeconds(29))));

    [Theory]
    [InlineData(31)]      // forward jump (speed hack)
    [InlineData(-7200)]   // backward jump (DST/NTP, #369)
    public void Clock_JumpPast30s_TrustsStopwatch(int jumpSeconds)
    {
        var stopwatch = TimeSpan.FromMinutes(10);
        Assert.Equal(stopwatch, SessionClock.Elapsed(TimeSpan.Zero, T0, T0.AddMinutes(10).AddSeconds(jumpSeconds), stopwatch));
    }

    [Fact]
    public void Clock_Exactly30s_StillWallClock_AndNeverNegative()
    {
        Assert.Equal(TimeSpan.FromSeconds(630),
            SessionClock.Elapsed(TimeSpan.Zero, T0, T0.AddSeconds(630), TimeSpan.FromMinutes(10)));
        Assert.Equal(TimeSpan.Zero, SessionClock.Elapsed(TimeSpan.Zero, T0, T0.AddSeconds(-10), TimeSpan.Zero));
    }

    [Fact]
    public void PhaseIndex_LastArrivedPhase()
    {
        var phases = new List<SessionPhase> { new() { StartMinute = 0 }, new() { StartMinute = 5 }, new() { StartMinute = 10 } };
        Assert.Equal(0, SessionTimeline.PhaseIndexAt(null, 7));
        Assert.Equal(0, SessionTimeline.PhaseIndexAt(phases, 4.99));
        Assert.Equal(1, SessionTimeline.PhaseIndexAt(phases, 5));
        Assert.Equal(2, SessionTimeline.PhaseIndexAt(phases, 60));
    }

    [Fact]
    public void DeferredQueue_FiresOnceWhenDue_SurvivesAThrowingStart_Clears()
    {
        var q = new DeferredStartQueue();
        var fired = new List<string>();
        q.Defer("flash", 1, () => fired.Add("flash"));
        q.Defer("bad", 0, () => throw new InvalidOperationException());
        q.Defer("lock cards", 2, () => fired.Add("lock cards"));
        q.FireDue(0.5);
        Assert.Empty(fired);
        Assert.False(q.IsPending("bad"));
        q.FireDue(1.0);
        q.FireDue(1.5);
        Assert.Equal(new[] { "flash" }, fired);
        Assert.True(q.IsPending("lock cards"));
        q.Clear();
        q.FireDue(99);
        Assert.Equal(new[] { "flash" }, fired);
    }

    /// <summary>WPF SaveCurrentSettings' 36 fields, in order (SessionEngine.cs:1060-1114), plus
    /// SubliminalDuration (37): WPF writes it from the session and never restores it (decision log).</summary>
    private static readonly string[] WpfSnapshotFields =
    {
        "FlashEnabled", "FlashFrequency", "FlashOpacity", "FlashClickable", "CorruptionMode", "FlashAudioEnabled",
        "ImageScale", "SimultaneousImages", "SubliminalEnabled", "SubliminalFrequency", "SubliminalOpacity", "SubliminalDuration",
        "SubAudioEnabled", "SubAudioVolume", "AudioDuckingEnabled", "DuckingLevel", "PinkFilterEnabled",
        "PinkFilterOpacity", "SpiralEnabled", "SpiralOpacity", "BrainDrainEnabled", "BrainDrainIntensity",
        "BubblesEnabled", "BubblesFrequency", "BubblesClickable", "BouncingTextEnabled", "BouncingTextSpeed",
        "BouncingTextSize", "BouncingTextOpacity", "MandatoryVideosEnabled", "VideosPerHour", "LockCardEnabled",
        "LockCardFrequency", "PopQuizEnabled", "PopQuizFrequency", "BubbleCountEnabled", "BubbleCountFrequency",
    };

    private static string Json(AppSettings s) => JsonConvert.SerializeObject(s, Formatting.Indented);

    [Fact]
    public void Snapshot_RestoreIsByteIdentical_AndCoversExactlyWpfFields()
    {
        var current = new AppSettings();
        var before = Json(current);
        var snap = SessionSettingsSnapshot.Capture(current);

        // A "session" that rewrites every settable scalar; the snapshot must carry only WPF's fields.
        var session = new AppSettings();
        foreach (var p in typeof(AppSettings).GetProperties().Where(p => p.CanWrite && p.CanRead && p.GetIndexParameters().Length == 0))
        {
            var v = p.GetValue(session);
            try
            {
                if (v is bool b) p.SetValue(session, !b);
                else if (v is int i) p.SetValue(session, i == 0 ? 1 : i - 1);
                else if (v is double d) p.SetValue(session, d == 0 ? 0.5 : d / 2);
            }
            catch { /* guarded setter */ }
        }
        SessionSettingsSnapshot.Capture(session).RestoreTo(current);
        var during = JObject.Parse(Json(current));
        var start = JObject.Parse(before);
        var changed = start.Properties().Where(p => !JToken.DeepEquals(p.Value, during[p.Name])).Select(p => p.Name).OrderBy(n => n);
        Assert.Equal(WpfSnapshotFields.OrderBy(n => n), changed);

        snap.RestoreTo(current);
        Assert.Equal(before, Json(current));
    }
}
