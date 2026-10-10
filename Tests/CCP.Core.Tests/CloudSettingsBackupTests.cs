using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// Cloud settings backup (ledger G7 / HA8): what leaves the machine, what a restore may change, and
/// that signed out means no call. A fake handler answers every request: no network.
/// </summary>
[Collection(SessionStatics.Name)]
public sealed class CloudSettingsBackupTests
{
    private sealed class Fake : HttpMessageHandler
    {
        public readonly List<(string Path, string? Token, string Body)> Calls = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Answer = _ => Json(HttpStatusCode.OK, "{\"success\":true}");
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.Content == null ? "" : await r.Content.ReadAsStringAsync(ct);
            Calls.Add((r.RequestUri!.AbsolutePath, r.Headers.TryGetValues("X-Auth-Token", out var v) ? v.First() : null, body));
            return Answer(r);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static JObject Unpack(string base64)
    {
        using var input = new MemoryStream(Convert.FromBase64String(base64));
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return JObject.Parse(reader.ReadToEnd());
    }

    private static string PackJson(JObject o)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(o.ToString(Newtonsoft.Json.Formatting.None));
            gzip.Write(bytes, 0, bytes.Length);
        }
        return Convert.ToBase64String(output.ToArray());
    }

    private static AppSettings Signed() => new() { UnifiedId = "u-1" };

    [Fact]
    public async Task Backup_PostsTheStrippedSettings_WithTheAccountIdAndToken()
    {
        var s = Signed();
        s.CustomAssetsPath = "C:/my/folder";
        s.DiscordWebhookUrl = "https://discord.example/hook";
        s.ChasterTabEnabled = true;
        s.ChasterLockId = "lock-1";
        s.PlayerLevel = 40;
        s.KeywordTriggersOffByPanic = true;
        s.PatreonPremiumValidUntil = DateTime.UtcNow.AddDays(3);
        s.BackRoomTunnel = false;
        var http = new Fake();
        var client = new CloudSettingsBackup(() => s, http, () => "tok-1");

        Assert.True(await client.BackupAsync());

        var call = Assert.Single(http.Calls);
        Assert.Equal("/v2/user/backup-settings", call.Path);
        Assert.Equal("tok-1", call.Token);
        var body = JObject.Parse(call.Body);
        Assert.Equal("u-1", (string?)body["unified_id"]);
        Assert.False(string.IsNullOrEmpty((string?)body["app_version"]));
        var sent = Unpack((string)body["settings_data"]!);
        foreach (var gone in new[]
                 {
                     "UnifiedId", "AuthToken", "OpenRouterApiKey", "CustomAssetsPath", "DiscordWebhookUrl", "PlayerLevel",
                     "PlayerXP", "SkillPoints", "UnlockedSkills", "UserDisplayName", "PatreonTier", "LastSeenUtc",
                     "FriendsPresenceShared", "ModPersonalityPreset", "KeywordTriggersOffByPanic",
                     "patreon_premium_valid_until", "patreon_lab_valid_until", "invite_grant_until",
                 })
            Assert.False(sent.ContainsKey(gone), gone + " must not leave the machine");
        Assert.DoesNotContain(sent.Properties(), p => p.Name.StartsWith("Chaster", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("tok-1", sent.ToString());
        Assert.DoesNotContain("discord.example", sent.ToString());
        // Ordinary settings ride along, Back Room options included (they are plain AppSettings).
        Assert.True(sent.ContainsKey("PanicKeyEnabled"));
        Assert.Equal(false, (bool?)sent["BackRoomTunnel"]);
    }

    [Fact]
    public void EveryChasterAndSecretNamedSetting_IsExcluded()
    {
        var keys = JObject.FromObject(new AppSettings()).Properties().Select(p => p.Name).ToList();
        var left = CloudSettingsBackup.BuildBackupObject(new AppSettings()).Properties().Select(p => p.Name).ToList();
        Assert.Contains(keys, k => k.StartsWith("Chaster", StringComparison.Ordinal));
        Assert.DoesNotContain(left, k => k.StartsWith("Chaster", StringComparison.OrdinalIgnoreCase));
        foreach (var marker in new[] { "Token", "ApiKey", "Secret", "Password", "Webhook", "Credential" })
            Assert.DoesNotContain(left, k => k.Contains(marker, StringComparison.OrdinalIgnoreCase));
        Assert.True(CloudSettingsBackup.ChasterLocalProperties.Length >= 5);
    }

    [Theory]
    [InlineData(null, "tok")]
    [InlineData("", "tok")]
    [InlineData("u-1", null)]
    [InlineData("u-1", "")]
    public async Task SignedOut_MakesNoCall(string? id, string? token)
    {
        var s = new AppSettings { UnifiedId = id };
        var http = new Fake();
        var client = new CloudSettingsBackup(() => s, http, () => token);

        Assert.False(client.HasIdentity);
        Assert.False(await client.BackupAsync());
        Assert.Null(await client.GetInfoAsync());
        Assert.Null(await client.DownloadAsync());
        Assert.Empty(http.Calls);
    }

    [Fact]
    public async Task OfflineMode_NeverUploads_AndARefusalIsFalse()
    {
        var s = Signed();
        var http = new Fake { Answer = _ => Json(HttpStatusCode.InternalServerError, "{\"error\":\"x\"}") };
        var client = new CloudSettingsBackup(() => s, http, () => "tok");
        Assert.False(await client.BackupAsync());
        Assert.Single(http.Calls);

        s.OfflineMode = true;
        Assert.False(await client.BackupAsync());
        Assert.Single(http.Calls);
    }

    [Fact]
    public async Task Info_ReadsDateAndVersion_NoBackupIsNull_AFailedCheckThrows()
    {
        var s = Signed();
        var http = new Fake
        {
            Answer = _ => Json(HttpStatusCode.OK,
                "{\"success\":true,\"backup\":{\"app_version\":\"7.1.5\",\"backed_up_at\":\"2026-10-09T18:30:00Z\",\"size_bytes\":1234}}"),
        };
        var client = new CloudSettingsBackup(() => s, http, () => "tok");
        var info = await client.GetInfoAsync();
        Assert.Equal("7.1.5", info!.AppVersion);
        Assert.Equal(new DateTime(2026, 10, 9, 18, 30, 0, DateTimeKind.Utc), info.BackedUpAt!.Value.ToUniversalTime());
        Assert.Equal(1234, info.SizeBytes);
        Assert.Equal("/v2/user/settings-backup", http.Calls[0].Path);
        Assert.Equal("u-1", (string?)JObject.Parse(http.Calls[0].Body)["unified_id"]);

        http.Answer = _ => Json(HttpStatusCode.OK, "{\"success\":true,\"backup\":null}");
        Assert.Null(await client.GetInfoAsync());

        http.Answer = _ => throw new HttpRequestException("down");
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetInfoAsync());
        Assert.Null(await client.DownloadAsync());   // the restore path never throws
    }

    [Fact]
    public async Task Restore_RoundTrips_AndThisMachineKeepsItsOwn()
    {
        // The machine that made the backup.
        var source = Signed();
        source.FlashFrequency = 9;
        source.StrictLockEnabled = false;
        var up = new Fake();
        Assert.True(await new CloudSettingsBackup(() => source, up, () => "tok").BackupAsync());
        var data = (string)JObject.Parse(up.Calls[0].Body)["settings_data"]!;

        // The machine it lands on.
        var current = Signed();
        current.CustomAssetsPath = "/home/me/content";
        current.DiscordWebhookUrl = "https://discord.example/mine";
        current.PlayerLevel = 77;
        current.SkillPoints = 12;
        current.UserDisplayName = "Me";
        current.ChasterTabEnabled = true;
        current.ChasterLockId = "lock-9";
        current.ChasterPrices = new List<string> { "a", "b" };
        current.FriendsPresenceShared = true;
        current.DisabledAssetPaths = new HashSet<string> { "x/y.png" };
        var down = new Fake { Answer = _ => Json(HttpStatusCode.OK, "{\"backup\":{\"app_version\":\"7.1.5\",\"settings_data\":\"" + data + "\"}}") };
        var restored = await new CloudSettingsBackup(() => current, down, () => "tok").DownloadAsync();
        Assert.NotNull(restored);
        CloudSettingsBackup.ApplyLocalWins(current, restored);

        Assert.Equal(9, restored!.FlashFrequency);                       // the backup's ordinary settings land
        Assert.Equal("u-1", restored.UnifiedId);
        Assert.Equal("/home/me/content", restored.CustomAssetsPath);     // no Windows path lands on Linux, or the reverse
        Assert.Equal("https://discord.example/mine", restored.DiscordWebhookUrl);
        Assert.Equal(77, restored.PlayerLevel);
        Assert.Equal(12, restored.SkillPoints);
        Assert.Equal("Me", restored.UserDisplayName);
        Assert.True(restored.ChasterTabEnabled);
        Assert.Equal("lock-9", restored.ChasterLockId);
        Assert.Equal(new[] { "a", "b" }, restored.ChasterPrices);
        Assert.NotSame(current.ChasterPrices, restored.ChasterPrices);
        Assert.True(restored.FriendsPresenceShared);
        Assert.Contains("x/y.png", restored.DisabledAssetPaths);         // an empty list may mean "not carried"
    }

    [Fact]
    public void AHostileBackup_CanNotLoosenSafety_OrPlantIdentityChasterOrSecrets()
    {
        var evil = JObject.FromObject(new AppSettings());
        evil["StrictLockEnabled"] = true;
        evil["BubbleCountStrictLock"] = true;
        evil["PanicKeyEnabled"] = false;
        evil["ScreenOcrEnabled"] = true;
        evil["KeywordTriggersOffByPanic"] = false;
        evil["UnifiedId"] = "someone-else";
        evil["CustomAssetsPath"] = "Z:/planted";
        evil["DiscordWebhookUrl"] = "https://evil.example/hook";
        evil["ChasterTabEnabled"] = true;
        evil["ChasterDailyLimitMinutes"] = 9999;
        evil["PlayerLevel"] = 999;
        evil["patreon_premium_valid_until"] = "2099-01-01T00:00:00Z";

        var current = Signed();
        current.StrictLockEnabled = false;
        current.BubbleCountStrictLock = false;
        current.PanicKeyEnabled = true;
        current.ScreenOcrEnabled = false;
        current.KeywordTriggersOffByPanic = true;
        current.ChasterTabEnabled = false;
        var limit = current.ChasterDailyLimitMinutes;

        var restored = CloudSettingsBackup.Decode(PackJson(evil))!;
        CloudSettingsBackup.ApplyLocalWins(current, restored);

        Assert.False(restored.StrictLockEnabled);
        Assert.False(restored.BubbleCountStrictLock);
        Assert.True(restored.PanicKeyEnabled);
        Assert.False(restored.ScreenOcrEnabled);
        Assert.True(restored.KeywordTriggersOffByPanic);
        Assert.Equal("u-1", restored.UnifiedId);
        Assert.Equal(current.CustomAssetsPath, restored.CustomAssetsPath);
        Assert.Equal(current.DiscordWebhookUrl, restored.DiscordWebhookUrl);
        Assert.False(restored.ChasterTabEnabled);
        Assert.Equal(limit, restored.ChasterDailyLimitMinutes);
        Assert.Equal(current.PlayerLevel, restored.PlayerLevel);
        Assert.Equal(current.PatreonPremiumValidUntil, restored.PatreonPremiumValidUntil);
    }

    [Fact]
    public void ARestore_MayTightenSafety_NeverLoosenIt()
    {
        // Strict Lock on here, off in the backup: off lands (less strict is always allowed).
        var current = Signed();
        current.StrictLockEnabled = true;
        current.PanicKeyEnabled = false;
        var restored = new AppSettings { StrictLockEnabled = false, PanicKeyEnabled = true, ScreenOcrEnabled = true };
        CloudSettingsBackup.HoldSafetyFloor(current, restored);
        Assert.False(restored.StrictLockEnabled);
        Assert.True(restored.PanicKeyEnabled);
        Assert.True(restored.ScreenOcrEnabled);   // no panic switched it off here: the backup's value stands

        // Panic off on both sides stays the player's own earlier choice; the restore did not make it.
        var both = new AppSettings { PanicKeyEnabled = false };
        CloudSettingsBackup.HoldSafetyFloor(current, both);
        Assert.False(both.PanicKeyEnabled);
    }

    [Fact]
    public void OverBudget_DropsThePerFileLists_AndSaysWhich()
    {
        var rng = new Random(7);
        var obj = new JObject
        {
            ["FlashFrequency"] = 3,
            ["DisabledAssetPaths"] = new JArray(Enumerable.Range(0, 4000).Select(i => (object)$"{rng.Next()}/{rng.Next()}-{i}.png")),
        };
        var small = CloudSettingsBackup.Encode((JObject)obj.DeepClone());
        Assert.True(small.Fits);
        Assert.Empty(small.Trimmed);

        var tight = CloudSettingsBackup.Encode(obj, budget: 2000);
        Assert.True(tight.Fits);
        Assert.Equal(new[] { "DisabledAssetPaths" }, tight.Trimmed);
        Assert.False(obj.ContainsKey("DisabledAssetPaths"));
        Assert.True(obj.ContainsKey("FlashFrequency"));
    }
}
