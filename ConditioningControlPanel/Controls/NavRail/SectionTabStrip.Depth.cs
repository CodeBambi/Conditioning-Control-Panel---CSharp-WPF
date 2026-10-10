using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ConditioningControlPanel.Controls.Depth;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Controls.NavRail
{
    /// <summary>
    /// Nav polish wave 10 (depth), the pages lane's paint: reads the shared brushes from
    /// Resources/Theme/Depth.xaml (App resources first, the dictionary itself when there is no
    /// themed Application, as in the render tests) and makes the two variants this lane needs:
    /// an absolute-mapped band (a 9 px well band stays 9 px on any height) and a hue-tinted
    /// shadow (DepthRules.ShadowColor). Nobody invents a shade here: colours come from the theme
    /// or from DepthRules.
    /// </summary>
    internal static class PageDepthPaint
    {
        private static ResourceDictionary? _dict;

        /// <summary>A Depth.xaml brush by key, or null when the dictionary cannot be reached.</summary>
        internal static Brush? Theme(string key)
        {
            try { if (Application.Current?.TryFindResource(key) is Brush b) return b; } catch { }
            try
            {
                _dict ??= new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/ConditioningControlPanel;component/Resources/Theme/Depth.xaml", UriKind.Absolute),
                };
                return _dict[key] as Brush;
            }
            catch { return null; }
        }

        /// <summary>A linear theme band mapped in pixels: <paramref name="px"/> long down (or across).</summary>
        internal static Brush? Band(string key, double px, bool across = false)
        {
            if (Theme(key) is not LinearGradientBrush src) return null;
            var b = src.Clone();
            b.MappingMode = BrushMappingMode.Absolute;
            b.StartPoint = new Point(0, 0);
            b.EndPoint = across ? new Point(px, 0) : new Point(0, px);
            b.Freeze();
            return b;
        }

        /// <summary>A drop / float band in the hue: the contact edge at ShadowColor(hue, alpha), gone at the foot.</summary>
        internal static LinearGradientBrush Shadow(Color hue, double alpha)
        {
            var c = DepthRules.ShadowColor(hue, alpha);
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            b.GradientStops.Add(new GradientStop(c, 0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
            b.Freeze();
            return b;
        }
    }

    public partial class SectionTabStrip
    {
        /// <summary>The pill drop band's inset from the pill's rounded ends.</summary>
        internal const double PillDropInset = 8;

        private Brush _pillDrop = Brushes.Transparent;
        private Border? _activePress;

        /// <summary>
        /// The strip's depth paint for a section hue: the sunken tray (floor, top / left bands, lit
        /// foot) and the hue-tinted drop band under every raised pill. Called from PaintFor, so the
        /// strip follows the live section by itself; nothing outside needs to wire it.
        /// </summary>
        internal void PaintDepthPages(Color hue)
        {
            TrayFloor.Background = PageDepthPaint.Theme("DepthWellFloorBrush");
            TrayWellTop.Background = PageDepthPaint.Band("DepthWellTop", DepthRules.WellPx);
            TrayWellLeft.Background = PageDepthPaint.Band("DepthWellLeft", 6, across: true);
            TrayWellFoot.BorderBrush = PageDepthPaint.Theme("DepthWellFoot");
            _pillDrop = PageDepthPaint.Shadow(hue, DepthRules.ShadowAlpha);

            // The lit tab is pressed IN: the pressed shade and bevel ride the sliding fill (under
            // the wave 9 gloss ring, which stays), and the fill sits ActiveSinkPx down with it.
            _activePress ??= new Border
            {
                CornerRadius = new CornerRadius(19),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
            };
            _activePress.Background = PageDepthPaint.Theme("DepthPressedShade");
            _activePress.BorderBrush = PageDepthPaint.Theme("DepthPressedBevel");
            if (!ReferenceEquals(ActiveFill.Child, _activePress)) ActiveFill.Child = _activePress;
            ActiveFillShift.Y = DepthRules.ActiveSinkPx;

            foreach (var p in _pills) DepthSettle(p, animate: false);
        }

        /// <summary>The drop band in a pill's template (under the face, never hit-testable).</summary>
        private static Rectangle? DropOf(Button pill)
        {
            try
            {
                pill.ApplyTemplate();
                return pill.Template?.FindName("DepthDrop", pill) as Rectangle;
            }
            catch { return null; }
        }

        /// <summary>
        /// Put a pill where the law says for its state: the face sits TravelFor(...) down (hover
        /// is the ring's own lift, so the face ignores it) and the drop band is ShadowFor(...) long,
        /// none while pressed or lit. Press goes down in PressMs; anything else springs back in
        /// ReleaseMs through a ReleaseOvershootPx overshoot. Motion Off jumps, so rest looks the same.
        /// </summary>
        private void DepthSettle(PillParts p, bool animate)
        {
            bool active = IsActive(p.Tab.Key);
            bool pressed = p.Pressed;
            bool hovered = p.Pill.IsMouseOver || p.Hovered;
            double y = DepthRules.TravelFor(true, pressed, active, hovered: false);
            double drop = DepthRules.ShadowFor(true, pressed, active, hovered);

            if (p.Drop != null)
            {
                p.Drop.Fill = _pillDrop;
                p.Drop.Visibility = drop > 0 ? Visibility.Visible : Visibility.Hidden;
                p.Drop.Height = drop + 2;
                p.Drop.Margin = new Thickness(PillDropInset, 0, PillDropInset, -drop);
            }

            var shift = p.FaceShift;
            if (shift == null) return;
            var level = Level;
            int ms = DepthRules.Ms(pressed ? DepthRules.PressMs : DepthRules.ReleaseMs, level);
            if (!animate || ms <= 0 || Math.Abs(shift.Y - y) < 0.01)
            {
                shift.BeginAnimation(TranslateTransform.YProperty, null);
                shift.Y = y;
                return;
            }
            if (pressed)
            {
                shift.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(y, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
                return;
            }
            var spring = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(ms), FillBehavior = FillBehavior.Stop };
            spring.KeyFrames.Add(new EasingDoubleKeyFrame(y - DepthRules.ReleaseOvershootPx,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms * 0.6)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            spring.KeyFrames.Add(new EasingDoubleKeyFrame(y,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
            spring.Completed += (_, _) => { shift.BeginAnimation(TranslateTransform.YProperty, null); shift.Y = y; };
            shift.BeginAnimation(TranslateTransform.YProperty, spring);
        }

        private void DepthPress(PillParts p, bool down)
        {
            if (p.Pressed == down) return;
            p.Pressed = down;
            DepthSettle(p, animate: true);
        }

        private void DepthHover(PillParts p, bool on)
        {
            p.Hovered = on;
            DepthSettle(p, animate: true);
        }

        // ---- test seams ---------------------------------------------------------------------

        /// <summary>Test seam: how far a pill's face sits down now (px, + = down).</summary>
        internal double PillFaceTravel(string key) => Part(key)?.FaceShift?.Y ?? double.NaN;

        /// <summary>Test seam: a pill's drop band length now (0 = no band: pressed or lit).</summary>
        internal double PillDropLength(string key) =>
            Part(key)?.Drop is { } d && d.Visibility == Visibility.Visible ? d.Height - 2 : 0;

        /// <summary>Test seam: a pill's drop band brush.</summary>
        internal Brush? PillDropBrush(string key) => Part(key)?.Drop?.Fill;

        /// <summary>Test seam: press (or release) a pill without a mouse.</summary>
        internal void DepthPressForTests(string key, bool down)
        {
            if (Part(key) is { } p) { p.Pressed = down; DepthSettle(p, animate: false); }
        }

        /// <summary>Test seam: hover (or leave) a pill without a mouse, for the depth state only.</summary>
        internal void DepthHoverForTests(string key, bool on)
        {
            if (Part(key) is { } p) { p.Hovered = on; DepthSettle(p, animate: false); }
        }

        internal Brush TrayFloorBrush => TrayFloor.Background;
        internal Brush TrayWellTopBrush => TrayWellTop.Background;
        internal Brush TrayWellLeftBrush => TrayWellLeft.Background;
        internal Brush TrayWellFootBrush => TrayWellFoot.BorderBrush;
        internal Border? ActivePressLayer => _activePress;
        internal double ActiveFillSink => ActiveFillShift.Y;
        internal FrameworkElement TrayHostForTests => TrayHost;
    }
}
