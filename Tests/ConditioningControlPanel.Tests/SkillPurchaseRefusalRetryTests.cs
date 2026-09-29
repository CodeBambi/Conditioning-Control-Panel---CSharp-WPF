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
/// #1300 / #1818: a skill the server refuses for balance syncs once, then asks again. The sync
/// used to be skipped inside the 30 s cooldown and while another sync (the level up's own) was
/// running, so the first click still failed and the wallet dropped. Drives the REAL
/// PurchaseSkillAsync and SyncProfileAsync against a fake proxy that credits the point at sync.
/// </summary>
[Collection("ServerClockStatics")]
public class SkillPurchaseRefusalRetryTests
{
    private static readonly BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;

    private sealed class FakeProxy : HttpMessageHandler
    {
        public int Purchases, Syncs, ServerPoints;
        public HttpStatusCode SyncStatus = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/v2/user/purchase-skill", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref Purchases);
                return Json($"{{\"success\":false,\"error\":\"Not enough sparkle points\",\"skill_points\":{ServerPoints}}}");
            }
            if (path.EndsWith("/v2/user/sync", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref Syncs);
                if (SyncStatus != HttpStatusCode.OK) return Json("{\"error\":\"down\"}", SyncStatus);
                ServerPoints++; // the sync is what credits the level up
                return Json($"{{\"success\":true,\"skill_points\":{ServerPoints}}}");
            }
            return Json("{}", HttpStatusCode.NotFound);
        }

        private static Task<HttpResponseMessage> Json(string body, HttpStatusCode code = HttpStatusCode.OK)
            => Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private static (ProfileSyncService Sync, FakeProxy Proxy, AppSettings Settings, string SkillId, Action Restore) Arrange()
    {
        ServerClock.ResetForTests();
        var skill = SkillDefinition.All.First(s => s.Cost >= 2 && string.IsNullOrEmpty(s.PrerequisiteId));
        var settings = new AppSettings { UnifiedId = "u_refusal_test", AuthToken = "tok1", SkillPoints = skill.Cost, PlayerLevel = 20, PlayerXP = 500 };

        var settingsProp = typeof(App).GetProperty("Settings", BindingFlags.Static | BindingFlags.Public)!;
        var previousSettings = settingsProp.GetValue(null);
        var service = (SettingsService)RuntimeHelpers.GetUninitializedObject(typeof(SettingsService));
        typeof(SettingsService).GetProperty(nameof(SettingsService.Current))!.SetValue(service, settings);
        typeof(SettingsService).GetField("_sealed", Priv)!.SetValue(service, true); // Save() is a no-op
        settingsProp.SetValue(null, service);
        var previousUid = App.UnifiedUserId;
        App.UnifiedUserId = settings.UnifiedId;

        var proxy = new FakeProxy { ServerPoints = skill.Cost - 1 };
        var sync = new ProfileSyncService();
        typeof(ProfileSyncService).GetField("_hasLoadedProfile", Priv)!.SetValue(sync, true);
        typeof(ProfileSyncService).GetField("_httpClient", Priv)!.SetValue(sync, new HttpClient(proxy));

        return (sync, proxy, settings, skill.Id, () =>
        {
            settingsProp.SetValue(null, previousSettings);
            App.UnifiedUserId = previousUid;
            ServerClock.ResetForTests();
        });
    }

    [Fact]
    public async Task InsideTheCooldown_ItWaitsTheRestOut_ThenSyncsAndAsksAgain()
    {
        var (sync, proxy, settings, skill, restore) = Arrange();
        try
        {
            // An ordinary sync ran just under 30 s ago (a quest, the conditioning time sync).
            typeof(ProfileSyncService).GetProperty(nameof(ProfileSyncService.LastSyncTime))!
                .SetValue(sync, DateTime.Now.AddSeconds(-29));

            await sync.PurchaseSkillAsync(skill);

            Assert.Equal(1, proxy.Syncs);
            Assert.Equal(2, proxy.Purchases);
        }
        finally { restore(); }
    }

    [Fact]
    public async Task WhileASyncIsRunning_ItWaitsForIt_ThenAsksAgain()
    {
        var (sync, proxy, settings, skill, restore) = Arrange();
        var gate = (SemaphoreSlim)typeof(ProfileSyncService).GetField("_syncGate", Priv)!.GetValue(sync)!;
        await gate.WaitAsync(); // the level up's own sync holds the gate
        try
        {
            _ = Task.Delay(300).ContinueWith(_ => gate.Release());

            await sync.PurchaseSkillAsync(skill);

            Assert.Equal(1, proxy.Syncs);
            Assert.Equal(2, proxy.Purchases);
        }
        finally { restore(); }
    }

    [Fact]
    public async Task WithNoRealSync_TheWalletIsKept()
    {
        var (sync, proxy, settings, skill, restore) = Arrange();
        proxy.SyncStatus = HttpStatusCode.InternalServerError;
        var before = settings.SkillPoints;
        try
        {
            var (ok, _) = await sync.PurchaseSkillAsync(skill);

            Assert.False(ok);
            Assert.Equal(1, proxy.Purchases);
            Assert.Equal(before, settings.SkillPoints);
        }
        finally { restore(); }
    }
}
