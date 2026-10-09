using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Controls.Fx
{
    /// <summary>
    /// A picture that fills its box the way an <see cref="ImageBrush"/> background does (same
    /// stretch, alignment, source crop and rounded corners), drawn with ONE direct DrawImage.
    ///
    /// <para>Why (perf, 2026-10-09): Avalonia's Skia backend paints an ImageBrush fill by rendering
    /// the tile into an intermediate surface and building a shader from it on EVERY draw. Any
    /// region repainted every frame (the Home mosaic sits over an ambient FX layer, so every tile
    /// is repainted at 30 fps) paid a surface + blit per tile per frame: 13 s of the render
    /// thread's minute in the owner's trace. DrawImage under a rounded clip is a direct bitmap
    /// draw.</para>
    /// </summary>
    public sealed class ArtFill : Control
    {
        public static readonly StyledProperty<IImage?> SourceProperty =
            AvaloniaProperty.Register<ArtFill, IImage?>(nameof(Source));
        public static readonly StyledProperty<Stretch> StretchProperty =
            AvaloniaProperty.Register<ArtFill, Stretch>(nameof(Stretch), Stretch.UniformToFill);
        public static readonly StyledProperty<AlignmentX> AlignmentXProperty =
            AvaloniaProperty.Register<ArtFill, AlignmentX>(nameof(AlignmentX), AlignmentX.Center);
        public static readonly StyledProperty<AlignmentY> AlignmentYProperty =
            AvaloniaProperty.Register<ArtFill, AlignmentY>(nameof(AlignmentY), AlignmentY.Center);
        public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
            AvaloniaProperty.Register<ArtFill, CornerRadius>(nameof(CornerRadius));
        /// <summary>The ImageBrush SourceRect equivalent, relative to the picture (null = all of it).</summary>
        public static readonly StyledProperty<Rect?> SourceViewboxProperty =
            AvaloniaProperty.Register<ArtFill, Rect?>(nameof(SourceViewbox));

        static ArtFill()
        {
            AffectsRender<ArtFill>(SourceProperty, StretchProperty, AlignmentXProperty, AlignmentYProperty,
                CornerRadiusProperty, SourceViewboxProperty);
            IsHitTestVisibleProperty.OverrideDefaultValue<ArtFill>(false);
        }

        public IImage? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
        public Stretch Stretch { get => GetValue(StretchProperty); set => SetValue(StretchProperty, value); }
        public AlignmentX AlignmentX { get => GetValue(AlignmentXProperty); set => SetValue(AlignmentXProperty, value); }
        public AlignmentY AlignmentY { get => GetValue(AlignmentYProperty); set => SetValue(AlignmentYProperty, value); }
        public CornerRadius CornerRadius { get => GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
        public Rect? SourceViewbox { get => GetValue(SourceViewboxProperty); set => SetValue(SourceViewboxProperty, value); }

        /// <summary>The source and destination rectangles an ImageBrush with these settings would map
        /// (pure, tests). Null when nothing is drawn.</summary>
        internal static (Rect Src, Rect Dest)? Map(Size image, Rect? viewbox, Size box, Stretch stretch, AlignmentX ax, AlignmentY ay)
        {
            if (image.Width <= 0 || image.Height <= 0 || box.Width <= 0 || box.Height <= 0) return null;
            var src = viewbox is Rect v
                ? new Rect(v.X * image.Width, v.Y * image.Height, v.Width * image.Width, v.Height * image.Height)
                : new Rect(image);
            if (src.Width <= 0 || src.Height <= 0) return null;
            double fx = ax switch { AlignmentX.Left => 0, AlignmentX.Right => 1, _ => .5 };
            double fy = ay switch { AlignmentY.Top => 0, AlignmentY.Bottom => 1, _ => .5 };
            var dest = new Rect(box);
            switch (stretch)
            {
                case Stretch.UniformToFill:
                {
                    double s = Math.Max(box.Width / src.Width, box.Height / src.Height);
                    double w = box.Width / s, h = box.Height / s;
                    src = new Rect(src.X + (src.Width - w) * fx, src.Y + (src.Height - h) * fy, w, h);
                    break;
                }
                case Stretch.Uniform:
                {
                    double s = Math.Min(box.Width / src.Width, box.Height / src.Height);
                    double w = src.Width * s, h = src.Height * s;
                    dest = new Rect((box.Width - w) * fx, (box.Height - h) * fy, w, h);
                    break;
                }
                case Stretch.None:
                {
                    double w = Math.Min(src.Width, box.Width), h = Math.Min(src.Height, box.Height);
                    src = new Rect(src.X + (src.Width - w) * fx, src.Y + (src.Height - h) * fy, w, h);
                    dest = new Rect((box.Width - w) * fx, (box.Height - h) * fy, w, h);
                    break;
                }
            }
            return (src, dest);
        }

        /// <summary>Paints <paramref name="host"/> with <paramref name="src"/> the way
        /// <c>host.Background = new ImageBrush(src) { ... }</c> did, as an ArtFill child that takes the
        /// host's corner radius. A source that is not an <see cref="IImage"/> keeps the brush.</summary>
        public static void Paint(Border host, IImageBrushSource? src, Stretch stretch,
            AlignmentY alignY = AlignmentY.Center, AlignmentX alignX = AlignmentX.Center)
        {
            if (src != null && src is not IImage)
            {
                host.Child = null;
                host.Background = new ImageBrush(src) { Stretch = stretch, AlignmentX = alignX, AlignmentY = alignY };
                return;
            }
            host.Background = null;
            if (src == null) { if (host.Child is ArtFill old) old.Source = null; return; }
            if (host.Child is not ArtFill fill)
            {
                fill = new ArtFill();
                RenderOptions.SetBitmapInterpolationMode(fill, RenderOptions.GetBitmapInterpolationMode(host));
                host.Child = fill;
            }
            fill.Stretch = stretch;
            fill.AlignmentX = alignX;
            fill.AlignmentY = alignY;
            fill.CornerRadius = host.CornerRadius;
            fill.Source = (IImage)src;
        }

        public override void Render(DrawingContext context)
        {
            var img = Source;
            if (img == null) return;
            var box = new Rect(Bounds.Size);
            if (Map(img.Size, SourceViewbox, box.Size, Stretch, AlignmentX, AlignmentY) is not { } m) return;
            var r = CornerRadius;
            if (r == default)
            {
                using (context.PushClip(box)) context.DrawImage(img, m.Src, m.Dest);
                return;
            }
            using (context.PushClip(new RoundedRect(box, r))) context.DrawImage(img, m.Src, m.Dest);
        }
    }
}
