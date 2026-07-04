using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Compositor;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Services.Mod;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Settings;
using SkiaSharp;
using LibVLCSharp.Shared;

namespace ConditioningControlPanel.Avalonia.Services.Overlays;

/// <summary>
/// Avalonia implementation of the screen-overlay subsystem.
/// Supports pink filter, spiral (GIF/static image), brain-drain darkening/pulsing overlay,
/// and ad-hoc sustained/timed overlays.
/// </summary>
public sealed class AvaloniaOverlayService : IOverlayService, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IScreenProvider _screens;
    private readonly IAppEnvironment _environment;
    private readonly LibVLC _libVlc;
    private readonly IModService? _mods;
    private readonly ILogger<AvaloniaOverlayService>? _logger;
    private readonly object _sync = new();
    private readonly CompositorEngine? _compositor;
    private readonly PinkTintLayer _pinkTintLayer;
    private readonly SpiralLayer _spiralLayer;
    private readonly BrainDrainLayer _brainDrainLayer;
    private readonly Dictionary<string, OverlayHold> _overlayHolds = new();
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private bool _isRunning;
    private bool _isDisposed;
    private int _lastAppliedPinkOpacity = -1;
    private Color _lastAppliedPinkColor = Colors.Transparent;
    private int _lastAppliedSpiralOpacity = -1;
    private int _lastAppliedBrainDrainIntensity = -1;
    private int? _adHocPinkOpacity;
    private int? _adHocSpiralOpacity;
    private int? _adHocBrainDrainIntensity;
    private string _lastSpiralCacheKey = "";

    public AvaloniaOverlayService(
        ISettingsService settings,
        IScreenProvider screens,
        IAppEnvironment environment,
        LibVLC libVlc,
        ILogger<AvaloniaOverlayService>? logger = null,
        CompositorEngine? compositor = null,
        IModService? mods = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _screens = screens ?? throw new ArgumentNullException(nameof(screens));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _libVlc = libVlc ?? throw new ArgumentNullException(nameof(libVlc));
        _logger = logger;
        _compositor = compositor;
        _mods = mods;
        // Live-reactive themed color: a mid-session mod/theme switch must recolor the tint
        // overlay AND the spiral immediately (OnModChanged). Layers stay dumb — only the
        // service re-resolves the active mod's FilterColor and re-pushes it.
        if (_mods != null)
            _mods.ActiveModChanged += OnModChanged;
        _pinkTintLayer = new PinkTintLayer();
        _spiralLayer = new SpiralLayer();
        _brainDrainLayer = new BrainDrainLayer();

        // Register the layers once for the service lifetime instead of per Start/Stop.
        // Registration only makes them available to the engine — IsActive is content-driven,
        // so idle layers render nothing and cost nothing. This keeps the ad-hoc brain-drain
        // path (Deeper actions, 'deeper' voice command, chaos payloads) functional while the
        // service is stopped, matching WPF's ShowOverlayTimed which creates blur windows on
        // demand; previously the layer was unregistered in Stop() and ad-hoc brain drain was
        // a silent no-op outside sessions.
        _compositor?.RegisterLayer(_pinkTintLayer);
        _compositor?.RegisterLayer(_spiralLayer);
        _compositor?.RegisterLayer(_brainDrainLayer);

        _updateTimer.Tick += UpdateOverlays;
        _screens.ScreensChanged += (_, _) => RefreshForMultiMonitorChange();
    }

    public bool IsRunning => _isRunning;

    // Set by remote-control commands (AvaloniaRemoteCommandExecutor) but intentionally never
    // consulted: WPF's OverlayService.BypassLevelCheck is write-only in exactly the same way,
    // so reading it here would be a parity break. If it should ever become functional, wire
    // it in BOTH heads as a product decision.
    public bool BypassLevelCheck { get; set; }

    public void Start()
    {
        if (_isRunning || _isDisposed) return;
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
        {
            _logger?.LogDebug("AvaloniaOverlayService: overlays are not supported on mobile; Start is a no-op");
            return;
        }

        _isRunning = true;
        _compositor?.Start();
        Dispatcher.UIThread.Invoke(RefreshOverlays);
        _updateTimer.Start();
        _logger?.LogInformation("AvaloniaOverlayService started (compositor layers)");
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;
        _updateTimer.Stop();
        Dispatcher.UIThread.Invoke(() =>
        {
            StopPinkFilter();
            StopSpiral();
            StopBrainDrain();
            StopAllSustainedOverlays();
        });
        // Layers stay registered (registered once in the ctor); zeroing their content above
        // deactivates them, and the engine auto-stops once nothing is active.
        _logger?.LogInformation("AvaloniaOverlayService stopped");
    }

    public void RefreshOverlays()
    {
        if (!_isRunning) return;
        Dispatcher.UIThread.Invoke(() =>
        {
            var settings = _settings.Current;
            if (settings == null) return;

            // Render the visual stack bottom-to-top so later windows sit above earlier
            // ones within the topmost layer: brain-drain -> spiral -> pink filter.
            // WPF parity (OverlayService.cs:203, :2190): the persistent BrainDrainEnabled
            // setting is gated on the level-70 unlock at the service, not just the UI card;
            // ad-hoc brain drain (Deeper actions, voice, chaos payloads) is deliberately
            // ungated, exactly like WPF's ShowOverlayTimed path.
            var brainDrainWanted =
                (settings.BrainDrainEnabled && settings.IsLevelUnlocked(AppSettings.BrainDrainUnlockLevel))
                || _adHocBrainDrainIntensity.HasValue;
            if (brainDrainWanted)
            {
                var intensity = _adHocBrainDrainIntensity ?? settings.BrainDrainIntensity;
                StartBrainDrain(intensity);
            }
            else
            {
                StopBrainDrain();
            }

            var spiralPath = GetSpiralPath();
            var spiralWanted = (settings.SpiralEnabled || _adHocSpiralOpacity.HasValue) && !string.IsNullOrEmpty(spiralPath);
            if (spiralWanted)
            {
                if (!string.Equals(spiralPath, _lastSpiralCacheKey, StringComparison.OrdinalIgnoreCase))
                    StartSpiral(spiralPath, _adHocSpiralOpacity ?? settings.SpiralOpacity);
                else
                    UpdateSpiralOpacity();
            }
            else
            {
                StopSpiral();
            }

            var pinkWanted = settings.PinkFilterEnabled || _adHocPinkOpacity.HasValue;
            if (pinkWanted)
            {
                // Live pickup with dedupe (WPF UpdateOverlays -> UpdatePinkFilterOpacity):
                // lot-2 ramps mutate PinkFilterOpacity every 1-2s and this 500ms tick applies
                // it; the (opacity, color) compare keeps unchanged ticks allocation-free.
                UpdatePinkFilterOpacity();
            }
            else
            {
                StopPinkFilter();
            }
        });
    }

    public void PulseOverlays()
    {
        if (!_isRunning) return;
        Dispatcher.UIThread.Invoke(() =>
        {
            var settings = _settings.Current;
            if (settings == null) return;

            var hasPink = settings.PinkFilterEnabled || _adHocPinkOpacity.HasValue;
            var hasSpiral = settings.SpiralEnabled || _adHocSpiralOpacity.HasValue;
            var hasBrainDrain = settings.BrainDrainEnabled || _adHocBrainDrainIntensity.HasValue;

            if (hasPink)
            {
                var boosted = Math.Min((_adHocPinkOpacity ?? settings.PinkFilterOpacity) * 2, 100);
                _pinkTintLayer.SetColor(GetFilterColor(boosted), boosted / 100.0);
                _lastAppliedPinkOpacity = -1;
            }

            if (hasSpiral)
            {
                var boostedOpacity = Math.Min((_adHocSpiralOpacity ?? settings.SpiralOpacity) * 2, 100);
                _spiralLayer.SetSource(_lastSpiralCacheKey, SpiralLayerOpacity(boostedOpacity));
                _lastAppliedSpiralOpacity = -1;
            }

            if (hasBrainDrain)
            {
                var boostedIntensity = Math.Min((_adHocBrainDrainIntensity ?? settings.BrainDrainIntensity) * 2, 100);
                UpdateBrainDrainIntensity(boostedIntensity);
                _lastAppliedBrainDrainIntensity = -1;
            }

            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                Dispatcher.UIThread.Invoke(() =>
                {
                    if (hasPink) UpdatePinkFilterOpacity();
                    if (hasSpiral) UpdateSpiralOpacity();
                    if (hasBrainDrain) UpdateBrainDrainIntensity();
                });
            });
        });
    }

    public void RefreshForMultiMonitorChange()
    {
        if (!_isRunning) return;
        Dispatcher.UIThread.Invoke(() =>
        {
            var settings = _settings.Current;
            if (settings == null) return;

            StopPinkFilter();
            StopSpiral();
            StopBrainDrain();

            if (settings.PinkFilterEnabled || _adHocPinkOpacity.HasValue)
                StartPinkFilter(_adHocPinkOpacity ?? settings.PinkFilterOpacity);

            var spiralPath = GetSpiralPath();
            if ((settings.SpiralEnabled || _adHocSpiralOpacity.HasValue) && !string.IsNullOrEmpty(spiralPath))
                StartSpiral(spiralPath, _adHocSpiralOpacity ?? settings.SpiralOpacity);

            // Same level-70 gate as RefreshOverlays (WPF parity: OverlayService.cs:381).
            if ((settings.BrainDrainEnabled && settings.IsLevelUnlocked(AppSettings.BrainDrainUnlockLevel))
                || _adHocBrainDrainIntensity.HasValue)
                StartBrainDrain(_adHocBrainDrainIntensity ?? settings.BrainDrainIntensity);
        });
    }

    public void ShowOverlayTimed(string kind, int durationMs, double opacity)
    {
        if (_isDisposed) return;

        var normalizedKind = NormalizeOverlayKind(kind);
        if (normalizedKind == null)
        {
            _logger?.LogDebug("AvaloniaOverlayService.ShowOverlayTimed: unsupported kind {Kind}", kind);
            return;
        }

        var safeDurationMs = Math.Max(50, durationMs);
        var clampedOpacity = Math.Clamp(opacity, 0.0, 1.0);

        Dispatcher.UIThread.Invoke(() =>
        {
            // Ad-hoc overlays (Deeper actions, voice commands, chaos payloads) work while the
            // service is stopped for ALL kinds: WPF's ShowOverlayTimed creates its windows on
            // demand regardless of Start/Stop. The engine Start lives in the Start* methods;
            // activation is content-driven, so the engine auto-stops again after Hide.
            ShowSustainedOverlayInternal(normalizedKind, clampedOpacity);

            // WPF ownership protocol: timed holds are ref-counted and independent of any
            // sustained hold or persistent setting; each expiry releases only its own hold.
            var hold = GetHold(normalizedKind);
            hold.TimedHolds++;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(safeDurationMs) };
            hold.Timers.Add(timer);
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                hold.Timers.Remove(timer);
                hold.TimedHolds = Math.Max(0, hold.TimedHolds - 1);
                ReleaseOverlayIfUnheld(normalizedKind, hold);
            };
            timer.Start();
        });
    }

    public void ShowOverlaySustained(string kind, double opacity)
    {
        if (_isDisposed) return;

        var normalizedKind = NormalizeOverlayKind(kind);
        if (normalizedKind == null)
        {
            _logger?.LogDebug("AvaloniaOverlayService.ShowOverlaySustained: unsupported kind {Kind}", kind);
            return;
        }

        var clampedOpacity = Math.Clamp(opacity, 0.0, 1.0);

        Dispatcher.UIThread.Invoke(() =>
        {
            // No _isRunning gate: ad-hoc sustained overlays work while the service is stopped
            // (WPF parity, see ShowOverlayTimed).

            // Always (re)apply, even during a timed hold: the ad-hoc opacity is authoritative
            // here (unlike WPF's window-exists early-return), and a sustained hold arriving
            // mid-timed-hold must survive that hold's expiry (WPF _sustainedPinkHeld).
            GetHold(normalizedKind).SustainedHeld = true;
            ShowSustainedOverlayInternal(normalizedKind, clampedOpacity);
        });
    }

    public void HideOverlaySustained(string kind)
    {
        if (_isDisposed) return;

        var normalizedKind = NormalizeOverlayKind(kind);
        if (normalizedKind == null) return;

        Dispatcher.UIThread.Invoke(() =>
        {
            var hold = GetHold(normalizedKind);
            hold.SustainedHeld = false;

            if (normalizedKind == "braindrain")
            {
                // WPF parity (OverlayService.cs:622): explicit brain-drain hide is
                // unconditional; the 500ms refresh restores it if BrainDrainEnabled.
                _adHocBrainDrainIntensity = null;
                StopBrainDrain();
                return;
            }

            ReleaseOverlayIfUnheld(normalizedKind, hold);
        });
    }

    /// <summary>
    /// Tears an ad-hoc overlay down only when its last co-owner releases it: no timed holds,
    /// no sustained hold, and the persistent setting does not keep it on. When the persistent
    /// setting holds it, the overlay is re-applied at its own opacity immediately so a timed
    /// expiry never blinks a user-enabled overlay off (WPF expiry checks the setting too).
    /// </summary>
    private void ReleaseOverlayIfUnheld(string kind, OverlayHold hold)
    {
        if (hold.TimedHolds > 0 || hold.SustainedHeld) return;

        var settings = _settings.Current;
        switch (kind)
        {
            case "pink":
                _adHocPinkOpacity = null;
                if (settings?.PinkFilterEnabled == true) StartPinkFilter(settings.PinkFilterOpacity);
                else StopPinkFilter();
                break;
            case "spiral":
                _adHocSpiralOpacity = null;
                if (settings?.SpiralEnabled == true)
                {
                    var path = GetSpiralPath();
                    if (string.IsNullOrEmpty(path)) StopSpiral();
                    else if (!string.Equals(path, _lastSpiralCacheKey, StringComparison.OrdinalIgnoreCase))
                        StartSpiral(path, settings.SpiralOpacity);
                    else UpdateSpiralOpacity();
                }
                else StopSpiral();
                break;
            case "braindrain":
                _adHocBrainDrainIntensity = null;
                if (settings?.BrainDrainEnabled == true && settings.IsLevelUnlocked(AppSettings.BrainDrainUnlockLevel))
                    StartBrainDrain(settings.BrainDrainIntensity);
                else StopBrainDrain();
                break;
        }
    }

    private OverlayHold GetHold(string kind) =>
        _overlayHolds.TryGetValue(kind, out var hold) ? hold : _overlayHolds[kind] = new OverlayHold();

    public void SetSustainedOverlayOpacity(string kind, double opacity)
    {
        if (_isDisposed) return;

        var normalizedKind = NormalizeOverlayKind(kind);
        if (normalizedKind == null) return;

        var clampedOpacity = Math.Clamp(opacity, 0.0, 1.0);

        Dispatcher.UIThread.Invoke(() =>
        {
            // No _isRunning gates: an ad-hoc overlay shown while the service is stopped must
            // still honor live opacity ramps (Deeper enhancement ramps drive this path).
            switch (normalizedKind)
            {
                case "pink":
                    _adHocPinkOpacity = (int)Math.Round(clampedOpacity * 100);
                    StartPinkFilter(_adHocPinkOpacity.Value);
                    break;
                case "spiral":
                    _adHocSpiralOpacity = (int)Math.Round(clampedOpacity * 100);
                    var spiralPath = GetSpiralPath();
                    if (!string.IsNullOrEmpty(spiralPath))
                        StartSpiral(spiralPath, _adHocSpiralOpacity.Value);
                    break;
                case "braindrain":
                    _adHocBrainDrainIntensity = Math.Max(1, (int)Math.Round(clampedOpacity * 100));
                    StartBrainDrain(_adHocBrainDrainIntensity.Value);
                    break;
            }
        });
    }

    public void ReleaseOpacityRampHolds()
    {
        if (_isDisposed) return;

        // WPF parity (OverlayService.ReleaseOpacityRampHolds): drop the pink/spiral ramp holds
        // so the 500ms settings-sync (UpdateOverlays) re-takes ownership and re-applies the
        // user's saved opacity. Reset BOTH dedupe sentinels per path, else the next tick sees
        // "unchanged" and never re-applies, leaving the ramped value stuck on screen.
        Dispatcher.UIThread.Invoke(() =>
        {
            _adHocPinkOpacity = null;
            _adHocSpiralOpacity = null;
            _lastAppliedPinkOpacity = -1;
            _lastAppliedPinkColor = Colors.Transparent;
            _lastAppliedSpiralOpacity = -1;
        });
    }

    public void WarmSpiralCache()
    {
        // WPF parity (OverlayService.WarmSpiralCache): pre-decode the spiral GIF frames off
        // the UI thread so the first chaos spiral of a run does not hitch. No-op if warm.
        try
        {
            var path = GetSpiralPath();
            if (!string.IsNullOrEmpty(path))
                _spiralLayer.Preload(path);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("WarmSpiralCache failed: {Error}", ex.Message);
        }
    }

    private void UpdateOverlays(object? sender, EventArgs e)
    {
        if (!_isRunning) return;
        RefreshOverlays();
    }

    private void ShowSustainedOverlayInternal(string kind, double opacity)
    {
        var opacityPercent = (int)Math.Round(opacity * 100);
        var settings = _settings.Current;

        switch (kind)
        {
            case "pink":
                _adHocPinkOpacity = opacityPercent;
                StartPinkFilter(opacityPercent);
                break;
            case "spiral":
                _adHocSpiralOpacity = opacityPercent;
                var spiralPath = GetSpiralPath();
                if (string.IsNullOrEmpty(spiralPath))
                {
                    _logger?.LogDebug("AvaloniaOverlayService.ShowOverlaySustained: no spiral path configured");
                    return;
                }
                if (!string.Equals(spiralPath, _lastSpiralCacheKey, StringComparison.OrdinalIgnoreCase))
                    StartSpiral(spiralPath, opacityPercent);
                else
                    UpdateSpiralOpacity();
                break;
            case "braindrain":
                _adHocBrainDrainIntensity = Math.Max(1, opacityPercent);
                StartBrainDrain(_adHocBrainDrainIntensity.Value);
                break;
        }
    }

    private static string? NormalizeOverlayKind(string kind)
    {
        return kind?.ToLowerInvariant() switch
        {
            "pink" or "pink_filter" => "pink",
            "spiral" => "spiral",
            "braindrain" or "blur" => "braindrain",
            _ => null
        };
    }

    private void StopAllSustainedOverlays()
    {
        foreach (var hold in _overlayHolds.Values)
        {
            foreach (var timer in hold.Timers)
                timer.Stop();
            hold.Timers.Clear();
            hold.TimedHolds = 0;
            hold.SustainedHeld = false;
        }
        _adHocPinkOpacity = null;
        _adHocSpiralOpacity = null;
        _adHocBrainDrainIntensity = null;
    }

    private void StartPinkFilter(int opacityPercent)
    {
        // Ad-hoc pink must work while the service is stopped (WPF parity), and the engine
        // may have auto-stopped in the meantime; Start() is idempotent and lazily recreates
        // the compositor windows.
        _compositor?.Start();
        var color = GetFilterColor(opacityPercent);
        _pinkTintLayer.SetColor(color, opacityPercent / 100.0);
        _lastAppliedPinkOpacity = opacityPercent;
        _lastAppliedPinkColor = color;
    }

    private void StopPinkFilter()
    {
        _pinkTintLayer.SetColor(Colors.Transparent, 0);
        _lastAppliedPinkOpacity = -1;
        _lastAppliedPinkColor = Colors.Transparent;
    }

    private void UpdatePinkFilterOpacity()
    {
        var settings = _settings.Current;
        if (settings == null) return;

        var opacity = _adHocPinkOpacity ?? settings.PinkFilterOpacity;
        var color = GetFilterColor(opacity);
        if (opacity == _lastAppliedPinkOpacity && color == _lastAppliedPinkColor) return;

        // Applying a change may be the OFF->ON transition after the engine auto-stopped
        // (all overlays idle >500ms closes the compositor windows); restart it.
        if (opacity > 0) _compositor?.Start();
        _pinkTintLayer.SetColor(color, opacity / 100.0);
        _lastAppliedPinkOpacity = opacity;
        _lastAppliedPinkColor = color;
    }

    /// <summary>
    /// WPF parity (OverlayService.cs:469-472 GetFilterRgb): the pink tint derives from the
    /// active mod's FilterColor (which itself falls back to the mod accent), with hot pink
    /// (255,105,180) as the final fallback — not from a theme resource.
    /// </summary>
    private Color GetFilterColor(int opacityPercent)
    {
        var alpha = (byte)Math.Clamp(opacityPercent / 100.0 * 255, 0, 255);
        var (r, g, b) = GetFilterRgb();
        return new Color(alpha, r, g, b);
    }

    private (byte R, byte G, byte B) GetFilterRgb()
    {
        try
        {
            var hex = _mods?.GetFilterColorHex();
            if (!string.IsNullOrWhiteSpace(hex))
            {
                var c = Color.Parse(hex);
                return (c.R, c.G, c.B);
            }
        }
        catch { /* malformed mod color — fall through to hot pink */ }
        return (255, 105, 180);
    }

    /// <summary>
    /// The active mod's tint color as an Avalonia color (FilterColor, falling back to the
    /// mod accent, then hot pink). Drives BOTH the tint overlay (PinkTintLayer) and the
    /// spiral tint (SpiralLayer) so the whole overlay stack re-skins per theme — green under
    /// Dronification, pink under CCP Default, etc. WPF keys the filter off FilterColor
    /// (OverlayService.cs:469-472 GetFilterRgb); the spiral tint is an Avalonia enhancement
    /// (WPF draws the spiral at its native image colors) for per-theme overlay+spiral color.
    /// </summary>
    private Color ThemedColor
    {
        get
        {
            try
            {
                var hex = _mods?.GetFilterColorHex();
                if (!string.IsNullOrWhiteSpace(hex)) return Color.Parse(hex);
            }
            catch { /* malformed mod color — fall through to hot pink */ }
            return Color.FromRgb(255, 105, 180);
        }
    }

    private static SKColor ToSk(Color c) => new SKColor(c.R, c.G, c.B);

    /// <summary>
    /// Live-reactive themed recolor: a mid-session mod/theme switch re-resolves the tint
    /// color and re-pushes it to the tint overlay and spiral immediately. The 500ms
    /// settings-sync tick also catches the running case, but this covers ad-hoc overlays
    /// shown while the service is stopped (Deeper ramps) and removes the tick latency.
    /// </summary>
    private void OnModChanged(object? sender, ModPackage _)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var themed = ToSk(ThemedColor);
            if (_pinkTintLayer.IsActive)
            {
                _lastAppliedPinkColor = Colors.Transparent; // force past the (opacity,color) dedupe
                UpdatePinkFilterOpacity();
            }
            if (_spiralLayer.IsActive)
                _spiralLayer.SetColor(themed);
        });
    }

    private string GetSpiralPath()
    {
        var settings = _settings.Current;
        if (settings == null) return "";

        if (!string.IsNullOrEmpty(settings.SpiralPath) && File.Exists(settings.SpiralPath))
            return settings.SpiralPath;

        // Prefer an active-mod override, then the shipped Spirals folder, then the assets folder.
        var resolver = App.Services?.GetService<AvaloniaModResourceResolver>();
        var modUri = resolver?.ResolveUri("spiral.gif");
        if (!string.IsNullOrEmpty(modUri) && modUri.StartsWith("file://", StringComparison.Ordinal))
        {
            var modPath = modUri.Substring(7);
            if (File.Exists(modPath)) return modPath;
        }

        var fallback = Path.Combine(_environment.BaseDirectory, "Spirals", "spiral.gif");
        if (File.Exists(fallback)) return fallback;

        var assetsFallback = Path.Combine(_environment.EffectiveAssetsPath, "spiral.gif");
        if (File.Exists(assetsFallback)) return assetsFallback;

        return GetBundledSpiralPath();
    }

    // WPF parity: its chain ends at the embedded pack://.../Resources/spiral.gif, so a default
    // spiral always exists. SpiralLayer decodes from a file path, so the bundled avares asset is
    // extracted once into the user-data cache and reused from there.
    private string GetBundledSpiralPath()
    {
        try
        {
            var cachePath = Path.Combine(_environment.UserDataPath, "cache", "spiral.gif");
            if (File.Exists(cachePath)) return cachePath;

            var uri = new Uri("avares://CCP.Avalonia/Assets/spiral.gif");
            if (!global::Avalonia.Platform.AssetLoader.Exists(uri)) return "";

            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            using var stream = global::Avalonia.Platform.AssetLoader.Open(uri);
            var tmpPath = cachePath + ".tmp";
            using (var file = File.Create(tmpPath))
                stream.CopyTo(file);
            File.Move(tmpPath, cachePath, overwrite: true);
            return cachePath;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to extract bundled spiral.gif; spiral stays disabled without a user spiral");
            return "";
        }
    }

    /// <summary>
    /// Map a 0-100 spiral opacity percentage to the layer's paint alpha. WPF parity: every
    /// spiral path applies a 90% reduction ("Very subtle opacity - 90% reduction",
    /// WPF OverlayService.cs:1201 and UpdateSpiralOpacity/ApplySpiralOpacityDirect/PulseOverlays),
    /// so the setting's 0-50 range maps to 0-5% on-screen alpha. Deliberate product decision —
    /// do not "clean up" this factor.
    /// </summary>
    private static double SpiralLayerOpacity(int opacityPercent) => (opacityPercent / 100.0) * 0.1;

    private void StartSpiral(string path, int opacityPercent)
    {
        // Same as StartPinkFilter/StartBrainDrain: ad-hoc spiral works while the service is
        // stopped and the engine may have auto-stopped; Start() is idempotent.
        _compositor?.Start();
        _spiralLayer.SetSource(path, SpiralLayerOpacity(opacityPercent));
        _spiralLayer.SetColor(ToSk(ThemedColor)); // theme-driven spiral tint (drone => green)
        _lastAppliedSpiralOpacity = opacityPercent;
        _lastSpiralCacheKey = path;
    }

    private void StopSpiral()
    {
        _spiralLayer.ClearSource();
        _lastAppliedSpiralOpacity = -1;
        _lastSpiralCacheKey = "";
    }

    private void UpdateSpiralOpacity()
    {
        var settings = _settings.Current;
        if (settings == null) return;

        var opacity = _adHocSpiralOpacity ?? settings.SpiralOpacity;
        if (opacity == _lastAppliedSpiralOpacity) return;

        if (opacity > 0) _compositor?.Start(); // OFF->ON transition may follow an engine auto-stop
        _spiralLayer.SetSource(_lastSpiralCacheKey, SpiralLayerOpacity(opacity));
        _spiralLayer.SetColor(ToSk(ThemedColor)); // keep the spiral tint on-theme across reloads
        _lastAppliedSpiralOpacity = opacity;
    }

    private void StartBrainDrain(int intensity)
    {
        // Ad-hoc brain drain must work while the service is stopped (WPF parity), and the
        // engine may have auto-stopped in the meantime; Start() is idempotent and lazily
        // recreates the compositor windows.
        _compositor?.Start();
        // WPF parity: capture FPS is 60 with BrainDrainHighRefresh, else 30 (WPF additionally
        // caps by PerformanceProfile tier, which is not ported to the Avalonia head).
        _brainDrainLayer.SetCaptureFps(_settings.Current?.BrainDrainHighRefresh == true ? 60 : 30);
        _brainDrainLayer.SetIntensity(intensity);
        _lastAppliedBrainDrainIntensity = intensity;
    }

    private void StopBrainDrain()
    {
        _brainDrainLayer.SetIntensity(0);
        _lastAppliedBrainDrainIntensity = -1;
    }

    private void UpdateBrainDrainIntensity(int? intensityOverride = null)
    {
        var settings = _settings.Current;
        if (settings == null) return;

        var intensity = intensityOverride ?? _adHocBrainDrainIntensity ?? settings.BrainDrainIntensity;
        if (intensity == _lastAppliedBrainDrainIntensity) return;

        _brainDrainLayer.SetIntensity(intensity);
        _lastAppliedBrainDrainIntensity = intensity;
    }

    private static bool IsStaticImageExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".webp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".bmp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVideoExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return string.Equals(ext, ".mp4", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".webm", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".mov", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".avi", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".mkv", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<ScreenInfo> GetScreens()
    {
        try
        {
            var all = _screens.GetAllScreens();
            if (all.Count == 0)
                return new[] { new ScreenInfo("fallback", new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), 1.0) };

            if (_settings.Current?.DualMonitorEnabled != true)
            {
                var primary = _screens.GetPrimaryScreen() ?? all[0];
                return new[] { primary };
            }
            return all;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("AvaloniaOverlayService: could not enumerate screens: {Error}", ex.Message);
            return new[] { new ScreenInfo("fallback", new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), 1.0) };
        }
    }

    public void NotifyTopWindowOpened() { }

    // WPF parity: OverlayService re-pins its windows when a topmost app window closes above
    // them (the closing window can steal the top band); the compositor exposes a force kick.
    public void NotifyTopWindowClosed() => _compositor?.ReassertTopmostNow();

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
        _updateTimer.Tick -= UpdateOverlays;
        if (_mods != null)
            _mods.ActiveModChanged -= OnModChanged;
        _brainDrainLayer.Dispose(); // frees cached screen-capture frames
    }

    /// <summary>
    /// Co-ownership record for one ad-hoc overlay kind (WPF ownership protocol: ref-counted
    /// timed holds + a sustained flag; the persistent setting is the third co-owner and is
    /// consulted at release time).
    /// </summary>
    private sealed class OverlayHold
    {
        public int TimedHolds;
        public bool SustainedHeld;
        public readonly List<DispatcherTimer> Timers = new();
    }
}
