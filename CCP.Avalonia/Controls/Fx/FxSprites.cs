using System;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Controls.Fx
{
    /// <summary>
    /// The Skia pieces every additive FX surface shares: the white soft radial sprites (baked once
    /// for the whole app and tinted per draw by a Modulate colour filter), the sampling, and the
    /// alpha byte helper. Twin of the statics at the foot of the WPF AmbientFxCanvas.cs.
    /// </summary>
    public static class FxSprites
    {
        private static SKImage? _dot;
        private static SKImage? _glow;
        private static readonly object Lock = new();

        /// <summary>Linear sampling (SkiaSharp 3 dropped SKPaint.FilterQuality; WPF used Medium = linear + nearest mip).</summary>
        public static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Nearest);

        /// <summary>White soft radial dot (128 px, no flat core), shared by every canvas.</summary>
        public static SKImage? Dot
        {
            get
            {
                if (_dot != null) return _dot;
                lock (Lock) { _dot ??= BakeRadial(128, 0f); }
                return _dot;
            }
        }

        /// <summary>A tighter-cored radial (160 px, flat to 0.28) for the glow-breath and big fog puffs.</summary>
        public static SKImage? GlowSprite
        {
            get
            {
                if (_glow != null) return _glow;
                lock (Lock) { _glow ??= BakeRadial(160, 0.28f); }
                return _glow;
            }
        }

        /// <summary>0..1 to an alpha byte, clamped.</summary>
        public static byte Alpha(float a) => (byte)Math.Clamp(a * 255f, 0f, 255f);

        /// <summary>A paint that adds what it draws (SKBlendMode.Plus), the WPF ambient paint.</summary>
        public static SKPaint AdditivePaint() => new() { IsAntialias = true, BlendMode = SKBlendMode.Plus };

        /// <summary>Draw <paramref name="img"/> stretched over a w x h box centred on (cx, cy).</summary>
        public static void DrawSprite(SKCanvas canvas, SKImage? img, SKPaint paint, float cx, float cy, float w, float h)
        {
            if (img == null) return;
            canvas.DrawImage(img, new SKRect(cx - w / 2f, cy - h / 2f, cx + w / 2f, cy + h / 2f), Sampling, paint);
        }

        /// <summary>WPF AmbientFxCanvas.BakeRadial, verbatim: white out to <paramref name="coreStop"/>, clear at the rim.</summary>
        public static SKImage? BakeRadial(int size, float coreStop)
        {
            try
            {
                var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
                using var surface = SKSurface.Create(info);
                surface.Canvas.Clear(SKColors.Transparent);
                float r = size / 2f;
                using (var shader = SKShader.CreateRadialGradient(
                    new SKPoint(r, r), r,
                    new[] { SKColors.White, SKColors.White, SKColors.White.WithAlpha(0) },
                    new[] { 0f, Math.Clamp(coreStop, 0f, 0.9f), 1f },
                    SKShaderTileMode.Clamp))
                using (var paint = new SKPaint { IsAntialias = true, Shader = shader })
                    surface.Canvas.DrawCircle(r, r, r, paint);
                return surface.Snapshot();
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug("FxSprites.BakeRadial: {E}", ex.Message);
                return null;
            }
        }
    }
}
