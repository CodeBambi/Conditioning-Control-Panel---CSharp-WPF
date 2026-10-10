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
            Retire(old);
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
            Retire(_frame);
            _frame = null;
            _surface?.Dispose();
            _surface = null;
        }

        public override void Render(DrawingContext context)
        {
            var f = _frame;
            if (f == null || !f.TryAddRef()) return;
            context.Custom(new FxDrawOp(f, new Rect(Bounds.Size), CompositeBlend));
        }

        // ---- frame lifetime (the section edge flicker, 2026-10-09) ---------------------------------
        //
        // Avalonia composes on its render thread, a frame behind the UI thread: while the UI thread
        // paints frame N+1 the render thread can still be drawing the op recorded for frame N. The
        // first port disposed frame N's image the moment N+1 was painted, so that late draw found
        // no image and drew NOTHING: the strip blinked out for one composed frame, at random, which
        // read as the section edge glow flickering (WPF repainted one WriteableBitmap in place and
        // never had a blank frame). Now each recorded draw op holds a reference on its frame and
        // the image is freed only when the surface AND every op that recorded it are done. Avalonia
        // disposes a replaced op on the render thread; the retired list is a hard cap in case it
        // ever does not, so a missed Dispose can never leak more than MaxRetired snapshots.

        /// <summary>Frames replaced on the UI thread that a recorded op still draws.</summary>
        private readonly System.Collections.Generic.List<FxFrame> _retired = new();

        /// <summary>How many replaced-but-referenced frames may wait for their ops before the
        /// oldest is freed anyway (the render thread is never this many frames behind).</summary>
        internal const int MaxRetired = 8;

        /// <summary>Frames still holding an image: the current one plus any a late op draws (tests).</summary>
        internal int LiveFrames
        {
            get
            {
                int n = _frame?.IsAlive == true ? 1 : 0;
                foreach (var f in _retired) if (f.IsAlive) n++;
                return n;
            }
        }

        /// <summary>A draw op for the current frame, as Render records it (tests). Dispose it.</summary>
        internal ICustomDrawOperation? RecordForTests()
        {
            var f = _frame;
            return f != null && f.TryAddRef() ? new FxDrawOp(f, new Rect(Bounds.Size), CompositeBlend) : null;
        }

        /// <summary>True while <paramref name="op"/> (from <see cref="RecordForTests"/>) can still draw its pixels.</summary>
        internal static bool OpCanDraw(ICustomDrawOperation op) => op is FxDrawOp d && d.CanDraw;

        private void Retire(FxFrame? old)
        {
            if (old != null && !old.Release()) _retired.Add(old);
            _retired.RemoveAll(f => !f.IsAlive);
            while (_retired.Count > MaxRetired)
            {
                _retired[0].ForceDispose();
                _retired.RemoveAt(0);
            }
        }

        /// <summary>
        /// One painted frame, reference counted: the surface holds one reference while it is the
        /// current frame, and every recorded draw op holds one until Avalonia disposes the op. The
        /// render thread draws it under <see cref="Gate"/>; the image is freed under the same lock
        /// when the last reference goes, so a snapshot is never freed mid-draw or before a late draw.
        /// </summary>
        private sealed class FxFrame
        {
            public readonly object Gate = new();
            public SKImage? Image;
            private int _refs = 1;   // the surface's own

            public FxFrame(SKImage image) => Image = image;

            public bool IsAlive { get { lock (Gate) return Image != null; } }

            public bool TryAddRef()
            {
                lock (Gate)
                {
                    if (Image == null) return false;
                    _refs++;
                    return true;
                }
            }

            /// <summary>Drops one reference; true when the image is gone afterwards.</summary>
            public bool Release()
            {
                lock (Gate)
                {
                    if (Image == null) return true;
                    if (--_refs > 0) return false;
                    Image.Dispose();
                    Image = null;
                    return true;
                }
            }

            public void ForceDispose()
            {
                lock (Gate)
                {
                    Image?.Dispose();
                    Image = null;
                    _refs = 0;
                }
            }
        }

        private sealed class FxDrawOp : ICustomDrawOperation
        {
            private readonly FxFrame _frame;
            private readonly SKBlendMode _blend;
            private int _disposed;

            public FxDrawOp(FxFrame frame, Rect bounds, SKBlendMode blend)
            {
                _frame = frame;   // the caller took a reference for this op
                Bounds = bounds;
                _blend = blend;
            }

            public Rect Bounds { get; }
            public bool CanDraw => System.Threading.Volatile.Read(ref _disposed) == 0 && _frame.IsAlive;
            public bool HitTest(Point p) => false;
            // Never equal: a reused op would freeze the surface on its last frame.
            public bool Equals(ICustomDrawOperation? other) => false;

            public void Dispose()
            {
                if (System.Threading.Interlocked.Exchange(ref _disposed, 1) == 0) _frame.Release();
            }

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
