using System.Windows.Media;
using ConditioningControlPanel.Controls.Depth;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// Home's share of the depth pass (nav polish wave 10). The page's raised things (the account
    /// ledge, the billboard's coins and beads) throw their shadow through two page resources,
    /// <c>HomeDepthDropBand</c> and <c>HomeDepthDropDisc</c>, which start as the neutral theme ink.
    /// <see cref="PaintDepthHome"/> repaints them pulled toward the section hue; the integrator
    /// calls it from MainWindow.PaintSectionWash. Static paint, no clock.
    /// </summary>
    public partial class SettingsTabView
    {
        /// <summary>Repaint Home's drop shadows with <see cref="DepthRules.ShadowColor(Color)"/>.</summary>
        internal void PaintDepthHome(Color hue)
        {
            Resources["HomeDepthDropBand"] = DropBand(hue);
            Resources["HomeDepthDropDisc"] = DropDisc(hue);
        }

        /// <summary>The DepthDropBand recipe in the hue's ink: ShadowAlpha at the contact edge, clear at the foot.</summary>
        internal static LinearGradientBrush DropBand(Color hue)
        {
            var ink = DepthRules.ShadowColor(hue);
            var brush = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(0, 1) };
            brush.GradientStops.Add(new GradientStop(ink, 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, ink.R, ink.G, ink.B), 1));
            brush.Freeze();
            return brush;
        }

        /// <summary>The DepthDropDisc recipe in the hue's ink.</summary>
        internal static RadialGradientBrush DropDisc(Color hue)
        {
            var ink = DepthRules.ShadowColor(hue);
            var brush = new RadialGradientBrush { Center = new System.Windows.Point(0.5, 0.5), GradientOrigin = new System.Windows.Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
            brush.GradientStops.Add(new GradientStop(ink, 0));
            brush.GradientStops.Add(new GradientStop(DepthRules.ShadowColor(hue, DepthRules.ShadowAlpha * 0.58), 0.6));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, ink.R, ink.G, ink.B), 1));
            brush.Freeze();
            return brush;
        }
    }
}
