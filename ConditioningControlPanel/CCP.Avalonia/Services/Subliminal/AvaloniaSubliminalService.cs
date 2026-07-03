using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Compositor;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Subliminal;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Services.Subliminal;

/// <summary>
/// Avalonia implementation of the subliminal-message effect engine.
/// Shows brief, centered text flashes via the unified compositor layer.
/// </summary>
public sealed class AvaloniaSubliminalService : ISubliminalService, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IScreenProvider _screens;
    private readonly IProgressionService _progression;
    private readonly ISessionService? _session;
    private readonly ILogger<AvaloniaSubliminalService>? _logger;
    private readonly Random _random = new();
    private readonly object _sync = new();
    private readonly CompositorEngine? _compositor;
    private readonly SubliminalLayer? _subliminalLayer;

    private CancellationTokenSource? _cts;
    private DispatcherTimer? _scheduledTimer;
    private bool _disposed;

    public AvaloniaSubliminalService(
        ISettingsService settings,
        IScreenProvider screens,
        IProgressionService progression,
        ISessionService? session = null,
        ILogger<AvaloniaSubliminalService>? logger = null,
        CompositorEngine? compositor = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _screens = screens ?? throw new ArgumentNullException(nameof(screens));
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        _session = session;
        _logger = logger;
        _compositor = compositor;
        _subliminalLayer = compositor != null ? new SubliminalLayer() : null;
        if (_subliminalLayer != null)
            _compositor?.RegisterLayer(_subliminalLayer);
    }

    public bool IsRunning { get; private set; }

    public event EventHandler? SubliminalDisplayed;

    public void Start()
    {
        if (IsRunning) return;
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
        {
            _logger?.LogDebug("AvaloniaSubliminalService: overlays are not supported on mobile; Start is a no-op");
            return;
        }

        IsRunning = true;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        ScheduleNext();
        _logger?.LogInformation("AvaloniaSubliminalService started");
    }

    public void FlashSubliminal()
    {
        var settings = _settings.Current;
        if (settings == null) return;

        var pool = settings.SubliminalPool;
        var activeTexts = pool.Where(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();
        if (activeTexts.Count == 0)
        {
            _logger?.LogDebug("AvaloniaSubliminalService: no active subliminal texts");
            return;
        }

        var text = activeTexts[_random.Next(activeTexts.Count)];
        ShowSubliminalVisuals(text);
        _progression.AddXP(10, XPSource.Subliminal);
    }

    public void FlashSubliminalCustom(string text, int? opacity = null, int? overrideDurationMs = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        text = text.Trim();
        if (text.Length > 200) text = text.Substring(0, 200);
        text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]*>", "");
        ShowSubliminalVisuals(text, opacity, overrideDurationMs);
        _progression.AddXP(10, XPSource.Subliminal);
    }

    public void FlashSubliminalCustom(string text, int? overrideDurationMs = null, bool suppressHaptic = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        FlashSubliminalCustom(text, opacity: null, overrideDurationMs: overrideDurationMs);
    }

    public void SetEnabled(bool on)
    {
        var s = _settings.Current;
        if (s == null) return;

        if (s.SubliminalEnabled != on)
            s.SubliminalEnabled = on;

        if (_session?.State == SessionState.Running)
        {
            if (on && !IsRunning) Start();
            else if (!on && IsRunning) Stop();
        }

        _settings.Save();
        _logger?.LogInformation("AvaloniaSubliminalService: subliminals toggled: {Enabled}", on);
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;

        _scheduledTimer?.Stop();
        _scheduledTimer = null;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _logger?.LogInformation("AvaloniaSubliminalService stopped");
    }

    private void ShowSubliminalVisuals(string text, int? opacity = null, int? overrideDurationMs = null)
    {
        var settings = _settings.Current;
        if (settings == null) return;

        var durationMs = overrideDurationMs.HasValue
            ? Math.Max(100, overrideDurationMs.Value)
            : Math.Max(100, settings.SubliminalDuration * 17);

        // WPF parity (SubliminalService.cs:583): targetOpacity = (override ?? SubliminalOpacity)/100,
        // read live per flash so preset/session/remote SubliminalOpacity changes apply immediately.
        var targetOpacity = Math.Clamp((opacity ?? settings.SubliminalOpacity) / 100.0, 0.0, 1.0);

        var bgColor = ParseColor(settings.SubBackgroundColor, Colors.Black);
        var textColor = ParseColor(settings.SubTextColor, Colors.Magenta);
        var bgTransparent = settings.SubBackgroundTransparent;

        _subliminalLayer?.Flash(text, bgColor, textColor, durationMs, bgTransparent, targetOpacity);
        SubliminalDisplayed?.Invoke(this, EventArgs.Empty);
    }

    private void ScheduleNext()
    {
        if (!IsRunning) return;

        var settings = _settings.Current;
        if (settings == null || !settings.SubliminalEnabled) return;

        _scheduledTimer?.Stop();

        var freq = Math.Max(1, settings.SubliminalFrequency);
        var baseInterval = 60.0 / freq;
        var variance = baseInterval * 0.3;
        var interval = baseInterval + (_random.NextDouble() * variance * 2 - variance);
        interval = Math.Max(1, interval);

        _scheduledTimer = StartOneShotTimer(TimeSpan.FromSeconds(interval), () =>
        {
            if (!IsRunning) return;
            var s = _settings.Current;
            if (s == null || !s.SubliminalEnabled) return;

            try { FlashSubliminal(); }
            catch (Exception ex) { _logger?.LogWarning(ex, "AvaloniaSubliminalService: FlashSubliminal failed"); }
            ScheduleNext();
        });
    }

    private static Color ParseColor(string? hex, Color fallback)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return Color.Parse(hex);
        }
        catch
        {
            return fallback;
        }
    }

    private static DispatcherTimer StartOneShotTimer(TimeSpan dueTime, Action callback)
    {
        var timer = new DispatcherTimer { Interval = dueTime };
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            timer.Stop();
            timer.Tick -= handler;
            callback();
        };
        timer.Tick += handler;
        timer.Start();
        return timer;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
