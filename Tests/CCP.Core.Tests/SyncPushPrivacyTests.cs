using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The six consent flags (WPF ProfileSyncService sync body, allow_discord_dm .. goon_share_dm). Adopt first,
/// push second: nothing before the load read the account's values; then the two the profile returns on every sync,
/// and all six for the account whose switches were set on this install. A local default never overwrites the account.</summary>
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
        var body = JObject.Parse(JsonConvert.SerializeObject(SyncPush.Body(s, null, consent: SyncPush.Privacy)));
        Assert.False((bool)body["allow_discord_dm"]!);
        Assert.False((bool)body["show_online_status"]!);
        Assert.True((bool)body["share_profile_picture"]!);
        Assert.False((bool)body["public_share_avatar"]!);
        Assert.True((bool)body["goon_share_avatar"]!);
        Assert.False((bool)body["goon_share_dm"]!);
    }

    [Fact]
    public void BeforeTheLoadAdoptedNothingIsSent_AfterItTheTwoTheProfileReturns()
    {
        var s = new AppSettings { UnifiedId = "u_1" };
        Assert.Equal(SyncBody.Field.None, SyncPush.ConsentFields(s, adopted: false));
        Assert.Equal(SyncPush.AdoptedConsent, SyncPush.ConsentFields(s, adopted: true));

        var body = JObject.Parse(JsonConvert.SerializeObject(SyncPush.Body(s, null, consent: SyncPush.ConsentFields(s, adopted: true))));
        Assert.True(body.ContainsKey("allow_discord_dm"));
        Assert.True(body.ContainsKey("show_online_status"));
        foreach (var key in new[] { "share_profile_picture", "public_share_avatar", "goon_share_avatar", "goon_share_dm" })
            Assert.False(body.ContainsKey(key), key);
    }

    [Fact]
    public void TheAccountWhoseSwitchesWereSetHereSendsAllSix_AnotherAccountDoesNot()
    {
        var s = new AppSettings { UnifiedId = "u_1", ConsentOwnedAccount = "u_1" };
        Assert.Equal(SyncPush.Privacy, SyncPush.ConsentFields(s, adopted: true));
        Assert.Equal(SyncPush.Privacy, SyncPush.ConsentFields(s, adopted: false));   // a change made here is owed even then

        s.UnifiedId = "u_2";
        Assert.Equal(SyncPush.AdoptedConsent, SyncPush.ConsentFields(s, adopted: true));
    }

    [Fact]
    public void TheAccountsValuesReplaceLocalDefaultsUnlessAChangeHereIsStillOwed()
    {
        var node = JObject.Parse("{\"allow_discord_dm\":true,\"show_online_status\":false}");
        var s = new AppSettings { UnifiedId = "u_1" };              // defaults: no DM, online shown
        Assert.True(ProfileAdopt.AdoptConsent(s, node));
        Assert.True(s.AllowDiscordDm);
        Assert.False(s.ShowOnlineStatus);                           // the account hides it: the default must not re-share
        Assert.False(ProfileAdopt.AdoptConsent(s, node));           // idempotent

        // A revoke made here and not delivered yet is newer than the server's value.
        var owed = new AppSettings { UnifiedId = "u_1", ConsentOwnedAccount = "u_1", ConsentPushPending = true, AllowDiscordDm = false };
        Assert.False(ProfileAdopt.AdoptConsent(owed, node));
        Assert.False(owed.AllowDiscordDm);

        // Absent or non-boolean values adopt nothing.
        var keep = new AppSettings { UnifiedId = "u_1", AllowDiscordDm = true };
        Assert.False(ProfileAdopt.AdoptConsent(keep, JObject.Parse("{\"allow_discord_dm\":\"yes\"}")));
        Assert.True(keep.AllowDiscordDm);
    }
}
