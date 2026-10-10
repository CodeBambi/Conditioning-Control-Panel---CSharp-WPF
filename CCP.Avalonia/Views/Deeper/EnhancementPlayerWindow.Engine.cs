using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Deeper
{
    // The engine half of the player (WPF EnhancementPlayerWindow.xaml.cs: _host.Bind at :1195 for
    // audio and :1447 for video, the EnhancementAudioPlayer transport, the eye tracking button).
    // Core's EnhancementHostService + EnhancementEngine do the rule work; this file is the time
    // source they read, the audio transport, and the panic / close teardown.
    public partial class EnhancementPlayerWindow
    {
        private static readonly List<EnhancementPlayerWindow> s_open = new();

        private readonly EnhancementHostService _host = new();
        private PlayerTimeSource? _timeSource;
        private IDeeperLocalAudio? _audio;
        private int _audioLoadGen;
        private double _lastRaisedSec = -1;
        private bool _engineHooked;

        /// <summary>The host this window drives (tests read IsRunning and the loaded file).</summary>
        internal EnhancementHostService Host => _host;

        /// <summary>The audio transport, once a file is open (tests).</summary>
        internal IDeeperLocalAudio? Audio => _audio;

        private void HookEngine()
        {
            if (_engineHooked) return;
            _engineHooked = true;
            // The head's effects and tracker behind Core's host (WPF: RealActionDispatcher, App.Webcam).
            EnhancementHostService.DispatcherFactory ??= static () => new RealActionDispatcher();
            EnhancementHostService.WebcamProvider ??= static () => TrackerWebcam.Instance;
            _timeSource = new PlayerTimeSource(this);
            _host.ActionLogged += OnHostActionLogged;
            _host.Diagnostic += OnHostDiagnostic;
            // WPF GamificationBridge.OnEnhancementCompleted (:657): the play count and the per-play badges.
            _host.EnhancementCompleted += (_, e) => global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var a = App.Achievements; if (a == null) return;
                    a.TrackEnhancementPlayed(e.DistinctTriggerTypes);
                    if (e.WebcamTriggerUsed) a.TryUnlock("wired_in");
                    if (e.GazeHeldFull) a.TryUnlock("dont_look_away");
                    if (e.Featured) a.TryUnlock("directors_cut");
                }
                catch (Exception ex) { Serilog.Log.Debug(ex, "enhancement completed count"); }
            });
            // Statics only once the window is really up: a window that is built and never shown
            // (render proof) must not sit in the open list or under the tracker's event.
            Opened += (_, _) =>
            {
                if (s_open.Contains(this)) return;
                s_open.Add(this);
                WebcamTracker.Instance.StateChanged += OnEyeStateChanged;
                RefreshEyeTrackingLabel();
            };
        }

        private void OnHostActionLogged(string line) => PostUi(() => IngestActionLine(line));
        private void OnHostDiagnostic(string line) => PostUi(() => IngestDiagnosticLine(line));

        private static void PostUi(Action a)
        {
            if (Dispatcher.UIThread.CheckAccess()) a(); else Dispatcher.UIThread.Post(a);
        }

        /// <summary>
        /// Runs on the 100 ms UI tick. Keeps the host in step with what the window shows (the
        /// loaded enhancement, whether media is playing), reads the audio clock, and feeds the
        /// engine its time at the 10 Hz WPF's sources fired at.
        /// </summary>
        private void EngineTick()
        {
            HookEngine();

            if (_audio != null && !_isVideoMode)
            {
                if (_isPlaying) _currentSec = _audio.PositionSeconds;
                var d = _audio.DurationSeconds;
                if (d > 0) _durationSec = d;
            }

            // Host follows the window: one loaded enhancement, engine bound only while media runs.
            if (!ReferenceEquals(_host.LoadedEnhancement, _loadedEnhancement))
            {
                if (_loadedEnhancement == null) _host.Unload();
                else _host.LoadFromMemory(_loadedEnhancement, _loadedFilePath ?? "memory");
            }
            if (_host.LoadedEnhancement != null && !_host.IsRunning && _isPlaying && MediaReady)
                _host.Bind(_timeSource!);

            if (_host.IsRunning && (_isPlaying || Math.Abs(_currentSec - _lastRaisedSec) > 0.001))
            {
                _lastRaisedSec = _currentSec;
                _timeSource!.Raise(_currentSec);
            }
        }

        private bool MediaReady => _isVideoMode ? _videoNavigated : _audio != null;

        // -- audio transport (WPF EnhancementAudioPlayer) ------------------------------------

        private async void OpenAudioAsync(string path)
        {
            var gen = ++_audioLoadGen;
            DisposeAudio();
            _host.UnbindEngine();
            _isPlaying = false;
            _currentSec = 0;
            _durationSec = 0;
            IDeeperLocalAudio? audio = null;
            try { audio = await DeeperLocalAudio.Open(path); }
            catch (Exception ex) { Log.Debug(ex, "EnhancementPlayer: audio open failed"); }
            if (gen != _audioLoadGen || _uiTimer == null) { audio?.Dispose(); return; }
            if (audio == null)
            {
                _txtStatus.Text = Loc.Get("deeper_player_status_audio_failed");
                IngestErrorLine(Loc.Get("deeper_player_status_audio_failed"));
                return;
            }
            _audio = audio;
            audio.Volume = _sliderVolume.Value / 100.0;
            audio.Ended += () => Dispatcher.UIThread.Post(() => { if (ReferenceEquals(_audio, audio)) OnAudioEnded(); });
            _durationSec = audio.DurationSeconds;
            _txtTotal.Text = FormatTime(_durationSec);
            // WPF LoadAudioAsync: the file plays as soon as it is open.
            AudioPlay();
        }

        private void AudioPlay()
        {
            if (_audio == null) return;
            _audio.Play();
            _isPlaying = true;
            _txtPlayPauseGlyph.Text = "⏸";
            _txtStatus.Text = Loc.Get("deeper_player_status_playing");
            UpdateStatusPill();
        }

        private void AudioPause(string statusKey = "deeper_player_status_stopped")
        {
            _audio?.Pause();
            _isPlaying = false;
            _txtPlayPauseGlyph.Text = "▶";
            _txtStatus.Text = Loc.Get(statusKey);
            UpdateStatusPill();
        }

        private void OnAudioEnded()
        {
            // One last tick at the end so the engine sees the media complete, then rewind.
            if (_host.IsRunning && _durationSec > 0) _timeSource!.Raise(_durationSec);
            _host.UnbindEngine();
            _isPlaying = false;
            _currentSec = 0;
            _txtPlayPauseGlyph.Text = "▶";
            _txtStatus.Text = Loc.Get("deeper_player_status_ended");
            UpdatePlayhead(0);
            UpdateStatusPill();
        }

        private void AudioSeek(double seconds)
        {
            if (_audio == null) return;
            seconds = Math.Clamp(seconds, 0, Math.Max(0, _durationSec));
            _audio.PositionSeconds = seconds;
            _currentSec = seconds;
        }

        private void DisposeAudio()
        {
            var a = _audio;
            _audio = null;
            try { a?.Dispose(); } catch (Exception ex) { Log.Debug(ex, "EnhancementPlayer: audio dispose"); }
        }

        // -- one shared player (WPF EnhancementPlayerWindow.ShowOrActivate) ---------------------

        /// <summary>The open player brought forward, else a new one shown over <paramref name="owner"/>;
        /// <paramref name="afterShow"/> then runs on it (the editor's Preview loads its enhancement).</summary>
        internal static EnhancementPlayerWindow ShowOrActivate(Window? owner, Action<EnhancementPlayerWindow>? afterShow = null)
        {
            var w = s_open.LastOrDefault();
            if (w != null)
            {
                if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
                w.Activate();
            }
            else
            {
                w = new EnhancementPlayerWindow(null, null);
                if (owner is { IsVisible: true }) w.Show(owner); else w.Show();
            }
            afterShow?.Invoke(w);
            return w;
        }

        /// <summary>The players open right now (tests).</summary>
        internal static IReadOnlyList<EnhancementPlayerWindow> Open => s_open;

        // -- panic and close -----------------------------------------------------------------

        /// <summary>Panic: every open player stops its media and its engine (overlay bands, haptics,
        /// point-fired flashes and subliminals go with the engine's Stop).</summary>
        internal static void StopAllForPanic()
        {
            foreach (var w in s_open.ToList())
            {
                try { w.StopForPanic(); }
                catch (Exception ex) { Log.Debug(ex, "EnhancementPlayer: panic stop"); }
            }
        }

        private void StopForPanic()
        {
            _host.UnbindEngine();
            if (_isVideoMode && _videoNavigated)
                _ = _videoBrowser.InvokeScriptAsync(DeeperPreview.Invoke("l.pause();"));
            _audio?.Pause();
            _isPlaying = false;
            _txtPlayPauseGlyph.Text = "▶";
            _txtStatus.Text = Loc.Get("deeper_player_status_escape_stopped");
            UpdateStatusPill();
        }

        private void CloseEngine()
        {
            HandBackEyeTracking();
            if (s_open.Remove(this))
            {
                try { WebcamTracker.Instance.StateChanged -= OnEyeStateChanged; } catch { }
            }
            _host.ActionLogged -= OnHostActionLogged;
            _host.Diagnostic -= OnHostDiagnostic;
            try { _host.Dispose(); } catch (Exception ex) { Log.Debug(ex, "EnhancementPlayer: host dispose"); }
            _audioLoadGen++;
            DisposeAudio();
        }

        // -- eye tracking (WPF BtnEyeTracking_Click :861) --------------------------------------

        /// <summary>The tracker doors. Tests swap them so no camera ever opens.</summary>
        internal static Func<bool> EyeIsRunning = () => WebcamTracker.Instance.IsRunning || WebcamTracker.Instance.IsStarting;
        internal static Func<Task<bool>> EyeStart = () => WebcamTracker.Instance.StartAsync();
        internal static Func<Task> EyeStop = () => WebcamTracker.Instance.StopAsync();
        internal static Func<bool> EyeConsentCurrent = () => Services.Webcam.WebcamConsent.IsCurrent(CoreSettings.Current);
        internal static Func<string?> EyeLastError = () => WebcamTracker.Instance.LastError;
        internal static Func<Window, string, string, Task<bool>> EyeConfirm = (o, t, m) => MessageDialog.ConfirmAsync(o, t, m);
        internal static Func<Window, string, string, Task> EyeNotice = (o, t, m) => MessageDialog.ShowAsync(o, t, m);

        private bool _eyeBusy;

        /// <summary>WPF _playerStartedWebcam: THIS player turned the camera on, so its close turns it
        /// off again. A camera that was already running when the player opened is never touched.</summary>
        private bool _eyeStartedHere;

        /// <summary>WPF Window_Closing (:2661): leave the camera the way the player found it.</summary>
        private void HandBackEyeTracking()
        {
            if (!_eyeStartedHere) return;
            _eyeStartedHere = false;
            try { if (EyeIsRunning()) _ = EyeStop(); }
            catch (Exception ex) { Log.Debug("EnhancementPlayer: webcam auto-stop failed: {Error}", ex.Message); }
        }

        internal async Task ToggleEyeTrackingAsync()
        {
            if (_eyeBusy) return;
            _eyeBusy = true;
            var title = Loc.Get("deeper_player_btn_eye_tracking_start");
            try
            {
                if (EyeIsRunning())
                {
                    _eyeStartedHere = false;   // the player's own stop: nothing left to hand back
                    await EyeStop();
                    return;
                }
                // First time: the consent and setup live on the Deeper tab's Webcam Setup card.
                if (!EyeConsentCurrent())
                {
                    await EyeNotice(this, title, Loc.Get("deeper_player_eye_tracking_first_time"));
                    return;
                }
                // Awaited: the camera opens only after the answer.
                if (!await EyeConfirm(this, title, Loc.Get("deeper_player_eye_tracking_confirm_start"))) return;
                if (await EyeStart()) _eyeStartedHere = true;   // WPF _playerStartedWebcam
                else
                    await EyeNotice(this, title, string.Format(Loc.Get("deeper_player_eye_tracking_start_failed_fmt"), EyeLastError() ?? ""));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "EnhancementPlayer: eye tracking toggle failed");
                try { await EyeNotice(this, title, string.Format(Loc.Get("deeper_player_eye_tracking_start_failed_fmt"), ex.Message)); } catch { }
            }
            finally
            {
                _eyeBusy = false;
                RefreshEyeTrackingLabel();
            }
        }

        private void OnEyeStateChanged() => PostUi(RefreshEyeTrackingLabel);

        private void RefreshEyeTrackingLabel()
        {
            if (_uiTimer == null && !_engineHooked) return;
            bool running;
            try { running = EyeIsRunning(); } catch { running = false; }
            _txtEyeTracking.Text = Loc.Get(running ? "deeper_player_btn_eye_tracking_stop" : "deeper_player_btn_eye_tracking_start");
        }

        // -- the time source the engine reads -------------------------------------------------

        /// <summary>WPF had three sources (audio player, VideoService, browser video); here the
        /// window already holds the clock for both modes, so one source reads it.</summary>
        private sealed class PlayerTimeSource : IPlaybackTimeSource
        {
            private readonly EnhancementPlayerWindow _w;
            public PlayerTimeSource(EnhancementPlayerWindow w) => _w = w;

            public event Action<double>? PlaybackTimeChanged;
            internal void Raise(double t) => PlaybackTimeChanged?.Invoke(t);

            public double GetCurrentTimeSeconds() => _w._currentSec;
            public double GetDurationSeconds() => _w._durationSec;
            public bool IsPlaying => _w._isPlaying;

            public void Seek(double seconds) => PostUi(() =>
            {
                if (_w._isVideoMode)
                {
                    if (!_w._videoNavigated) return;
                    _w._currentSec = Math.Max(0, seconds);
                    _ = _w._videoBrowser.InvokeScriptAsync(DeeperPreview.Invoke(
                        "l.currentTime=" + _w._currentSec.ToString("0.###", CultureInfo.InvariantCulture) + ";"));
                }
                else _w.AudioSeek(seconds);
            });

            public void Pause() => PostUi(() =>
            {
                if (_w._isVideoMode)
                {
                    if (_w._videoNavigated) _ = _w._videoBrowser.InvokeScriptAsync(DeeperPreview.Invoke("l.pause();"));
                    _w._isPlaying = false;
                    _w._txtPlayPauseGlyph.Text = "▶";
                    _w.UpdateStatusPill();
                }
                else _w.AudioPause();
            });

            public void Play() => PostUi(() =>
            {
                if (_w._isVideoMode)
                {
                    if (_w._videoNavigated) _ = _w._videoBrowser.InvokeScriptAsync(DeeperPreview.Invoke("l.play();"));
                }
                else _w.AudioPlay();
            });

            public PlaybackRect GetVideoRect()
            {
                try
                {
                    if (!_w._isVideoMode || !_w._videoBrowser.IsEffectivelyVisible) return PlaybackRect.Empty;
                    var b = _w._videoBrowser.Bounds;
                    if (b.Width <= 0 || b.Height <= 0) return PlaybackRect.Empty;
                    var scale = _w.RenderScaling <= 0 ? 1 : _w.RenderScaling;
                    var tl = _w._videoBrowser.PointToScreen(new Point(0, 0));
                    return new PlaybackRect(tl.X / scale, tl.Y / scale, b.Width, b.Height);
                }
                catch { return PlaybackRect.Empty; }
            }
        }

        /// <summary>The port's tracker as the engine's webcam. The tracker has no mouth-open
        /// signal, so mouth_open rules never fire on this head.</summary>
        internal sealed class TrackerWebcam : IEnhancementWebcam
        {
            internal static readonly TrackerWebcam Instance = new();
            public event Action? OnBlink { add => WebcamTracker.Instance.OnBlink += value; remove => WebcamTracker.Instance.OnBlink -= value; }
            public event Action? OnFaceLost { add => WebcamTracker.Instance.OnFaceLost += value; remove => WebcamTracker.Instance.OnFaceLost -= value; }
            public event Action? OnFaceFound { add => WebcamTracker.Instance.OnFaceFound += value; remove => WebcamTracker.Instance.OnFaceFound -= value; }
            public event Action? OnMouthOpen { add { } remove { } }

            private readonly Dictionary<Action<double, double>, Action<Point>> _gaze = new();
            public event Action<double, double>? OnGazeMove
            {
                add
                {
                    if (value == null) return;
                    Action<Point> h = pt => value(pt.X, pt.Y);
                    lock (_gaze) _gaze[value] = h;
                    WebcamTracker.Instance.OnGazeMove += h;
                }
                remove
                {
                    if (value == null) return;
                    Action<Point>? h;
                    lock (_gaze) { if (!_gaze.Remove(value, out h)) return; }
                    WebcamTracker.Instance.OnGazeMove -= h;
                }
            }
        }
    }
}
