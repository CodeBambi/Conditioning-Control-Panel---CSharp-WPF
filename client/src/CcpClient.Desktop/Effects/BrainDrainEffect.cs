using CcpClient.Desktop.Audio;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// <b>Brain Drain — the AUDIO half, and only the audio half.</b> WPF's IMMERSION rack row
/// (<c>Views/Tabs/StudioTabView.xaml.cs:514-515</c>), started by <c>StartEngine</c> at
/// <c>MainWindow/MainWindow.StartStop.cs:241-244</c> and stopped at <c>:342</c>.
///
/// <para><b>THIS ROW IS HALF A ROW, PERMANENTLY, AND SAYS SO EVERYWHERE A USER CAN SEE.</b> Upstream
/// puts two unrelated mechanisms behind one <c>BrainDrainEnabled</c> flag:</para>
/// <list type="number">
/// <item>the <b>AUDIO half</b> — a paced one-shot on a 500 ms/5 s window
/// (<c>Services/LockCard/BrainDrainService.cs:13-30,178-189</c>), which is what this class is;</item>
/// <item>the <b>VISUAL half</b> — a desktop-wide blur/melt started from the same flag
/// (<c>Services/Notifications/OverlayService.cs:382-386</c>), which upstream's own panel labels in as
/// many words: "VISUAL half: the screen blur's strength"
/// (<c>Views/Controls/Studio/BrainDrainFeatureControl.xaml.cs:170</c>).</item>
/// </list>
/// <para>The second one is not portable here and not nearly: its fallback route runs a <b>30–60 FPS
/// screen-capture timer</b> feeding per-screen blur windows
/// (<c>OverlayService.cs:1965-1995</c>), and blurring what is BEHIND an overlay needs a desktop
/// read-back this port has no capability for at all. Its other route is a compositor layer
/// (<c>Services/Compositor/BrainDrainCapturePump.cs</c>, <c>BrainDrainLayer.cs</c>) that is a
/// subsystem of its own.</para>
///
/// <para><b>So the absence is made loud rather than left silent, in three places.</b> The rack row is
/// titled <see cref="DisplayTitle"/> — "Brain Drain (audio half)" — so a user reads the scope before
/// they enable anything. The panel carries a permanent notice naming the blur and why it is not here.
/// And <see cref="Ready"/> returns <see cref="CapabilityState.Degraded"/> <b>on every healthy
/// run</b>, carrying <see cref="EffectReasonCodes.BrainDrainVisualHalfAbsent"/>, because the missing
/// half is a property of the BUILD and not of the run: a row that only admitted to being half a row
/// when something else broke would be exactly the silently-missing half this is guarding against.</para>
///
/// <para><b>The dot is <see cref="EffectDotState.Live"/> while the audio half runs, and that is a
/// decision rather than an oversight.</b> Two landed rules disagree here — SP-105's says a module
/// that cannot draw is <c>Armed</c>, the Subliminals rule says a module whose schedule is really on
/// the clock is <c>Live</c> — and neither settles it, because neither was written for a row that is a
/// PROPER SUBSET of its upstream. The rule this packet adds: <b>the dot is scoped to what the row IS,
/// and a row that is a subset must say so where the user reads it.</b> Everything this row claims to
/// be is running and audible, so <c>Live</c> is true of it; <c>Armed</c> would be the opposite lie
/// from the one SP-105 prevented — an under-claim about a module that is really working. SP-105's
/// rule still bites in its own case and is implemented: with no confirmed render session this module
/// reads <c>Armed</c>, because then nothing reaches anybody at all
/// (<see cref="AudioCueEffect.WorkIsRunning"/>).</para>
///
/// <para><b>What else is NOT ported</b>: the level-70 unlock (<c>BrainDrainService.cs:116-120</c> —
/// the port has no level system), the Discord presence call (<c>:138</c>), the live master-volume
/// push (<c>:339-349</c>, and the port has no master volume at all — see
/// <see cref="BrainDrainPresetDocument.DefaultVolumePercent"/>), and the melt variant of the blur
/// (<c>MainWindow.StartStop.cs:241</c>), which belongs to the visual half.</para>
/// </summary>
public sealed class BrainDrainEffect : AudioCueEffect
{
    /// <summary>WPF's rack key for this module (<c>StudioTabView.xaml.cs:514</c>).</summary>
    public const string EffectId = "braindrain";

    /// <summary>
    /// The row's label — upstream's "Brain Drain" (<c>StudioTabView.xaml.cs:514</c>,
    /// <c>en.json:1428</c>) with the scope appended. <b>The suffix is load-bearing, not decoration:</b>
    /// it is what makes the dot's <c>Live</c> honest, because the dot is scoped to what the row says
    /// it is. Renaming this row to plain "Brain Drain" would turn a truthful dot into a false one.
    /// </summary>
    public const string DisplayTitle = "Brain Drain (audio half)";

    /// <summary>Upstream's folder name under the sounds root (<c>BrainDrainService.cs:75</c>).</summary>
    public const string ClipFolderName = "braindrain";

    private readonly PersistenceStore<BrainDrainPresetDocument> _preset;

    public BrainDrainEffect(
        AsyncOperationOwner owner,
        EffectSignal signal,
        ISessionClock clock,
        IAudioCuePool pool,
        IAudioPresence presence,
        PersistenceStore<BrainDrainPresetDocument> preset,
        Random? random = null)
        : base(owner, signal, clock, "braindrain-schedule", pool, presence, random)
    {
        ArgumentNullException.ThrowIfNull(preset);
        _preset = preset;
    }

    /// <summary>
    /// The sentence the panel shows about the missing half, and the one the arm result's reason
    /// carries. Held as a constant so the row, the panel and the typed outcome cannot drift into
    /// three different accounts of the same absence.
    /// </summary>
    public const string VisualHalfNotice =
        "This row is the AUDIO half of Brain Drain only. Upstream's same switch also blurs and melts "
        + "your whole desktop — its own panel calls that the \"VISUAL half\" — by capturing the screen "
        + "30 to 60 times a second and re-blurring it behind a full-screen overlay. This build has no "
        + "desktop read-back capability at all, so that half is ABSENT here rather than broken, and it "
        + "is not coming back with a setting. Nothing on this panel turns it on.";

    /// <inheritdoc/>
    public override string Id => EffectId;

    /// <inheritdoc/>
    public override string Title => DisplayTitle;

    /// <inheritdoc/>
    public override bool Enabled => _preset.Current.Enabled;

    /// <summary>This module's persisted dials (public so the panel reads the real document).</summary>
    public BrainDrainPresetDocument Preset => _preset.Current;

    /// <inheritdoc/>
    protected override TimeSpan Window => BrainDrainSchedule.Window(_preset.Current.HighRefresh);

    /// <inheritdoc/>
    protected override double TriggerProbability =>
        BrainDrainSchedule.TriggerProbability(_preset.Current.IntensityPercent, Window);

    /// <inheritdoc/>
    protected override float Volume => _preset.Current.VolumePercent / 100f;

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

    /// <summary>The AUDIO half's intensity dial (<c>BrainDrainService.cs:46-50</c>). Writes and
    /// re-evaluates, the port's convention since SP-105.</summary>
    public void SetIntensityPercent(int percent)
    {
        var clamped = Math.Clamp(percent, BrainDrainSchedule.MinIntensity, BrainDrainSchedule.MaxIntensity);
        if (_preset.Current.IntensityPercent == clamped)
        {
            return;
        }

        _preset.Mutate(p => p.IntensityPercent = percent);
        Refresh();
    }

    /// <summary>
    /// The high-refresh switch (<c>BrainDrainService.cs:60-69</c>). It re-evaluates, and here that is
    /// behaviour rather than convention: the window IS the schedule, so a switch that only took
    /// effect at the next firing would leave a 5-second tick running after the user asked for
    /// 500 ms. Upstream reaches the same place by calling <c>UpdateTimerInterval</c> on start
    /// (<c>:131</c>).
    /// </summary>
    public void SetHighRefresh(bool highRefresh)
    {
        if (_preset.Current.HighRefresh == highRefresh)
        {
            return;
        }

        _preset.Mutate(p => p.HighRefresh = highRefresh);
        Refresh();
    }

    /// <summary>The port-local volume dial (see <see cref="BrainDrainPresetDocument.DefaultVolumePercent"/>
    /// for why upstream has none).</summary>
    public void SetVolumePercent(int percent)
    {
        var clamped = Math.Clamp(
            percent, BrainDrainPresetDocument.MinVolumePercent, BrainDrainPresetDocument.MaxVolumePercent);
        if (_preset.Current.VolumePercent == clamped)
        {
            return;
        }

        _preset.Mutate(p => p.VolumePercent = percent);
        RaiseChanged();
    }

    /// <summary>
    /// <b>Never <see cref="CapabilityState.Available"/>, on any run, however healthy.</b> The shared
    /// audio body's narrowings are applied first — a missing render session still refuses outright,
    /// an empty clip folder still degrades for its own reason — and whatever survives is then
    /// narrowed once more, unconditionally, to name the half of this row that does not exist here.
    ///
    /// <para>That unconditional step is the point. Every other module in the rack can report a clean
    /// <c>Available</c> when everything works, because every other module is the whole of its
    /// upstream row. This one is not, and a session that recorded it as fully armed would be
    /// recording something untrue about the build rather than about the run.</para>
    /// </summary>
    protected override CapabilityState Ready(CapabilityState scheduled)
    {
        var audio = base.Ready(scheduled);
        var surviving = audio switch
        {
            CapabilityState.Available a => a.Detail,
            CapabilityState.Degraded d => d.SurvivingSemantics,
            // The audio half itself refused (no render session, dial off, generation cancelled).
            // That refusal is returned untouched: there is no surviving half to qualify, and
            // stacking a second reason on top of it would bury the one the user needs.
            _ => null,
        };

        if (surviving is null)
        {
            return audio;
        }

        return new CapabilityState.Degraded(
            $"the AUDIO half of Brain Drain is armed — {surviving}",
            new CapabilityReason(EffectReasonCodes.BrainDrainVisualHalfAbsent, VisualHalfNotice));
    }
}
