using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Security pass 3 on Circe's tab (2026-09-26): the pure rules. Cloud restore never carries a
/// Chaster setting, a relock catch-up rides the priced write inside the day's push ceiling, an API
/// 401 is not a dead link, a write that broke mid-flight stays in doubt, and a Remote session keeps
/// counting for a short grace after it ends.
/// </summary>
public class ChasterSecurityTests
{
    private static readonly DateTime Noon = new(2026, 9, 26, 12, 0, 0);
    private static readonly DateTime Utc = new(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Every_chaster_setting_is_found_by_name()
    {
        var names = ProfileSyncService.ChasterLocalProperties.Select(p => p.Name).ToHashSet();
        foreach (var expected in new[]
        {
            nameof(AppSettings.ChasterTabEnabled), nameof(AppSettings.ChasterPrices), nameof(AppSettings.ChasterLockId),
            nameof(AppSettings.ChasterDailyLimitMinutes), nameof(AppSettings.ChasterBacklogLimitMinutes),
            nameof(AppSettings.ChasterDailyLimitPendingMinutes), nameof(AppSettings.ChasterDailyLimitPendingAtUtc),
            nameof(AppSettings.ChasterBacklogLimitPendingMinutes), nameof(AppSettings.ChasterBacklogLimitPendingAtUtc),
            nameof(AppSettings.ChasterPaused), nameof(AppSettings.ChasterRelockPastEnd),
            nameof(AppSettings.ChasterConsentSeen), nameof(AppSettings.ChasterRafflePostDays),
        })
        {
            Assert.Contains(expected, names);
            Assert.True(ProfileSyncService.IsExcludedFromBackup(expected));
        }
        // The two composite views are [JsonIgnore]: copying their parts is enough.
        Assert.DoesNotContain(nameof(AppSettings.ChasterDayLimit), names);
        Assert.False(ProfileSyncService.IsExcludedFromBackup(nameof(AppSettings.FlashEnabled)));
    }

    [Fact]
    public void A_cloud_restore_keeps_every_chaster_setting_of_this_machine()
    {
        var pendingAt = new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc);
        var current = new AppSettings
        {
            ChasterTabEnabled = false,
            ChasterPrices = new List<string> { "typo" },
            ChasterLockId = null,
            ChasterDailyLimitMinutes = 60,
            ChasterBacklogLimitMinutes = 120,
            ChasterDailyLimitPendingMinutes = 240,
            ChasterDailyLimitPendingAtUtc = pendingAt,
            ChasterPaused = true,
        };
        // A backup from another machine (or an old one) that has the tab on, every price, raised limits.
        var restored = new AppSettings
        {
            ChasterTabEnabled = true,
            ChasterPrices = new List<string> { "typo", "session", "natasha" },
            ChasterLockId = "somebodyslock",
            ChasterDailyLimitMinutes = 720,
            ChasterBacklogLimitMinutes = 2880,
            ChasterDailyLimitPendingMinutes = 0,
            ChasterDailyLimitPendingAtUtc = null,
            ChasterPaused = false,
            ChasterRelockPastEnd = true,
            ChasterConsentSeen = true,
            ChasterRafflePostDays = true,
        };

        ProfileSyncService.PreserveLocalOnlyFields(current, restored);

        Assert.False(restored.ChasterTabEnabled);
        Assert.Equal(new[] { "typo" }, restored.ChasterPrices);
        Assert.NotSame(current.ChasterPrices, restored.ChasterPrices);
        Assert.Null(restored.ChasterLockId);
        Assert.Equal(60, restored.ChasterDailyLimitMinutes);
        Assert.Equal(120, restored.ChasterBacklogLimitMinutes);
        Assert.Equal(240, restored.ChasterDailyLimitPendingMinutes);
        Assert.Equal(pendingAt, restored.ChasterDailyLimitPendingAtUtc);
        Assert.True(restored.ChasterPaused);
        Assert.False(restored.ChasterRelockPastEnd);
        Assert.False(restored.ChasterConsentSeen);
        Assert.False(restored.ChasterRafflePostDays);
    }

    [Fact]
    public void A_catch_up_rides_the_price_inside_the_days_room()
    {
        var s = new TabState { BalanceSeconds = 900 };
        var limits = TabLimits.FromMinutes(20, 120); // 1200 s a day
        var plan = CircesTab.PlanPush(s, canRemove: false, limits, Noon);

        var fits = CircesTab.WithCatchUp(plan, 200, s, limits, Noon);
        Assert.Equal(new TabPush(TabPushKind.Add, 900, 200), fits);
        Assert.Equal(1100, fits.Total);

        var shrinks = CircesTab.WithCatchUp(plan, 600, s, limits, Noon);
        Assert.Equal(new TabPush(TabPushKind.Add, 600, 600), shrinks);

        // A catch-up with no room left for a price is dropped, not sent on its own.
        Assert.Equal(new TabPush(TabPushKind.Add, 900, 0), CircesTab.WithCatchUp(plan, 1200, s, limits, Noon));
        Assert.Equal(new TabPush(TabPushKind.None, 0), CircesTab.WithCatchUp(new TabPush(TabPushKind.None, 0), 300, s, limits, Noon));
        // Never more than a lock's own run-out limit, whatever the caller hands in.
        var big = CircesTab.WithCatchUp(new TabPush(TabPushKind.Add, 60), 99999, new TabState(), TabLimits.FromMinutes(720, 2880), Noon);
        Assert.Equal(LockRelock.MaxCatchUpSeconds + LockRelock.MarginSeconds, big.CatchUp);
    }

    [Fact]
    public void A_landed_catch_up_counts_on_the_ceiling_and_the_ladder_but_not_the_tab()
    {
        var s = new TabState { BalanceSeconds = 900 };
        CircesTab.ApplyPush(s, new TabPush(TabPushKind.Add, 600, 300), Noon, Utc);

        Assert.Equal(300, s.BalanceSeconds);
        Assert.Equal(600, s.PushedNetSeconds);
        Assert.Equal(900, CircesTab.PushedToday(s, Noon));
        Assert.Equal(900, ChasterLadder.Lifetime(s));
    }

    [Fact]
    public void A_doubted_catch_up_only_takes_the_price_off_the_tab()
    {
        var s = new TabState { BalanceSeconds = 900 };
        CircesTab.MarkPending(s, new TabPush(TabPushKind.Add, 600, 300), Noon);
        Assert.Equal(900, s.PendingSeconds);

        Assert.Equal(900, CircesTab.ResolvePending(s, Utc));
        Assert.Equal(300, s.BalanceSeconds);
        Assert.Equal(900, CircesTab.PushedToday(s, Noon));
        Assert.Equal(900, ChasterLadder.Lifetime(s));
        Assert.Equal(0, s.PendingCatchUpSeconds);
    }

    [Theory]
    [InlineData(401, false, ChasterStatus.Unauthorized)]
    [InlineData(401, true, ChasterStatus.Unauthorized)]
    [InlineData(502, true, ChasterStatus.TimedOut)]
    [InlineData(504, true, ChasterStatus.TimedOut)]
    [InlineData(502, false, ChasterStatus.Unavailable)]
    [InlineData(503, true, ChasterStatus.Unavailable)]
    [InlineData(404, true, ChasterStatus.NotFound)]
    public void The_api_maps_401_as_a_refused_token_and_a_gateway_write_as_a_doubt(int code, bool write, ChasterStatus expected)
    {
        Assert.Equal(expected, ChasterClient.MapApi((HttpStatusCode)code, write));
    }

    [Fact]
    public void The_broker_still_reads_401_as_a_dead_link()
    {
        Assert.Equal(ChasterStatus.LinkExpired, ChasterClient.Map(HttpStatusCode.Unauthorized));
    }

    [Theory]
    [InlineData(HttpRequestError.ConnectionError, true)]
    [InlineData(HttpRequestError.NameResolutionError, true)]
    [InlineData(HttpRequestError.ResponseEnded, false)]
    [InlineData(HttpRequestError.Unknown, false)]
    [InlineData(HttpRequestError.InvalidResponse, false)]
    public void Only_a_failure_to_connect_proves_a_write_never_left(HttpRequestError error, bool neverSent)
    {
        Assert.Equal(neverSent, ChasterClient.NeverSent(new HttpRequestException(error, "x")));
    }

    [Fact]
    public void A_remote_session_counts_for_a_short_grace_after_it_ends()
    {
        var ended = new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc);
        Assert.True(ChasterService.RemoteCounts(true, null, ended));
        Assert.False(ChasterService.RemoteCounts(false, null, ended));
        Assert.True(ChasterService.RemoteCounts(false, ended, ended.AddSeconds(90)));
        Assert.False(ChasterService.RemoteCounts(false, ended, ended + ChasterService.RemoteGrace));
    }
}
