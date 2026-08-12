using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Behaviors
{
    /// <summary>
    /// The "hover pop" micro-interaction from the cclabs website, as an attached behavior:
    /// art nudges up to 1.06 with a soft back-ease overshoot and wobbles a couple of degrees
    /// before settling. Enable it on any FrameworkElement with
    /// <c>fx:HoverPop.IsEnabled="True"</c>.
    ///
    /// Rules this file keeps, because breaking any of them is how hover polish turns into a bug:
    ///
    /// <list type="bullet">
    /// <item>RenderTransform only. A LayoutTransform here would reflow every neighbouring card
    /// on hover.</item>
    /// <item>It installs its OWN <see cref="TransformGroup"/> holding a scale and a rotate, on the
    /// element's first hover. If the element already carries a RenderTransform, the existing
    /// transform is WRAPPED into the group (index 0) rather than replaced, so authored offsets
    /// survive.</item>
    /// <item>It refuses to fight another storyboard: if the element's existing RenderTransform is
    /// named (registered in a namescope) or already has an animation clock on it, the behavior
    /// no-ops. Apply it to an inner art child at the call site instead.</item>
    /// <item>Every animation is applied straight to the transform objects via
    /// <c>BeginAnimation</c> with <see cref="HandoffBehavior.SnapshotAndReplace"/>, so re-entering
    /// mid-flight continues from wherever the art currently sits instead of snapping.</item>
    /// <item>It honours the app's reduced-motion gate (<see cref="MotionFx.AllowTransitions"/>) —
    /// at MotionLevel.Off nothing moves at all.</item>
    /// </list>
    /// </summary>
    public static class HoverPop
    {
        // ---- the frozen effect spec ----
        private const double PopScale = 1.06;
        private const int EnterScaleMs = 220;
        private const double BackEaseAmplitude = 0.5;

        private const int WobbleMs1 = 90;
        private const int WobbleMs2 = 200;
        private const int WobbleMs3 = 310;
        private const int WobbleMs4 = 430;
        private const double WobbleDeg1 = 2.2;
        private const double WobbleDeg2 = -1.4;
        private const double WobbleDeg3 = 0.7;

        private const int LeaveScaleMs = 180;
        private const int LeaveRotateMs = 120;

        /// <summary>
        /// The wobble keyframe timeline. Built once, frozen, and shared by every hover target in
        /// the app — a frozen timeline is thread-safe to reuse and costs nothing per element.
        /// </summary>
        private static readonly DoubleAnimationUsingKeyFrames WobbleIn = BuildWobble();

        /// <summary>Scale-in with the BackEase overshoot. Frozen and shared.</summary>
        private static readonly DoubleAnimation ScaleIn = Freeze(new DoubleAnimation(
            PopScale, TimeSpan.FromMilliseconds(EnterScaleMs))
        {
            EasingFunction = Freeze(new BackEase
            {
                EasingMode = EasingMode.EaseOut,
                Amplitude = BackEaseAmplitude,
            }),
        });

        /// <summary>Scale back to rest. Frozen and shared.</summary>
        private static readonly DoubleAnimation ScaleOut = Freeze(new DoubleAnimation(
            1.0, TimeSpan.FromMilliseconds(LeaveScaleMs))
        {
            EasingFunction = Freeze(new QuadraticEase { EasingMode = EasingMode.EaseOut }),
        });

        /// <summary>Rotation cut back to 0 — this is what kills a wobble still in flight.</summary>
        private static readonly DoubleAnimation RotateOut = Freeze(new DoubleAnimation(
            0.0, TimeSpan.FromMilliseconds(LeaveRotateMs))
        {
            EasingFunction = Freeze(new QuadraticEase { EasingMode = EasingMode.EaseOut }),
        });

        private static DoubleAnimationUsingKeyFrames BuildWobble()
        {
            var wobble = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(WobbleMs4),
            };
            wobble.KeyFrames.Add(Key(0.0, 0));
            wobble.KeyFrames.Add(Key(WobbleDeg1, WobbleMs1));
            wobble.KeyFrames.Add(Key(WobbleDeg2, WobbleMs2));
            wobble.KeyFrames.Add(Key(WobbleDeg3, WobbleMs3));
            wobble.KeyFrames.Add(Key(0.0, WobbleMs4));
            return Freeze(wobble);
        }

        private static EasingDoubleKeyFrame Key(double value, int ms) => new(
            value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)))
        {
            EasingFunction = Freeze(new QuadraticEase { EasingMode = EasingMode.EaseInOut }),
        };

        private static T Freeze<T>(T freezable) where T : Freezable
        {
            try { if (freezable.CanFreeze) freezable.Freeze(); } catch { }
            return freezable;
        }

        // ---- attached property ----

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled", typeof(bool), typeof(HoverPop),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

        public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

        /// <summary>
        /// The transform pair this behavior owns for a given element, cached so MouseEnter never
        /// has to walk the transform group again. Stored on the element itself — no static
        /// dictionary, so nothing here can keep a dead visual alive.
        /// </summary>
        private static readonly DependencyProperty RigProperty =
            DependencyProperty.RegisterAttached(
                "Rig", typeof(Rig), typeof(HoverPop), new PropertyMetadata(null));

        private sealed class Rig
        {
            public ScaleTransform Scale { get; init; } = null!;
            public RotateTransform Rotate { get; init; } = null!;
        }

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement element) return;
            try
            {
                bool on = e.NewValue is true;
                bool was = e.OldValue is true;
                if (on == was) return;

                // Handlers are idempotent to add/remove, and detaching leaves the art at rest.
                element.MouseEnter -= OnMouseEnter;
                element.MouseLeave -= OnMouseLeave;
                element.Unloaded -= OnUnloaded;

                if (!on)
                {
                    Rest(element);
                    element.SetValue(RigProperty, null);
                    return;
                }

                element.MouseEnter += OnMouseEnter;
                element.MouseLeave += OnMouseLeave;
                element.Unloaded += OnUnloaded;

                // The transform rig is installed on FIRST HOVER, not here. Attaching costs three
                // handler subscriptions and nothing else, which matters: the assets grid realises
                // one tile per file in the folder (uncapped - thousands is realistic), and only
                // the handful the pointer actually visits ever pay for a TransformGroup. Waiting
                // for the hover also means we read a settled RenderTransform rather than a
                // half-applied template one.
            }
            catch { }
        }

        private static void OnUnloaded(object sender, RoutedEventArgs e)
        {
            // Stop the clocks when the art leaves the tree (tab switch mid-hover would otherwise
            // leave a scaled, tilted image parked for the next time the tab is shown).
            if (sender is FrameworkElement element) Rest(element);
        }

        private static void OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
            => Enter(sender as FrameworkElement);

        private static void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
            => Leave(sender as FrameworkElement);

        /// <summary>
        /// Pops the art. Public because several surfaces already own their hover plumbing in
        /// code (FeatureCard's <c>ApplyHover</c>, the achievement tiles' holo-foil tilt) and the
        /// art element there sits under scrims and overlays that make its own MouseEnter an
        /// unreliable trigger — those call sites drive the pop from the card's hover instead.
        /// </summary>
        public static void Enter(FrameworkElement? element)
        {
            if (element == null) return;
            try
            {
                var rig = EnsureRig(element);
                if (rig == null) return;

                if (!Allowed())
                {
                    rig.Scale.ScaleX = rig.Scale.ScaleY = PopScale;
                    rig.Rotate.Angle = 0;
                    return;
                }

                rig.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, ScaleIn, HandoffBehavior.SnapshotAndReplace);
                rig.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, ScaleIn, HandoffBehavior.SnapshotAndReplace);
                rig.Rotate.BeginAnimation(RotateTransform.AngleProperty, WobbleIn, HandoffBehavior.SnapshotAndReplace);
            }
            catch { }
        }

        /// <summary>Sends the art home, cutting any wobble still in flight. See <see cref="Enter"/>.</summary>
        public static void Leave(FrameworkElement? element)
        {
            if (element == null) return;
            try
            {
                var rig = element.GetValue(RigProperty) as Rig;
                if (rig == null) return;

                if (!Allowed())
                {
                    rig.Scale.ScaleX = rig.Scale.ScaleY = 1.0;
                    rig.Rotate.Angle = 0;
                    return;
                }

                rig.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, ScaleOut, HandoffBehavior.SnapshotAndReplace);
                rig.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, ScaleOut, HandoffBehavior.SnapshotAndReplace);
                // SnapshotAndReplace here is what cuts the wobble: it takes the current angle as
                // the start and runs a short ride home, instead of letting the keyframes finish.
                rig.Rotate.BeginAnimation(RotateTransform.AngleProperty, RotateOut, HandoffBehavior.SnapshotAndReplace);
            }
            catch { }
        }

        private static bool Allowed()
        {
            // App.Settings can be null very early in startup and in unit-test hosts; motion on is
            // the safe default there, and the whole call is wrapped anyway.
            try { return MotionFx.AllowTransitions; }
            catch { return true; }
        }

        private static void Rest(FrameworkElement element)
        {
            try
            {
                if (element.GetValue(RigProperty) is not Rig rig) return;
                rig.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                rig.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                rig.Rotate.BeginAnimation(RotateTransform.AngleProperty, null);
                rig.Scale.ScaleX = rig.Scale.ScaleY = 1.0;
                rig.Rotate.Angle = 0;
            }
            catch { }
        }

        // ---- transform plumbing ----

        /// <summary>
        /// Returns the element's scale+rotate pair, installing them on first use. Returns null —
        /// leaving the element completely untouched — when the existing RenderTransform belongs to
        /// somebody else (named or already animated); those call sites should target an inner
        /// child instead.
        /// </summary>
        private static Rig? EnsureRig(FrameworkElement element)
        {
            try
            {
                if (element.GetValue(RigProperty) is Rig cached) return cached;

                var existing = element.RenderTransform;

                // Somebody else's transform, already under a storyboard or reachable by name from
                // one — do not touch it.
                if (existing != null && !IsIdentity(existing) && IsForeign(element, existing)) return null;

                var scale = new ScaleTransform(1.0, 1.0);
                var rotate = new RotateTransform(0.0);
                var group = new TransformGroup();

                // Wrap, never replace: an authored offset/flip keeps its place at the bottom of
                // the stack and our pop composes on top of it.
                if (existing != null && !IsIdentity(existing)) group.Children.Add(existing);
                group.Children.Add(scale);
                group.Children.Add(rotate);

                element.RenderTransformOrigin = new Point(0.5, 0.5);
                element.RenderTransform = group;

                var rig = new Rig { Scale = scale, Rotate = rotate };
                element.SetValue(RigProperty, rig);
                return rig;
            }
            catch { return null; }
        }

        private static bool IsIdentity(Transform transform)
        {
            try
            {
                if (ReferenceEquals(transform, Transform.Identity)) return true;
                return transform.Value.IsIdentity;
            }
            catch { return false; }
        }

        /// <summary>
        /// True when the transform looks like it belongs to somebody else: it already has an
        /// animation clock on it, or it is registered in a namescope under a name (which is how
        /// <c>Storyboard.TargetName</c> reaches a transform, so a name means a storyboard is
        /// likely to start driving it later even if nothing is running right now).
        /// </summary>
        private static bool IsForeign(FrameworkElement element, Transform transform)
        {
            try
            {
                // A frozen transform can be neither named nor animated — safe to sit under.
                if (transform.IsFrozen) return false;
                if (transform.HasAnimatedProperties) return true;
                if (IsNamed(element, transform)) return true;

                if (transform is TransformGroup group)
                {
                    foreach (var child in group.Children)
                        if (child != null && IsForeign(element, child)) return true;
                }
                return false;
            }
            catch { return true; }  // unsure = hands off
        }

        /// <summary>
        /// Looks for the transform instance in the namescopes above the element. WPF offers no
        /// reverse name lookup, but a <see cref="NameScope"/> is enumerable, so a short walk up
        /// the tree finds an <c>x:Name</c>d transform. Capped at a few levels — the interesting
        /// scope is always the template or the UserControl right above the art.
        /// </summary>
        private static bool IsNamed(FrameworkElement element, Transform transform)
        {
            try
            {
                DependencyObject? node = element;
                for (int depth = 0; node != null && depth < 12; depth++)
                {
                    if (NameScope.GetNameScope(node) is System.Collections.IEnumerable scope)
                    {
                        foreach (var entry in scope)
                        {
                            if (entry is System.Collections.Generic.KeyValuePair<string, object> pair
                                && ReferenceEquals(pair.Value, transform))
                                return true;
                        }
                    }
                    node = node is Visual or Visual3D
                        ? VisualTreeHelper.GetParent(node)
                        : LogicalTreeHelper.GetParent(node);
                }
                return false;
            }
            catch { return false; }
        }
    }
}
