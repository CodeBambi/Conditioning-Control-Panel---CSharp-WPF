using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Helpers;

namespace ConditioningControlPanel.Services.Possession;

// =====================================================================================================
//  EmberAttribution - "clarity in front" made visible. Read POSSESSION.md, section THE RULE.
//
//  Every haunt has to be answerable in one second from across the room: "was that Lockdown?" This class
//  is the entire answer. Ember #FF8A5C is reserved for it: crimson is the ROOM (Lockdown's theme hue),
//  ember is the room DOING something. Nothing else in the app may paint with it.
//
//    charge   - the ember ripple over a control BEFORE anything moves (no effect may skip it)
//    outline  - a thin ember frame that FOLLOWS the possessed control while the ghost is live
//    ring     - an ember ring around the cursor while anything at all is possessed (refcounted)
//    pulse    - a window-edge flare for rung changes and tripwires
//
//  Everything lives in the host's GhostLayer (a hit-test-transparent Canvas over the whole window) and
//  everything is UI-thread only. Photosafe (and Windows' own "no client-area animation") swaps the
//  ripple for a still tint and softens the pulse - the grammar survives, the strobe does not.
// =====================================================================================================
public sealed class EmberAttribution : IPossessionAttribution
{
    /// <summary>Ember. Possession only. Never reuse for theme (that is crimson #DC143C).</summary>
    public static readonly Color Ember = Color.FromRgb(0xFF, 0x8A, 0x5C);

    private static readonly SolidColorBrush EmberSolid = Frozen(new SolidColorBrush(Ember));
    private static readonly SolidColorBrush EmberOutline = Frozen(new SolidColorBrush(Color.FromArgb(191, Ember.R, Ember.G, Ember.B))); // 75%
    private static readonly SolidColorBrush EmberTint12 = Frozen(new SolidColorBrush(Color.FromArgb(31, Ember.R, Ember.G, Ember.B)));   // 12%
    private static readonly SolidColorBrush EmberTint08 = Frozen(new SolidColorBrush(Color.FromArgb(20, Ember.R, Ember.G, Ember.B)));   // 8%

    private const int OutlineFollowMs = 100;   // effects MOVE the control under the outline - it has to chase
    private const int MaxLivePulses = 3;

    private readonly IPossessionHost _host;
    private readonly Func<bool> _photosafe;

    private readonly List<OutlineHandle> _outlines = new();
    private readonly List<Border> _pulses = new();
    private readonly List<FrameworkElement> _charges = new();
    private DispatcherTimer? _followTimer;
    private Ellipse? _ring;
    private bool _ringHooked;
    private int _possessCount;

    /// <param name="photosafe">Read LIVE (App.Settings.Current.LockdownPhotosafe) so flipping the toggle
    /// mid-lockdown takes effect on the very next charge instead of at the next lockdown.</param>
    public EmberAttribution(IPossessionHost host, Func<bool>? photosafe = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _photosafe = photosafe ?? (() => false);
    }

    public bool AnyPossessed => _possessCount > 0;

    private bool StillMotion
    {
        // Either the user asked for no flashing, or Windows itself says "no client-area animation".
        get
        {
            try { return _photosafe() || !SystemParameters.ClientAreaAnimation; }
            catch { return true; }
        }
    }

    private static SolidColorBrush Frozen(SolidColorBrush b) { b.Freeze(); return b; }

    // ---------------------------------------------------------------------------------------------
    //  The charge
    // ---------------------------------------------------------------------------------------------

    public Task ChargeAsync(FrameworkElement target, CancellationToken ct, int durationMs = 400)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (target == null) { tcs.TrySetResult(false); return tcs.Task; }

        DispatcherHelper.RunOnUI(() =>
        {
            try { StartCharge(target, ct, durationMs, tcs); }
            catch (Exception ex)
            {
                App.Logger?.Warning("Possession charge failed: {Error}", ex.Message);
                tcs.TrySetResult(false);
            }
        });
        return tcs.Task;
    }

    private void StartCharge(FrameworkElement target, CancellationToken ct, int durationMs, TaskCompletionSource<bool> tcs)
    {
        var layer = _host.GhostLayer;
        if (layer == null || !target.IsVisible) { tcs.TrySetResult(false); return; }

        var bounds = BoundsOf(target);
        if (bounds.IsEmpty || bounds.Width <= 1 || bounds.Height <= 1) { tcs.TrySetResult(false); return; }

        var still = StillMotion;
        var border = new Border
        {
            Width = bounds.Width,
            Height = bounds.Height,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(2),
            BorderBrush = EmberSolid,
            Background = EmberTint12,
            IsHitTestVisible = false,
            Opacity = still ? 1.0 : 0.0,
        };
        if (!still)
        {
            // The glow is what carries the ripple at a distance; skip it in still mode so the tint reads
            // as a flat highlight rather than a slow bloom.
            border.Effect = new DropShadowEffect { Color = Ember, BlurRadius = 18, ShadowDepth = 0, Opacity = 1 };
        }
        Canvas.SetLeft(border, bounds.X);
        Canvas.SetTop(border, bounds.Y);
        layer.Children.Add(border);
        _charges.Add(border);

        var done = 0;
        CancellationTokenRegistration reg = default;
        DispatcherTimer? holdTimer = null;

        void Finish()
        {
            if (Interlocked.Exchange(ref done, 1) != 0) return;
            try { holdTimer?.Stop(); } catch { }
            try { reg.Dispose(); } catch { }
            RemoveChild(border);
            _charges.Remove(border);
            tcs.TrySetResult(true);
        }

        if (still)
        {
            // Photosafe / reduced motion: a still tint that simply sits there long enough to be seen.
            var holdMs = Math.Max(durationMs, 600);
            holdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(holdMs) };
            holdTimer.Tick += (_, __) => Finish();
            holdTimer.Start();
        }
        else
        {
            var anim = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(Math.Max(80, durationMs)),
                FillBehavior = FillBehavior.Stop,
            };
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(0)));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0.95, KeyTime.FromPercent(0.35), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            anim.Completed += (_, __) => Finish();
            border.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        if (ct.CanBeCanceled)
            reg = ct.Register(() => DispatcherHelper.RunOnUI(Finish));
    }

    // ---------------------------------------------------------------------------------------------
    //  The possessed outline (+ the cursor ring it refcounts)
    // ---------------------------------------------------------------------------------------------

    public IDisposable Possess(FrameworkElement target)
    {
        var handle = new OutlineHandle(this, target);
        DispatcherHelper.RunOnUI(handle.Start);
        return handle;
    }

    private void AddOutline(OutlineHandle handle)
    {
        var layer = _host.GhostLayer;
        if (layer == null) return;

        var border = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = EmberOutline,
            Background = EmberTint08,
            CornerRadius = new CornerRadius(6),
            IsHitTestVisible = false,
            Width = 0,
            Height = 0,
        };
        handle.Border = border;
        layer.Children.Add(border);
        _outlines.Add(handle);
        PositionOutline(handle);

        _possessCount++;
        UpdateRing();

        if (_followTimer == null)
        {
            _followTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(OutlineFollowMs) };
            _followTimer.Tick += OnFollowTick;
        }
        if (!_followTimer.IsEnabled) _followTimer.Start();
    }

    private void RemoveOutline(OutlineHandle handle)
    {
        if (_outlines.Remove(handle))
        {
            _possessCount = Math.Max(0, _possessCount - 1);
            UpdateRing();
        }
        RemoveChild(handle.Border);
        handle.Border = null;
        if (_outlines.Count == 0) { try { _followTimer?.Stop(); } catch { } }
    }

    private void OnFollowTick(object? sender, EventArgs e)
    {
        // One timer for every live outline: the effects underneath are translating / scaling their
        // victims, so a static overlay would drift off the control within a second.
        for (int i = _outlines.Count - 1; i >= 0; i--)
        {
            try { PositionOutline(_outlines[i]); }
            catch { /* a control that vanished mid-haunt just loses its outline */ }
        }
    }

    private void PositionOutline(OutlineHandle handle)
    {
        var border = handle.Border;
        var el = handle.Element;
        if (border == null || el == null) return;

        var b = BoundsOf(el);
        if (b.IsEmpty || b.Width <= 0 || b.Height <= 0 || !el.IsVisible)
        {
            border.Visibility = Visibility.Collapsed;
            return;
        }
        border.Visibility = Visibility.Visible;
        border.Width = b.Width;
        border.Height = b.Height;
        Canvas.SetLeft(border, b.X);
        Canvas.SetTop(border, b.Y);
    }

    private sealed class OutlineHandle : IDisposable
    {
        private readonly EmberAttribution _owner;
        private bool _disposed;
        private bool _started;
        public FrameworkElement? Element;
        public Border? Border;

        public OutlineHandle(EmberAttribution owner, FrameworkElement element)
        {
            _owner = owner;
            Element = element;
        }

        public void Start()
        {
            if (_disposed || _started || Element == null) return;
            _started = true;
            try { _owner.AddOutline(this); }
            catch (Exception ex) { App.Logger?.Warning("Possession outline failed: {Error}", ex.Message); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DispatcherHelper.RunOnUI(() =>
            {
                try { if (_started) _owner.RemoveOutline(this); }
                catch (Exception ex) { App.Logger?.Warning("Possession outline release failed: {Error}", ex.Message); }
                Element = null;
            });
        }
    }

    // ---------------------------------------------------------------------------------------------
    //  The cursor ring
    // ---------------------------------------------------------------------------------------------

    private void UpdateRing()
    {
        var layer = _host.GhostLayer;
        if (layer == null) return;

        if (_ring == null)
        {
            _ring = new Ellipse
            {
                Width = 22,
                Height = 22,
                Stroke = EmberSolid,
                StrokeThickness = 1.5,
                Fill = null,
                IsHitTestVisible = false,
                Opacity = 0,
            };
            layer.Children.Add(_ring);
        }
        else if (!layer.Children.Contains(_ring))
        {
            layer.Children.Add(_ring);
        }

        HookRing();
        FadeRing(_possessCount > 0 ? 1.0 : 0.0);
    }

    private void HookRing()
    {
        if (_ringHooked) return;
        var win = _host.Window;
        if (win == null) return;
        win.PreviewMouseMove += OnRingMouseMove;
        win.MouseLeave += OnRingMouseLeave;
        _ringHooked = true;
    }

    private void OnRingMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_ring == null || _possessCount <= 0) return;
        try
        {
            var p = e.GetPosition(_host.GhostLayer);
            Canvas.SetLeft(_ring, p.X - _ring.Width / 2);
            Canvas.SetTop(_ring, p.Y - _ring.Height / 2);
            if (_ring.Opacity <= 0) FadeRing(1.0);
        }
        catch { }
    }

    private void OnRingMouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => FadeRing(0);

    private void FadeRing(double to)
    {
        if (_ring == null) return;
        try
        {
            var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(250)) { FillBehavior = FillBehavior.HoldEnd };
            _ring.BeginAnimation(UIElement.OpacityProperty, anim);
        }
        catch { }
    }

    // ---------------------------------------------------------------------------------------------
    //  The edge pulse
    // ---------------------------------------------------------------------------------------------

    public void EdgePulse(double strength)
    {
        if (strength <= 0) return;
        DispatcherHelper.RunOnUI(() =>
        {
            try { StartPulse(strength); }
            catch (Exception ex) { App.Logger?.Warning("Possession edge pulse failed: {Error}", ex.Message); }
        });
    }

    private void StartPulse(double strength)
    {
        var layer = _host.GhostLayer;
        if (layer == null) return;
        if (_pulses.Count >= MaxLivePulses) return;   // overlapping scares stack, but never into a strobe

        var w = layer.ActualWidth;
        var h = layer.ActualHeight;
        if (w <= 0 || h <= 0) return;

        var still = StillMotion;
        strength = Math.Clamp(strength, 0, still ? 0.5 : 1.0);
        var ms = still ? 900 : 500;

        var border = new Border
        {
            Width = w,
            Height = h,
            BorderThickness = new Thickness(10),
            BorderBrush = EmberSolid,
            IsHitTestVisible = false,
            Opacity = 0,
            Effect = new BlurEffect { Radius = 24 },
        };
        Canvas.SetLeft(border, 0);
        Canvas.SetTop(border, 0);
        layer.Children.Add(border);
        _pulses.Add(border);

        var anim = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(ms),
            FillBehavior = FillBehavior.Stop,
        };
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(0)));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(strength, KeyTime.FromPercent(0.4), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        anim.Completed += (_, __) =>
        {
            RemoveChild(border);
            _pulses.Remove(border);
        };
        border.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    // ---------------------------------------------------------------------------------------------
    //  Teardown
    // ---------------------------------------------------------------------------------------------

    /// <summary>Rip every ember overlay off the window. The reassembly exit and the crash-safe
    /// UndoAll both end here, so it must never throw and must leave nothing behind.</summary>
    public void ReleaseAll()
    {
        DispatcherHelper.RunOnUI(() =>
        {
            try
            {
                foreach (var h in _outlines.ToArray()) RemoveChild(h.Border);
                _outlines.Clear();
                foreach (var p in _pulses.ToArray()) RemoveChild(p);
                _pulses.Clear();
                foreach (var c in _charges.ToArray()) RemoveChild(c);
                _charges.Clear();

                _possessCount = 0;
                try { _followTimer?.Stop(); } catch { }

                if (_ring != null)
                {
                    _ring.BeginAnimation(UIElement.OpacityProperty, null);
                    _ring.Opacity = 0;
                    RemoveChild(_ring);
                }
                if (_ringHooked)
                {
                    var win = _host.Window;
                    if (win != null)
                    {
                        win.PreviewMouseMove -= OnRingMouseMove;
                        win.MouseLeave -= OnRingMouseLeave;
                    }
                    _ringHooked = false;
                }
            }
            catch (Exception ex) { App.Logger?.Warning("Possession ReleaseAll failed: {Error}", ex.Message); }
        });
    }

    // ---------------------------------------------------------------------------------------------
    //  Helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>The element's bounds in GhostLayer coordinates, WITH its render transform applied - an
    /// effect that already slid the control has to be outlined where it actually sits now.</summary>
    private Rect BoundsOf(FrameworkElement el)
    {
        try
        {
            var layer = _host.GhostLayer;
            if (layer == null || el == null || !el.IsVisible) return Rect.Empty;
            if (el.ActualWidth <= 0 || el.ActualHeight <= 0) return Rect.Empty;
            var t = el.TransformToVisual(layer);
            return t.TransformBounds(new Rect(new Point(0, 0), el.RenderSize));
        }
        catch { return Rect.Empty; }
    }

    private void RemoveChild(UIElement? child)
    {
        if (child == null) return;
        try { _host.GhostLayer?.Children.Remove(child); }
        catch { }
    }
}
