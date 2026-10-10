using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// A CPU Skia surface for ambient effects that is cheaper than <c>SKElement</c> in the three
    /// ways that matter at 30 fps (perf pass, 2026-10-07):
    /// <list type="bullet">
    /// <item>It rasters at <see cref="ResolutionScale"/> of the screen's pixels. Fog, aurora and
    /// soft sprites read the same at half resolution and cost a quarter of the fill and of the
    /// bitmap upload; WPF scales the result back up with linear filtering.</item>
    /// <item>It keeps ONE <see cref="WriteableBitmap"/> and repaints it in place. The element's
    /// own render data (a single DrawImage) is only re-recorded when the bitmap is replaced, so a
    /// frame costs a pixel update and no visual-tree invalidation.</item>
    /// <item>It paints when asked (<see cref="Redraw"/>), synchronously, so the caller's clock is
    /// the frame clock.</item>
    /// </list>
    /// The paint handler sees an <see cref="SKImageInfo"/> sized to the FULL screen pixels with
    /// the canvas pre-scaled, so drawing code written for <c>SKElement</c> works unchanged.
    /// </summary>
    public sealed class FxSurface : FrameworkElement
    {
        private WriteableBitmap? _bitmap;
        private double _scale = 1.0;

        public event EventHandler<SKPaintSurfaceEventArgs>? PaintSurface;

        public FxSurface()
        {
            IsHitTestVisible = false;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
            SizeChanged += (_, _) => Redraw();
        }

        /// <summary>Share of the screen's pixels the surface rasters at (0.25 to 1).</summary>
        public double ResolutionScale
        {
            get => _scale;
            set
            {
                var v = Math.Clamp(value, 0.25, 1.0);
                if (Math.Abs(v - _scale) < 0.001) return;
                _scale = v;
                Redraw();
            }
        }

        /// <summary>Pixel size of the backing bitmap right now (tests).</summary>
        internal (int W, int H) BackingSize => _bitmap == null ? (0, 0) : (_bitmap.PixelWidth, _bitmap.PixelHeight);

        /// <summary>Clear and repaint the surface now.</summary>
        public void Redraw()
        {
            double aw = ActualWidth, ah = ActualHeight;
            if (aw <= 0 || ah <= 0 || double.IsNaN(aw) || double.IsNaN(ah)) return;

            double dpiX = 1, dpiY = 1;
            try { var dpi = VisualTreeHelper.GetDpi(this); dpiX = dpi.DpiScaleX; dpiY = dpi.DpiScaleY; } catch { }

            int fullW = Math.Max(1, (int)Math.Ceiling(aw * dpiX));
            int fullH = Math.Max(1, (int)Math.Ceiling(ah * dpiY));
            int w = Math.Max(1, (int)Math.Ceiling(fullW * _scale));
            int h = Math.Max(1, (int)Math.Ceiling(fullH * _scale));

            if (_bitmap == null || _bitmap.PixelWidth != w || _bitmap.PixelHeight != h)
            {
                _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
                InvalidateVisual();
            }

            var bmp = _bitmap;
            bmp.Lock();
            try
            {
                var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
                using (var surface = SKSurface.Create(info, bmp.BackBuffer, bmp.BackBufferStride))
                {
                    if (surface != null)
                    {
                        var canvas = surface.Canvas;
                        canvas.Clear(SKColors.Transparent);
                        canvas.Save();
                        canvas.Scale((float)w / fullW, (float)h / fullH);
                        PaintSurface?.Invoke(this, new SKPaintSurfaceEventArgs(surface,
                            new SKImageInfo(fullW, fullH, SKColorType.Bgra8888, SKAlphaType.Premul)));
                        canvas.Restore();
                        canvas.Flush();
                    }
                }
                bmp.AddDirtyRect(new Int32Rect(0, 0, w, h));
            }
            finally { bmp.Unlock(); }
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (_bitmap != null) dc.DrawImage(_bitmap, new Rect(0, 0, ActualWidth, ActualHeight));
        }
    }
}
