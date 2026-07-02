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
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Compositor;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Services.Mod;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Settings;
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
    private readonly ILogger<AvaloniaOverlayService>? _logger;
    private readonly object _sync = new();
    private readonly CompositorEngine? _compositor;
    private readonly PinkTintLayer _pinkTintLayer;
    private readonly SpiralLayer _spiralLayer;
    private readonly BrainDrainLayer _brainDrainLayer;
    private readonly Dictionary<string, SustainedOverlayState> _sustainedOverlays = new();
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _brainDrainPulseTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };

    private bool _isRunning;
    private bool _isDisposed;
    private int _lastAppliedPinkOpacity = -1;
    private int _lastAppliedSpiralOpacity = -1;
    private int _lastAppliedBrainDrainIntensity = -1;
    private int? _adHocPinkOpacity;
    private int? _adHocSpiralOpacity;
    private int? _adHocBrainDrainIntensity;
    private string _lastSpiralCacheKey = "";
    private double _brainDrainPulsePhase;

    public AvaloniaOverlayService(
        ISettingsService settings,
        IScreenProvider screens,
        IAppEnvironment environment,
        LibVLC libVlc,
        ILogger<AvaloniaOverlayService>? logger = null,
        CompositorEngine? compositor = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _screens = screens ?? throw new ArgumentNullException(nameof(screens));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _libVlc = libVlc ?? throw new ArgumentNullException(nameof(libVlc));
        _logger = logger;
        _compositor = compositor;
        _pinkTintLayer = new PinkTintLayer();
        _spiralLayer = new SpiralLayer();
        _brainDrainLayer = new BrainDrainLayer();
        _updateTimer.Tick += UpdateOverlays;
        _brainDrainPulseTimer.Tick += BrainDrainPulseTick;
        _screens.ScreensChanged += (_, _) => RefreshForMultiMonitorChange();
    }

    public bool IsRunning => _isRunning;
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
        _compositor?.RegisterLayer(_pinkTintLayer);
        _compositor?.RegisterLayer(_spiralLayer);
        _compositor?.RegisterLayer(_brainDrainLayer);
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
        _compositor?.UnregisterLayer(_pinkTintLayer);
        _compositor?.UnregisterLayer(_spiralLayer);
        _compositor?.UnregisterLayer(_brainDrainLayer);
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
            var brainDrainWanted = settings.BrainDrainEnabled || _adHocBrainDrainIntensity.HasValue;
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
                StartPinkFilter(_adHocPinkOpacity ?? settings.PinkFilterOpacity);
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
                _spiralLayer.SetSource(_lastSpiralCacheKey, boostedOpacity / 100.0);
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

            if (settings.BrainDrainEnabled || _adHocBrainDrainIntensity.HasValue)
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
            if (!_isRunning && normalizedKind != "braindrain") return; // ad-hoc brain-drain can work while service stopped

            ShowSustainedOverlayInternal(normalizedKind, clampedOpacity);

            // Stop any existing timer for this kind first.
            if (_sustainedOverlays.TryGetValue(normalizedKind, out var existing))
            {
                existing.AutoHideTimer?.Stop();
                existing.AutoHideTimer = null;
            }

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(safeDurationMs) };
            var state = new SustainedOverlayState(normalizedKind) { AutoHideTimer = timer };
            _sustainedOverlays[normalizedKind] = state;

            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Dispatcher.UIThread.Invoke(() => HideOverlaySustained(normalizedKind));
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
            if (!_isRunning && normalizedKind != "braindrain") return;

            // Idempotent: if already active, leave it to SetSustainedOverlayOpacity to change opacity.
            if (_sustainedOverlays.ContainsKey(normalizedKind)) return;

            ShowSustainedOverlayInternal(normalizedKind, clampedOpacity);
            _sustainedOverlays[normalizedKind] = new SustainedOverlayState(normalizedKind);
        });
    }

    public void HideOverlaySustained(string kind)
    {
        if (_isDisposed) return;

        var normalizedKind = NormalizeOverlayKind(kind);
        if (normalizedKind == null) return;

        Dispatcher.UIThread.Invoke(() =>
        {
            if (_sustainedOverlays.TryGetValue(normalizedKind, out var state))
            {
                state.AutoHideTimer?.Stop();
                _sustainedOverlays.Remove(normalizedKind);
            }

            switch (normalizedKind)
            {
                case "pink":
                    _adHocPinkOpacity = null;
                    StopPinkFilter();
                    break;
                case "spiral":
                    _adHocSpiralOpacity = null;
                    StopSpiral();
                    break;
                case "braindrain":
                    _adHocBrainDrainIntensity = null;
                    StopBrainDrain();
                    break;
            }
        });
    }

    public void SetSustainedOverlayOpacity(string kind, double opacity)
    {
        if (_isDisposed) return;

        var normalizedKind = NormalizeOverlayKind(kind);
        if (normalizedKind == null) return;

        var clampedOpacity = Math.Clamp(opacity, 0.0, 1.0);

        Dispatcher.UIThread.Invoke(() =>
        {
            switch (normalizedKind)
            {
                case "pink":
                    _adHocPinkOpacity = (int)Math.Round(clampedOpacity * 100);
                    if (_isRunning)
                        StartPinkFilter(_adHocPinkOpacity.Value);
                    break;
                case "spiral":
                    _adHocSpiralOpacity = (int)Math.Round(clampedOpacity * 100);
                    if (_isRunning)
                    {
                        var spiralPath = GetSpiralPath();
                        if (!string.IsNullOrEmpty(spiralPath))
                            StartSpiral(spiralPath, _adHocSpiralOpacity.Value);
                    }
                    break;
                case "braindrain":
                    _adHocBrainDrainIntensity = Math.Max(1, (int)Math.Round(clampedOpacity * 100));
                    if (_isRunning)
                        StartBrainDrain(_adHocBrainDrainIntensity.Value);
                    break;
            }
        });
    }

    public void WarmSpiralCache()
    {
        // Spiral layer loads on-demand; no pre-warming needed.
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
        foreach (var state in _sustainedOverlays.Values.ToList())
            state.AutoHideTimer?.Stop();
        _sustainedOverlays.Clear();
        _adHocPinkOpacity = null;
        _adHocSpiralOpacity = null;
        _adHocBrainDrainIntensity = null;
    }

    private void StartPinkFilter(int opacityPercent)
    {
        _pinkTintLayer.SetColor(GetFilterColor(opacityPercent), opacityPercent / 100.0);
        _lastAppliedPinkOpacity = opacityPercent;
    }

    private void StopPinkFilter()
    {
        _pinkTintLayer.SetColor(Colors.Transparent, 0);
        _lastAppliedPinkOpacity = -1;
    }

    private void UpdatePinkFilterOpacity()
    {
        var settings = _settings.Current;
        if (settings == null) return;

        var opacity = _adHocPinkOpacity ?? settings.PinkFilterOpacity;
        if (opacity == _lastAppliedPinkOpacity) return;

        _pinkTintLayer.SetColor(GetFilterColor(opacity), opacity / 100.0);
        _lastAppliedPinkOpacity = opacity;
    }

    private static Color GetFilterColor(int opacityPercent)
    {
        var alpha = (byte)Math.Clamp(opacityPercent / 100.0 * 255, 0, 255);
        var accent = AppColor("PinkColor", new Color(0xFF, 0xFF, 0x69, 0xB4));
        return new Color(alpha, accent.R, accent.G, accent.B);
    }

    private static Color AppColor(string key, Color fallback)
    {
        if (global::Avalonia.Application.Current?.TryGetResource(key, global::Avalonia.Styling.ThemeVariant.Default, out var v) == true && v is Color c)
            return c;
        return fallback;
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

    private void StartSpiral(string path, int opacityPercent)
    {
        _spiralLayer.SetSource(path, opacityPercent / 100.0);
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

        _spiralLayer.SetSource(_lastSpiralCacheKey, opacity / 100.0);
        _lastAppliedSpiralOpacity = opacity;
    }

    private void StartBrainDrain(int intensity)
    {
        _brainDrainLayer.SetIntensity(intensity);
        _lastAppliedBrainDrainIntensity = intensity;
        _brainDrainPulsePhase = 0;
        _brainDrainPulseTimer.Start();
    }

    private void StopBrainDrain()
    {
        _brainDrainPulseTimer.Stop();
        _brainDrainLayer.SetIntensity(0);
        _lastAppliedBrainDrainIntensity = -1;
        _brainDrainPulsePhase = 0;
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

    private void BrainDrainPulseTick(object? sender, EventArgs e)
    {
        // Pulse is handled inside BrainDrainLayer.Update()
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

    public void NotifyTopWindowClosed() { }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
        _updateTimer.Tick -= UpdateOverlays;
        _brainDrainPulseTimer.Tick -= BrainDrainPulseTick;
        _brainDrainPulseTimer.Stop();
    }

    private sealed class SustainedOverlayState
    {
        public string Kind { get; }
        public DispatcherTimer? AutoHideTimer { get; set; }

        public SustainedOverlayState(string kind)
        {
            Kind = kind;
        }
    }
}
