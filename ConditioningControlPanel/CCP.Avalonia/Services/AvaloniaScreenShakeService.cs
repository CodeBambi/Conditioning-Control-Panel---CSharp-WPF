using System;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Services;

/// <summary>
/// Avalonia port of the WPF <c>ScreenShakeService</c> (Services/UI/ScreenShakeService.cs).
/// Applies a <see cref="TranslateTransform"/> jitter to the MAIN WINDOW's content
/// root via a <see cref="DispatcherTimer"/>, mirroring the WPF amplitude/cadence,
/// and restores the prior transform on finish.
///
/// Scope note (deviation from WPF, per the pre-approved port design): the WPF
/// service snapshots EVERY visible top-level and shakes each window's content. This
/// head deliberately shakes ONLY the main window's content root so overlay and
/// compositor windows (topmost click-through "tinted glass") are never touched.
/// It never moves any window position — same contract as WPF (RenderTransform only).
/// No-ops safely when there is no desktop main window or content root (headless/smoke).
///
/// Avalonia v12 facts (verified against 12.0.5 source):
///   - <c>Visual.RenderTransform</c> is a <c>StyledProperty&lt;ITransform?&gt;</c>
///     (Avalonia.Base/Visual.cs), so the restore slot is typed <see cref="ITransform"/>.
///   - Mutating <c>TranslateTransform.X/Y</c> raises Changed and re-renders the visual
///     (Avalonia.Base/Media/TranslateTransform.cs + Visual.cs IMutableTransform hook).
///   This RenderTransform+TranslateTransform+DispatcherTimer jitter is the same v12
///   pattern already used by MantraWindow.axaml.cs:92-93 and ChaosHudWindow.axaml.cs:705.
/// </summary>
public sealed class AvaloniaScreenShakeService : IScreenShakeService, IDisposable
{
    private readonly ILogger<AvaloniaScreenShakeService>? _logger;
    private readonly Random _rng = new();

    private DispatcherTimer? _timer;
    private Control? _target;
    private TranslateTransform? _transform;
    private ITransform? _prior;
    private DateTime _endsAtUtc;
    private double _amplitude;
    private bool _disposed;

    public AvaloniaScreenShakeService(ILogger<AvaloniaScreenShakeService>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void Shake(double intensity, int durationMs)
    {
        if (_disposed) return;
        // WPF ScreenShakeService.cs:46-47 — clamp to 0..1 and short-circuit a no-op.
        if (ScreenShakeMath.IsNoOp(intensity, durationMs)) return;

        // WPF marshals to the UI thread (ScreenShakeService.cs:52-55); Dispatcher.UIThread
        // is the v12 equivalent of Application.Current.Dispatcher.
        if (Dispatcher.UIThread.CheckAccess()) StartOnUi(intensity, durationMs);
        else Dispatcher.UIThread.Post(() => StartOnUi(intensity, durationMs));
    }

    private void StartOnUi(double intensity, int durationMs)
    {
        try
        {
            // Single-flight: restore any prior in-flight shake before re-snapshotting the
            // target (WPF Reset(), ScreenShakeService.cs restores then clears).
            Reset();

            var root = ResolveContentRoot();
            if (root == null) return; // headless / no main window content → safe no-op.

            _target = root;
            _prior = root.RenderTransform;
            _transform = new TranslateTransform(0, 0);
            root.RenderTransform = _transform;

            _amplitude = ScreenShakeMath.Amplitude(intensity); // WPF ScreenShakeService.cs:84
            _endsAtUtc = DateTime.UtcNow.AddMilliseconds(durationMs);

            if (_timer == null)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(ScreenShakeMath.TickIntervalMs) // WPF :24 (30ms)
                };
                _timer.Tick += OnTick;
            }
            if (!_timer.IsEnabled) _timer.Start();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("AvaloniaScreenShakeService start error: {Error}", ex.Message);
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            if (_transform == null || DateTime.UtcNow >= _endsAtUtc)
            {
                Stop();
                return;
            }
            // WPF ScreenShakeService.cs:202-203 — symmetric jitter in [-amp, +amp]; the same
            // offset on both axes each tick (a single earthquake, not per-window wobble).
            _transform.X = ScreenShakeMath.Offset(_amplitude, _rng.NextDouble());
            _transform.Y = ScreenShakeMath.Offset(_amplitude, _rng.NextDouble());
        }
        catch { /* swallow per-tick errors (WPF OnTick catch) */ }
    }

    private void Stop()
    {
        try { _timer?.Stop(); } catch { }
        try
        {
            // Zero out first so nothing sticks, then restore the pre-shake transform only if
            // ours is still the active one (WPF Stop()+Reset(): don't clobber a swap mid-shake).
            if (_transform != null) { _transform.X = 0; _transform.Y = 0; }
            if (_target != null && ReferenceEquals(_target.RenderTransform, _transform))
                _target.RenderTransform = _prior;
        }
        catch { }
        _target = null;
        _transform = null;
        _prior = null;
    }

    private void Reset() => Stop();

    /// <summary>Resolve the desktop main window's content root, or null when there is no
    /// classic-desktop lifetime / main window / content (headless, single-view, smoke).</summary>
    private static Control? ResolveContentRoot()
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } window
            && window.Content is Control content)
        {
            return content;
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Stop(); } catch { }
        _timer = null;
    }
}
