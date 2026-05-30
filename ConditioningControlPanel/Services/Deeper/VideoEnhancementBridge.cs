using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Ties <see cref="VideoService"/> playback (mandatory videos AND
    /// asset-folder videos — the same PlayVideo path) to the enhancement
    /// runtime. On <see cref="VideoService.VideoStarted"/> it resolves a
    /// .ccpenh.json for the playing file via <see cref="EnhancementResolver"/>
    /// and, on a match, binds an <see cref="EnhancementEngine"/> to the orphaned
    /// <see cref="VideoServiceTimeSource"/> adapter on the primary (audio-bearing)
    /// video window. Unbinds on <see cref="VideoService.VideoEnded"/>.
    ///
    /// Owns its OWN <see cref="EnhancementHostService"/> instance — exactly like
    /// <see cref="BrowserEnhancementBridge"/> — so it never conflicts with the
    /// standalone Deeper player bound to <c>App.DeeperHost</c> (a mandatory video
    /// can fire while the player is open).
    ///
    /// Gated by <c>AppSettings.VideoEnhanceIfPossible</c> (default off): no-ops
    /// entirely when the setting is off.
    /// </summary>
    public sealed class VideoEnhancementBridge : IDisposable
    {
        private readonly VideoService _video;
        private readonly EnhancementHostService _host = new();
        private VideoServiceTimeSource? _source;
        private bool _disposed;

        public VideoEnhancementBridge(VideoService video)
        {
            _video = video ?? throw new ArgumentNullException(nameof(video));
            _video.VideoStarted += OnVideoStarted;
            _video.VideoEnded += OnVideoEnded;
        }

        // VideoStarted fires on the UI thread (StartVideoPlayback runs from a
        // DispatcherTimer tick). VideoEnded can fire from Cleanup() on a LibVLC
        // callback thread, so teardown is marshaled to the UI thread to avoid
        // racing the engine's UI-thread tick / webcam handlers.
        private void OnVideoStarted(object? sender, EventArgs e) => RunOnUi(BindForCurrentVideo);
        private void OnVideoEnded(object? sender, EventArgs e) => RunOnUi(Unbind);

        private void BindForCurrentVideo()
        {
            if (_disposed) return;
            try
            {
                var enabled = App.Settings?.Current?.VideoEnhanceIfPossible ?? false;
                if (!enabled) return;

                var path = _video.LastVideoPath;
                if (string.IsNullOrEmpty(path)) return;

                // Re-bind cleanly if a prior video's engine is somehow still bound.
                Unbind();

                ResolvedEnhancement resolved;
                try
                {
                    resolved = EnhancementResolver.ResolveForLocalMedia(path);
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("VideoEnhancementBridge: resolve failed for {File}: {Error}",
                        Path.GetFileName(path), ex.Message);
                    return;
                }
                if (!resolved.Found) return;

                // Acceptance #6: when LibVLC is unavailable the MediaElement
                // fallback window never sets PrimaryMediaPlayer, so the engine
                // has no playback clock to attach to. Log loudly rather than
                // failing silent (and don't bind a dead source).
                if (_video.PrimaryMediaPlayer == null)
                {
                    App.Logger?.Warning(
                        "VideoEnhancementBridge: matched enhancement ({Source}) for {File} but no primary LibVLC player is active (MediaElement fallback?) — enhancement engine cannot attach.",
                        resolved.Source, Path.GetFileName(path));
                    return;
                }

                bool loaded = resolved.FilePath != null
                    ? _host.LoadFromFile(resolved.FilePath)
                    : _host.LoadFromMemory(resolved.Enhancement!, "video:" + Path.GetFileName(path));
                if (!loaded) return;

                // Capture the source in a local so the attach/detach lambdas
                // operate on THIS binding's source, not whatever the field
                // points to later — mirrors BrowserAutoDiscovery.BindActive so a
                // fast next video can't make a stale detach tear down the new
                // source.
                var src = new VideoServiceTimeSource(_video);
                _source = src;
                if (!_host.Bind(src,
                        attach: () => src.Attach(),
                        detach: () =>
                        {
                            try { src.Detach(); } catch { }
                            try { src.Dispose(); } catch { }
                            if (ReferenceEquals(_source, src)) _source = null;
                        }))
                {
                    src.Dispose();
                    if (ReferenceEquals(_source, src)) _source = null;
                    try { _host.Unload(); } catch { }
                    return;
                }

                App.Logger?.Information("VideoEnhancementBridge: bound enhancement ({Source}) for {File}",
                    resolved.Source, Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "VideoEnhancementBridge.BindForCurrentVideo failed");
                Unbind();
            }
        }

        private void Unbind()
        {
            try { _host.UnbindEngine(); } catch { }
            try { _host.Unload(); } catch { }
            try { _source?.Dispose(); } catch { }
            _source = null;
        }

        private static void RunOnUi(Action action)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null || d.HasShutdownStarted)
            {
                try { action(); } catch { }
                return;
            }
            if (d.CheckAccess()) action();
            else { try { d.BeginInvoke(action, DispatcherPriority.Normal); } catch { } }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _video.VideoStarted -= OnVideoStarted; } catch { }
            try { _video.VideoEnded -= OnVideoEnded; } catch { }
            Unbind();
            try { _host.Dispose(); } catch { }
        }
    }
}
