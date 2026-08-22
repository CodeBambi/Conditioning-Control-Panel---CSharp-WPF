namespace CcpClient.Desktop.Audio;

/// <summary>
/// Stable machine-readable reason codes for the audio-render capability
/// (runtime-capability-contract §1: "codes are additive; new codes land with their consumer row").
/// They live beside their consumer for the same reason <see cref="Tray.TrayReasonCodes"/> does.
///
/// <para><b>The distinction that carries the honesty here</b>, and it is a THREE-way one rather
/// than the tray's two. <see cref="RenderReadbackAbsent"/> says this build has no way to ASK the
/// platform whether sound is really going out, so nothing was claimed. <see cref="NoRenderEndpoint"/>
/// says the OS was asked and reports no output device at all. <see cref="RenderDeviceRefused"/> says
/// a device existed, was really opened, and the native layer refused. And
/// <see cref="RenderSessionUnconfirmed"/> is the one that makes this capability worth having: the
/// device call SUCCEEDED and the OS still does not report a render session for this process — which
/// is precisely the state a capability that trusted its own return value would report as working.</para>
/// </summary>
public static class AudioReasonCodes
{
    /// <summary>
    /// This build has no way to READ BACK from the platform that audio is really being rendered, so
    /// no <c>Available</c> can be earned here and nothing was attempted. Never means "this platform
    /// cannot play sound" — on Linux it emphatically can, and the port's own backend enumerates
    /// there (<c>client/docs/audio-backend-spike.md:13,39</c>); it means the OS-side proof is
    /// WASAPI-only in this build, and the detail names the route and the manual gate that would
    /// settle it.
    /// </summary>
    public const string RenderReadbackAbsent = "audio-render-readback-absent";

    /// <summary>
    /// The OS was asked and reports no active render endpoint. WPF's own equivalent state — audio
    /// suppressed because there is nowhere to play (<c>Services/AudioService.cs:163-166</c>, issue
    /// #779) — and, like WPF's, it is not permanent: a later open re-enumerates.
    /// </summary>
    public const string NoRenderEndpoint = "audio-no-render-endpoint";

    /// <summary>
    /// An endpoint existed and the native device open/start was really attempted and failed. The
    /// detail carries the backend's own error text. Distinct from <see cref="NoRenderEndpoint"/>
    /// because something was attempted, and from <see cref="RenderSessionUnconfirmed"/> because the
    /// native layer itself said no.
    /// </summary>
    public const string RenderDeviceRefused = "audio-render-device-refused";

    /// <summary>
    /// <b>The code this whole capability exists for.</b> The device call returned success and the
    /// operating system does NOT report a render session owned by this process on the endpoint. A
    /// capability that took its own return value as proof would be claiming <c>Available</c> right
    /// here. The detail names what was asked (<c>IAudioSessionControl2::GetProcessId</c> /
    /// <c>IAudioSessionControl::GetState</c>) and what came back.
    /// </summary>
    public const string RenderSessionUnconfirmed = "audio-render-session-unconfirmed";

    /// <summary>A cue was asked for before the device was opened, or after it was closed. Nothing was
    /// attempted, and reporting success would be a claim for work that did not happen.</summary>
    public const string RenderDeviceNotOpen = "audio-render-device-not-open";

    /// <summary>
    /// The cue's file could not be turned into a player: it is missing, undecodable, or the bounded
    /// construction was abandoned (<see cref="PlayerConstructionTimeoutException"/>).
    /// The detail carries the failure verbatim.
    /// </summary>
    public const string CuePlaybackFailed = "audio-cue-playback-failed";

    /// <summary>The presence was disposed; its device is gone and it will never cue again. Truthful
    /// terminal state, never a silent no-op.</summary>
    public const string RenderPresenceDisposed = "audio-render-presence-disposed";
}
