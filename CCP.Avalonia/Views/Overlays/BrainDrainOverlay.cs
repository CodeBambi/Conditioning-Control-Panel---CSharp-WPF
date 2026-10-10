using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;   // ScreenList
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Compositor;
using Serilog;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// The Brain Drain VISUAL half: a haze over the desktop, one click-through window per targeted
    /// monitor, each drawing a blurred (and, with Melt, slowly warped) shrunk copy of that monitor
    /// over the real screen at the alpha the dial asks for. The Avalonia counterpart of WPF
    /// OverlayService.StartBrainDrainBlur / StopBrainDrainBlur / UpdateBrainDrainBlurOpacity +
    /// Services/Compositor/BrainDrainLayer.
    ///
    /// <para>Follows four settings, live: <c>BrainDrainBlurStrength</c> (0 = no picture at all),
    /// <c>BrainDrainMeltEnabled</c> (restarts the capture, as WPF: the melt flag is fixed for one
    /// run), <c>BrainDrainKeepPicturesClear</c> (the haze sinks under the app's own topmost windows)
    /// and <c>AllowOverlayCapture</c> (the capture affinity of the haze windows).</para>
    ///
    /// <para>Up only while the engine runs and is not paused, as WPF RefreshOverlays. The engine
    /// stop, a pause and a panic all take it down through <see cref="CloseAll"/>.</para>
    ///
    /// <para>Not ported: Linux. The haze needs a screen grab that leaves the app's own overlays out;
    /// an X11 root grab under a compositor returns the haze itself. <see cref="IsSupported"/> is
    /// false there, nothing is shown and the card greys its visual rows.</para>
    /// </summary>
    internal static class BrainDrainOverlay
    {
        private static readonly List<BrainDrainOverlayWindow> Windows = new();
        private static BrainDrainCapturePump? _pump;
        private static PixelRect[] _shownOn = Array.Empty<PixelRect>();
        private static Window? _hookedOwner;
        private static Visual? _host;
        private static INotifyPropertyChanged? _followed;
        private static DispatcherTimer? _keepClearTimer;
        private static bool _refused;

        /// <summary>Whether this platform can draw the haze at all.</summary>
        internal static bool IsSupported => BrainDrainCapturePump.IsSupported;

        /// <summary>The haze is on screen now (WPF OverlayService.BrainDrainVisualUp).</summary>
        internal static bool IsShowing => Windows.Count > 0;

        // A timed drain (WPF OverlayService.ShowOverlayTimed "braindrain" / "braindrain_melt"): held
        // up for its duration whatever the base feature says, then it lifts by itself.
        private static (int Intensity, bool Melt)? _timed;
        private static DispatcherTimer? _timedTimer;

        /// <summary>A timed drain is still in flight (WPF TimedBrainDrainActive).</summary>
        internal static bool TimedActive => _timed is not null;

        /// <summary>WPF ShowOverlayTimed: the haze for <paramref name="ms"/>, then down again unless
        /// the base feature wants it. False when nothing could be shown.</summary>
        internal static bool ShowTimed(Visual host, int intensity, bool melt, int ms)
        {
            if (BrainDrainVisualPolicy.IsSilent(intensity)) return false;
            _timedTimer?.Stop();
            _timed = (intensity, melt);
            Refresh(host);
            if (!IsShowing) { _timed = null; return false; }
            _timedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms)) };
            _timedTimer.Tick += (_, _) => EndTimed();
            _timedTimer.Start();
            return true;
        }

        internal static void EndTimed()
        {
            _timedTimer?.Stop();
            _timedTimer = null;
            if (_timed is null) return;
            _timed = null;
            RefreshFromSetting();
        }

        /// <summary>The strength the windows draw at right now (tests).</summary>
        internal static int CurrentIntensity { get; private set; }

        /// <summary>Tests: put windows up without the platform checks (headless has no click-through).</summary>
        internal static bool SkipPlatformChecksForTest;

        /// <summary>WPF RefreshOverlays' gate, same as the tint: the engine (or a session) runs and is not paused.</summary>
        private static bool ShouldShow()
            => App.Sessions?.IsPaused != true
               && (CoreSession.IsEngineRunningProvider is null || CoreSession.IsEngineRunning || App.Sessions?.IsRunning == true);

        /// <summary>Bring the haze in line with the settings. Idempotent: every handler just calls it.</summary>
        public static void Refresh(Visual host)
        {
            _host = host;
            var s = CoreSettings.Current;
            Follow(s);

            var baseWants = BrainDrainVisualPolicy.WantsBlur(s.BrainDrainEnabled, s.BrainDrainBlurStrength);
            // The base haze follows the engine. A timed drain (a keyword trigger, a bubble pop) is an
            // ad-hoc effect like a keyword flash: it shows with the engine off too, never while paused.
            var engineUp = ShouldShow();
            if (!engineUp) baseWants = false;
            var timedOk = _timed is not null && App.Sessions?.IsPaused != true;
            if ((!IsSupported && !SkipPlatformChecksForTest) || _refused || (!baseWants && !timedOk))
            {
                CloseWindows();
                return;
            }

            var screens = ScreenList.Enumerate(host);
            var primary = -1;
            for (var i = 0; i < screens.Count; i++) if (screens[i].IsPrimary) { primary = i; break; }
            var want = PinkFilterOverlay.ResolveScreenIndices(s.GlobalTargetMonitor, s.DualMonitorEnabled, screens.Count, primary);
            var bounds = want.Select(i => screens[i].Bounds).ToArray();
            if (bounds.Length == 0) { CloseAll(); return; }

            // The user's own loop wins: a timed drain only decides while the base feature is quiet.
            var intensity = baseWants ? s.BrainDrainBlurStrength : _timed!.Value.Intensity;
            var melt = baseWants ? s.BrainDrainMeltEnabled : _timed!.Value.Melt;

            // Same monitors, same variant: move the dial in place (WPF UpdateBrainDrainBlurOpacity).
            if (Windows.Count > 0 && _pump is { } live && live.Melt == melt && bounds.SequenceEqual(_shownOn))
            {
                SetIntensity(intensity);
                ApplyCaptureAffinity();
                ArmKeepClear(s.BrainDrainKeepPicturesClear);
                return;
            }

            CloseWindows();
            _host = host;

            var interval = TimeSpan.FromMilliseconds(1000.0 / BrainDrainLayerRules.Fps(s.BrainDrainHighRefresh));
            _pump = new BrainDrainCapturePump(BrainDrainLayerRules.Downscale, interval, melt, bounds,
                BrainDrainLayerRules.SigmaFor(intensity), BrainDrainLayerRules.MeltAmplitudeFor(intensity));

            HookOwner(TopLevel.GetTopLevel(host) as Window);
            foreach (var i in want)
            {
                var w = new BrainDrainOverlayWindow(_pump, screens[i].Bounds, interval);
                w.PlaceOn(screens[i]);
                w.Show();
                if (!SkipPlatformChecksForTest && !Accept(w)) { CloseWindows(); return; }
                Windows.Add(w);
            }
            _shownOn = bounds;
            SetIntensity(intensity);
            ApplyCaptureAffinity();
            ArmKeepClear(s.BrainDrainKeepPicturesClear);
            Log.Information("Brain Drain haze up on {Count} screen(s): strength {Intensity}%, melt {Melt}",
                Windows.Count, intensity, melt);
        }

        private static void SetIntensity(int intensity)
        {
            CurrentIntensity = intensity;
            _pump?.SetBlur(BrainDrainLayerRules.SigmaFor(intensity), BrainDrainLayerRules.MeltAmplitudeFor(intensity));
            var alpha = BrainDrainLayerRules.AlphaFor(intensity);
            foreach (var w in Windows) w.SetDrawAlpha(alpha);
        }

        /// <summary>Takes the haze down: the engine stop, a pause, a panic and the shell closing.
        /// Never blocks: the capture thread frees its own handles.</summary>
        public static void CloseAll()
        {
            _timedTimer?.Stop();
            _timedTimer = null;
            _timed = null;
            CloseWindows();
        }

        private static void CloseWindows()
        {
            _keepClearTimer?.Stop();
            _keepClearTimer = null;
            var pump = _pump;
            _pump = null;
            pump?.Shutdown();
            foreach (var w in Windows)
            {
                try { w.Close(); }
                catch (Exception ex) { Log.Debug("Brain Drain: failed to close a haze window: {E}", ex.Message); }
            }
            if (Windows.Count > 0) Log.Information("Brain Drain haze down");
            Windows.Clear();
            _shownOn = Array.Empty<PixelRect>();
            if (_hookedOwner is not null) { _hookedOwner.Closed -= OnOwnerClosed; _hookedOwner = null; }
        }

        // ---- settings follow (WPF OverlayService.OnSettingsPropertyChanged) --------------------------

        private static void Follow(INotifyPropertyChanged settings)
        {
            if (ReferenceEquals(settings, _followed)) return;
            if (_followed is not null) _followed.PropertyChanged -= OnSettingChanged;
            _followed = settings;
            settings.PropertyChanged += OnSettingChanged;
        }

        private static void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(Models.AppSettings.BrainDrainEnabled):
                case nameof(Models.AppSettings.BrainDrainBlurStrength):
                case nameof(Models.AppSettings.BrainDrainMeltEnabled):
                case nameof(Models.AppSettings.BrainDrainKeepPicturesClear):
                case nameof(Models.AppSettings.AllowOverlayCapture):
                case nameof(Models.AppSettings.BrainDrainHighRefresh):
                    break;
                default: return;
            }
            if (!ReferenceEquals(sender, CoreSettings.Current)) return;   // a swapped-out settings object
            if (Dispatcher.UIThread.CheckAccess()) RefreshFromSetting();
            else Dispatcher.UIThread.Post(RefreshFromSetting);
        }

        private static void RefreshFromSetting()
        {
            try { if (_host is { } host) Refresh(host); }
            catch (Exception ex) { Log.Debug("Brain Drain: live refresh failed: {E}", ex.Message); }
        }

        // ---- the two safety refusals (as the tint) ---------------------------------------------------

        private static bool Accept(BrainDrainOverlayWindow w)
        {
            if (!X11Overlay.SetClickThrough(w, true))
            {
                Log.Warning("Brain Drain: this platform cannot make the haze click-through, so it is not shown");
                _refused = true;
                try { w.Close(); } catch { }
                return false;
            }
            if (w.ActualTransparencyLevel == WindowTransparencyLevel.None)
            {
                Log.Warning("Brain Drain: per-pixel transparency was refused, so the haze is not shown");
                _refused = true;
                try { w.Close(); } catch { }
                return false;
            }
            return true;
        }

        private static void HookOwner(Window? owner)
        {
            if (owner is null || ReferenceEquals(owner, _hookedOwner)) return;
            if (_hookedOwner is not null) _hookedOwner.Closed -= OnOwnerClosed;
            _hookedOwner = owner;
            owner.Closed += OnOwnerClosed;
        }

        private static void OnOwnerClosed(object? sender, EventArgs e) => CloseAll();

        // ---- capture exclusion (WPF OverlayCaptureAffinity) ------------------------------------------

        private const uint WdaNone = 0x0000, WdaExcludeFromCapture = 0x0011;

        /// <summary>WPF OverlayCaptureAffinity.Apply: the haze is hidden from screenshots and
        /// recordings unless the user opted in (<c>AllowOverlayCapture</c>, default off). Windows only
        /// (SetWindowDisplayAffinity); on Linux there is no per-window capture exclusion, so this is a
        /// no-op there and the haze shows in captures. Safe to call repeatedly.</summary>
        internal static void ApplyCaptureAffinity()
        {
            if (!OperatingSystem.IsWindows()) return;
            var allow = false;
            try { allow = CoreSettings.Current.AllowOverlayCapture; } catch { }
            foreach (var w in Windows)
            {
                try
                {
                    var hwnd = w.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                    if (hwnd != IntPtr.Zero) SetWindowDisplayAffinity(hwnd, AffinityFor(allow));
                }
                catch (Exception ex) { Log.Debug("Brain Drain: capture affinity failed: {E}", ex.Message); }
            }
        }

        /// <summary>The affinity for a setting value: opted in = visible in captures.</summary>
        internal static uint AffinityFor(bool allowCapture) => allowCapture ? WdaNone : WdaExcludeFromCapture;

        // ---- keep pictures clear (WPF OverlayService.SinkBrainDrainUnderOwnWindows) ------------------

        private static void ArmKeepClear(bool on)
        {
            if (!OperatingSystem.IsWindows()) return;
            if (!on)
            {
                if (_keepClearTimer is null) return;
                _keepClearTimer.Stop();
                _keepClearTimer = null;
                // Off: the haze goes back over everything (WPF's forced pass raises it).
                foreach (var w in Windows) { try { X11Overlay.Raise(w); } catch { } }
                return;
            }
            if (_keepClearTimer is not null) return;
            _keepClearTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };   // WPF reconciler, 2 Hz
            _keepClearTimer.Tick += (_, _) => { try { SinkUnderOwnWindows(); } catch (Exception ex) { Log.Debug("Brain Drain keep clear failed: {E}", ex.Message); } };
            _keepClearTimer.Start();
            try { SinkUnderOwnWindows(); } catch { }
        }

        private static void SinkUnderOwnWindows()
        {
            if (!OperatingSystem.IsWindows() || Windows.Count == 0) return;
            var drains = new HashSet<IntPtr>();
            foreach (var w in Windows)
            {
                var h = w.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (h != IntPtr.Zero) drains.Add(h);
            }
            if (drains.Count == 0) return;

            uint ownPid = (uint)Environment.ProcessId;
            var band = new List<BrainDrainKeepClear.BandEntry>();
            var drainOrder = new List<IntPtr>();
            // The topmost band only: it ends at the first non-topmost window. Capped.
            int steps = 0;
            for (var h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero && steps++ < 1024; h = GetWindow(h, GwHwndNext))
            {
                if ((Win32Overlay.GetWindowLong(h, GwlExStyle) & Win32Overlay.WsExTopmost) == 0) break;
                if (!IsWindowVisible(h)) continue;
                bool drain = drains.Contains(h);
                GetWindowThreadProcessId(h, out uint pid);
                band.Add(new BrainDrainKeepClear.BandEntry(h, pid == ownPid, drain));
                if (drain) drainOrder.Add(h);
            }

            var anchor = BrainDrainKeepClear.AnchorIfNeeded(band);
            if (anchor == IntPtr.Zero) return;
            foreach (var d in drainOrder)
            {
                SetWindowPos(d, anchor, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
                anchor = d;   // keep the haze windows stacked together, in their current order
            }
        }

        private const int GwlExStyle = -20;
        private const uint GwHwndNext = 2, SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoActivate = 0x10;

        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
        [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }

    /// <summary>
    /// One monitor's haze: a transparent, topmost, click-through window whose only child is an
    /// <see cref="FxSurface"/> at a quarter of the screen's pixels (the capture is a quarter size
    /// too, so nothing is lost). A <see cref="FrameClock"/> at the capture cadence takes the pump's
    /// newest frame and repaints only when there is one or the dial moved (WPF #853: a repeat of
    /// the frame already on screen is not a repaint). No <c>Effect</c> anywhere: the blur is done
    /// on the small capture, on the pump's thread.
    /// </summary>
    internal sealed class BrainDrainOverlayWindow : Window
    {
        private static readonly SKSamplingOptions Linear = new(SKFilterMode.Linear, SKMipmapMode.None);

        private readonly BrainDrainCapturePump? _pump;
        private readonly PixelRect _captureBounds;
        private readonly FxSurface _surface = new() { ResolutionScale = 0.25 };
        private readonly FrameClock _clock;
        private readonly SKPaint _drawPaint = new();
        private SKImage? _frame;          // UI-thread-owned once taken
        private byte _drawAlpha = 255;
        private bool _dirty = true;

        /// <summary>Frames this window has drawn (tests).</summary>
        internal int FramesDrawn { get; private set; }
        internal byte DrawAlpha => _drawAlpha;
        internal FxSurface Surface => _surface;

        public BrainDrainOverlayWindow() : this(null, default, TimeSpan.FromMilliseconds(33)) { }   // --render-all

        public BrainDrainOverlayWindow(BrainDrainCapturePump? pump, PixelRect captureBounds, TimeSpan interval)
        {
            _pump = pump;
            _captureBounds = captureBounds;
            SystemDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            CanResize = false;
            Focusable = false;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = 640;
            Height = 400;
            Content = _surface;
            _surface.PaintSurface += OnPaint;
            _clock = new FrameClock(this) { Interval = interval };
            _clock.Tick += (_, _) => Step();
            Opened += (_, _) => _clock.Start();
            Closed += (_, _) =>
            {
                _clock.Stop();
                _frame?.Dispose();
                _frame = null;
                _drawPaint.Dispose();
            };
        }

        public void SetDrawAlpha(byte alpha)
        {
            if (_drawAlpha == alpha) return;
            _drawAlpha = alpha;
            _dirty = true;      // the dial moved: repaint even with no new frame
        }

        /// <summary>One beat: take the pump's newest frame, repaint if anything changed.</summary>
        internal void Step()
        {
            if (_pump is not null && _pump.TryTakeFrame(_captureBounds, out var fresh) && fresh is not null)
            {
                _frame?.Dispose();   // UI-thread-owned: the pump is provably not looking at it
                _frame = fresh;
                _dirty = true;
            }
            if (!_dirty || _frame is null) return;
            _dirty = false;
            _surface.Redraw();
        }

        private void OnPaint(object? sender, FxPaintEventArgs e)
        {
            if (_frame is null) return;
            // Alpha mix: the real screen always ghosts through (never fully opaque).
            _drawPaint.Color = SKColors.White.WithAlpha(_drawAlpha);
            e.Canvas.DrawImage(_frame, new SKRect(0, 0, e.Info.Width, e.Info.Height), Linear, _drawPaint);
            FramesDrawn++;
        }

        /// <summary>Fills exactly one monitor.</summary>
        public void PlaceOn(Screen screen)
        {
            var b = screen.Bounds;
            var scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
            Position = new PixelPoint(b.X, b.Y);
            Width = b.Width / scale;
            Height = b.Height / scale;
        }
    }
}
