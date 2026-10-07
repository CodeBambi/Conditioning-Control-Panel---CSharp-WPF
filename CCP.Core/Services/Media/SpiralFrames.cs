using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The portable half of the spiral overlay: the opacity curve and the frame-cache budget, shared
    /// by every head so they paint and decode the same spiral the same way. The window, the decoder
    /// and the frame clock stay in each head.
    /// </summary>
    public static class SpiralFrames
    {
        /// <summary>Long side a decoded frame is capped to: the spiral is stretched over the whole
        /// screen at low opacity, so a bigger frame buys nothing visible (#572).</summary>
        public const int MaxDimension = 1280;
        public const long MaxCacheBytes = 300L * 1024 * 1024;
        public const int MaxFrames = 120;

        /// <summary>
        /// ccp-bugs #722: the spiral slider (0..1) to the alpha it paints. The spiral used to paint
        /// at a flat tenth of the slider, so 100% was 0.1 and barely visible. The bottom half keeps
        /// that exact curve (nobody's 10-50% setting changes); above it the line climbs steeply so
        /// 100% paints at 0.6. Every spiral opacity path goes through here.
        /// </summary>
        public static double Paint(double f)
        {
            f = double.IsNaN(f) ? 0 : Math.Clamp(f, 0, 1);
            return f <= 0.5 ? f * 0.1 : 0.05 + (f - 0.5) * 1.1;
        }

        /// <summary>
        /// Which frames to keep and at what size (#572 "3.5 GB / laggy"): the long side capped at
        /// <see cref="MaxDimension"/>, at most <see cref="MaxFrames"/> frames and
        /// <see cref="MaxCacheBytes"/> in total (never fewer than 8). A GIF delay outside 20..500 ms
        /// plays at 50 ms. Ceiling stride, not floor: floor keeps frames 0..max-1 and drops the tail,
        /// breaking the loop point (#683); the delay is scaled by the stride so the loop keeps its
        /// wall-clock length.
        /// </summary>
        public static (int Width, int Height, int Frames, int Step, int DelayMs) Plan(
            int width, int height, int frameCount, int frameDelayMs)
        {
            if (frameDelayMs < 20 || frameDelayMs > 500) frameDelayMs = 50;
            double scale = Math.Min(1.0, (double)MaxDimension / Math.Max(1, Math.Max(width, height)));
            int w = Math.Max(1, (int)Math.Round(width * scale));
            int h = Math.Max(1, (int)Math.Round(height * scale));
            long bytesPerFrame = (long)w * h * 4;
            var maxFrames = (int)Math.Min(Math.Min(frameCount, MaxFrames), Math.Max(8, MaxCacheBytes / Math.Max(1, bytesPerFrame)));
            var step = Math.Max(1, (int)Math.Ceiling(frameCount / (double)Math.Max(1, maxFrames)));
            return (w, h, maxFrames, step, frameDelayMs * step);
        }
    }
}
