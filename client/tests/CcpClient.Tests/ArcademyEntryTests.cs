using CcpClient.Desktop.Entitlement;
using CcpClient.Desktop.Features.Arcademy;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Slice 8 of the Arcademy row: the ENTRY POINT and its T2 bar
/// (<c>ArcademyHostService.Launch</c>, <c>:126-146</c>; <c>Services/TierGate.cs:76-81</c>).
///
/// <para><b>THE DOOR OUTRANKS THE BAR, AND THAT IS THE FACT THIS FILE LEADS WITH.</b> Upstream
/// refuses on <c>DoorAvailable</c> at <c>:136-141</c> and only reaches <c>DemandLab</c> at
/// <c>:146</c>, so a build with the door shut never reads a login, never asks an authority, and
/// never adjudicates an account for a surface it is not offering. The port keeps that order, and
/// the fact below proves it by counting the questions asked rather than by reading the code.</para>
///
/// <para><b>What these facts are NOT.</b> Nothing here opens a window, a page or a browser, and
/// nothing here is reachable by a user: <see cref="ArcademyDoor.Available"/> is a
/// <c>static readonly false</c> with no override seam, so the Proceed branch below is exercised
/// against the GATE as a pure function and never against a launch that opened anything. The
/// hidden Play-tab strip is proved in <c>ArcademyEntryHeadlessTests</c>.</para>
/// </summary>
public class ArcademyEntryTests
{
    /// <summary>An entitlement seam that must never be reached. Shared with
    /// <c>ArcademyServingTests</c>, which asserts the same order from the other side.</summary>
    internal static readonly Func<CancellationToken, Task<EntitlementOutcome>> NeverAsked =
        _ => throw new InvalidOperationException(
            "the entitlement was resolved behind a SHUT door — the door must answer first (:136-146)");

    /// <summary>Wording that only ever belongs to a REAL refusal — an authority's answer about
    /// this account. If any of it reaches a could-not-verify message, the port is telling a user
    /// something nobody determined.</summary>
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

    private static string? FirstRefusalWordingIn(string message) =>
        RefusalWording.FirstOrDefault(w => message.Contains(w, StringComparison.OrdinalIgnoreCase));

    private static EntitlementOutcome Unavailable(string code) =>
        new EntitlementOutcome.Unavailable(new EntitlementReason(code, "fixture detail"));

    [Fact]
    public void TheBarIsTierTwo_AndAPledgeBelowItIsARefusal_NeverAnUnknown()
    {
        // Upstream asks TierGate.DemandLab(ProductName) — the UNKEYED overload (:146), so unlike
        // DTRH there is no daily-free term and this gate is the whole bar: HasLabAccess, tier 2.
        Assert.Equal(EntitlementTier.Lab, ArcademyGate.RequiredTier);
        var proceed = Assert.IsType<ArcademyGateDecision.Proceed>(
            ArcademyGate.Decide(new EntitlementOutcome.Entitled(EntitlementTier.Lab, "fixture")));
        Assert.Equal(EntitlementTier.Lab, proceed.Tier);

        // A tier-1 patron IS an authority's answer about this account, so it is the same refusal
        // WPF gives them — and it names the Arcademy, because the feature name is what
        // tiergate_denied_lab interpolates (en.json:4704).
        var below = Assert.IsType<ArcademyGateDecision.RefusedNotEntitled>(
            ArcademyGate.Decide(new EntitlementOutcome.Entitled(EntitlementTier.Supporter, "fixture")));
        Assert.Contains("is a Tier 2 perk", below.Message, StringComparison.Ordinal);
        Assert.Contains(ArcademyDoor.ProductName, below.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Rabbit Hole", below.Message, StringComparison.Ordinal);

        // An explicit "no pledge" is the same refusal, and neither is ever an unknown.
        Assert.IsType<ArcademyGateDecision.RefusedNotEntitled>(
            ArcademyGate.Decide(new EntitlementOutcome.NotEntitled("fixture")));
    }

    [Fact]
    public void AnUndeterminedEntitlement_IsNeverRenderedAsARefusalOfTheAccount()
    {
        // This build ships UnconfiguredTierSource, so this is not the rare branch — it is the one
        // every real user reaches. Rendering it as a refusal tells all of them they stopped paying.
        foreach (var code in EveryReasonCode)
        {
            var decision = Assert.IsType<ArcademyGateDecision.RefusedUnverified>(
                ArcademyGate.Decide(Unavailable(code)));
            Assert.Equal(code, decision.ReasonCode);
            Assert.Null(FirstRefusalWordingIn(decision.Message));
            // Every code this build defines has an authored sentence: reaching the unworded
            // marker would mean a user read a placeholder.
            Assert.DoesNotContain(DtrhGate.UnwordedReasonMarker, decision.Message, StringComparison.Ordinal);
            Assert.Contains(ArcademyGate.CouldNotVerifyHeader, decision.Message, StringComparison.Ordinal);
            Assert.Contains(ArcademyGate.CouldNotVerifyFooter, decision.Message, StringComparison.Ordinal);
        }

        // The guard above is only worth anything if it CATCHES one: the tier refusal itself must
        // trip it.
        Assert.NotNull(FirstRefusalWordingIn(ArcademyGate.TierRefusalMessage));
    }

    [Fact]
    public void ATierThisBuildDoesNotDefine_NeverOpensPaidContent()
    {
        // `>=` on a raw enum would open the door for (EntitlementTier)99. The comparison never
        // runs on a value this build does not define, and the answer is "could not tell" rather
        // than a refusal of the account, because nothing about the account was learned.
        var decision = Assert.IsType<ArcademyGateDecision.RefusedUnverified>(
            ArcademyGate.Decide(new EntitlementOutcome.Entitled((EntitlementTier)99, "fixture")));

        Assert.Equal(EntitlementReasonCodes.TierAuthorityFault, decision.ReasonCode);
    }

    [Fact]
    public async Task AttendAsksTheDoorBeforeItAsksAnyAuthority()
    {
        var log = new List<string>();
        var asked = 0;
        var host = new ApplicationHost(new SinkAdapter(log), [], new StartupTrace());
        var launch = new ArcademyLaunch(host, _ =>
        {
            asked++;
            return Task.FromResult<EntitlementOutcome>(
                new EntitlementOutcome.Entitled(EntitlementTier.Lab, "a fixture that would open the door"));
        })
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "ccp-arcademy-order-" + Guid.NewGuid().ToString("N")),
        };

        var outcome = await launch.AttendAsync(TestContext.Current.CancellationToken);

        // The door answered, and the bar behind it was never consulted — not once, and not even
        // for a fixture that would have said yes. Ask the bar first and this counter reads 1.
        Assert.IsType<ArcademyLaunch.ArcademyAttendOutcome.Refused>(outcome);
        Assert.Equal(0, asked);
        Assert.Null(launch.LastDecision);
        Assert.Null(launch.Participant);

        // The refusal is silent to the user and named in the transcript, as upstream's is
        // (:139-141: a debug log and a return, because there is no announced feature to explain a
        // refusal about yet). Nothing here says a tier was involved.
        Assert.Contains(log, l => l.Contains(ArcademyDoor.Refusal.Reason, StringComparison.Ordinal));
        Assert.DoesNotContain(log, l => l.Contains("tier bar", StringComparison.Ordinal));
    }

    private sealed class SinkAdapter(List<string> lines) : ILogSink
    {
        public void Log(string message) => lines.Add(message);
    }
}
