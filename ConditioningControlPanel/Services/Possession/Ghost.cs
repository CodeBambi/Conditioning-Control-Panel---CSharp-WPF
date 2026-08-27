using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ConditioningControlPanel.Services.Possession;

// =====================================================================================================
//  POSSESSION - Ghost. Read Services/Possession/POSSESSION.md first.
//
//  Snapshot-and-puppet. A ghost takes a RenderTargetBitmap of a real control at the window's DPI, drops
//  an Image of it into the host's GhostLayer at exactly the control's position and size, and then hides
//  the real control (Opacity 0 - NOT Visibility, NOT IsHitTestVisible: an invisible control still
//  works, which is the whole point: the toggle you just watched crumble to ash still toggles).
//
//  LAYER SPACE vs DESIGN SPACE. MainWindow's content lives inside a Viewbox around a fixed-size design
//  canvas, while GhostLayer / RubbleFloor are siblings OUTSIDE that Viewbox. A victim's ActualWidth is
//  therefore in DESIGN units and can differ per axis from what the user actually sees. Everything a
//  ghost parks in the layer is measured in LAYER units through TransformToVisual(GhostLayer) (see
//  LayerBoundsOf / LayerScaleOf), and the snapshot is rendered at that scale so the ash is crisp at the
//  size it is actually drawn.
//
//  ExplodeIntoTiles() cuts that snapshot into a grid of CroppedBitmap tiles, each an Image parked over
//  its own slice of the original, ready to be scattered / dropped / re-formed.
//
//  Dispose ALWAYS restores: the real control's original Opacity value (whatever it was, not 1.0) comes
//  back and every borrowed Image leaves the layer.
// =====================================================================================================

public sealed class Ghost : IDisposable
{
    private readonly IPossessionHost _host;
    private readonly FrameworkElement _target;
    private readonly List<Image> _tiles = new();
    private readonly List<UIElement> _extras = new();

    // What Hide() found before it wrote anything. "Was there a local value at all" is half of the
    // answer: a control whose Opacity or IsHitTestVisible comes from a Style setter, a trigger or a
    // storyboard has NO local value, and stamping one on the way back pins it forever - the hover
    // highlight never fires again, the disabled state never greys out. See FallEffect and
    // Ghost.NeutralTransform for the same dance.
    private bool _opacityWasLocal;
    private double _opacityValue = 1;
    private bool _hitTestChanged;
    private bool _hitTestWasLocal;
    private bool _hitTestValue = true;

    private bool _hidden;
    private bool _disposed;

    /// <summary>The full-size snapshot Image sitting in the GhostLayer (null once exploded).</summary>
    public Image? Visual { get; private set; }

    /// <summary>The raw snapshot, kept so tiles / glyph crops can be cut from it later. Its DIP space is
    /// LAYER space (it was rendered at the victim's on-screen scale).</summary>
    public RenderTargetBitmap? Bitmap { get; }

    /// <summary>Top-left of the ghost in GhostLayer coordinates at capture time.</summary>
    public Point Origin { get; }

    /// <summary>On-screen size in GhostLayer units (design size times the Viewbox scale).</summary>
    public Size SizeDip { get; }

    /// <summary>The victim's design-to-layer scale at capture time.</summary>
    public double ScaleX { get; }
    public double ScaleY { get; }

    public DpiScale Dpi { get; }
    public FrameworkElement Target => _target;
    public IReadOnlyList<Image> Tiles => _tiles;

    private Ghost(IPossessionHost host, FrameworkElement target, RenderTargetBitmap? bmp, Point origin,
                  Size size, DpiScale dpi, double scaleX, double scaleY)
    {
        _host = host;
        _target = target;
        Bitmap = bmp;
        Origin = origin;
        SizeDip = size;
        Dpi = dpi;
        ScaleX = scaleX;
        ScaleY = scaleY;
    }

    // ---- layer geometry ----------------------------------------------------------------------------

    /// <summary>
    /// The element's rectangle in GhostLayer coordinates. Goes through TransformToVisual (the layer is a Viewbox SIBLING, not an ancestor) so the
    /// Viewbox (and any other transform between the two) is accounted for; ActualWidth on its own is a
    /// design-space lie once the window is not at its design size.
    /// </summary>
    public static Rect LayerBoundsOf(IPossessionHost host, FrameworkElement el)
    {
        try
        {
            var layer = host?.GhostLayer;
            if (layer == null || el == null) return Rect.Empty;
            if (!el.IsVisible) return Rect.Empty;
            var gt = el.TransformToVisual(layer);
            return gt.TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight));
        }
        catch { return Rect.Empty; }
    }

    /// <summary>
    /// How many layer units one design unit of this element covers, per axis. Anything measured in
    /// layer pixels (a cursor distance, a window edge, a swap delta) must be divided by these before it
    /// is handed to the element's own TransformLease, which lives in design space.
    /// </summary>
    public static (double X, double Y) LayerScaleOf(IPossessionHost host, FrameworkElement el)
    {
        try
        {
            var layer = host?.GhostLayer;
            if (layer == null || el == null) return (1, 1);
            var gt = el.TransformToVisual(layer);
            var o = gt.Transform(new Point(0, 0));
            var ux = gt.Transform(new Point(1, 0));
            var uy = gt.Transform(new Point(0, 1));
            double sx = Length(ux.X - o.X, ux.Y - o.Y);
            double sy = Length(uy.X - o.X, uy.Y - o.Y);
            if (double.IsNaN(sx) || double.IsInfinity(sx) || sx <= 0.0001) sx = 1;
            if (double.IsNaN(sy) || double.IsInfinity(sy) || sy <= 0.0001) sy = 1;
            return (sx, sy);
        }
        catch { return (1, 1); }
    }

    private static double Length(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);

    // ---- capture -----------------------------------------------------------------------------------

    /// <summary>
    /// Snapshot <paramref name="target"/> and park an Image of it in host.GhostLayer at the target's
    /// exact on-screen position and size. Returns null when the element cannot be rendered (zero size,
    /// no host layer, not in a visual tree).
    /// </summary>
    public static Ghost? Capture(FrameworkElement target, IPossessionHost host)
    {
        if (target == null || host == null) return null;
        try
        {
            double dw = target.ActualWidth, dh = target.ActualHeight;
            if (dw <= 0 || dh <= 0) return null;
            var layer = host.GhostLayer;
            if (layer == null) return null;

            var dpi = VisualTreeHelper.GetDpi(target);
            Rect bounds;
            double sx, sy;
            RenderTargetBitmap? bmp;

            // Bounds, scale and pixels are all measured with the victim's own RenderTransform
            // neutralised, so a control that is already leaning somewhere still photographs square and
            // still lands back in the seat it actually occupies.
            using (var neutral = new NeutralTransform(target))
            {
                bounds = LayerBoundsOf(host, target);
                var scale = LayerScaleOf(host, target);
                sx = scale.X;
                sy = scale.Y;
                if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return null;
                bmp = RenderScaled(target, dpi, sx, sy);
            }
            if (bmp == null) return null;

            var ghost = new Ghost(host, target, bmp, new Point(bounds.X, bounds.Y),
                                  new Size(bounds.Width, bounds.Height), dpi, sx, sy);

            var img = new Image
            {
                Source = bmp,
                Width = bounds.Width,
                Height = bounds.Height,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false,
                SnapsToDevicePixels = true
            };
            Canvas.SetLeft(img, bounds.X);
            Canvas.SetTop(img, bounds.Y);
            layer.Children.Add(img);
            ghost.Visual = img;
            return ghost;
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Ghost.Capture failed: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Render an element to a bitmap at the window's DPI and at its design-to-layer scale, ignoring its
    /// own RenderTransform. The result's DIP space is LAYER space: one bitmap DIP is one GhostLayer unit.
    /// </summary>
    public static RenderTargetBitmap? Snapshot(FrameworkElement target, DpiScale? dpiOverride = null,
                                               double scaleX = 1, double scaleY = 1)
    {
        try
        {
            if (target == null || target.ActualWidth <= 0 || target.ActualHeight <= 0) return null;
            var dpi = dpiOverride ?? VisualTreeHelper.GetDpi(target);
            using var neutral = new NeutralTransform(target);
            return RenderScaled(target, dpi, scaleX, scaleY);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Ghost.Snapshot failed: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>Caller MUST already hold a NeutralTransform on the target.</summary>
    private static RenderTargetBitmap? RenderScaled(FrameworkElement target, DpiScale dpi, double sx, double sy)
    {
        try
        {
            double dw = target.ActualWidth, dh = target.ActualHeight;
            if (dw <= 0 || dh <= 0) return null;
            if (sx <= 0.0001) sx = 1;
            if (sy <= 0.0001) sy = 1;

            int pw = Math.Max(1, (int)Math.Ceiling(dw * sx * dpi.DpiScaleX));
            int ph = Math.Max(1, (int)Math.Ceiling(dh * sy * dpi.DpiScaleY));
            if (pw > 8192 || ph > 8192) return null;   // absurd target, not worth the memory

            var rtb = new RenderTargetBitmap(pw, ph, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);

            // RenderTargetBitmap applies the visual's own RenderTransform, so scaling the element from
            // its top-left corner for the length of the render is what makes the bitmap crisp at the
            // size it will actually be drawn.
            bool scaled = false;
            try
            {
                if (Math.Abs(sx - 1) > 0.001 || Math.Abs(sy - 1) > 0.001)
                {
                    target.RenderTransform = new ScaleTransform(sx, sy);
                    scaled = true;
                }
                rtb.Render(target);
            }
            finally
            {
                if (scaled) { try { target.RenderTransform = Transform.Identity; } catch { } }
            }

            rtb.Freeze();
            return rtb;
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Ghost.RenderScaled failed: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>Cut a rectangle out of a snapshot as a standalone Image. The rect is in the bitmap's own
    /// DIP space, which for our snapshots is LAYER space.</summary>
    public static Image? CropImage(BitmapSource src, Rect dipRect, DpiScale dpi)
    {
        try
        {
            if (src == null || dipRect.Width <= 0 || dipRect.Height <= 0) return null;
            int x = (int)Math.Floor(dipRect.X * dpi.DpiScaleX);
            int y = (int)Math.Floor(dipRect.Y * dpi.DpiScaleY);
            int w = (int)Math.Ceiling(dipRect.Width * dpi.DpiScaleX);
            int h = (int)Math.Ceiling(dipRect.Height * dpi.DpiScaleY);

            x = Math.Clamp(x, 0, Math.Max(0, src.PixelWidth - 1));
            y = Math.Clamp(y, 0, Math.Max(0, src.PixelHeight - 1));
            w = Math.Clamp(w, 1, src.PixelWidth - x);
            h = Math.Clamp(h, 1, src.PixelHeight - y);

            var cropped = new CroppedBitmap(src, new Int32Rect(x, y, w, h));
            cropped.Freeze();
            return new Image
            {
                Source = cropped,
                Width = dipRect.Width,
                Height = dipRect.Height,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false,
                SnapsToDevicePixels = true
            };
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Ghost.CropImage failed: {Error}", ex.Message);
            return null;
        }
    }

    // ---- puppetry ----------------------------------------------------------------------------------

    /// <summary>Hide the real control behind its ghost. Opacity only: it stays hit-testable, so the
    /// user can still click the thing they are watching fall apart.</summary>
    public void Hide(bool alsoDisableHitTesting = false)
    {
        if (_disposed || _hidden) return;
        try
        {
            _opacityWasLocal = _target.ReadLocalValue(UIElement.OpacityProperty) != DependencyProperty.UnsetValue;
            _opacityValue = _target.Opacity;
            _target.Opacity = 0;

            if (alsoDisableHitTesting)
            {
                _hitTestWasLocal = _target.ReadLocalValue(UIElement.IsHitTestVisibleProperty) != DependencyProperty.UnsetValue;
                _hitTestValue = _target.IsHitTestVisible;
                _target.IsHitTestVisible = false;
                _hitTestChanged = true;
            }
            _hidden = true;
        }
        catch (Exception ex) { App.Logger?.Warning("Ghost.Hide failed: {Error}", ex.Message); }
    }

    /// <summary>
    /// Put the real control's ORIGINAL opacity back and drop every borrowed visual. Original means
    /// what it had, INCLUDING "it had no local value of its own": Hide's write is cleared rather than
    /// overwritten in that case, so a control whose opacity or hit-testing comes from a style, a
    /// trigger or a running storyboard is handed back still under their control. Hit-testing is only
    /// touched when Hide actually turned it off.
    /// </summary>
    public void Restore()
    {
        try
        {
            if (_hidden)
            {
                if (_opacityWasLocal) _target.Opacity = _opacityValue;
                else _target.ClearValue(UIElement.OpacityProperty);

                if (_hitTestChanged)
                {
                    if (_hitTestWasLocal) _target.IsHitTestVisible = _hitTestValue;
                    else _target.ClearValue(UIElement.IsHitTestVisibleProperty);
                    _hitTestChanged = false;
                }
                _hidden = false;
            }
        }
        catch (Exception ex) { App.Logger?.Warning("Ghost.Restore failed: {Error}", ex.Message); }

        RemoveVisuals();
    }

    /// <summary>
    /// Break the snapshot into cols x rows Image tiles, each parked exactly over its own slice of the
    /// original (so frame zero of the crumble is pixel-identical to the control). Sizes are LAYER units.
    /// The full-size ghost Image is removed. Returns an empty list when there is nothing to cut.
    /// </summary>
    public IReadOnlyList<Image> ExplodeIntoTiles(int cols, int rows)
    {
        if (_disposed || Bitmap == null) return Array.Empty<Image>();
        try
        {
            cols = Math.Clamp(cols, 1, 24);
            rows = Math.Clamp(rows, 1, 24);
            var layer = _host.GhostLayer;
            if (layer == null) return Array.Empty<Image>();

            double tw = SizeDip.Width / cols;
            double th = SizeDip.Height / rows;
            if (tw <= 0 || th <= 0) return Array.Empty<Image>();

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var rect = new Rect(c * tw, r * th, tw, th);
                    var tile = CropImage(Bitmap, rect, Dpi);
                    if (tile == null) continue;
                    Canvas.SetLeft(tile, Origin.X + rect.X);
                    Canvas.SetTop(tile, Origin.Y + rect.Y);
                    layer.Children.Add(tile);
                    _tiles.Add(tile);
                }
            }

            if (_tiles.Count > 0 && Visual != null)
            {
                try { layer.Children.Remove(Visual); } catch { }
                Visual = null;
            }
            return _tiles;
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("Ghost.ExplodeIntoTiles failed: {Error}", ex.Message);
            return _tiles;
        }
    }

    /// <summary>Park an extra visual (ember tint plate, ash, dust) in the ghost layer; Dispose sweeps it.</summary>
    public void AddExtra(UIElement el, double left, double top)
    {
        try
        {
            var layer = _host.GhostLayer;
            if (layer == null) return;
            Canvas.SetLeft(el, left);
            Canvas.SetTop(el, top);
            layer.Children.Add(el);
            _extras.Add(el);
        }
        catch { }
    }

    private void RemoveVisuals()
    {
        try
        {
            var layer = _host.GhostLayer;
            if (layer != null)
            {
                if (Visual != null) { try { layer.Children.Remove(Visual); } catch { } }
                foreach (var t in _tiles) { try { layer.Children.Remove(t); } catch { } }
                foreach (var e in _extras) { try { layer.Children.Remove(e); } catch { } }
            }
        }
        catch { }
        Visual = null;
        _tiles.Clear();
        _extras.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Restore();
    }

    /// <summary>
    /// Holds an element's RenderTransform / RenderTransformOrigin at identity for the length of a
    /// measure-and-render, then puts BOTH back exactly (including "there was no local value at all").
    /// Nothing pumps the dispatcher in between, so the user never sees the element snap.
    /// </summary>
    private readonly struct NeutralTransform : IDisposable
    {
        private readonly FrameworkElement? _el;
        private readonly Transform? _priorTransform;
        private readonly bool _priorTransformLocal;
        private readonly Point _priorOrigin;
        private readonly bool _priorOriginLocal;

        public NeutralTransform(FrameworkElement el)
        {
            _el = el;
            _priorTransform = null;
            _priorTransformLocal = false;
            _priorOrigin = default;
            _priorOriginLocal = false;
            try
            {
                _priorTransformLocal = el.ReadLocalValue(UIElement.RenderTransformProperty) != DependencyProperty.UnsetValue;
                _priorTransform = el.RenderTransform;
                _priorOriginLocal = el.ReadLocalValue(UIElement.RenderTransformOriginProperty) != DependencyProperty.UnsetValue;
                _priorOrigin = el.RenderTransformOrigin;

                el.RenderTransformOrigin = new Point(0, 0);
                el.RenderTransform = Transform.Identity;
            }
            catch { }
        }

        public void Dispose()
        {
            var el = _el;
            if (el == null) return;
            try
            {
                if (_priorTransformLocal && _priorTransform != null) el.RenderTransform = _priorTransform;
                else if (_priorTransformLocal) el.RenderTransform = Transform.Identity;
                else el.ClearValue(UIElement.RenderTransformProperty);

                if (_priorOriginLocal) el.RenderTransformOrigin = _priorOrigin;
                else el.ClearValue(UIElement.RenderTransformOriginProperty);
            }
            catch (Exception ex) { App.Logger?.Warning("Ghost transform restore failed: {Error}", ex.Message); }
        }
    }
}
