using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Audio;

/// <summary>
/// What the operating system says about this process's audio output, right now — the ONE thing a
/// backend cannot tell you about itself.
///
/// <para>Every field is read back from the platform, never remembered from a call this process
/// made. <see cref="Peak"/> is the strongest of them: it is the sample level the OS's own audio
/// engine metered on the stream this process owns, and it reads ZERO on an open device with nothing
/// cued — which is what stops a fact built on it from passing vacuously.</para>
/// </summary>
/// <param name="Asked">The read-back was attempted at all. False on a build with no read-back
/// mechanism, which is the Linux case and is never dressed up as a zero.</param>
/// <param name="EndpointName">The render endpoint the device is up on, as the backend's fresh
/// enumeration named it. Null before a device is open.</param>
/// <param name="SessionOwnedByThisProcess">The OS's session enumerator holds a session whose owning
/// process id is this process (<c>IAudioSessionControl2::GetProcessId</c>).</param>
/// <param name="SessionActive">That session's state is <c>AudioSessionStateActive</c>
/// (<c>IAudioSessionControl::GetState</c>, <c>audiosessiontypes.h:152</c>).</param>
/// <param name="MeterReadable">The per-session peak meter could be obtained
/// (<c>IAudioMeterInformation</c>). Separate from the value, because "no meter" and "a meter reading
/// zero" are different facts.</param>
/// <param name="Peak">The metered peak, 0..1.</param>
/// <param name="SessionsOnEndpoint">How many sessions the endpoint holds in total — the search's own
/// negative control, so a read-back that degenerated into "found nothing, ever" is visible.</param>
public sealed record AudioRenderObservation(
    bool Asked,
    string? EndpointName,
    bool SessionOwnedByThisProcess,
    bool SessionActive,
    bool MeterReadable,
    float Peak,
    int SessionsOnEndpoint)
{
    /// <summary>Nothing was asked, because this build has no way to ask.</summary>
    public static AudioRenderObservation NotAsked { get; } = new(false, null, false, false, false, 0f, 0);

    /// <summary>The OS confirms a live render session for this process. This — and only this — is
    /// what <see cref="CapabilityState.Available"/> is allowed to rest on.</summary>
    public bool Confirmed => Asked && SessionOwnedByThisProcess && SessionActive;
}

/// <summary>One clip to play. <paramref name="Slot"/> is the caller's own channel identity: a second
/// cue on the same slot REPLACES the first, which is what both upstream services do with their one
/// player field (<c>Services/LockCard/MindWipeService.cs:826-840</c>,
/// <c>Services/LockCard/BrainDrainService.cs:271-292</c>).</summary>
/// <param name="Slot">Stable channel id, e.g. the module's rack key.</param>
/// <param name="Path">A local audio file.</param>
/// <param name="Volume">Gain 0..1.</param>
public sealed record AudioCue(string Slot, string Path, float Volume);

/// <summary>
/// An audio presence: this process's ability to put sound on a real output device, and — the part
/// that makes it a capability rather than a wrapper — its ability to ASK THE OPERATING SYSTEM
/// whether that is really happening.
///
/// <para><b>The contract, and the reason this interface exists at all.</b> Every method returns a
/// typed <see cref="CapabilityState"/> (<c>runtime-capability-contract.md</c> §1), and
/// <see cref="CapabilityState.Available"/> is returned ONLY after the platform confirmed the effect
/// — the same discipline <see cref="Tray.ITrayPresence"/> holds with its <c>NIM_MODIFY</c> round
/// trip and <see cref="Overlay.IOverlayPresence"/> with its eight. <b>A backend must never return
/// Available because its own call did not throw.</b> That shape — the method returned, therefore it
/// worked — is the one this port has rejected four times, and for audio it is especially cheap to
/// get away with, because every failure mode is inaudible from inside the process.</para>
///
/// <para><b>What Available can and cannot mean here, stated on the interface rather than buried in
/// a record file.</b> It means: the OS reports an active render session owned by this process on a
/// named endpoint, and (for <see cref="Cue"/>) a player really reached that device's mixer. It does
/// NOT mean the endpoint was unmuted, that a DAC converted anything, that speakers are connected, or
/// that a human heard it. A virtual/RDP sink satisfies every check in this file with no physical
/// output anywhere — the port has already recorded that class on WSLg
/// (<c>client/docs/audio-backend-spike.md:24,88</c>). <b>"A person heard it" is a manual gate, never
/// a state this interface returns.</b></para>
///
/// <para><b>What a caller does with Unavailable.</b> Exactly what WPF does: stay quiet and do not
/// spin. Both upstream audio services open their tick with
/// <c>if (App.Audio?.IsOutputSuppressed == true) return;</c>
/// (<c>Services/LockCard/MindWipeService.cs:771</c>, <c>Services/LockCard/BrainDrainService.cs:211</c>)
/// — the comment there is "endpoint down — stay quiet, don't spin", and the spin it is avoiding is a
/// device re-open every tick forever (issues #778/#779). A caller must never respond to Unavailable
/// by carrying on and reporting itself as running: a module whose sound cannot reach anybody is not
/// running, and its dot must say so.</para>
///
/// <para><b>Thread affinity.</b> None required. The native backend beneath this has no UI-thread
/// affinity (WPF says so of NAudio in both services' stop paths, e.g.
/// <c>MindWipeService.cs:245-251</c>) and neither does this. Player construction is bounded and
/// always happens off any synchronization context (see
/// <see cref="OrphanSafePlayerFactory{TPlayer}"/>), so a caller on the UI thread cannot be wedged by
/// a slow device.</para>
/// </summary>
public interface IAudioPresence : IDisposable
{
    /// <summary>
    /// Bring an output device up, and then ASK THE OS whether it really is up.
    /// <see cref="CapabilityState.Available"/> means the operating system reports an active render
    /// session owned by this process — not that a device call returned. Idempotent: a second call on
    /// an open presence re-reads the OS and returns what it says now.
    /// </summary>
    CapabilityState Open();

    /// <summary>
    /// Play one clip on its slot, replacing whatever that slot was playing.
    /// <see cref="CapabilityState.Available"/> means a player really reached the device's mixer AND
    /// the OS still confirms the render session; if the player started but the read-back no longer
    /// confirms, the answer is <see cref="CapabilityState.Degraded"/> naming which half holds.
    /// </summary>
    CapabilityState Cue(AudioCue cue);

    /// <summary>
    /// Stop and drop whatever a slot is playing — WPF's <c>StopCurrentAudio</c>
    /// (<c>MindWipeService.cs:857-874</c>, <c>BrainDrainService.cs:316-334</c>), which both services
    /// call unconditionally on stop so a panic can always reach the audio. Silent on a slot that has
    /// nothing, which is not a success claim: it returns <see cref="CapabilityState.Unavailable"/>
    /// with <see cref="AudioReasonCodes.RenderDeviceNotOpen"/> when there was nothing to stop.
    /// </summary>
    CapabilityState Silence(string slot);

    /// <summary>
    /// Is this slot still SOUNDING — a player present AND actually playing?
    ///
    /// <para>Deliberately not "is the slot occupied". A slot holds its player until something
    /// displaces it or silences it; there is no natural-completion sweep, so an occupied slot is
    /// routinely one whose clip finished long ago. A caller that skips work while a slot is
    /// occupied would fall silent after its first clip, which is a worse defect than the one that
    /// question is usually being asked to fix.</para>
    ///
    /// <para>A backend with no player, no device, or no such slot answers false. This is a
    /// question, never a claim that anything is audible to a human.</para>
    /// </summary>
    bool IsSounding(string slot);

    /// <summary>
    /// Ask the OS what it believes is happening, RIGHT NOW. A live round trip — never throws, and a
    /// build with no read-back answers <see cref="AudioRenderObservation.NotAsked"/> rather than a
    /// shaped zero.
    ///
    /// <para>Deliberately separate from <see cref="LastObservation"/> because it is not free: it
    /// walks every session on the default endpoint. Callers that repaint (a panel, a dot) read the
    /// remembered one; callers that want the truth this instant (a diagnostic, a test) call this.</para>
    /// </summary>
    AudioRenderObservation Observe();

    /// <summary>The last state <see cref="Open"/> produced, or null before it was ever called. What
    /// a panel renders, so a user reads the OS's own answer rather than a sentence about a
    /// platform.</summary>
    CapabilityState? LastOpen { get; }

    /// <summary>
    /// The most recent read-back this presence actually took, refreshed on every <see cref="Open"/>
    /// and every <see cref="Cue"/>. <b>It is a remembered ANSWER, never a remembered REQUEST</b> —
    /// the distinction between "the OS told us this a moment ago" and "we called Open once", and the
    /// second of those is what this capability exists to refuse.
    /// </summary>
    AudioRenderObservation LastObservation { get; }

    /// <summary>
    /// True only while a device this presence opened is still open AND the last read-back confirmed
    /// it. Cheap by construction, so a dot repainted on every gesture does not walk the OS's session
    /// table — the cost of the freshness is one round trip per cue, and a cue is what makes it
    /// stale.
    /// </summary>
    bool IsRendering { get; }
}
