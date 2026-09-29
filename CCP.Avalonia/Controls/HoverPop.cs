using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// Port of ConditioningControlPanel/Behaviors/HoverPopBehavior.cs: on hover the art nudges up
    /// to 1.06 on a back-ease (amplitude 0.5) overshoot and wobbles 2.2 / -1.4 / 0.7 degrees before
    /// settling; on leave it rides home and the wobble is cut. <c>ctrl:HoverPop.IsEnabled="True"</c>.
    ///
    /// Same rules as WPF: RenderTransform only (no layout), the rig is installed on FIRST hover and
    /// wraps an authored Transform instead of replacing it, re-entry continues from wherever the art
    /// sits (the snapshot WPF gets from SnapshotAndReplace), and at MotionLevel.Off nothing animates
    /// (WPF snaps to the end values, so this does too).
    /// ponytail: WPF also refuses an x:Named/animated transform; here only a non-Transform
    /// (TransformOperations from a style) is refused. Add the name check if a storyboard fights it.
    /// </summary>
    public static class HoverPop
    {
        private const double PopScale = 1.06, BackEaseAmplitude = 0.5;
        private const double EnterScaleMs = 220, LeaveScaleMs = 180, LeaveRotateMs = 120;
        private static readonly (double Ms, double Deg)[] Wobble = { (0, 0), (90, 2.2), (200, -1.4), (310, 0.7), (430, 0) };

        public static readonly AttachedProperty<bool> IsEnabledProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(HoverPop));

        public static bool GetIsEnabled(Control c) => c.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(Control c, bool value) => c.SetValue(IsEnabledProperty, value);

        // On the element, not a static dictionary keyed strongly: nothing here keeps a dead visual alive.
        private static readonly ConditionalWeakTable<Control, Rig> Rigs = new();

        private sealed class Rig
        {
            public readonly ScaleTransform Scale = new(1, 1);
            public readonly RotateTransform Rotate = new();
            public readonly Stopwatch Clock = new();
            public DispatcherTimer? Timer;
            public bool Entering;
            public double FromScale = 1, FromAngle;
        }

        static HoverPop() => IsEnabledProperty.Changed.AddClassHandler<Control>((c, e) =>
        {
            c.PointerEntered -= OnEnter;
            c.PointerExited -= OnExit;
            c.DetachedFromVisualTree -= OnDetached;
            if (e.NewValue is not true) { Rest(c); return; }
            c.PointerEntered += OnEnter;
            c.PointerExited += OnExit;
            c.DetachedFromVisualTree += OnDetached;
        });

        private static void OnEnter(object? s, PointerEventArgs e) => Enter(s as Control);
        private static void OnExit(object? s, PointerEventArgs e) => Leave(s as Control);
        // A tab switch mid-hover would otherwise park a scaled, tilted image for the next visit.
        private static void OnDetached(object? s, VisualTreeAttachmentEventArgs e) { if (s is Control c) Rest(c); }

        /// <summary>Pops the art. Public for surfaces that drive it from a card's own hover
        /// (FeatureCard), as WPF's FeatureCard/achievement tiles/Exclusives do.</summary>
        public static void Enter(Control? c)
        {
            if (c == null || EnsureRig(c) is not { } rig) return;
            if (!Allowed()) { Snap(rig, PopScale); return; }
            Start(rig, entering: true);
        }

        /// <summary>Sends the art home, cutting any wobble still in flight.</summary>
        public static void Leave(Control? c)
        {
            if (c == null || !Rigs.TryGetValue(c, out var rig)) return;
            if (!Allowed()) { Snap(rig, 1); return; }
            Start(rig, entering: false);
        }

        private static bool Allowed()
        {
            try { return AmbientFxCanvas.Env.AllowTransitions; }
            catch { return true; }   // settings not up yet: motion on, as WPF
        }

        private static void Rest(Control c) { if (Rigs.TryGetValue(c, out var rig)) Snap(rig, 1); }

        private static void Snap(Rig rig, double scale)
        {
            rig.Timer?.Stop();
            rig.Scale.ScaleX = rig.Scale.ScaleY = scale;
            rig.Rotate.Angle = 0;
        }

        private static void Start(Rig rig, bool entering)
        {
            rig.Entering = entering;
            rig.FromScale = rig.Scale.ScaleX;
            rig.FromAngle = rig.Rotate.Angle;
            rig.Clock.Restart();
            rig.Timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Step(rig));
            rig.Timer.Start();
            Step(rig);
        }

        private static void Step(Rig rig)
        {
            double t = rig.Clock.Elapsed.TotalMilliseconds;
            double scale, angle;
            bool done;
            if (rig.Entering)
            {
                scale = Lerp(rig.FromScale, PopScale, BackOut(Math.Min(t / EnterScaleMs, 1)));
                angle = WobbleAt(t);
                done = t >= Wobble[^1].Ms;
            }
            else
            {
                scale = Lerp(rig.FromScale, 1, QuadOut(Math.Min(t / LeaveScaleMs, 1)));
                angle = Lerp(rig.FromAngle, 0, QuadOut(Math.Min(t / LeaveRotateMs, 1)));
                done = t >= LeaveScaleMs;
            }
            rig.Scale.ScaleX = rig.Scale.ScaleY = scale;
            rig.Rotate.Angle = angle;
            if (done) rig.Timer?.Stop();
        }

        // WPF EasingDoubleKeyFrame with QuadraticEase EaseInOut per segment.
        private static double WobbleAt(double t)
        {
            for (int i = 1; i < Wobble.Length; i++)
                if (t <= Wobble[i].Ms)
                {
                    var (m0, d0) = Wobble[i - 1];
                    return Lerp(d0, Wobble[i].Deg, QuadInOut((t - m0) / (Wobble[i].Ms - m0)));
                }
            return 0;
        }

        private static double Lerp(double a, double b, double p) => a + (b - a) * p;
        private static double QuadOut(double t) => 1 - (1 - t) * (1 - t);
        private static double QuadInOut(double t) => t < 0.5 ? 2 * t * t : 1 - 2 * (1 - t) * (1 - t);

        // WPF BackEase EaseOut: 1 - f(1 - t), f(x) = x^3 - x * A * sin(pi x).
        private static double BackOut(double t)
        {
            double x = 1 - t;
            return 1 - (x * x * x - x * BackEaseAmplitude * Math.Sin(Math.PI * x));
        }

        private static Rig? EnsureRig(Control c)
        {
            if (Rigs.TryGetValue(c, out var cached)) return cached;
            var existing = c.RenderTransform;
            bool authored = existing != null && !existing.Value.IsIdentity;
            if (authored && existing is not Transform) return null;   // a style's TransformOperations: hands off

            var rig = new Rig();
            var group = new TransformGroup();
            if (authored) group.Children.Add((Transform)existing!);   // wrap, never replace
            group.Children.Add(rig.Scale);
            group.Children.Add(rig.Rotate);
            c.RenderTransformOrigin = RelativePoint.Center;
            c.RenderTransform = group;
            Rigs.Add(c, rig);
            return rig;
        }
    }
}
