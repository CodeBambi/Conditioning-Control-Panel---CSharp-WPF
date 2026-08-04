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
        /// <summary>The chaos random-segment jump is ONE seek per session. <c>meta</c> can arrive
        /// twice (Infinity at loadedmetadata, the real value at durationchange) and a second seek
        /// would yank the user back mid-clip.</summary>
        private bool _browserSegmentSeeked;

        /// <summary>True while a browser video session owns the screen. Read by AudioService so the
        /// duck sweep never lowers the app's OWN video audio (LibVLC audio is in-process and
        /// structurally un-duckable, so this is what keeps the two engines at parity).</summary>
        public bool IsBrowserSessionActive => _browserActive;

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
                _browserSegmentSeeked = false;

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
                    // Parity with the LibVLC video windows, which do NOT hide the pointer: a video
                    // that swallows the cursor while the rest of the app still shows one reads as a
                    // frozen machine, and attention targets are clicked over this surface.
                    HideCursor = false,
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
                        // EXACT parity with the LibVLC path (StartVideoPlayback): WS_EX_NOACTIVATE is
                        // stamped ONLY during a chaos run, where the game layer must keep z-order.
                        // Outside chaos the always-on PreventClickRaise is what stops a click from
                        // raising the video, and the window keeps focus - which the WebView2 needs,
                        // because ESC / panic keys only reach C# through the page's {type:'key'}
                        // reports and a page that never gets a keydown reports nothing.
                        if (App.Chaos?.IsRunning == true) MakeNonActivating(win);
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

                // PlayVideo's prologue armed the wedge ladder before routing was decided, and
                // _playbackStarted = true above would unlock its DESTRUCTIVE rungs (off-thread
                // player Stop, RetireSharedLibVLC, the _wedgeStallSeen veto stand-down) for a
                // session that owns no native player at all. Disarm it here; a UI stall during a
                // browser video must trigger nothing. FallbackToLibVlc re-arms it before it hands
                // the clip to LibVLC, and WedgeWatchdogTick also no-ops while _browserActive.
                StopWedgeWatchdog();

                App.Bubbles?.PauseAndClear();
                if (App.Settings.Current.AttentionChecksEnabled) SetupAttention();

                VideoDiag.Log("VIDEO", $"browser session on screen ({_windows.Count} window(s)) - {Path.GetFileName(path)}");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "VideoService: browser session start threw - falling back to LibVLC");
                // NOT a bare StopBrowserSession(): _videoPlaying is still true here, so the strict
                // Closing veto would refuse to close the windows this throw is trying to clean up
                // and strand fullscreen surfaces on screen forever. Same clear/restore dance as the
                // runtime fallback, strict bridge included.
                try { StopBrowserSessionForHandoff(); } catch { }
                // The disarm above already ran if the throw came after it; the LibVLC attempt this
                // false return triggers must not build its windows unguarded (that window creation
                // is where the historic freeze strikes). Pre-roll re-arm, same as FallbackToLibVlc.
                _playbackStarted = false;
                StartWedgeWatchdog();
                return false;
            }
        }

        /// <summary>
        /// Close the browser surfaces while the run CONTINUES (start-path throw, runtime fallback) -
        /// i.e. everywhere the windows must go but <see cref="_videoPlaying"/> must stay true for the
        /// LibVLC attempt that follows.
        ///
        /// Two things have to be true at once for that: the strict Closing veto
        /// (<c>SetupStrictHandlers</c>) refuses a close while <c>_videoPlaying</c> is set and no
        /// teardown is running, so the flag is cleared across the close; and
        /// <see cref="IsStrictActive"/> is <c>(_videoPlaying &amp;&amp; _strictActive) || _strictRetryPending</c>,
        /// so clearing it would make a strict session look non-strict for the duration - exactly the
        /// gap <see cref="_strictRetryPending"/> exists to bridge for ShowMessage. Bridge it the same way.
        /// </summary>
        private void StopBrowserSessionForHandoff()
        {
            var wasPlaying = _videoPlaying;
            var wasRetryPending = _strictRetryPending;
            bool bridgeStrict = wasPlaying && _strictActive;

            if (bridgeStrict) _strictRetryPending = true;
            _videoPlaying = false;
            try { StopBrowserSession(); }
            finally
            {
                _videoPlaying = wasPlaying;
                if (bridgeStrict) _strictRetryPending = wasRetryPending;
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
        /// The runtime half of the hybrid contract (plan §4): the page could not play this file.
        ///
        /// PRE-START failures only (error 101 before the first frame, ProcessFailed, environment
        /// failure, a throw on the start path) replay the clip through LibVLC exactly once, silently.
        /// No VideoEnded is raised - as far as every consumer is concerned this is still the same
        /// video run, and the duck ref taken in PlayVideo is deliberately kept so CloseAll still
        /// balances it exactly once.
        ///
        /// A failure AFTER playback genuinely started is NOT replayed: see the branch below.
        /// </summary>
        private void FallbackToLibVlc(string reason)
        {
            if (_browserFallbackDone) return;
            _browserFallbackDone = true;

            var path = _browserPath;
            var strict = _browserStrict;

            // ---- mid-clip failure: end the run, never replay it ----
            // VideoStarted has already gone out to seven subscribers; a replay would fire it a
            // second time and make the user rewatch the whole clip from 0 for a stall at the end.
            // The engine has already marked the file browser-unsafe when it blamed the file, so the
            // NEXT play of it routes to LibVLC anyway. Mirrors BubbleCountWindow's startedOnce
            // branch: end through the same funnel a natural end uses, so it is one VideoEnded and
            // EndCurrentVideo's normal attention pass/fail handling.
            if (_browserStartedFired)
            {
                App.Logger?.Warning("VideoService: browser playback failed MID-CLIP for {File} ({Reason}) - ending the video instead of replaying it",
                    Path.GetFileName(path ?? "(none)"), reason);
                VideoDiag.Log("VIDEO", $"browser MID-CLIP FAILURE ({reason}) - ending {Path.GetFileName(path ?? "?")} through the normal end funnel");
                OnEnded();
                return;
            }

            App.Logger?.Warning("VideoService: browser playback failed for {File} ({Reason}) - replaying via LibVLC",
                Path.GetFileName(path ?? "(none)"), reason);
            VideoDiag.Log("VIDEO", $"browser FALLBACK ({reason}) - replaying {Path.GetFileName(path ?? "?")} via LibVLC");

            // Handing the clip back to LibVLC means the wedge ladder matters again. Re-arm it in
            // the pre-roll observation state BEFORE the handoff below - closing WebView2 HWNDs is
            // an out-of-process round trip that can itself stall, and StartVideoPlayback re-arms
            // for real once its windows are up. WedgeWatchdogTick still no-ops until
            // StopBrowserSession clears _browserActive, so this cannot fire early.
            _playbackStarted = false;
            StartWedgeWatchdog();

            // Drops the surfaces without letting the strict Closing veto strand them, and without
            // letting IsStrictActive blink false across the handoff. Guarded like the start-path
            // twin: a throw here would escape into the dispatcher continuation with
            // _browserFallbackDone already latched, so nothing would ever retry.
            try { StopBrowserSessionForHandoff(); }
            catch (Exception ex) { App.Logger?.Error(ex, "VideoService: browser handoff teardown threw during fallback"); }

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
                // NOT for content-pack clips: those live at a fresh ccp_temp_<GUID> path per play, so
                // every backfill would add a key that can never be hit again and video_metadata.json
                // would grow without bound.
                if (!string.IsNullOrEmpty(_browserPath) && !IsMediaTempPath(_browserPath))
                    MetadataCache?.StoreDuration(_browserPath!, _duration);
            }
            catch (Exception ex) { App.Logger?.Debug("VideoService: browser duration backfill failed - {Error}", ex.Message); }

            // Chaos random segment: same shared fraction as the LibVLC path, applied as a seek now
            // that the length is known (the page holds every screen in lockstep).
            try
            {
                long segMs = (long)(_segmentSec * 1000);
                // One seek per session: meta arrives twice for webm / fragmented mp4 and a second
                // jump would drag the user back to the segment start mid-clip.
                if (!_browserSegmentSeeked && SegmentArmed && durationMs > segMs)
                {
                    long startMs = (long)((durationMs - segMs) * _segmentFraction);
                    if (startMs > 500)
                    {
                        _browserSegmentSeeked = true;
                        Browser.Seek(startMs);
                        App.Logger?.Information("VideoService: browser random segment - seeking to {Start}s of {Len}s",
                            startMs / 1000, durationMs / 1000);
                    }
                }
            }
            catch (Exception ex) { App.Logger?.Debug("VideoService: browser random-segment seek - {Error}", ex.Message); }
        }

        /// <summary>True for a content-pack clip's per-play decrypt path (App.GetMediaTempPath()).
        /// Those paths are GUIDs that never come back, so nothing may be cached against them.</summary>
        private static bool IsMediaTempPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                var root = App.GetMediaTempPath();
                if (string.IsNullOrEmpty(root)) return false;
                var rootFull = Path.GetFullPath(root)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return Path.GetFullPath(path).StartsWith(rootFull + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private void OnBrowserPlaying()
        {
            if (!BrowserEventIsCurrent() || _browserStartedFired) return;
            // The internal flags are set SYNCHRONOUSLY: the mid-clip-failure branch in
            // FallbackToLibVlc keys off _browserStartedFired, and it must be true from the instant
            // the page says it is playing, whether or not the hop below has landed yet.
            _browserStartedFired = true;

            VideoDiag.Log("VIDEO", "browser playback confirmed (page reported playing)");
            App.Logger?.Information("Playing: {File} (browser engine)", Path.GetFileName(_browserPath ?? ""));

            // The RAISE is deferred. This runs inside WebView2's WebMessageReceived callback, and
            // VideoStarted has seven subscribers (barks, chaos, bouncing text, session log, ...)
            // that show windows and start their own work - all of it would run nested inside the
            // browser's message loop. Same discipline as the LibVLC handlers.
            PostAfterPageMessage(() =>
            {
                if (!BrowserEventIsCurrent()) return;
                VideoStarted?.Invoke(this, EventArgs.Empty);
                _ = App.Haptics?.StartVideoBackgroundVibeAsync();
                try { App.Haptics?.FunScript?.OnVideoStarted(_browserPath ?? ""); }
                catch (Exception ex) { App.Logger?.Debug("FunScript start hook failed: {Error}", ex.Message); }
            });
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
            // EVERY branch below either shows WPF windows (the grace-pause overlays) or tears the
            // session down, and this arrives inside WebView2's message callback - so the whole
            // policy is evaluated one dispatcher hop later, with the liveness re-checked there.
            // Two rapid ESCs therefore queue two hops, and the second finds the session already
            // gone rather than running Cleanup a second time (which would re-fire VideoEnded and
            // release an InteractionQueue slot it no longer owns).
            PostAfterPageMessage(() => HandleBrowserKey(key));
        }

        private void HandleBrowserKey(string key)
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
                    // Already off the page's callback (see OnBrowserKey), but still guarded: a
                    // second ESC that raced in behind this one must not run the teardown twice.
                    if (_browserActive && BrowserEventIsCurrent() && _videoPlaying && !_isCleaningUp)
                        Cleanup();
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
                    if (_browserActive && BrowserEventIsCurrent() && _videoPlaying && !_isCleaningUp)
                        ForceCleanup();
                }
            }
            catch (Exception ex) { App.Logger?.Debug("VideoService: browser key handling failed - {Error}", ex.Message); }
        }

        /// <summary>
        /// The page reports DOM key values ("Escape", "F8", "a", " ", "7", "."); the setting stores a
        /// WPF <see cref="System.Windows.Input.Key"/> name ("Escape", "F8", "A", "Space", "D7",
        /// "OemPeriod"). Function keys, Escape and letters line up case-insensitively; everything
        /// else needs translating, or a user whose panic key is a digit gets nothing from a focused
        /// WebView2. The low-level keyboard hook remains the primary route - this is the page's.
        /// </summary>
        private static bool MatchesPanicKey(string pageKey, string? panicKey)
        {
            if (string.IsNullOrEmpty(pageKey) || string.IsNullOrEmpty(panicKey)) return false;
            var normalized = TranslateDomKey(pageKey);
            return string.Equals(normalized, panicKey, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>DOM <c>KeyboardEvent.key</c> -> WPF <see cref="System.Windows.Input.Key"/> name.
        /// Anything with no special mapping passes through unchanged (F1-F24, Escape, Home, End,
        /// Insert, Delete, PageUp/PageDown and letters all already agree case-insensitively).</summary>
        private static string TranslateDomKey(string pageKey)
        {
            // Digits are "0".."9" in the DOM and D0..D9 in WPF (the numpad ones report the same
            // values without event.location, which the bridge does not carry - the LL hook covers
            // a numpad binding).
            if (pageKey.Length == 1 && pageKey[0] >= '0' && pageKey[0] <= '9') return "D" + pageKey;

            return pageKey switch
            {
                " " => "Space",
                "Backspace" => "Back",
                "Tab" => "Tab",
                "Enter" => "Return",
                "ArrowUp" => "Up",
                "ArrowDown" => "Down",
                "ArrowLeft" => "Left",
                "ArrowRight" => "Right",
                "," => "OemComma",
                "." => "OemPeriod",
                "-" => "OemMinus",
                "=" => "OemPlus",
                ";" => "OemSemicolon",
                "'" => "OemQuotes",
                "[" => "OemOpenBrackets",
                "]" => "OemCloseBrackets",
                "\\" => "OemBackslash",
                "/" => "OemQuestion",
                "`" => "OemTilde",
                _ => pageKey,
            };
        }
    }
}
