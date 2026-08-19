namespace CcpClient.Desktop.Video;

/// <summary>
/// Stable machine-readable reasons a video surface or a clip refused
/// (<c>runtime-capability-contract.md</c> §1). Additive; every code lands with the consumer that
/// reads it.
///
/// <para><b>Why the decode refusals and the surface refusals are kept apart.</b> "This file is not
/// playable" and "the frames are not reaching a surface" are the two halves of this port's oldest
/// failure shape, and collapsing them into one code would hide exactly the distinction SP-111 was
/// created to hold open: a decoder returning bytes proves a FILE is readable and nothing more.
/// A caller that cannot tell a bad file from a dead surface reports the wrong thing to the user and
/// the wrong thing to a bug report.</para>
/// </summary>
public static class VideoReasonCodes
{
    /// <summary>No mechanism on this platform: nothing was attempted. The Linux/macOS branch, and
    /// the shape <c>ISecretStore</c>/<c>ITrayPresence</c>/<c>IOverlayPresence</c>/
    /// <c>IAudioPresence</c>/<c>IInputPresence</c> all use for the same situation.</summary>
    public const string VideoMechanismAbsent = "video-mechanism-absent";

    /// <summary>The OS's media stack would not start at all (<c>MFStartup</c> failed), so nothing on
    /// this machine can be decoded and no clip was opened.</summary>
    public const string VideoMediaStackUnavailable = "video-media-stack-unavailable";

    /// <summary>
    /// The OS opened nothing for this path: no source reader, no video stream, or a stream whose
    /// frames it will not convert to the surface's pixel format. <b>A file refusal, never a surface
    /// refusal</b> — the next clip in the pool may play perfectly.
    /// </summary>
    public const string VideoClipUnreadable = "video-clip-unreadable";

    /// <summary>The clip opened and the OS reported a degenerate frame size (zero width or height),
    /// so there is nothing to present even though the container parsed.</summary>
    public const string VideoClipHasNoPicture = "video-clip-has-no-picture";

    /// <summary>The surface window could not be created at all (class registration or
    /// <c>CreateWindowEx</c> failed).</summary>
    public const string VideoWindowCreationFailed = "video-window-creation-failed";

    /// <summary>This process has no display to put a video surface on — the OS's own monitor count
    /// is zero, or its window station is not interactive. Read from the OS, never inferred from a
    /// platform check.</summary>
    public const string VideoNoDisplay = "video-no-display";

    /// <summary>The OS placed the surface but does not report it visible, at the requested
    /// rectangle, or above every ordinary window.</summary>
    public const string VideoSurfaceNotOnScreen = "video-surface-not-on-screen";

    /// <summary>
    /// The surface is on screen and the window manager routes a point inside it somewhere ELSE:
    /// another window is covering it there. Measured on this machine while SP-111 was being written
    /// — the shipping WPF product's own topmost window owned the point (plan.md §0, Q4).
    /// </summary>
    public const string VideoSurfaceOccluded = "video-surface-occluded";

    /// <summary>
    /// <b>The refusal this capability exists for.</b> The frame was handed to the OS and the OS's own
    /// copy of the surface does not hold it: the painted letterbox is not there, or the picture band
    /// does not carry the decoded frame's pixels. This is the difference between "frames decoded" and
    /// "frames on a surface", and it is the only thing separating this capability from a decoder that
    /// returns bytes.
    /// </summary>
    public const string VideoFrameNotHeld = "video-frame-not-held";

    /// <summary>Asked to present a frame, advance, or withdraw a presence with no clip open and
    /// nothing on screen.</summary>
    public const string VideoNothingPresented = "video-nothing-presented";

    /// <summary>
    /// A clip is already playing on this surface, so a second one was not opened (SP-112).
    ///
    /// <para><b>Added by the video capability's SECOND consumer, for a collision the FIRST one
    /// already tried to prevent and could not.</b> <c>MandatoryVideoEffect.Compose</c>'s first guard
    /// is "a clip is already on screen — drop the firing" and it runs on the CLOCK thread while the
    /// playback starts on the SURFACE thread, so it cannot close the window between the two. Before
    /// this code existed, a second start in that window silently overwrote the first clip's handle
    /// and its end-callback — leaking an open decoder and leaving the first module believing it was
    /// still playing. Upstream refuses the same collision at the same granularity: its interaction
    /// queue drops or defers a video-class interaction that arrives while another is live
    /// (<c>Services/BubbleCountService.cs:169-186</c>).</para>
    /// </summary>
    public const string VideoAlreadyPlaying = "video-already-playing";

    /// <summary>The presence was disposed; its window is gone and it will never present again.</summary>
    public const string VideoPresenceDisposed = "video-presence-disposed";
}
