using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ConditioningControlPanel.Services.Possession;

// =====================================================================================================
//  POSSESSION - TransformLease. Read Services/Possession/POSSESSION.md first.
//
//  A ghost needs to MOVE a real control and then hand it back untouched. This wraps the element's
//  existing RenderTransform in a TransformGroup { prior, scale, skew, rotate, translate } and hands
//  the four transforms to the effect. Release() eases ours back to identity and then restores the
//  PRIOR transform object (not a fresh identity) and the prior RenderTransformOrigin exactly, mirroring
//  ScreenShakeService.TargetEntry / Reset.
//
//  Leases are refcounted per element through a ConditionalWeakTable, so two effects that land on the
//  same control share one wrapper instead of nesting two groups (the second Release is the one that
//  actually restores).
// =====================================================================================================

public sealed class TransformLease
{
    private static readonly ConditionalWeakTable<UIElement, TransformLease> _table = new();

    private readonly UIElement _el;
    private readonly TransformGroup _group;
    private readonly Transform? _priorTransform;
    private readonly bool _priorTransformWasLocal;
    private readonly Point _priorOrigin;
    private readonly bool _priorOriginWasLocal;

    private int _refCount = 1;
    private bool _released;

    /// <summary>Screen-space offset (applied last, so it is never scaled or rotated).</summary>
    public TranslateTransform Translate { get; } = new TranslateTransform(0, 0);
    public RotateTransform Rotate { get; } = new RotateTransform(0);
    public SkewTransform Skew { get; } = new SkewTransform(0, 0);
    public ScaleTransform Scale { get; } = new ScaleTransform(1, 1);

    public UIElement Element => _el;
    public bool IsReleased => _released;

    private TransformLease(UIElement el)
    {
        _el = el;

        _priorTransformWasLocal = el.ReadLocalValue(UIElement.RenderTransformProperty) != DependencyProperty.UnsetValue;
        _priorTransform = el.RenderTransform;
        _priorOriginWasLocal = el.ReadLocalValue(UIElement.RenderTransformOriginProperty) != DependencyProperty.UnsetValue;
        _priorOrigin = el.RenderTransformOrigin;

        // Detach the prior transform before re-parenting it into our group so no collection ever
        // sees a transform that is still installed on the element.
        try { el.RenderTransform = Transform.Identity; } catch { }

        _group = new TransformGroup();
        if (_priorTransform != null && !ReferenceEquals(_priorTransform, Transform.Identity))
        {
            try { _group.Children.Add(_priorTransform); }
            catch { /* a sealed / foreign transform we cannot re-parent: drop it, Release still restores it */ }
        }
        _group.Children.Add(Scale);
        _group.Children.Add(Skew);
        _group.Children.Add(Rotate);
        _group.Children.Add(Translate);

        try { el.RenderTransform = _group; }
        catch (Exception ex) { App.Logger?.Warning("TransformLease install failed: {Error}", ex.Message); }
    }

    /// <summary>Wrap (or join) the element's transform chain. Null when the element is null.</summary>
    public static TransformLease? Take(UIElement? el)
    {
        if (el == null) return null;
        try
        {
            if (_table.TryGetValue(el, out var existing) && !existing._released)
            {
                existing._refCount++;
                return existing;
            }
            var lease = new TransformLease(el);
            _table.Remove(el);
            _table.Add(el, lease);
            return lease;
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("TransformLease.Take failed: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>True when some effect already holds a lease on this element.</summary>
    public static bool IsLeased(UIElement? el)
        => el != null && _table.TryGetValue(el, out var l) && !l._released;

    /// <summary>Centre (or any) transform origin. The prior origin is restored on release.</summary>
    public void SetOrigin(Point origin)
    {
        try { _el.RenderTransformOrigin = origin; } catch { }
    }

    /// <summary>Snap our four transforms back to identity with no animation (kills running animations).</summary>
    public void ZeroNow()
    {
        try
        {
            PossAnim.Settle(Translate, TranslateTransform.XProperty, 0);
            PossAnim.Settle(Translate, TranslateTransform.YProperty, 0);
            PossAnim.Settle(Rotate, RotateTransform.AngleProperty, 0);
            PossAnim.Settle(Skew, SkewTransform.AngleXProperty, 0);
            PossAnim.Settle(Skew, SkewTransform.AngleYProperty, 0);
            PossAnim.Settle(Scale, ScaleTransform.ScaleXProperty, 1);
            PossAnim.Settle(Scale, ScaleTransform.ScaleYProperty, 1);
        }
        catch { }
    }

    /// <summary>Fire-and-forget release (see ReleaseAsync).</summary>
    public void Release(TimeSpan animateBack) => _ = ReleaseAsync(animateBack);

    /// <summary>
    /// Drop one reference. On the last one, ease our transforms back to identity over
    /// <paramref name="animateBack"/> and then restore the element's prior transform + origin EXACTLY.
    /// Safe to call twice, after the element left the tree, or with TimeSpan.Zero (synchronous restore).
    /// </summary>
    public async Task ReleaseAsync(TimeSpan animateBack)
    {
        if (_released) return;
        if (--_refCount > 0) return;
        _released = true;

        try
        {
            double ms = animateBack.TotalMilliseconds;
            if (ms > 1)
            {
                var ease = PossAnim.EaseInOut;
                PossAnim.To(Translate, TranslateTransform.XProperty, 0, ms, ease);
                PossAnim.To(Translate, TranslateTransform.YProperty, 0, ms, ease);
                PossAnim.To(Rotate, RotateTransform.AngleProperty, 0, ms, ease);
                PossAnim.To(Skew, SkewTransform.AngleXProperty, 0, ms, ease);
                PossAnim.To(Skew, SkewTransform.AngleYProperty, 0, ms, ease);
                PossAnim.To(Scale, ScaleTransform.ScaleXProperty, 1, ms, ease);
                PossAnim.To(Scale, ScaleTransform.ScaleYProperty, 1, ms, ease);
                await PossAnim.DelayAsync(ms + 30, CancellationToken.None).ConfigureAwait(true);
            }
            ZeroNow();
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("TransformLease release animation failed: {Error}", ex.Message);
        }

        RestoreNow();
    }

    /// <summary>Crash-safe synchronous restore (UndoAll). No animation, no awaits.</summary>
    public void ReleaseImmediate()
    {
        if (_released) return;
        _refCount = 0;
        _released = true;
        ZeroNow();
        RestoreNow();
    }

    private void RestoreNow()
    {
        try
        {
            // Only give the element back if OUR group is still the installed transform; if something
            // else took over in the meantime, leave it alone (our transforms are identity by now).
            if (ReferenceEquals(_el.RenderTransform, _group))
            {
                if (_priorTransform != null) { try { _group.Children.Remove(_priorTransform); } catch { } }

                if (_priorTransformWasLocal && _priorTransform != null) _el.RenderTransform = _priorTransform;
                else if (_priorTransformWasLocal) _el.RenderTransform = Transform.Identity;
                else _el.ClearValue(UIElement.RenderTransformProperty);
            }

            if (_priorOriginWasLocal) _el.RenderTransformOrigin = _priorOrigin;
            else _el.ClearValue(UIElement.RenderTransformOriginProperty);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("TransformLease restore failed: {Error}", ex.Message);
        }
        try { _table.Remove(_el); } catch { }
    }
}

/// <summary>
/// Tiny animation kit shared by every Possession effect. House rule: nothing linear, nothing that
/// leaves a property stuck under a held animation (Settle hands the value back to the DP).
/// </summary>
public static class PossAnim
{
    public static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    public static readonly IEasingFunction EaseIn = new CubicEase { EasingMode = EasingMode.EaseIn };
    public static readonly IEasingFunction EaseInOut = new CubicEase { EasingMode = EasingMode.EaseInOut };
    public static readonly IEasingFunction Gravity = new QuadraticEase { EasingMode = EasingMode.EaseIn };
    public static readonly IEasingFunction Sine = new SineEase { EasingMode = EasingMode.EaseInOut };

    /// <summary>Animate a double DP to a value with an ease (never a linear jump).</summary>
    public static void To<T>(T target, DependencyProperty prop, double to, double ms,
                             IEasingFunction? ease = null, double? from = null)
        where T : DependencyObject, IAnimatable
    {
        try
        {
            var anim = new DoubleAnimation
            {
                To = to,
                Duration = TimeSpan.FromMilliseconds(Math.Max(1, ms)),
                EasingFunction = ease ?? EaseOut,
                FillBehavior = FillBehavior.HoldEnd
            };
            if (from.HasValue) anim.From = from.Value;
            target.BeginAnimation(prop, anim);
        }
        catch { }
    }

    /// <summary>A forever ping-pong (breathe / wobble / drift). Settle() stops it.</summary>
    public static void Oscillate<T>(T target, DependencyProperty prop, double from, double to,
                                    double halfPeriodMs, IEasingFunction? ease = null)
        where T : DependencyObject, IAnimatable
    {
        try
        {
            var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(Math.Max(1, halfPeriodMs)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = ease ?? Sine
            };
            target.BeginAnimation(prop, anim);
        }
        catch { }
    }

    /// <summary>Up then down in one animation (an ember tint plate blooming over falling debris).</summary>
    public static void Pulse<T>(T target, DependencyProperty prop, double peak, double riseMs, double fallMs,
                                double from = 0, double to = 0)
        where T : DependencyObject, IAnimatable
    {
        try
        {
            var kf = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(Math.Max(2, riseMs + fallMs)),
                FillBehavior = FillBehavior.HoldEnd
            };
            kf.KeyFrames.Add(new EasingDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            kf.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(Math.Max(1, riseMs))), EaseOut));
            kf.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(Math.Max(2, riseMs + fallMs))), EaseIn));
            target.BeginAnimation(prop, kf);
        }
        catch { }
    }

    /// <summary>Stop any animation on the property and write the value back as a plain local value.</summary>
    public static void Settle<T>(T target, DependencyProperty prop, double? finalValue = null)
        where T : DependencyObject, IAnimatable
    {
        try
        {
            double v = finalValue ?? (double)target.GetValue(prop);
            target.BeginAnimation(prop, null);
            target.SetValue(prop, v);
        }
        catch { }
    }

    /// <summary>Cancellable delay that never throws.</summary>
    public static async Task<bool> DelayAsync(double ms, CancellationToken ct)
    {
        if (ms <= 0) return !ct.IsCancellationRequested;
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(ms), ct).ConfigureAwait(true);
            return !ct.IsCancellationRequested;
        }
        catch { return false; }
    }

    /// <summary>Animate then wait for it (plus a frame of slack).</summary>
    public static async Task<bool> ToAsync<T>(T target, DependencyProperty prop, double to, double ms,
                                              CancellationToken ct, IEasingFunction? ease = null, double? from = null)
        where T : DependencyObject, IAnimatable
    {
        To(target, prop, to, ms, ease, from);
        return await DelayAsync(ms + 20, ct).ConfigureAwait(true);
    }
}
