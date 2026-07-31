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
    // Brain Drain needs the SAME guards, and needs them harder: the base feature is disabled for
    // rework, so settings.BrainDrainEnabled is false for everyone — which meant RefreshBrainDrainState
    // tore a live Deeper braindrain band down on the next RefreshOverlays() (autonomy, remote control,
    // or the user toggling pink/spiral on the dashboard all call it). Shared by "braindrain" and
    // "braindrain_melt": they are ONE underlying overlay, never co-active.
    private int _timedBrainDrainHolds;
    // Sustained holds: an overlay shown via ShowOverlaySustained (voice "go pink"/"spiral", Deeper
    // region bands) has no hide timer, so — like the timed holds above — the periodic reconcilers
    // (RefreshOverlays / UpdateOverlays) must NOT tear it down just because the persistent feature
    // setting is off. Bool, not a counter: ShowOverlaySustained is idempotent (the ad-hoc paths
    // early-return on a non-empty window list), so a repeat show + single hide must still release it.
    private bool _sustainedPinkHeld;
    private bool _sustainedSpiralHeld;
    private bool _sustainedBrainDrainHeld;

    /// <summary>
    /// Pure teardown decision for an overlay that ad-hoc callers can hold open: tear it down only
    /// when the persistent feature doesn't want it AND nothing ad-hoc is still holding it (a timed
    /// overlay in flight, or a sustained Deeper band). Extracted (like ResolveZOrderAction) so the
    /// guard the 500ms reconcilers depend on is unit-testable.
    /// </summary>
    internal static bool ShouldStopHeldOverlay(bool featureWantsIt, int timedHolds, bool sustainedHeld)
        => !featureWantsIt && timedHolds == 0 && !sustainedHeld;

    // Deeper enhancement overlay bands are the ONE case where an overlay must sit ABOVE a playing
    // mandatory video: the pink/spiral tint IS the enhanced video's effect (pre-compositor it was a
    // fresh topmost window created per band, so it naturally drew over the just-created video window).
    // ReassertZOrder otherwise pins every overlay BELOW the video (#497). This depth counts live
    // Deeper overlay bands (driven by the Deeper dispatcher's band Start/Stop); >0 flips the reconciler
    // to pin the overlay hosts above the video instead. Ambient/session/voice overlays are unaffected.
    // Reset to 0 by the enhancement engine on Stop so a leaked band can't strand overlays above future
    // videos. Volatile: read on the UI reconciler, written from the (UI-thread) dispatcher.
    private int _deeperOverlayBandDepth;
    internal bool DeeperOverlayBandActive => System.Threading.Volatile.Read(ref _deeperOverlayBandDepth) > 0;

    /// <summary>A Deeper enhancement overlay band opened (region entered). While any band is live the
    /// z-order reconciler pins overlays ABOVE the enhanced video, and we kick an immediate reassert so
    /// the tint pops over the video without waiting for the ~500ms reconcile tick.</summary>
    internal void BeginDeeperOverlayBand()
    {
        System.Threading.Interlocked.Increment(ref _deeperOverlayBandDepth);
        Application.Current?.Dispatcher?.BeginInvoke(new Action(() => { try { ReassertZOrder(force: true); } catch { } }));
    }

    /// <summary>A Deeper enhancement overlay band closed (region exited). On the last band exit the
    /// reconciler returns to pinning overlays below the video, and a forced reassert re-seats them.</summary>
    internal void EndDeeperOverlayBand()
    {
        if (System.Threading.Interlocked.Decrement(ref _deeperOverlayBandDepth) < 0)
            System.Threading.Interlocked.Exchange(ref _deeperOverlayBandDepth, 0);
        Application.Current?.Dispatcher?.BeginInvoke(new Action(() => { try { ReassertZOrder(force: true); } catch { } }));
    }

    /// <summary>Force-clear the Deeper band depth. Called by the enhancement engine on Stop so an
    /// abnormally-ended band (no matching exit) can't leave overlays pinned above later videos.</summary>
    internal void ResetDeeperOverlayBands() => System.Threading.Interlocked.Exchange(ref _deeperOverlayBandDepth, 0);

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

    // #573: a timed overlay fired while the overlay is ALREADY showing (pink/spiral bubble pop
    // during a base overlay or Deeper band) used to be swallowed whole by the ad-hoc shows'
    // early-return. Instead we park a temporary intensity bump in _rampXOpacity (so the 500ms
    // settings-sync can't stomp it) and restore the previous owner when the last timed hold
    // releases. _bumpPrevRampX remembers what was parked before the first bump (a band's hold,
    // or null = settings-sync owns it).
    private bool _bumpPinkActive;
    private double? _bumpPrevRampPink;
    private bool _bumpSpiralActive;
    private double? _bumpPrevRampSpiral;

    // Unified overlay host (Settings.UnifiedOverlayHost, experimental): pink/spiral render as
    // compositor layers on the shared per-monitor Skia host instead of per-effect layered
    // windows. This service keeps ALL the opacity math (ramps, pulses, holds, settings sync)
    // and pushes final values; the layers only draw. Layers are created lazily so the engine
    // stays fully parked when the flag is off. Route checks use "<layer>?.IsActive == true"
    // rather than the flag so a mid-run flag flip can't strand a visible layer.
    private Compositor.PinkTintLayer? _pinkLayer;
    private Compositor.SpiralLayer? _spiralLayer;
    private static bool UseCompositor => App.CompositorEnabled;
    private Compositor.PinkTintLayer GetPinkLayer()
    {
        if (_pinkLayer == null)
        {
            _pinkLayer = new Compositor.PinkTintLayer(App.Compositor!);
            App.Compositor!.RegisterLayer(_pinkLayer);
        }
        return _pinkLayer;
    }
    private Compositor.SpiralLayer GetSpiralLayer()
    {
        if (_spiralLayer == null)
        {
            _spiralLayer = new Compositor.SpiralLayer(App.Compositor!);
            App.Compositor!.RegisterLayer(_spiralLayer);
        }
        return _spiralLayer;
    }
    private Compositor.BrainDrainLayer? _brainDrainLayer;
    private Compositor.BrainDrainLayer GetBrainDrainLayer()
    {
        if (_brainDrainLayer == null)
        {
            _brainDrainLayer = new Compositor.BrainDrainLayer(App.Compositor!);
            App.Compositor!.RegisterLayer(_brainDrainLayer);
        }
        return _brainDrainLayer;
    }
    // "Is showing" checks must cover BOTH render paths - the 500ms sync and pulse gate on these.
    private bool PinkShowing => _pinkFilterWindows.Count > 0 || _pinkLayer?.IsActive == true;
    private bool BrainDrainShowing => _brainDrainBlurWindows.Count > 0 || _brainDrainLayer?.IsActive == true;
    private bool SpiralShowing => _spiralWindows.Count > 0 || _spiralLayer?.IsShowing == true
        || _spiralLayerDecodePending != null;
    // Off-thread spiral decode state for the layer route: the path being decoded right now
    // (also counts as "showing" so the 500ms sync doesn't re-enter), and the last path that
    // produced zero frames (routed to the legacy windows instead of retrying forever).
    private string? _spiralLayerDecodePending;
    private string? _spiralLayerFailedPath;

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

    // Spiral randomizer (#641): when SpiralRandomize is on, GetSpiralPath picks a random spiral
    // from the pool at overlay/session START (never per-tick — the decoded-frame cache above is
    // keyed by path and a mid-run re-decode causes a ~1s hitch). _lastRandomSpiralPath backs the
    // no-repeat guard so we don't draw the same spiral twice in a row when >1 is available.
    private static readonly string[] SpiralExtensions = { ".gif", ".png", ".jpg", ".jpeg", ".webp" };
    private readonly Random _spiralRandom = new();
    private string? _lastRandomSpiralPath;

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

                var configured = (!string.IsNullOrEmpty(settings.SpiralPath) && File.Exists(settings.SpiralPath))
                    ? settings.SpiralPath
                    : null;

                // Randomizer (#641): pick a fresh spiral from the pool at start only. The pool is the
                // folder of the configured spiral if one is set, else the user Spirals library folder
                // (the same %LOCALAPPDATA%\ConditioningControlPanel\Spirals the Spiral card populates).
                // Falls back to the configured/default single spiral when the pool has <2 entries.
                if (settings.SpiralRandomize)
                {
                    var randomized = PickRandomSpiral(configured);
                    if (randomized != null) return randomized;
                }

                if (configured != null) return configured;

                return ModResourceResolver.ResolveUri("spiral.gif");
            }

            /// <summary>
            /// Build the spiral pool and return a random member (different from the last pick when
            /// possible). Returns null when no pool exists so the caller falls back to the single
            /// configured/default spiral. Called only at overlay/session start (never per-tick).
            /// </summary>
            private string? PickRandomSpiral(string? configured)
            {
                try
                {
                    // Pool directory: folder of the configured spiral, else the user Spirals library.
                    var poolDir = !string.IsNullOrEmpty(configured)
                        ? Path.GetDirectoryName(configured)
                        : Path.Combine(App.UserDataPath, "Spirals");

                    if (string.IsNullOrEmpty(poolDir) || !Directory.Exists(poolDir))
                        return null;

                    var pool = Directory.GetFiles(poolDir)
                        .Where(f => SpiralExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .ToList();

                    if (pool.Count == 0) return null;
                    if (pool.Count == 1) return pool[0];

                    // No-repeat guard (mirrors WallpaperService.Shuffle): avoid the previous pick.
                    string pick;
                    do
                    {
                        pick = pool[_spiralRandom.Next(pool.Count)];
                    } while (pick == _lastRandomSpiralPath);

                    _lastRandomSpiralPath = pick;
                    return pick;
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "[Overlay] Failed to pick random spiral");
                    return null;
                }
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
                if (!PinkShowing)
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
                if (!SpiralShowing)
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
            PinkShowing, SpiralShowing, BrainDrainShowing);
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
            var hasPink = settings.PinkFilterEnabled && PinkShowing;
            var hasSpiral = settings.SpiralEnabled && SpiralShowing;
            var hasBrainDrain = settings.BrainDrainEnabled && BrainDrainShowing;

            if (!hasPink && !hasSpiral && !hasBrainDrain) return;

            // Double the intensity
            if (hasPink)
            {
                var boosted = Math.Min(settings.PinkFilterOpacity * 2, 100);
                var alpha = (byte)(boosted / 100.0 * 255);
                var (fr, fg, fb) = GetFilterRgb();
                if (_pinkLayer?.IsActive == true)
                {
                    _pinkLayer.Set(fr, fg, fb, boosted / 100.0);
                }
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
                _spiralLayer?.SetOpacity(boostedOpacity);
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
                if (_brainDrainLayer?.IsActive == true)
                    _brainDrainLayer.Pulse(boostedIntensity);
                foreach (var img in _brainDrainImages.Values)
                {
                    if (img.Effect is System.Windows.Media.Effects.BlurEffect blur)
                        blur.Radius = blurRadius;
                }
            }

            // Restore after 1 second. When a session/Deeper ramp owns an overlay's opacity,
            // Update*Opacity() deliberately early-returns (it must not fight the live ramp) — so
            // routing the restore through it left the overlay stuck at the *boosted* value, up to
            // fully opaque, recoverable only by toggling the overlay off/on (#535). Restore to the
            // ramp's own value when a ramp is active; otherwise fall back to the user's settings.
            Task.Delay(1000).ContinueWith(_ =>
            {
                try
                {
                    DispatcherHelper.RunOnUISync(() =>
                    {
                        if (hasPink)
                        {
                            if (_rampPinkOpacity.HasValue) ApplyPinkOpacityDirect(_rampPinkOpacity.Value);
                            else UpdatePinkFilterOpacity();
                        }
                        if (hasSpiral)
                        {
                            if (_rampSpiralOpacity.HasValue) ApplySpiralOpacityDirect(_rampSpiralOpacity.Value);
                            else UpdateSpiralOpacity();
                        }
                        if (hasBrainDrain)
                        {
                            if (_rampBrainDrainOpacity.HasValue)
                                UpdateBrainDrainBlurOpacity(Math.Max(1, (int)Math.Round(_rampBrainDrainOpacity.Value * 100)));
                            else
                                UpdateBrainDrainBlurOpacity(_currentBrainDrainIntensity);
                        }
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

        if (settings.PinkFilterEnabled && !PinkShowing)
        {
            StartPinkFilter();
        }
        else if (!settings.PinkFilterEnabled && PinkShowing && _timedPinkHolds == 0 && !_sustainedPinkHeld)
        {
            StopPinkFilter();
        }
        else if (PinkShowing)
        {
            UpdatePinkFilterOpacity();
        }

        var spiralPath = GetSpiralPath();
        if (settings.SpiralEnabled && !string.IsNullOrEmpty(spiralPath) && !SpiralShowing)
        {
            _spiralPath = spiralPath;
            StartSpiral();
        }
        else if (!settings.SpiralEnabled && SpiralShowing && _timedSpiralHolds == 0 && !_sustainedSpiralHeld)
        {
            StopSpiral();
        }
        else if (SpiralShowing)
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
        // A user-picked color (suggestion #643) wins over the mod/default retint.
        // Empty setting defers to the active mod's filter color, then hot pink.
        var custom = App.Settings?.Current?.PinkFilterColor;
        if (TryParseHexColor(custom, out var rgb))
            return rgb;
        return App.Mods?.GetFilterColorRgb() ?? (255, 105, 180);
    }

    /// <summary>Parses a "#RRGGBB" (or "RRGGBB") string. Returns false for null/empty/malformed.</summary>
    private static bool TryParseHexColor(string? hex, out (byte R, byte G, byte B) rgb)
    {
        rgb = (255, 105, 180);
        if (string.IsNullOrWhiteSpace(hex)) return false;
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6) return false;
        try
        {
            rgb = (Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
            return true;
        }
        catch { return false; }
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
            "pink_filter"     => () => ShowPinkFilterAdHoc(opacityPercent),
            "spiral"          => () => ShowSpiralAdHoc(),
            "braindrain"      => () => StartBrainDrainBlur(Math.Max(1, opacityPercent)),
            "braindrain_melt" => () => StartBrainDrainBlur(Math.Max(1, opacityPercent), melt: true),
            _ => null
        };

        Action? hide = kind switch
        {
            "pink_filter" => () => StopPinkFilter(),
            "spiral"      => () => StopSpiral(),
            // Melt and plain braindrain are the same underlying overlay - one stop covers both.
            "braindrain" or "braindrain_melt" => () => StopBrainDrainBlur(),
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
                if (kind == "pink_filter")
                {
                    _timedPinkHolds++;
                    if (PinkShowing)
                    {
                        // #573: show() would early-return on an already-showing overlay, silently
                        // swallowing the boost. Bump the live opacity instead (never downward) and
                        // park it in the ramp hold so the settings-sync can't stomp it; the hide
                        // timer restores the previous owner.
                        if (!_bumpPinkActive) { _bumpPinkActive = true; _bumpPrevRampPink = _rampPinkOpacity; }
                        double current = _rampPinkOpacity ?? (App.Settings?.Current?.PinkFilterOpacity ?? 0) / 100.0;
                        double target = Math.Max(current, opacityPercent / 100.0);
                        _rampPinkOpacity = target;
                        ApplyPinkOpacityDirect(target);
                    }
                    else show();
                }
                else if (kind == "spiral")
                {
                    _timedSpiralHolds++;
                    if (SpiralShowing)
                    {
                        if (!_bumpSpiralActive) { _bumpSpiralActive = true; _bumpPrevRampSpiral = _rampSpiralOpacity; }
                        double current = _rampSpiralOpacity ?? (App.Settings?.Current?.SpiralOpacity ?? 0) / 100.0;
                        double target = Math.Max(current, opacityPercent / 100.0);
                        _rampSpiralOpacity = target;
                        ApplySpiralOpacityDirect(target);
                    }
                    else show();
                }
                else
                {
                    // braindrain / braindrain_melt: same hold counter (one underlying overlay).
                    // StartBrainDrainBlur early-returns when it's already showing, so a second
                    // timed effect just rides the live blur - the counter keeps it alive until the
                    // LAST hide fires. No #573-style opacity bump here: braindrain's strength is a
                    // blur radius, not an alpha, and it has no bump/restore machinery.
                    _timedBrainDrainHolds++;
                    show();
                }
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
                    if (_timedPinkHolds == 0)
                    {
                        // Undo any #573 intensity bump: hand opacity back to the previous owner (a
                        // band's parked hold, or the settings-sync). Only touch the ramp hold while
                        // the overlay is still up — a Stop/panic in between already cleared the
                        // holds, and re-parking a stale value would freeze a future overlay (#563).
                        if (_bumpPinkActive)
                        {
                            _bumpPinkActive = false;
                            if (PinkShowing)
                            {
                                _rampPinkOpacity = _bumpPrevRampPink;
                                if (_rampPinkOpacity is double prevPink) ApplyPinkOpacityDirect(prevPink);
                                else _lastAppliedPinkOpacity = -1; // settings-sync re-applies next tick
                            }
                            _bumpPrevRampPink = null;
                        }
                        if (!settings.PinkFilterEnabled) hide();
                    }
                }
                else if (kind == "spiral")
                {
                    if (_timedSpiralHolds > 0) _timedSpiralHolds--;
                    if (_timedSpiralHolds == 0)
                    {
                        if (_bumpSpiralActive)
                        {
                            _bumpSpiralActive = false;
                            if (SpiralShowing)
                            {
                                _rampSpiralOpacity = _bumpPrevRampSpiral;
                                if (_rampSpiralOpacity is double prevSpiral) ApplySpiralOpacityDirect(prevSpiral);
                                else _lastAppliedSpiralOpacity = -1; // settings-sync re-applies next tick
                            }
                            _bumpPrevRampSpiral = null;
                        }
                        if (!settings.SpiralEnabled) hide();
                    }
                }
                else // braindrain / braindrain_melt
                {
                    // Same release discipline as pink/spiral: drop this hold, and only tear the blur
                    // down when nothing else owns it - another timed effect, a sustained Deeper band,
                    // or the user's base Brain Drain feature. Before the counter existed this branch
                    // only checked the setting, so the first timed effect to end killed a co-active
                    // band (the setting is false for everyone while the feature is reworked).
                    if (_timedBrainDrainHolds > 0) _timedBrainDrainHolds--;
                    if (ShouldStopHeldOverlay(settings.BrainDrainEnabled, _timedBrainDrainHolds, _sustainedBrainDrainHeld))
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
            "pink_filter"     => () => ShowPinkFilterAdHoc(opacityPercent),
            "spiral"          => () => ShowSpiralAdHoc(),
            "braindrain"      => () => StartBrainDrainBlur(Math.Max(1, opacityPercent)),
            "braindrain_melt" => () => StartBrainDrainBlur(Math.Max(1, opacityPercent), melt: true),
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
                // Park the ramp-ownership hold at the band's opacity so the 500ms settings-sync
                // (UpdatePinkFilterOpacity/UpdateSpiralOpacity) early-returns and won't stomp a
                // constant-opacity Deeper band back to the user's saved opacity within half a second
                // (#563 symptom-1). Ramp bands overwrite this each Update tick; HideOverlaySustained
                // clears it on band exit, so the lifecycle stays symmetric.
                if (kind == "pink_filter") { _sustainedPinkHeld = PinkShowing; if (PinkShowing) _rampPinkOpacity = opacity; }
                else if (kind == "spiral") { _sustainedSpiralHeld = SpiralShowing; if (SpiralShowing) _rampSpiralOpacity = opacity; }
                // braindrain / braindrain_melt share the one hold + ramp (never co-active). Gated on
                // BrainDrainShowing for the same reason: a show() that no-oped (compositor host gone,
                // GDI failure) must not leave a stale hold blocking a later legitimate teardown.
                else { _sustainedBrainDrainHeld = BrainDrainShowing; if (BrainDrainShowing) _rampBrainDrainOpacity = opacity; }
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
            // Release any Deeper opacity-ramp hold on band exit BEFORE the conditional teardown.
            // A ramp (SetSustainedOverlayOpacity) parks _rampXOpacity, which makes the 500ms
            // settings-sync early-return so it won't fight the ramp. If the base overlay feature
            // is enabled we don't StopPinkFilter/StopSpiral here — so without clearing the hold
            // the overlay stayed frozen at the ramp's final opacity forever (#563). Clearing it
            // returns ownership to the reconciler, which re-applies the user's saved opacity next
            // tick (base-on) or the overlay is torn down (base-off).
            "pink_filter" => () => { _sustainedPinkHeld = false; _rampPinkOpacity = null; _lastAppliedPinkOpacity = -1; if (_timedPinkHolds == 0 && !settings.PinkFilterEnabled) StopPinkFilter(); },
            "spiral"      => () => { _sustainedSpiralHeld = false; _rampSpiralOpacity = null; _lastAppliedSpiralOpacity = -1; if (_timedSpiralHolds == 0 && !settings.SpiralEnabled) StopSpiral(); },
            // Guard braindrain the same way as pink/spiral: a Deeper band exit must not tear down
            // the user's base Brain Drain feature, NOR a timed braindrain effect that is still in
            // flight. Clear ramp ownership + the sustained hold, then stop only when nothing else
            // owns the blur. (#563 consistency)
            "braindrain" or "braindrain_melt" => () =>
            {
                _sustainedBrainDrainHeld = false;
                _rampBrainDrainOpacity = null;
                if (ShouldStopHeldOverlay(settings.BrainDrainEnabled, _timedBrainDrainHolds, _sustainedBrainDrainHeld))
                    StopBrainDrainBlur();
            },
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
                    case "braindrain_melt":   // same underlying overlay, same ramp
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

    /// <summary>
    /// Releases the pink/spiral ramp holds set by <see cref="SetSustainedOverlayOpacity"/>
    /// WITHOUT tearing the overlays down. Called when a session ends but the overlays may
    /// legitimately stay up (the user had them enabled before the session): the 500ms
    /// settings-sync takes ownership again on its next tick and re-applies the user's
    /// saved opacity. Stop/panic paths don't need this — StopPinkFilter/StopSpiral
    /// already clear the holds.
    /// </summary>
    public void ReleaseOpacityRampHolds()
    {
        _rampPinkOpacity = null;
        _rampSpiralOpacity = null;
        _lastAppliedPinkOpacity = -1;
        _lastAppliedSpiralOpacity = -1;
    }

    private void ApplyPinkOpacityDirect(double opacity)
    {
        var (fr, fg, fb) = GetFilterRgb();
        byte a = (byte)Math.Clamp(opacity * 255, 0, 255);
        if (_pinkLayer?.IsActive == true)
            _pinkLayer.Set(fr, fg, fb, opacity);
        foreach (var window in _pinkFilterWindows)
            if (window.Content is Border border &&
                border.Background is System.Windows.Media.SolidColorBrush brush)
                brush.Color = System.Windows.Media.Color.FromArgb(a, fr, fg, fb);
        // Force the next post-ramp settings-sync to re-apply from settings.
        _lastAppliedPinkOpacity = -1;
    }

    /// <summary>
    /// Re-push the filter color to a live tint. The compositor layer is dirty-gated
    /// (#550) and the 500ms settings-sync only re-applies on an opacity change, so a
    /// color-only change (the color picker) needs an explicit re-apply. No-op when the
    /// tint isn't showing — the next Show reads the fresh color from GetFilterRgb().
    /// </summary>
    public void RefreshFilterColor()
    {
        if (!PinkShowing) return;
        // Preserve whoever owns the opacity right now (a ramp, else the saved setting).
        var opacity = _rampPinkOpacity ?? (App.Settings?.Current?.PinkFilterOpacity ?? 0) / 100.0;
        ApplyPinkOpacityDirect(opacity);
    }

    private void ApplySpiralOpacityDirect(double opacity)
    {
        var scaled = opacity * 0.1; // 90% reduction, matching CreateSpiralGifWindow / UpdateSpiralOpacity
        _spiralLayer?.SetOpacity(scaled);
        foreach (var image in _spiralGifImages) image.Opacity = scaled;
        foreach (var media in _spiralMediaElements) media.Opacity = scaled;
        _lastAppliedSpiralOpacity = -1;
    }

    private void ShowPinkFilterAdHoc(int opacityPercent)
    {
        if (PinkShowing) return;
        if (UseCompositor)
        {
            var (fr, fg, fb) = GetFilterRgb();
            GetPinkLayer().Show(fr, fg, fb, opacityPercent / 100.0);
            return;
        }
        try
        {
            // Per-effect monitor target (suggestion #639): -1 follows DualMonitorEnabled.
            var screens = App.ResolveScreens(
                App.Settings?.Current?.PinkFilterTargetMonitor ?? App.MonitorTargetFollowGlobal);

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
        if (SpiralShowing) return;
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
        if (PinkShowing) return;

        if (UseCompositor)
        {
            var s = App.Settings.Current;
            var (fr, fg, fb) = GetFilterRgb();
            GetPinkLayer().Show(fr, fg, fb, s.PinkFilterOpacity / 100.0);
            _lastAppliedPinkOpacity = s.PinkFilterOpacity / 100.0;
            App.Logger?.Debug("Pink filter started on compositor layer at opacity {Opacity}%", s.PinkFilterOpacity);
            return;
        }

        try
        {
            var settings = App.Settings.Current;

            // Per-effect monitor target (suggestion #639): -1 follows DualMonitorEnabled.
            var screens = App.ResolveScreens(settings.PinkFilterTargetMonitor);

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
        _pinkLayer?.Hide(); // both paths cleared unconditionally - the flag may have flipped mid-run
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
        if (_pinkLayer?.IsActive == true)
        {
            _pinkLayer.Set(fr, fg, fb, actualOpacity);
            return;
        }
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
        if (SpiralShowing) return;

        _isGifSpiral = _spiralPath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);

        // Compositor route covers the GIF/animated path only; video spirals (MediaElement)
        // have no layer frame source yet and always use the legacy windows. Frames come from
        // the LEGACY decoder + cache so asset support (pack:// resources, file:// mod overrides,
        // user paths) is identical to the legacy windows by construction.
        if (UseCompositor && _isGifSpiral && _spiralPath != _spiralLayerFailedPath)
        {
            var finalOpacity = (App.Settings.Current.SpiralOpacity / 100.0) * 0.1;

            // Cache hit: show immediately (frames are frozen, shared with the legacy cache).
            if (_spiralFramesCacheKey == _spiralPath && _spiralFramesCache.Count > 0)
            {
                GetSpiralLayer().ShowFrames(_spiralFramesCache, _spiralFramesCacheDelay, finalOpacity);
                _lastAppliedSpiralOpacity = finalOpacity;
                App.Logger?.Debug("Spiral started on compositor layer ({Path}, cached)", _spiralPath);
                return;
            }

            // Cache miss: decode OFF the UI thread. The legacy path decodes synchronously and
            // hitches the whole UI ~1s - felt as a lag spike exactly when a spiral-payload
            // bubble pops. SpiralShowing counts the pending decode so the sync doesn't re-enter.
            if (_spiralLayerDecodePending != null) return;
            var path = _spiralPath;
            _spiralLayerDecodePending = path;
            Task.Run(() =>
            {
                var (frames, delay) = DecodeGifFrames(path);
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                dispatcher.BeginInvoke(() =>
                {
                    _spiralLayerDecodePending = null;
                    if (frames.Count == 0)
                    {
                        // Next StartSpiral routes this path to the legacy windows (no retry churn).
                        _spiralLayerFailedPath = path;
                        App.Logger?.Warning("Spiral: layer decode produced no frames for {Path}; legacy path will be used", path);
                        return;
                    }
                    _spiralFramesCache = frames;
                    _spiralFramesCacheKey = path;
                    _spiralFramesCacheDelay = delay;
                    // The user may have turned the spiral off (or swapped assets) mid-decode.
                    var s = App.Settings.Current;
                    bool stillWanted = _spiralPath == path
                        && (s.SpiralEnabled || _timedSpiralHolds > 0 || _sustainedSpiralHeld);
                    if (stillWanted && UseCompositor)
                    {
                        GetSpiralLayer().ShowFrames(frames, delay, (s.SpiralOpacity / 100.0) * 0.1);
                        _lastAppliedSpiralOpacity = (s.SpiralOpacity / 100.0) * 0.1;
                        App.Logger?.Debug("Spiral started on compositor layer ({Path}, decoded off-thread)", path);
                    }
                });
            });
            return;
        }

        try
        {
            var settings = App.Settings.Current;

            // Per-effect monitor target (suggestion #639): -1 follows DualMonitorEnabled.
            var screens = App.ResolveScreens(settings.SpiralTargetMonitor);

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
        // Per-effect monitor target (suggestion #639): -1 follows DualMonitorEnabled.
        var screens = App.ResolveScreens(settings.SpiralTargetMonitor);

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

            // ModResourceResolver.ResolveUri returns file:// URIs for mod-override assets;
            // File.Exists() on the raw URI string is always false, so unwrap to a local path.
            if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                try { path = new Uri(path).LocalPath; }
                catch (Exception ex) { App.Logger?.Debug("Spiral: bad file:// URI {Path}: {E}", path, ex.Message); }
            }

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

                // Downscale + budget the frame cache (#572 "3.5 GB / laggy" report): frames were
                // decoded at the GIF's native size with no cap — a fullscreen-sized custom spiral
                // (SpiralPath) at 120 Bgra32 frames retains ~1 GB. The spiral is stretched over
                // the whole screen at low opacity, so capping the long side loses nothing
                // visually, and the byte budget bounds the worst case regardless of dimensions.
                const int maxDimension = 1280;
                const long maxCacheBytes = 300L * 1024 * 1024;

                double frameScale = Math.Min(1.0, (double)maxDimension / Math.Max(gif.Width, gif.Height));
                int frameW = Math.Max(1, (int)Math.Round(gif.Width * frameScale));
                int frameH = Math.Max(1, (int)Math.Round(gif.Height * frameScale));
                long bytesPerFrame = (long)frameW * frameH * 4;

                var maxFrames = (int)Math.Min(Math.Min(frameCount, 120),
                    Math.Max(8, maxCacheBytes / Math.Max(1, bytesPerFrame)));
                // Ceiling, not integer division: floor-step keeps frames 0..maxFrames-1 and
                // silently drops the tail for any GIF with maxFrames <= frameCount < 2*maxFrames,
                // breaking the loop point (#683 family). Ceiling subsamples the whole clip evenly;
                // scaling the delay by the stride preserves the wall-clock loop duration.
                var step = Math.Max(1, (int)Math.Ceiling(frameCount / (double)maxFrames));
                if (step > 1)
                    delay = TimeSpan.FromMilliseconds(frameDelayMs * step);
                if (maxFrames < Math.Min(frameCount, 120))
                    App.Logger?.Warning("Spiral: frame cache capped at {Frames} frames ({W}x{H}) to stay under {MB} MB — a smaller spiral GIF will loop smoother",
                        maxFrames, frameW, frameH, maxCacheBytes / (1024 * 1024));

                for (int i = 0; i < frameCount && frames.Count < maxFrames; i += step)
                {
                    gif.SelectActiveFrame(dimension, i);
                    using var frameBitmap = new System.Drawing.Bitmap(frameW, frameH);
                    using (var g = System.Drawing.Graphics.FromImage(frameBitmap))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                        g.DrawImage(gif, 0, 0, frameW, frameH);
                    }

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
        _spiralLayer?.Hide(); // both paths cleared unconditionally - the flag may have flipped mid-run
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

        if (_spiralLayer?.IsActive == true)
        {
            _spiralLayer.SetOpacity(opacity);
            return;
        }

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

    /// <summary><paramref name="melt"/> selects the "braindrain_melt" variant. Melt and plain blur
    /// are ONE overlay - they never co-exist by design - so the flag only picks the render mode on
    /// the compositor layer (Phase 2; today it renders identically). The legacy per-screen-window
    /// path ignores it.</summary>
    public void StartBrainDrainBlur(int intensity, bool melt = false)
    {
        if (BrainDrainShowing) return;

        _currentBrainDrainIntensity = intensity;

        DispatcherHelper.RunOnUISync(() =>
        {
            try
            {
                if (UseCompositor)
                {
                    // Compositor route: capture + blur render on the shared capture-excluded
                    // host; no per-screen layered windows, no WPF BlurEffect rasterization.
                    GetBrainDrainLayer().Start(intensity, melt);
                    App.Logger?.Information("Brain Drain started on compositor layer, intensity {Intensity}%, melt {Melt}", intensity, melt);
                    return;
                }

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
            _sustainedBrainDrainHeld = false; // overlay is gone (incl. force-stop / panic) — drop any stale sustained hold

            // Layer route (checked by activity, not the flag - mid-run flag flips must not strand it).
            if (_brainDrainLayer?.IsActive == true)
            {
                DispatcherHelper.RunOnUISync(() => _brainDrainLayer.Stop());
            }

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
            if (_brainDrainLayer?.IsActive == true)
            {
                _brainDrainLayer.SetIntensity(intensity);
            }
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
    private PhysicalScreenBounds GetPhysicalScreenBounds(System.Windows.Forms.Screen screen, bool quiet = false)
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

                    // quiet: the ReassertBounds reconciler compares every 5s — logging each
                    // probe would be steady churn for the zero-drift common case.
                    if (!quiet)
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
        // While a monitor/DPI change settles, both the screen cache and the HWND rects are in
        // flux — repositioning against stale geometry would fight the OS mid-storm (the same
        // reason the recreate path above defers). The post-settle kick heals any drift.
        if (Services.UI.DisplayChangeCoordinator.SpawnsSuppressed) return;

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
            // Mid display-change storm the geometry is in flux; the post-settle kick heals drift.
            if (Services.UI.DisplayChangeCoordinator.SpawnsSuppressed) return;

            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            if (window.Tag is not string deviceName) return;

            var screen = App.GetAllScreensCached().FirstOrDefault(s => s.DeviceName == deviceName);
            if (screen == null) return;   // monitor unplugged — RecreateOverlays handles that case

            var expected = GetPhysicalScreenBounds(screen, quiet: true);
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

        // While a mandatory/session video is playing, keep the overlays in the topmost band but
        // pinned BELOW the video window instead of forcing them to the front. Otherwise this
        // reconciler (and NotifyTopWindowClosed after each clip) buries the video behind the
        // spiral/pink filter; the video window is deliberately non-re-raising, so it never
        // recovers, and with autonomy chaining clips the next one shows "only by flashes" (#497).
        IntPtr videoHwnd = IntPtr.Zero;
        try
        {
            if (App.Video?.IsPlaying == true && App.Video.PrimaryVideoWindow is Window vw)
                videoHwnd = new System.Windows.Interop.WindowInteropHelper(vw).Handle;
        }
        catch { }

        // A live Deeper enhancement overlay band means the overlay IS the enhanced video's effect and
        // must sit ABOVE it, restoring the pre-compositor behavior (the #497 below-video pin is only
        // correct for ambient/session overlays that happen to co-exist with a mandatory video).
        bool aboveVideo = DeeperOverlayBandActive;

        foreach (var list in new[] { _pinkFilterWindows, _spiralWindows, _brainDrainBlurWindows })
        {
            foreach (var window in list)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) continue;
                ReassertOne(hwnd, videoHwnd, aboveVideo, force, ref anyRecovered);
            }
        }

        // Compositor hosts carry the SAME fullscreen effects when the unified renderer is on,
        // but only assert HWND_TOPMOST on their show/topology edges — reconcile them exactly
        // like the legacy windows or a later topmost raise (chaos chrome ~1/s, a mandatory
        // video) buries every layer; worse, a host re-shown mid-video pins itself above the
        // deliberately non-re-raising video window (#497's shape all over again).
        try
        {
            if (App.Compositor is { } engine)
                foreach (var hostHwnd in engine.GetVisibleHostHandles())
                    ReassertOne(hostHwnd, videoHwnd, aboveVideo, force, ref anyRecovered);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ReassertZOrder (compositor hosts) failed: {Error}", ex.Message);
        }
        return anyRecovered;
    }

    /// <summary>One window's worth of ReassertZOrder. Normally: below the active video (#497), else
    /// topmost. When <paramref name="aboveVideo"/> (a live Deeper overlay band), the overlay is the
    /// enhanced video's own effect, so pin it to the top of the topmost band ABOVE the video instead.</summary>
    /// <summary>Where a single overlay window should be pinned relative to a (possibly playing) video.</summary>
    internal enum ZOrderAction { None, PinBelowVideo, PinTopmost }

    /// <summary>
    /// Pure z-order decision for one overlay window. Extracted from <see cref="ReassertOne"/> so the
    /// Deeper-band-above-video rule (and the #497 below-video pin it must preserve) can be unit-tested.
    /// <paramref name="aboveVideo"/> — a live Deeper enhancement band, so the overlay IS the video's own
    /// effect and must sit above it; otherwise a co-existing video keeps overlays pinned just below it.
    /// </summary>
    internal static ZOrderAction ResolveZOrderAction(bool hasVideo, bool isVideoWindow, bool aboveVideo, bool needsPin, bool force)
    {
        if (hasVideo && !isVideoWindow && !aboveVideo)
            return ZOrderAction.PinBelowVideo;
        if (needsPin || force || aboveVideo)
            return ZOrderAction.PinTopmost;
        return ZOrderAction.None;
    }

    private static void ReassertOne(IntPtr hwnd, IntPtr videoHwnd, bool aboveVideo, bool force, ref bool anyRecovered)
    {
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        bool needsPin = (exStyle & WS_EX_TOPMOST) == 0;

        switch (ResolveZOrderAction(videoHwnd != IntPtr.Zero, hwnd == videoHwnd, aboveVideo, needsPin, force))
        {
            case ZOrderAction.PinBelowVideo:
                // Insert directly below the active video window: stays topmost (above the
                // desktop and other apps) but under the video the user is meant to watch.
                SetWindowPos(hwnd, videoHwnd, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                if (needsPin) anyRecovered = true;
                break;
            case ZOrderAction.PinTopmost:
                // Top of the topmost band. Above a playing video too — the mandatory video window is
                // deliberately non-re-raising, so a Deeper band's tint set here stays over it. Re-applied
                // every reconcile tick while aboveVideo holds (cheap; no fight since the video won't re-raise).
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                if (needsPin) anyRecovered = true;
                break;
        }
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

        // Only start/update brain drain if the overlay service is running (engine is active).
        // Unconditional stop: this is a full teardown (service not running), same as Stop()/panic —
        // StopBrainDrainBlur clears the sustained hold so nothing stale survives it.
        if (!_isRunning)
        {
            // Don't start brain drain if engine isn't running
            StopBrainDrainBlur();
            return;
        }

        bool featureWantsIt = settings.BrainDrainEnabled && settings.IsLevelUnlocked(70); // Level 70 requirement for Brain Drain
        if (featureWantsIt)
        {
            if (!BrainDrainShowing)
            {
                StartBrainDrainBlur((int)settings.BrainDrainIntensity);
            }
            else if (!_rampBrainDrainOpacity.HasValue)
            {
                // Already running, just update intensity (a Deeper ramp owns it when active).
                UpdateBrainDrainBlurOpacity((int)settings.BrainDrainIntensity);
            }
        }
        // The base feature being off is NOT permission to kill an ad-hoc blur: a timed effect or a
        // sustained Deeper band owns it until its own hide fires. Without this guard every
        // RefreshOverlays() (autonomy, remote control, the user toggling pink/spiral) silently
        // killed a live Deeper braindrain band mid-video, since the base feature is off for everyone
        // while it's being reworked. Same discipline as pink/spiral in RefreshOverlays.
        else if (ShouldStopHeldOverlay(featureWantsIt, _timedBrainDrainHolds, _sustainedBrainDrainHeld))
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
        try { _brainDrainLayer?.Stop(); } catch { /* shutdown path - GDI freed by OS anyway */ }

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
