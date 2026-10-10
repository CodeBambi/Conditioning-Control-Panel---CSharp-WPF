using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Controls.Depth;
using ConditioningControlPanel.Services;

// Lives in ConditioningControlPanel.Controls (not .Depth) on purpose: MainWindow.xaml already maps
// that namespace as controls:, so the START row opts in with controls:HudPlank.Kind and the
// window root needs no new xmlns (another lane owns those lines).
namespace ConditioningControlPanel.Controls
{
    /// <summary>How a HUD button stands on the sheet (nav polish wave 10, lane B).</summary>
    public enum HudPlankKind
    {
        /// <summary>The style draws exactly what it drew before the depth pass.</summary>
        None = 0,
        /// <summary>An ordinary raised plank: DepthRaisedBevel + DepthRaisedSheen, RaisedPx drop.</summary>
        Plank = 1,
        /// <summary>START only: DepthPlankBevel + DepthPlankSheen, a StartPx drop.</summary>
        Chunky = 2,
    }

    /// <summary>
    /// The press for the HUD planks (the START row: favourite star, START, the options caret, Save,
    /// Exit). Opt-in through <c>depth:HudPlank.Kind</c>, so every other PinkButton / SecondaryButton
    /// in the app draws exactly as before. The template names two parts:
    ///  * <c>DepthFace</c>: the face that travels (TravelFor: hover lifts, press drops, release
    ///    springs back with the overshoot). A TranslateTransform owned by this class, so a button's
    ///    own RenderTransform (START's ignition dip) is never touched.
    ///  * <c>DepthDrop</c>: the drop band under it, scaled to ShadowFor (gone while pressed).
    /// Motion Off sets the values without a clock, so the planks look the same at rest.
    /// </summary>
    public static class HudPlank
    {
        public static readonly DependencyProperty KindProperty = DependencyProperty.RegisterAttached(
            "Kind", typeof(HudPlankKind), typeof(HudPlank),
            new FrameworkPropertyMetadata(HudPlankKind.None, OnKindChanged));

        public static HudPlankKind GetKind(DependencyObject o) => (HudPlankKind)o.GetValue(KindProperty);
        public static void SetKind(DependencyObject o, HudPlankKind v) => o.SetValue(KindProperty, v);

        private static readonly DependencyProperty WasPressedProperty = DependencyProperty.RegisterAttached(
            "WasPressed", typeof(bool), typeof(HudPlank), new PropertyMetadata(false));

        private static readonly DependencyPropertyDescriptor? IsPressedDescriptor =
            DependencyPropertyDescriptor.FromProperty(ButtonBase.IsPressedProperty, typeof(ButtonBase));

        // ---- pure numbers (pinned by HudDepthTests) ---------------------------------------------

        /// <summary>The drop band's authored height: its longest shadow (hovered).</summary>
        internal static double DropBaseHeight(HudPlankKind kind) =>
            (kind == HudPlankKind.Chunky ? DepthRules.StartPx : DepthRules.RaisedPx) + DepthRules.HoverLiftPx;

        /// <summary>The drop length for a state: ShadowFor, stretched to StartPx for the chunky plank.</summary>
        internal static double DropLength(HudPlankKind kind, bool enabled, bool pressed, bool hovered)
        {
            if (kind == HudPlankKind.None) return 0;
            double len = DepthRules.ShadowFor(enabled, pressed, active: false, hovered);
            if (len <= 0) return 0;
            return kind == HudPlankKind.Chunky ? len - DepthRules.RaisedPx + DepthRules.StartPx : len;
        }

        /// <summary>The face's travel: a HUD plank is never "lit", so only hover / press move it.</summary>
        internal static double Travel(bool enabled, bool pressed, bool hovered) =>
            DepthRules.TravelFor(enabled, pressed, active: false, hovered);

        /// <summary>Which clock a change rides: press is fast, release springs, hover glides.</summary>
        internal static int DurationFor(bool pressed, bool releasing, ConditioningControlPanel.Models.MotionLevel level) =>
            DepthRules.Ms(pressed ? DepthRules.PressMs : releasing ? DepthRules.ReleaseMs : DepthRules.HoverMs, level);

        // ---- wiring -----------------------------------------------------------------------------

        private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ButtonBase b) return;
            var was = (HudPlankKind)e.OldValue;
            var now = (HudPlankKind)e.NewValue;
            if (was == HudPlankKind.None && now != HudPlankKind.None)
            {
                b.Loaded += OnLoaded;
                b.MouseEnter += OnPointer;
                b.MouseLeave += OnPointer;
                b.IsEnabledChanged += OnEnabled;
                IsPressedDescriptor?.AddValueChanged(b, OnPressed);
            }
            else if (was != HudPlankKind.None && now == HudPlankKind.None)
            {
                b.Loaded -= OnLoaded;
                b.MouseEnter -= OnPointer;
                b.MouseLeave -= OnPointer;
                b.IsEnabledChanged -= OnEnabled;
                IsPressedDescriptor?.RemoveValueChanged(b, OnPressed);
            }
            if (b.IsLoaded) Update(b, releasing: false, animate: false);
        }

        private static void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is ButtonBase b) Update(b, releasing: false, animate: false);
        }

        private static void OnPointer(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is ButtonBase b) Update(b, releasing: false, animate: true);
        }

        private static void OnEnabled(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is ButtonBase b) Update(b, releasing: false, animate: true);
        }

        private static void OnPressed(object? sender, EventArgs e)
        {
            if (sender is not ButtonBase b) return;
            bool was = (bool)b.GetValue(WasPressedProperty);
            b.SetValue(WasPressedProperty, b.IsPressed);
            Update(b, releasing: was && !b.IsPressed, animate: true);
        }

        internal static void Update(ButtonBase b, bool releasing, bool animate)
        {
            try
            {
                var kind = GetKind(b);
                if (kind == HudPlankKind.None || b.Template == null) return;
                b.ApplyTemplate();
                var face = b.Template.FindName("DepthFace", b) as UIElement;
                var drop = b.Template.FindName("DepthDrop", b) as FrameworkElement;

                bool enabled = b.IsEnabled, pressed = b.IsPressed, hovered = b.IsMouseOver;
                int ms = animate ? DurationFor(pressed, releasing, MotionFx.Level) : 0;

                if (face != null)
                {
                    if (face.RenderTransform is not TranslateTransform slide || slide.IsFrozen)
                        face.RenderTransform = slide = new TranslateTransform();
                    double to = Travel(enabled, pressed, hovered);
                    if (ms <= 0)
                    {
                        slide.BeginAnimation(TranslateTransform.YProperty, null);
                        slide.Y = to;
                    }
                    else if (releasing)
                    {
                        // Spring: past rest by the overshoot (upward), then settle.
                        var spring = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(ms) };
                        spring.KeyFrames.Add(new EasingDoubleKeyFrame(to - DepthRules.ReleaseOvershootPx,
                            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms * 0.6)),
                            new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                        spring.KeyFrames.Add(new EasingDoubleKeyFrame(to,
                            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)),
                            new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
                        slide.BeginAnimation(TranslateTransform.YProperty, spring, HandoffBehavior.SnapshotAndReplace);
                    }
                    else
                    {
                        slide.BeginAnimation(TranslateTransform.YProperty,
                            new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
                            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } },
                            HandoffBehavior.SnapshotAndReplace);
                    }
                }

                if (drop != null)
                {
                    double baseH = DropBaseHeight(kind);
                    if (Math.Abs(drop.Height - baseH) > 0.01)
                    {
                        // The band hangs entirely below the plate: its top meets the plate's foot.
                        drop.Height = baseH;
                        drop.Margin = new Thickness(drop.Margin.Left, 0, drop.Margin.Right, -baseH);
                    }
                    drop.RenderTransformOrigin = new Point(0.5, 0);
                    if (drop.RenderTransform is not ScaleTransform scale || scale.IsFrozen)
                        drop.RenderTransform = scale = new ScaleTransform();
                    double s = DropLength(kind, enabled, pressed, hovered) / baseH;
                    if (ms <= 0)
                    {
                        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                        scale.ScaleY = s;
                    }
                    else
                    {
                        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                            new DoubleAnimation(s, TimeSpan.FromMilliseconds(ms)), HandoffBehavior.SnapshotAndReplace);
                    }
                }
            }
            catch (Exception ex) { App.Logger?.Debug("HudPlank.Update: {E}", ex.Message); }
        }
    }
}
