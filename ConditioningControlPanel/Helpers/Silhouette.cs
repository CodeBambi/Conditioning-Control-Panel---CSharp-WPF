using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ConditioningControlPanel.Services;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace ConditioningControlPanel.Helpers
{
    /// <summary>
    /// A solid-colour cut-out of a wardrobe item: the item's own alpha channel used as a stencil,
    /// filled flat. The Achievements tab paints locked rewards this way - you get the SHAPE of the
    /// thing you have not earned (a bow, a collar, a charm) without the colour that would make it
    /// feel already yours.
    ///
    /// Two rules that matter:
    ///   * The host Grid is ALWAYS square (explicit Width/Height = size). The mask is a Uniform
    ///     ImageBrush over the whole element, so a non-square host would letterbox the stencil and
    ///     the fill rectangle would paint the bars too.
    ///   * The mask cache is keyed by ITEM ID, never by ImageSource.
    ///     <see cref="WardrobeCatalog.GetImage"/> decodes with BitmapCreateOptions.IgnoreImageCache,
    ///     so the ImageSource instance it hands back is not a stable identity to key on - two calls
    ///     for the same id can be two different objects.
    ///
    /// A missing PNG returns <c>null</c>. Callers fall back to a glyph; the tab never renders a
    /// broken tile because art did not ship.
    /// </summary>
    public static class Silhouette
    {
        private static readonly object _gate = new();
        private static readonly Dictionary<string, ImageBrush> _masks = new(StringComparer.Ordinal);

        /// <summary>Default fill for a locked cut-out - the tab's own card ink.</summary>
        public static readonly SolidColorBrush DefaultFill =
            FrozenBrush(Color.FromRgb(0x0F, 0x0F, 0x1C));

        /// <summary>
        /// Builds the cut-out, or null when the item has no usable art.
        /// </summary>
        /// <param name="itemId">Wardrobe registry id.</param>
        /// <param name="size">Edge length in DIPs; the host is square at exactly this size.</param>
        /// <param name="fill">The stencil's colour.</param>
        /// <param name="glow">Optional accent bloom painted UNDER the fill (blurred, 25%).</param>
        public static Grid? Build(string? itemId, double size, Brush? fill, Brush? glow = null)
        {
            if (string.IsNullOrWhiteSpace(itemId) || size <= 0) return null;

            var mask = GetMask(itemId!);
            if (mask == null) return null;                    // no art - caller draws a glyph

            var paint = fill ?? DefaultFill;

            var host = new Grid
            {
                Width = size,
                Height = size,
                IsHitTestVisible = false,
            };

            if (glow != null)
            {
                host.Children.Add(new Rectangle
                {
                    Fill = glow,
                    OpacityMask = mask,
                    Opacity = 0.25,
                    // Performance bias: this is decoration behind a 16-56px stencil, not a hero
                    // blur. The quality bias would put every card on a full-rate raster pass.
                    Effect = new BlurEffect { Radius = 8, RenderingBias = RenderingBias.Performance },
                    IsHitTestVisible = false,
                });
            }

            host.Children.Add(new Rectangle
            {
                Fill = paint,
                OpacityMask = mask,
                IsHitTestVisible = false,
            });

            return host;
        }

        /// <summary>
        /// Drops every cached mask. Called from <see cref="WardrobeCatalog.Invalidate"/> - the art
        /// stage can rewrite these PNGs under a running app, and a stale frozen brush would keep
        /// painting the old shape forever.
        /// </summary>
        public static void InvalidateCache()
        {
            lock (_gate) { _masks.Clear(); }
        }

        // ---------------------------------------------------------------------------------

        private static ImageBrush? GetMask(string itemId)
        {
            lock (_gate)
            {
                if (_masks.TryGetValue(itemId, out var cached)) return cached;

                ImageBrush? brush = null;
                try
                {
                    var art = WardrobeCatalog.GetImage(itemId);
                    if (art != null)
                    {
                        brush = new ImageBrush(art) { Stretch = Stretch.Uniform };
                        if (brush.CanFreeze) brush.Freeze();
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Silhouette: mask for {Id} failed: {E}", itemId, ex.Message);
                    brush = null;
                }

                // Null is NOT cached: art can land mid-session (content pack install), and the
                // dictionary is typed to a non-null brush anyway.
                if (brush != null) _masks[itemId] = brush;
                return brush;
            }
        }

        private static SolidColorBrush FrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
