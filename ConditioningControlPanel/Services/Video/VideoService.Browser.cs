using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using ConditioningControlPanel.Services.Video.Browser;
using Screen = System.Windows.Forms.Screen;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The browser half of the hybrid video engine (docs/BROWSER_VIDEO_ENGINE_PLAN.md).
    ///
    /// VideoService stays the director for BOTH engines: scheduling, selection, XP, troll replay,
    /// mercy, events, ducking, safety timers, attention checks, strict mode and grace pause all live
    /// in the main file and are shared verbatim. Everything in here is the thin adapter that makes a
    /// WebView2 session look exactly like a LibVLC one to those consumers.
    ///
    /// Deliberately NOT armed for a browser session: the vout watchdog, the wedge ladder, the native
    /// quarantine/retire machinery and the poison cooldown. There is no in-process decoder to wedge,
    /// so a browser session must never spend one of the four per-session LibVLC retires or be gated
    /// by <see cref="NativePoisonCooldownRemainingMs"/>.
    /// </summary>
    public partial class VideoService
    {
        private BrowserVideoEngine? _browser;
        private bool _browserWired;

        /// <summary>True between a browser session's first window and its teardown. Read by the
        /// shared control surface (pause/play/seek/volume/time) to route to the page.</summary>
        private volatile bool _browserActive;

        private string? _browserPath;
        private bool _browserStrict;
        private long _browserTimeMs = -1;
        /// <summary>Teardown generation captured when the session started - a late page message that
        /// belongs to a video the user already panicked away must not act (see _teardownGeneration).</summary>
        private int _browserGeneration;
        /// <summary>At most ONE LibVLC replay per browser trigger. The page reports its own 10s
        /// no-playing failure AND this side runs the same clock, so both can fire for one stall.</summary>
        private bool _browserFallbackDone;
        /// <summary>VideoStarted fires on the page's first real <c>playing</c>, not at window-show:
        /// a session that never reaches a frame falls back to LibVLC, which fires it instead.</summary>
        private bool _browserStartedFired;

        private BrowserVideoEngine Browser
        {
            get
            {
                var engine = _browser ??= BrowserVideoEngine.Instance;
                if (!_browserWired)
                {
                    _browserWired = true;
                    engine.Meta += OnBrowserMeta;
                    engine.Playing += OnBrowserPlaying;
                    engine.Time += OnBrowserTime;
                    engine.Ended += OnBrowserEnded;
                    engine.Failed += OnBrowserFailed;
                    engine.Clicked += OnBrowserClicked;
                    engine.KeyPressed += OnBrowserKey;
                }
                return engine;
            }
        }

        // ===================== start =====================

        /// <summary>
        /// Runs a mandatory video through the browser engine. Returns false when nothing could be
        /// brought up, in which case the caller falls straight through to the LibVLC path with
        /// nothing to undo. Must be called on the UI thread, from StartVideoPlayback only.
        /// </summary>
        private bool StartBrowserVideoPlayback(string path, bool strict)
        {
            try
            {
                var screens = App.GetAllScreensCached().ToList();
                if (screens.Count == 0) return false;   // LibVLC path owns the no-screens end
                var primary = screens.FirstOrDefault(s => s.Primary) ?? screens[0];
                var secondaries = screens.Where(s => !s.Primary).ToList();
                if (!ShouldFillSecondaryMonitors(screens.Count)) secondaries.Clear();

                _browserPath = path;
                _browserStrict = strict;
                _browserTimeMs = -1;
                _browserGeneration = _teardownGeneration;
                _browserFallbackDone = false;
                _browserStartedFired = false;

                // Chaos random-segment mode: the LibVLC path seeks from LengthChanged, but the page
                // takes a start offset directly. The fraction needs the duration, which only arrives
                // with `meta`, so the real jump happens there - this covers nothing yet on purpose.
                var req = new BrowserVideoRequest
                {
                    Path = path,
                    PrimaryScreen = primary,
                    SecondaryScreens = secondaries,
                    Volume = GetEffectiveVolume() / 100.0,
                    Muted = GetEffectiveVolume() <= 0,
                    BlurBackground = App.Settings?.Current?.VideoBlurredBackgroundEnabled == true,
                    IsHostPaused = () => _gracePaused,
                    ConfigureBeforeShow = (win, _, _) =>
                    {
                        // Same strict contract as a LibVLC window: Closing veto + key swallowing.
                        // The page ALSO preventDefaults those keys and reports them back as
                        // {type:'key'} (see OnBrowserKey) because a focused WebView2 eats keyboard
                        // input before WPF ever sees it.
                        SetupStrictHandlers(win, strict);
                    },
                    ConfigureAfterShow = (win, _, _) =>
                    {
                        // Non-activating so the WebView2 never takes focus away from the app's own
                        // key handling, and so a click on the video can't raise it over the
                        // attention targets / chaos layer. Both need a realized HWND.
                        MakeNonActivating(win);
                        PreventClickRaise(win);
                    },
                };

                if (!Browser.StartSession(req))
                {
                    VideoDiag.Log("VIDEO", "browser session could not start - using LibVLC");
                    return false;
                }

                _browserActive = true;
                foreach (var w in Browser.Windows) _windows.Add(w);
                _primaryVideoWindow = Browser.PrimaryWindow;

                // Same two guards the LibVLC path arms before the first frame: a 10-minute backstop
                // in case `meta` never arrives, and the user's hard max-length cap (#584).
                StartFallbackSafetyTimer();
                StartMaxLengthCapTimer();

                // A window is on screen: grace pause becomes available, the strict veto means
                // something. NO wedge watchdog - nothing native can wedge the dispatcher here.
                _playbackStarted = true;

                App.Bubbles?.PauseAndClear();
                if (App.Settings.Current.AttentionChecksEnabled) SetupAttention();

                VideoDiag.Log("VIDEO", $"browser session on screen ({_windows.Count} window(s)) - {Path.GetFileName(path)}");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "VideoService: browser session start threw - falling back to LibVLC");
                try { StopBrowserSession(); } catch { }
                return false;
            }
        }

        // ===================== teardown =====================

        /// <summary>
        /// Closes the browser surfaces and drops them from the shared window list. Called from
        /// CloseAll (the single teardown funnel for every path: natural end, panic, safety timeout,
        /// attention retry, session lock, suspend) and from the LibVLC fallback. Idempotent.
        /// </summary>
        private void StopBrowserSession()
        {
            var engine = _browser;
            if (engine == null || (!_browserActive && !engine.IsSessionActive)) return;

            _browserActive = false;
            _browserTimeMs = -1;

            var browserWindows = engine.Windows.ToList();
            try { engine.StopSession(); }
            catch (Exception ex) { App.Logger?.Debug("VideoService: browser StopSession failed - {Error}", ex.Message); }

            foreach (var w in browserWindows)
            {
                _windows.Remove(w);
                if (ReferenceEquals(_primaryVideoWindow, w)) _primaryVideoWindow = null;
            }
        }

        /// <summary>
        /// The runtime half of the hybrid contract (plan §4): the page could not play this file, so
        /// replay it through LibVLC exactly once, silently. No VideoEnded is raised - as far as every
        /// consumer is concerned this is still the same video run, and the duck ref taken in
        /// PlayVideo is deliberately kept so CloseAll still balances it exactly once.
        /// </summary>
        private void FallbackToLibVlc(string reason)
        {
            if (_browserFallbackDone) return;
            _browserFallbackDone = true;

            var path = _browserPath;
            var strict = _browserStrict;
            App.Logger?.Warning("VideoService: browser playback failed for {File} ({Reason}) - replaying via LibVLC",
                Path.GetFileName(path ?? "(none)"), reason);
            VideoDiag.Log("VIDEO", $"browser FALLBACK ({reason}) - replaying {Path.GetFileName(path ?? "?")} via LibVLC");

            // The strict Closing veto refuses a close while _videoPlaying is true and no teardown is
            // running, which would strand the browser windows on screen. Same trick ShowMessage uses.
            var wasPlaying = _videoPlaying;
            _videoPlaying = false;
            try { StopBrowserSession(); }
            finally { _videoPlaying = wasPlaying; }

            if (string.IsNullOrEmpty(path) || _isCleaningUp) { Cleanup(); return; }

            // Reset everything the failed attempt consumed so the LibVLC run starts clean. The
            // attention schedule is rebuilt by StartVideoPlayback's own SetupAttention call.
            try { _safetyTimer?.Stop(); } catch { }
            try { _attentionTimer?.Stop(); } catch { }
            lock (_targets)
            {
                foreach (var t in _targets.ToList()) t.Destroy();
                _targets.Clear();
            }
            _hits = _total = _spawned = 0;
            _spawnTimes.Clear();
            _duration = 0;
            _lastWatchPositionMs = 0;
            _creditedWatchSeconds = 0;
            _startTime = DateTime.Now;

            StartVideoPlayback(path, strict, forceLibVlc: true);
        }

        // ===================== page events =====================

        /// <summary>True when a page message still belongs to the live run.</summary>
        private bool BrowserEventIsCurrent()
            => _browserActive && !_isCleaningUp && _browserGeneration == _teardownGeneration;

        /// <summary>
        /// Queue work for AFTER the current page message returns. Page events arrive inside the
        /// WebView2 message callback, and anything heavy done there (closing the very windows that
        /// raised it, CloseAll's pumped waits, building LibVLC windows) re-enters the browser's own
        /// message loop. Same discipline as the LibVLC handlers, which never do teardown inline on
        /// the LibVLC event thread. Normal priority — DispatcherPriority.Loaded is starved here.
        /// </summary>
        private static void PostAfterPageMessage(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            try { dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, action); }
            catch (Exception ex) { App.Logger?.Debug("VideoService: browser continuation dispatch failed - {Error}", ex.Message); }
        }

        private void OnBrowserMeta(long durationMs, int width, int height)
        {
            if (!BrowserEventIsCurrent()) return;
            // 0 = the page cannot know the duration (some webm / fragmented mp4). Arming a
            // zero-length guillotine would kill the clip instantly; the 10-minute fallback timer
            // stays armed as the net, exactly as it does when LibVLC's LengthChanged never fires.
            if (durationMs <= 0)
            {
                App.Logger?.Debug("VideoService: browser meta with unknown duration ({W}x{H}) - keeping the fallback timer", width, height);
                return;
            }

            _duration = durationMs / 1000.0;
            App.Logger?.Information("VideoService: browser meta - duration={Duration}s ({W}x{H})", _duration, width, height);

            // meta can arrive more than once (Infinity at loadedmetadata, real value at
            // durationchange) - last one wins, so the guillotine is simply re-armed.
            StartSafetyTimer(_duration);

            // Free duration metadata: the selection-time min/max filter improves with every browser
            // play, with no LibVLC parse (and no preparse crash surface, #750-#753).
            try
            {
                if (!string.IsNullOrEmpty(_browserPath)) MetadataCache?.StoreDuration(_browserPath!, _duration);
            }
            catch (Exception ex) { App.Logger?.Debug("VideoService: browser duration backfill failed - {Error}", ex.Message); }

            // Chaos random segment: same shared fraction as the LibVLC path, applied as a seek now
            // that the length is known (the page holds every screen in lockstep).
            try
            {
                long segMs = (long)(_segmentSec * 1000);
                if (SegmentArmed && durationMs > segMs)
                {
                    long startMs = (long)((durationMs - segMs) * _segmentFraction);
                    if (startMs > 500)
                    {
                        Browser.Seek(startMs);
                        App.Logger?.Information("VideoService: browser random segment - seeking to {Start}s of {Len}s",
                            startMs / 1000, durationMs / 1000);
                    }
                }
            }
            catch (Exception ex) { App.Logger?.Debug("VideoService: browser random-segment seek - {Error}", ex.Message); }
        }

        private void OnBrowserPlaying()
        {
            if (!BrowserEventIsCurrent() || _browserStartedFired) return;
            _browserStartedFired = true;

            VideoDiag.Log("VIDEO", "browser playback confirmed (page reported playing)");
            VideoStarted?.Invoke(this, EventArgs.Empty);
            _ = App.Haptics?.StartVideoBackgroundVibeAsync();
            try { App.Haptics?.FunScript?.OnVideoStarted(_browserPath ?? ""); }
            catch (Exception ex) { App.Logger?.Debug("FunScript start hook failed: {Error}", ex.Message); }
            App.Logger?.Information("Playing: {File} (browser engine)", Path.GetFileName(_browserPath ?? ""));
        }

        private void OnBrowserTime(long ms)
        {
            if (!_browserActive) return;
            _browserTimeMs = ms;
            _lastWatchPositionMs = ms;   // watch-time crediting (#447), same field the LibVLC path feeds
            try { PrimaryPlaybackTimeMsChanged?.Invoke(ms); }
            catch (Exception ex) { App.Logger?.Debug("PrimaryPlaybackTimeMsChanged handler error: {Error}", ex.Message); }
        }

        private void OnBrowserEnded()
        {
            if (!BrowserEventIsCurrent()) return;
            // Straight into the shared end-of-video pipeline: attention pass/fail, XP, troll replay,
            // mercy, watch credit, ScheduleNext — but off the page's message callback first.
            PostAfterPageMessage(() => { if (BrowserEventIsCurrent()) OnEnded(); });
        }

        private void OnBrowserFailed(string reason, bool blameFile)
        {
            if (!_browserActive) return;
            // Marked immediately (cheap, and the routing decision must not depend on the hop landing).
            if (blameFile && !string.IsNullOrEmpty(_browserPath))
                BrowserUnsafeVideoCache.Add(_browserPath, reason);
            PostAfterPageMessage(() => { if (_browserActive) FallbackToLibVlc(reason); });
        }

        private void OnBrowserClicked()
        {
            if (!_browserActive) return;
            // Window-level PreviewMouseDown never fires over a WebView2 child HWND, so this message
            // is what keeps the attention targets clickable on top of the video.
            BringTargetsToFront();
        }

        /// <summary>
        /// Keyboard policy for a browser session. Mirrors <c>SetupStrictHandlers</c>: the page
        /// preventDefaults the dangerous keys and reports them here, because a focused WebView2 sends
        /// keystrokes to Chromium rather than to the WPF window.
        /// </summary>
        private void OnBrowserKey(string key, bool alt, bool ctrl, bool shift)
        {
            if (!BrowserEventIsCurrent()) return;
            try
            {
                var settings = App.Settings?.Current;
                if (settings == null) return;
                bool isEscape = string.Equals(key, "Escape", StringComparison.OrdinalIgnoreCase);

                if (_browserStrict)
                {
                    // Strict: Escape may PAUSE, never escape - and only under the same conditions the
                    // strict window handler allows (never during lockdown, never when Escape IS the
                    // panic key, which the global hook owns). Everything else is swallowed.
                    if (isEscape &&
                        App.Lockdown?.IsActive != true &&
                        settings.PanicKeyEnabled &&
                        !string.Equals(settings.PanicKey, "Escape", StringComparison.Ordinal))
                    {
                        if (TryGracePauseFromPanic())
                            VideoDiag.Log("PANIC", "strict browser window: ESC consumed as video grace pause");
                    }
                    return;
                }

                if (isEscape)
                {
                    if (TryGracePauseFromPanic())
                    {
                        VideoDiag.Log("PANIC", "ESC consumed as video grace pause (browser window)");
                        return;
                    }
                    VideoDiag.Log("PANIC", "ESC received by the browser video page - dismissing via Cleanup");
                    PostAfterPageMessage(Cleanup);   // teardown must not run inside the page's callback
                    return;
                }

                if (settings.PanicKeyEnabled && MatchesPanicKey(key, settings.PanicKey))
                {
                    if (TryGracePauseFromPanic())
                    {
                        VideoDiag.Log("PANIC", $"panic key '{key}' consumed by the browser page as a grace pause");
                        return;
                    }
                    VideoDiag.Log("PANIC", $"panic key '{key}' received by the browser page - ForceCleanup");
                    PostAfterPageMessage(() => ForceCleanup());
                }
            }
            catch (Exception ex) { App.Logger?.Debug("VideoService: browser key handling failed - {Error}", ex.Message); }
        }

        /// <summary>
        /// The page reports DOM key names ("Escape", "F8", "a", " "); the setting stores a WPF
        /// <see cref="System.Windows.Input.Key"/> name ("Escape", "F8", "A", "Space"). Function keys,
        /// Escape and letters line up case-insensitively once Space is translated, which covers every
        /// panic key a user can actually bind through the settings picker.
        /// </summary>
        private static bool MatchesPanicKey(string pageKey, string? panicKey)
        {
            if (string.IsNullOrEmpty(pageKey) || string.IsNullOrEmpty(panicKey)) return false;
            var normalized = pageKey == " " ? "Space" : pageKey;
            return string.Equals(normalized, panicKey, StringComparison.OrdinalIgnoreCase);
        }
    }
}
