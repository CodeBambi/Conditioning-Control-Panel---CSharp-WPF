using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Audio;

/// <summary>
/// The honest refusal. Used where this build cannot EARN an audio claim.
///
/// <para><b>What makes it honest rather than a stub, and why the distinction is sharper for audio
/// than for anything else the port has refused.</b> A stub swallows the call and returns success.
/// For a tray icon or an overlay, a stub is at least visibly wrong — nothing appears. For audio,
/// a stub is <b>indistinguishable from working</b> to every automated check that exists: the method
/// returns, no exception is thrown, and silence looks exactly like a clip the user's speakers are
/// too quiet to render. That is why this class refuses on EVERY path —
/// <see cref="Open"/>, <see cref="Cue"/> and <see cref="Silence"/> — never reports
/// <see cref="IsRendering"/> true, and answers <see cref="Observe"/> with
/// <see cref="AudioRenderObservation.NotAsked"/> rather than a zeroed observation. "We did not ask"
/// and "we asked and the level was zero" are different facts, and collapsing them into one is how
/// an audio capability lies.</para>
///
/// <para><b>Not a no-op.</b> A caller that sees Unavailable must stay quiet and stop scheduling
/// work, which is exactly what WPF does with its own suppressed-output flag
/// (<c>Services/LockCard/MindWipeService.cs:771</c>, <c>Services/LockCard/BrainDrainService.cs:211</c>
/// — "endpoint down — stay quiet, don't spin"), and it must report its module as NOT running. A
/// module that kept its dot lit over a presence that refuses would be claiming a sound nobody can
/// hear, which is the precise failure this whole capability exists to make impossible.</para>
/// </summary>
public sealed class UnsupportedAudioPresence : IAudioPresence
{
    private readonly CapabilityReason _reason;

    public UnsupportedAudioPresence(string code, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        _reason = new CapabilityReason(code, detail);
    }

    /// <summary>The refusal this presence was constructed with, verbatim.</summary>
    public CapabilityReason Reason => _reason;

    /// <inheritdoc/>
    public bool IsRendering => false;

    /// <inheritdoc/>
    public CapabilityState? LastOpen { get; private set; }

    /// <summary>Never anything but <see cref="AudioRenderObservation.NotAsked"/>. A zeroed
    /// observation would read like a measurement that came back empty; this build took no
    /// measurement at all.</summary>
    public AudioRenderObservation LastObservation => AudioRenderObservation.NotAsked;

    /// <inheritdoc/>
    public CapabilityState Open() => LastOpen = new CapabilityState.Unavailable(_reason);

    /// <inheritdoc/>
    public CapabilityState Cue(AudioCue cue)
    {
        ArgumentNullException.ThrowIfNull(cue);
        return new CapabilityState.Unavailable(_reason);
    }

    /// <summary>
    /// Refuses, and the refusal is not pedantry: reporting <c>Available</c> from a stop that stopped
    /// nothing would let a caller's teardown pin read green on a build that never played anything.
    /// </summary>
    /// <summary>Nothing was ever started here, so nothing is sounding.</summary>
    public bool IsSounding(string slot) => false;

    public CapabilityState Silence(string slot)
    {
        ArgumentException.ThrowIfNullOrEmpty(slot);
        return new CapabilityState.Unavailable(_reason);
    }

    /// <summary>Nothing was asked, and the record says so in its own field rather than by returning
    /// zeros that read like a measurement.</summary>
    public AudioRenderObservation Observe() => AudioRenderObservation.NotAsked;

    public void Dispose()
    {
        // Nothing was ever acquired. Deliberately empty, and deliberately not reporting success
        // anywhere else in this class.
    }
}
