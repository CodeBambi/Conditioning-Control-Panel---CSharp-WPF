namespace CcpClient.Desktop.Haptics;

/// <summary>
/// Chooses the haptic sink.
///
/// <para><b>This factory does not look at the operating system, and that is the finding.</b> Every
/// other capability factory in this port switches on <c>OperatingSystem.Is*</c> because every other
/// capability is a platform API: a layered window, a raw input queue, a WASAPI endpoint, a
/// <c>WindowFromPoint</c> hit test. <b>Haptics is not.</b> Upstream's two providers are a WebSocket
/// client and an HTTP client talking to loopback (<c>Services/Haptics/ButtplugProvider.cs:27,83</c>;
/// <c>Services/Haptics/LovenseProvider.cs:21,83,89</c>) — nothing in either one is Windows-only, and
/// a Linux box running Intiface Central would answer identically. So the axis of refusal here is not
/// the platform: it is whether this BUILD admits a client at all.</para>
///
/// <para><b>Selection is not availability</b> (<c>runtime-capability-contract.md</c> §2 rule 2).
/// Nothing here produces <see cref="Capabilities.CapabilityState.Available"/>; only
/// <see cref="HapticServerObservation.Classify"/> does, and only after a real ask that a real client
/// made.</para>
/// </summary>
public static class HapticSinkFactory
{
    /// <summary>
    /// The provider routes this build has a client for. <b>Empty, and the refusal is produced by
    /// reading it rather than by a hard-coded branch</b> — so the day one is admitted, the same code
    /// stops refusing without being edited to stop refusing.
    /// </summary>
    public static IReadOnlyList<HapticProviderRoute> AdmittedRoutes { get; } = [HapticProviderRoute.Lovense];

    /// <summary>
    /// The gap, in one paragraph, worded so that nobody can read it as a missing toy.
    ///
    /// <para>It names both routes because a refusal justified against one provider is exactly the
    /// failure this capability was built to avoid, and it names the SERVER because the thing this
    /// build cannot talk to is another program, not hardware.</para>
    /// </summary>
    public const string AdmissionGap =
        "this build admits no haptic provider client, so nothing was attempted. THIS IS NOT \"no device found\": "
        + "there is nothing here with which to look. Upstream ships two providers and BOTH are clients of a "
        + "SEPARATE SERVER PROCESS the user installs, not drivers — Buttplug.io over a WebSocket to "
        + "ws://127.0.0.1:12345 into Intiface Central (Services/Haptics/ButtplugProvider.cs:27,83), and Lovense "
        + "over HTTP to http://127.0.0.1:20010 into Lovense Connect or Lovense Remote "
        + "(Services/Haptics/LovenseProvider.cs:21,83,89). Admitting either is an owner decision about a "
        + "dependency, and the two cost different things: see HapticSinkFactory.DescribeRoute.";

    /// <summary>
    /// What ONE named route would need here, priced. Both branches are reachable so neither
    /// description is written for a route nobody looked at.
    ///
    /// <para>The Buttplug line is the packet's stopping point: it is a decision about a package,
    /// and this build does not make it.</para>
    /// </summary>
    public static string DescribeRoute(HapticProviderRoute route) => route switch
    {
        HapticProviderRoute.Buttplug =>
            "Buttplug.io / Intiface Central needs a NuGet package this project does not carry: the shipping app "
            + "references Buttplug 5.0.1 (ConditioningControlPanel.csproj:60) and its provider imports "
            + "Buttplug.Client and Buttplug.Core.Messages (Services/Haptics/ButtplugProvider.cs:6-7). With the "
            + "package, the sink is ONE file: connect a ButtplugWebsocketConnector to ws://127.0.0.1:12345, scan "
            + "(:89-95), take every device whose features expose a Vibrate output (:62-66), and set levels per "
            + "feature — Buttplug outputs LATCH, so no keep-alive is needed at all "
            + "(Services/Haptics/ButtplugProviderV2.cs:27-30). Without the package it is not one file: it is a "
            + "hand-written implementation of Buttplug message spec v4 over ClientWebSocket, and the port would "
            + "then own a wire protocol somebody else versions.",

        HapticProviderRoute.Lovense =>
            "Lovense Connect / Lovense Remote needs NO HAPTICS-SPECIFIC package at all: the shipping provider's "
            + "wire imports are pure BCL (Services/Haptics/LovenseProvider.cs:1-7 — System.Net.Http and "
            + "System.Text.Json). Its ONE non-BCL using is Serilog at :8, which is the shipping app's existing "
            + "logger and not a haptics dependency: this port logs through its own ILogSink, so nothing new enters "
            + "the dependency graph for the Lovense route. The sink is one file plus two pieces of real work: an "
            + "HttpClient whose certificate exception is loopback-only (:41-52), and a HOLD strategy, because the "
            + "LAN API expires its own command — timeSec, floored at a whole second by "
            + "Math.Max(1, durationMs / 1000) (:232-233) — while Connect mode expires nothing at all (:242-243). "
            + "It reaches Lovense hardware only.",

        HapticProviderRoute.None =>
            "no route. This build admits none, which is the state it is in: " + AdmissionGap,

        _ => "this build has no description for that route, which means it is not one of the two upstream ships",
    };

    /// <summary>
    /// The manual gate that a real haptic claim would need — and it is DOWNSTREAM of the admission
    /// decision above, not a substitute for it.
    ///
    /// <para><b>Read the order.</b> Nothing on this list can be attempted until a client is
    /// admitted, so a refusal that quoted this gate today would be telling a user to go and fix
    /// something that is not what is wrong. It is here so that the day a route is admitted the gate
    /// is already written, and so that the difference between the two gaps is legible in the source
    /// rather than only in a record.</para>
    ///
    /// <para>The last step is the one no automated step on any platform discharges, and it is not
    /// dischargeable at any depth of API: a haptic server reports what it believes it commanded over
    /// Bluetooth, and neither this process nor upstream's can tell a toy that vibrated from one with
    /// a flat battery in the next room.</para>
    /// </summary>
    public const string DeviceManualGate =
        "MANUAL GATE (undischarged, and it CANNOT be attempted until a provider client is admitted): "
        + "(1) install and run the server the route needs — Intiface Central for Buttplug, or Lovense Connect / "
        + "Lovense Remote for Lovense — and confirm it is listening (the shipping app's own ports are 12345 and "
        + "20010; Lovense Connect's documented HTTPS alias is :30010); "
        + "(2) pair one real device to that server and see it listed in the server's own UI, which is where "
        + "upstream sends people (\"Connect your device in Intiface first\", ButtplugProvider.cs:135); "
        + "(3) drive one level through this sink and read the server's device list back, which is the strongest "
        + "fact any code on this machine can produce; "
        + "(4) a HUMAN reports that the device really moved and that it STOPPED when the all-stop ran — neither "
        + "half of which any automated step discharges on any platform, because a device's motor state is not on "
        + "this machine's API surface at all.";

    /// <summary>
    /// The sink for this build. <b>Every path returns a refusal today</b>, and it is produced by
    /// asking <see cref="AdmittedRoutes"/> rather than by a constant.
    /// </summary>
    public static IHapticSink Create() => CreateFrom(AdmittedRoutes);

    /// <summary>
    /// The selection itself, over a given admitted-route list.
    ///
    /// <para>Taking the list as a parameter is what makes the guard below EXECUTABLE rather than
    /// merely written: with the product list it returns the refusal, and a fact can hand it a
    /// non-empty list and watch it refuse to manufacture anything. A guard nothing can run is a
    /// comment with a keyword in front of it.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A route is admitted and no sink is constructed for it. Deliberately louder than a fallback:
    /// a factory that quietly returned a no-op for an admitted route would be the fake-available
    /// shape the truthful-capability contract bans, and it would do it on the exact path where a
    /// user believes their toy is connected. The day a route is admitted, its sink is constructed
    /// HERE and this throw becomes unreachable for that route.
    /// </exception>
    public static IHapticSink CreateFrom(IReadOnlyList<HapticProviderRoute> admittedRoutes)
    {
        ArgumentNullException.ThrowIfNull(admittedRoutes);
        if (admittedRoutes.Count == 0)
        {
            return new UnadmittedHapticSink(HapticReasonCodes.HapticNoAdmittedProvider, AdmissionGap);
        }

        // The throw below is still reachable, and keeping it reachable is the point: it fires for a
        // route that is admitted with no client behind it, which is the fake-available shape the
        // truthful-capability contract bans. Lovense stops reaching it because Lovense now HAS a
        // client, not because the check was relaxed.
        if (admittedRoutes.Contains(HapticProviderRoute.Lovense))
        {
            return new LovenseHapticSink(_ => { });
        }

        throw new InvalidOperationException(
            "a haptic provider route is admitted and no sink is constructed for it — admit the route AND "
            + $"its client together ({string.Join(", ", admittedRoutes)})");
    }

    /// <summary>
    /// The sink for ONE named route, so the per-route refusal text is reachable and executed rather
    /// than only written. It refuses for the same reason <see cref="Create"/> does and adds what
    /// that specific route would need.
    /// </summary>
    public static IHapticSink CreateFor(HapticProviderRoute route) =>
        AdmittedRoutes.Contains(route)
            ? Create()
            : new UnadmittedHapticSink(
                HapticReasonCodes.HapticNoAdmittedProvider,
                AdmissionGap + " " + DescribeRoute(route));
}
