using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Invites;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Invite week, the pure half: what a typed code becomes, what the server's replies read as,
/// and how a grant opens the premium window without ever turning into the 14-day grace.
/// </summary>
public class InviteRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("PINK-MIA-7Q4X", "PINK-MIA-7Q4X")]
    [InlineData("  pink-mia-7q4x ", "PINK-MIA-7Q4X")]
    [InlineData("PINK MIA 7Q4X", "PINKMIA7Q4X")]
    [InlineData("https://cclabs.app/i/pink-mia-7q4x", "PINK-MIA-7Q4X")]
    [InlineData("cclabs.app/i/PINK-MIA-7Q4X?ref=discord", "PINK-MIA-7Q4X")]
    [InlineData("-ABC123-", "ABC123")]
    public void NormalizeCode_Accepts(string raw, string expected)
        => Assert.Equal(expected, InviteRules.NormalizeCode(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABC12")]                       // too short
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXY")]   // too long
    [InlineData("PINK_MIA")]                    // underscore
    [InlineData("PINK<script>")]
    [InlineData("cclabs.app/i/")]
    public void NormalizeCode_Rejects(string? raw)
        => Assert.Null(InviteRules.NormalizeCode(raw));

    [Fact]
    public void ApplyGrant_OpensTheWindowToTheExactEnd()
    {
        var s = new AppSettings();
        var end = Now.AddDays(7);
        Assert.True(InviteRules.ApplyGrant(s, end, Now));
        Assert.Equal(end, s.PatreonPremiumValidUntil);
        Assert.Null(s.PatreonLabValidUntil);
    }

    [Fact]
    public void ApplyGrant_NeverShortensASubscribersWindow()
    {
        var s = new AppSettings { PatreonPremiumValidUntil = Now.AddDays(14) };
        Assert.False(InviteRules.ApplyGrant(s, Now.AddDays(7), Now));
        Assert.Equal(Now.AddDays(14), s.PatreonPremiumValidUntil);
    }

    [Fact]
    public void ApplyGrant_ClampsAGrantThatClaimsMoreThanAWeek()
    {
        var s = new AppSettings();
        Assert.True(InviteRules.ApplyGrant(s, Now.AddYears(5), Now));
        Assert.Equal(Now + InviteRules.MaxGrantAhead, s.PatreonPremiumValidUntil);
    }

    [Fact]
    public void ApplyGrant_IgnoresAnEndedGrant()
    {
        var s = new AppSettings();
        Assert.False(InviteRules.ApplyGrant(s, Now.AddMinutes(-1), Now));
        Assert.False(InviteRules.ApplyGrant(s, null, Now));
        Assert.False(InviteRules.ApplyGrant(null, Now.AddDays(3), Now));
        Assert.Null(s.PatreonPremiumValidUntil);
    }

    [Fact]
    public void ApplyGrant_ReappliedEachHeartbeatDoesNotCreep()
    {
        var s = new AppSettings();
        var end = Now.AddDays(7);
        InviteRules.ApplyGrant(s, end, Now);
        Assert.False(InviteRules.ApplyGrant(s, end, Now.AddDays(3)));
        Assert.Equal(end, s.PatreonPremiumValidUntil);
    }

    [Theory]
    [InlineData("{\"invite_grant_until\":\"2026-10-09T12:00:00Z\"}", true)]
    [InlineData("{\"invite_grant_until\":\"2026-10-09T14:00:00+02:00\"}", true)]
    [InlineData("{\"invite_grant_until\":null}", false)]
    [InlineData("{\"invite_grant_until\":12345}", false)]
    [InlineData("{\"invite_grant_until\":\"next week\"}", false)]
    [InlineData("{}", false)]
    [InlineData("<html>", false)]
    [InlineData("", false)]
    public void ParseHeartbeatGrant(string body, bool expectsDate)
    {
        var got = InviteRules.ParseHeartbeatGrant(body);
        Assert.Equal(expectsDate, got.HasValue);
        if (got.HasValue) Assert.Equal(new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc), got.Value);
    }

    [Fact]
    public void ParseUtc_AcceptsAnAlreadyParsedDateToken()
    {
        var token = new JValue(new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc), InviteRules.ParseUtc(token));
    }

    [Fact]
    public void ParseSnapshot_ReadsSlotsAndTheLifetimeCount()
    {
        var reply = JObject.Parse(@"{
            ""ok"": true,
            ""resets_at"": ""2026-11-01T00:00:00Z"",
            ""converted_total"": 4,
            ""codes"": [
                { ""code"": ""pink-mia-7q4x"", ""state"": ""converted"", ""invitee_name"": ""kaycee"", ""day"": 9 },
                { ""code"": ""PINK-MIA-2B8R"", ""state"": ""trying"", ""invitee_name"": ""j.doll"", ""day"": 3 },
                { ""code"": ""PINK-MIA-K3ZZ"", ""state"": ""open"", ""invitee_name"": ""leaked"" },
                { ""code"": ""PINK-MIA-W0W0"", ""state"": ""something_new"" },
                { ""code"": ""bad code!"" }
            ]
        }");
        var snap = InviteRules.ParseSnapshot(reply);
        Assert.NotNull(snap);
        Assert.Equal(4, snap!.ConvertedTotal);
        Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), snap.ResetsAtUtc);
        Assert.Equal(4, snap.Slots.Count);
        Assert.Equal(new InviteSlot("PINK-MIA-7Q4X", InviteSlotState.Converted, "kaycee", 9), snap.Slots[0]);
        Assert.Equal(InviteSlotState.Trying, snap.Slots[1].State);
        // An open slot never shows a name, whatever the server sent.
        Assert.Null(snap.Slots[2].InviteeName);
        Assert.Equal(InviteSlotState.Open, snap.Slots[3].State);
    }

    [Theory]
    [InlineData("{\"ok\":false,\"reason\":\"not_subscribed\"}")]
    [InlineData("{\"ok\":true}")]
    [InlineData("{\"ok\":true,\"codes\":\"nope\"}")]
    public void ParseSnapshot_RefusalsAndJunkAreNull(string json)
        => Assert.Null(InviteRules.ParseSnapshot(JObject.Parse(json)));

    [Fact]
    public void ParseSnapshot_NegativeCountReadsAsZero()
        => Assert.Equal(0, InviteRules.ParseSnapshot(JObject.Parse("{\"codes\":[],\"converted_total\":-3}"))!.ConvertedTotal);

    [Fact]
    public void ParseRedeem_Outcomes()
    {
        Assert.Equal(RedeemOutcome.Offline, InviteRules.ParseRedeem(null));

        var refused = InviteRules.ParseRedeem(JObject.Parse("{\"ok\":false,\"reason\":\"already_had_week\"}"));
        Assert.False(refused.Ok);
        Assert.Equal("already_had_week", refused.Reason);

        Assert.Equal("unknown", InviteRules.ParseRedeem(JObject.Parse("{\"ok\":false}")).Reason);

        var ok = InviteRules.ParseRedeem(JObject.Parse("{\"ok\":true,\"grant_until\":\"2026-10-09T12:00:00Z\"}"));
        Assert.True(ok.Ok);
        Assert.Equal(new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc), ok.GrantUntilUtc);
    }
}
