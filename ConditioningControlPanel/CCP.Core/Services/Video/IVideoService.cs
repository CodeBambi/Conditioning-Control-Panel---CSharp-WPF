namespace ConditioningControlPanel.Core.Services.Video;

/// <summary>
/// Cross-platform seam for the mandatory-video effect engine.
/// The WPF implementation schedules and plays full-screen attention-check videos;
/// the Avalonia head begins with a no-op stub so the feature control can toggle
/// live state without the full engine port blocking the UI.
/// </summary>
public interface IVideoService
{
    /// <summary>Whether the mandatory video scheduler is currently running.</summary>
    bool IsRunning { get; }

    /// <summary>Whether a video window is currently open and playing.</summary>
    bool IsPlaying { get; }

    /// <summary>
    /// Whether a STRICT mandatory video is currently playing (attention-locked: ESC/panic
    /// must not dismiss it). Mirrors WPF <c>VideoService.IsStrictActive =&gt; _videoPlaying
    /// &amp;&amp; _strictActive</c> (VideoService.cs:182). Default-implemented as <c>false</c> so
    /// existing implementations and test fakes keep compiling; only the real head overrides
    /// it. Used by the avatar quick-menu engine-stop guard (#479) to refuse stopping the
    /// engine mid strict-locked video.
    /// </summary>
    bool IsStrictActive => false;

    /// <summary>
    /// Whether the service is in the middle of tearing down active video windows.
    /// Default-implemented as <c>false</c> so existing implementations and test fakes keep
    /// compiling; only heads with a real teardown phase override it. Used to defer the
    /// post-session summary until a dying fullscreen video surface has cleared (#462).
    /// </summary>
    bool IsCleaningUp => false;

    /// <summary>
    /// Whether any video window (primary or secondary) is currently open, including a window
    /// that is open-but-not-yet-playing or mid-close. Defaults to <see cref="IsPlaying"/> so
    /// existing implementations keep compiling; native heads override with real window state
    /// so the post-session summary defer can wait open windows out (#462).
    /// </summary>
    bool HasOpenVideoWindows => IsPlaying;

    /// <summary>Starts the mandatory video scheduler.</summary>
    void Start();

    /// <summary>Stops the scheduler and closes active video windows.</summary>
    void Stop();

    /// <summary>Refreshes the video search path after asset/mod changes.</summary>
    void RefreshVideosPath();

    /// <summary>Immediately plays the specified video file in strict mode.</summary>
    void PlaySpecificVideo(string videoPath, bool strictMode);

    /// <summary>Immediately plays a randomly-selected video from the configured search paths.</summary>
    void PlayRandomVideo();

    /// <summary>Immediately plays a video from a URL.</summary>
    void PlayUrl(string url);

    /// <summary>Immediately triggers a random video, with optional stuck-state force cleanup.</summary>
    void TriggerVideo();

    /// <summary>
    /// Chaos: arm the NEXT video to start at a random position with at least
    /// <paramref name="segmentSec"/> seconds left to play, so a chaos-capped video reads as a
    /// random slice (WPF VideoService.ArmRandomSegment; EffectPayload.cs:154-166). Default no-op
    /// so heads and test fakes without segment support keep compiling; only the segment-aware
    /// head (AvaloniaVideoService) implements it.
    /// </summary>
    void ArmRandomSegment(double segmentSec) { }

    /// <summary>Forcibly cleans up any stuck video windows and resets the interaction queue.</summary>
    void ForceCleanup();

    /// <summary>Re-applies current master/video volume and preferred output device to active playback.</summary>
    void UpdateVolume();

    /// <summary>
    /// The file path of the video most recently started by the scheduler.
    /// Used by the session log to record media played during a session.
    /// </summary>
    string? LastVideoPath { get; }

    /// <summary>Raised when a video is about to start playing.</summary>
    event EventHandler? VideoAboutToStart;

    /// <summary>Raised when a video has started playing.</summary>
    event EventHandler? VideoStarted;

    /// <summary>Raised when a video has finished playing.</summary>
    event EventHandler? VideoEnded;

    /// <summary>
    /// Pauses the primary (audio-bearing ambient) video in place without teardown, preserving playback
    /// position, for the DTRH world-freeze mechanic. Mirrors WPF <c>VideoService.PausePrimary</c>
    /// (VideoService.cs:164), which pauses only the audio-bearing player and never the mandatory/strict
    /// window. Default no-op for heads without pausable primary playback.
    /// </summary>
    void PausePrimary() { }

    /// <summary>
    /// Resumes primary video previously paused by <see cref="PausePrimary"/>. Mirrors WPF
    /// <c>VideoService.PlayPrimary</c> (VideoService.cs:171). Default no-op.
    /// </summary>
    void PlayPrimary() { }
}
