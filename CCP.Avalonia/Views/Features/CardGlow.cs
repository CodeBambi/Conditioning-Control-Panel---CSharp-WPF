using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// The on glow of a mosaic tile (perf, 2026-10-09). WPF put a DropShadowEffect on the tile's
    /// root border and breathed its Opacity; in Avalonia an Effect makes the whole tile (art, text,
    /// chrome) an offscreen layer that is re-rendered and blurred on every animation frame, which
    /// was most of the dashboard lag with the engine on. The glow is now a sibling Border behind
    /// the tile wearing a BoxShadow in FxGlowColor; its Opacity breathes, nothing else moves.
    /// </summary>
    internal static class CardGlow
    {
        /// <summary>Default blur, the WPF DropShadowEffect BlurRadius.</summary>
        public const double BlurRadius = 18;

        public static BoxShadows Shadow(object? colour, double blur = BlurRadius) =>
            new(new BoxShadow { Blur = blur, Color = colour is Color c ? c : Colors.HotPink });

        /// <summary>Keeps <paramref name="layer"/>'s shadow in the current mod's FxGlowColor.</summary>
        public static void Bind(Border layer, Func<double> blur)
        {
            layer.Bind(Border.BoxShadowProperty,
                layer.GetResourceObservable("FxGlowColor", c => Shadow(c, blur())));
        }

        /// <summary>Re-applies the shadow after a blur change (performance tier).</summary>
        public static void SetBlur(Border layer, double blur) => Bind(layer, () => blur);   // a new binding replaces the old
    }
}
