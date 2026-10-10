using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Depth;
using ConditioningControlPanel.Motion;
using MotionLevel = ConditioningControlPanel.Models.MotionLevel;

// Lives in ConditioningControlPanel.Avalonia.Controls (not .Depth), as WPF 7.1.5 put HudPlank in
// ConditioningControlPanel.Controls: the views already map that namespace (ctrl: / controls:), so
// the START row opts in with ctrl:HudPlank.Kind and no window root needs a new xmlns.
namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// Port of WPF 7.1.5 Controls/Depth/HudPlank.cs: the press for the HUD planks (the START row:
    /// favourite star, START, the options caret, Save, Exit). Opt-in through
    /// <c>ctrl:HudPlank.Kind</c>, so every other PinkButton / SecondaryButton draws exactly as before.
    /// The theme template names two parts:
    ///  * <c>DepthFace</c>: the face that travels (hover lifts 2, press drops 2 in 90 ms, release
    ///    springs back over 140 ms past rest by 1 px). A TranslateTransform owned by this class.
    ///  * <c>DepthDrop</c>: the drop band under it, ScaleY = the shadow length (gone while pressed).
    /// Every number comes from Core <see cref="HudPlankRules"/> / <see cref="DepthRules"/>; the face
    /// track is <see cref="HudPlankRules.FaceTrack"/> sampled on a 16 ms clock (the HoverPop pattern),
    /// re-entry starts from wherever the face sits (WPF SnapshotAndReplace). Motion Off sets the
    /// values with no clock, so planks look the same at rest.
    /// </summary>
    public static class HudPlank
    {
        public static readonly AttachedProperty<HudPlankKind> KindProperty =
            AvaloniaProperty.RegisterAttached<Button, HudPlankKind>("Kind", typeof(HudPlank));

        public static HudPlankKind GetKind(Button b) => b.GetValue(KindProperty);
        public static void SetKind(Button b, HudPlankKind v) => b.SetValue(KindProperty, v);

        /// <summary>Tests swap in a stepped clock and call <see cref="Step(Button)"/>.</summary>
        internal static TimeProvider Time = TimeProvider.System;

        /// <summary>The motion level the plank obeys (MotionFx.Level). Tests may pin it.</summary>
        internal static Func<MotionLevel> Level = () =>
        {
            try { return AmbientFxCanvas.Env.Level; }
            catch { return MotionLevel.Full; }   // settings not up yet: motion on, as WPF
        };

        private sealed class Rig
        {
            public Control? Face;
            public Control? Drop;
            public TranslateTransform? Slide;
            public ScaleTransform? Scale;
            public bool WasPressed;
            public DispatcherTimer? Timer;
            public long Started;
            public int Ms;
            public Keyframe[] Track = Array.Empty<Keyframe>();
            public double FaceFrom, DropFrom, DropTo;
        }

        private static readonly ConditionalWeakTable<Button, Rig> Rigs = new();

        static HudPlank() => KindProperty.Changed.AddClassHandler<Button>((b, e) =>
        {
            var was = e.GetOldValue<HudPlankKind>();
            var now = e.GetNewValue<HudPlankKind>();
            if (was == HudPlankKind.None && now != HudPlankKind.None)
            {
                b.TemplateApplied += OnTemplateApplied;
                b.PropertyChanged += OnButtonPropertyChanged;
            }
            else if (was != HudPlankKind.None && now == HudPlankKind.None)
            {
                b.TemplateApplied -= OnTemplateApplied;
                b.PropertyChanged -= OnButtonPropertyChanged;
                if (Rigs.TryGetValue(b, out var rig)) rig.Timer?.Stop();
            }
            if (now != HudPlankKind.None && !Rigs.TryGetValue(b, out _))
            {
                // Set from code after the template went up: find the parts the template already made.
                Control? faceP = null, dropP = null;
                foreach (var c in b.GetVisualDescendants().OfType<Control>())
                {
                    if (c.Name == "DepthFace") faceP = c;
                    else if (c.Name == "DepthDrop") dropP = c;
                }
                if (faceP != null || dropP != null) Rigs.Add(b, new Rig { Face = faceP, Drop = dropP });
            }
            if (Rigs.TryGetValue(b, out _)) Update(b, releasing: false, animate: false);
        });

        private static void OnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
        {
            if (sender is not Button b) return;
            var rig = Rigs.GetValue(b, _ => new Rig());
            rig.Timer?.Stop();
            rig.Face = e.NameScope.Find<Control>("DepthFace");
            rig.Drop = e.NameScope.Find<Control>("DepthDrop");
            rig.Slide = null;
            rig.Scale = null;
            Update(b, releasing: false, animate: false);
        }

        private static void OnButtonPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (sender is not Button b) return;
            if (e.Property == Button.IsPressedProperty)
            {
                if (!Rigs.TryGetValue(b, out var rig)) return;
                bool was = rig.WasPressed;
                rig.WasPressed = b.IsPressed;
                Update(b, releasing: was && !b.IsPressed, animate: true);
            }
            else if (e.Property == InputElement.IsPointerOverProperty
                     || e.Property == InputElement.IsEffectivelyEnabledProperty)
            {
                Update(b, releasing: false, animate: true);
            }
        }

        /// <summary>Moves the face and the drop to the button's state. Public for a view that
        /// changes a plank's state from code (WPF MainWindow re-ran it after an ignition dip).</summary>
        public static void Update(Button b, bool releasing, bool animate)
        {
            var kind = GetKind(b);
            if (kind == HudPlankKind.None || !Rigs.TryGetValue(b, out var rig)) return;

            bool enabled = b.IsEffectivelyEnabled, pressed = b.IsPressed, hovered = b.IsPointerOver;
            int ms = animate ? HudPlankRules.DurationFor(pressed, releasing, Level()) : 0;

            if (rig.Face is { } face && rig.Slide == null)
            {
                rig.Slide = new TranslateTransform();
                face.RenderTransform = rig.Slide;
            }
            if (rig.Drop is { } drop)
            {
                double baseH = HudPlankRules.DropBaseHeight(kind);
                if (Math.Abs(drop.Height - baseH) > 0.01)
                {
                    // The band hangs entirely below the plate: its top meets the plate's foot.
                    drop.Height = baseH;
                    drop.Margin = new Thickness(drop.Margin.Left, 0, drop.Margin.Right, -baseH);
                }
                if (rig.Scale == null)
                {
                    drop.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);
                    rig.Scale = new ScaleTransform(1, 1);
                    drop.RenderTransform = rig.Scale;
                }
            }

            double faceTo = HudPlankRules.Travel(enabled, pressed, hovered);
            double dropTo = HudPlankRules.DropScale(kind, enabled, pressed, hovered);

            if (ms <= 0)
            {
                rig.Timer?.Stop();
                if (rig.Slide != null) rig.Slide.Y = faceTo;
                if (rig.Scale != null) rig.Scale.ScaleY = dropTo;
                return;
            }

            rig.FaceFrom = rig.Slide?.Y ?? 0;
            rig.DropFrom = rig.Scale?.ScaleY ?? dropTo;
            rig.DropTo = dropTo;
            rig.Ms = ms;
            rig.Track = HudPlankRules.FaceTrack(faceTo, releasing, ms);
            rig.Started = Time.GetTimestamp();
            rig.Timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Step(rig));
            rig.Timer.Start();
            Step(rig);
        }

        /// <summary>One clock tick for <paramref name="b"/> at <see cref="Time"/>'s now; a no-op once
        /// the timer has stopped, like the real tick.</summary>
        internal static void Step(Button b)
        {
            if (Rigs.TryGetValue(b, out var rig) && rig.Timer?.IsEnabled == true) Step(rig);
        }

        private static void Step(Rig rig)
        {
            double t = Time.GetElapsedTime(rig.Started).TotalMilliseconds;
            if (rig.Slide != null) rig.Slide.Y = Keyframes.Sample(rig.Track, rig.FaceFrom, t);
            if (rig.Scale != null)
            {
                double p = rig.Ms <= 0 ? 1 : Math.Clamp(t / rig.Ms, 0, 1);   // one linear tween, as WPF
                rig.Scale.ScaleY = rig.DropFrom + (rig.DropTo - rig.DropFrom) * p;
            }
            if (t >= rig.Ms) rig.Timer?.Stop();
        }
    }
}
