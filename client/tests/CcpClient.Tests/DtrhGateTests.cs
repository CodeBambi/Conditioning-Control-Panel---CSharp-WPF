using System.Reflection;
using CcpClient.Desktop.Entitlement;
using CcpClient.Desktop.Features.Dtrh;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-094: the Tier-2 door in front of Down The Rabbit Hole, proved as a pure function so all
/// three branches are decidable without a window (the rendered half lives in
/// <c>PlayPageHeadlessTests</c>).
///
/// <para>THE FAILURE THIS FILE EXISTS TO PREVENT. SP-092 separated "you are not a patron" from
/// "I could not tell" at the capability. The UI is where that separation dies, because both
/// look like "no" on a card — and today they are not even equally likely: this build ships
/// <see cref="UnconfiguredTierSource"/>, so <b>every real user gets the "could not tell"
/// branch</b>. If it renders as a refusal, the port tells every one of them they stopped
/// paying.</para>
/// </summary>
public class DtrhGateTests
{
    /// <summary>Wording that only ever belongs to a REAL refusal — an authority's answer about
    /// this account. If any of it reaches a could-not-verify message, the port is telling a
    /// user something nobody determined.</summary>
    private static readonly string[] RefusalWording =
    [
        "is a Tier 2 perk",
        "upgrade your pledge",
        "not a patron",
        "no pledge",
    ];

    private static readonly string[] EveryReasonCode =
    [
        EntitlementReasonCodes.HostAppDataAbsent,
        EntitlementReasonCodes.HostTokenAbsent,
        EntitlementReasonCodes.HostTokenEmpty,
        EntitlementReasonCodes.HostTokenUndecryptable,
        EntitlementReasonCodes.HostReadFailed,
        EntitlementReasonCodes.UnsupportedPlatform,
        EntitlementReasonCodes.TierAuthorityAbsent,
        EntitlementReasonCodes.TierAuthorityUnreachable,
        EntitlementReasonCodes.TierAuthorityRejected,
        EntitlementReasonCodes.TierAuthorityFault,
    ];

    /// <summary>The guard, as a named predicate, so the test below can run it against a SEEDED
    /// bad message and prove it actually catches one.</summary>
    private static string? FirstRefusalWordingIn(string message) =>
        RefusalWording.FirstOrDefault(w => message.Contains(w, StringComparison.OrdinalIgnoreCase));

    private static EntitlementOutcome Unavailable(string code) =>
        new EntitlementOutcome.Unavailable(new EntitlementReason(code, "fixture detail"));

    [Fact]
    public void EntitledAtTheLabBar_Proceeds_AndCarriesTheTier()
    {
        // WPF's bar is tier 2: TierGate.RequiresLab reads HasLabAccess (Services/TierGate.cs:88-94),
        // and the DTRH handlers demand exactly that (MainWindow.Lab.cs:228,313).
        var decision = DtrhGate.Decide(new EntitlementOutcome.Entitled(EntitlementTier.Lab, "fixture"));

        var proceed = Assert.IsType<DtrhGateDecision.Proceed>(decision);
        Assert.Equal(EntitlementTier.Lab, proceed.Tier);
        Assert.Equal(EntitlementTier.Lab, DtrhGate.RequiredTier);
    }

    [Fact]
    public void EntitledBelowTheLabBar_RefusesWithTheTierMessage_AndIsNeverReportedAsUnknown()
    {
        // A tier-1 patron IS an authority answer about this account, so it is the SAME refusal
        // WPF gives them: RequiresLab evaluates HasLabAccess false and shows tiergate_denied_lab.
        // Reporting it as "could not verify" would be the mirror-image lie of the one this
        // packet is about — telling someone the port is broken when it simply said no.
        var decision = DtrhGate.Decide(new EntitlementOutcome.Entitled(EntitlementTier.Supporter, "fixture"));

        var refused = Assert.IsType<DtrhGateDecision.RefusedNotEntitled>(decision);
        Assert.Contains("is a Tier 2 perk", refused.Message, StringComparison.Ordinal);
        Assert.IsNotType<DtrhGateDecision.RefusedUnverified>(decision);
    }

    [Fact]
    public void NotEntitled_RefusesWithWpfsVerbatimTierMessage_AndNamesTheUpgradeRoute()
    {
        // Services/TierGate.cs:128,133 raises an 8s Warning toast carrying
        // Loc.GetF("tiergate_denied_lab", featureName) with a "See tiers" action. The port has
        // no toast and no App Info & Data page (divergence §10 D17), so it carries the same
        // sentence and names the route in words.
        var decision = DtrhGate.Decide(new EntitlementOutcome.NotEntitled("the authority answered: no pledge"));

        var refused = Assert.IsType<DtrhGateDecision.RefusedNotEntitled>(decision);
        Assert.Contains(
            "Down the Rabbit Hole is a Tier 2 perk - upgrade your pledge to unlock it.",
            refused.Message,
            StringComparison.Ordinal);
        Assert.Contains("See tiers", refused.Message, StringComparison.Ordinal);
        // The feature name WPF passes to the gate, unchanged (MainWindow.Lab.cs:228).
        Assert.Equal("Down the Rabbit Hole", DtrhGate.FeatureName);
        // And the badge wears the livery's words, never the literal "TIER 2" (TierBadge.cs:21).
        Assert.Equal("PRIME SUBJECT", DtrhGate.TierBadgeWording);
    }

    [Fact]
    public void Unavailable_TierAuthorityAbsent_IsTheBranchEveryUserHitsToday_AndIsNeverTheNotEntitledRefusal()
    {
        // THE NAMED TEST. This build's authority is UnconfiguredTierSource, so a readable
        // shipping-app login resolves to Unavailable(tier-authority-absent) for everyone —
        // this is the product's ONLY reachable branch until an owner permission decision lands.
        var decision = DtrhGate.Decide(Unavailable(EntitlementReasonCodes.TierAuthorityAbsent));

        var unverified = Assert.IsType<DtrhGateDecision.RefusedUnverified>(decision);
        Assert.Equal("tier-authority-absent", unverified.ReasonCode);

        // It refuses — an unknown entitlement may not open paid content.
        Assert.IsNotType<DtrhGateDecision.Proceed>(decision);
        // But it is a DIFFERENT refusal, in every way a user could notice.
        Assert.IsNotType<DtrhGateDecision.RefusedNotEntitled>(decision);
        Assert.NotEqual(DtrhGate.TierRefusalMessage, unverified.Message);
        Assert.Null(FirstRefusalWordingIn(unverified.Message));
        // And it says out loud what happened and what did not.
        Assert.Contains("could not verify your entitlement", unverified.Message, StringComparison.Ordinal);
        Assert.Contains("no entitlement authority configured", unverified.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was decided about your account", unverified.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryUnavailableReasonCode_HasItsOwnAuthoredWording_AndNoneCarriesTheRefusalWording()
    {
        // The closure table. A reason code added later cannot inherit "you are not a patron"
        // by default, and cannot fall through to a placeholder either.
        Assert.Equal(10, EveryReasonCode.Length); // the loop below is not vacuous
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var code in EveryReasonCode)
        {
            var decision = DtrhGate.Decide(Unavailable(code));
            var unverified = Assert.IsType<DtrhGateDecision.RefusedUnverified>(decision);
            Assert.Equal(code, unverified.ReasonCode);

            var explanation = DtrhGate.Explain(code);
            Assert.False(string.IsNullOrWhiteSpace(explanation));
            Assert.DoesNotContain(DtrhGate.UnwordedReasonMarker, explanation, StringComparison.Ordinal);
            Assert.True(seen.Add(explanation), $"reason code '{code}' reuses another code's wording");
            Assert.Null(FirstRefusalWordingIn(unverified.Message));
        }

        // Every shipped code is covered; the guard is exhaustive over what exists today.
        Assert.Equal(EveryReasonCode.Length, seen.Count);

        // Positive control, without which every "does not contain" above could be satisfied by
        // a gate that never produces the refusal wording at all.
        Assert.Equal("is a Tier 2 perk", FirstRefusalWordingIn(DtrhGate.TierRefusalMessage));
    }

    [Fact]
    public void TheRefusalWordingGuard_Bites_OnASeededCouldNotVerifyMessage()
    {
        // The guard above is only worth having if it CATCHES one. Rather than mutating the
        // source and putting it back, the predicate itself is run against messages seeded with
        // exactly the collapse this packet forbids — a could-not-verify line that has drifted
        // into refusal wording.
        var seeded = new[]
        {
            DtrhGate.CouldNotVerifyHeader + "\nupgrade your pledge to unlock it.",
            DtrhGate.CouldNotVerifyHeader + "\nYou are not a patron.",
            "Down the Rabbit Hole is a Tier 2 perk - upgrade your pledge to unlock it.",
            DtrhGate.CouldNotVerifyHeader + "\nthe authority answered: no pledge.",
        };

        Assert.Equal(RefusalWording.Length, seeded.Length); // one seed per banned phrase
        foreach (var message in seeded)
        {
            Assert.NotNull(FirstRefusalWordingIn(message));
        }

        // The unmutated control: the real message for the branch users hit passes the guard,
        // which is what makes the assertions above non-vacuous.
        Assert.Null(FirstRefusalWordingIn(DtrhGate.UnverifiedMessage(EntitlementReasonCodes.TierAuthorityAbsent)));
    }

    [Fact]
    public void TheGateHasExactlyThreeDecisions_TwoDistinctRefusalTypes_AndNoBooleanCollapse()
    {
        // Shape guard, the sibling of EntitlementOutcome's own. The two refusals are different
        // TYPES rather than one refusal carrying a flag, because a flag is what a renderer
        // forgets to read — and a bool accessor anywhere on this hierarchy would have to map
        // "could not tell" onto true or false, recreating the conflation one layer up.
        var cases = typeof(DtrhGateDecision).GetNestedTypes(BindingFlags.Public)
            .Where(t => t.IsSubclassOf(typeof(DtrhGateDecision)))
            .ToArray();

        Assert.Equal(3, cases.Length);
        Assert.Contains(typeof(DtrhGateDecision.Proceed), cases);
        Assert.Contains(typeof(DtrhGateDecision.RefusedNotEntitled), cases);
        Assert.Contains(typeof(DtrhGateDecision.RefusedUnverified), cases);

        // The hierarchy is closed: the private constructor stops a fourth case being declared
        // outside this file, which is what makes the three-way switch total.
        Assert.Empty(typeof(DtrhGateDecision).GetConstructors(BindingFlags.Instance | BindingFlags.Public));

        Assert.DoesNotContain(
            typeof(DtrhGateDecision).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            p => p.PropertyType == typeof(bool));
        Assert.DoesNotContain(
            typeof(DtrhGateDecision).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            m => m.ReturnType == typeof(bool) && m.GetParameters().Length == 0);
    }

    [Fact]
    public void ATierThisBuildDoesNotDefine_IsRefusedAsUnverified_AndNeverProceeds()
    {
        // Defence in depth on the ONE direction where a mistake hands out paid content.
        // `tier >= Lab` on a raw enum opens the door for (EntitlementTier)99, and the tier is a
        // value that arrives from outside this process.
        var decision = DtrhGate.Decide(new EntitlementOutcome.Entitled((EntitlementTier)99, "rogue"));

        var unverified = Assert.IsType<DtrhGateDecision.RefusedUnverified>(decision);
        Assert.Equal(EntitlementReasonCodes.TierAuthorityFault, unverified.ReasonCode);
        Assert.IsNotType<DtrhGateDecision.Proceed>(decision);

        // Zero is refused by the comparison anyway; asserting it pins that the refusal is the
        // UNVERIFIED one rather than "you are not a patron", which zero would have produced.
        var zero = DtrhGate.Decide(new EntitlementOutcome.Entitled(default, "rogue"));
        Assert.IsType<DtrhGateDecision.RefusedUnverified>(zero);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public async Task AnAuthorityNamingATierThisBuildDoesNotDefine_IsUnavailable_NeverEntitled(int rawTier)
    {
        // SP-094 closes the hole AT THE SOURCE too (board row: "the entitlement capability can
        // be talked into an over-grant by an undefined enum value"). Every other hole in this
        // capability was closed toward REFUSAL; this was the only one leaning toward granting.
        var capability = new HostLoginEntitlement(
            StubReader.Yielding(EntitlementFixtures.TokenValue),
            new StubTierSource(new TierLookup(TierLookupStatus.Entitled, (EntitlementTier)rawTier, "rogue authority")));

        var outcome = await capability.ResolveAsync(CancellationToken.None);

        var unavailable = Assert.IsType<EntitlementOutcome.Unavailable>(outcome);
        Assert.Equal(EntitlementReasonCodes.TierAuthorityFault, unavailable.Reason.Code);
        Assert.Contains("does not define", unavailable.Reason.Detail, StringComparison.Ordinal);
        // Unknown, not refused: the honest classification of an answer this build cannot read.
        Assert.IsNotType<EntitlementOutcome.NotEntitled>(outcome);
        // And the defined tiers still pass, so the guard is not simply refusing everything.
        Assert.IsType<EntitlementOutcome.Entitled>(await new HostLoginEntitlement(
                StubReader.Yielding(EntitlementFixtures.TokenValue),
                new StubTierSource(TierLookup.Entitled(EntitlementTier.Lab)))
            .ResolveAsync(CancellationToken.None));
    }
}
