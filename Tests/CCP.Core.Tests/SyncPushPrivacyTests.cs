using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The six consent flags (WPF ProfileSyncService sync body, allow_discord_dm .. goon_share_dm) ride a push
/// only after a Privacy and Sharing switch changed here: an ordinary push never carries a local default.</summary>
public class SyncPushPrivacyTests
{
    private static readonly string[] Keys =
        { "allow_discord_dm", "show_online_status", "share_profile_picture", "public_share_avatar", "goon_share_avatar", "goon_share_dm" };

    [Fact]
    public void AnOrdinaryPushCarriesNoConsentFlag()
    {
        var s = new AppSettings { UnifiedId = "u_1", ShowOnlineStatus = true, ShareProfilePicture = true };
        var body = JObject.Parse(JsonConvert.SerializeObject(SyncPush.Body(s, null)));
        foreach (var key in Keys) Assert.False(body.ContainsKey(key), key);
    }

    [Fact]
    public void APrivacyPushCarriesAllSixFromSettings()
    {
        var s = new AppSettings
        {
            UnifiedId = "u_1", AllowDiscordDm = false, ShowOnlineStatus = false, ShareProfilePicture = true,
            PublicShareRealAvatar = false, GoonShareAvatar = true, GoonShareDiscordDm = false,
        };
        var body = JObject.Parse(JsonConvert.SerializeObject(SyncPush.Body(s, null, privacy: true)));
        Assert.False((bool)body["allow_discord_dm"]!);
        Assert.False((bool)body["show_online_status"]!);
        Assert.True((bool)body["share_profile_picture"]!);
        Assert.False((bool)body["public_share_avatar"]!);
        Assert.True((bool)body["goon_share_avatar"]!);
        Assert.False((bool)body["goon_share_dm"]!);
    }
}
