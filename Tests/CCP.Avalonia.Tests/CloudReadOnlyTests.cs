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
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Read-only cloud (unit 6): transcribed /v2/user/profile and login bodies through a fake handler must change settings
/// exactly as WPF's adopt does. The expected values follow the pre-move WPF rules (ProfileSyncService
/// ReadServerProfileBeforePushAsync / ApplyServerCurveEpoch, V2AuthServiceHead.ApplyUserDataToSettings) by hand, with
/// App.Progression.GetTotalXP = XpCurve on the settings' epoch. Part of AccountSeedTests: both touch the same statics.
/// </summary>
public sealed partial class AccountSeedTests
{
    // local: settings before; server: the `user` node; expect: settings after. Epoch-0 costs: L1 800, L2 822, L3 843, L4 865;
    // L41 costs 1661 on curve v1 and 1703 on v2, identical below.
    private const string Golden = """
    [
      { "name": "load: server ahead adopts, arms the watermark, tier rise",
        "local":  { "PlayerLevel": 3, "PlayerXP": 100, "CurrentSeason": "2026-09" },
        "server": { "unified_id": "u1", "level": 5, "xp": 3500, "current_season": "2026-09", "effective_tier": 1 },
        "expect": { "PlayerLevel": 5, "PlayerXP": 170, "LastConfirmedServerXp": 3500, "LastConfirmedServerXpAccount": "u1",
                    "LastConfirmedServerXpSeason": "2026-09", "PatreonTier": 1, "CurrentSeason": "2026-09" } },
      { "name": "load: local higher is kept, no agreement recorded",
        "local":  { "PlayerLevel": 5, "PlayerXP": 500, "CurrentSeason": "2026-09" },
        "server": { "unified_id": "u1", "level": 5, "xp": 3500, "current_season": "2026-09" },
        "expect": { "PlayerLevel": 5, "PlayerXP": 500, "LastConfirmedServerXp": 0, "PatreonTier": 0 } },
      { "name": "load: season advances forward and clears the watermark",
        "local":  { "PlayerLevel": 3, "PlayerXP": 100, "CurrentSeason": "2026-08",
                    "LastConfirmedServerXp": 1600, "LastConfirmedServerXpAccount": "u1", "LastConfirmedServerXpSeason": "2026-08" },
        "server": { "unified_id": "u1", "level": 1, "xp": 0, "current_season": "2026-09" },
        "expect": { "PlayerLevel": 3, "PlayerXP": 100, "CurrentSeason": "2026-09", "LastConfirmedServerXp": 0,
                    "LastConfirmedServerXpAccount": null, "LastConfirmedServerXpSeason": null } },
      { "name": "load: stale server season is not adopted and not watermarked",
        "local":  { "PlayerLevel": 3, "PlayerXP": 100, "CurrentSeason": "2026-09" },
        "server": { "unified_id": "u1", "level": 5, "xp": 3500, "current_season": "2026-08" },
        "expect": { "PlayerLevel": 5, "PlayerXP": 170, "CurrentSeason": "2026-09", "LastConfirmedServerXp": 0 } },
      { "name": "load: curve epoch mismatch re-prices by total before the adopt",
        "local":  { "PlayerLevel": 42, "PlayerXP": 0, "CurrentSeason": "2026-09", "DescentEpoch": 0 },
        "server": { "unified_id": "u1", "level": 1, "xp": 0, "curve_epoch": 1, "current_season": "2026-09" },
        "expect": { "DescentEpoch": 1, "PlayerLevel": 41, "PlayerXP": 1661, "HighestLevelEver": 41 } },
      { "name": "load: ceremony pending, no re-price",
        "local":  { "PlayerLevel": 42, "PlayerXP": 0, "CurrentSeason": "2026-09", "DescentEpoch": 0, "PendingDescentMigrationChoice": "restore" },
        "server": { "unified_id": "u1", "level": 1, "xp": 0, "curve_epoch": 1, "current_season": "2026-09" },
        "expect": { "DescentEpoch": 0, "PlayerLevel": 42, "PlayerXP": 0 } },
      { "name": "login: server higher adopts, identity and 14-day grace",
        "login": true,
        "local":  { "PlayerLevel": 3, "PlayerXP": 100 },
        "server": { "unified_id": "u1", "display_name": "Bambi", "level": 5, "xp": 3500, "patreon_tier": 2, "patreon_id": "p9",
                    "current_season": "2026-09", "highest_level_ever": 7 },
        "expect": { "PlayerLevel": 5, "PlayerXP": 170, "UnifiedId": "u1", "UserDisplayName": "Bambi", "PatreonTier": 2,
                    "HasLinkedPatreon": true, "HasLinkedDiscord": false, "CurrentSeason": "2026-09", "HighestLevelEver": 7,
                    "PatreonPremiumValidUntil": "2026-01-15T00:00:00Z", "PatreonLabValidUntil": "2026-01-15T00:00:00Z" } },
      { "name": "login: local higher is kept",
        "login": true,
        "local":  { "PlayerLevel": 5, "PlayerXP": 500 },
        "server": { "unified_id": "u1", "level": 5, "xp": 3500 },
        "expect": { "PlayerLevel": 5, "PlayerXP": 500, "PatreonPremiumValidUntil": null } }
    ]
    """;

    private static readonly string[] Touched =
    {
        "PlayerLevel", "PlayerXP", "CurrentSeason", "DescentEpoch", "HighestLevelEver", "LastConfirmedServerXp",
        "LastConfirmedServerXpAccount", "LastConfirmedServerXpSeason", "PatreonTier", "UnifiedId", "UserDisplayName",
        "HasLinkedPatreon", "HasLinkedDiscord", "IsSeason0Og", "PatreonPremiumValidUntil", "PatreonLabValidUntil",
        "PendingDescentMigrationChoice",
    };

    private static JToken Get(AppSettings s, string n) =>
        JToken.FromObject(typeof(AppSettings).GetProperty(n)!.GetValue(s) ?? JValue.CreateNull());

    private static void Set(AppSettings s, string n, JToken v)
    {
        var p = typeof(AppSettings).GetProperty(n)!;
        p.SetValue(s, v.Type == JTokenType.Null ? null : v.ToObject(p.PropertyType));
    }

    /// <summary>Records every request; answers restore-session and the profile GET with <see cref="Profile"/>.</summary>
    private sealed class Wire : HttpMessageHandler
    {
        public readonly List<string> Seen = new();
        public string Profile = "{}";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Seen.Add($"{r.Method} {r.RequestUri!.AbsolutePath}");
            var body = r.RequestUri.AbsolutePath == "/v2/user/profile" ? Profile : "{\"auth_token\":\"tok\"}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task ServerResponses_ChangeSettingsExactlyAsWpfAdopts_AndNothingIsWritten()
    {
        var s = CoreSettings.Current;
        var saved = Touched.ToDictionary(n => n, n => Get(s, n));
        var oldToken = s.AuthToken;
        try
        {
            Assert.True(AccountSeed.Seed(Make, p => new DiscordAccount(() => p, () => new AppSettings())));
            var wire = new Wire();
            foreach (JObject c in JArray.Parse(Golden))
            {
                var name = (string)c["name"]!;
                foreach (var n in Touched) Set(s, n, saved[n]);
                (s.PlayerLevel, s.PlayerXP, s.CurrentSeason, s.DescentEpoch, s.HighestLevelEver, s.PatreonTier) = (1, 0, null, 0, 0, 0);
                (s.LastConfirmedServerXp, s.LastConfirmedServerXpAccount, s.LastConfirmedServerXpSeason) = (0, null, null);
                (s.PatreonPremiumValidUntil, s.PatreonLabValidUntil, s.PendingDescentMigrationChoice) = (null, null, null);
                s.UnifiedId = "u1"; s.AuthToken = "tok";
                foreach (var p in (JObject)c["local"]!) Set(s, p.Key, p.Value!);
                var server = (JObject)c["server"]!;

                if (c["login"]?.Value<bool>() == true)
                    ProfileAdopt.ApplyUserData(s, server.ToObject<V2AuthService.V2User>()!, null, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                else
                {
                    wire.Profile = new JObject { ["user"] = server }.ToString();
                    CoreAccount.UnifiedUserId = "u1";
                    await AccountSeed.ValidateRestoredSessionAsync(new V2AuthService(() => s, wire)); // startup: restore + load
                }

                foreach (var p in (JObject)c["expect"]!)
                {
                    var actual = Get(s, p.Key);
                    if (p.Value!.Type == JTokenType.Date)
                        Assert.True(p.Value.ToObject<DateTime>().ToUniversalTime() == actual.ToObject<DateTime>().ToUniversalTime(), $"{name}: {p.Key} = {actual}");
                    else
                        Assert.True(JToken.DeepEquals(p.Value, actual) || p.Value.Type is JTokenType.Integer or JTokenType.Float
                            && Math.Abs(p.Value.Value<double>() - actual.Value<double>()) < 0.001, $"{name}: {p.Key} = {actual}, want {p.Value}");
                }
                if (name.Contains("tier rise")) Assert.True(s.PatreonPremiumValidUntil > DateTime.UtcNow.AddDays(13), name);
            }

            // Unit 6 is READ-ONLY: restore-session (the WPF check) and the profile GET, nothing else.
            Assert.NotEmpty(wire.Seen);
            Assert.All(wire.Seen, r => Assert.True(r.StartsWith("GET ") || r == "POST /v2/auth/restore-session", $"write endpoint called: {r}"));
        }
        finally
        {
            foreach (var n in Touched) Set(s, n, saved[n]);
            s.AuthToken = oldToken;
            CoreAccount.UnifiedUserId = null;
        }
    }

    [Fact]
    public async Task LoginSuccess_LoadsTheProfile_WithoutAnyWriteEndpoint()
    {
        var s = CoreSettings.Current;
        var saved = Touched.ToDictionary(n => n, n => Get(s, n));
        var (oldToken, oldNew) = (s.AuthToken, AccountSeed.NewV2);
        var wire = new Wire { Profile = """{"user":{"unified_id":"u1","level":5,"xp":3500,"current_season":"2026-09"}}""" };
        try
        {
            AccountSeed.NewV2 = () => new V2AuthService(() => s, wire);
            (s.PlayerLevel, s.PlayerXP, s.CurrentSeason, s.DescentEpoch, s.PendingDescentMigrationChoice) = (1, 0, "2026-09", 0, null);
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (global::Avalonia.Application.Current is null)
                    global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .UseSkia().UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                var dialog = new global::ConditioningControlPanel.Avalonia.Views.Dialogs.LoginDialog();
                dialog.Succeed(new V2AuthService.V2User { UnifiedId = "u1", Level = 1, Xp = 0 }, "tok", null, false);
                await dialog.ProfileLoad;
            });
            Assert.Equal(5, s.PlayerLevel);                     // the login path did load and adopt
            Assert.Equal(new[] { "GET /v2/user/profile" }, wire.Seen);
        }
        finally
        {
            foreach (var n in Touched) Set(s, n, saved[n]);
            (s.AuthToken, AccountSeed.NewV2) = (oldToken, oldNew);
            CoreAccount.UnifiedUserId = null;
        }
    }
}
