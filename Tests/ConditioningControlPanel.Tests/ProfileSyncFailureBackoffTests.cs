using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #1830's sync failure backoff, driven through the REAL SyncProfileAsync and the REAL
/// ServerClockHandler against a fake proxy. App.Settings and ServerClock are process-wide
/// statics, so this runs in the non-parallel ServerClockStatics collection.
/// </summary>
[Collection("ServerClockStatics")]
public class ProfileSyncFailureBackoffTests
{
    private static readonly BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;

    private sealed class FakeProxy : HttpMessageHandler
    {
        public TimeSpan Delay = TimeSpan.Zero;
        public Func<HttpRequestMessage, HttpStatusCode?>? SyncOverride;
        public readonly List<string> Log = new();
        public int SyncRequests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/v2/user/sync", StringComparison.Ordinal)) Interlocked.Increment(ref SyncRequests);
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);

            HttpResponseMessage res;
            if (path.EndsWith("/v2/user/sync", StringComparison.Ordinal))
                res = SyncOverride?.Invoke(request) is { } code
                    ? Json(code, "{\"error\":\"forced\"}")
                    : Json(HttpStatusCode.OK, "{\"success\":true}");
            else if (path.EndsWith("/v2/auth/restore-session", StringComparison.Ordinal))
                res = Json(HttpStatusCode.OK, "{\"success\":true,\"auth_token\":\"tok2\"}");
            else
                res = Json(HttpStatusCode.NotFound, "{}");
            res.Headers.Date = DateTimeOffset.UtcNow;
            var token = request.Headers.TryGetValues("X-Auth-Token", out var t) ? t.First() : "-";
            lock (Log) Log.Add($"{path} {token} {(int)res.StatusCode}");
            return res;
        }

        private static HttpResponseMessage Json(HttpStatusCode code, string body)
            => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static (ProfileSyncService Sync, FakeProxy Proxy, AppSettings Settings, Action Restore) Arrange(
        TimeSpan? clientTimeout = null)
    {
        ServerClock.ResetForTests();
        var settings = new AppSettings { UnifiedId = "u_backoff_test", AuthToken = "tok1", SkillPoints = 5, PlayerLevel = 20, PlayerXP = 500 };
        var settingsProp = typeof(App).GetProperty("Settings", BindingFlags.Static | BindingFlags.Public)!;
        var previousSettings = settingsProp.GetValue(null);
        var service = (SettingsService)RuntimeHelpers.GetUninitializedObject(typeof(SettingsService));
        typeof(SettingsService).GetProperty(nameof(SettingsService.Current))!.SetValue(service, settings);
        typeof(SettingsService).GetField("_sealed", Priv)!.SetValue(service, true); // Save() is a no-op
        settingsProp.SetValue(null, service);
        var previousUid = App.UnifiedUserId;
        App.UnifiedUserId = settings.UnifiedId;

        var proxy = new FakeProxy();
        var sync = new ProfileSyncService();
        typeof(ProfileSyncService).GetField("_hasLoadedProfile", Priv)!.SetValue(sync, true);
        typeof(ProfileSyncService).GetField("_httpClient", Priv)!.SetValue(sync,
            new HttpClient(new ServerClockHandler { InnerHandler = proxy }) { Timeout = clientTimeout ?? TimeSpan.FromSeconds(30) });

        return (sync, proxy, settings, () =>
        {
            sync.StopHeartbeat();
            settingsProp.SetValue(null, previousSettings);
            App.UnifiedUserId = previousUid;
            ServerClock.ResetForTests();
        });
    }

    [Fact]
    public async Task SkipsDuringTheBackoff_AreNotCountedAsFailures()
    {
        var (sync, proxy, _, restore) = Arrange();
        proxy.SyncOverride = _ => HttpStatusCode.BadGateway; // one blip
        var raised = new List<int>();
        sync.SyncHealthChanged += (_, n) => raised.Add(n);
        try
        {
            await sync.SyncProfileAsync();
            for (int i = 0; i < 4; i++) await sync.SyncProfileAsync(); // XP nudge, quest, level up...

            Assert.Equal(1, proxy.SyncRequests);
            Assert.Equal(1, sync.ConsecutiveSyncFailures);
            Assert.Equal(new[] { 1 }, raised);
        }
        finally { restore(); }
    }

    [Fact]
    public async Task ATimeout_ArmsTheBackoff()
    {
        var (sync, proxy, _, restore) = Arrange(clientTimeout: TimeSpan.FromSeconds(1));
        proxy.Delay = TimeSpan.FromSeconds(3); // the proxy hangs past HttpClient.Timeout
        try
        {
            Assert.False(await sync.SyncProfileAsync());
            Assert.False(await sync.SyncProfileAsync());
            Assert.False(await sync.SyncProfileAsync());

            Assert.Equal(1, proxy.SyncRequests);
            Assert.NotNull(typeof(ProfileSyncService).GetField("_syncBlockedUntilUtc", Priv)!.GetValue(sync));
        }
        finally { restore(); }
    }

    [Fact]
    public async Task AHealed401_OpensTheGateAtOnce()
    {
        var (sync, proxy, settings, restore) = Arrange();
        // The server refuses the old token; restore-session hands out tok2.
        proxy.SyncOverride = req => req.Headers.GetValues("X-Auth-Token").First() == "tok1" ? HttpStatusCode.Unauthorized : null;
        try
        {
            Assert.False(await sync.SyncProfileAsync());
            Assert.Equal("tok2", settings.AuthToken);

            Assert.True(await sync.SyncProfileAsync(), "the sync after a healed 401 was skipped: " + string.Join(" | ", proxy.Log));
            Assert.Contains("/v2/user/sync tok2 200", proxy.Log);
        }
        finally { restore(); }
    }
}
