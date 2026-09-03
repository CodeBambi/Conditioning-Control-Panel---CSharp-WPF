using System.Windows;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Helpers
{
    /// <summary>The one place a Core viewbox becomes a WPF rect: at the brush.</summary>
    internal static class ArtViewboxExtensions
    {
        public static Rect ToRect(this ArtViewbox v) => new(v.X, v.Y, v.Width, v.Height);
    }
}
