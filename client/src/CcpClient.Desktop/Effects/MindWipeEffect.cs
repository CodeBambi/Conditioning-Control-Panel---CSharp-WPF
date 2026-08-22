using CcpClient.Desktop.Audio;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// <b>Mind Wipe</b> — WPF's IMMERSION rack row (<c>Views/Tabs/StudioTabView.xaml.cs:509-510</c>),
/// started by <c>StartEngine</c> at <c>MainWindow/MainWindow.StartStop.cs:229-230</c> and stopped by
/// <c>StopEngineCore</c> at <c>:341</c>.
///
/// <para><b>Why this module exists in the port, and the refusal it settles.</b> An earlier wave
/// chose Flash Images over this one deliberately, and said why in the owner digest: Mind Wipe is pure
/// audio and "would have looked completely finished. It was rejected on purpose, because nothing in
/// this project can actually verify that a sound played". That was true then. It is no longer true:
/// <see cref="IAudioPresence"/> earns its <c>Available</c> from the operating system's own render
/// session and its own peak meter. Audible output still needs a human check. <b>This module is the first thing in
/// the port whose output nobody in the process can see, and it is the first whose dot depends on
/// asking something outside it.</b></para>
///
/// <para><b>Where it differs from the four paced/continuous modules already landed:</b></para>
/// <list type="number">
/// <item>It is <b>rolled, not paced</b> — a fixed 10-second window with a probability per window
/// (<see cref="MindWipeSchedule"/>), where the other paced pair compute an interval from their
/// dial.</item>
/// <item>Its <c>Live</c> has a second clause that is not about this process at all
/// (<see cref="AudioCueEffect"/>).</item>
/// <item>Its "surface" refuses in a medium that looks identical to working: silence and a muted
/// speaker are the same observation, which is why the panel reports the capability's typed outcome
/// verbatim instead of a sentence.</item>
/// </list>
///
/// <para><b>What is NOT ported</b>, and is not pretended: loop mode with its A/B crossfade engine
/// (<c>Services/LockCard/MindWipeService.cs:293-380</c>, started from
/// <c>MainWindow.StartStop.cs:232-237</c>), the "Clean Slate" achievement at 60 s of looping
/// (<c>:355-365</c>), session mode's escalating frequency (<c>:213-235</c>, which belongs to the
/// scripted preset session the port does not have — D99), the custom-clip override
/// (<c>:139-146</c>, no picker on any ported panel), <c>TriggerOnce</c>'s test button
/// (<c>:879-895</c>), the Discord presence call (<c>:206</c>) and the level-75 unlock
/// (<c>:16</c> — the port has no level system). Each is recorded as a divergence rather than
/// stubbed.</para>
/// </summary>
public sealed class MindWipeEffect : AudioCueEffect
{
    /// <summary>WPF's rack key for this module (<c>StudioTabView.xaml.cs:509</c>).</summary>
    public const string EffectId = "mindwipe";

    /// <summary>The row's label as the shipping app shows it, minus the emoji the port's rack does
    /// not render (<c>StudioTabView.xaml.cs:509</c>, <c>en.json:1432</c> — "🧠 Mind Wipe").</summary>
    public const string DisplayTitle = "Mind Wipe";

    /// <summary>Upstream's folder name under the sounds root (<c>MindWipeService.cs:149</c>).</summary>
    public const string ClipFolderName = "mindwipe";

    private readonly PersistenceStore<MindWipePresetDocument> _preset;

    public MindWipeEffect(
        AsyncOperationOwner owner,
        EffectSignal signal,
        ISessionClock clock,
        IAudioCuePool pool,
        IAudioPresence presence,
        PersistenceStore<MindWipePresetDocument> preset,
        Random? random = null)
        : base(owner, signal, clock, "mindwipe-schedule", pool, presence, random)
    {
        ArgumentNullException.ThrowIfNull(preset);
        _preset = preset;
    }

    /// <inheritdoc/>
    public override string Id => EffectId;

    /// <inheritdoc/>
    public override string Title => DisplayTitle;

    /// <inheritdoc/>
    public override bool Enabled => _preset.Current.Enabled;

    /// <summary>This module's persisted dials (public so the panel reads the real document).</summary>
    public MindWipePresetDocument Preset => _preset.Current;

    /// <inheritdoc/>
    protected override TimeSpan Window => MindWipeSchedule.Window;

    /// <inheritdoc/>
    protected override double TriggerProbability =>
        MindWipeSchedule.TriggerProbability(_preset.Current.PerHour);

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

    /// <summary>The frequency dial. Writes and re-evaluates, the port's convention for every module's
    /// dial — and it matters here because the dial changes the ODDS of the next window,
    /// so a user who turns it up expects the next ten seconds to be more likely, not the one after
    /// the current schedule expires.</summary>
    public void SetPerHour(int perHour)
    {
        if (_preset.Current.PerHour == Math.Clamp(perHour, MindWipeSchedule.MinPerHour, MindWipeSchedule.MaxPerHour))
        {
            return;
        }

        _preset.Mutate(p => p.PerHour = perHour);
        Refresh();
    }

    /// <summary>
    /// The volume dial. WPF applies a moved slider to the CURRENTLY PLAYING clip as well as to the
    /// next one (<c>MindWipeService.cs:102-122</c> sets <c>_audioReader.Volume</c> live). The port
    /// does not: <see cref="IAudioPresence"/> has no live-gain seam, so a moved slider takes effect
    /// on the next cue. Recorded as a divergence — a clip is a second or two long, so the window in
    /// which the two differ is that clip.
    /// </summary>
    public void SetVolumePercent(int percent)
    {
        var clamped = Math.Clamp(
            percent, MindWipePresetDocument.MinVolumePercent, MindWipePresetDocument.MaxVolumePercent);
        if (_preset.Current.VolumePercent == clamped)
        {
            return;
        }

        _preset.Mutate(p => p.VolumePercent = percent);
        RaiseChanged();
    }
}
