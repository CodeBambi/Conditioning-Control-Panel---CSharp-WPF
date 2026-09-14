using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Prizes;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Back Room prize ownership feed 1 (backroom CONTRACT 10.17.E): ProfileSyncService hands the
/// <c>prizes</c> block from sync and validate responses to the OwnershipService for the account
/// the request was made for, and clears it on logout and account switch. The ownership service
/// gets an injected account source and a synchronous event marshal, so no App or dispatcher runs.
/// </summary>
public class ProfileSyncPrizesFeedTests
{
    private const string Me = "uid-me";
    private const string Other = "uid-other";

    private string? _account = Me;
    private readonly List<OwnershipChangedEventArgs> _events = new();
    private readonly OwnershipService _ownership;
    private readonly ProfileSyncService.PrizesFeed _feed;

    public ProfileSyncPrizesFeedTests()
    {
        _ownership = new OwnershipService(() => _account, null, invoke => invoke());
        _ownership.OwnershipChanged += (_, e) => _events.Add(e);
        _feed = new ProfileSyncService.PrizesFeed(() => _ownership);
    }

    private static PrizesBlock Block(long revision, params string[] grants)
        => new() { Revision = revision, Grants = grants.ToList() };

    [Fact]
    public void SyncSnapshot_IsApplied()
    {
        Assert.True(_feed.Apply(Me, Me, Block(3, PrizeGrants.JackpotRemix, PrizeGrants.RacingTrack(0)), "test"));

        Assert.True(_ownership.IsGranted(PrizeGrants.JackpotRemix));
        Assert.True(_ownership.IsGranted("rt.original.00"));
        Assert.False(_ownership.IsGranted("rt.original.01"));
        Assert.Equal(3, _ownership.Revision);
        var e = Assert.Single(_events);
        Assert.Equal(2, e.Added.Count);

        // A later sync adds a grant: only the difference is announced.
        Assert.True(_feed.Apply(Me, Me, Block(4, PrizeGrants.JackpotRemix, PrizeGrants.RacingTrack(0), "fx.flash.pendulum"), "test"));
        Assert.True(_ownership.IsGranted("fx.flash.pendulum"));
        Assert.Equal(new[] { "fx.flash.pendulum" }, _events[1].Added);
    }

    [Fact]
    public void MissingBlock_LeavesStateAlone()
    {
        _feed.Apply(Me, Me, Block(2, PrizeGrants.JackpotRemix), "test");
        _events.Clear();

        Assert.False(_feed.Apply(Me, Me, null, "old server"));
        Assert.False(_feed.Apply(Me, Me, new PrizesBlock { Revision = 9, Grants = null }, "grants missing"));

        Assert.True(_ownership.IsGranted(PrizeGrants.JackpotRemix));
        Assert.Equal(2, _ownership.Revision);
        Assert.Empty(_events);
    }

    [Fact]
    public void EmptyGrants_IsARealSnapshot()
    {
        _feed.Apply(Me, Me, Block(2, PrizeGrants.JackpotRemix), "test");
        Assert.True(_feed.Apply(Me, Me, Block(3), "test"));
        Assert.False(_ownership.IsGranted(PrizeGrants.JackpotRemix));
    }

    [Fact]
    public void OtherAccountResponse_IsIgnored()
    {
        _feed.Apply(Me, Me, Block(2, PrizeGrants.JackpotRemix), "test");
        _events.Clear();

        // A validate the server resolved to a different record (duplicate-identity pair).
        Assert.False(_feed.Apply(Me, Other, Block(7, "fx.bubble.rain"), "validate"));
        // A validate with no resolved record cannot be matched to this session.
        Assert.False(_feed.Apply(Me, null, Block(7, "fx.bubble.rain"), "validate"));
        // No session account when the request went out.
        Assert.False(_feed.Apply(null, Me, Block(7, "fx.bubble.rain"), "validate"));

        Assert.False(_ownership.IsGranted("fx.bubble.rain"));
        Assert.True(_ownership.IsGranted(PrizeGrants.JackpotRemix));
        Assert.Empty(_events);
    }

    [Fact]
    public void ResponseForAnAccountNoLongerSignedIn_IsIgnored()
    {
        // The request went out for Me; the session moved to Other before the answer landed.
        _account = Other;
        _feed.Apply(Me, Me, Block(5, PrizeGrants.JackpotRemix), "V2 sync");

        Assert.False(_ownership.IsGranted(PrizeGrants.JackpotRemix));
        Assert.Empty(_ownership.Grants);
        Assert.Empty(_events);
    }

    [Fact]
    public void Logout_Clears()
    {
        _feed.Apply(Me, Me, Block(3, PrizeGrants.JackpotRemix, "fx.bubble.rain"), "test");
        _events.Clear();

        _feed.Clear();

        Assert.False(_ownership.IsGranted(PrizeGrants.JackpotRemix));
        Assert.Equal(0, _ownership.Revision);
        var e = Assert.Single(_events);
        Assert.Empty(e.Added);
        Assert.Equal(2, e.Removed.Count);

        // Signing back in to the same account starts clean and takes a snapshot again.
        Assert.True(_feed.Apply(Me, Me, Block(3, PrizeGrants.JackpotRemix), "test"));
        Assert.True(_ownership.IsGranted(PrizeGrants.JackpotRemix));
    }

    [Fact]
    public void AccountSwitch_ClearsBeforeTheNextAccountsSnapshot()
    {
        _feed.Apply(Me, Me, Block(8, PrizeGrants.JackpotRemix), "test");
        _events.Clear();

        _account = Other;
        _feed.NoteAccount(Other);

        var cleared = Assert.Single(_events);
        Assert.Equal(new[] { PrizeGrants.JackpotRemix }, cleared.Removed);
        Assert.Equal(0, _ownership.Revision);

        // The new account's first snapshot applies even at a lower revision.
        Assert.True(_feed.Apply(Other, Other, Block(1, "fx.bubble.rain"), "V2 sync"));
        Assert.True(_ownership.IsGranted("fx.bubble.rain"));
        Assert.False(_ownership.IsGranted(PrizeGrants.JackpotRemix));

        // Same account again: no clear.
        _events.Clear();
        _feed.NoteAccount(Other);
        Assert.Empty(_events);
    }

    [Fact]
    public void ValidateResponses_ParseThePrizesBlock_WithBothSerializers()
    {
        const string json = "{\"unified_id\":\"uid-me\",\"prizes\":{\"revision\":4,\"grants\":[\"fx.jackpot_remix\",\"rt.original.00\"]}}";

        // Patreon and SubscribeStar validate read with System.Text.Json (ReadFromJsonAsync).
        var patreon = System.Text.Json.JsonSerializer.Deserialize<PatreonSubscriptionResponse>(json)!;
        Assert.Equal(4, patreon.Prizes!.Revision);
        Assert.Equal(new[] { "fx.jackpot_remix", "rt.original.00" }, patreon.Prizes.Grants);

        var discord = System.Text.Json.JsonSerializer.Deserialize<DiscordUserResponse>(json)!;
        Assert.Equal("uid-me", discord.UnifiedId);
        Assert.Equal(2, discord.Prizes!.Grants!.Count);

        // The block class is shared with the Newtonsoft-read sync response.
        var viaNewtonsoft = Newtonsoft.Json.JsonConvert.DeserializeObject<PatreonSubscriptionResponse>(json)!;
        Assert.Equal(4, viaNewtonsoft.Prizes!.Revision);

        Assert.Null(System.Text.Json.JsonSerializer.Deserialize<DiscordUserResponse>("{\"unified_id\":\"uid-me\"}")!.Prizes);
    }

    [Fact]
    public void Grants_AreNeverCachedWithProviderState()
    {
        Assert.Null(typeof(PatreonCachedState).GetProperty("Prizes"));
        Assert.Null(typeof(DiscordCachedState).GetProperty("Prizes"));
    }
}
