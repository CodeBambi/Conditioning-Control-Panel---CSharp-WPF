namespace CcpClient.Desktop.Video;

/// <summary>The host platform a video backend is selected for.</summary>
public enum VideoHostPlatform
{
    /// <summary>Not one of the platforms this build knows about.</summary>
    Unknown,

    Windows,

    Linux,

    MacOs,
}

/// <summary>
/// Chooses the video presence and the decoder for a platform.
///
/// <para><b>Selection is not availability.</b> <c>runtime-capability-contract.md</c> §2 rule 2 bans
/// a platform check from producing <c>Available</c>, and nothing here produces it — only
/// <see cref="Win32VideoPresence.Show"/> does, and only after the operating system's own copy of the
/// surface was read back and found to carry the decoded picture. A platform this build cannot PROVE
/// gets an <see cref="UnsupportedVideoPresence"/> whose reason names the route and the gate that
/// would settle it.</para>
/// </summary>
public static class VideoPresenceFactory
{
    /// <summary>
    /// The exact manual gate a Linux box must run before any Linux video claim is made. A constant,
    /// so it travels with the refusal to whoever hits it rather than living only in a record file
    /// nobody reads at 2am — the same reason <c>Tray.TrayPresenceFactory.LinuxManualGate</c>,
    /// <c>Overlay</c>'s, <c>Audio.AudioPresenceFactory.LinuxManualGate</c> and
    /// <c>Input.InputPresenceFactory.LinuxManualGate</c> are constants.
    ///
    /// <para><b>And the reason this gate has SIX steps rather than the audio gate's four.</b> Video
    /// needs two independent things proven, not one. The DECODE half has no Media Foundation on
    /// Linux at all, so a port there needs a different decoder entirely (GStreamer or FFmpeg), and
    /// which formats a given distribution can open is a packaging question rather than a code
    /// question — a machine with no <c>gstreamer1.0-libav</c> plays nothing and nothing in the
    /// source says so. The DISPLAY half is harder still: the fact this capability rests on is a
    /// READ-BACK of the operating system's own copy of the surface, and under Wayland a client
    /// cannot read back its own composited window — the compositor owns it and the protocol offers
    /// no equivalent of <c>GetDC(hwnd)</c>. <b>So on Wayland the honest expectation is that the
    /// PROOF is unavailable even where the picture works</b>, which is a different answer from "it
    /// does not work" and must be recorded as one.</para>
    /// </summary>
    public const string LinuxManualGate =
        "MANUAL GATE (Linux, undischarged): on a real Linux desktop session — NOT WSLg — prove the video chain "
        + "six ways, on BOTH an X11 and a Wayland session, because they answer differently: "
        + "(1) a decoder exists and opens the port's own fixture clip AND a real clip from the user's library "
        + "(`gst-launch-1.0 filesrc location=<clip> ! decodebin ! fakesink -v` prints a caps line with a real "
        + "width/height, or `ffprobe -show_streams <clip>` names a video stream) — and record WHICH packages "
        + "were installed, because a distribution without gstreamer1.0-libav opens almost nothing and the source "
        + "cannot tell you that; "
        + "(2) the surface is on screen above every ordinary window (X11: `xprop -root _NET_CLIENT_LIST_STACKING`; "
        + "Wayland: the compositor's own layer dump, e.g. `swaymsg -t get_tree`); "
        + "(3) a pointer hit test at a point inside it resolves to it and NOT to another window — the occlusion "
        + "question, which is the one that BIT on Windows while this capability was being written; "
        + "(4) the operating system's own copy of THAT surface can be read back and carries the decoded picture "
        + "(X11: `xwd -id <window>` or an XGetImage on the window, compared against the decoder's own frame) — "
        + "THIS is the step that earns Available, and on Wayland there is no protocol for it: a client may not "
        + "read its own composited output, so the expected outcome there is a NAMED refusal even if the picture "
        + "is plainly visible to a human; "
        + "(5) the read-back CHANGES between two decoded frames that differ — a still picture and a playing film "
        + "are the same window, and the difference between them is the entire content of this capability; "
        + "(6) a HUMAN watches the clip and confirms it played, which no automated step on ANY platform "
        + "discharges, Windows included. "
        + "Until steps 1-5 are run, a caller must not present anything and must report its module as NOT running "
        + "— never open a window anyway and call that success.";

    /// <summary>Builds the presence for the platform this process is on.</summary>
    public static IVideoPresence Create() => CreateFor(CurrentPlatform());

    /// <summary>Builds the decoder for the platform this process is on.</summary>
    public static IVideoClipSource CreateClipSource() => CreateClipSourceFor(CurrentPlatform());

    /// <summary>
    /// The presence for a named platform. Both branches are reachable from either OS, so the refusal
    /// path is testable on a Windows box and the Windows path is testable nowhere else — the honest
    /// asymmetry, stated rather than hidden (the <c>TrayPresenceFactory</c> precedent).
    /// </summary>
    public static IVideoPresence CreateFor(VideoHostPlatform platform)
    {
        // The Windows branch is an if rather than a switch arm because the backend is annotated
        // [SupportedOSPlatform("windows")] — it holds COM RCWs and releases them — and this is the
        // guard the platform-compatibility analyser reads. It is also the honest answer to being
        // ASKED for the Windows backend from a process that is not on Windows, which is a thing the
        // suite does on purpose so the refusal path is testable from either OS.
        if (platform == VideoHostPlatform.Windows)
        {
            return OperatingSystem.IsWindows()
                ? new Win32VideoPresence(CreateClipSourceFor(platform))
                : new UnsupportedVideoPresence(
                    VideoReasonCodes.VideoMechanismAbsent,
                    "the Windows video backend was asked for from a process that is not on Windows; its surface "
                    + "and its read-back are Win32 and Media Foundation, and nothing was attempted");
        }

        return platform switch
        {
            // Linux CAN put a window on screen and CAN decode video; that is not what is missing.
            // What is missing is the ability to EARN the claim. Available here means the operating
            // system's own copy of the surface was read back and found to carry the decoded picture,
            // and on Linux that read is display-server-specific and unimplemented — XGetImage on
            // X11, and under Wayland a protocol that deliberately does not let a client read its own
            // composited output at all. Showing a window anyway and reporting success would be
            // exactly the stub this capability exists to make impossible, and for video it is the
            // stub with the longest history in this repository: a decoder returning bytes has been
            // mistaken for a picture on a screen four times under other names.
            VideoHostPlatform.Linux => new UnsupportedVideoPresence(
                VideoReasonCodes.VideoMechanismAbsent,
                "no Linux video read-back is implemented or verified in this build, so no picture claim can be "
                + "earned here and no surface is shown. What is absent is the PROOF, not the window: on Windows "
                + "the proof is the OS's own copy of the surface, read back through the window's device context "
                + "and compared pixel for pixel against the decoded frame over a bar that must return exactly "
                + "the colour it was filled with. The Linux analogues are per-display-server (XGetImage under "
                + "X11; under Wayland a client cannot read its own composited window at all). Until the gate "
                + "below runs, a caller must report its module as NOT running. " + LinuxManualGate),

            VideoHostPlatform.MacOs => new UnsupportedVideoPresence(
                VideoReasonCodes.VideoMechanismAbsent,
                "macOS is outside this port's supported Windows/Linux target set (architecture.md), so no "
                + "AVFoundation surface or read-back exists here and nothing was attempted"),

            _ => new UnsupportedVideoPresence(
                VideoReasonCodes.VideoMechanismAbsent,
                "this platform is not one this build has a video read-back for, and nothing was attempted"),
        };
    }

    /// <summary>The decoder for a named platform.</summary>
    public static IVideoClipSource CreateClipSourceFor(VideoHostPlatform platform)
    {
        if (platform == VideoHostPlatform.Windows)
        {
            return OperatingSystem.IsWindows()
                ? new MediaFoundationClipSource()
                : new UnsupportedVideoClipSource(
                    VideoReasonCodes.VideoMechanismAbsent,
                    "the Windows decoder was asked for from a process that is not on Windows; Media Foundation "
                    + "is a Windows mechanism and nothing was attempted");
        }

        return platform switch
        {
            VideoHostPlatform.Linux => new UnsupportedVideoClipSource(
                VideoReasonCodes.VideoMechanismAbsent,
                "this build has no Linux decoder: Media Foundation is a Windows mechanism and no GStreamer or "
                + "FFmpeg route is implemented here, so no file is opened and no frame is produced. "
                + LinuxManualGate),

            VideoHostPlatform.MacOs => new UnsupportedVideoClipSource(
                VideoReasonCodes.VideoMechanismAbsent,
                "macOS is outside this port's supported Windows/Linux target set (architecture.md); no "
                + "AVFoundation decoder exists here and nothing was attempted"),

            _ => new UnsupportedVideoClipSource(
                VideoReasonCodes.VideoMechanismAbsent,
                "this platform is not one this build has a decoder for, and nothing was attempted"),
        };
    }

    /// <summary>The platform this process is on, as a selection input only.</summary>
    public static VideoHostPlatform CurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return VideoHostPlatform.Windows;
        }

        if (OperatingSystem.IsLinux())
        {
            return VideoHostPlatform.Linux;
        }

        if (OperatingSystem.IsMacOS())
        {
            return VideoHostPlatform.MacOs;
        }

        return VideoHostPlatform.Unknown;
    }
}
