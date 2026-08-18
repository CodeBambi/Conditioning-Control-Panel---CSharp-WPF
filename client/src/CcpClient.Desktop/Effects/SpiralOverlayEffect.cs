using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// <b>Spiral Overlay</b> — WPF's fourth EFFECTS rack row
/// (<c>Views/Tabs/StudioTabView.xaml.cs:490-491</c>), and <b>the first module in the port whose
/// picture has to keep changing while it is on</b>.
///
/// <para><b>Why this module exists (SP-106), and what it was built to find out.</b> Three modules
/// ran under the spine and none of them had to move: Flash Images and Subliminals are PACED (fire,
/// wait, fire) and Pink Filter is CONTINUOUS but STATIC (it goes up and stays up). Per-frame work
/// was the untested axis, and SP-105's own verdict named the risk it carries: a module that must
/// change every frame might end up re-implementing a scheduler to get its frames, which is exactly
/// the thing <see cref="OwnedSessionEffect"/> was created to remove.</para>
///
/// <para><b>The answer, and it is the shape of this file.</b> <see cref="ISessionEffect"/> fits
/// unchanged (third time). <see cref="OwnedSessionEffect"/> fits unchanged. <b>This module takes no
/// clock, implements no interval, and owns no timer</b> — the frame cadence lives in
/// <see cref="SpiralSurfacePresenter"/>, beside the topmost cadence SP-105 already put there, on
/// the precedent that a cadence which keeps a SURFACE correct is the surface's while an interval
/// that decides when a MODULE is due is <see cref="PacedSessionEffect{TFiring}"/>'s. A spiral is
/// never "due": it is on, and what is on screen turns.</para>
///
/// <para><b>What "running" means here, and why the dot needed a third meaning.</b> A paced module is
/// <see cref="EffectDotState.Live"/> when a firing is on the CLOCK. A static continuous module is
/// <c>Live</c> when its surface is confirmed on the SCREEN. Neither can say what this module needs
/// to say, because <b>"on screen but frozen" is a real state and it is not always a broken one</b>:
/// WPF starts no frame timer for a one-frame spiral (<c>Services/Notifications/OverlayService.cs:1370</c>),
/// so a still image sitting there is the module working. <c>Live</c> here therefore means the
/// surface is up, the last frame was really held, AND — only when the clip has more than one frame —
/// the next advance is on the clock. See <see cref="SpiralSurfacePresenter.Running"/>.</para>
///
/// <para><b>What is NOT ported</b>, and is recorded rather than stubbed: the randomiser that picks a
/// fresh spiral per session (<c>OverlayService.cs:311-316</c>, D87); the spiral compiled into the
/// application, because this port bundles no art (<c>ModResourceResolver.ResolveSpiralUri()</c>,
/// D86); the video-file route through <c>MediaElement</c> (<c>:1745-1800</c>), which needs a media
/// stack the port does not have (D89); the timed and sustained holds that let autonomy, remote
/// control and the Deeper engine own the spiral for a while (<c>:900-965</c>); the ramp link
/// (<c>:542-543</c>); the per-effect monitor target (<c>:1347</c>); and the compositor layer route
/// (<c>:1297</c>). Each is a subsystem this port does not have; a silent no-op would make the module
/// look complete.</para>
/// </summary>
public sealed class SpiralOverlayEffect : OwnedSessionEffect
{
    /// <summary>WPF's rack key for this module (<c>StudioTabView.xaml.cs:490</c>), and the same key
    /// its quick-toggle switches on (<c>MainWindow/MainWindow.Presets.cs:1253</c>).</summary>
    public const string EffectId = "spiral";

    /// <summary>The row's label as the shipping app shows it (<c>StudioTabView.xaml.cs:490</c>).</summary>
    public const string DisplayTitle = "Spiral Overlay";

    private readonly PersistenceStore<SpiralPresetDocument> _preset;
    private readonly ISpiralSurface? _surface;
    private readonly Func<string?> _resolvePath;

    /// <param name="owner">This module's operation owner: one generation per armed module.</param>
    /// <param name="signal">The one boundary a state change or a draw is allowed to arrive on.</param>
    /// <param name="preset">This module's persisted dials.</param>
    /// <param name="resolvePath">Which spiral to draw, re-asked on every engage — WPF re-resolves
    /// inside its own reconcile too (<c>OverlayService.cs:437</c>). Null means the library is
    /// empty, which is an ordinary first-run state and not a failure.</param>
    /// <param name="surface">Where the frames go. Null composes the module with nowhere to draw,
    /// which is what a test that wants no window does.</param>
    public SpiralOverlayEffect(
        AsyncOperationOwner owner,
        EffectSignal signal,
        PersistenceStore<SpiralPresetDocument> preset,
        Func<string?> resolvePath,
        ISpiralSurface? surface = null)
        : base(owner, signal, "spiral-layer")
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(resolvePath);
        _preset = preset;
        _resolvePath = resolvePath;
        _surface = surface;
    }

    /// <inheritdoc/>
    public override string Id => EffectId;

    /// <inheritdoc/>
    public override string Title => DisplayTitle;

    /// <inheritdoc/>
    public override bool Enabled => _preset.Current.Enabled;

    /// <summary>The presentation this module's dials currently describe. Read by the module panel so
    /// the user sees the opacity that would really be used — the dial times WPF's ×0.1 — without
    /// starting a session.</summary>
    public SpiralPresentation Presentation => new(_preset.Current.OpacityPercent);

    /// <summary>The spiral this module would draw right now, or null when there is none. The panel
    /// reports the FILE NAME only; the folder is named separately, exactly as the flash pool's line
    /// does.</summary>
    public string? SpiralPath => _resolvePath();

    /// <summary>How many frames the open clip has, or 0 when nothing is open. One means a still
    /// spiral, which never moves and is not supposed to.</summary>
    public int FrameCount => _surface?.FrameCount ?? 0;

    /// <summary>What the OS last said about placing this module's layer, verbatim, or null when
    /// nothing has been attempted. The module panel reports it word for word.</summary>
    public CapabilityState? LastPlacement => _surface?.LastPlacement;

    /// <summary>True while the surface is up at all, whether or not it is still moving. The panel
    /// uses this to tell the two frozen states apart; the DOT does not, on purpose.</summary>
    public bool Showing => _surface is { Showing: true };

    /// <summary>
    /// The moving module's answer to "is the work really running": the surface says its picture is
    /// up AND still changing. Not a stored bool, not the dial, and not "Engage returned" — and
    /// notably not <see cref="Showing"/> either, because a spiral that stopped turning is exactly
    /// the confident half-truth this three-state design exists to refuse.
    /// </summary>
    protected override bool WorkIsRunning => _surface is { Running: true };

    /// <inheritdoc/>
    public override void SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
        {
            return;
        }

        _preset.Mutate(p => p.Enabled = enabled);
        RaiseChanged();
    }

    /// <summary>
    /// Move the opacity dial and re-apply it to whatever is already on screen — WPF's opacity
    /// slider, which writes the setting and calls <c>RefreshOverlays()</c> so the change lands on
    /// the live window instead of at the next session (<c>Features/SpiralFeatureControl.xaml.cs</c>
    /// -&gt; <c>OverlayService.cs:446</c>, <c>UpdateSpiralOpacity</c>). The save is the caller's,
    /// exactly as the other three panels own theirs.
    /// </summary>
    public void SetOpacityPercent(int percent)
    {
        if (_preset.Current.OpacityPercent == Math.Clamp(
            percent, SpiralPresetDocument.MinOpacityPercent, SpiralPresetDocument.MaxOpacityPercent))
        {
            return;
        }

        _preset.Mutate(p => p.OpacityPercent = percent);
        Refresh();
    }

    /// <summary>
    /// Put the spiral up, or re-apply it to the one that is already up. The dial and the generation
    /// have already been checked by <see cref="OwnedSessionEffect"/>; what is left is this module's
    /// own four answers, in the order that keeps each one honest.
    /// </summary>
    protected override CapabilityState Engage(int generation)
    {
        var presentation = Presentation;

        // WPF's clamp lets the opacity reach ZERO (AppSettings.cs:2675) and WPF at zero still
        // creates a full-screen always-on-top window holding a fully transparent image — the exact
        // ghost this port's overlay refuses to construct. So nothing is placed and the module says
        // so in type: it really did take the session (Degraded, not Unavailable) and really will
        // show nothing (not Available). Its dot then reads Armed, because nothing is running.
        if (presentation.IsInvisible)
        {
            _surface?.Withdraw();
            return new CapabilityState.Degraded(
                "the module is engaged and its layer is fully transparent, so nothing is drawn",
                new CapabilityReason(
                    EffectReasonCodes.SpiralTransparent,
                    "the spiral's opacity dial is at 0 %, so there is nothing to put on screen"));
        }

        // WPF's own second condition (OverlayService.cs:377). An empty library is a first-run state,
        // not a fault: the paced sibling with an empty phrase pool reports the same grade of outcome
        // (subliminal-no-active-phrase), and for the same reason — the module holds the session and
        // the visible half does not.
        var path = _resolvePath();
        if (string.IsNullOrWhiteSpace(path))
        {
            _surface?.Withdraw();
            return new CapabilityState.Degraded(
                "the module is engaged and has no spiral to draw, so nothing is on screen",
                new CapabilityReason(
                    EffectReasonCodes.SpiralNoImage,
                    "no spiral is configured and the spirals folder has none this build can decode, so there "
                    + "is nothing to put on screen"));
        }

        if (_surface is null)
        {
            return new CapabilityState.Unavailable(new CapabilityReason(
                EffectReasonCodes.EffectNoSurface,
                $"the '{Id}' module has no surface in this composition, so nothing was placed on screen"));
        }

        // A continuous module's work IS a native window, and a native window belongs to a UI thread.
        // The paced modules never meet this: scheduling needs no UI, and their draw is a later posted
        // projection that is simply skipped while the boundary is unbound (contract §5.3). Here the
        // arm and the draw are the same act, so skip-until-bound has to be answered in the arm
        // result rather than swallowed inside it. (SP-105's finding, unchanged for a fourth module —
        // it is a property of DRAWING at arm time, not of being static.)
        if (!Signal.IsBound)
        {
            return new CapabilityState.Unavailable(new CapabilityReason(
                EffectReasonCodes.EffectNoUiThread,
                $"no UI thread is bound yet, so the '{Id}' module's surface could not be placed and nothing "
                + "was drawn"));
        }

        return _surface.Engage(path, presentation);
    }

    /// <summary>
    /// A running module that is drawing nothing narrows its own claim, which is the seat
    /// <see cref="OwnedSessionEffect.Ready"/> exists for. Here it catches the case the surface can
    /// produce and the module cannot see coming: the layer went up and <b>then</b> stopped moving,
    /// so the OS's <c>Available</c> is true about the placement and false about the module.
    /// </summary>
    protected override CapabilityState Ready(CapabilityState engaged)
    {
        if (engaged is CapabilityState.Available available && _surface is { Running: false })
        {
            return new CapabilityState.Degraded(
                available.Detail,
                new CapabilityReason(
                    EffectReasonCodes.SpiralNotDecoded,
                    "the spiral's layer is on screen but its frames are not advancing, so the picture is "
                    + "frozen"));
        }

        return engaged;
    }

    /// <summary>
    /// Take the spiral off the screen and stop advancing it. WPF's stop closes every spiral window
    /// and kills the frame timer (<c>OverlayService.cs:407</c> -&gt; <c>StopSpiral</c>), and its
    /// reconcile does the same the moment the dial goes off (<c>:448-450</c>).
    ///
    /// <para><b>For a MOVING module, releasing the work is two things at once</b>, and that is the
    /// only place this module's teardown differs from the static one's. A paced module drops a
    /// pending one-shot and leaves the screen alone; a static continuous module withdraws a surface
    /// and has no timer; this one has BOTH, and the timer must die with the surface or a stopped
    /// session would keep repainting a window nobody can see. The presenter does both inside
    /// <see cref="ISpiralSurface.Withdraw"/> so the pair cannot be separated by a caller.</para>
    ///
    /// <para><b>Thread.</b> This is called from a teardown thread as well as from a gesture, and a
    /// native window belongs to the thread that made it, so the withdraw is POSTED. The
    /// <see cref="ISpiralSurface.Engaged"/> test in front of it is not an optimisation: one
    /// <see cref="OwnedSessionEffect.Disarm"/> reaches this three times (directly, from the
    /// generation's cancellation callback, and from the parked operation's tail). It tests
    /// <c>Engaged</c> rather than <c>Showing</c> because a spiral whose repaint failed is no longer
    /// showing and <b>still owns an open clip</b>: <see cref="OverlaySurfaceSet.Repaint"/> retires
    /// the slot, and the advance that discovered the failure has already nulled its own handle
    /// without re-arming, so the TIMER is gone by then — but the decoder is not, and a
    /// <c>Showing</c> guard would leak it for the rest of the process.</para>
    /// </summary>
    protected override void ReleaseWork()
    {
        if (_surface is not { Engaged: true })
        {
            return;
        }

        Signal.Post(_surface.Withdraw);
    }
}
