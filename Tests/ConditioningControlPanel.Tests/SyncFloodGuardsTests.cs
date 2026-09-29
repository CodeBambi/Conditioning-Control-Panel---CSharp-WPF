using System;
using System.Linq;
using System.Net;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The three client-caused floods from the Sep 2026 Vercel logs: a skewed clock 403ing every sync,
/// an oversized settings backup 413ing every five minutes, and dead refresh tokens retried for ever.
/// </summary>
[Collection("ServerClockStatics")]
public class SyncFloodGuardsTests
{
    private static readonly DateTimeOffset Local = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    // ---- clock offset ----

    [Fact]
    public void Offset_FiveHoursFast_IsAdopted()
    {
        var offset = ServerClock.ComputeOffset(Local.AddHours(5), Local);
        Assert.Equal(TimeSpan.FromHours(5), offset);
    }

    [Fact]
    public void Offset_InsideDeadband_ReadsAsZero()
    {
        Assert.Equal(TimeSpan.Zero, ServerClock.ComputeOffset(Local.AddSeconds(3), Local));
        Assert.Equal(TimeSpan.Zero, ServerClock.ComputeOffset(Local.AddSeconds(-4), Local));
    }

    [Fact]
    public void Offset_Absurd_OrMissing_IsIgnored()
    {
        Assert.Null(ServerClock.ComputeOffset(Local.AddDays(30), Local));
        Assert.Null(ServerClock.ComputeOffset(null, Local));
    }

    [Fact]
    public void Observe_CorrectsUtcNow_AndReportsTheMove()
    {
        ServerClock.ResetForTests();
        try
        {
            Assert.True(ServerClock.Observe(Local.AddSeconds(-18002), Local));
            Assert.Equal(-18002, ServerClock.Offset.TotalSeconds, 0);
            var skew = (ServerClock.UtcNow - DateTimeOffset.UtcNow).TotalSeconds;
            Assert.InRange(skew, -18003, -18001);
            // Same answer again is not a move.
            Assert.False(ServerClock.Observe(Local.AddSeconds(-18002), Local));
            // A bogus header leaves the learned offset alone.
            Assert.False(ServerClock.Observe(Local.AddYears(3), Local));
            Assert.Equal(-18002, ServerClock.Offset.TotalSeconds, 0);
        }
        finally { ServerClock.ResetForTests(); }
    }

    [Fact]
    public void Refusal_ClockSkewBody_GivesReasonAndServerTime()
    {
        var ms = Local.ToUnixTimeMilliseconds();
        var reason = ServerClock.ParseRefusal($"{{\"error\":\"x\",\"reason\":\"clock_skew\",\"server_time\":{ms}}}", out var t);
        Assert.Equal("clock_skew", reason);
        Assert.Equal(Local, t);
    }

    [Fact]
    public void Refusal_OldPlainBody_IsTolerated()
    {
        Assert.Null(ServerClock.ParseRefusal("Forbidden", out var t));
        Assert.Null(t);
        Assert.Null(ServerClock.ParseRefusal(null, out _));
    }

    // ---- sync backoff ----

    [Fact]
    public void Backoff_DoublesFromThirtySeconds_AndCapsAtFifteenMinutes()
    {
        Assert.Equal(TimeSpan.Zero, SyncFailureBackoff.Delay(0));
        Assert.Equal(TimeSpan.FromSeconds(30), SyncFailureBackoff.Delay(1));
        Assert.Equal(TimeSpan.FromSeconds(60), SyncFailureBackoff.Delay(2));
        Assert.Equal(TimeSpan.FromSeconds(480), SyncFailureBackoff.Delay(5));
        Assert.Equal(TimeSpan.FromMinutes(15), SyncFailureBackoff.Delay(6));
        Assert.Equal(TimeSpan.FromMinutes(15), SyncFailureBackoff.Delay(1000));
    }

    [Fact]
    public void Backoff_ForeverRefusedClient_StaysUnderAFewRequestsAMinute()
    {
        // Worst case: every caller hammers at once, every attempt through the gate is refused.
        var now = DateTime.UtcNow;
        var end = now.AddHours(1);
        DateTime? until = null;
        var failures = 0;
        var sent = 0;
        for (var t = now; t < end; t = t.AddMilliseconds(500))
        {
            if (SyncFailureBackoff.ShouldSkip(t, until, "tok", "tok", TimeSpan.Zero, TimeSpan.Zero)) continue;
            sent++;
            failures++;
            until = t + SyncFailureBackoff.Delay(failures);
        }
        Assert.True(sent <= 12, $"sent {sent} in an hour");
    }

    [Fact]
    public void Backoff_OpensForANewToken_OrAMovedClock()
    {
        var now = DateTime.UtcNow;
        var until = now.AddMinutes(5);
        Assert.True(SyncFailureBackoff.ShouldSkip(now, until, "a", "a", TimeSpan.Zero, TimeSpan.Zero));
        Assert.False(SyncFailureBackoff.ShouldSkip(now, until, "a", "b", TimeSpan.Zero, TimeSpan.Zero));
        Assert.False(SyncFailureBackoff.ShouldSkip(now, until, "a", "a", TimeSpan.Zero, TimeSpan.FromHours(-5)));
        Assert.False(SyncFailureBackoff.ShouldSkip(until, until, "a", "a", TimeSpan.Zero, TimeSpan.Zero));
        Assert.False(SyncFailureBackoff.ShouldSkip(now, null, "a", "a", TimeSpan.Zero, TimeSpan.Zero));
    }

    // ---- settings backup size ----

    private static JObject BigLibrary(int files)
    {
        var rng = new Random(7);
        var paths = new JArray(Enumerable.Range(0, files)
            .Select(i => $"folder{rng.Next(1000)}/{Guid.NewGuid():N}_{i}.gif"));
        return new JObject
        {
            ["MasterVolume"] = 50,
            ["ActiveAssetPaths"] = new JArray(),
            ["DisabledAssetPaths"] = paths,
            ["AssetPresets"] = new JArray(new JObject { ["Name"] = "p", ["DisabledAssetPaths"] = paths.DeepClone() }),
        };
    }

    [Fact]
    public void Backup_SmallSettings_AreSentWhole()
    {
        var obj = new JObject { ["MasterVolume"] = 50, ["DisabledAssetPaths"] = new JArray("a/b.gif") };
        var r = SettingsBackupBudget.Encode(obj);
        Assert.True(r.Fits);
        Assert.Empty(r.Trimmed);
        Assert.NotNull(obj["DisabledAssetPaths"]);
    }

    [Fact]
    public void Backup_OverBudget_TrimsAssetListsInOrder_UntilItFits()
    {
        var obj = BigLibrary(20000);
        var r = SettingsBackupBudget.Encode(obj, budget: 200_000);
        Assert.True(r.Base64.Length <= 200_000);
        // The empty legacy list was dropped on the way and fits back; both big lists stay out.
        Assert.Equal(new[] { "DisabledAssetPaths", "AssetPresets" }, r.Trimmed);
        Assert.NotNull(obj["ActiveAssetPaths"]);
        Assert.NotNull(obj["MasterVolume"]);
    }

    private static JArray Paths(int n, int seed)
    {
        var rng = new Random(seed);
        return new JArray(Enumerable.Range(0, n).Select(_ => $"library/{rng.Next():x8}/{rng.Next():x8}{rng.Next():x8}.jpg"));
    }

    private static JArray Presets(params int[] pathCounts) => new(pathCounts.Select((n, i) =>
        new JObject { ["Name"] = $"preset {i}", ["DisabledAssetPaths"] = Paths(n, 100 + i) }));

    [Fact]
    public void Backup_SmallPresets_AreKept_WhenThePathListIsTheBulk()
    {
        var obj = new JObject { ["PlayerLevel"] = 42, ["AssetPresets"] = Presets(20, 20), ["DisabledAssetPaths"] = Paths(40_000, 3) };
        var r = SettingsBackupBudget.Encode(obj);
        Assert.True(r.Fits);
        Assert.Equal(new[] { "DisabledAssetPaths" }, r.Trimmed);
        Assert.NotNull(obj["AssetPresets"]);
    }

    [Fact]
    public void Backup_EitherAloneFits_ThePresetsStay()
    {
        var obj = new JObject { ["AssetPresets"] = Presets(4_000), ["DisabledAssetPaths"] = Paths(4_000, 3) };
        var alone = SettingsBackupBudget.Encode(new JObject { ["DisabledAssetPaths"] = Paths(4_000, 3) }).Base64.Length;
        var r = SettingsBackupBudget.Encode(obj, budget: alone + alone / 2);
        Assert.Equal(new[] { "DisabledAssetPaths" }, r.Trimmed);
        Assert.NotNull(obj["AssetPresets"]);
    }

    [Fact]
    public void Backup_PresetsAreTheBulk_ThePathListComesBack()
    {
        var obj = new JObject { ["AssetPresets"] = Presets(20_000, 20_000), ["DisabledAssetPaths"] = Paths(100, 3) };
        var r = SettingsBackupBudget.Encode(obj, budget: 200_000);
        Assert.True(r.Fits);
        Assert.Equal(new[] { "AssetPresets" }, r.Trimmed);
        Assert.NotNull(obj["DisabledAssetPaths"]);
    }

    [Fact]
    public void Backup_StillOverAfterTrimming_DoesNotFit_AndNamesTheCulprit()
    {
        var obj = new JObject
        {
            ["Huge"] = new JArray(Enumerable.Range(0, 5000).Select(_ => Guid.NewGuid().ToString("N"))),
            ["Small"] = 1,
        };
        var r = SettingsBackupBudget.Encode(obj, budget: 10_000);
        Assert.False(r.Base64.Length <= 10_000);
        Assert.Equal("Huge", SettingsBackupBudget.LargestProperties(obj, 1).Single().Name);
    }

    [Fact]
    public void Backup_DefaultBudget_StaysUnderTheServerLimit()
    {
        Assert.True(SettingsBackupBudget.MaxEncodedBytes < 700 * 1000);
    }

    // ---- dead refresh grants ----

    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{\"error\":\"x\",\"reason\":\"grant_dead\"}")]
    [InlineData(HttpStatusCode.InternalServerError, "{\"error\":\"invalid_grant\"}")]
    public void DeadGrantBody_IsRefusedAtOnce_EvenWithAFreshExpiry(HttpStatusCode status, string body)
    {
        var outcome = PatreonGrantHealth.Classify(status, null, false, Now.AddMinutes(-5), Now, body);
        Assert.Equal(PatreonRefreshOutcome.Refused, outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "{\"reason\":\"provider_config\"}")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "{\"reason\":\"provider_busy\",\"retry_after\":30}")]
    [InlineData(HttpStatusCode.BadGateway, "{\"error\":\"Provider unavailable\",\"reason\":\"provider_unavailable\"}")]
    [InlineData(HttpStatusCode.InternalServerError, "{\"error\":\"Internal server error\",\"reason\":\"server_error\"}")]
    public void ProviderTrouble_IsNeverTheGrant_HoweverStale(HttpStatusCode status, string body)
    {
        var outcome = PatreonGrantHealth.Classify(status, null, false, Now.AddDays(-30), Now, body);
        Assert.Equal(PatreonRefreshOutcome.Unavailable, outcome);
    }

    [Fact]
    public void PlainInternalError_KeepsTheOldStaleRule()
    {
        Assert.Equal(PatreonRefreshOutcome.Unavailable, PatreonGrantHealth.Classify(
            HttpStatusCode.InternalServerError, null, false, Now.AddHours(-1), Now, "{\"error\":\"Internal server error\"}"));
    }

    [Fact]
    public void DeadRefreshTokens_BlockOnlyTheRefusedToken()
    {
        DeadRefreshTokens.ResetForTests();
        try
        {
            Assert.False(DeadRefreshTokens.IsDead("old"));
            DeadRefreshTokens.MarkDead("old");
            Assert.True(DeadRefreshTokens.IsDead("old"));
            Assert.False(DeadRefreshTokens.IsDead("new-after-reconnect"));
            Assert.False(DeadRefreshTokens.IsDead(null));
            DeadRefreshTokens.MarkDead(null);
        }
        finally { DeadRefreshTokens.ResetForTests(); }
    }
}

[CollectionDefinition("ServerClockStatics", DisableParallelization = true)]
public class ServerClockStaticsCollection { }
