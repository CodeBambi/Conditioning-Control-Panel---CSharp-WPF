namespace CcpClient.Desktop.Session;

/// <summary>
/// Stable machine-readable reason codes for what a session module says when it is armed
/// (runtime-capability-contract §1: "codes are additive; new codes land with their consumer row").
/// They live beside their consumer, as <see cref="Overlay.OverlayReasonCodes"/> and
/// <see cref="Tray.TrayReasonCodes"/> do.
///
/// <para><b>Why arming has reason codes at all (SP-101, hazard 1 from SP-098's review).</b>
/// <c>Arm()</c> returned <c>void</c>, so "this module took the session and is paced" and "this
/// module did nothing" were the same observation. That is survivable for two modules whose only
/// precondition is a persisted flag. It is not survivable for the modules still to come, whose
/// preconditions are an audio device and a webcam: a module that cannot run must be able to SAY so,
/// in the typed vocabulary the rest of the port already refuses in, or the session will report
/// itself running with a silent hole in it.</para>
/// </summary>
public static class EffectReasonCodes
{
    /// <summary>
    /// The module's own persisted dial is off, so nothing was scheduled. <b>Not a refusal</b> —
    /// it is the user's setting, and WPF's own start body simply does not call a disabled module
    /// (<c>MainWindow/MainWindow.StartStop.cs:186</c>). It is typed because a caller that cannot
    /// tell it from a successful arm cannot report which modules a session actually took.
    /// </summary>
    public const string EffectDialOff = "effect-dial-off";

    /// <summary>
    /// The generation behind the schedule was already cancelled when the arm reached the clock, so
    /// nothing was scheduled. This is the teardown-races-arm window, and it is an outcome rather
    /// than a fault (async-lifecycle-fault-contract §5.5).
    /// </summary>
    public const string EffectGenerationCancelled = "effect-generation-cancelled";

    /// <summary>
    /// Subliminals: the phrase pool has nothing active in it, so the schedule runs and every firing
    /// shows nothing. Upstream's own outcome — <c>FlashSubliminal</c> logs "No active subliminal
    /// texts" and returns before anything is counted or displayed
    /// (<c>Services/Subliminal/SubliminalService.cs:207-212</c>) — and the reason the arm result is
    /// <c>Degraded</c> rather than <c>Available</c> or <c>Unavailable</c>: the paced half really
    /// holds, the visible half really does not.
    /// </summary>
    public const string SubliminalNoActivePhrase = "subliminal-no-active-phrase";

    /// <summary>
    /// A CONTINUOUS module's work is a native window, and this composition has no surface to place
    /// one on (SP-105). Distinct from <see cref="EffectNoUiThread"/>: there, a surface exists and
    /// there is no thread that may legally touch it; here there is no surface at all, which is what
    /// a build or a test that composed the module without one produces.
    /// </summary>
    public const string EffectNoSurface = "effect-no-surface";

    /// <summary>
    /// No UI thread is bound, so a continuous module's surface could not be placed (SP-105).
    ///
    /// <para><b>This code exists because a continuous module cannot use skip-until-bound the way a
    /// paced one does.</b> A paced module schedules on a clock — no UI needed — and its DRAW is a
    /// later posted projection that is silently skipped while the boundary is unbound
    /// (async-lifecycle-fault-contract §5.3). For a module that is simply on, the arm and the draw
    /// are the same act, so "skipped" is the whole outcome and has to be sayable rather than
    /// swallowed.</para>
    /// </summary>
    public const string EffectNoUiThread = "effect-no-ui-thread";

    /// <summary>
    /// Pink Filter: the opacity dial is at zero, so the module is engaged and there is nothing to
    /// draw. WPF's clamp allows it — <c>Math.Clamp(value, 0, 50)</c>
    /// (<c>CCP.Core/Models/AppSettings.cs:3737</c>) — and WPF at zero still puts a full-screen
    /// layered window on the desktop holding alpha 0. The port refuses to place an invisible
    /// always-on-top window (<c>Overlay/OverlaySurfaceRequest.cs</c> will not construct one), so the
    /// arm result is <see cref="Capabilities.CapabilityState.Degraded"/>: the module really took the session, and
    /// really will show nothing. The Subliminals shape (<see cref="SubliminalNoActivePhrase"/>) with
    /// one difference that matters — the dot goes with it. A paced module with nothing to show is
    /// still <c>Live</c> because its clock is running; this one has no clock, so it reads
    /// <c>Armed</c>.
    /// </summary>
    public const string PinkFilterTransparent = "pink-filter-transparent";

    /// <summary>
    /// Spiral Overlay: the module is on and there is no spiral to draw (SP-106). WPF's start has
    /// the same second condition — <c>settings.SpiralEnabled &amp;&amp; !string.IsNullOrEmpty(spiralPath)</c>
    /// (<c>Services/Notifications/OverlayService.cs:377</c>) — and its third fallback is a spiral
    /// compiled into the application, which this port does not bundle (D86). So on a fresh install
    /// the library is empty and the module takes the session and shows nothing: the
    /// <see cref="SubliminalNoActivePhrase"/> shape, one module later.
    /// </summary>
    public const string SpiralNoImage = "spiral-no-image";

    /// <summary>
    /// Spiral Overlay: a spiral file was found and this build could not turn it into frames — it is
    /// missing, corrupt, or in a format with no codec here. Upstream's own outcome is a warning and
    /// a return with nothing on screen (<c>OverlayService.cs:1352-1356</c>), and it is typed here so
    /// the panel can tell "you have no spiral" from "your spiral does not work".
    /// </summary>
    public const string SpiralNotDecoded = "spiral-not-decoded";

    /// <summary>
    /// Spiral Overlay: the opacity dial is at zero, so the module is engaged and there is nothing to
    /// draw. WPF's clamp allows it (<c>CCP.Core/Models/AppSettings.cs:2675</c>) and WPF at zero still
    /// puts a full-screen always-on-top window on the desktop holding a fully transparent image. The
    /// <see cref="PinkFilterTransparent"/> case, on the module next to it, with the same answer:
    /// <see cref="Capabilities.CapabilityState.Degraded"/> from the module, nothing placed, and a dot
    /// that reads <c>Armed</c>.
    /// </summary>
    public const string SpiralTransparent = "spiral-transparent";

    /// <summary>
    /// Intensity Ramp: this composition gave the ramp no dials at all, so there is nothing for it to
    /// drive (SP-108). The <see cref="EffectNoSurface"/> shape on a module that has no surface: a
    /// build or a test that composed the module without its targets, said in type rather than run as
    /// a timer that writes nothing.
    /// </summary>
    public const string RampNoDial = "ramp-no-dial";

    /// <summary>
    /// Intensity Ramp: dials exist and the user has linked none of them, so the ramp will run for
    /// the whole session and change nothing (SP-108). Every link ships OFF
    /// (<c>CCP.Core/Models/AppSettings.cs:2589-2621</c>), so this is the state a freshly enabled ramp
    /// is in until the user ticks something — which makes it the one refusal here a user is likely to
    /// meet, and the reason the arm result is <see cref="Capabilities.CapabilityState.Degraded"/>
    /// rather than silence.
    ///
    /// <para><b>It is also the dot's negative control.</b> A ramp holding no dial has taken nothing
    /// and owes nothing back, so <see cref="EffectDotState.Live"/> would be a claim about a module
    /// the user cannot observe by any means.</para>
    /// </summary>
    public const string RampNoLinkedDial = "ramp-no-linked-dial";

    /// <summary>
    /// Intensity Ramp: the multiplier is at its floor of 1.0×
    /// (<c>CCP.Core/Models/AppSettings.cs:2467-2471</c> — the default), so
    /// <c>currentMult = 1.0 + (1.0 - 1.0) × eased</c> is 1.0 for the whole session and no held dial
    /// will climb (<c>MainWindow/MainWindow.StartStop.cs:501</c>).
    ///
    /// <para><b>This takes the SUBLIMINALS answer, not the PINK FILTER answer, and the difference is
    /// the dot.</b> A tint at 0 % opacity has no window at all, so it is <c>Degraded</c> and reads
    /// <c>Armed</c>. A ramp at neutral gain is a live machine holding real state — moving the
    /// multiplier makes the dials climb with no re-arm, and STOP still gives them back — so it is
    /// <c>Degraded</c> and reads <c>Live</c>, exactly as a paced module over an empty pool is.</para>
    /// </summary>
    public const string RampMultiplierFlat = "ramp-multiplier-flat";
}
