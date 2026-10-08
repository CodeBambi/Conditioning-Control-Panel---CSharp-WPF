using System;

namespace ConditioningControlPanel.Services.Billboard.Board
{
    /// <summary>
    /// Touch mapping, pure: the grid is drawn Uniform (16:9) and centred in the view. WPF 7.1.5 kept
    /// this beside BoardTileView and took a Point; Core takes the two coordinates.
    /// </summary>
    public static class BoardTouch
    {
        public static bool TileAt(double normalizedX, double normalizedY, double viewW, double viewH, out double tx, out double ty)
        {
            tx = ty = 0;
            const double gw = BoardPicture.GridW, gh = BoardPicture.GridH;
            double s = Math.Min(viewW / gw, viewH / gh);
            if (s <= 0) return false;
            double ox = (viewW - gw * s) / 2, oy = (viewH - gh * s) / 2;
            double x = (normalizedX * viewW - ox) / s, y = (normalizedY * viewH - oy) / s;
            if (x < 0 || y < 0 || x >= gw || y >= gh) return false;
            tx = Math.Floor(x);
            ty = Math.Floor(y);
            return true;
        }
    }
}
