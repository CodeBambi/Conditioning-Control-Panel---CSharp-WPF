using System;
using global::Avalonia;
using global::Avalonia.Animation;
using global::Avalonia.Media;
using global::Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>DispatcherTimer-based animation helpers for overlay code-behind.
/// Mirrors the WPF animation contracts (opacity fades, scale pulses, double tweens)
/// without requiring Avalonia's animation system on keep-alive overlay windows.
/// TODO: replace with Avalonia Animation classes once the overlay lifetime model is stable.</summary>
internal sealed class OpacityFade : IDisposable
{
    private readonly global::Avalonia.Controls.Control _target;
    private readonly DispatcherTimer _timer = new();
    private readonly double _from;
    private readonly double _to;
    private readonly double _durationMs;
    private readonly double _startMs;
    private readonly Action? _onComplete;
    private bool _done;

    public OpacityFade(global::Avalonia.Controls.Control target, double from, double to,
                       double durationMs, Action? onComplete = null)
    {
        _target = target;
        _from = from;
        _to = to;
        _durationMs = Math.Max(1, durationMs);
        _startMs = Environment.TickCount64;
        _onComplete = onComplete;
        _target.Opacity = from;
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += Tick;
        _timer.Start();
    }

    private void Tick(object? sender, EventArgs e)
    {
        if (_done) return;
        double elapsed = Environment.TickCount64 - _startMs;
        double t = Math.Min(1, elapsed / _durationMs);
        _target.Opacity = _from + (_to - _from) * t;
        if (t >= 1)
        {
            _done = true;
            _timer.Stop();
            _onComplete?.Invoke();
        }
    }

    public void Cancel()
    {
        _done = true;
        _timer.Stop();
    }

    public void Dispose()
    {
        Cancel();
        _timer.Tick -= Tick;
    }
}

/// <summary>One-shot double animation on an Avalonia animatable property.
/// Used for pop/bounce transforms where a full storyboard is overkill. Supports an optional
/// start delay (WPF <c>BeginTime</c>) and completion callback (WPF <c>Animation.Completed</c>).</summary>
internal static class AvaloniaChaosAnim
{
    public static void AnimateDouble(Animatable target, AvaloniaProperty property,
                                     double from, double to, double durationMs,
                                     EasingMode easing = EasingMode.EaseOut,
                                     Action? onComplete = null, double startDelayMs = 0)
    {
        target.SetValue(property, from);
        double beginMs = Environment.TickCount64 + Math.Max(0, startDelayMs);
        double dur = Math.Max(1, durationMs);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            double now = Environment.TickCount64;
            if (now < beginMs) return; // hold at `from` through the start delay
            double t = Math.Min(1, (now - beginMs) / dur);
            double value = from + (to - from) * Ease(easing, t);
            target.SetValue(property, value);
            if (t >= 1) { timer.Stop(); onComplete?.Invoke(); }
        };
        timer.Start();
    }

    /// <summary>Animate a <see cref="ScaleTransform"/> uniformly on both axes.</summary>
    public static void ScaleTo(ScaleTransform target, double from, double to, double durationMs,
                               EasingMode easing = EasingMode.EaseOut,
                               double startDelayMs = 0, Action? onComplete = null)
    {
        AnimateDouble(target, ScaleTransform.ScaleXProperty, from, to, durationMs, easing, null, startDelayMs);
        AnimateDouble(target, ScaleTransform.ScaleYProperty, from, to, durationMs, easing, onComplete, startDelayMs);
    }

    /// <summary>Scale-pop with a slight overshoot (WPF <c>BackEase</c> EaseOut).</summary>
    public static void ScalePop(ScaleTransform target, double from, double to, double durationMs,
                                double startDelayMs = 0, Action? onComplete = null) =>
        ScaleTo(target, from, to, durationMs, EasingMode.BackOut, startDelayMs, onComplete);

    private static double Ease(EasingMode easing, double t) => easing switch
    {
        EasingMode.EaseIn => t * t,
        EasingMode.EaseInOut => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2,
        EasingMode.Linear => t,
        EasingMode.BackOut => BackOut(t),
        _ => 1 - Math.Pow(1 - t, 3), // EaseOut cubic default
    };

    // Overshooting ease-out ("back") — mirrors WPF BackEase EaseOut for pop-in transforms.
    private static double BackOut(double t)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        double u = t - 1;
        return 1 + c3 * u * u * u + c1 * u * u;
    }

    public enum EasingMode { EaseOut, EaseIn, EaseInOut, Linear, BackOut }
}
