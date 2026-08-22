using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// One dial the Intensity Ramp is allowed to drive: a percentage belonging to ANOTHER module, which
/// the ramp borrows for the length of a session and gives back when it stops.
///
/// <para><b>Why the ramp does not simply call the modules.</b> Upstream's ramp writes five
/// named settings by hand inside its tick (<c>MainWindow/MainWindow.StartStop.cs:504-540</c>), which
/// is why upstream's ramp cannot be tested without an <c>AppSettings</c>, a dispatcher and a window.
/// Here the ramp knows nothing about spirals or tints: it holds a list of these, and the composition
/// root decides what is in it. That is what lets the module be exercised with no surface at all —
/// and a module that never needs a surface must never acquire one.</para>
///
/// <para><b>The two halves are separate because they have different thread rules, and that split is
/// deliberate.</b> WPF does both inside one <c>Dispatcher.Invoke</c> (<c>:504</c>). Here:</para>
/// <list type="bullet">
/// <item><see cref="Write"/> moves the PERSISTED dial. <see cref="PersistenceStore{TModel}.Mutate"/>
/// takes its own lock, so this is safe from the clock's pool thread and from a teardown thread, and
/// it runs SYNCHRONOUSLY on the caller's thread.</item>
/// <item><see cref="Reapply"/> pushes that value onto whatever the owning module has LIVE, which for
/// both current dials means touching a native overlay window. That belongs to the UI thread, so the
/// ramp routes it through a dispatch.</item>
/// </list>
///
/// <para><b>What the split buys, beyond thread safety, and it is a PORT-SIDE hazard rather than an
/// upstream one.</b> On this port's teardown path a post is not delivered — the dispatcher is
/// already down — so a single combined operation would leave the ramped value in the document for
/// <c>PersistenceStore.FlushAsync</c> to write, and the user's own opacity would be silently
/// replaced by whatever the ramp had reached. Splitting the write means the VALUE always comes back
/// even when the re-apply cannot be delivered.</para>
///
/// <para><b>Upstream has no such hole, and the mechanism that closes it is worth naming</b> because
/// this port must not lose it: every path that sets <c>_exitRequested</c> stops the engine first
/// (<c>MainWindow/MainWindow.xaml.cs:327-328</c>, <c>MainWindow/MainWindow.Settings.cs:454-462</c>,
/// and the double-panic exit at <c>MainWindow/MainWindow.xaml.cs:1207,1230</c>, whose
/// <c>StopEngine()</c> already ran at <c>:1169</c>), and <c>StopEngine</c> calls
/// <c>StopRampTimer</c> (<c>MainWindow/MainWindow.StartStop.cs:388</c> -&gt; <c>:437-479</c>), which
/// IS the restore. The bare <c>_rampTimer?.Stop()</c> at
/// <c>MainWindow/MainWindow.WindowChrome.cs:167</c> is a defensive sweep over a timer already nulled
/// at <c>MainWindow.StartStop.cs:440</c>. Recorded as D95.</para>
///
/// <para><b>Direction.</b> A dial is strictly one-directional — the ramp calls the target, and no
/// target ever calls the ramp. That is what keeps the lock order acyclic across two modules that
/// each own an <see cref="Lifecycle.AsyncOperationOwner"/> with its own lock
/// (<c>Lifecycle/OperationRegistry.cs:122</c>), and it is the property an implementation of this
/// interface must not break.</para>
/// </summary>
public interface IIntensityDial
{
    /// <summary>The owning module's rack key, e.g. <c>"spiral"</c>. Stable dispatch identity (A-004),
    /// and what the ramp records custody against.</summary>
    string Id { get; }

    /// <summary>How the ramp's panel names this dial to the user. A display string, never dispatch.</summary>
    string Label { get; }

    /// <summary>
    /// The highest value the ramp may drive this dial to. WPF caps each link inside the tick —
    /// spiral 100 (<c>MainWindow.StartStop.cs:517</c>), pink filter <b>50</b> (<c>:523</c>) — and for
    /// both ported dials that cap is the dial's own setter clamp
    /// (<c>AppSettings.cs:2675</c>, <c>:3737</c>), so the ramp can never ask for a value the owning
    /// document would silently correct.
    /// </summary>
    int Ceiling { get; }

    /// <summary>The dial's value right now, in percent.</summary>
    int Read();

    /// <summary>Move the persisted dial. Synchronous, safe from any thread, touches no window.</summary>
    void Write(int percent);

    /// <summary>Push the written value onto whatever the owning module has live. UI-thread work.</summary>
    void Reapply();
}

/// <summary>
/// The Spiral Overlay module's opacity, as a dial the ramp can drive — WPF's
/// <c>RampLinkSpiralOpacity</c> branch (<c>MainWindow/MainWindow.StartStop.cs:513-519</c>).
///
/// <para><see cref="Write"/> plus <see cref="Reapply"/> is exactly
/// <see cref="SpiralOverlayEffect.SetOpacityPercent"/> decomposed across the thread boundary — the
/// same store mutation and the same <c>Refresh()</c>, in the same order — so the ramp and the
/// panel's own slider converge on one behaviour rather than two.</para>
/// </summary>
public sealed class SpiralOpacityDial : IIntensityDial
{
    private readonly PersistenceStore<SpiralPresetDocument> _preset;
    private readonly SpiralOverlayEffect _effect;

    public SpiralOpacityDial(PersistenceStore<SpiralPresetDocument> preset, SpiralOverlayEffect effect)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(effect);
        _preset = preset;
        _effect = effect;
    }

    /// <inheritdoc/>
    public string Id => SpiralOverlayEffect.EffectId;

    /// <inheritdoc/>
    public string Label => "Spiral Overlay opacity";

    /// <inheritdoc/>
    /// <remarks>WPF <c>Math.Min(spiralBase * currentMult, 100)</c>
    /// (<c>MainWindow.StartStop.cs:517</c>), and the document's own clamp ceiling.</remarks>
    public int Ceiling => SpiralPresetDocument.MaxOpacityPercent;

    /// <inheritdoc/>
    public int Read() => _preset.Current.OpacityPercent;

    /// <inheritdoc/>
    public void Write(int percent) => _preset.Mutate(p => p.OpacityPercent = percent);

    /// <inheritdoc/>
    public void Reapply() => _effect.Refresh();
}

/// <summary>
/// The Flash Images module's opacity, as a dial the ramp can drive — WPF's
/// <c>RampLinkFlashOpacity</c> branch, the FIRST of its five and the one the port could not have
/// (<c>MainWindow/MainWindow.StartStop.cs:506-510</c>, <c>settings.FlashOpacity = newVal</c> capped
/// at 100).
///
/// <para><b>Why this dial arrives with the Visuals row and not with the ramp.</b> The ramp's own composition
/// comment named the condition exactly — "flash opacity, master volume and subliminal volume have
/// no dial on any ported panel, so they are absent rather than present-and-inert (D93)"
/// (<c>Session/SessionParticipant.cs</c>). The Visuals row IS that panel, so the condition is
/// discharged for flash opacity and for it alone; the two audio links stay absent because their
/// dials still do not exist.</para>
///
/// <para><b><see cref="Reapply"/> is a NO-OP, and that is a divergence rather than an oversight.</b>
/// Upstream re-reads <c>FlashOpacity</c> on every composition frame and re-tints windows that are
/// ALREADY on screen (<c>Services/Flash/FlashService.cs:2072</c>, spent at <c>:2108-2117</c>). This
/// port sets a layered window's <c>LWA_ALPHA</c> once, at placement, and the only way to change it
/// afterwards is <see cref="Overlay.IOverlayPresence.Present"/> — which walks the whole top-level
/// z-order and, to run its differential hit test, <b>clears click-through and restores it</b>
/// (<c>Overlay/Win32OverlayPresence.cs:558</c>, <c>:566</c>, <c>:574</c>). Re-tinting up to ten live
/// flashes on the ramp's 2-second cadence would open that gap repeatedly on a surface whose entire
/// contract is that the user's clicks pass through it. So the ramp's new value reaches the NEXT
/// flash, not the ones already up. Recorded as D174.</para>
/// </summary>
public sealed class FlashOpacityDial : IIntensityDial
{
    private readonly VisualsDials _dials;

    public FlashOpacityDial(VisualsDials dials)
    {
        ArgumentNullException.ThrowIfNull(dials);
        _dials = dials;
    }

    /// <inheritdoc/>
    /// <remarks>The dial belongs to Flash Images — the ramp records custody against the module whose
    /// value it borrowed, not against the panel the slider is drawn on.</remarks>
    public string Id => FlashImagesEffect.EffectId;

    /// <inheritdoc/>
    public string Label => "Flash Images opacity";

    /// <inheritdoc/>
    /// <remarks>WPF <c>Math.Min(flashBase * currentMult, 100)</c>
    /// (<c>MainWindow.StartStop.cs:508</c>), and the document's own clamp ceiling
    /// (<c>AppSettings.cs:859</c>).</remarks>
    public int Ceiling => VisualsPresetDocument.MaxFlashOpacityPercent;

    /// <inheritdoc/>
    public int Read() => _dials.Preset.FlashOpacityPercent;

    /// <inheritdoc/>
    /// <remarks>Routed through <see cref="VisualsDials.SetFlashOpacityPercent"/> rather than round
    /// it, so the ramp and the panel's own slider converge on one behaviour — the reason
    /// <see cref="SpiralOpacityDial"/> gives, applied to a dial whose owner is not an effect.</remarks>
    public void Write(int percent) => _dials.SetFlashOpacityPercent(percent);

    /// <inheritdoc/>
    /// <remarks>Nothing to push: see the class remarks and D174.</remarks>
    public void Reapply()
    {
    }
}

/// <summary>
/// The Pink Filter module's opacity, as a dial the ramp can drive — WPF's
/// <c>RampLinkPinkFilterOpacity</c> branch (<c>MainWindow/MainWindow.StartStop.cs:521-525</c>), whose
/// cap is <b>50</b> rather than 100 because a tint the user could take to fully opaque would black
/// out the desktop it is drawn over.
/// </summary>
public sealed class PinkFilterOpacityDial : IIntensityDial
{
    private readonly PersistenceStore<PinkFilterPresetDocument> _preset;
    private readonly PinkFilterEffect _effect;

    public PinkFilterOpacityDial(PersistenceStore<PinkFilterPresetDocument> preset, PinkFilterEffect effect)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(effect);
        _preset = preset;
        _effect = effect;
    }

    /// <inheritdoc/>
    public string Id => PinkFilterEffect.EffectId;

    /// <inheritdoc/>
    public string Label => "Pink Filter opacity";

    /// <inheritdoc/>
    /// <remarks>WPF <c>Math.Min(pinkBase * currentMult, 50)</c>
    /// (<c>MainWindow.StartStop.cs:523</c>), and the document's own clamp ceiling.</remarks>
    public int Ceiling => PinkFilterPresetDocument.MaxOpacityPercent;

    /// <inheritdoc/>
    public int Read() => _preset.Current.OpacityPercent;

    /// <inheritdoc/>
    public void Write(int percent) => _preset.Mutate(p => p.OpacityPercent = percent);

    /// <inheritdoc/>
    public void Reapply() => _effect.Refresh();
}
