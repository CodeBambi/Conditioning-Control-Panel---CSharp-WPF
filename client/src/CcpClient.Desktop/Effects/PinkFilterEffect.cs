using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// <b>Pink Filter</b> — WPF's fifth EFFECTS rack row
/// (<c>Views/Tabs/StudioTabView.xaml.cs:493-494</c>), and <b>the first CONTINUOUS module in the
/// port</b>.
///
/// <para><b>Why this module exists (SP-105), and what it was built to find out.</b> The session
/// spine had two implementations and both of them were TIMED. SP-101's own template verdict named
/// the untested axis: a continuous effect is what shows whether <see cref="ISessionEffect"/> is
/// genuinely a spine or whether the spine had quietly been assumed to be a scheduler. WPF drives
/// this module and Spiral Overlay with <b>no timer at all</b> — the quick-toggle is
/// <c>s.PinkFilterEnabled = !s.PinkFilterEnabled; App.Overlay?.RefreshOverlays();</c>
/// (<c>MainWindow/MainWindow.Presets.cs:1255</c>), with no <c>Start</c>, no <c>Stop</c>, no tick and
/// not even the <c>if (running)</c> the three paced rows above it carry (<c>:1250-1252</c>).</para>
///
/// <para><b>The answer.</b> <see cref="ISessionEffect"/> fits unchanged, and this module implements
/// it with no interval, no firing, no counter and no clock of its own.
/// <see cref="PacedSessionEffect{TFiring}"/> does not fit and was not bent to; the half of it that
/// was really the spine is now <see cref="OwnedSessionEffect"/>, which both kinds derive from.</para>
///
/// <para><b>What "running" means here, and why the dot had to change authority.</b> A paced module
/// is <see cref="EffectDotState.Live"/> when a firing is on the CLOCK — true even for Subliminals
/// over an empty pool, where every firing will show nothing. This module has no clock between
/// "armed" and "on screen", so its only honest answer is the SCREEN's:
/// <see cref="OwnedSessionEffect.WorkIsRunning"/> is the surface's own confirmed presence. A Pink
/// Filter whose overlay refused — no display, or any Linux build, where the backend refuses by
/// design — reads <see cref="EffectDotState.Armed"/>, never <c>Live</c>, and its arm result carries
/// the backend's own refusal verbatim.</para>
///
/// <para><b>What is NOT ported</b>, and is recorded rather than stubbed: the timed and sustained
/// holds that let autonomy, remote control and the Deeper engine own the tint for a while
/// (<c>Services/Notifications/OverlayService.cs:900-965</c>), <c>PulseOverlays</c> (<c>:461-500</c>),
/// the recreate-the-windows fallback after three seconds of sustained topmost loss
/// (<c>:2597-2622</c>), the per-effect monitor target and multi-monitor placement
/// (<c>:1149-1157</c>), the compositor layer route, and the mod retint in the colour chain
/// (<c>:685</c> — the port has no mod system to ask). Each is a subsystem this port does not have;
/// a silent no-op would make the module look complete.</para>
/// </summary>
public sealed class PinkFilterEffect : OwnedSessionEffect
{
    /// <summary>WPF's rack key for this module (<c>StudioTabView.xaml.cs:493</c>), and the same key
    /// its quick-toggle switches on (<c>MainWindow/MainWindow.Presets.cs:1255</c>).</summary>
    public const string EffectId = "pinkfilter";

    /// <summary>The row's label as the shipping app shows it (<c>StudioTabView.xaml.cs:493</c>).</summary>
    public const string DisplayTitle = "Pink Filter";

    private readonly PersistenceStore<PinkFilterPresetDocument> _preset;
    private readonly IPinkFilterSurface? _surface;

    public PinkFilterEffect(
        AsyncOperationOwner owner,
        EffectSignal signal,
        PersistenceStore<PinkFilterPresetDocument> preset,
        IPinkFilterSurface? surface = null)
        : base(owner, signal, "pinkfilter-layer")
    {
        ArgumentNullException.ThrowIfNull(preset);
        _preset = preset;
        _surface = surface;
    }

    /// <inheritdoc/>
    public override string Id => EffectId;

    /// <inheritdoc/>
    public override string Title => DisplayTitle;

    /// <inheritdoc/>
    public override bool Enabled => _preset.Current.Enabled;

    /// <summary>The tint this module's dials currently describe. Read by the module panel so the
    /// user sees the colour that would be used, including the fallback, without starting a session.</summary>
    public PinkFilterTint Tint
    {
        get
        {
            var preset = _preset.Current;
            var (r, g, b) = PinkFilterColour.Resolve(preset.Colour);
            return new PinkFilterTint(r, g, b, preset.OpacityPercent);
        }
    }

    /// <summary>What the OS last said about placing this module's tint, verbatim, or null when
    /// nothing has been attempted. The module panel reports it word for word.</summary>
    public CapabilityState? LastPlacement => _surface?.LastPlacement;

    /// <summary>
    /// The continuous module's answer to "is the work really running": the surface says a tint is
    /// up. Not a stored bool, not the dial, and not "Engage returned" — a module whose layer the OS
    /// refused has nothing running, and this is the value that stops its dot claiming otherwise.
    /// </summary>
    protected override bool WorkIsRunning => _surface is { Showing: true };

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
    /// Move the opacity dial and re-tint whatever is already on screen — WPF's opacity slider, which
    /// writes the setting, saves, and calls <c>RefreshOverlays()</c> so the change lands on the live
    /// window instead of at the next session (<c>Features/PinkFilterFeatureControl.xaml.cs:99-109</c>,
    /// reconciled at <c>OverlayService.cs:434-437</c>). The save is the caller's, exactly as the
    /// flash panel's sliders own theirs.
    /// </summary>
    public void SetOpacityPercent(int percent)
    {
        if (_preset.Current.OpacityPercent == Math.Clamp(
            percent, PinkFilterPresetDocument.MinOpacityPercent, PinkFilterPresetDocument.MaxOpacityPercent))
        {
            return;
        }

        _preset.Mutate(p => p.OpacityPercent = percent);
        Refresh();
    }

    /// <summary>
    /// Put the tint up, or re-tint the one that is already up. The dial and the generation have
    /// already been checked by <see cref="OwnedSessionEffect"/>; what is left is this module's own
    /// three answers.
    /// </summary>
    protected override CapabilityState Engage(int generation)
    {
        var tint = Tint;

        // WPF's clamp lets the opacity reach ZERO (AppSettings.cs:3737) and WPF at zero still
        // creates a full-screen layered window holding alpha 0 — a window the OS agrees exists, is
        // visible and is on top, that composites nothing. That is the exact ghost this port's
        // overlay refuses to construct, so nothing is placed and the module says so in type: the
        // module really did take the session (Degraded, not Unavailable) and really will show
        // nothing (not Available). Its dot then reads Armed, because nothing is running.
        if (tint.IsInvisible)
        {
            _surface?.Withdraw();
            return new CapabilityState.Degraded(
                "the module is engaged and its tint is fully transparent, so nothing is drawn",
                new CapabilityReason(
                    EffectReasonCodes.PinkFilterTransparent,
                    "the pink filter's opacity dial is at 0 %, so there is nothing to put on screen"));
        }

        if (_surface is null)
        {
            return new CapabilityState.Unavailable(new CapabilityReason(
                EffectReasonCodes.EffectNoSurface,
                $"the '{Id}' module has no surface in this composition, so nothing was placed on screen"));
        }

        // A continuous module's work IS a native window, and a native window belongs to a UI
        // thread. The paced modules never meet this: scheduling needs no UI, and their draw is a
        // later posted projection that is simply skipped while the boundary is unbound (contract
        // §5.3). Here the arm and the draw are the same act, so skip-until-bound has to be answered
        // in the arm result rather than swallowed inside it.
        if (!Signal.IsBound)
        {
            return new CapabilityState.Unavailable(new CapabilityReason(
                EffectReasonCodes.EffectNoUiThread,
                $"no UI thread is bound yet, so the '{Id}' module's surface could not be placed and nothing "
                + "was drawn"));
        }

        return _surface.Engage(tint);
    }

    /// <summary>
    /// Take the tint off the screen. WPF's stop closes every filter window
    /// (<c>OverlayService.cs:407</c> -&gt; <c>:1233-1249</c>) and its reconcile does the same the
    /// moment the dial goes off (<c>:434-437</c>) — the half of stop the user can see.
    ///
    /// <para><b>This is the shape of the whole finding, in one method.</b> For a PACED module,
    /// "release the work" and "take it off the screen" are two different acts: the base drops the
    /// pending one-shot here and the surfaces that are already up retire on their own lifetimes,
    /// which is why nothing hides when a flash module's dial goes off mid-session
    /// (<c>FlashService.cs:538-546</c> returns without closing a window). For a CONTINUOUS module
    /// they are the SAME act, because the work IS the surface. So the withdraw lives here rather
    /// than in <see cref="OwnedSessionEffect.OnDisarmed"/>, which means the dial-off path takes the
    /// tint down as well as the stop path — and that is upstream's behaviour, not a convenience.</para>
    ///
    /// <para><b>Thread.</b> This is called from a teardown thread as well as from a gesture, and a
    /// native window belongs to the thread that made it, so the withdraw is POSTED. The
    /// <see cref="IPinkFilterSurface.Showing"/> test in front of it is not an optimisation: one
    /// <see cref="OwnedSessionEffect.Disarm"/> reaches this three times (directly, from the
    /// generation's cancellation callback, and from the parked operation's tail), and without it
    /// every stop would queue three teardowns of a surface that only exists once.</para>
    /// </summary>
    protected override void ReleaseWork()
    {
        if (_surface is not { Showing: true })
        {
            return;
        }

        Signal.Post(_surface.Withdraw);
    }
}
