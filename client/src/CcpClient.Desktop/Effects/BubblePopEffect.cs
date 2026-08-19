using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// <b>Bubble Pop</b> — WPF's Bubble Pop dashboard card
/// (<c>Features/BubblePopFeatureControl.xaml.cs</c>, service <c>Services/BubbleService.cs</c>), and
/// <b>the first module in this port the user is expected to ACT on rather than watch</b>.
///
/// <para><b>Why this module exists (SP-113), and what it was built to find out.</b> Ten modules ran
/// and every one of them was something done TO the user: a picture, a sound, a question they type
/// into. SP-112 refused Bubble Pop with a census rather than an excuse — <c>Input/**</c> handles no
/// mouse message at all, owns one window, places it once, and defines <c>Confirmed</c> as
/// <c>IsForegroundWindow &amp;&amp; SystemKeyboardFocusIsThisWindow</c>, which is the exact inverse
/// of what a bubble needs. That census was re-verified line by line before this module was written
/// (<c>spine-tasks/SP-113-pointer-surface/plan.md</c> §1) and it holds in every particular.</para>
///
/// <para><b>The answer, and it is the shape of this file.</b> <see cref="ISessionEffect"/> fits
/// unchanged (fifth time). <see cref="OwnedSessionEffect"/> fits unchanged. This module takes no
/// clock and owns no timer: both cadences — the spawn interval and the 30 ms animation step — live
/// in <see cref="BubblePopSurfacePresenter"/>, on SP-106's rule that a cadence keeping a SURFACE
/// correct is the surface's. What was genuinely new is the capability underneath
/// (<c>Pointer/**</c>), not the spine.</para>
///
/// <para><b>What "running" means here, and why it needed no eighth dot meaning.</b> A field that is
/// on the desktop and that the window manager routes no click to is a picture of a game: visible,
/// and impossible to play. That is <b>DEMAND</b>, SP-110's sixth meaning — <i>a claim on the user's
/// attention which the operating system grants and can revoke without this process doing
/// anything</i> — with a different INSTRUMENT. SP-110's instrument was the foreground and the
/// keyboard focus; a bubble must take neither, so its instrument is the routing answer. The meaning
/// is the same and the finding is that the meaning was never the mechanism. See
/// <see cref="BubblePopSurfacePresenter.Running"/>.</para>
///
/// <para><b>What is NOT ported</b> is declared on <see cref="BubblePopSurfacePresenter"/> and in
/// <see cref="BubblePopPresetDocument"/>, next to the dials that would have carried it, rather than
/// shipped as switches that move nothing (D93's rule).</para>
/// </summary>
public sealed class BubblePopEffect : OwnedSessionEffect
{
    /// <summary>
    /// WPF's own rack key for this module, and it is <c>"bubbles"</c> rather than anything spelled
    /// like the row's title: <c>Add("bubbles", "🫧", "Bubble_pop.png", "Bubble Pop", …)</c>
    /// (<c>Views/Tabs/StudioTabView.xaml.cs:499-500</c>), and it is the same key the quick-toggle
    /// switches on (<c>MainWindow/MainWindow.Presets.cs:1256</c>). A-004's rule is that the dispatch
    /// identity is upstream's key and never a display string, so it is taken verbatim even though it
    /// does not match the class name.
    /// </summary>
    public const string EffectId = "bubbles";

    /// <summary>The row's label as the shipping app shows it
    /// (<c>Views/Tabs/StudioTabView.xaml.cs:499</c>).</summary>
    public const string DisplayTitle = "Bubble Pop";

    private readonly PersistenceStore<BubblePopPresetDocument> _preset;
    private readonly IBubblePopSurface? _surface;

    /// <param name="owner">This module's operation owner: one generation per armed module.</param>
    /// <param name="signal">The one boundary a state change or a placement is allowed to arrive on.</param>
    /// <param name="preset">This module's persisted dials.</param>
    /// <param name="surface">Where the clickable targets go. Null composes the module with nowhere
    /// to place them, which is what a test that wants no desktop does.</param>
    public BubblePopEffect(
        AsyncOperationOwner owner,
        EffectSignal signal,
        PersistenceStore<BubblePopPresetDocument> preset,
        IBubblePopSurface? surface = null)
        : base(owner, signal, "bubblepop-field")
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

    /// <summary>The dials a run would use right now, so the panel can show them without arming.</summary>
    public BubblePopSettings Settings
    {
        get
        {
            var preset = _preset.Current;
            return new BubblePopSettings(preset.PerMinute, preset.SizePercent, preset.SpeedBoostPercent);
        }
    }

    /// <summary>What the OS last said about placing or moving one of this module's targets, verbatim,
    /// or null when nothing has been attempted. The module panel reports it word for word.</summary>
    public CapabilityState? LastPlacement => _surface?.LastPlacement;

    /// <summary>Bubbles the user popped in the run that is up, or the last one.</summary>
    public int Popped => _surface?.Popped ?? 0;

    /// <summary>Bubbles that floated away unhit.</summary>
    public int Missed => _surface?.Missed ?? 0;

    /// <summary>How many targets are on the desktop right now, and how many of them the OS says it
    /// routes a click TO. The panel shows both, because the difference between them is the only
    /// visible symptom of a field that cannot be played.</summary>
    public (int Up, int Routable) Targets => _surface is null ? (0, 0) : (_surface.TargetsUp, _surface.RoutableTargets);

    /// <summary>How many clicks the OS delivered to this field's own windows, and how many
    /// activations its window procedure refused. <b>Evidence, not a claim</b>: a field nobody has
    /// clicked has zero of both, and no arm result or dot may rest on either.</summary>
    public (int Presses, int ActivationsRefused) Delivery => _surface?.Delivery ?? (0, 0);

    /// <summary>
    /// <b>The dot's third clause.</b> Not <see cref="IBubblePopSurface.Showing"/>, and not a stored
    /// "I started the field" bool: a field the window manager routes nothing to is a claim on the
    /// user's attention that the operating system has taken back.
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
    /// Move the spawn-frequency dial and re-time a running field. WPF's own second entry point: the
    /// frequency slider calls <c>RefreshFrequency()</c> so the change lands now rather than after the
    /// old interval expires (<c>Features/BubblePopFeatureControl.xaml.cs:116</c>).
    /// </summary>
    public void SetPerMinute(int perMinute)
    {
        var clamped = Math.Clamp(perMinute, BubblePopField.MinPerMinute, BubblePopField.MaxPerMinute);
        if (_preset.Current.PerMinute == clamped)
        {
            return;
        }

        _preset.Mutate(p => p.PerMinute = perMinute);
        Refresh();
    }

    /// <summary>
    /// Move the size dial. Applied to the NEXT spawn rather than to bubbles already on screen — this
    /// surface never resizes a placed window, which is upstream's own #494 hazard turned into a rule
    /// (<c>Pointer/Win32PointerSurface.cs</c>, <c>Move</c>).
    /// </summary>
    public void SetSizePercent(int percent)
    {
        var clamped = Math.Clamp(percent, BubblePopField.MinSizePercent, BubblePopField.MaxSizePercent);
        if (_preset.Current.SizePercent == clamped)
        {
            return;
        }

        _preset.Mutate(p => p.SizePercent = percent);
        Refresh();
    }

    /// <summary>Move the travel-speed boost. Applied to the next spawn, as upstream applies it in the
    /// bubble's own constructor (<c>Services/BubbleService.cs:2831-2836</c>).</summary>
    public void SetSpeedBoostPercent(int percent)
    {
        var clamped = Math.Clamp(
            percent, BubblePopField.MinSpeedBoostPercent, BubblePopField.MaxSpeedBoostPercent);
        if (_preset.Current.SpeedBoostPercent == clamped)
        {
            return;
        }

        _preset.Mutate(p => p.SpeedBoostPercent = percent);
        Refresh();
    }

    /// <summary>
    /// Start the field, or re-apply the dials to one already running. The dial and the generation
    /// have already been checked by <see cref="OwnedSessionEffect"/>.
    /// </summary>
    protected override CapabilityState Engage(int generation)
    {
        if (_surface is null)
        {
            return new CapabilityState.Unavailable(new CapabilityReason(
                EffectReasonCodes.EffectNoSurface,
                $"the '{Id}' module has no pointer surface in this composition, so no bubbles were placed"));
        }

        // A field's work IS a set of native windows, and a native window belongs to a UI thread. The
        // same answer the two continuous drawing modules already give, for the same reason: here the
        // arm and the placement are one act, so skip-until-bound has to be answered in the arm result
        // rather than swallowed inside it (contract §5.3).
        if (!Signal.IsBound)
        {
            return new CapabilityState.Unavailable(new CapabilityReason(
                EffectReasonCodes.EffectNoUiThread,
                $"no UI thread is bound yet, so the '{Id}' module's targets could not be placed and no bubbles "
                + "are on screen"));
        }

        return _surface.Engage(Settings);
    }

    /// <summary>
    /// Narrow the arm result to what this module can honestly claim, and <b>this is where the port's
    /// THIRD kind of degradation lands.</b>
    ///
    /// <list type="bullet">
    /// <item>No pointer channel at all — no window station, no display, or a platform this build
    /// cannot earn — is the Pink Filter answer: <c>Unavailable</c> here, <c>Armed</c> in the dot,
    /// because the whole channel is gone.</item>
    /// <item>A field that is up and that the window manager routes NO click to is
    /// <c>Degraded</c> — the module is running, the channel exists, and the very next step may route
    /// again — with a dark dot, because a game nobody can click is not being played. That is neither
    /// of the two answers the port already had: it is not a missing CHANNEL and it is not missing
    /// CONTENT.</item>
    /// </list>
    /// </summary>
    protected override CapabilityState Ready(CapabilityState engaged)
    {
        if (_surface is null)
        {
            return engaged;
        }

        // The CHANNEL being gone beats everything: no window station, no display, or a platform this
        // build cannot earn. That is the Pink Filter answer and it is not narrowed here.
        if (engaged is CapabilityState.Unavailable channel
            && channel.Reason.Code == EffectReasonCodes.PointerSurfaceUnavailable)
        {
            return engaged;
        }

        // Everything else is narrowed by what is ACTUALLY on the desktop, including a placement the
        // capability itself refused: a target the window manager routes nothing to is still a window
        // the field is holding, and the module — not the capability — is the thing that knows a game
        // is running with nobody able to play it.
        var (up, routable) = Targets;
        if (up > 0 && routable == 0)
        {
            return new CapabilityState.Degraded(
                "the field is running and its bubbles are on the desktop; the window manager is routing clicks at "
                + "none of them",
                new CapabilityReason(
                    EffectReasonCodes.PointerFieldNotRoutable,
                    $"{up} target(s) are up and the OS routes a click at the centre of none of them to this "
                    + "process; something is above them. The bubbles are visible and cannot be hit"));
        }

        return engaged;
    }

    /// <summary>
    /// Drop the field. Upstream's <c>Stop()</c> stops the spawn timer, stops the animation driver and
    /// pops every bubble (<c>Services/BubbleService.cs:715-736</c>) — and for a CONTINUOUS module
    /// "release the work" and "take it off the screen" are the same act, which is why the withdraw is
    /// here rather than in <see cref="OwnedSessionEffect.OnDisarmed"/> and why the dial going off
    /// mid-session clears the desktop too.
    ///
    /// <para><b>Thread.</b> Called from a teardown thread as well as from a gesture, and a native
    /// window belongs to the thread that made it, so the withdraw is POSTED.</para>
    ///
    /// <para><b>The <see cref="IBubblePopSurface.Showing"/> test in front of it IS only a cost
    /// saving here, and that is a correction rather than a claim.</b> One
    /// <see cref="OwnedSessionEffect.Disarm"/> reaches this three times (directly, from the
    /// generation's cancellation callback, and from the parked operation's tail), and Pink Filter's
    /// identical guard is load-bearing for its surface. This presenter's <c>Withdraw</c> is
    /// idempotent under its own gate — after the first call its target map is empty, both cadence
    /// handles are null and <c>_engaged</c> is false — so a triple post produces one teardown and
    /// two no-ops. The mutation that removes this guard SURVIVES the sweep for exactly that reason
    /// (M-ch), and the record disposes of it as an equivalent mutant with that proof rather than
    /// inventing a fact to pin a difference nobody can observe.</para>
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
