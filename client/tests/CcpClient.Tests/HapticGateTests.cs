using CcpClient.Desktop.Entitlement;
using CcpClient.Desktop.Haptics;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The haptics premium gate, which consumes <c>Entitlement/**</c> UNCHANGED.
///
/// <para>The one rule every fact here serves: <b>"I could not tell" must never render as "you are
/// not a patron."</b> The shipping app breaks it on this exact surface —
/// <c>App.Patreon?.HasPremiumAccess != true</c> shows one message for both answers
/// (<c>MainWindow/MainWindow.Haptics.cs:489-497</c>) — and the port's typed outcome exists so it
/// cannot (<c>Entitlement/EntitlementOutcome.cs:7-17</c>).</para>
/// </summary>
public class HapticGateTests
{
    [Fact]
    public void THEBARIsTIERONE_BecauseUpstreamGatesHapticsOnHasPremiumAccess()
    {
        // HasPremiumAccess is CurrentTier >= PatreonTier.Level1 (PatreonService.cs:134), which is
        // EntitlementTier.Supporter. DTRH's bar is Lab (tier 2), and the two doors must not drift
        // into one another: same folder, same three answers, a different bar.
        Assert.Equal(EntitlementTier.Supporter, HapticGate.RequiredTier);
        Assert.NotEqual(CcpClient.Desktop.Features.Dtrh.DtrhGate.RequiredTier, HapticGate.RequiredTier);
    }

    [Theory]
    [InlineData(EntitlementTier.Supporter)]
    [InlineData(EntitlementTier.Lab)]
    public void APLEDGEAtOrAboveTheBarAllows(EntitlementTier tier)
    {
        var decision = HapticGate.Decide(new EntitlementOutcome.Entitled(tier, "confirmed"));

        var allow = Assert.IsType<HapticGateDecision.Allow>(decision);
        Assert.Equal(tier, allow.Tier);
    }

    [Fact]
    public void ANAUTHORITYThatSaysNOPLEDGEIsTheOnlyThingThatEarnsTheNOTAPATRONRefusal()
    {
        var decision = HapticGate.Decide(new EntitlementOutcome.NotEntitled("no pledge"));

        var refused = Assert.IsType<HapticGateDecision.RefusedNotEntitled>(decision);
        // WPF's own words, verbatim (en.json:3394), plus the route out the port has to name in
        // words because it has no tiers page.
        Assert.Contains(HapticGate.DeniedMessage, refused.Message, StringComparison.Ordinal);
        Assert.Contains(HapticGate.UpgradeRoute, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EVERYUNKNOWNAnswerRefusesUNVERIFIED_AndCarriesItsOwnReasonCode()
    {
        // REFLECTED, not hand-listed. HapticGate's own comment claims a reason code added later
        // "cannot silently inherit" the refusal wording, and a hardcoded list cannot hold that claim:
        // the eleventh code would arrive unswept and the fact would still pass. This is the pattern
        // LaunchFaultTextTests.cs:109-116 already uses over the same vocabulary.
        var codes = typeof(EntitlementReasonCodes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string) && f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // The blindness guard the reflection needs: a sweep that discovers nothing passes vacuously.
        Assert.True(codes.Length >= 10,
            $"only {codes.Length} entitlement reason codes were discovered — the sweep has gone blind");
        Assert.All(codes, code =>
        {
            var decision = HapticGate.Decide(
                new EntitlementOutcome.Unavailable(new EntitlementReason(code, "detail")));

            var unverified = Assert.IsType<HapticGateDecision.RefusedUnverified>(decision);
            Assert.Equal(code, unverified.ReasonCode);
            // THE RULE. No unknown-answer message may carry the refusal wording, so a reason code
            // added later cannot silently inherit "you are not a patron".
            Assert.DoesNotContain(HapticGate.DeniedMessage, unverified.Message, StringComparison.Ordinal);
            Assert.Contains(HapticGate.CouldNotVerifyHeader, unverified.Message, StringComparison.Ordinal);
            Assert.Contains(HapticGate.CouldNotVerifyFooter, unverified.Message, StringComparison.Ordinal);
            // And every one of them is really WORDED, rather than falling into the marker.
            Assert.DoesNotContain(HapticGate.UnwordedReasonMarker, unverified.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ATIERThisBuildDoesNOTDefineIsUNKNOWN_NeverGranted()
    {
        // The one direction where a mistake hands out a paid feature: `>=` on a raw enum would open
        // for (EntitlementTier)99. HostLoginEntitlement closes it at the source and
        // DtrhGate takes the same second guard; this is the third.
        var decision = HapticGate.Decide(new EntitlementOutcome.Entitled((EntitlementTier)99, "rogue"));

        var unverified = Assert.IsType<HapticGateDecision.RefusedUnverified>(decision);
        Assert.Equal(EntitlementReasonCodes.TierAuthorityFault, unverified.ReasonCode);
    }

    [Fact]
    public void EVERYDEFINEDTierClearsThisBar_SoTheBELOWTHEBARArmIsUNREACHABLEHere_AndSaysSo()
    {
        // Stated rather than hidden, because "compiled but never executed" is a thing this port
        // counts. DtrhGate's bar is Lab, so ITS below-the-bar arm is reachable (a Supporter is
        // refused there). Haptics' bar is the LOWEST tier this build defines, so no confirmed pledge
        // can fall under it and the only route to RefusedNotEntitled here is an authority that said
        // "no pledge". The arm stays in the source anyway: it is what keeps the gate correct the day
        // a lower tier is defined, and deleting it would leave `Enum.IsDefined` as the only guard.
        var defined = Enum.GetValues<EntitlementTier>();

        Assert.NotEmpty(defined);
        Assert.All(defined, tier =>
        {
            Assert.True(tier >= HapticGate.RequiredTier);
            Assert.IsType<HapticGateDecision.Allow>(
                HapticGate.Decide(new EntitlementOutcome.Entitled(tier, "confirmed")));
        });
    }

    [Fact]
    public void ANUNKNOWNReasonCodeStillRendersHONESTLY_RatherThanFallingIntoTheRefusal()
    {
        var message = HapticGate.UnverifiedMessage("some-code-nobody-authored");

        Assert.Contains(HapticGate.UnwordedReasonMarker, message, StringComparison.Ordinal);
        Assert.DoesNotContain(HapticGate.DeniedMessage, message, StringComparison.Ordinal);
    }

    [Fact]
    public void THISBUILDSRealAuthorityRefusesEVERYUSER_AndItRefusesTHROUGHTheUnknownBranch()
    {
        // Not the rare branch: UnconfiguredTierSource is what ships, so this is the branch every
        // real user reaches. It is the THIRD door in that state after the two DTRH doors, with the
        // same cause and the same owner decision behind it.
        var outcome = new EntitlementOutcome.Unavailable(new EntitlementReason(
            EntitlementReasonCodes.TierAuthorityAbsent, "no entitlement authority is configured in this build"));

        var unverified = Assert.IsType<HapticGateDecision.RefusedUnverified>(HapticGate.Decide(outcome));

        Assert.Equal(EntitlementReasonCodes.TierAuthorityAbsent, unverified.ReasonCode);
        Assert.Contains("gap in the port, not a finding about your account",
            unverified.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void THEGATEIsAPUREFunctionAndRefusesANullOutcome()
    {
        Assert.Throws<ArgumentNullException>(() => HapticGate.Decide(null!));
    }
}
