namespace CcpClient.Desktop.Haptics;

/// <summary>
/// Stable machine-readable reason codes for the haptic capability
/// (<c>runtime-capability-contract.md</c> §1: codes are additive and land with their consumer).
/// They live beside their consumer for the same reason <c>Overlay/OverlayReasonCodes.cs</c>,
/// <c>Audio/AudioReasonCodes.cs</c> and <c>Pointer/PointerReasonCodes.cs</c> do.
///
/// <para><b>Why a sink that drives a toy needs its own vocabulary, and why it is not the audio
/// vocabulary with different words.</b> Every other capability in this port fails somewhere this
/// process can look: a window the OS will not place, an endpoint the mixer will not meter, a click
/// the window manager routes elsewhere. <b>This one fails in a place that is not on this machine's
/// API surface at all.</b> Upstream's two providers are a WebSocket client
/// (<c>Services/Haptics/ButtplugProvider.cs:27,83</c> — <c>ws://127.0.0.1:12345</c>) and an HTTP
/// client (<c>Services/Haptics/LovenseProvider.cs:21,83,89</c> — <c>http://127.0.0.1:20010</c>),
/// and <b>both are clients of a separate server process the user installs</b>. So the failure
/// ladder has a rung no other capability has: this build may have no CLIENT with which to ask, and
/// that is a different fact from "the server is not running", which is different again from "the
/// server is running and no toy is attached".</para>
///
/// <para><b>The three must never collapse.</b> Upstream collapses the last two —
/// <c>ConnectAsync</c> returns <c>false</c> both for "no devices found"
/// (<c>ButtplugProvider.cs:132-138</c>, <c>LovenseProvider.cs:116-117</c>) and for a failed request
/// (<c>ButtplugProvider.cs:140-145</c>, <c>LovenseProvider.cs:119-124</c>) — and the user-visible
/// consequence is a message telling somebody to plug a toy in when the real problem is that
/// Intiface is not running.</para>
/// </summary>
public static class HapticReasonCodes
{
    /// <summary>
    /// <b>This build admits no haptic provider client, so nothing was attempted.</b>
    ///
    /// <para>This is the port's real answer today and it is a property of the BUILD, not of the
    /// machine, not of the platform and not of the user's toy box. It is never "no device found":
    /// a refusal that named a missing device would be false, because there is no client here with
    /// which to look. The detail names the two upstream routes, what each would need, and the fact
    /// that both speak to a separate server process rather than to hardware.</para>
    /// </summary>
    public const string HapticNoAdmittedProvider = "haptic-no-admitted-provider";

    /// <summary>
    /// A client exists and the server it speaks to did not answer: Intiface Central / Lovense
    /// Connect is not running, is on another port, or the loopback route is broken. Upstream's own
    /// note that this is real and not theoretical is on <c>IHapticProvider.PingAsync</c>
    /// (<c>Services/Haptics/IHapticProvider.cs:23-28</c>): <i>"IsConnected can lie when the OS
    /// routing table changes after connect (e.g. user enables a VPN)"</i>.
    /// </summary>
    public const string HapticServerUnreachable = "haptic-server-unreachable";

    /// <summary>
    /// The server answered and reported <b>no device</b>. This is the code that carries upstream's
    /// own wording — <i>"No devices found. Connect your device in Intiface first."</i>
    /// (<c>ButtplugProvider.cs:135</c>) and <i>"No toys found. Connect toy in Lovense app first."</i>
    /// (<c>LovenseProvider.cs:116</c>) — and it is a
    /// <see cref="Capabilities.CapabilityState.DependencyMissing"/> cause rather than an
    /// <see cref="Capabilities.CapabilityState.Unavailable"/> one, because the missing thing is a
    /// named external dependency the user can go and attach.
    /// </summary>
    public const string HapticNoDevice = "haptic-no-device";

    /// <summary>
    /// The server answered about a device this sink does not hold. A CALLER fault reported in type,
    /// distinct from every platform state above — the same distinction
    /// <c>PointerReasonCodes.PointerTargetUnknown</c> draws.
    /// </summary>
    public const string HapticDeviceUnknown = "haptic-device-unknown";

    /// <summary>
    /// Output was refused because the entitlement gate is not open. It is a REFUSAL of the output,
    /// never a statement about the user: <see cref="HapticGate"/> decides which of the two
    /// refusals applies, and "I could not tell" and "you are not a patron" reach the surface as
    /// different types (<c>Entitlement/EntitlementOutcome.cs:7-17</c>).
    ///
    /// <para>Upstream gates in the same place — <c>Services/Haptics/Core/HapticMixer.cs:191-204</c>
    /// evaluates the premium bar once per 10 Hz tick rather than in every consumer — and stops the
    /// toys once on the open→closed transition (<c>:253-262</c>).</para>
    /// </summary>
    public const string HapticNotEntitled = "haptic-not-entitled";

    /// <summary>The sink was disposed; it holds nothing and will never drive anything again.</summary>
    /// <summary>The server answered and REFUSED the command. Distinct from
    /// <see cref="HapticServerUnreachable"/> on purpose: "nobody is listening" and "the program
    /// listening said no" have different repairs, and reporting the second as the first sends a user
    /// to restart an application that is already running.</summary>
    public const string HapticCommandRefused = "haptic-command-refused";

    /// <summary>A stop did not reach every device it was owed to. The loudest code here, because it
    /// is the only one that means A TOY MAY STILL BE RUNNING — it is never folded into a generic
    /// unreachable, and never reported as a success with a caveat.</summary>
    public const string HapticStopIncomplete = "haptic-stop-incomplete";

    public const string HapticSinkDisposed = "haptic-sink-disposed";
}
