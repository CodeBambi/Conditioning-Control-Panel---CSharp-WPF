using System;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// "A web video owns the screen right now": the one fact WPF's BrowserMediaService.IsPlaying /
    /// PlayingChanged gives its listeners (the Arcademy freezes a class under it when
    /// ProtectBrowserVideoPlayback is on). This head has no browser media service; whatever starts or
    /// ends a web-video takeover calls <see cref="Set"/>. Listeners subscribe while their window is up
    /// and unsubscribe on close.
    /// </summary>
    internal static class BrowserVideoSignal
    {
        public static bool IsPlaying { get; private set; }

        public static event Action<bool>? PlayingChanged;

        /// <summary>Report the playback state. Raises only on a flip. Never throws.</summary>
        public static void Set(bool playing)
        {
            if (IsPlaying == playing) return;
            IsPlaying = playing;
            try { PlayingChanged?.Invoke(playing); }
            catch (Exception ex) { Log.Debug("[BrowserVideo] a PlayingChanged listener threw: {E}", ex.Message); }
        }
    }
}
