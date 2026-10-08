using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Depth;
using ConditioningControlPanel.Motion;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// The press spring of WPF 7.1.5 Theme/Controls.xaml SecondaryButton: while pressed the plate
    /// (template part <c>border</c>) squashes to 0.97 over 80 ms (linear, a bare DoubleAnimation),
    /// on release it springs back to 1.0 over 150 ms on a BackEase EaseOut, Amplitude 0.3 (the small
    /// overshoot past 1.0 is the spring). The WPF MultiTrigger only fires while HudPlank.Kind is None:
    /// on a plank the press is ONE motion, the travel, so this stands down there.
    ///
    /// Opted in from a ControlTheme setter (<c>ctrl:PressSquish.Scale</c> = 0.97). Like the WPF
    /// storyboard it is not gated on the motion level. A 16 ms clock samples the curve (the HoverPop
    /// pattern) because an Avalonia Transition has one duration and one easing for both directions,
    /// and this press is fast-linear in, slow-spring out. Re-entry starts from the live scale.
    /// </summary>
    public static class PressSquish
    {
        /// <summary>The template's own numbers (Controls.xaml SecondaryButton storyboards).</summary>
        public const int DownMs = 80, ReleaseMs = 150;
        public const double ReleaseAmplitude = 0.3;

        /// <summary>The pressed scale; 0 or 1 = off.</summary>
        public static readonly AttachedProperty<double> ScaleProperty =
            AvaloniaProperty.RegisterAttached<Button, double>("Scale", typeof(PressSquish));

        public static double GetScale(Button b) => b.GetValue(ScaleProperty);
        public static void SetScale(Button b, double v) => b.SetValue(ScaleProperty, v);

        internal static TimeProvider Time = TimeProvider.System;

        private sealed class Rig
        {
            public Control? Part;
            public ScaleTransform? Scale;
            public DispatcherTimer? Timer;
            public long Started;
            public double From, To;
            public bool Down;
        }

        private static readonly ConditionalWeakTable<Button, Rig> Rigs = new();

        static PressSquish() => ScaleProperty.Changed.AddClassHandler<Button>((b, e) =>
        {
            b.TemplateApplied -= OnTemplateApplied;
            b.PropertyChanged -= OnPropertyChanged;
            double v = e.GetNewValue<double>();
            if (v <= 0 || v == 1) return;
            b.TemplateApplied += OnTemplateApplied;
            b.PropertyChanged += OnPropertyChanged;
        });

        private static void OnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
        {
            if (sender is not Button b) return;
            var rig = Rigs.GetValue(b, _ => new Rig());
            rig.Timer?.Stop();
            rig.Part = e.NameScope.Find<Control>("border");
            rig.Scale = null;
        }

        private static void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != Button.IsPressedProperty || sender is not Button b) return;
            if (HudPlank.GetKind(b) != HudPlankKind.None) return;   // the plank's travel is the press
            if (!Rigs.TryGetValue(b, out var rig) || rig.Part is not { } part) return;
            if (rig.Scale == null)
            {
                rig.Scale = new ScaleTransform(1, 1);
                part.RenderTransformOrigin = RelativePoint.Center;
                part.RenderTransform = rig.Scale;
            }
            rig.Down = b.IsPressed;
            rig.From = rig.Scale.ScaleX;
            rig.To = b.IsPressed ? GetScale(b) : 1.0;
            rig.Started = Time.GetTimestamp();
            rig.Timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Step(rig));
            rig.Timer.Start();
            Step(rig);
        }

        internal static void Step(Button b)
        {
            if (Rigs.TryGetValue(b, out var rig) && rig.Timer?.IsEnabled == true) Step(rig);
        }

        /// <summary>The scale at <paramref name="ms"/> into a press (down) or a release (up).</summary>
        public static double At(double from, double to, bool down, double ms)
        {
            if (down) return from + (to - from) * Math.Clamp(ms / DownMs, 0, 1);
            return from + (to - from) * Easings.BackOut(Math.Clamp(ms / ReleaseMs, 0, 1), ReleaseAmplitude);
        }

        private static void Step(Rig rig)
        {
            if (rig.Scale == null) return;
            double t = Time.GetElapsedTime(rig.Started).TotalMilliseconds;
            rig.Scale.ScaleX = rig.Scale.ScaleY = At(rig.From, rig.To, rig.Down, t);
            if (t >= (rig.Down ? DownMs : ReleaseMs)) rig.Timer?.Stop();
        }
    }
}
