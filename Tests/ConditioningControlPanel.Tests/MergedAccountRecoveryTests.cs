using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Split-accounts contract D, the pure halves: the wire shape of a merged-account refusal
/// (409 {"error":"merged","canonical_unified_id":"u_..."}) and the loop guard that decides
/// whether the desktop swaps to the canonical. The app-facing runner is not exercised here
/// (it touches settings, timers and the sign-in); these pin what it decides on.
/// </summary>
public class MergedAccountRecoveryTests
{
    private const string Canonical = "u_mfk3q9x1a2b3c4d5e6f7";
    private const string Tombstone = "u_mfk1abcd112233445566";

    // ---------------------------------------------------------------- parsing

    [Fact]
    public void ParsesAMergedRefusal()
    {
        var body = "{\"error\":\"merged\",\"canonical_unified_id\":\"" + Canonical + "\"}";

        Assert.True(MergedAccountResponse.TryParse(409, body, out var id));
        Assert.Equal(Canonical, id);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(500)]
    public void OnlyA409Counts(int status)
    {
        var body = "{\"error\":\"merged\",\"canonical_unified_id\":\"" + Canonical + "\"}";

        Assert.False(MergedAccountResponse.TryParse(status, body, out var id));
        Assert.Null(id);
    }

    [Theory]
    [InlineData("{\"error\":\"merge_required\",\"canonical_unified_id\":\"u_mfk3q9x1a2b3c4d5e6f7\"}")]  // a different 409
    [InlineData("{\"error\":\"supabase_id_mismatch\"}")]
    [InlineData("{\"error\":\"merged\"}")]                                                            // no canonical
    [InlineData("{\"error\":\"merged\",\"canonical_unified_id\":null}")]
    [InlineData("{\"error\":\"merged\",\"canonical_unified_id\":42}")]
    [InlineData("{\"error\":[\"merged\"],\"canonical_unified_id\":\"u_mfk3q9x1a2b3c4d5e6f7\"}")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html>gateway</html>")]
    [InlineData("{not json")]
    public void OtherConflictsAndBadBodiesAreNotMerged(string body)
    {
        Assert.False(MergedAccountResponse.TryParse(409, body, out var id));
        Assert.Null(id);
    }

    [Fact]
    public void NullBodyIsNotMerged()
    {
        Assert.False(MergedAccountResponse.TryParse(409, null, out _));
    }

    [Theory]
    [InlineData("u_mfk3q9x1a2b3c4d5e6f7", true)]
    [InlineData("u_abc123", true)]
    [InlineData("u_", false)]
    [InlineData("u_abc", false)]                     // too short to be a minted id
    [InlineData("mfk3q9x1a2b3c4d5e6f7", false)]      // no prefix
    [InlineData("U_MFK3Q9X1A2B3C4D5E6F7", false)]    // the server mints lower-case only
    [InlineData("u_mfk3q9x1-a2b3c4d5e6f7", false)]   // punctuation
    [InlineData("u_mfk3q9x1 a2b3", false)]
    [InlineData("u_mfk3q9x1\"},{\"x\":\"", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void UnifiedIdShapeIsConservative(string? id, bool valid)
    {
        Assert.Equal(valid, MergedAccountResponse.IsValidUnifiedId(id));
    }

    [Fact]
    public void AnOversizedCanonicalIsRefused()
    {
        var huge = "u_" + new string('a', 65);
        var body = "{\"error\":\"merged\",\"canonical_unified_id\":\"" + huge + "\"}";

        Assert.False(MergedAccountResponse.TryParse(409, body, out _));
    }

    // ---------------------------------------------------------------- decision + loop guard

    [Fact]
    public void SwapsOnAFreshCanonical()
    {
        var policy = new MergedSwapPolicy();

        Assert.Equal(MergedSwapDecision.Swap, policy.Decide(Tombstone, Canonical));
    }

    [Fact]
    public void NeverSwapsToTheIdAlreadyHeld()
    {
        var policy = new MergedSwapPolicy();

        Assert.Equal(MergedSwapDecision.IgnoreSameId, policy.Decide(Canonical, Canonical));
        Assert.Equal(0, policy.SwapCount);
    }

    [Fact]
    public void SwapsWhenNothingIsHeldYet()
    {
        var policy = new MergedSwapPolicy();

        Assert.Equal(MergedSwapDecision.Swap, policy.Decide(null, Canonical));
        Assert.Equal(MergedSwapDecision.Swap, policy.Decide("", Canonical));
    }

    [Fact]
    public void RefusesAMalformedCanonical()
    {
        var policy = new MergedSwapPolicy();

        Assert.Equal(MergedSwapDecision.IgnoreInvalidId, policy.Decide(Tombstone, null));
        Assert.Equal(MergedSwapDecision.IgnoreInvalidId, policy.Decide(Tombstone, "nope"));
    }

    [Fact]
    public void OneSwapPerCanonicalPerSession()
    {
        var policy = new MergedSwapPolicy();

        Assert.Equal(MergedSwapDecision.Swap, policy.Decide(Tombstone, Canonical));
        Assert.True(policy.MarkSwapped(Canonical));

        // A late 409 from a timer that still carried the old id: ignored, not re-run.
        Assert.Equal(MergedSwapDecision.IgnoreAlreadySwapped, policy.Decide(Tombstone, Canonical));
        // And once the swap has landed the fast path answers same-id.
        Assert.Equal(MergedSwapDecision.IgnoreSameId, policy.Decide(Canonical, Canonical));
        Assert.Equal(1, policy.SwapCount);
    }

    [Fact]
    public void AChainedMergeIsFollowedOnceMore()
    {
        var policy = new MergedSwapPolicy();
        const string later = "u_mfk9zzzz998877665544";

        Assert.True(policy.MarkSwapped(Canonical));
        Assert.Equal(MergedSwapDecision.Swap, policy.Decide(Canonical, later));
        Assert.True(policy.MarkSwapped(later));
        Assert.Equal(MergedSwapDecision.IgnoreAlreadySwapped, policy.Decide(Canonical, later));
        Assert.Equal(2, policy.SwapCount);
    }

    [Fact]
    public void TheClaimIsSingleFlight()
    {
        var policy = new MergedSwapPolicy();

        Assert.True(policy.MarkSwapped(Canonical));
        Assert.False(policy.MarkSwapped(Canonical));
    }

    [Fact]
    public async Task ConcurrentClaimsYieldExactlyOneWinner()
    {
        var policy = new MergedSwapPolicy();
        using var start = new ManualResetEventSlim(false);

        var claims = Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            start.Wait();
            return policy.MarkSwapped(Canonical);
        })).ToArray();

        start.Set();
        var results = await Task.WhenAll(claims);

        Assert.Equal(1, results.Count(won => won));
        Assert.Equal(1, policy.SwapCount);
    }

    [Fact]
    public void LiveHandlerIgnoresAResponseThatIsNotMerged()
    {
        // The static entry point must be a no-op for anything but a merged 409, so wiring it
        // into every door is free on the common path. (A real merged 409 would touch app
        // state, which is why the live swap itself is not driven from here.)
        Assert.False(MergedAccountRecovery.TryHandle(200, "{\"ok\":true}"));
        Assert.False(MergedAccountRecovery.TryHandle(409, "{\"error\":\"merge_required\"}"));
        Assert.False(MergedAccountRecovery.TryHandle(401, "{\"error\":\"merged\",\"canonical_unified_id\":\"" + Canonical + "\"}"));
    }
}
