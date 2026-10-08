// PORTED from WPF 7.1.5 Services/Launcher/BreakoutCardArt.cs: the vector covers for the two Breakout doors
// (no media load, no animation clock), same coordinates and inks.
using System;
using Avalonia;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>Shared vector covers for the two Breakout doors, 640 x 360.</summary>
    internal static class BreakoutCardArt
    {
        private static IImage? _demo, _full;

        public static IImage Demo => _demo ??= Build(false);
        public static IImage Full => _full ??= Build(true);

        private static DrawingImage Build(bool portals)
        {
            var group = new DrawingGroup();
            using (var d = group.Open())
            {
                var mint = Ink("#9CF2D0");
                var gold = Ink("#FFD58A");
                var blue = Ink("#49B6FF");
                var orange = Ink("#FF9C4C");
                // WPF LinearGradientBrush(a, b, 55): an angle gradient from the top-left corner.
                double rad = 55 * Math.PI / 180;
                d.DrawRectangle(new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(Math.Cos(rad), Math.Sin(rad), RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.FromRgb(19, 33, 53), 0), new GradientStop(Color.FromRgb(33, 18, 43), 1) },
                }, null, new Rect(0, 0, 640, 360));
                for (int i = 0; i < 26; i++)
                    d.DrawEllipse(Ink(i % 3 == 0 ? "#597C85" : "#344052"), null,
                        new Point(18 + i * 97 % 604, 20 + i * 53 % 310), 1.5, 1.5);
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < (portals ? 5 : 7); col++)
                    {
                        double x = (portals ? 192 : 98) + col * 65;
                        double y = 66 + row * 30;
                        if (portals && (row + col) % 5 == 0) continue;
                        var brush = row == 0 ? gold : row == 1 ? mint : Ink("#D88AAF");
                        d.DrawRectangle(brush, null, new Rect(x, y, 56, 20), 5, 5);
                        d.DrawRectangle(Ink("#45FFFFFF"), null, new Rect(x + 4, y + 3, 48, 3), 1.5, 1.5);
                    }
                var flight = Geometry.Parse(portals ? "M 315,278 Q 228,216 155,202 M 483,194 Q 415,170 384,113" : "M 318,278 L 400,202 L 348,135");
                d.DrawGeometry(null, new Pen(portals ? blue : mint, 3) { DashStyle = new DashStyle(new double[] { 2, 4 }, 0) }, flight);
                if (portals)
                {
                    Mouth(d, new Point(151, 211), blue, -18);
                    Mouth(d, new Point(489, 206), orange, 18);
                    d.DrawEllipse(Brushes.White, new Pen(orange, 4), new Point(450, 179), 8, 8);
                }
                else d.DrawEllipse(Brushes.White, new Pen(mint, 4), new Point(399, 201), 8, 8);
                d.DrawRectangle(Ink("#274D50"), new Pen(mint, 2), new Rect(254, 283, 132, 14), 7, 7);
                d.DrawRectangle(mint, null, new Rect(268, 282, 104, 5), 2.5, 2.5);
            }
            return new DrawingImage(group);
        }

        private static SolidColorBrush Ink(string hex) => new(Color.Parse(hex));

        private static void Mouth(DrawingContext d, Point center, IBrush colour, double angle)
        {
            var about = Matrix.CreateTranslation(-center.X, -center.Y)
                        * Matrix.CreateRotation(angle * Math.PI / 180)
                        * Matrix.CreateTranslation(center.X, center.Y);
            using (d.PushTransform(about))
            {
                d.DrawEllipse(null, new Pen(colour, 16), center, 18, 66);
                d.DrawEllipse(Ink("#0C172B"), new Pen(Brushes.White, 2), center, 15, 63);
                d.DrawEllipse(null, new Pen(colour, 4), center, 12, 58);
            }
        }
    }
}
