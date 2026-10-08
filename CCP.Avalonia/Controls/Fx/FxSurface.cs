using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Controls.Fx
{
    /// <summary>What a paint handler gets: the canvas (pre-scaled) and the FULL-resolution size.</summary>
    public sealed class FxPaintEventArgs : EventArgs
    {
        public FxPaintEventArgs(SKCanvas canvas, SKImageInfo info)
        {
            Canvas = canvas;
            Info = info;
        }

        public SKCanvas Canvas { get; }

        /// <summary>Sized to the control's FULL device pixels; the canvas is already scaled down to
        /// the backing resolution, so drawing code written for full size works unchanged.</summary>
        public SKImageInfo Info { get; }
    }

    /// <summary>
    /// PORTED from ConditioningControlPanel/Controls/FxSurface.cs (perf pass, 2026-10-07).
    ///
    /// <para>A CPU Skia surface for ambient effects, cheap in the three ways that matter at 30 fps:</para>
    /// <list type="bullet">
    /// <item>It rasters at <see cref="ResolutionScale"/> of the screen's pixels. Fog, aurora and
    /// soft sprites read the same at half resolution and cost a quarter of the fill and of the
    /// upload; the draw scales the result back up with linear filtering.</item>
    /// <item>It keeps ONE offscreen <see cref="SKSurface"/> and repaints it in place. Each paint
    /// hands the render thread an immutable snapshot (copy-on-write, so no copy is made unless the
    /// render thread still holds the last one), drawn through an <see cref="ICustomDrawOperation"/>
    /// that leases the window's own Skia canvas (<see cref="ISkiaSharpApiLeaseFeature"/>), the same
    /// seam <see cref="TakeoverOrb"/> uses.</item>
    /// <item>It paints when asked (<see cref="Redraw"/>), synchronously, so the caller's clock is
    /// the frame clock.</item>
    /// </list>
    ///
    /// <para>Layers paint INTO the surface with <see cref="SKBlendMode.Plus"/> (their own paints),
    /// which is where the bloom comes from: overlapping glows add. The finished surface is then
    /// composited onto the window with <see cref="CompositeBlend"/>, SrcOver by default, which is
    /// exactly what WPF did with its WriteableBitmap. Set it to Plus for a layer that should also
    /// add onto what is behind it.</para>
    /// </summary>
    public sealed class FxSurface : Control
    {
        private double _scale = 1.0;
        private SKSurface? _surface;
        private int _w, _h;
        private FxFrame? _frame;

        public event EventHandler<FxPaintEventArgs>? PaintSurface;

        public FxSurface()
        {
            IsHitTestVisible = false;
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

        /// <summary>How the finished surface lands on the window. SrcOver = WPF; Plus = add onto the backdrop.</summary>
        public SKBlendMode CompositeBlend { get; set; } = SKBlendMode.SrcOver;

        /// <summary>Pixel size of the backing surface right now (tests).</summary>
        internal (int W, int H) BackingSize => _surface == null ? (0, 0) : (_w, _h);

        /// <summary>Paints since creation (tests, bench).</summary>
        internal int PaintCount { get; private set; }

        /// <summary>Wall time of the last paint in ms (bench).</summary>
        internal double LastPaintMs { get; private set; }

        /// <summary>Clear and repaint the surface now.</summary>
        public void Redraw()
        {
            double aw = Bounds.Width, ah = Bounds.Height;
            if (aw <= 0 || ah <= 0 || double.IsNaN(aw) || double.IsNaN(ah)) return;

            double dpi = 1;
            try { dpi = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1; } catch { }
            if (dpi <= 0 || double.IsNaN(dpi)) dpi = 1;

            int fullW = Math.Max(1, (int)Math.Ceiling(aw * dpi));
            int fullH = Math.Max(1, (int)Math.Ceiling(ah * dpi));
            int w = Math.Max(1, (int)Math.Ceiling(fullW * _scale));
            int h = Math.Max(1, (int)Math.Ceiling(fullH * _scale));

            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_surface == null || _w != w || _h != h)
            {
                _surface?.Dispose();
                _surface = SKSurface.Create(new SKImageInfo(w, h, SKImageInfo.PlatformColorType, SKAlphaType.Premul));
                _w = w;
                _h = h;
                if (_surface == null) return;
            }

            var canvas = _surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.Save();
            canvas.Scale((float)w / fullW, (float)h / fullH);
            try
            {
                PaintSurface?.Invoke(this, new FxPaintEventArgs(canvas,
                    new SKImageInfo(fullW, fullH, SKImageInfo.PlatformColorType, SKAlphaType.Premul)));
            }
            finally
            {
                canvas.Restore();
                canvas.Flush();
            }

            var next = new FxFrame(_surface.Snapshot());
            var old = _frame;
            _frame = next;
            old?.Release();
            PaintCount++;
            LastPaintMs = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            InvalidateVisual();
        }

        /// <summary>The surface's current pixels as an image copy (tests). Null before the first paint.</summary>
        internal SKBitmap? CopyBacking()
        {
            var f = _frame;
            if (f == null) return null;
            lock (f.Gate)
            {
                if (f.Image == null) return null;
                var bmp = new SKBitmap(f.Image.Width, f.Image.Height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
                return f.Image.ReadPixels(bmp.Info, bmp.GetPixels(), bmp.RowBytes, 0, 0) ? bmp : null;
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == BoundsProperty &&
                change.OldValue is Rect o && change.NewValue is Rect n && o.Size != n.Size)
                Redraw();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _frame?.Release();
            _frame = null;
            _surface?.Dispose();
            _surface = null;
        }

        public override void Render(DrawingContext context)
        {
            var f = _frame;
            if (f == null) return;
            context.Custom(new FxDrawOp(f, new Rect(Bounds.Size), CompositeBlend));
        }

        /// <summary>
        /// One painted frame. The render thread draws it under <see cref="Gate"/>, and the UI
        /// thread releases it under the same lock, so a snapshot can never be freed mid-draw.
        /// </summary>
        private sealed class FxFrame
        {
            public readonly object Gate = new();
            public SKImage? Image;

            public FxFrame(SKImage image) => Image = image;

            public void Release()
            {
                lock (Gate)
                {
                    Image?.Dispose();
                    Image = null;
                }
            }
        }

        private sealed class FxDrawOp : ICustomDrawOperation
        {
            private readonly FxFrame _frame;
            private readonly SKBlendMode _blend;

            public FxDrawOp(FxFrame frame, Rect bounds, SKBlendMode blend)
            {
                _frame = frame;
                Bounds = bounds;
                _blend = blend;
            }

            public Rect Bounds { get; }
            public bool HitTest(Point p) => false;
            // Never equal: a reused op would freeze the surface on its last frame.
            public bool Equals(ICustomDrawOperation? other) => false;
            public void Dispose() { }

            public void Render(ImmediateDrawingContext context)
            {
                var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                if (lease is null) return;
                using var leased = lease.Lease();
                var canvas = leased.SkCanvas;
                lock (_frame.Gate)
                {
                    var img = _frame.Image;
                    if (img == null) return;
                    using var paint = new SKPaint { BlendMode = _blend };
                    canvas.DrawImage(img, SKRect.Create(0, 0, (float)Bounds.Width, (float)Bounds.Height),
                        new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), paint);
                }
            }
        }
    }
}
