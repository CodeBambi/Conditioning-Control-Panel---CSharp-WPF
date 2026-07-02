using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.ComponentModel;
using ConditioningControlPanel.Helpers;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Service that manages screen overlays: Pink Filter and Spiral
/// </summary>
public class OverlayService : IDisposable
{
    private readonly List<Window> _pinkFilterWindows = new();

    // Ad-hoc "timed" overlays (ShowOverlayTimed, e.g. dashboard trigger bubbles) share the
    // persistent pink/spiral windows. These counters mark a timed overlay as in-flight so the
    // reconcile loops (RefreshOverlays / UpdateOverlays) don't tear it down a tick later just
    // because the persistent feature is off. Decremented when the timed overlay's hide fires.
    private int _timedPinkHolds;
    private int _timedSpiralHolds;
    // Sustained holds: an overlay shown via ShowOverlaySustained (voice "go pink"/"spiral", Deeper
    // region bands) has no hide timer, so — like the timed holds above — the periodic reconcilers
    // (RefreshOverlays / UpdateOverlays) must NOT tear it down just because the persistent feature
    // setting is off. Bool, not a counter: ShowOverlaySustained is idempotent (the ad-hoc paths
    // early-return on a non-empty window list), so a repeat show + single hide must still release it.
    private bool _sustainedPinkHeld;
    private bool _sustainedSpiralHeld;

    public OverlayService()
    {
        // Subscribe to settings changes if App.Settings.Current is available
        if (App.Settings?.Current != null)
        {
            App.Settings.Current.PropertyChanged += CurrentSettings_PropertyChanged;
        }
    }
    private readonly List<Window> _spiralWindows = new();
    private readonly List<MediaElement> _spiralMediaElements = new();
    private readonly List<Window> _brainDrainBlurWindows = new();
    private bool _isRunning;
    private DispatcherTimer? _updateTimer;
    private DispatcherTimer? _gifLoopTimer;
    private bool _isDisposed;
    private bool _isGifSpiral;
    private string _spiralPath = "";
    private Dictionary<MediaElement, DateTime> _mediaStartTimes = new();
    private double _lastAppliedPinkOpacity = -1;
    private double _lastAppliedSpiralOpacity = -1;
    // Deeper opacity-ramp override. When set, a Deeper enhancement owns this
    // overlay's opacity for a ramped band; the 500ms settings-sync
    // (UpdatePinkFilterOpacity / UpdateSpiralOpacity) must not stomp it.
    // Normalized 0..1 (spiral applies its own ×0.1 reduction on top).
    private double? _rampPinkOpacity;
    private double? _rampSpiralOpacity;
    private double? _rampBrainDrainOpacity;
    private int _consecutiveTopmostLossCount;
    // Recreate-overlays backoff. Destroying + recreating every layered overlay window on a 3s cadence
    // is a GDI/composition-surface churn engine (feeds the "not enough quota" exhaustion, and the
    // render-thread close/create is the freeze class). If a few recreations don't win topmost back,
    // stop recreating and just keep forcing z-order — recreation isn't helping and only burns surfaces.
    // Reset to 0 whenever topmost is regained, so a genuinely transient loss can still recreate later.
    private int _recreateAttempts;
    private const int MaxRecreateAttempts = 3;
    // Tick counter for periodic topmost-layer "kick". The 500ms timer drives this;
    // every ~5s (10 ticks) we re-issue HWND_TOPMOST even if the WS_EX_TOPMOST flag
    // is set, because Windows can reorder within the topmost layer (fullscreen
    // video, OS notifications, browser overlays) without ever clearing the flag —
    // that's the case behind the "spiral disappeared mid-session, toggle off/on
    // brings it back" reports.
    private int _topmostKickTickCounter;

    // GIF frame animation fields
    private readonly List<System.Windows.Controls.Image> _spiralGifImages = new();
    private List<BitmapSource> _spiralGifFrames = new();
    private int _currentGifFrameIndex;
    // Decoded-frame cache keyed by spiral path: re-decoding the GIF on the UI thread froze
    // everything for ~1s each time chaos re-showed the spiral. Frames are frozen → safe to reuse.
    private List<BitmapSource> _spiralFramesCache = new();
    private string _spiralFramesCacheKey = "";
    private TimeSpan _spiralFramesCacheDelay = TimeSpan.FromMilliseconds(50);
    private TimeSpan _gifFrameDelay = TimeSpan.FromMilliseconds(50);
    private DispatcherTimer? _gifFrameTimer;

    public bool IsRunning => _isRunning;

    /// <summary>
    /// When true, overlay level checks are bypassed (e.g. for remote control commands).
    /// </summary>
    public bool BypassLevelCheck { get; set; }

    // Legacy P/Invoke declarations (kept for compatibility)
    private const int SRCCOPY = 0x00CC0020;
    
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSrc, int xSrc, int ySrc, int dwRop);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, int dwRop);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(IntPtr hdc, int iStretchMode);

    private const int HALFTONE = 4;

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
    
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);
    
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
    
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);
    
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);
    
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int Left, Right, Top, Bottom;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    private const int ACCENT_ENABLE_BLURBEHIND = 3;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
    private const int WCA_ACCENT_POLICY = 19;

            private string GetSpiralPath()
            {
                var settings = App.Settings.Current;
                
                if (!string.IsNullOrEmpty(settings.SpiralPath) && File.Exists(settings.SpiralPath))
                {
                    return settings.SpiralPath;
                }
                
                return ModResourceResolver.ResolveUri("spiral.gif");
            }
    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;

        DispatcherHelper.RunOnUISync(() =>
        {
            var settings = App.Settings.Current;

            if (settings.PinkFilterEnabled)
            {
                StartPinkFilter();
            }

            var spiralPath = GetSpiralPath();
            if (settings.SpiralEnabled && !string.IsNullOrEmpty(spiralPath))
            {
                _spiralPath = spiralPath;
                StartSpiral();
            }

            if (settings.BrainDrainEnabled && settings.IsLevelUnlocked(70))
            {
                StartBrainDrainBlur((int)settings.BrainDrainIntensity);
            }

            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _updateTimer.Tick += UpdateOverlays;
            _updateTimer.Start();
        });

        App.Logger?.Information("OverlayService started");
    }

    public void Stop()
    {
        _isRunning = false;

        try
        {
            _updateTimer?.Stop();
            _updateTimer = null;

            StopPinkFilter();
            StopSpiral();
            StopBrainDrainBlur();
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Error during OverlayService Stop");
        }

        App.Logger?.Information("OverlayService stopped");
    }

    public void RefreshOverlays()
    {
        if (!_isRunning) return;

        DispatcherHelper.RunOnUISync(() =>
        {
            var settings = App.Settings.Current;

            if (settings.PinkFilterEnabled)
            {
                if (_pinkFilterWindows.Count == 0)
                    StartPinkFilter();
                else
                    UpdatePinkFilterOpacity();
            }
            else if (_timedPinkHolds == 0 && !_sustainedPinkHeld)   // don't kill an in-flight timed/sustained pink overlay
            {
                StopPinkFilter();
            }

            var spiralPath = GetSpiralPath();
            if (settings.SpiralEnabled && !string.IsNullOrEmpty(spiralPath))
            {
                _spiralPath = spiralPath;
                if (_spiralWindows.Count == 0)
                    StartSpiral();
                else
                    UpdateSpiralOpacity();
            }
            else if (_timedSpiralHolds == 0 && !_sustainedSpiralHeld)   // don't kill an in-flight timed/sustained spiral overlay
            {
                StopSpiral();
            }

            // Handle Brain Drain via its dedicated refresh state method
            RefreshBrainDrainState();
        });

        App.Logger?.Debug("Overlays refreshed - Pink: {Pink}, Spiral: {Spiral}, BrainDrain: {BrainDrain}",
            _pinkFilterWindows.Count > 0, _spiralWindows.Count > 0, _brainDrainBlurWindows.Count > 0);
    }

    /// <summary>
    /// Briefly doubles the intensity of all active overlays for ~1 second, then restores.
    /// </summary>
    public void PulseOverlays()
    {
        if (!_isRunning) return;

        DispatcherHelper.RunOnUISync(() =>
        {
            var settings = App.Settings.Current;
            var hasPink = settings.PinkFilterEnabled && _pinkFilterWindows.Count > 0;
            var hasSpiral = settings.SpiralEnabled && _spiralWindows.Count > 0;
            var hasBrainDrain = settings.BrainDrainEnabled && _brainDrainBlurWindows.Count > 0;

            if (!hasPink && !hasSpiral && !hasBrainDrain) return;

            // Double the intensity
            if (hasPink)
            {
                var boosted = Math.Min(settings.PinkFilterOpacity * 2, 100);
                var alpha = (byte)(boosted / 100.0 * 255);
                var (fr, fg, fb) = GetFilterRgb();
                foreach (var window in _pinkFilterWindows)
                {
                    if (window.Content is Border border &&
                        border.Background is System.Windows.Media.SolidColorBrush brush)
                    {
                        brush.Color = System.Windows.Media.Color.FromArgb(alpha, fr, fg, fb);
                    }
                }
                _lastAppliedPinkOpacity = -1;
            }

            if (hasSpiral)
            {
                var boostedOpacity = Math.Min((settings.SpiralOpacity / 100.0) * 0.1 * 2, 1.0);
                foreach (var image in _spiralGifImages)
                    image.Opacity = boostedOpacity;
                foreach (var media in _spiralMediaElements)
                    media.Opacity = boostedOpacity;
                _lastAppliedSpiralOpacity = -1;
            }

            if (hasBrainDrain)
            {
                var boostedIntensity = Math.Min(_currentBrainDrainIntensity * 2, 200);
                double blurRadius = boostedIntensity * 0.4;
                foreach (var img in _brainDrainImages.Values)
                {
                    if (img.Effect is System.Windows.Media.Effects.BlurEffect blur)
                        blur.Radius = blurRadius;
                }
            }

            // Restore after 1 second
            Task.Delay(1000).ContinueWith(_ =>
            {
                try
                {
                    DispatcherHelper.RunOnUISync(() =>
                    {
                        if (hasPink) UpdatePinkFilterOpacity();
                        if (hasSpiral) UpdateSpiralOpacity();
                        if (hasBrainDrain) UpdateBrainDrainBlurOpacity(_currentBrainDrainIntensity);
                    });
                }
                catch { /* Window may have closed */ }
            });
        });

        App.Logger?.Debug("Overlay pulse triggered");
    }

    /// <summary>
    /// Restart all overlays when dual monitor setting changes.
    /// Windows need to be recreated to match the new monitor setup.
    /// </summary>
    public void RefreshForDualMonitorChange()
    {
        if (!_isRunning) return;

        DispatcherHelper.RunOnUISync(() =>
        {
            var settings = App.Settings.Current;

            // Stop and restart pink filter if enabled
            if (settings.PinkFilterEnabled)
            {
                StopPinkFilter();
                StartPinkFilter();
            }

            // Stop and restart spiral if enabled
            var spiralPath = GetSpiralPath();
            if (settings.SpiralEnabled && !string.IsNullOrEmpty(spiralPath))
            {
                StopSpiral();
                _spiralPath = spiralPath;
                StartSpiral();
            }

            // Stop and restart brain drain if enabled
            if (settings.BrainDrainEnabled && settings.IsLevelUnlocked(70))
            {
                StopBrainDrainBlur();
                StartBrainDrainBlur((int)settings.BrainDrainIntensity);
            }
        });

        App.Logger?.Information("Overlays refreshed for dual monitor change - DualMonitor: {Enabled}",
            App.Settings.Current.DualMonitorEnabled);
    }

    private void UpdateOverlays(object? sender, EventArgs e)
    {
        var settings = App.Settings.Current;

        if (settings.PinkFilterEnabled && _pinkFilterWindows.Count == 0)
        {
            StartPinkFilter();
        }
        else if (!settings.PinkFilterEnabled && _pinkFilterWindows.Count > 0 && _timedPinkHolds == 0 && !_sustainedPinkHeld)
        {
            StopPinkFilter();
        }
        else if (_pinkFilterWindows.Count > 0)
        {
            UpdatePinkFilterOpacity();
        }

        var spiralPath = GetSpiralPath();
        if (settings.SpiralEnabled && !string.IsNullOrEmpty(spiralPath) && _spiralWindows.Count == 0)
        {
            _spiralPath = spiralPath;
            StartSpiral();
        }
        else if (!settings.SpiralEnabled && _spiralWindows.Count > 0 && _timedSpiralHolds == 0 && !_sustainedSpiralHeld)
        {
            StopSpiral();
        }
        else if (_spiralWindows.Count > 0)
        {
            UpdateSpiralOpacity();
        }

        bool needed = ReassertZOrder();
        if (needed)
        {
            _consecutiveTopmostLossCount++;
            if (_consecutiveTopmostLossCount >= 6) // 6 x 500ms = 3 seconds of continuous loss
            {
                _consecutiveTopmostLossCount = 0;
                if (Services.UI.DisplayChangeCoordinator.SpawnsSuppressed)
                {
                    // A monitor/DPI change is settling — topmost loss is expected and transient. Just
                    // reassert; don't tear down + recreate windows into the composition rebuild storm,
                    // and don't spend the recreate budget on it.
                    ReassertZOrder(force: true);
                }
                else if (_recreateAttempts < MaxRecreateAttempts)
                {
                    _recreateAttempts++;
                    RecreateOverlays();
                }
                else
                {
                    // Backed off: recreation didn't win topmost back after several tries. Keep forcing
                    // z-order instead of churning layered surfaces every 3s (freeze cluster #431/#451).
                    ReassertZOrder(force: true);
                }
            }
        }
        else
        {
            _consecutiveTopmostLossCount = 0;
            _recreateAttempts = 0; // topmost regained — allow recreation to help again on a future loss
        }

        // Periodic unconditional kick to handle in-layer reordering even when the
        // WS_EX_TOPMOST flag is technically still set on our windows.
        _topmostKickTickCounter++;
        if (_topmostKickTickCounter >= 10) // 10 x 500ms = 5 seconds
        {
            _topmostKickTickCounter = 0;
            ReassertZOrder(force: true);
            ReassertBounds();   // heal mixed-DPI size drift too (#457)
        }
    }

    #region Pink Filter

    private static (byte R, byte G, byte B) GetFilterRgb()
    {
        return App.Mods?.GetFilterColorRgb() ?? (255, 105, 180);
    }

    /// <summary>
    /// Ad-hoc one-shot overlay used by Deeper enhancement Effects. Bypasses the
    /// per-overlay enabled/disabled settings flags so creator content can fire
    /// any overlay regardless of how the user has the live overlay system
    /// configured. Auto-dismisses after <paramref name="durationMs"/> via a
    /// <see cref="DispatcherTimer"/> (NOT Task.Delay — see CLAUDE.md known
    /// issue 6 about fire-and-forget Tasks at app shutdown).
    /// </summary>
    public void ShowOverlayTimed(string kind, int durationMs, double opacity)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        int opacityPercent = (int)Math.Clamp(opacity * 100.0, 0, 100);
        int safeDurationMs = Math.Max(50, durationMs);

        Action? show = kind switch
        {
            "pink_filter" => () => ShowPinkFilterAdHoc(opacityPercent),
            "spiral"      => () => ShowSpiralAdHoc(),
            "braindrain"  => () => StartBrainDrainBlur(Math.Max(1, opacityPercent)),
            _ => null
        };

        Action? hide = kind switch
        {
            "pink_filter" => () => StopPinkFilter(),
            "spiral"      => () => StopSpiral(),
            "braindrain"  => () => StopBrainDrainBlur(),
            _ => null
        };

        if (show == null || hide == null)
        {
            App.Logger?.Debug("ShowOverlayTimed: unknown kind {Kind}", kind);
            return;
        }

        Action runShow = () =>
        {
            try
            {
                // Mark the timed overlay in-flight so the reconcile loops leave it alone.
                if (kind == "pink_filter") _timedPinkHolds++;
                else if (kind == "spiral") _timedSpiralHolds++;
                show();
            }
            catch (Exception ex) { App.Logger?.Debug("ShowOverlayTimed show: {E}", ex.Message); }
        };
        if (dispatcher.CheckAccess()) runShow();
        else dispatcher.Invoke(runShow);

        var hideTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(safeDurationMs)
        };
        hideTimer.Tick += (_, _) =>
        {
            hideTimer.Stop();
            try
            {
                var settings = App.Settings.Current;
                // Release the hold; only actually tear down when no other timed overlay holds it
                // AND the persistent feature isn't keeping it on (then the reconciler owns it).
                if (kind == "pink_filter")
                {
                    if (_timedPinkHolds > 0) _timedPinkHolds--;
                    if (_timedPinkHolds == 0 && !settings.PinkFilterEnabled) hide();
                }
                else if (kind == "spiral")
                {
                    if (_timedSpiralHolds > 0) _timedSpiralHolds--;
                    if (_timedSpiralHolds == 0 && !settings.SpiralEnabled) hide();
                }
                else
                {
                    hide();
                }
            }
            catch (Exception ex) { App.Logger?.Debug("ShowOverlayTimed hide: {E}", ex.Message); }
        };
        hideTimer.Start();
    }

    /// <summary>
    /// Band-mode counterpart to <see cref="ShowOverlayTimed"/> for Deeper Region-mode
    /// effects. Shows the overlay with no hide timer; the engine reconciler is
    /// responsible for calling <see cref="HideOverlaySustained"/> on band exit.
    /// Idempotent — calling twice with the same kind is a no-op (the underlying
    /// ad-hoc paths already early-return when their window list is non-empty).
    /// </summary>
    public void ShowOverlaySustained(string kind, double opacity)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        int opacityPercent = (int)Math.Clamp(opacity * 100.0, 0, 100);

        Action? show = kind switch
        {
            "pink_filter" => () => ShowPinkFilterAdHoc(opacityPercent),
            "spiral"      => () => ShowSpiralAdHoc(),
            "braindrain"  => () => StartBrainDrainBlur(Math.Max(1, opacityPercent)),
            _ => null
        };

        if (show == null)
        {
            App.Logger?.Debug("ShowOverlaySustained: unknown kind {Kind}", kind);
            return;
        }

        Action runShow = () =>
        {
            try
            {
                show();
                // Mark it held so the periodic reconcilers (RefreshOverlays / UpdateOverlays) leave it
                // alone — without this they tore the unguarded ad-hoc window down on the next tick because
                // the persistent feature setting was off ("go pink flashes then immediately goes away").
                // Both show() and the reconcilers run on this one UI thread, so they can't interleave;
                // gate on a window actually appearing so a no-op show (e.g. spiral with no asset) doesn't
                // leave a stale hold that would later block a legitimate teardown.
                if (kind == "pink_filter") _sustainedPinkHeld = _pinkFilterWindows.Count > 0;
                else if (kind == "spiral") _sustainedSpiralHeld = _spiralWindows.Count > 0;
            }
            catch (Exception ex) { App.Logger?.Debug("ShowOverlaySustained show: {E}", ex.Message); }
        };
        if (dispatcher.CheckAccess()) runShow();
        else dispatcher.Invoke(runShow);
    }

    /// <summary>
    /// Hides an overlay shown via <see cref="ShowOverlaySustained"/>. Idempotent.
    /// </summary>
    public void HideOverlaySustained(string kind)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        // Release the sustained hold, then only actually tear the overlay down when nothing else is
        // keeping it on (a timed overlay still holding it, or the persistent feature setting). Mirrors
        // the timed hide-timer's release discipline so a sustained exit can't stomp a co-active owner.
        var settings = App.Settings.Current;
        Action? hide = kind switch
        {
            "pink_filter" => () => { _sustainedPinkHeld = false; if (_timedPinkHolds == 0 && !settings.PinkFilterEnabled) StopPinkFilter(); },
            "spiral"      => () => { _sustainedSpiralHeld = false; if (_timedSpiralHolds == 0 && !settings.SpiralEnabled) StopSpiral(); },
            "braindrain"  => () => StopBrainDrainBlur(),
            _ => null
        };

        if (hide == null) return;

        Action runHide = () =>
        {
            try { hide(); }
            catch (Exception ex) { App.Logger?.Debug("HideOverlaySustained: {E}", ex.Message); }
        };
        if (dispatcher.CheckAccess()) runHide();
        else dispatcher.Invoke(runHide);
    }

    /// <summary>
    /// Live-updates the opacity of an overlay shown via <see cref="ShowOverlaySustained"/>.
    /// Used by Deeper enhancement opacity ramps to interpolate a sustained overlay's
    /// opacity across a region. <paramref name="opacity"/> is normalized 0..1 (spiral
    /// applies its own ×0.1 reduction, matching the global spiral path). While a ramp
    /// is active the 500ms settings-sync leaves this overlay alone so it isn't stomped.
    /// Only pink_filter and spiral support ramping; other kinds are ignored.
    /// </summary>
    public void SetSustainedOverlayOpacity(string kind, double opacity)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        opacity = Math.Clamp(opacity, 0, 1);

        Action apply = () =>
        {
            try
            {
                switch (kind)
                {
                    case "pink_filter":
                        _rampPinkOpacity = opacity;
                        ApplyPinkOpacityDirect(opacity);
                        break;
                    case "spiral":
                        _rampSpiralOpacity = opacity;
                        ApplySpiralOpacityDirect(opacity);
                        break;
                    case "braindrain":
                        // Brain Drain ramps via blur-intensity, not alpha. Map the normalized
                        // 0..1 ramp to an intensity the same way the band's start action does
                        // (StartBrainDrainBlur uses opacity*100), so 0→max actually deepens the blur.
                        _rampBrainDrainOpacity = opacity;
                        UpdateBrainDrainBlurOpacity(Math.Max(1, (int)Math.Round(opacity * 100)));
                        break;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("SetSustainedOverlayOpacity: {E}", ex.Message); }
        };
        if (dispatcher.CheckAccess()) apply();
        else dispatcher.Invoke(apply);
    }

    private void ApplyPinkOpacityDirect(double opacity)
    {
        var (fr, fg, fb) = GetFilterRgb();
        byte a = (byte)Math.Clamp(opacity * 255, 0, 255);
        foreach (var window in _pinkFilterWindows)
            if (window.Content is Border border &&
                border.Background is System.Windows.Media.SolidColorBrush brush)
                brush.Color = System.Windows.Media.Color.FromArgb(a, fr, fg, fb);
        // Force the next post-ramp settings-sync to re-apply from settings.
        _lastAppliedPinkOpacity = -1;
    }

    private void ApplySpiralOpacityDirect(double opacity)
    {
        var scaled = opacity * 0.1; // 90% reduction, matching CreateSpiralGifWindow / UpdateSpiralOpacity
        foreach (var image in _spiralGifImages) image.Opacity = scaled;
        foreach (var media in _spiralMediaElements) media.Opacity = scaled;
        _lastAppliedSpiralOpacity = -1;
    }

    private void ShowPinkFilterAdHoc(int opacityPercent)
    {
        if (_pinkFilterWindows.Count > 0) return;
        try
        {
            var settings = App.Settings?.Current;
            var screens = settings?.DualMonitorEnabled == true
                ? App.GetAllScreensCached()
                : new[] { System.Windows.Forms.Screen.PrimaryScreen! };

            foreach (var screen in screens)
            {
                var w = CreatePinkFilterForScreen(screen, opacityPercent);
                if (w != null) _pinkFilterWindows.Add(w);
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ShowPinkFilterAdHoc: {E}", ex.Message);
        }
    }

    private void ShowSpiralAdHoc()
    {
        // Spiral has heavier setup (GIF/video branching, frame timer); reuse the
        // existing path. If settings have no spiral path configured, this is a
        // no-op — Deeper logs at the dispatcher.
        if (_spiralWindows.Count > 0) return;
        try
        {
            var spiralPath = GetSpiralPath();
            if (string.IsNullOrEmpty(spiralPath))
            {
                App.Logger?.Debug("ShowSpiralAdHoc: no spiral path configured; skipping");
                return;
            }
            _spiralPath = spiralPath;
            StartSpiral();
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ShowSpiralAdHoc: {E}", ex.Message);
        }
    }

    private void StartPinkFilter()
    {
        if (_pinkFilterWindows.Count > 0) return;

        try
        {
            var settings = App.Settings.Current;
            
            var screens = settings.DualMonitorEnabled 
                ? App.GetAllScreensCached() 
                : new[] { System.Windows.Forms.Screen.PrimaryScreen! };

            foreach (var screen in screens)
            {
                var window = CreatePinkFilterForScreen(screen, settings.PinkFilterOpacity);
                if (window != null)
                {
                    _pinkFilterWindows.Add(window);
                }
            }

            App.Logger?.Debug("Pink filter started on {Count} screens at opacity {Opacity}%", 
                _pinkFilterWindows.Count, settings.PinkFilterOpacity);
        }
        catch (Exception ex)
        {
            App.Logger?.Error("Failed to start pink filter: {Error}", ex.Message);
        }
    }

    private Window? CreatePinkFilterForScreen(System.Windows.Forms.Screen screen, int opacity)
    {
        try
        {
            // Get WPF-compatible screen bounds for initial window creation
            var wpfBounds = GetWpfScreenBounds(screen);

            // Linear opacity (no exponential curve)
            var actualOpacity = opacity / 100.0;
            var (fr, fg, fb) = GetFilterRgb();

            var pinkOverlay = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(
                                    (byte)(actualOpacity * 255), fr, fg, fb)),
                Opacity = 1.0
            };

            // Create window - initial position is approximate, will be corrected via SetWindowPos
            var window = new Window
            {
                WindowStyle = System.Windows.WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                IsHitTestVisible = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = wpfBounds.Left,
                Top = wpfBounds.Top,
                Width = wpfBounds.Width,
                Height = wpfBounds.Height,
                Content = pinkOverlay
            };

            // Capture screen reference for use in handler
            var targetScreen = screen;
            HookBoundsRestore(window, targetScreen);

            window.SourceInitialized += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);

                // Use SetWindowPos with physical pixel coordinates for exact positioning
                // This bypasses WPF's DPI virtualization which causes offset issues on mixed-DPI setups
                PositionWindowOnScreen(window, targetScreen);
            };

            window.Show();

            App.Logger?.Debug("Pink filter created for {Screen}", screen.DeviceName);

            return window;
        }
        catch (Exception ex)
        {
            App.Logger?.Error("Failed to create pink filter for screen: {Error}", ex.Message);
            return null;
        }
    }

    internal void StopPinkFilter()
    {
        foreach (var window in _pinkFilterWindows.ToList())
        {
            try { window.Close(); }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to close pink filter window: {Error}", ex.Message);
            }
        }
        _lastAppliedPinkOpacity = -1;
        _rampPinkOpacity = null;
        _sustainedPinkHeld = false; // overlay is gone (incl. force-stop / panic) — drop any stale sustained hold
        _pinkFilterWindows.Clear();
        App.Logger?.Debug("Pink filter stopped");
    }

    private void UpdatePinkFilterOpacity()
    {
        if (_rampPinkOpacity.HasValue) return; // a Deeper ramp owns this overlay's opacity
        var actualOpacity = App.Settings.Current.PinkFilterOpacity / 100.0;
        if (actualOpacity == _lastAppliedPinkOpacity) return;
        _lastAppliedPinkOpacity = actualOpacity;
        var (fr, fg, fb) = GetFilterRgb();
        foreach (var window in _pinkFilterWindows)
        {
            if (window.Content is Border border)
            {
                if (border.Background is System.Windows.Media.SolidColorBrush brush)
                {
                    brush.Color = System.Windows.Media.Color.FromArgb((byte)(actualOpacity * 255), fr, fg, fb);
                }
            }
        }
    }

    #endregion

    #region Spiral

    private void StartSpiral()
    {
        if (_spiralWindows.Count > 0) return;

        try
        {
            var settings = App.Settings.Current;

            var screens = settings.DualMonitorEnabled
                ? App.GetAllScreensCached()
                : new[] { System.Windows.Forms.Screen.PrimaryScreen! };

            _isGifSpiral = _spiralPath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);

            // For GIFs, load frames once and share across all screens
            if (_isGifSpiral)
            {
                if (!LoadSpiralGifFrames())
                {
                    App.Logger?.Warning("Spiral: Failed to load GIF frames from {Path}", _spiralPath);
                    return;
                }

                foreach (var screen in screens)
                {
                    var (window, image) = CreateSpiralGifWindow(screen, settings.SpiralOpacity);
                    if (window != null)
                    {
                        _spiralWindows.Add(window);
                        if (image != null)
                            _spiralGifImages.Add(image);
                    }
                }

                // Start frame animation timer
                if (_spiralGifFrames.Count > 1 && _spiralGifImages.Count > 0)
                {
                    _gifFrameTimer = new DispatcherTimer(DispatcherPriority.Render)
                    {
                        Interval = _gifFrameDelay
                    };
                    _gifFrameTimer.Tick += GifFrameTimer_Tick;
                    _gifFrameTimer.Start();
                    App.Logger?.Debug("Spiral GIF animation started with {FrameCount} frames at {Delay}ms interval",
                        _spiralGifFrames.Count, _gifFrameDelay.TotalMilliseconds);
                }
            }
            else
            {
                CreateSpiralVideoWindows();
            }

            App.Logger?.Debug("Spiral started on {Count} screens at opacity {Opacity}% (GIF: {IsGif})",
                _spiralWindows.Count, settings.SpiralOpacity, _isGifSpiral);
        }
        catch (Exception ex)
        {
            App.Logger?.Error("Failed to start spiral: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Create spiral GIF windows on all screens using pre-loaded frames.
    /// Must be called on the UI thread.
    /// </summary>
    /// <summary>
    /// Create spiral video windows on all screens.
    /// Must be called on the UI thread.
    /// </summary>
    private void CreateSpiralVideoWindows()
    {
        var settings = App.Settings.Current;
        var screens = settings.DualMonitorEnabled
            ? App.GetAllScreensCached()
            : new[] { System.Windows.Forms.Screen.PrimaryScreen! };

        foreach (var screen in screens)
        {
            var (window, media) = CreateSpiralVideoWindow(screen, settings.SpiralOpacity);
            if (window != null)
            {
                _spiralWindows.Add(window);
                if (media != null)
                {
                    _spiralMediaElements.Add(media);
                }
            }
        }

        // Start loop timer for video files
        if (_spiralMediaElements.Count > 0)
        {
            _gifLoopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _gifLoopTimer.Tick += VideoLoopTimer_Tick;
            _gifLoopTimer.Start();
        }

        App.Logger?.Debug("Spiral video started on {Count} screens at opacity {Opacity}%",
            _spiralWindows.Count, settings.SpiralOpacity);
    }

    /// <summary>
    /// Load GIF frames from file or embedded resource.
    /// </summary>
    private bool LoadSpiralGifFrames()
    {
        _currentGifFrameIndex = 0;

        // Reuse cached frames for this path instead of re-decoding (the decode runs on the UI
        // thread and freezes everything on screen for ~1s — very visible when chaos re-shows the
        // spiral on each detonation). Frames are frozen; the per-show copy is cheap and safe.
        if (_spiralFramesCacheKey == _spiralPath && _spiralFramesCache.Count > 0)
        {
            _spiralGifFrames = new List<BitmapSource>(_spiralFramesCache);
            _gifFrameDelay = _spiralFramesCacheDelay;
            return true;
        }

        var (frames, delay) = DecodeGifFrames(_spiralPath);
        if (frames.Count == 0) { _spiralGifFrames = new List<BitmapSource>(); return false; }

        _spiralGifFrames = frames;
        _gifFrameDelay = delay;
        _spiralFramesCache = new List<BitmapSource>(frames);   // cache for instant reuse (no re-decode → no freeze)
        _spiralFramesCacheKey = _spiralPath;
        _spiralFramesCacheDelay = delay;
        return true;
    }

    /// <summary>
    /// Pre-decode the spiral GIF into the frame cache off the UI thread, so the first chaos
    /// spiral of a run doesn't hitch. Uses the configured/default spiral; no-op if already warm.
    /// </summary>
    public void WarmSpiralCache()
    {
        try
        {
            var path = GetSpiralPath();
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) return;
            if (_spiralFramesCacheKey == path && _spiralFramesCache.Count > 0) return;

            Task.Run(() =>
            {
                try
                {
                    var (frames, delay) = DecodeGifFrames(path);
                    if (frames.Count == 0) return;
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_spiralFramesCacheKey != path || _spiralFramesCache.Count == 0)
                        {
                            _spiralFramesCache = frames;
                            _spiralFramesCacheKey = path;
                            _spiralFramesCacheDelay = delay;
                            App.Logger?.Debug("Spiral cache warmed off-thread: {Count} frames", frames.Count);
                        }
                    }));
                }
                catch (Exception ex) { App.Logger?.Debug("WarmSpiralCache decode: {E}", ex.Message); }
            });
        }
        catch (Exception ex) { App.Logger?.Debug("WarmSpiralCache: {E}", ex.Message); }
    }

    /// <summary>
    /// Pure GIF→frozen-frames decode (no shared state) — safe to call off the UI thread. Used by
    /// both the on-demand load and the background warm-up. Returns the frames and the frame delay.
    /// </summary>
    private static (List<BitmapSource> frames, TimeSpan delay) DecodeGifFrames(string path)
    {
        var frames = new List<BitmapSource>();
        var delay = TimeSpan.FromMilliseconds(50);
        try
        {
            Stream? gifStream = null;
            bool needsDispose = false;

            if (path.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
            {
                var streamInfo = System.Windows.Application.GetResourceStream(new Uri(path, UriKind.Absolute));
                if (streamInfo?.Stream != null) { gifStream = streamInfo.Stream; needsDispose = true; }
            }
            else if (File.Exists(path))
            {
                gifStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                needsDispose = true;
            }

            if (gifStream == null)
            {
                App.Logger?.Warning("Spiral: Could not open stream for {Path}", path);
                return (frames, delay);
            }

            try
            {
                using var gif = System.Drawing.Image.FromStream(gifStream);
                var dimension = new FrameDimension(gif.FrameDimensionsList[0]);
                var frameCount = gif.GetFrameCount(dimension);

                var frameDelayMs = 50;
                try
                {
                    var propertyItem = gif.GetPropertyItem(0x5100); // FrameDelay
                    if (propertyItem?.Value != null && propertyItem.Value.Length >= 4)
                    {
                        frameDelayMs = BitConverter.ToInt32(propertyItem.Value, 0) * 10;
                        if (frameDelayMs < 20 || frameDelayMs > 500) frameDelayMs = 50;
                    }
                }
                catch (Exception ex) { App.Logger?.Debug("Spiral: Could not read GIF frame delay: {Error}", ex.Message); }

                delay = TimeSpan.FromMilliseconds(frameDelayMs);

                var maxFrames = Math.Min(frameCount, 120);
                var step = frameCount > maxFrames ? frameCount / maxFrames : 1;

                for (int i = 0; i < frameCount && frames.Count < maxFrames; i += step)
                {
                    gif.SelectActiveFrame(dimension, i);
                    using var frameBitmap = new System.Drawing.Bitmap(gif.Width, gif.Height);
                    using (var g = System.Drawing.Graphics.FromImage(frameBitmap))
                        g.DrawImage(gif, 0, 0, gif.Width, gif.Height);

                    var bitmapSource = ConvertToBitmapSource(frameBitmap);
                    bitmapSource.Freeze();
                    frames.Add(bitmapSource);
                }

                App.Logger?.Information("Spiral: Decoded {Count} GIF frames from {Path}", frames.Count, path);
                return (frames, delay);
            }
            finally { if (needsDispose) gifStream.Dispose(); }
        }
        catch (Exception ex)
        {
            App.Logger?.Error("Spiral: Failed to decode GIF frames: {Error}", ex.Message);
            return (frames, delay);
        }
    }

    private static BitmapSource ConvertToBitmapSource(System.Drawing.Bitmap bitmap)
    {
        var bitmapData = bitmap.LockBits(
            new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        try
        {
            var bitmapSource = BitmapSource.Create(
                bitmap.Width, bitmap.Height,
                96, 96,
                PixelFormats.Bgra32,
                null,
                bitmapData.Scan0,
                bitmapData.Stride * bitmap.Height,
                bitmapData.Stride);

            bitmapSource.Freeze();
            return bitmapSource;
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }

    private void GifFrameTimer_Tick(object? sender, EventArgs e)
    {
        if (_spiralGifFrames.Count == 0 || _spiralGifImages.Count == 0) return;
        try
        {
            _currentGifFrameIndex = (_currentGifFrameIndex + 1) % _spiralGifFrames.Count;
            var frame = _spiralGifFrames[_currentGifFrameIndex];
            foreach (var image in _spiralGifImages)
                image.Source = frame;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Spiral: Frame tick failed: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Timer tick for video looping (MediaElement doesn't always fire MediaEnded reliably).
    /// </summary>
    private void VideoLoopTimer_Tick(object? sender, EventArgs e)
    {
        foreach (var media in _spiralMediaElements)
        {
            try
            {
                if (media.NaturalDuration.HasTimeSpan)
                {
                    var currentPos = media.Position;
                    if (currentPos >= media.NaturalDuration.TimeSpan - TimeSpan.FromMilliseconds(100))
                    {
                        media.Position = TimeSpan.Zero;
                        media.Play();
                    }
                }
            }
            catch
            {
                // Ignore errors during tick
            }
        }
    }

    /// <summary>
    /// Creates a spiral window with pre-loaded GIF frames.
    /// </summary>
    private (Window? window, System.Windows.Controls.Image? image) CreateSpiralGifWindow(System.Windows.Forms.Screen screen, int opacity)
    {
        try
        {
            if (_spiralGifFrames.Count == 0) return (null, null);

            var wpfBounds = GetWpfScreenBounds(screen);

            // Very subtle opacity - 90% reduction
            var actualOpacity = (opacity / 100.0) * 0.1;

            var image = new System.Windows.Controls.Image
            {
                Source = _spiralGifFrames[0],
                Stretch = Stretch.UniformToFill,
                Opacity = actualOpacity,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var container = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ClipToBounds = true
            };
            container.Children.Add(image);

            var window = new Window
            {
                WindowStyle = System.Windows.WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                IsHitTestVisible = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = wpfBounds.Left,
                Top = wpfBounds.Top,
                Width = wpfBounds.Width,
                Height = wpfBounds.Height,
                Content = container
            };

            var targetScreen = screen;
            HookBoundsRestore(window, targetScreen);

            window.SourceInitialized += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);
                PositionWindowOnScreen(window, targetScreen);
            };

            window.Show();

            App.Logger?.Debug("Spiral GIF window created for {Screen}", screen.DeviceName);

            return (window, image);
        }
        catch (Exception ex)
        {
            App.Logger?.Error("Failed to create spiral GIF window: {Error}", ex.Message);
            return (null, null);
        }
    }

    /// <summary>
    /// Creates a spiral window with MediaElement for video files (.mp4, .webm, etc.).
    /// </summary>
    private (Window? window, MediaElement? media) CreateSpiralVideoWindow(System.Windows.Forms.Screen screen, int opacity)
    {
        try
        {
            var wpfBounds = GetWpfScreenBounds(screen);
            var actualOpacity = (opacity / 100.0) * 0.1;

            var mediaElement = new MediaElement
            {
                Source = new Uri(_spiralPath),
                LoadedBehavior = MediaState.Play,
                UnloadedBehavior = MediaState.Manual,
                Stretch = Stretch.UniformToFill,
                Opacity = actualOpacity,
                IsMuted = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            mediaElement.MediaEnded += (s, e) =>
            {
                mediaElement.Position = TimeSpan.Zero;
                mediaElement.Play();
            };

            var container = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ClipToBounds = true
            };
            container.Children.Add(mediaElement);

            var window = new Window
            {
                WindowStyle = System.Windows.WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                IsHitTestVisible = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = wpfBounds.Left,
                Top = wpfBounds.Top,
                Width = wpfBounds.Width,
                Height = wpfBounds.Height,
                Content = container
            };

            var targetScreen = screen;
            HookBoundsRestore(window, targetScreen);

            window.SourceInitialized += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);
                PositionWindowOnScreen(window, targetScreen);
            };

            window.Show();

            App.Logger?.Debug("Spiral video window created for {Screen}", screen.DeviceName);

            return (window, mediaElement);
        }
        catch (Exception ex)
        {
            App.Logger?.Error("Failed to create spiral video window: {Error}", ex.Message);
            return (null, null);
        }
    }

    internal void StopSpiral()
    {
        _gifFrameTimer?.Stop();
        _gifFrameTimer = null;

        _gifLoopTimer?.Stop();
        _gifLoopTimer = null;

        foreach (var img in _spiralGifImages)
            img.Source = null;
        _spiralGifImages.Clear();
        _spiralGifFrames.Clear();
        _currentGifFrameIndex = 0;


        // Stop and clear MediaElements
        foreach (var media in _spiralMediaElements.ToList())
        {
            try { media.Stop(); media.Close(); }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to stop spiral media: {Error}", ex.Message);
            }
        }
        _spiralMediaElements.Clear();
        _mediaStartTimes.Clear();

        // Close all windows
        foreach (var window in _spiralWindows.ToList())
        {
            try { window.Close(); }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to close spiral window: {Error}", ex.Message);
            }
        }
        _lastAppliedSpiralOpacity = -1;
        _rampSpiralOpacity = null;
        _sustainedSpiralHeld = false; // overlay is gone (incl. force-stop / panic) — drop any stale sustained hold
        _spiralWindows.Clear();
        App.Logger?.Debug("Spiral stopped");
    }

    private void UpdateSpiralOpacity()
    {
        if (_rampSpiralOpacity.HasValue) return; // a Deeper ramp owns this overlay's opacity
        // Very subtle opacity - 90% reduction
        var opacity = (App.Settings.Current.SpiralOpacity / 100.0) * 0.1;
        if (opacity == _lastAppliedSpiralOpacity) return;
        _lastAppliedSpiralOpacity = opacity;

        // Update GIF images
        foreach (var image in _spiralGifImages)
        {
            image.Opacity = opacity;
        }

        // Update MediaElements (for video spirals)
        foreach (var media in _spiralMediaElements)
        {
            media.Opacity = opacity;
        }
    }

    #endregion

    #region Brain Drain Blur (Screen Capture - Optimized)

    private readonly Dictionary<Window, System.Windows.Controls.Image> _brainDrainImages = new();
    private readonly Dictionary<Window, System.Windows.Forms.Screen> _brainDrainScreens = new();
    private DispatcherTimer? _brainDrainCaptureTimer;
    private int _currentBrainDrainIntensity = 50;
    // Linear downscale factor for the captured screen: we BitBlt-shrink the screen, blur the
    // small bitmap with a proportionally smaller radius, and let WPF upscale it (the upscale is
    // itself part of the blur). Captured + blur radius both divided by this. Set at start.
    private int _brainDrainDownscale = 4;
    private System.Drawing.Bitmap? _captureBitmap;
    private IntPtr _captureHdc;
    private IntPtr _captureMemDc;
    private IntPtr _captureHBitmap;

    public void StartBrainDrainBlur(int intensity)
    {
        if (_brainDrainBlurWindows.Count > 0) return;

        _currentBrainDrainIntensity = intensity;

        DispatcherHelper.RunOnUISync(() =>
        {
            try
            {
                var settings = App.Settings.Current;
                var screens = settings.DualMonitorEnabled
                    ? App.GetAllScreensCached()
                    : new[] { System.Windows.Forms.Screen.PrimaryScreen! };

                // Pick the downscale factor for this run from the active performance tier.
                var tier = PerformanceProfile.CurrentTier;
                _brainDrainDownscale = PerformanceProfile.BrainDrainDownscale(tier);

                foreach (var screen in screens)
                {
                    var window = CreateBrainDrainWindow(screen, intensity);
                    if (window != null)
                    {
                        _brainDrainBlurWindows.Add(window);
                    }
                }

                // Refresh rate based on setting, capped by the performance tier:
                // Normal: 30 FPS (balanced); High Refresh: 60 FPS (smoother, more CPU).
                // The blur masks lower frame rates, so the tier cap (e.g. 15 FPS under load) is
                // visually fine while roughly halving/quartering capture cost.
                int fps = Math.Min(settings.BrainDrainHighRefresh ? 60 : 30,
                                   PerformanceProfile.BrainDrainFps(tier));
                double intervalMs = 1000.0 / fps;

                _brainDrainCaptureTimer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(intervalMs)
                };
                _brainDrainCaptureTimer.Tick += BrainDrainCaptureTick;
                _brainDrainCaptureTimer.Start();

                App.Logger?.Information("Brain Drain started on {Count} screens at {Fps} FPS, intensity {Intensity}%",
                    _brainDrainBlurWindows.Count, fps, intensity);
            }
            catch (Exception ex)
            {
                App.Logger?.Error("Failed to start Brain Drain: {Error}", ex.Message);
            }
        });
    }

    public void StopBrainDrainBlur()
    {
        try
        {
            _rampBrainDrainOpacity = null; // release any Deeper ramp ownership
            _brainDrainCaptureTimer?.Stop();
            _brainDrainCaptureTimer = null;

            // Clean up GDI resources
            CleanupCaptureResources();

            var windowsToClose = _brainDrainBlurWindows.ToList();
            foreach (var window in windowsToClose)
            {
                try
                {
                    DispatcherHelper.RunOnUISync(() => window.Close());
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Failed to close brain drain window: {Error}", ex.Message);
                }
            }
            _brainDrainBlurWindows.Clear();
            _brainDrainImages.Clear();
            _brainDrainScreens.Clear();

            App.Logger?.Debug("Brain Drain stopped");
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Error stopping Brain Drain blur");
        }
    }

    public void UpdateBrainDrainBlurOpacity(int intensity)
    {
        _currentBrainDrainIntensity = intensity;
        // Keep in sync with CreateBrainDrainWindow's downscaled-source radius.
        double blurRadius = (intensity * 0.4) / Math.Max(1, _brainDrainDownscale);

        DispatcherHelper.RunOnUISync(() =>
        {
            foreach (var img in _brainDrainImages.Values)
            {
                if (img.Effect is System.Windows.Media.Effects.BlurEffect blur)
                {
                    blur.Radius = blurRadius;
                }
            }
        });
    }

    private void BrainDrainCaptureTick(object? sender, EventArgs e)
    {
        if (_brainDrainImages.Count == 0)
        {
            _brainDrainCaptureTimer?.Stop();
            return;
        }

        // Snapshot to prevent "collection modified during enumeration" if StopBrainDrainBlur()
        // is triggered by an event during iteration (e.g., Image.Source assignment)
        foreach (var kvp in _brainDrainImages.ToList())
        {
            var window = kvp.Key;
            var image = kvp.Value;

            if (_brainDrainScreens.TryGetValue(window, out var screen))
            {
                var capture = CaptureScreenOptimized(screen);
                if (capture != null)
                {
                    image.Source = capture;
                }
            }
        }
    }

    private System.Windows.Media.Imaging.BitmapSource? CaptureScreenOptimized(System.Windows.Forms.Screen screen)
    {
        IntPtr hdcSrc = IntPtr.Zero;
        IntPtr hdcDest = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr hOld = IntPtr.Zero;

        try
        {
            var bounds = screen.Bounds;

            // Downscaled capture target — even dimensions, at least 2px. Capturing + blurring a
            // 1/4 (or 1/8) size bitmap is dramatically cheaper than full-screen, and the upscale
            // back to full size (Image.Stretch=Fill) reads as additional blur.
            int divisor = Math.Max(1, _brainDrainDownscale);
            int dw = Math.Max(2, (bounds.Width / divisor) & ~1);
            int dh = Math.Max(2, (bounds.Height / divisor) & ~1);

            // Get screen DC
            hdcSrc = GetDC(IntPtr.Zero);
            if (hdcSrc == IntPtr.Zero) return null;

            hdcDest = CreateCompatibleDC(hdcSrc);
            if (hdcDest == IntPtr.Zero) return null;

            hBitmap = CreateCompatibleBitmap(hdcSrc, dw, dh);
            if (hBitmap == IntPtr.Zero) return null;

            hOld = SelectObject(hdcDest, hBitmap);

            // Shrink the screen content into the small bitmap in one GDI call.
            SetStretchBltMode(hdcDest, HALFTONE);
            StretchBlt(hdcDest, 0, 0, dw, dh,
                       hdcSrc, bounds.X, bounds.Y, bounds.Width, bounds.Height, SRCCOPY);

            // Restore selection before creating bitmap source
            if (hOld != IntPtr.Zero)
            {
                SelectObject(hdcDest, hOld);
                hOld = IntPtr.Zero;
            }

            // Convert to WPF BitmapSource
            var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

            bitmapSource.Freeze();
            return bitmapSource;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Screen capture failed: {Error}", ex.Message);
            return null;
        }
        finally
        {
            // Always cleanup GDI handles in reverse order of creation
            if (hOld != IntPtr.Zero && hdcDest != IntPtr.Zero)
                SelectObject(hdcDest, hOld);
            if (hBitmap != IntPtr.Zero)
                DeleteObject(hBitmap);
            if (hdcDest != IntPtr.Zero)
                DeleteDC(hdcDest);
            if (hdcSrc != IntPtr.Zero)
                ReleaseDC(IntPtr.Zero, hdcSrc);
        }
    }

    private void CleanupCaptureResources()
    {
        try
        {
            if (_captureHBitmap != IntPtr.Zero) { DeleteObject(_captureHBitmap); _captureHBitmap = IntPtr.Zero; }
            if (_captureMemDc != IntPtr.Zero) { DeleteDC(_captureMemDc); _captureMemDc = IntPtr.Zero; }
            if (_captureHdc != IntPtr.Zero) { ReleaseDC(IntPtr.Zero, _captureHdc); _captureHdc = IntPtr.Zero; }
            _captureBitmap?.Dispose();
            _captureBitmap = null;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Error cleaning up capture resources: {Error}", ex.Message);
        }
    }

    private Window? CreateBrainDrainWindow(System.Windows.Forms.Screen screen, int intensity)
    {
        try
        {
            var wpfBounds = GetWpfScreenBounds(screen);
            // The source bitmap is 1/divisor size and gets upscaled by Stretch=Fill, so a
            // proportionally smaller blur radius yields the same on-screen blur far more cheaply.
            double blurRadius = (intensity * 0.4) / Math.Max(1, _brainDrainDownscale);

            var image = new System.Windows.Controls.Image
            {
                Stretch = Stretch.Fill,
                Effect = new System.Windows.Media.Effects.BlurEffect
                {
                    Radius = blurRadius,
                    KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
                    RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
                }
            };

            // Create window - initial position is approximate, will be corrected via SetWindowPos
            var window = new Window
            {
                WindowStyle = System.Windows.WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                IsHitTestVisible = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = wpfBounds.Left,
                Top = wpfBounds.Top,
                Width = wpfBounds.Width,
                Height = wpfBounds.Height,
                Content = image
            };

            // Capture screen reference for use in handler
            var targetScreen = screen;
            HookBoundsRestore(window, targetScreen);

            window.SourceInitialized += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);

                // Exclude from capture so we don't capture ourselves
                SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);

                // Use SetWindowPos with physical pixel coordinates for exact positioning
                // This bypasses WPF's DPI virtualization which causes offset issues on mixed-DPI setups
                PositionWindowOnScreen(window, targetScreen);
            };

            window.Show();

            _brainDrainImages[window] = image;
            _brainDrainScreens[window] = screen;

            App.Logger?.Debug("Brain Drain created for {Screen}", screen.DeviceName);

            return window;
        }
        catch (Exception ex)
        {
            App.Logger?.Error("Failed to create Brain Drain window: {Error}", ex.Message);
            return null;
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Represents screen bounds - can be in physical pixels or WPF logical units
    /// </summary>
    private struct WpfScreenBounds
    {
        public double Left;
        public double Top;
        public double Width;
        public double Height;
    }

    /// <summary>
    /// Represents screen bounds in physical pixels (for use with SetWindowPos)
    /// </summary>
    private struct PhysicalScreenBounds
    {
        public int Left;
        public int Top;
        public int Width;
        public int Height;
    }

    /// <summary>
    /// Gets the actual physical pixel bounds of a monitor using Win32 APIs.
    /// This is the most reliable method for multi-monitor setups with different DPI.
    /// </summary>
    private PhysicalScreenBounds GetPhysicalScreenBounds(System.Windows.Forms.Screen screen)
    {
        try
        {
            // Get monitor handle from a point inside the screen
            var point = new POINT { X = screen.Bounds.X + screen.Bounds.Width / 2, Y = screen.Bounds.Y + screen.Bounds.Height / 2 };
            var hMonitor = MonitorFromPoint(point, 2); // MONITOR_DEFAULTTONEAREST

            if (hMonitor != IntPtr.Zero)
            {
                var monitorInfo = new MONITORINFO();
                monitorInfo.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(MONITORINFO));

                if (GetMonitorInfo(hMonitor, ref monitorInfo))
                {
                    var bounds = new PhysicalScreenBounds
                    {
                        Left = monitorInfo.rcMonitor.Left,
                        Top = monitorInfo.rcMonitor.Top,
                        Width = monitorInfo.rcMonitor.Right - monitorInfo.rcMonitor.Left,
                        Height = monitorInfo.rcMonitor.Bottom - monitorInfo.rcMonitor.Top
                    };

                    App.Logger?.Debug("Screen {Name}: Physical bounds from Win32 = ({X},{Y},{W}x{H})",
                        screen.DeviceName, bounds.Left, bounds.Top, bounds.Width, bounds.Height);

                    return bounds;
                }
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Failed to get physical screen bounds via Win32: {Error}", ex.Message);
        }

        // Fallback to Screen.Bounds (may be virtualized on mixed-DPI setups)
        App.Logger?.Debug("Screen {Name}: Falling back to Screen.Bounds = ({X},{Y},{W}x{H})",
            screen.DeviceName, screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);

        return new PhysicalScreenBounds
        {
            Left = screen.Bounds.X,
            Top = screen.Bounds.Y,
            Width = screen.Bounds.Width,
            Height = screen.Bounds.Height
        };
    }

    /// <summary>
    /// Positions a window to exactly cover a screen using physical pixel coordinates.
    /// This bypasses WPF's DPI virtualization for reliable multi-monitor positioning.
    /// </summary>
    private void PositionWindowOnScreen(Window window, System.Windows.Forms.Screen screen)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            App.Logger?.Warning("Cannot position window - no HWND yet");
            return;
        }

        var bounds = GetPhysicalScreenBounds(screen);

        // Use SetWindowPos with physical pixel coordinates - this bypasses WPF's DPI translation
        bool success = SetWindowPos(
            hwnd,
            HWND_TOPMOST,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);

        App.Logger?.Debug("Positioned window on {Screen} at physical ({X},{Y},{W}x{H}), success={Success}",
            screen.DeviceName, bounds.Left, bounds.Top, bounds.Width, bounds.Height, success);
    }

    /// <summary>
    /// Re-asserts the physical-pixel placement of every overlay window whose HWND rect no
    /// longer matches its screen. Placement is set exactly once at SourceInitialized, and
    /// every later SetWindowPos passes SWP_NOSIZE — so when a fullscreen video or DPI
    /// re-evaluation makes WPF re-apply the window's DIP Width/Height (computed with the
    /// PRIMARY monitor's scale), the wrong size stuck on mixed-DPI secondaries (#457).
    /// Cheap when nothing drifted: one GetWindowRect compare per overlay window.
    /// </summary>
    private void ReassertBounds()
    {
        foreach (var list in new[] { _pinkFilterWindows, _spiralWindows, _brainDrainBlurWindows })
        {
            foreach (var window in list.ToList())
                ReassertBoundsFor(window);
        }
    }

    /// <summary>
    /// Tags an overlay window with its target screen (so the drift reconciler can
    /// re-resolve it after display changes) and restores physical placement whenever
    /// WPF re-applies DIP bounds on a per-monitor DPI change (#457).
    /// </summary>
    private void HookBoundsRestore(Window window, System.Windows.Forms.Screen targetScreen)
    {
        window.Tag = targetScreen.DeviceName;
        window.DpiChanged += (s, e) =>
        {
            // Let WPF finish applying its own resize first, then stamp physical bounds back.
            Application.Current?.Dispatcher?.BeginInvoke(DispatcherPriority.Background,
                new Action(() => ReassertBoundsFor(window)));
        };
    }

    private void ReassertBoundsFor(Window window)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            if (window.Tag is not string deviceName) return;

            var screen = App.GetAllScreensCached().FirstOrDefault(s => s.DeviceName == deviceName);
            if (screen == null) return;   // monitor unplugged — RecreateOverlays handles that case

            var expected = GetPhysicalScreenBounds(screen);
            if (!GetWindowRect(hwnd, out var rect)) return;

            if (rect.Left == expected.Left && rect.Top == expected.Top &&
                rect.Right - rect.Left == expected.Width && rect.Bottom - rect.Top == expected.Height)
                return;

            App.Logger?.Debug("Overlay drifted on {Screen}: actual=({L},{T},{W}x{H}) expected=({EL},{ET},{EW}x{EH}) — repositioning",
                deviceName, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top,
                expected.Left, expected.Top, expected.Width, expected.Height);
            PositionWindowOnScreen(window, screen);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ReassertBoundsFor failed: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Re-asserts HWND_TOPMOST on overlay windows. By default only windows that have
    /// actually lost the WS_EX_TOPMOST flag are re-pinned. Pass <paramref name="force"/>
    /// = true to re-issue HWND_TOPMOST unconditionally, which bumps the window to the
    /// front of the topmost layer even when its flag is already set — required after
    /// fullscreen videos, OS notifications, or other topmost windows have temporarily
    /// reordered things without clearing our flag.
    /// Returns true if any window was re-pinned.
    /// </summary>
    private bool ReassertZOrder(bool force = false)
    {
        bool anyRecovered = false;
        foreach (var list in new[] { _pinkFilterWindows, _spiralWindows, _brainDrainBlurWindows })
        {
            foreach (var window in list)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) continue;

                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                bool needsPin = (exStyle & WS_EX_TOPMOST) == 0;
                if (needsPin || force)
                {
                    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    if (needsPin) anyRecovered = true;
                }
            }
        }
        return anyRecovered;
    }

    /// <summary>
    /// Called by FlashService/VideoService/MainWindow after closing topmost windows.
    /// Immediately re-asserts overlay z-order after a short delay to let the closing window fully destroy.
    /// </summary>
    public void NotifyTopWindowClosed()
    {
        if (!_isRunning) return;

        Application.Current?.Dispatcher?.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (_isDisposed || !_isRunning) return;
            // Force re-pin even if WS_EX_TOPMOST is technically still set — a topmost
            // sibling closing leaves us in the topmost layer but possibly behind
            // whatever else was there, so we need to bump to the front.
            ReassertZOrder(force: true);
            // A closing fullscreen video can also have provoked a DPI re-evaluation that
            // shrank overlays to primary-scale size on mixed-DPI setups (#457).
            ReassertBounds();
        });
    }

    /// <summary>
    /// Recreates all active overlay windows. Used as a fallback when overlays persistently lose topmost status.
    /// </summary>
    private void RecreateOverlays()
    {
        App.Logger?.Warning("Overlay topmost loss persisted for 3s — recreating overlay windows");

        var settings = App.Settings.Current;
        bool hadPinkFilter = _pinkFilterWindows.Count > 0;
        bool hadSpiral = _spiralWindows.Count > 0;
        bool hadBrainDrain = _brainDrainBlurWindows.Count > 0;

        if (hadPinkFilter) StopPinkFilter();
        if (hadSpiral) StopSpiral();
        if (hadBrainDrain) StopBrainDrainBlur();

        if (hadPinkFilter && settings.PinkFilterEnabled) StartPinkFilter();
        if (hadSpiral && settings.SpiralEnabled) StartSpiral();
        // Brain drain was previously only logged here, so a braindrain overlay that was the one losing
        // topmost got torn down and never came back. Restart it too (same intensity source the other
        // reconcile paths use) so recreation doesn't silently kill the effect.
        if (hadBrainDrain && settings.BrainDrainEnabled) StartBrainDrainBlur((int)settings.BrainDrainIntensity);
    }

    /// <summary>
    /// Gets the screen bounds converted to WPF device-independent coordinates.
    /// Used for initial window creation - final positioning done via SetWindowPos.
    /// </summary>
    private WpfScreenBounds GetWpfScreenBounds(System.Windows.Forms.Screen screen)
    {
        // For initial window creation, we use approximate WPF coordinates
        // The SourceInitialized handler will then use SetWindowPos with physical pixels
        // to get the exact positioning right
        double primaryDpi = GetPrimaryMonitorDpi();
        double primaryScale = primaryDpi / 96.0;

        // Use physical bounds from Win32 for more accurate initial position
        var physicalBounds = GetPhysicalScreenBounds(screen);

        double left = physicalBounds.Left / primaryScale;
        double top = physicalBounds.Top / primaryScale;
        double width = physicalBounds.Width / primaryScale;
        double height = physicalBounds.Height / primaryScale;

        App.Logger?.Debug("Screen {Name}: Physical=({PX},{PY},{PW}x{PH}), PrimaryDPI={PDPI}, WPF=({WX},{WY},{WW}x{WH})",
            screen.DeviceName,
            physicalBounds.Left, physicalBounds.Top, physicalBounds.Width, physicalBounds.Height,
            primaryDpi,
            left, top, width, height);

        return new WpfScreenBounds
        {
            Left = left,
            Top = top,
            Width = width,
            Height = height
        };
    }

    private double GetMonitorDpi(System.Windows.Forms.Screen screen)
    {
        try
        {
            var hMonitor = MonitorFromPoint(new POINT { X = screen.Bounds.X + 1, Y = screen.Bounds.Y + 1 }, 2);
            if (hMonitor != IntPtr.Zero)
            {
                var result = GetDpiForMonitor(hMonitor, 0, out uint dpiX, out uint dpiY);
                if (result == 0)
                {
                    return dpiX;
                }
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Could not get DPI for monitor: {Error}", ex.Message);
        }
        return 96.0;
    }

    private double GetPrimaryMonitorDpi()
    {
        try
        {
            var primary = System.Windows.Forms.Screen.PrimaryScreen;
            if (primary != null)
            {
                return GetMonitorDpi(primary);
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Could not get primary monitor DPI: {Error}", ex.Message);
        }
        return 96.0;
    }

    private double GetDpiScaleForScreen(System.Windows.Forms.Screen screen)
    {
        try
        {
            uint dpiX = 96, dpiY = 96;
            var hMonitor = MonitorFromPoint(new POINT { X = screen.Bounds.X + 1, Y = screen.Bounds.Y + 1 }, 2);

            if (hMonitor != IntPtr.Zero)
            {
                var result = GetDpiForMonitor(hMonitor, 0, out dpiX, out dpiY);
                if (result == 0)
                {
                    return dpiX / 96.0;
                }
            }

            using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
            return g.DpiX / 96.0;
        }
        catch
        {
            return 1.0;
        }
    }

    private double GetDpiScale()
    {
        try
        {
            using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
            return g.DpiX / 96.0;
        }
        catch
        {
            return 1.0;
        }
    }

    private void MakeClickThrough(Window window)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            // WS_EX_TRANSPARENT: clicks pass through
            // WS_EX_LAYERED: allows transparency
            // WS_EX_NOACTIVATE: never steals keyboard/mouse focus
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Failed to make window click-through: {Error}", ex.Message);
        }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOPMOST = 0x00000008;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

    private const uint WDA_NONE = 0x0;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11; // Windows 10 2004+

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [System.Runtime.InteropServices.DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool EnumDisplaySettingsEx(string? lpszDeviceName, uint iModeNum, ref DEVMODE lpDevMode, uint dwFlags);

    public const int ENUM_CURRENT_SETTINGS = -1;
    public const int ENUM_REGISTRY_SETTINGS = -2;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Ansi)]
    public struct DEVMODE
    {
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmCurrentMode;
        public uint dmFields;

        public short dmPositionX;
        public short dmPositionY;
        public Orientation dmDisplayOrientation;
        public DisplayFixedOutput dmDisplayFixedOutput;

        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    public enum Orientation : int
    {
        DMDO_DEFAULT = 0,
        DMDO_90 = 1,
        DMDO_180 = 2,
        DMDO_270 = 3
    }

    public enum DisplayFixedOutput : int
    {
        DMDFO_DEFAULT = 0,
        DMDFO_STRETCH = 1,
        DMDFO_CENTER = 2
    }

    private int GetScreenRefreshRate(System.Windows.Forms.Screen screen)
    {
        DEVMODE dm = new DEVMODE();
        dm.dmSize = (ushort)System.Runtime.InteropServices.Marshal.SizeOf(typeof(DEVMODE));
        if (EnumDisplaySettingsEx(screen.DeviceName, unchecked((uint)ENUM_CURRENT_SETTINGS), ref dm, 0))
        {
            return (int)dm.dmDisplayFrequency;
        }
        return 60;
    }

    #endregion

    private void CurrentSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Ensure this is executed on the UI thread
        DispatcherHelper.RunOnUISync(() =>
        {
            if (e.PropertyName == nameof(App.Settings.Current.BrainDrainIntensity) ||
                e.PropertyName == nameof(App.Settings.Current.BrainDrainEnabled))
            {
                App.Logger?.Debug("Brain Drain setting changed: {PropertyName}. Refreshing state.", e.PropertyName);
                RefreshBrainDrainState();
            }
            // Add other property names for PinkFilter, Spiral, etc. here if needed
            // else if (e.PropertyName == nameof(App.Settings.Current.PinkFilterEnabled) ||
            //          e.PropertyName == nameof(App.Settings.Current.PinkFilterOpacity))
            // {
            //      RefreshPinkFilterState();
            // }
            // else if (e.PropertyName == nameof(App.Settings.Current.SpiralEnabled) ||
            //          e.PropertyName == nameof(App.Settings.Current.SpiralOpacity))
            // {
            //      RefreshSpiralState();
            // }
        });
    }

    // New method to encapsulate Brain Drain specific refresh logic
    private void RefreshBrainDrainState()
    {
        var settings = App.Settings.Current;

        // Only start/update brain drain if the overlay service is running (engine is active)
        if (!_isRunning)
        {
            // Don't start brain drain if engine isn't running
            StopBrainDrainBlur();
            return;
        }

        if (settings.BrainDrainEnabled && settings.IsLevelUnlocked(70)) // Level 70 requirement for Brain Drain
        {
            if (_brainDrainBlurWindows.Count == 0)
            {
                StartBrainDrainBlur((int)settings.BrainDrainIntensity);
            }
            else if (!_rampBrainDrainOpacity.HasValue)
            {
                // Already running, just update intensity (a Deeper ramp owns it when active).
                UpdateBrainDrainBlurOpacity((int)settings.BrainDrainIntensity);
            }
        }
        else
        {
            StopBrainDrainBlur();
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        // Stop the service normally first
        _isRunning = false;
        _updateTimer?.Stop();
        _updateTimer = null;

        // Dispose previously stopped only _updateTimer, leaving the capture + GIF timers running with
        // _brainDrainImages populated — so BrainDrainCaptureTick kept grabbing the screen every tick
        // after teardown, and the native capture DC/HBITMAP leaked. Stop them all and free the handles.
        _brainDrainCaptureTimer?.Stop();
        _brainDrainCaptureTimer = null;
        _gifFrameTimer?.Stop();
        _gifFrameTimer = null;
        _gifLoopTimer?.Stop();
        _gifLoopTimer = null;
        _brainDrainImages.Clear();
        CleanupCaptureResources();

        // Unsubscribe from settings changes
        if (App.Settings?.Current != null)
        {
            App.Settings.Current.PropertyChanged -= CurrentSettings_PropertyChanged;
        }

        // Forcefully close all overlay windows - don't rely on Dispatcher during shutdown
        try
        {
            // Close all brain drain blur windows
            foreach (var window in _brainDrainBlurWindows.ToList())
            {
                try { window.Close(); }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Failed to close brain drain window on dispose: {Error}", ex.Message);
                }
            }
            _brainDrainBlurWindows.Clear();

            // Close all pink filter windows
            foreach (var window in _pinkFilterWindows.ToList())
            {
                try { window.Close(); }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Failed to close pink filter window on dispose: {Error}", ex.Message);
                }
            }
            _pinkFilterWindows.Clear();

            // Close all spiral windows and release frame data
            foreach (var window in _spiralWindows.ToList())
            {
                try { window.Close(); }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Failed to close spiral window on dispose: {Error}", ex.Message);
                }
            }
            _spiralWindows.Clear();
            foreach (var img in _spiralGifImages)
                img.Source = null;
            _spiralGifImages.Clear();
            _spiralGifFrames.Clear();
    

            App.Logger?.Debug("OverlayService disposed - all windows closed");
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Error during OverlayService disposal");
        }
    }
}
