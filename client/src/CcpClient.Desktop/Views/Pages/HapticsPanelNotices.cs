using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Haptics;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// The <b>Haptics</b> panel's sentences.
///
/// <para><b>This panel has to say three different "no"s without letting any of them be read as
/// another.</b> The switch is off because an entitlement could not be verified; the sink cannot
/// reach anything because this build admits no provider client; and even with both of those solved
/// nothing would happen yet, because no effect module sends anything to this sink. Collapsing any
/// two of those into one sentence would send a user to fix the wrong thing — which is exactly what
/// upstream's own <i>"No devices found. Connect your device in Intiface first."</i>
/// (<c>Services/Haptics/ButtplugProvider.cs:135</c>) does when the real problem is that Intiface is
/// not running.</para>
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
    /// </summary>
    public static string DescribeLiveState(EffectDotState dot, bool enabled, bool reachable) => dot switch
    {
        EffectDotState.Armed =>
            "The dot is lit: the switch is on and a device is really reachable. It will not go further than this in "
            + "this build — nothing sends anything to it yet.",
        _ when enabled && !reachable =>
            "The dot is dark: the switch is on and nothing here can reach a device. See the line below for which "
            + "part is missing.",
        _ when !enabled && reachable =>
            "The dot is dark because the switch is off. A device is reachable, so turning it on is all that is "
            + "missing.",
        _ =>
            "The dot is dark: the switch is off, and nothing here could reach a device even if it were on.",
    };

    /// <summary>
    /// The gate's answer, rendered so that the two refusals never read alike.
    ///
    /// <para>This is the sentence the whole entitlement type system exists to protect
    /// (<c>Entitlement/EntitlementOutcome.cs:7-17</c>). The Windows app has only ONE refusal here —
    /// <c>App.Patreon?.HasPremiumAccess != true</c> shows "Haptic feedback is available for Patreon
    /// supporters." whether the answer was "no pledge" or "I could not ask"
    /// (<c>MainWindow/MainWindow.Haptics.cs:487-496</c>). This port keeps them apart.</para>
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
    /// <para><b>It must never say "no device found."</b> That refusal is available in this port's
    /// vocabulary (<c>HapticReasonCodes.HapticNoDevice</c>) and is deliberately not what this build
    /// produces, because there is no client here with which to look. The
    /// <see cref="CapabilityState.DependencyMissing"/> arm below is the one that WOULD say it, and
    /// it names the dependency the way the port names every other absent one.</para>
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
    /// The absence line — what the Windows app's haptics page has that this one does not, and the
    /// one that matters most: nothing sends anything here yet.
    ///
    /// <para>It is on the PAGE rather than only in a record, on the precedent SP-111, SP-113, SP-115
    /// and SP-117 set for a half-ported row. The last sentence is D179: the thirteen ported effect
    /// modules are silent to this sink, where the Windows app drives it from eight sites in three of
    /// them (<c>Services/Flash/FlashService.cs:1453,1480,1516,1915</c>;
    /// <c>Services/Video/VideoService.cs:2580,4585,6580</c>;
    /// <c>Services/SubliminalService.cs:230</c>).</para>
    /// </summary>
    public static string DescribeAbsences() =>
        "The Windows app's haptics page also has a provider list with two server addresses, an auto-connect box, a "
        + "per-event routing table, a master cap and a signal-processing block. None of them is here, because every "
        + "one of them configures a connection this build cannot make — a control that decides nothing is worse "
        + "than a missing one. And even with a device attached, nothing would move: no effect in this build sends "
        + "anything to haptics yet.";
}
