// PORTED from ConditioningControlPanel/Services/CardMute.cs (7.1.5): the rule that drains an off
// Home tile to grey, and the greyscale twin of its art. Opacity alone was read as "a bit further
// away", never as "off"; colour is what a glance sorts by (WPF 6.9.4).

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    internal static class CardMuteRule
    {
        /// <summary>How long the colour takes to drain or return on a state change or a hover.
        /// Interaction motion: runs whenever transitions are allowed; under Off the tile snaps.</summary>
        public const int FadeMs = 200;

        /// <summary>A single tile: grey only when it opted into dimming, its feature is off, it is
        /// not locked, not teased, and the mouse is elsewhere.</summary>
        public static bool ShouldMute(bool dimWhenInactive, bool isActive, bool isLocked, bool hovered, bool teased)
            => dimWhenInactive && !isActive && !isLocked && !teased && !hovered;

        /// <summary>One half of a split tile: grey when its feature is off and the mouse has not
        /// committed the card to it.</summary>
        public static bool ShouldMuteHalf(bool isActive, bool halfHovered)
            => !isActive && !halfHovered;

        /// <summary>Snap or fade: fade only when motion is allowed AND the card is already on
        /// screen, so a tile never animates its first frame.</summary>
        public static int TransitionMs(bool allowTransitions, bool loaded)
            => allowTransitions && loaded ? FadeMs : 0;

        /// <summary>Paints a grey layer's opacity: a one-shot ease-out fade when <paramref name="ms"/>
        /// is positive, a snap otherwise. A compositor transition, never a looping animation.</summary>
        public static void Fade(Visual layer, bool mute, int ms)
        {
            double to = mute ? 1.0 : 0.0;
            if (ms <= 0)
            {
                layer.Transitions = null;
                layer.Opacity = to;
                return;
            }
            layer.Transitions = new global::Avalonia.Animation.Transitions
            {
                new global::Avalonia.Animation.DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(ms),
                    Easing = new global::Avalonia.Animation.Easings.QuadraticEaseOut(),
                },
            };
            layer.Opacity = to;
        }
    }

    /// <summary>
    /// A greyscale twin of a tile's art, built once per source and cached for its lifetime. The
    /// twin is downscaled to <see cref="MaxEdge"/> first (a tile is ~150 px on the wall, the source
    /// art is 1376 px), so the conversion is cheap and the twin never looks soft on the tile.
    /// </summary>
    internal static class ArtDesaturate
    {
        public const int MaxEdge = 320;

        private static readonly ConditionalWeakTable<object, Bitmap> Cache = new();

        /// <summary>The grey twin, or null when the source is not a bitmap or cannot be read; the
        /// caller then falls back to the opacity dim alone.</summary>
        public static Bitmap? Of(IImageBrushSource? source)
        {
            if (source is not Bitmap bmp) return null;
            try
            {
                if (Cache.TryGetValue(bmp, out var hit)) return hit;
                var grey = Build(bmp);
                if (grey == null) return null;
                Cache.AddOrUpdate(bmp, grey);
                return grey;
            }
            catch (Exception ex)
            {
                Log.Debug("ArtDesaturate: {E}", ex.Message);
                return null;
            }
        }

        private static Bitmap? Build(Bitmap bmp)
        {
            var size = bmp.PixelSize;
            if (size.Width <= 0 || size.Height <= 0) return null;
            int longest = Math.Max(size.Width, size.Height);
            Bitmap src = bmp;
            Bitmap? scaled = null;
            if (longest > MaxEdge)
            {
                double k = (double)MaxEdge / longest;
                scaled = bmp.CreateScaledBitmap(
                    new PixelSize(Math.Max(1, (int)Math.Round(size.Width * k)), Math.Max(1, (int)Math.Round(size.Height * k))),
                    BitmapInterpolationMode.HighQuality);
                src = scaled;
            }
            try
            {
                var format = src.Format;
                bool bgra = format == PixelFormats.Bgra8888;
                if (!bgra && format != PixelFormats.Rgba8888) return null;
                int w = src.PixelSize.Width, h = src.PixelSize.Height, stride = w * 4;
                var pixels = new byte[stride * h];
                var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try
                {
                    src.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), pixels.Length, stride);
                    ToGrey(pixels, bgra);
                    return new Bitmap(format!.Value, src.AlphaFormat ?? AlphaFormat.Premul, handle.AddrOfPinnedObject(),
                        new PixelSize(w, h), new Vector(96, 96), stride);
                }
                finally { handle.Free(); }
            }
            finally { scaled?.Dispose(); }
        }

        /// <summary>
        /// In-place Rec.601 luma over a 4-byte buffer. Alpha is untouched and every colour channel
        /// lands on the same value, so the pixel is grey by construction (premultiplied stays valid:
        /// luma is linear in the channels).
        /// </summary>
        internal static void ToGrey(byte[] px, bool bgra = true)
        {
            int bi = bgra ? 0 : 2, ri = bgra ? 2 : 0;
            for (int i = 0; i + 3 < px.Length; i += 4)
            {
                // 0.114 B + 0.587 G + 0.299 R, in 16.16 fixed point.
                int luma = (px[i + bi] * 7471 + px[i + 1] * 38470 + px[i + ri] * 19595 + 32768) >> 16;
                px[i] = px[i + 1] = px[i + 2] = (byte)luma;
            }
        }
    }
}
