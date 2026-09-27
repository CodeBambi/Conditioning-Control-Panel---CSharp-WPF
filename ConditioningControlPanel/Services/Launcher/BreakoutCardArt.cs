using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Launcher;

/// <summary>Shared vector covers for the two Breakout doors. No media load or animation clock.</summary>
public static class BreakoutCardArt
{
    public static ImageSource Demo { get; } = Build(false);
    public static ImageSource Full { get; } = Build(true);

    private static DrawingImage Build(bool portals)
    {
        var group = new DrawingGroup();
        using (var d = group.Open())
        {
            var mint = Ink("#9CF2D0");
            var gold = Ink("#FFD58A");
            var blue = Ink("#49B6FF");
            var orange = Ink("#FF9C4C");
            d.DrawRectangle(new LinearGradientBrush(Color.FromRgb(19, 33, 53), Color.FromRgb(33, 18, 43), 55), null, new Rect(0, 0, 640, 360));
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
                    d.DrawRoundedRectangle(brush, null, new Rect(x, y, 56, 20), 5, 5);
                    d.DrawRoundedRectangle(Ink("#45FFFFFF"), null, new Rect(x + 4, y + 3, 48, 3), 1.5, 1.5);
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
            d.DrawRoundedRectangle(Ink("#274D50"), new Pen(mint, 2), new Rect(254, 283, 132, 14), 7, 7);
            d.DrawRoundedRectangle(mint, null, new Rect(268, 282, 104, 5), 2.5, 2.5);
        }
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private static SolidColorBrush Ink(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    private static void Mouth(DrawingContext d, Point center, Brush colour, double angle)
    {
        d.PushTransform(new RotateTransform(angle, center.X, center.Y));
        d.DrawEllipse(null, new Pen(colour, 16), center, 18, 66);
        d.DrawEllipse(Ink("#0C172B"), new Pen(Brushes.White, 2), center, 15, 63);
        d.DrawEllipse(null, new Pen(colour, 4), center, 12, 58);
        d.Pop();
    }
}
