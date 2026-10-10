using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Webcam;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>What the For You feed asks of the webcam tracker. The app's one is
    /// <see cref="GameWindow.TrackerFypEye"/> over <see cref="WebcamTracker"/>; tests pass a fake, so a
    /// test never opens a camera.</summary>
    internal interface IFypEye
    {
        bool IsRunning { get; }
        bool Calibrated { get; }
        /// <summary>The last start ended in an error (WPF WebcamTrackingState.Error), as opposed to "no camera".</summary>
        bool Faulted { get; }
        /// <summary>The calibrated monitor's origin (physical px) and its DPI scale.</summary>
        (double OriginX, double OriginY, double Scale) CalSpace { get; }
        event Action? OnBlink;
        event Action? OnEyesClosedLong;
        event Action<Point>? OnGazeMove;
        Task<bool> StartAsync();
        void Stop();
    }

    /// <summary>
    /// For You eye control (WPF 7.1.5 Services/Fyp/FypHostService.cs :1188-1437). Blink = swap one tile
    /// on the active page (the tile being LOOKED at when gaze mode is on and calibrated, otherwise a
    /// random one). Eyes held shut for 2 s = change the whole page.
    ///
    /// <para>Consent first: the same webcam consent every other call site uses, asked before the camera
    /// is touched; a refusal turns the setting back off and tells the page why. The page hears every
    /// step as an <c>eyeStatus</c> frame (reason: null | consent | starting | no-camera | error).</para>
    /// </summary>
    internal sealed partial class GameWindow
    {
        /// <summary>Seams (tests): the tracker, the consent ask, the calibration dialog.</summary>
        internal static Func<IFypEye> FypEyeSource = () => TrackerFypEye.Instance;
        internal static Func<Window, Task<bool>> FypEyeConsent = DefaultFypEyeConsent;
        internal static Func<Window, Task> FypEyeCalibrate = async owner => { await Windows.WebcamCalibrationWindow.ShowDialogWithRecalibrate(owner); };
        internal static Func<bool> FypEyeCalibrating = () => Windows.WebcamCalibrationWindow.IsShowing;

        /// <summary>OnGazeMove fires per processed frame (~30 Hz). The page only needs to know which
        /// TILE is looked at, so one frame in 100 ms is plenty.</summary>
        internal const int FypGazePostIntervalMs = 100;

        private IFypEye? _fypEye;
        private bool _fypEyeSubscribed;      // OnBlink + OnEyesClosedLong attached
        private bool _fypGazeSubscribed;     // OnGazeMove attached (needs eyeGaze + calibration)
        private bool _fypStartedWebcam;      // this window started the camera, so it stops it
        private bool _fypEyeEnabling;        // an enable sequence is in flight (re-entrancy guard)
        private DateTime _fypLastGazePostAt = DateTime.MinValue;

        /// <summary>The in-flight enable, for tests to await.</summary>
        internal Task FypEyeTask { get; private set; } = Task.CompletedTask;

        private static async Task<bool> DefaultFypEyeConsent(Window owner)
        {
            if (WebcamConsent.IsCurrent(CoreSettings.Current)) return true;
            await new Dialogs.WebcamConsentDialog().ShowDialogSafe(owner);
            return WebcamConsent.IsCurrent(CoreSettings.Current);
        }

        /// <summary>WPF EnableEyeControl: consent, camera, subscriptions, reporting each step. Fire and
        /// forget: the page-message handler never blocks on a camera open.</summary>
        private void EnableFypEyeControl()
        {
            if (_fypEyeEnabling) return;
            _fypEyeEnabling = true;
            FypEyeTask = EnableFypEyeControlAsync();
        }

        private async Task EnableFypEyeControlAsync()
        {
            try
            {
                // Off the web-message callback before anything modal or long-running.
                await Task.Yield();

                IFypEye? eye = null;
                try { eye = _fypEye ??= FypEyeSource(); } catch (Exception ex) { Log.Debug("[Game] fyp eye source: {E}", ex.Message); }
                if (eye == null) { FailFypEyeControl("no-camera"); return; }

                if (!await FypEyeConsent(this)) { FailFypEyeControl("consent"); return; }

                if (!eye.IsRunning)
                {
                    PostFypEyeStatus("starting");
                    // StartAsync opens the camera and builds the models off the UI thread.
                    if (!await eye.StartAsync())
                    {
                        FailFypEyeControl(eye.Faulted ? "error" : "no-camera");
                        return;
                    }
                    _fypStartedWebcam = true;
                }

                // The window may have closed, or the toggle may be back off, while the camera was
                // opening. Undo rather than leave an orphan camera running.
                if (IsClosedOrClosing || !CoreSettings.Current.FypEyeControl)
                {
                    DisableFypEyeControl();
                    return;
                }

                eye.OnBlink += OnFypEyeBlink;
                eye.OnEyesClosedLong += OnFypEyeClosedLong;
                _fypEyeSubscribed = true;
                SyncFypGazeSubscription();
                PostFypEyeStatus(null);
                Log.Information("[Game] fyp: eye control ON (gaze {Gaze})", _fypGazeSubscribed ? "on" : "off");
            }
            catch (Exception ex)
            {
                Log.Warning("[Game] fyp: eye control enable failed: {E}", ex.Message);
                FailFypEyeControl("error");
            }
            finally { _fypEyeEnabling = false; }
        }

        /// <summary>WPF FailEyeControl: the persisted setting goes back off, whatever got half-built is
        /// torn down, and the page hears why so its toggle resets with a reason.</summary>
        private void FailFypEyeControl(string reason)
        {
            try { CoreSettings.Current.FypEyeControl = false; CoreSettings.Save(); } catch { }
            DisableFypEyeControl();
            PostFypEyeStatus(reason);
        }

        /// <summary>WPF DisableEyeControl: unsubscribe everything and hand the camera back. Safe to call
        /// repeatedly and when nothing was ever enabled.</summary>
        private void DisableFypEyeControl()
        {
            var eye = _fypEye;
            try
            {
                if (eye != null)
                {
                    if (_fypEyeSubscribed)
                    {
                        eye.OnBlink -= OnFypEyeBlink;
                        eye.OnEyesClosedLong -= OnFypEyeClosedLong;
                    }
                    if (_fypGazeSubscribed) eye.OnGazeMove -= OnFypEyeGaze;
                }
            }
            catch (Exception ex) { Log.Debug("[Game] fyp: eye unsubscribe: {E}", ex.Message); }
            _fypEyeSubscribed = false;
            _fypGazeSubscribed = false;

            try { if (_fypStartedWebcam && eye?.IsRunning == true) eye.Stop(); }
            catch (Exception ex) { Log.Debug("[Game] fyp: webcam stop: {E}", ex.Message); }
            _fypStartedWebcam = false;
        }

        /// <summary>WPF SyncGazeSubscription: gaze is only wired when it can resolve a tile (eye control
        /// on, gaze mode on, AND a calibration loaded).</summary>
        private void SyncFypGazeSubscription()
        {
            var eye = _fypEye;
            var s = CoreSettings.Current;
            var want = _fypEyeSubscribed && eye != null && s.FypEyeControl && s.FypEyeGaze && eye.Calibrated;
            if (want == _fypGazeSubscribed) return;
            try
            {
                if (eye == null) { _fypGazeSubscribed = false; return; }
                if (want) eye.OnGazeMove += OnFypEyeGaze;
                else eye.OnGazeMove -= OnFypEyeGaze;
                _fypGazeSubscribed = want;
            }
            catch (Exception ex) { Log.Debug("[Game] fyp: gaze subscribe: {E}", ex.Message); }
        }

        /// <summary>WPF PostEyeStatus: what is actually true, so the page's toggles and status line
        /// render reality.</summary>
        private void PostFypEyeStatus(string? reason)
        {
            try
            {
                var s = CoreSettings.Current;
                var eye = _fypEye;
                Post(new
                {
                    type = "eyeStatus",
                    enabled = s.FypEyeControl,
                    gaze = s.FypEyeGaze,
                    running = eye?.IsRunning == true,
                    calibrated = eye?.Calibrated == true,
                    reason,
                });
            }
            catch (Exception ex) { Log.Debug("[Game] fyp: eyeStatus post: {E}", ex.Message); }
        }

        /// <summary>WPF RunGazeCalibration: the page's Calibrate button runs the gaze calibration over
        /// the feed, then gaze is re-wired and the page hears the new state.</summary>
        private async Task RunFypGazeCalibration()
        {
            try
            {
                if (FypEyeCalibrating()) return;
                if (_fypEye?.IsRunning != true)
                {
                    // The page disables the button without a running camera; belt and braces.
                    PostFypEyeStatus(null);
                    return;
                }
                ExitFypGhost();   // WPF :1371: a parked window cannot host a modal dot dance
                await FypEyeCalibrate(this);
                SyncFypGazeSubscription();
                PostFypEyeStatus(null);
            }
            catch (Exception ex) { Log.Warning("[Game] fyp: gaze calibration failed: {E}", ex.Message); }
        }

        // The tracker raises all three on the UI thread, which owns the web view.
        private void OnFypEyeBlink()
        {
            try { Post(new { type = "blink" }); }
            catch (Exception ex) { Log.Debug("[Game] fyp: blink post: {E}", ex.Message); }
        }

        private void OnFypEyeClosedLong()
        {
            try { Post(new { type = "eyesClosed" }); }
            catch (Exception ex) { Log.Debug("[Game] fyp: eyesClosed post: {E}", ex.Message); }
        }

        /// <summary>WPF OnEyeGaze: the gaze point as 0..1 of the web view, plus an <c>inside</c> flag so
        /// the page can ignore gaze that is off the window instead of pinning it to an edge tile.</summary>
        private void OnFypEyeGaze(Point p)
        {
            try
            {
                var now = DateTime.UtcNow;
                if ((now - _fypLastGazePostAt).TotalMilliseconds < FypGazePostIntervalMs) return;
                var eye = _fypEye;
                if (eye == null) return;

                var size = Web.Bounds.Size;
                PixelPoint origin;
                try { origin = Web.PointToScreen(new Point(0, 0)); } catch { return; }
                if (FypGaze.Normalize(p, eye.CalSpace, origin, RenderScaling, size) is not { } g) return;

                _fypLastGazePostAt = now;
                Post(new { type = "gaze", x = g.X, y = g.Y, inside = g.Inside });
            }
            catch (Exception ex) { Log.Debug("[Game] fyp: gaze post: {E}", ex.Message); }
        }

        /// <summary>The app's eye: <see cref="WebcamTracker"/>.</summary>
        internal sealed class TrackerFypEye : IFypEye
        {
            internal static readonly TrackerFypEye Instance = new();
            private static WebcamTracker T => WebcamTracker.Instance;
            public bool IsRunning => T.IsRunning;
            public bool Calibrated => T.Calibration != null;
            // WPF :1246: only WebcamTrackingState.Error reads "error"; a camera that would not open
            // (missing, denied, in use) reads "no-camera".
            public bool Faulted => T.LastError != null && !T.StartWasStopped && !T.StartFoundNoCamera;
            public (double OriginX, double OriginY, double Scale) CalSpace
            {
                get
                {
                    var mb = T.Calibration?.MonitorBounds;
                    var scale = mb is { DpiScale: > 0.25 and < 8.0 } ? mb.DpiScale : 1.0;
                    return (mb?.DeviceName != null ? mb.X : 0, mb?.DeviceName != null ? mb.Y : 0, scale);
                }
            }
            public event Action? OnBlink { add => T.OnBlink += value; remove => T.OnBlink -= value; }
            public event Action? OnEyesClosedLong { add => T.OnEyesClosedLong += value; remove => T.OnEyesClosedLong -= value; }
            public event Action<Point>? OnGazeMove { add => T.OnGazeMove += value; remove => T.OnGazeMove -= value; }
            public Task<bool> StartAsync() => T.StartAsync();
            public void Stop() => T.Stop();
        }
    }

    /// <summary>
    /// Gaze point to normalized web-view coordinates (WPF OnEyeGaze). The tracker emits DIPs local to
    /// the CALIBRATED monitor's top-left at THAT monitor's DPI, so: back to physical desktop pixels
    /// through the calibration's own scale and origin, then into the web view through the web view's
    /// own DPI. The two can differ on a mixed-DPI desktop.
    /// </summary>
    internal static class FypGaze
    {
        internal static (double X, double Y, bool Inside)? Normalize(Point gaze, (double OriginX, double OriginY, double Scale) cal,
            PixelPoint webOriginPx, double webScale, Size webSize)
        {
            if (webSize.Width <= 0 || webSize.Height <= 0) return null;
            var calScale = cal.Scale > 0 ? cal.Scale : 1.0;
            var physX = gaze.X * calScale + cal.OriginX;
            var physY = gaze.Y * calScale + cal.OriginY;
            var s = webScale <= 0 ? 1.0 : webScale;
            var nx = ((physX - webOriginPx.X) / s) / webSize.Width;
            var ny = ((physY - webOriginPx.Y) / s) / webSize.Height;
            var inside = nx >= 0 && nx <= 1 && ny >= 0 && ny <= 1;
            return (Math.Clamp(nx, 0, 1), Math.Clamp(ny, 0, 1), inside);
        }
    }
}
