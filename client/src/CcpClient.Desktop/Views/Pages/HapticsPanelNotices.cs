using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Haptics;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// The <b>Haptics</b> panel's sentences.
///
/// <para><b>This panel has to say two different "no"s without letting either be read as the
/// other.</b> The switch is off because an entitlement could not be verified; and the sink reaches
/// nothing because the user has ticked no provider route, or because the server behind the route
/// they ticked is not running. Collapsing them into one sentence would send a user to fix the wrong
/// thing —
/// which is exactly what upstream's own <i>"No devices found. Connect your device in Intiface
/// first."</i> (<c>Services/Haptics/ButtplugProvider.cs:135</c>) does when the real problem is that
/// Intiface is not running.</para>
///
/// <para><b>There used to be a THIRD "no" here, and it named the wrong cause.</b> These
/// sentences said no effect module sends anything to the sink. D210 wired the limb and
/// SIX live call sites now fire it — <c>Effects/FlashSurfacePresenter.cs:314</c>,
/// <c>Effects/MandatoryVideoEffect.cs:307</c>, <c>:340</c> and <c>:419</c>,
/// <c>Effects/SubliminalsEffect.cs:223</c>, <c>Effects/BouncingTextField.cs:237</c> — each
/// incrementing <see cref="Haptics.HapticLimb.Moments"/> and posting a real envelope
/// (<c>HapticLimb.cs:187-227</c>). D210 records that the reason moved one rung out and that
/// <c>Views/**</c> was not touched: <b>the XML docs were maintained (<c>HapticLimb.cs:139-140</c>,
/// <c>IHapticSink.cs:288</c>) and the sentence the user reads was not.</b> Misnaming the cause is
/// the exact defect the paragraph above cites upstream for.</para>
/// </summary>
public static class HapticsPanelNotices
{
    /// <summary>
    /// The lead line: what this row is, and what is on the other end of it.
    ///
    /// <para>It names the SERVER because that is the honest description of both upstream providers:
    /// a WebSocket client into Intiface Central (<c>ButtplugProvider.cs:27,83</c>) and an HTTP client
    /// into Lovense Connect or Lovense Remote (<c>LovenseProvider.cs:21,83,89</c>). Neither is a
    /// driver, and a user who thinks this app talks to their toy directly will look in the wrong
    /// place when it does not.</para>
    /// </summary>
    public static string DescribeWhatItIs() =>
        "Haptics drives a toy while the session runs. It never talks to hardware itself: the Windows app connects "
        + "to a separate program you install — Intiface Central for Buttplug.io toys, or Lovense Connect / Lovense "
        + "Remote for Lovense ones — and that program owns the device.";

    /// <summary>
    /// What the dot means right now.
    ///
    /// <para><b>There is no <c>Live</c> arm and there cannot be one</b>: see
    /// <see cref="HapticParticipant.Dot"/>. The two reachable states are spelled out here so a dark
    /// dot is never read as a fault.</para>
    ///
    /// <para><b>The <see cref="EffectDotState.Armed"/> arm is the one this packet made
    /// READABLE, and it was written before anybody could read it.</b> <c>Dot</c> is
    /// <c>Enabled &amp;&amp; LastObservation is { Confirmed: true }</c>
    /// (<c>HapticParticipant.cs:257-258</c>), <c>Confirmed</c> requires
    /// <c>ClientAdmitted &amp;&amp; DeviceCount &gt;= 1</c> (<c>IHapticSink.cs:196</c>), and
    /// <c>LastObservation</c> is assigned only past the <c>Sink.Route == None</c> early return
    /// (<c>HapticParticipant.cs:306-335</c>). <b>So this arm renders only where a route the user
    /// ticked has been asked and a server has answered with a device</b> — which is why neither
    /// "nothing sends anything to it yet" (the earlier
    /// wording) nor "the sink admits no provider route" (this file's own first attempt) may appear
    /// here: in the only world where a user reads this arm, a confirmed observation has given the
    /// limb non-empty <c>DeviceKeys</c> (<c>:103</c>) and <c>Send</c> reaches <c>Sends++</c>
    /// (<c>HapticLimb.cs:575-585</c>). Both would be self-refuting, not merely stale. What is true
    /// in that world is what the dot MEANS — reach, never traffic — and that
    /// <c>Allowed()</c> consults the entitlement gate (<c>OutputAllowed &amp;&amp; Enabled</c>,
    /// <c>:102</c>, <c>:146</c>) before anything is delivered.</para>
    /// </summary>
    public static string DescribeLiveState(EffectDotState dot, bool enabled, bool reachable) => dot switch
    {
        EffectDotState.Armed =>
            "The dot is lit: the switch is on and a device is really reachable. It will not go further than this in "
            + "this build: this row's dot has only two states and it reports REACH, not traffic, so it looks exactly "
            + "the same whether or not a pulse is going out. Whether one is going out is decided by the entitlement "
            + "gate that every command from the effect modules passes on its way to the device.",
        _ when enabled && !reachable =>
            "The dot is dark: the switch is on and no device has been reached. See the line below for which part "
            + "is missing — it is usually a provider box left un-ticked, or its server not running.",
        _ when !enabled && reachable =>
            "The dot is dark because the switch is off. A device is reachable, so turning it on is all that is "
            + "missing.",
        _ =>
            "The dot is dark: the switch is off, and no device has been reached. Nothing has been asked of a "
            + "haptic server, because nothing is asked of one until you switch this on.",
    };

    /// <summary>
    /// The gate's answer, rendered so that the two refusals never read alike.
    ///
    /// <para>This is the sentence the whole entitlement type system exists to protect
    /// (<c>Entitlement/EntitlementOutcome.cs:7-17</c>). The Windows app has only ONE refusal here —
    /// <c>App.Patreon?.HasPremiumAccess != true</c> shows "Haptic feedback is available for Patreon
    /// supporters." whether the answer was "no pledge" or "I could not ask"
    /// (<c>MainWindow/MainWindow.Haptics.cs:489-497</c>). This port keeps them apart.</para>
    /// </summary>
    public static string DescribeGate(HapticGateDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return decision switch
        {
            HapticGateDecision.Allow allow =>
                $"Your pledge was confirmed ({allow.Tier}), so this switch is yours to use.",
            HapticGateDecision.RefusedNotEntitled refused => refused.Message,
            HapticGateDecision.RefusedUnverified unverified => unverified.Message,
            // The hierarchy is closed (the constructor is private), so this arm is unreachable
            // today; it is the codebase's own convention for these switches and it prints the
            // decision rather than inventing a sentence.
            _ => decision.ToString() ?? string.Empty,
        };
    }

    /// <summary>
    /// The capability line: what the sink itself last said, verbatim.
    ///
    /// <para><b>It says "no device found" in exactly ONE case and never as a catch-all.</b> That
    /// refusal (<c>HapticReasonCodes.HapticNoDevice</c>) is earned only when a server really answered
    /// and really named nothing, which is the <see cref="CapabilityState.DependencyMissing"/> arm
    /// below; it names the dependency the way the port names every other absent one. Every other
    /// refusal — no route ticked, no server listening, nothing asked yet — renders its own detail
    /// verbatim, because the whole reason those codes are distinct is that they have different
    /// repairs.</para>
    /// </summary>
    public static string DescribeSink(CapabilityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state switch
        {
            CapabilityState.Available available => "Reachable: " + available.Detail,
            CapabilityState.Degraded degraded =>
                $"Partly reachable: {degraded.SurvivingSemantics}. {degraded.Reason.Detail}",
            CapabilityState.DependencyMissing missing =>
                $"A haptic server answered and there is no {missing.Dependency}. {missing.Reason.Detail}",
            CapabilityState.PermissionRequired permission => "Not permitted: " + permission.Reason.Detail,
            CapabilityState.Faulted faulted => "The haptic capability faulted: " + faulted.Reason.Detail,
            CapabilityState.Unavailable unavailable => unavailable.Reason.Detail,
            _ => state.ToString() ?? string.Empty,
        };
    }

    /// <summary>
    /// The absence line — what the Windows app's haptics page has that this one does not, and where
    /// the send really stops.
    ///
    /// <para>It is on the PAGE rather than only in a record, on the precedent four earlier rows
    /// set for a half-ported row.</para>
    ///
    /// <para><b>This sentence has now been wrong TWICE, in the same way, and the pattern is the
    /// point.</b> Its first version blamed the effect modules ("no effect in this build sends anything
    /// to haptics yet"), which D210 falsified by wiring the limb. Its second version blamed the
    /// BUILD — "no provider route is admitted at all, so the thing to fix is the missing route" —
    /// which this packet falsifies by admitting both of them. Each time, a user was sent to wait for a
    /// release when the actual repair was in front of them. What is true now is a SERVER and a
    /// DEVICE: <see cref="Haptics.HapticSinkFactory.AdmittedRoutes"/> carries both upstream routes,
    /// the panel has a checkbox for each, and <c>HapticLimb.Send</c> stops at
    /// <see cref="Haptics.HapticLimb.EvaluationsWithNoDevice"/><c>++</c> only while no observation has
    /// named a device. D179/D202/D210 are the superseded rows.</para>
    ///
    /// <para><b>What it still may NOT say is that anything moved.</b> Nothing in this repository has
    /// driven a real toy on any platform (<see cref="Haptics.HapticSinkFactory.DeviceManualGate"/>),
    /// so this line describes what the product will ATTEMPT and never what a user will feel.</para>
    /// </summary>
    public static string DescribeAbsences() =>
        "The Windows app's haptics page also has two server addresses, an auto-connect box, a per-event routing "
        + "table, a master cap and a signal-processing block. None of them is here, because none of them has an "
        + "implementation here that would honour it — a control that decides nothing is worse than a missing one. "
        + "The provider boxes ARE here, because this build has a client for both routes and connects every one you "
        + "tick, together rather than one instead of the other. The effect modules are not the reason nothing "
        + "moves either: the flash decay ladder, the video background layer and its stop, the subliminal pulse and "
        + "the bounce tap all command the haptic limb now. What is left between those commands and a motor is "
        + "outside this program: tick a provider above, and have its server running — Intiface Central for "
        + "Intiface, Lovense Connect or Lovense Remote for Lovense — with your toy already paired to it. Until a "
        + "server names a device there is nothing to address, and no run of this app has yet driven a real toy.";
}
