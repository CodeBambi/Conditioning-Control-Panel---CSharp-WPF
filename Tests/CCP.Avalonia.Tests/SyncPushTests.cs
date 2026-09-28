using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Unit 7c: the Avalonia push (Core SyncPush through AccountSeed) against a fake handler - it never reaches a real
/// server. Part of AccountSeedTests: the same statics (CoreAccount, CoreSettings.Current, AccountSeed.Sync).
/// </summary>
public sealed partial class AccountSeedTests
{
    private static readonly string[] Allowed = { "unified_id", "xp", "level", "descent_epoch", "achievements" };

    /// <summary>Records every request with its body; profile GET answers <see cref="Profile"/> (500 when null).</summary>
    private sealed class SyncWire : HttpMessageHandler
    {
        public readonly List<(string Path, JObject? Body)> Seen = new();
        public string? Profile;
        public HttpStatusCode SyncStatus = HttpStatusCode.OK;
        public string SyncReply = "{\"success\":true}";
        public IEnumerable<JObject> Syncs => Seen.Where(r => r.Path == "POST /v2/user/sync").Select(r => r.Body!);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.Content == null ? null : JObject.Parse(await r.Content.ReadAsStringAsync(ct));
            var path = $"{r.Method} {r.RequestUri!.AbsolutePath}";
            lock (Seen) Seen.Add((path, body));
            if (path == "GET /v2/user/profile")
                return Profile == null ? new(HttpStatusCode.InternalServerError) { Content = new StringContent("{}") }
                    : new(HttpStatusCode.OK) { Content = new StringContent(Profile) };
            if (path == "POST /v2/user/sync") return new(SyncStatus) { Content = new StringContent(SyncReply) };
            return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    private static readonly DateTime T0 = new(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Signs account <paramref name="id"/> in over a fresh default install and wires the push to the fake.</summary>
    private (SyncWire Wire, SyncPush Sync, List<string> Local) SignIn(string id, string? profile, SyncWire? wire = null)
    {
        var s = CoreSettings.Current;
        wire ??= new SyncWire();
        wire.Profile = profile;
        var local = new List<string>();
        var sync = new SyncPush(() => local, () => true, wire);
        var now = T0;
        sync.UtcNow = () => now;
        AccountSeed.Sync = sync;
        AccountSeed.NewV2 = () => new V2AuthService(() => s, wire);
        (s.UnifiedId, s.AuthToken, s.OfflineMode, s.CurrentSeason) = (id, "tok", false, "2026-08");
        CoreAccount.UnifiedUserId = id;
        return (wire, sync, local);
    }

    private static string L40Profile(string id, params string[] achievements) =>
        new JObject { ["user"] = new JObject { ["unified_id"] = id, ["level"] = 40, ["xp"] = L40Xp,
            ["current_season"] = "2026-08", ["achievements"] = new JArray(achievements) } }.ToString();

    private static int L40Xp => (int)XpCurve.CumulativeXpToReachLevel(40, ProfileAdopt.Epoch(CoreSettings.Current)) + 250;

    private void WithFreshInstall(Func<Task> body) => Fresh(body).GetAwaiter().GetResult();

    private async Task Fresh(Func<Task> body)
    {
        var s = CoreSettings.Current;
        var saved = Touched.ToDictionary(n => n, n => Get(s, n));
        var (oldToken, oldNew, oldOffline) = (s.AuthToken, AccountSeed.NewV2, s.OfflineMode);
        try
        {
            Assert.True(AccountSeed.Seed(Make, p => new DiscordAccount(() => p, () => new AppSettings())));
            ProgressionClear.Apply(s);
            s.DescentMigrationCompleted = false;
            await body();
        }
        finally
        {
            AccountSeed.Sync?.Reset();
            AccountSeed.Sync = null;
            foreach (var n in Touched) Set(s, n, saved[n]);
            (s.AuthToken, AccountSeed.NewV2, s.OfflineMode) = (oldToken, oldNew, oldOffline);
            CoreAccount.UnifiedUserId = null;
        }
    }

    [Fact]
    public void NoPush_BeforeASuccessfulLoad_NorAfterLogout_UntilTheNextLoad() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", profile: null);
        Assert.False(await sync.PushAsync("level-up"));           // signed in, never loaded
        Assert.False(await AccountSeed.LoadProfileAsync());       // the load fails (500)
        Assert.False(await sync.PushAsync("level-up"));
        Assert.Empty(wire.Syncs);

        wire.Profile = L40Profile("u1");
        Assert.True(await AccountSeed.LoadProfileAsync());
        Assert.Single(wire.Syncs);                                // the push after the read

        await AccountSeed.Logout();
        var s = CoreSettings.Current;                             // signed straight back in, same push, not loaded yet
        (s.UnifiedId, s.AuthToken) = ("u1", "tok");
        CoreAccount.UnifiedUserId = "u1";
        sync.UtcNow = () => T0.AddMinutes(5);
        Assert.False(await sync.PushAsync("level-up"));
        Assert.Single(wire.Syncs);
    });

    [Fact]
    public void FreshLinuxInstall_AfterLoad_SendsOnlyKnownFields_NeverLowerThanTheServer() => WithFreshInstall(async () =>
    {
        var (wire, _, local) = SignIn("u1", L40Profile("u1", "first_steps", "night_owl"));
        local.Add("linux_only");
        Assert.True(await AccountSeed.LoadProfileAsync());

        var body = Assert.Single(wire.Syncs);
        Assert.All(body.Properties(), p => Assert.Contains(p.Name, Allowed));
        foreach (var k in new[] { "stats", "allow_discord_dm", "public_share_avatar", "web_xp_claim_ack", "install_date",
                     "reset_weekly_quest", "reset_daily_quest", "force_streak_override", "force_skills_reset", "cosmetics" })
            Assert.Null(body[k]);
        Assert.Equal("u1", (string?)body["unified_id"]);
        Assert.True((int)body["xp"]! >= L40Xp, $"xp {body["xp"]} < {L40Xp}");
        Assert.Equal(40, (int)body["level"]!);
        Assert.Equal(1, (int)body["descent_epoch"]!);
        Assert.Superset(new HashSet<string> { "first_steps", "night_owl", "linux_only" },
            body["achievements"]!.Values<string>().ToHashSet()!);
    });

    [Fact]
    public void LevelReset_TheNextBodyCarriesTheResetLevel() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", L40Profile("u1"));
        wire.SyncReply = """{"success":true,"level_reset":true,"user":{"level":1,"xp":0,"current_season":"2026-08","highest_level_ever":1}}""";
        Assert.True(await AccountSeed.LoadProfileAsync());
        Assert.Equal(1, CoreSettings.Current.PlayerLevel);

        wire.SyncReply = "{\"success\":true}";
        sync.UtcNow = () => T0.AddSeconds(31);
        Assert.True(await sync.PushAsync("level-up"));
        var next = wire.Syncs.Last();
        Assert.Equal(1, (int)next["level"]!);
        Assert.True((int)next["xp"]! < 100);
    });

    [Fact]
    public void RateLimited_NoPushWithinTheCooldown() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", L40Profile("u1"));
        wire.SyncStatus = (HttpStatusCode)429;
        Assert.True(await AccountSeed.LoadProfileAsync());
        Assert.Single(wire.Syncs);

        wire.SyncStatus = HttpStatusCode.OK;
        sync.UtcNow = () => T0.AddSeconds(10);
        Assert.False(await sync.PushAsync("level-up"));
        Assert.Single(wire.Syncs);
        sync.UtcNow = () => T0.AddSeconds(31);
        Assert.True(await sync.PushAsync("level-up"));
        Assert.Equal(2, wire.Syncs.Count());
    });

    [Fact]
    public void Heartbeat_SendsWpfsFourFields() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", profile: null);
        Assert.True(await sync.HeartbeatAsync());
        var body = wire.Seen.Single(r => r.Path == "POST /v2/user/heartbeat").Body!;
        Assert.Equal(new[] { "unified_id", "is_active", "in_session", "app_version" }, body.Properties().Select(p => p.Name));
        Assert.Equal("u1", (string?)body["unified_id"]);
        Assert.True((bool)body["is_active"]!);
        Assert.True((bool)body["in_session"]!);                  // the SessionRunner seam says running
        Assert.Equal(CoreReleaseContent.AppVersion, (string?)body["app_version"]);
    });

    [Fact]
    public void Logout_ClearsProgression_AndTheNextAccountDoesNotInheritXp() => WithFreshInstall(async () =>
    {
        var (wire, _, _) = SignIn("u1", L40Profile("u1"));
        Assert.True(await AccountSeed.LoadProfileAsync());
        Assert.Equal(40, CoreSettings.Current.PlayerLevel);

        await AccountSeed.Logout();
        Assert.Equal(1, CoreSettings.Current.PlayerLevel);
        Assert.Equal(0, CoreSettings.Current.PlayerXP);
        Assert.Equal(0, CoreSettings.Current.LastConfirmedServerXp);

        var second = """{"user":{"unified_id":"u2","level":2,"xp":900,"current_season":"2026-08"}}""";
        SignIn("u2", second, wire);
        Assert.True(await AccountSeed.LoadProfileAsync());
        var body = wire.Syncs.Last();
        Assert.Equal("u2", (string?)body["unified_id"]);
        Assert.Equal(900, (int)body["xp"]!);
        Assert.Equal(2, (int)body["level"]!);
    });

    [Fact]
    public void ProgressionBank_GatesOnLogin_LevelsUp_AndRaisesLevelUp() => WithFreshInstall(() =>
    {
        var s = CoreSettings.Current;
        var levels = new List<int>();
        void OnLevel(int l) => levels.Add(l);
        ProgressionBank.LevelUp += OnLevel;
        try
        {
            ProgressionBank.Add(5000, "Session");                // signed out: nothing banked
            Assert.Equal((1, 0.0), (s.PlayerLevel, s.PlayerXP));

            CoreAccount.UnifiedUserId = "u1";
            var cost = XpCurve.GetXPForLevel(1, ProfileAdopt.Epoch(s)) + XpCurve.GetXPForLevel(2, ProfileAdopt.Epoch(s));
            ProgressionBank.Add(cost + 5, "Session");
            Assert.Equal(3, s.PlayerLevel);
            Assert.Equal(5, s.PlayerXP, 3);
            Assert.Equal(3, s.HighestLevelEver);
            Assert.Equal(new[] { 2, 3 }, levels);
        }
        finally { ProgressionBank.LevelUp -= OnLevel; }
        return Task.CompletedTask;
    });
}
