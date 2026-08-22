using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Haptics;

/// <summary>
/// Which upstream provider route a sink speaks for. <b>A selection input only</b>: naming a route
/// never makes one admitted, and <see cref="HapticSinkFactory.AdmittedRoutes"/> is the only thing
/// that decides which are.
/// </summary>
public enum HapticProviderRoute
{
    /// <summary>No route. The value every sink this build constructs carries.</summary>
    None,

    /// <summary>Buttplug.io through Intiface Central, a WebSocket at <c>ws://127.0.0.1:12345</c>
    /// (<c>Services/Haptics/ButtplugProvider.cs:27,83</c>).</summary>
    Buttplug,

    /// <summary>Lovense Connect / Lovense Remote, HTTP at <c>http://127.0.0.1:20010</c>
    /// (<c>Services/Haptics/LovenseProvider.cs:21,83,89</c>).</summary>
    Lovense,
}

/// <summary>
/// One actuator's desired level, 0..1.
///
/// <para><b>The seam carries 0..1 and nothing else, and that is a two-provider decision.</b>
/// Buttplug takes a percentage and lets the client map it onto the feature's own step range
/// (<c>ButtplugProvider.cs:268</c>, <c>DeviceOutput.Vibrate.Percent(intensity)</c>); Lovense
/// quantizes to 0..20 with levels 1-2 deliberately unreachable
/// (<c>LovenseProvider.cs:200-204</c>). Quantization is therefore the PROVIDER's, which is exactly
/// what upstream's own second-generation contract says
/// (<c>Services/Haptics/Core/HapticContracts.cs:36-38,70-82</c>: a 0..1 intensity, and
/// <c>HapticActuator.Steps</c> is the native resolution each provider quantizes to).</para>
/// </summary>
public readonly record struct HapticLevel
{
    private HapticLevel(double value) => Value = value;

    /// <summary>Nothing at all. Distinct from "the quietest thing a toy can do": upstream refuses
    /// to substitute a floor for a zero — <i>"slider at 0 = silence, not a floor buzz"</i>
    /// (<c>Services/Haptics/HapticService.cs:779</c>), and the same rule again for the video
    /// background layer (<c>:836-843</c>).</summary>
    public static HapticLevel Silent => new(0.0);

    /// <summary>The clamped level, 0..1. Both providers clamp at their own boundary
    /// (<c>ButtplugProvider.cs:300</c> is <c>Math.Clamp(intensity, 0.0, 1.0)</c>;
    /// <c>LovenseProvider.cs:204</c> is <c>Math.Clamp(i, 0, 20)</c>), so clamping here cannot make
    /// either of them behave differently and it stops an out-of-range value ever reaching a wire.</summary>
    public double Value { get; }

    /// <summary>True when this level asks for nothing.</summary>
    public bool IsSilent => Value <= 0.0;

    /// <summary>Clamp into 0..1. A NaN is silence rather than an exception: an arithmetic slip in a
    /// caller's envelope must never become an unhandled fault on the path that drives hardware.</summary>
    public static HapticLevel Of(double value) =>
        double.IsNaN(value) ? Silent : new(Math.Clamp(value, 0.0, 1.0));

    public override string ToString() => Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// One line of a level-set command: which actuator on the addressed device, and what level it
/// should hold until the next command.
///
/// <para><b>Why there is an index and why there is no "kind".</b> The index is what makes this seam
/// fit BOTH providers rather than one: <c>LovenseProvider</c> holds a single <c>_toyId</c>
/// (<c>:22</c>, overwritten per toy at <c>:132</c>, so only the LAST discovered toy is addressable
/// at all) while <c>ButtplugProvider</c> fans one intensity across every feature with a Vibrate
/// output (<c>:264-278</c>). Neither can drive a two-motor device's motors independently, and
/// upstream repaired exactly that in its second generation —
/// <c>HapticContracts.cs:31-39</c>, <i>"Index disambiguates same-type motors (Edge=2 vibes,
/// Lapis=3)"</i>. There is no actuator KIND because both cited providers drive exactly one kind:
/// <c>ButtplugProvider</c> filters on <c>OutputType.Vibrate</c> (<c>:62-66</c>) and
/// <c>LovenseProvider</c> sends <c>Vibrate:{i}</c> (<c>:233</c>, <c>:244</c>). Upstream's v2
/// contract has eleven kinds (<c>HapticContracts.cs:16-29</c>); admitting ten of them here would
/// be shaping a seam around implementations this port has not read against a device.</para>
/// </summary>
/// <param name="ActuatorIndex">Which vibrating actuator on the device, zero-based.</param>
/// <param name="Level">What it should hold.</param>
public readonly record struct HapticOutput(int ActuatorIndex, HapticLevel Level)
{
    /// <summary>The addressed actuator. Negative indexes are refused at construction rather than at
    /// a provider boundary, so a caller's arithmetic error cannot reach a wire as a device id.</summary>
    public int ActuatorIndex { get; } = ActuatorIndex >= 0
        ? ActuatorIndex
        : throw new ArgumentOutOfRangeException(nameof(ActuatorIndex), ActuatorIndex,
            "an actuator index is zero-based and never negative");
}

/// <summary>
/// Everything known about the server and its devices at one moment, from one ask.
///
/// <para><b>Every member is an answer, never a value this process remembered writing</b> — the same
/// rule <c>Pointer/IPointerSurface.cs</c>'s and <c>Audio/IAudioPresence.cs</c>'s observations
/// follow, and here it has a specific provenance. Upstream's contract says
/// <c>IsConnected</c> can lie (<c>Services/Haptics/IHapticProvider.cs:23-28</c>) and then one of
/// its two implementations answers <c>PingAsync</c> from that very field
/// (<c>ButtplugProvider.cs:215-221</c>) while the other really asks
/// (<c>LovenseProvider.cs:163-186</c>). This type is why that cannot happen here: there is no
/// cached <c>IsConnected</c> anywhere in this seam to answer from.</para>
/// </summary>
/// <param name="Asked">False means no question was put to anything, so nothing below means anything.</param>
/// <param name="Route">Which provider route the ask went down, or <see cref="HapticProviderRoute.None"/>.</param>
/// <param name="ClientAdmitted">
/// Whether this build has a client that could speak to a server at all. <b>The rung no other
/// capability in this port has</b>, and the one that is false today.
/// </param>
/// <param name="ServerAnswered">Whether the separate server process answered.</param>
/// <param name="DeviceKeys">
/// The devices the server named, as <c>provider:id</c> keys — upstream's own identity shape
/// (<c>HapticContracts.cs:51,67</c>). A snapshot from THIS ask; nothing here is cached, so a caller
/// that needs the fact later asks again.
/// </param>
public sealed record HapticServerObservation(
    bool Asked,
    HapticProviderRoute Route,
    bool ClientAdmitted,
    bool ServerAnswered,
    IReadOnlyList<string> DeviceKeys)
{
    /// <summary>Nothing was asked and nothing is admitted. The only value this build ever produces.</summary>
    public static HapticServerObservation NotAsked { get; } =
        new(false, HapticProviderRoute.None, false, false, []);

    /// <summary>How many devices the server named in this ask.</summary>
    public int DeviceCount => DeviceKeys.Count;

    /// <summary>
    /// Every necessary condition for this process to be able to move something a human can feel.
    /// <b>This — and only this — is what <see cref="CapabilityState.Available"/> may rest on</b>,
    /// and <see cref="Classify"/> returns <see cref="CapabilityState.Available"/> exactly when this
    /// is true.
    /// </summary>
    public bool Confirmed => Asked && ClientAdmitted && ServerAnswered && DeviceCount >= 1;

    /// <summary>
    /// The typed state this observation earns. <b>Arm order is the whole design</b>: the admission
    /// question is asked FIRST, so no combination of the other three fields can produce an answer
    /// about a server this build cannot reach, and no run of this product can report a missing
    /// device when the real gap is that there is no client with which to look.
    /// </summary>
    public CapabilityState Classify() =>
        !ClientAdmitted
            ? new CapabilityState.Unavailable(new CapabilityReason(
                HapticReasonCodes.HapticNoAdmittedProvider, HapticSinkFactory.AdmissionGap))
            : !Asked
                ? new CapabilityState.Unavailable(new CapabilityReason(
                    CapabilityReasonCodes.NotProbed,
                    "a haptic client is admitted in this build and nothing has been asked of its server yet, "
                    + "so nothing is known about it"))
                : !ServerAnswered
                    ? new CapabilityState.Unavailable(new CapabilityReason(
                        HapticReasonCodes.HapticServerUnreachable,
                        "the haptic server did not answer. Start Intiface Central (Buttplug) or Lovense Connect / "
                        + "Lovense Remote (Lovense) and check that nothing is diverting loopback traffic — upstream's "
                        + "own note is that a VPN can break this route after a successful connect "
                        + "(Services/Haptics/IHapticProvider.cs:23-28)"))
                    : DeviceCount == 0
                        ? new CapabilityState.DependencyMissing(
                            "a haptic device attached to the running haptic server",
                            new CapabilityReason(HapticReasonCodes.HapticNoDevice,
                                "the haptic server answered and named no device. Connect your device in Intiface "
                                + "first (Services/Haptics/ButtplugProvider.cs:135), or connect the toy in the "
                                + "Lovense app first (Services/Haptics/LovenseProvider.cs:116)"))
                        : new CapabilityState.Available(
                            $"the haptic server answered over the {Route} route and named {DeviceCount} device(s) "
                            + "this process can address");
}

/// <summary>
/// A haptic sink: this process's ability to hold a level on somebody else's hardware, and its
/// ability to ask whether that is really so.
///
/// <para><b>What is on the other end is not a device — it is another program.</b> Both of
/// upstream's providers are clients of a separate server process the user installs: Buttplug over a
/// WebSocket into Intiface Central (<c>ButtplugProvider.cs:27,83</c>), Lovense over HTTP into
/// Lovense Connect or Lovense Remote (<c>LovenseProvider.cs:21,83,89</c>). Neither is a driver and
/// neither touches a USB or Bluetooth stack. Every refusal in this namespace is worded from that
/// fact.</para>
///
/// <para><b>LEVEL-SET, and the stop is its own verb.</b> There is deliberately no
/// <c>Vibrate(intensity, durationMs)</c>, because upstream's own two implementations of that exact
/// signature disagree about who ends it: <c>ButtplugProvider</c> runs a client-side
/// <c>Task.Delay(durationMs)</c> and then stops every device itself (<c>:309-331</c>) — the level it
/// set LATCHES until the next command (<c>ButtplugProviderV2.cs:27-30</c>) — while
/// <c>LovenseProvider</c> hands the duration to the SERVER as <c>timeSec</c> — floored at one whole
/// second, <c>Math.Max(1, durationMs / 1000)</c> (<c>:232-233</c>) — and in Connect mode passes no
/// duration at all, so <i>"the device maintains vibration until next command"</i> (<c>:242-243</c>).
/// A seam that took a duration would be promising something one of the two cannot represent and the
/// other cannot honour. So it takes a LEVEL, and each provider chooses its own way to hold it —
/// which is the choice upstream's own second-generation contract leaves open in as many words
/// (<c>HapticContracts.cs:70-73</c>: <i>"Lovense: timeSec 0 + keep-alive refresh, or short-timeSec
/// repeats — provider's choice"</i>).</para>
///
/// <para><b>What <see cref="CapabilityState.Available"/> may mean here.</b> Only what
/// <see cref="HapticServerObservation.Confirmed"/> says: a client is admitted, the server was
/// asked, it answered, and it named at least one device. It never means a call returned, and it
/// never means anything about a human.</para>
///
/// <para><b>What it can never mean, and this is where the chain stops early.</b> That anybody FELT
/// anything. A device's own motor state is not on this machine's API surface at any depth: the
/// server reports what it believes it commanded over Bluetooth, and nothing in this process — or in
/// upstream's — can distinguish a toy that vibrated from one with a flat battery in the next room.
/// That is a named manual gate (<c>HapticSinkFactory.DeviceManualGate</c>) and no automated step on
/// any platform discharges it.</para>
///
/// <para><b>FIVE statements in the effect spine now COMMAND this sink</b>, through
/// <see cref="IHapticLimb"/> and <see cref="HapticLimb"/> — a flash image placed, a clip really
/// playing, a clip off the screen, a subliminal card shown, a bouncing word hitting an edge.
/// Upstream drives haptics from <b>EIGHTEEN</b> sites in the whole
/// <c>Services/{Flash,Video,Subliminal}</c> family; ten have a port trigger point and reach those
/// five statements, eight are absent-by-decision. The enumeration, its method and every decision's
/// quoted words are <c>client/docs/haptic-limb-census.md</c>, pinned by
/// <c>HapticSiteCensusTests</c>, which re-derives the candidate set from the shipping bytes rather
/// than trusting a figure typed into a comment. <b>This paragraph used to say THIRTEEN over three
/// named files</b>, which was the count before the searched universe widened from a file list to
/// the three directories — corrected once and rewritten here (D202).</para>
///
/// <para><b>Nothing is DELIVERED, and the reason is now one rung further out.</b> It is no longer
/// that the modules are silent; it is that the limb has nothing to address.
/// <see cref="HapticSinkFactory.AdmittedRoutes"/> is empty, so no server was ever asked, so no
/// <see cref="HapticServerObservation"/> names a device, so <see cref="SetOutputsAsync"/> is never
/// reached. <b>A landed capability is not a working feature</b>, and a limb commanding a sink with
/// no device is a limb, not a feature.</para>
///
/// <para><b>Two of the eighteen are unported for a reason that is about a PROGRAM rather than a
/// moment</b>, named here so nobody re-derives it: <c>VideoService.cs:2584</c> and <c>:6584</c> (and
/// <c>VideoService.Browser.cs:453</c>) hand a <c>.funscript</c> sidecar to
/// <c>App.Haptics.FunScript</c>,
/// a script player this port has not ported at all. Two further lines nearby are NOT among the
/// eighteen at all because they fail the outward-flow test: <c>VideoService.cs:4567</c> and
/// <c>:4680</c> subscribe to <c>ToyInput.ButtonPressed</c>, which is
/// haptic INPUT — the toy driving the app — and runs the opposite way through this seam. Settings
/// reads (<c>FlashService.cs:1602-1607</c>, <c>VideoService.cs:4649-4651</c>,
/// <c>SubliminalService.cs:584</c>) are not commands and are not counted.</para>
/// </summary>
public interface IHapticSink : IDisposable
{
    /// <summary>The provider route this sink speaks for. <see cref="HapticProviderRoute.None"/> in
    /// every sink this build constructs.</summary>
    HapticProviderRoute Route { get; }

    /// <summary>
    /// Ask the server what it has, right now.
    ///
    /// <para><b>There is no <c>IsConnected</c> and no <c>Ping</c>.</b> One way to ask, defined as
    /// touching the wire — see <see cref="HapticServerObservation"/> for the two upstream
    /// implementations that made this structural rather than a convention.</para>
    /// </summary>
    Task<HapticServerObservation> ObserveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Open the route and say what was found, as <see cref="HapticServerObservation.Classify"/>
    /// decides it.
    ///
    /// <para>Three answers where upstream has two: <c>ConnectAsync</c> returns <c>false</c> for a
    /// server that did not answer AND for a server that answered with no devices
    /// (<c>ButtplugProvider.cs:132-145</c>, <c>LovenseProvider.cs:116-124</c>), and a user told to
    /// plug a toy in when Intiface is not running has been sent to the wrong place.</para>
    /// </summary>
    Task<CapabilityState> ConnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Set the current target levels for the actuators of ONE device. Level-set semantics: each
    /// level holds until the next command for that actuator, and how it is held is the provider's
    /// business (see the interface remarks).
    /// </summary>
    /// <param name="deviceKey">A key from a <see cref="HapticServerObservation"/> of this sink.</param>
    /// <param name="outputs">The levels. An empty list is a caller error, not a silent no-op.</param>
    Task<CapabilityState> SetOutputsAsync(
        string deviceKey, IReadOnlyList<HapticOutput> outputs, CancellationToken cancellationToken);

    /// <summary>
    /// Immediate all-stop for everything this sink drives. Bypasses any throttle and any
    /// unchanged-send suppression, and is safe to call at any time including during teardown —
    /// upstream's own requirement on the same verb (<c>HapticContracts.cs:130-132</c>).
    ///
    /// <para><b>It never reports <see cref="CapabilityState.Available"/> for having stopped
    /// nothing.</b> A sink that answered "stopped" from a build that never drove anything would let
    /// a teardown pin read green on exactly the run where the guarantee is worthless — the same
    /// reasoning as <c>Pointer/UnsupportedPointerSurface.Close</c>.</para>
    /// </summary>
    Task<CapabilityState> StopAllAsync();

    /// <summary>The most recent <see cref="ConnectAsync"/> / <see cref="SetOutputsAsync"/> /
    /// <see cref="StopAllAsync"/> outcome, verbatim, or null before anything was attempted.</summary>
    CapabilityState? LastOutcome { get; }
}
