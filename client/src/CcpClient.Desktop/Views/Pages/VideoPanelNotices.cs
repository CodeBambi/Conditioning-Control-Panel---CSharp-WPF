using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Video;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// The Mandatory Video panel's sentences, as functions of state rather than as strings the page
/// pastes in — the same shape and the same reason as <see cref="AudioPanelNotices"/> and
/// <see cref="InputPanelNotices"/>.
///
/// <para><b>What the user reads about the picture is the CAPABILITY's own typed outcome, quoted.</b>
/// Never a sentence composed here about a platform: the whole point of the video capability is that
/// the operating system answers, and a panel that paraphrased the answer would be one edit away from
/// paraphrasing it wrongly.</para>
/// </summary>
public static class VideoPanelNotices
{
    /// <summary>
    /// The module's own live line.
    /// </summary>
    /// <param name="dot">The dot the row is showing.</param>
    /// <param name="playedCount">How many clips have really started.</param>
    /// <param name="last">The most recent firing, or null.</param>
    /// <param name="sessionRunning">Whether a session is running at all.</param>
    /// <param name="canReachADisplay">The OS's necessary condition.</param>
    /// <param name="playing">Whether a clip is on screen right now.</param>
    /// <param name="framesDecoded">How many pictures the decoder produced for it.</param>
    /// <param name="framesHeld">How many of them the OS was confirmed to be holding.</param>
    /// <param name="framesAdvanced">Of those, how many changed the OS's copy of the surface.</param>
    public static string DescribeVideoState(
        EffectDotState dot,
        int playedCount,
        MandatoryVideoEvent? last,
        bool sessionRunning,
        bool canReachADisplay,
        bool playing,
        int framesDecoded,
        int framesHeld,
        int framesAdvanced)
    {
        if (!sessionRunning)
        {
            return canReachADisplay
                ? "Nothing plays until the session starts. Press START on the Studio page."
                : "The operating system reports nowhere to put a picture for this process, so nothing will "
                    + "play even after the session starts. The line below is its own answer.";
        }

        if (!canReachADisplay)
        {
            return "The session is running and the operating system reports nowhere to put a picture, so no "
                + "clip is shown. The line below is its own answer.";
        }

        if (playing)
        {
            // The two counts side by side, ALWAYS, because they are the whole point: a decoder that
            // returns bytes into a dead surface shows a climbing left-hand number and a flat
            // right-hand one.
            var motion = dot == EffectDotState.Live
                ? "the picture is moving"
                : "the picture has STOPPED changing — the decoder is running and the operating system's copy "
                    + "of the surface is not";
            return $"A clip is on screen and {motion}. {framesDecoded} pictures decoded, {framesHeld} of them "
                + $"the operating system confirmed it was holding, {framesAdvanced} of those changed what it "
                + "holds.";
        }

        return last is { } fired
            ? $"The schedule is running; {playedCount} clip(s) so far, the last at "
                + $"{fired.At.ToLocalTime():HH:mm:ss}. Nothing is on screen right now."
            : "The schedule is running and no clip has played yet.";
    }

    /// <summary>The clip pool's line. Names the folder, because "where do I put them" must have an
    /// answer on the panel rather than in a document.</summary>
    public static string DescribeClipPool(int count, string folder) =>
        count > 0
            ? $"{count} clip(s) in {folder} (subfolders included)."
            : $"No clips in {folder}. Drop an .mp4/.mov/.avi/.wmv/.mkv/.webm in there — or in a subfolder — "
                + "and the next firing picks it up, without restarting the session.";

    /// <summary>
    /// The capability's own answer, quoted: what the operating system said about the last picture,
    /// and what its last read-back of the surface reported.
    /// </summary>
    public static string DescribeVideoCapability(CapabilityState? lastPlacement, VideoSurfaceObservation observation)
    {
        var placement = lastPlacement switch
        {
            null => "The video surface has not been asked for anything yet.",
            CapabilityState.Available available => $"The operating system said: {available.Detail}",
            CapabilityState.Degraded degraded =>
                $"The operating system said: {degraded.SurvivingSemantics} ({degraded.Reason.Code}: {degraded.Reason.Detail})",
            CapabilityState.Unavailable unavailable =>
                $"The operating system refused: {unavailable.Reason.Code} — {unavailable.Reason.Detail}",
            CapabilityState.DependencyMissing missing =>
                $"A dependency is missing: {missing.Reason.Code} — {missing.Reason.Detail}",
            CapabilityState.PermissionRequired permission =>
                $"Permission is required: {permission.Reason.Code} — {permission.Reason.Detail}",
            CapabilityState.Faulted faulted =>
                $"The video surface faulted: {faulted.Reason.Code} — {faulted.Reason.Detail}",
            _ => "The video surface returned a state this panel does not know how to describe.",
        };

        if (!observation.Asked)
        {
            return placement + " No read-back has been taken on this build — that is 'nobody asked', which is "
                + "not the same as 'the answer was no'.";
        }

        return placement
            + $" Last read-back: on screen={observation.WindowVisible}, above every ordinary window="
            + $"{observation.AboveEveryOrdinaryWindow}, the window manager routes its own centre to it="
            + $"{observation.HitTestRoutesHere}, the bar it was filled with is held={observation.LetterboxHeld}, "
            + $"{observation.PictureMatched} of {observation.PictureSampled} sampled picture points carry the "
            + "decoded frame.";
    }
}
