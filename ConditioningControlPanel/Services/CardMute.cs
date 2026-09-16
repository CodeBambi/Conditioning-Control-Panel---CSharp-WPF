using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// When a dashboard tile's art goes grey: the ONE answer both card controls ask.
    ///
    /// <para>The mosaic's off tiles used to rest at 62% opacity and nothing else, and the Discord
    /// verdict was that on and off look the same. Opacity on a dark wall reads as "a bit further
    /// away", not as "off". Colour does: an active tile is the only one wearing its own
    /// palette, and a glance down the wall says what START will run.</para>
    ///
    /// <para>Pure on purpose, so the priority order lives in one testable place: a lock veil
    /// wins over the mute (a locked tile keeps the look it always had and must not read as
    /// merely switched off), a tease keeps its blur, and a hover hands the colour back so an off
    /// tile still reads as live.</para>
    /// </summary>
    public static class CardMuteRule
    {
        /// <summary>How long the colour takes to drain or return on a state change or a hover.
        /// Interaction motion, so it runs whenever <see cref="MotionFx.AllowTransitions"/> does;
        /// under Off the tile snaps.</summary>
        public const int FadeMs = 200;

        /// <summary>A single tile: grey only when it opted into dimming, its feature is off, it is
        /// not locked, not teased, and the mouse is elsewhere.</summary>
        public static bool ShouldMute(bool dimWhenInactive, bool isActive, bool isLocked, bool hovered, bool teased)
            => dimWhenInactive && !isActive && !isLocked && !teased && !hovered;

        /// <summary>One half of a split tile: grey when its feature is off and the mouse has not
        /// committed the card to it. No opt-in here - a split tile only ever carries toggles.</summary>
        public static bool ShouldMuteHalf(bool isActive, bool halfHovered)
            => !isActive && !halfHovered;

        /// <summary>Snap or fade: fade only when motion is allowed AND the card is already on
        /// screen, so a tile never animates its first frame.</summary>
        public static int TransitionMs(bool allowTransitions, bool loaded)
            => allowTransitions && loaded ? FadeMs : 0;
    }

    /// <summary>
    /// A greyscale twin of a tile's art, built once per source and cached for its lifetime.
    ///
    /// <para>Not a pixel shader: WPF's ShaderEffect needs a compiled .ps and a build step this
    /// repo does not have, and the cheap alternative is at least as good here. The twin is
    /// downscaled to <see cref="MaxEdge"/> before conversion (a tile is ~150px on the wall, the
    /// source art is 1024), so the conversion is a few thousand pixels, alpha survives (the
    /// vault and the tease tile art carry it; a Gray8 FormatConvertedBitmap would drop it), and
    /// the result is a frozen bitmap the card can paint under its colour layer for free.</para>
    /// </summary>
    public static class ArtDesaturate
    {
        /// <summary>Longest edge of the grey twin. Larger than a wall tile, smaller than the
        /// source, so the twin never looks soft on the tile and never costs a full decode.</summary>
        public const int MaxEdge = 320;

        private static readonly ConditionalWeakTable<ImageSource, ImageSource> Cache = new();

        /// <summary>The grey twin, or null when the source is not a bitmap (a DrawingImage, say)
        /// or cannot be read - the caller then falls back to the opacity dim alone.</summary>
        public static ImageSource? Of(ImageSource? source)
        {
            if (source is not BitmapSource bmp) return null;
            try
            {
                if (Cache.TryGetValue(source, out var hit)) return hit;
                var grey = Build(bmp);
                if (grey == null) return null;
                Cache.AddOrUpdate(source, grey);
                return grey;
            }
            catch (Exception ex)
            {
                Diag.Swallowed(ex, "art desaturate");
                return null;
            }
        }

        private static ImageSource? Build(BitmapSource bmp)
        {
            if (bmp.PixelWidth <= 0 || bmp.PixelHeight <= 0) return null;

            BitmapSource src = bmp;
            int longest = Math.Max(bmp.PixelWidth, bmp.PixelHeight);
            if (longest > MaxEdge)
            {
                double scale = (double)MaxEdge / longest;
                src = new TransformedBitmap(bmp, new ScaleTransform(scale, scale));
            }
            if (src.Format != PixelFormats.Bgra32)
                src = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);

            int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
            var pixels = new byte[stride * h];
            src.CopyPixels(pixels, stride, 0);
            ToGrey(pixels);

            var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }

        /// <summary>
        /// In-place Rec.601 luma over a BGRA32 buffer. Alpha is untouched, and every colour
        /// channel lands on the same value, so the pixel is grey by construction. Kept pure so
        /// the maths can be pinned by a test with no bitmap in sight.
        /// </summary>
        internal static void ToGrey(byte[] bgra)
        {
            for (int i = 0; i + 3 < bgra.Length; i += 4)
            {
                // 0.114 B + 0.587 G + 0.299 R, in 16.16 fixed point - no doubles per pixel.
                int luma = (bgra[i] * 7471 + bgra[i + 1] * 38470 + bgra[i + 2] * 19595 + 32768) >> 16;
                bgra[i] = bgra[i + 1] = bgra[i + 2] = (byte)luma;
            }
        }
    }
}
