namespace CcpClient.Desktop.Haptics;

/// <summary>
/// Chooses the haptic sink.
///
/// <para><b>This factory does not look at the operating system, and that is the finding.</b> Every
/// other capability factory in this port switches on <c>OperatingSystem.Is*</c> because every other
/// capability is a platform API: a layered window, a raw input queue, a WASAPI endpoint, a
/// <c>WindowFromPoint</c> hit test. <b>Haptics is not.</b> Upstream's two providers are a WebSocket
/// client and an HTTP client talking to loopback (<c>Services/Haptics/ButtplugProvider.cs:27,83</c>;
/// <c>Services/Haptics/LovenseProvider.cs:21,83,89</c>) — nothing READ in either one is Windows-only.
/// So the axis here is not the platform: it is which routes have a client in this BUILD (both of them
/// now) and which of those the USER has switched on.</para>
///
/// <para><b>That is a claim about the SOURCE, and it is the only one made here.</b> This file used to
/// add that <i>"a Linux box running Intiface Central would answer identically"</i>, which is
/// <b>UNEVIDENCED</b> and is removed rather than softened in place: no run of either sink against any
/// server on Linux exists in this repository, nothing in it has driven a real toy on any platform,
/// and reading a Windows-only API out of two source files does not establish that a second operating
/// system behaves the same way. The manual gate below is where that would be discharged, by a human,
/// on the platform in question.</para>
///
/// <para><b>Selection is not availability</b> (<c>runtime-capability-contract.md</c> §2 rule 2).
/// Nothing here produces <see cref="Capabilities.CapabilityState.Available"/>; only
/// <see cref="HapticServerObservation.Classify"/> does, and only after a real ask that a real client
/// made.</para>
/// </summary>
public static class HapticSinkFactory
{
    /// <summary>
    /// The provider routes this build has a client for. <b>BOTH of upstream's real providers</b>, in
    /// upstream's own preference order (<c>Services/Haptics/Core/HapticDeviceManager.cs:21</c>).
    ///
    /// <para><b>This list is not a choice the user makes and never was.</b> It says which routes have
    /// a client compiled in; which of them run is the user's per-route flags
    /// (<see cref="HapticSettingsDocument.EnabledRoutes"/>), and they are a SET — upstream registers
    /// all its providers unconditionally (<c>:36-38</c>) and connects every ENABLED one together
    /// (<c>:102</c>, <c>:109-125</c>). Upstream's third provider is a mock, which this port does not
    /// have and will not add: a fake toy that reports success is the fake-available shape the
    /// truthful-capability contract bans.</para>
    /// </summary>
    public static IReadOnlyList<HapticProviderRoute> AdmittedRoutes { get; } =
        [HapticProviderRoute.Lovense, HapticProviderRoute.Buttplug];

    /// <summary>
    /// The refusal for a route with no client behind it, in one paragraph, worded so that nobody can
    /// read it as a missing toy.
    ///
    /// <para>It names both admitted routes because a refusal justified against one provider is
    /// exactly the failure this capability was built to avoid, and it names the SERVER because what
    /// is on the other end of either route is another program, not hardware.</para>
    ///
    /// <para><b>No route this build ships reaches it any more</b>, and that is the change rather than
    /// a reason to delete it: it is what <see cref="HapticProviderRoute.None"/> gets, and what a
    /// route added to the enum without a client would get, which is the only way this text can be
    /// wrong again.</para>
    /// </summary>
    public const string AdmissionGap =
        "no haptic provider client stands behind that route, so nothing was attempted. THIS IS NOT \"no device "
        + "found\": there is nothing here with which to look. Upstream ships two real providers and BOTH are clients "
        + "of a SEPARATE SERVER PROCESS the user installs, not drivers — Buttplug.io over a WebSocket to "
        + "ws://127.0.0.1:12345 into Intiface Central (Services/Haptics/ButtplugProvider.cs:27,83), and Lovense "
        + "over HTTP to http://127.0.0.1:20010 into Lovense Connect or Lovense Remote "
        + "(Services/Haptics/LovenseProvider.cs:21,83,89). THIS BUILD HAS A CLIENT FOR BOTH OF THEM, so this "
        + "refusal is about a route outside that pair and never about the product's reach.";

    /// <summary>
    /// The manual gate that a real haptic claim needs. <b>It is now ATTEMPTABLE</b> — every step
    /// below has a client behind it — and it is still UNDISCHARGED, which are different facts and
    /// must not be collapsed.
    ///
    /// <para><b>Read the order.</b> Until both routes were admitted, nothing on this list could be
    /// attempted at all, so quoting it would have told a user to fix something that was not what was
    /// wrong. That rung is gone. What remains is a device and a person, and as of 2026-08-23 the
    /// owner reports no device on hand, so no step past (1) has been run by anybody.</para>
    ///
    /// <para>The last step is the one no automated step on any platform discharges, and it is not
    /// dischargeable at any depth of API: a haptic server reports what it believes it commanded over
    /// Bluetooth, and neither this process nor upstream's can tell a toy that vibrated from one with
    /// a flat battery in the next room.</para>
    /// </summary>
    public const string DeviceManualGate =
        "MANUAL GATE (undischarged; a HUMAN with a device is the only thing that can run it, and no automated step "
        + "on any platform substitutes for step 4): "
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
    /// The sink this build hands the participant: <b>every route the user has ticked, driven
    /// together</b>.
    ///
    /// <para>The enabled set is a delegate rather than a value because it is a user setting that
    /// changes while the app runs, and because the participant constructs this sink before its
    /// settings file has loaded. Upstream re-reads the same flags at every connect
    /// (<c>Services/Haptics/Core/HapticDeviceManager.cs:102</c>, <c>:91-98</c>).</para>
    /// </summary>
    /// <param name="enabledRoutes">
    /// The user's per-route flags, asked afresh at every operation — normally
    /// <see cref="HapticSettingsDocument.EnabledRoutes"/> off the live document.
    /// </param>
    public static IHapticSink Create(Func<IReadOnlyList<HapticProviderRoute>> enabledRoutes)
    {
        ArgumentNullException.ThrowIfNull(enabledRoutes);
        return new CompositeHapticSink(
            () => Admitted(enabledRoutes()), route => SinkFor(route, AdmittedRoutes));
    }

    /// <summary>
    /// The selection itself, over a fixed route list.
    ///
    /// <para>Taking the list as a parameter is what makes the guard below EXECUTABLE rather than
    /// merely written: a fact can hand it a route with no client and watch it refuse to manufacture
    /// anything. A guard nothing can run is a comment with a keyword in front of it.</para>
    ///
    /// <para>An EMPTY list is not that guard and never was: it is the user having ticked nothing,
    /// which is a typed refusal and never an exception — upstream returns false before connecting
    /// anything (<c>Services/Haptics/Core/HapticDeviceManager.cs:103-107</c>) and its button refuses
    /// before calling the manager at all (<c>MainWindow/MainWindow.Haptics.cs:653-660</c>).</para>
    /// </summary>
    /// <param name="routes">The routes to bring up. Empty is the user having ticked nothing.</param>
    /// <param name="admittedRoutes">
    /// Which routes count as admitted; null takes <see cref="AdmittedRoutes"/>. It is a parameter so
    /// the guard below can be RUN — a fact hands it a route it calls admitted for which no client
    /// exists, and watches the factory refuse to manufacture one.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// A route is named and no sink can be constructed for it. Deliberately louder than a fallback:
    /// a factory that quietly returned a no-op for such a route would be the fake-available shape the
    /// truthful-capability contract bans, and it would do it on the exact path where a user believes
    /// their toy is connected. Both admitted routes stopped reaching it by ACQUIRING A CLIENT, never
    /// by the check being relaxed.
    /// </exception>
    public static IHapticSink CreateFrom(
        IReadOnlyList<HapticProviderRoute> routes,
        IReadOnlyList<HapticProviderRoute>? admittedRoutes = null)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var admitted = admittedRoutes ?? AdmittedRoutes;

        // Eager, not lazy: a route with no client is a BUILD mistake, and one that waited for the
        // first command would surface on the path a user is watching. Building one to check costs
        // nothing observable — no client here opens anything in its constructor; both connect only
        // in ConnectAsync — and the composite builds its own on demand.
        foreach (var route in routes)
        {
            SinkFor(route, admitted).Dispose();
        }

        return new CompositeHapticSink(() => routes, route => SinkFor(route, admitted));
    }

    /// <summary>
    /// The sink for ONE named route. A route with a client gets that client; a route without one gets
    /// the refusal that names what is behind the two this build does have.
    /// </summary>
    public static IHapticSink CreateFor(HapticProviderRoute route) => SinkFor(route, AdmittedRoutes);

    /// <summary>
    /// One route, one client. <b>The whole admission decision is this switch</b>: a route reaches its
    /// arm because a client was written for it, and everything else reaches the refusal below.
    /// </summary>
    private static IHapticSink SinkFor(
        HapticProviderRoute route, IReadOnlyList<HapticProviderRoute> admittedRoutes)
    {
        if (!admittedRoutes.Contains(route))
        {
            return new UnadmittedHapticSink(HapticReasonCodes.HapticNoAdmittedProvider, AdmissionGap);
        }

        return route switch
        {
            HapticProviderRoute.Lovense => new LovenseHapticSink(_ => { }),
            HapticProviderRoute.Buttplug => new ButtplugHapticSink(_ => { }),
            _ => throw new InvalidOperationException(
                "a haptic provider route is admitted and no sink is constructed for it — admit the route AND "
                + $"its client together ({route})"),
        };
    }

    private static IReadOnlyList<HapticProviderRoute> Admitted(IReadOnlyList<HapticProviderRoute>? wanted) =>
        wanted is null ? [] : [.. wanted.Where(AdmittedRoutes.Contains)];
}
