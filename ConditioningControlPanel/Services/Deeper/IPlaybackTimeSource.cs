using System;
using System.Windows;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Generic time source the EnhancementEngine consumes. Hides the playback
    /// pipeline (VideoService fullscreen, BrowserService WebView2 video, future
    /// EnhancementAudioPlayer) behind a small uniform surface.
    ///
    /// Time is always in seconds (double) for consistency with the on-disk
    /// schema (regions, haptic events, time_reached). Implementations should
    /// fire PlaybackTimeChanged on the UI thread.
    /// </summary>
    public interface IPlaybackTimeSource
    {
        /// <summary>
        /// Fires when the underlying media's playback position advances.
        /// Argument is current time in seconds. Should fire at ~10 Hz to keep
        /// timeline-event resolution comfortably tight; the engine debounces
        /// internally so over-firing is safe.
        /// </summary>
        event Action<double>? PlaybackTimeChanged;

        /// <summary>Current playback position in seconds, or 0 if not playing.</summary>
        double GetCurrentTimeSeconds();

        /// <summary>Total media duration in seconds, or 0 if unknown.</summary>
        double GetDurationSeconds();

        bool IsPlaying { get; }

        void Seek(double seconds);
        void Pause();
        void Play();

        /// <summary>
        /// Screen-space rect of the rendered video frame, used to normalize
        /// gaze-target rules' [x, y, w, h] (0..1) into screen DIPs. Returns
        /// Rect.Empty for audio-only sources or when no window is visible —
        /// the engine then skips gaze evaluation silently.
        /// </summary>
        Rect GetVideoRect();
    }
}
