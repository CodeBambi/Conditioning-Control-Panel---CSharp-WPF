using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ConditioningControlPanel.Services.Possession.Effects;

/// <summary>
/// R3 "drop" - the title starts losing letters. One glyph at a time comes loose, falls with gravity,
/// bounces once and lies on the rubble floor at the bottom of the window; the word it left keeps its
/// spacing (the letter's slot is still there, just transparent), so the gap is unmistakable.
/// Up to 40% of the letters, never the first one, never a space, never bound text, never the timer.
/// Undo flies every glyph back into its slot and restores the exact original string.
/// </summary>
public sealed class DropEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] _roles =
        { PossessionRole.TabHeader, PossessionRole.Title, PossessionRole.Label };

    private sealed class Glyph
    {
        public Image Image = null!;
        public TranslateTransform Tr = new();
        public RotateTransform Rot = new();
        public Point Home;
        public int Index;
        public bool InRubble;
    }

    private TextBlock? _tb;
    private string? _originalText;
    private readonly HashSet<int> _dropped = new();
    private readonly List<Glyph> _glyphs = new();
    private RenderTargetBitmap? _snapshot;
    private DpiScale _dpi;
    private Point _tbOrigin;     // layer space
    private double _startX;      // design units, inside the TextBlock
    private double _sx = 1;      // layer units per design unit
    private double _sy = 1;
    private int _maxDrops;

    public override string Id => "drop";
    public override PossessionRung MinRung => PossessionRung.Collapse;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
    public override bool IsBig => true;
    public override double Weight => 4;
    /// <summary>Zero = it stays broken until the lockdown ends and reassembly puts it back.</summary>
    public override TimeSpan HoldFor => TimeSpan.Zero;
    public override IReadOnlyList<PossessionRole> Roles => _roles;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        if (target?.Role == PossessionRole.Timer) return false;
        var tb = PossessionVisual.FindTextBlock(target?.Element);
        if (!PossessionVisual.IsRewritable(tb, 4)) return false;
        var text = tb!.Text;
        if (text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0) return false;
        if (CountEligible(text) < 1) return false;
        return TryMeasureLine(tb, out _, out _);
    }

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        _tb = PossessionVisual.FindTextBlock(target?.Element);
        if (!PossessionVisual.IsRewritable(_tb, 4)) return Task.CompletedTask;

        _originalText = _tb!.Text;
        if (!TryMeasureLine(_tb, out _startX, out _)) { _originalText = null; return Task.CompletedTask; }

        // The title lives inside the design Viewbox; the glyphs will live in the ghost layer OUTSIDE
        // it. Everything measured off the TextBlock (FormattedText widths, its own height) is design
        // space and gets scaled on the way out, and the snapshot is rendered at that same scale so the
        // falling letters are crisp instead of resampled.
        var bounds = PossessionVisual.BoundsOf(ctx.Host, _tb);
        if (bounds.IsEmpty) { _originalText = null; return Task.CompletedTask; }
        var scale = PossessionVisual.ScaleOf(ctx.Host, _tb);
        _sx = scale.X;
        _sy = scale.Y;

        _dpi = VisualTreeHelper.GetDpi(_tb);
        _snapshot = Ghost.Snapshot(_tb, _dpi, _sx, _sy);
        if (_snapshot == null) { _originalText = null; return Task.CompletedTask; }

        _tbOrigin = new Point(bounds.X, bounds.Y);
        _maxDrops = Math.Max(1, (int)(CountEligible(_originalText) * 0.4));

        _ = DropLoopAsync(ctx, Cts?.Token ?? CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task DropLoopAsync(PossessionContext ctx, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _dropped.Count < _maxDrops)
            {
                if (!await PossAnim.DelayAsync(Rand(2000, 4000), ct).ConfigureAwait(true)) return;
                if (ct.IsCancellationRequested) return;
                await DropOneAsync(ctx, ct).ConfigureAwait(true);
            }
        }
        catch (Exception ex) { App.Logger?.Warning("Possession drop loop failed: {Error}", ex.Message); }
    }

    private async Task DropOneAsync(PossessionContext ctx, CancellationToken ct)
    {
        var tb = _tb;
        var text = _originalText;
        if (tb == null || text == null || _snapshot == null) return;

        int index = PickIndex(text);
        if (index < 0) return;

        // Where the glyph lives inside the TextBlock, in DIPs.
        double x0 = _startX + MeasureTo(tb, text, index, _dpi);
        double x1 = _startX + MeasureTo(tb, text, index + 1, _dpi);
        double w = Math.Max(2, x1 - x0);
        double h = tb.ActualHeight;
        if (h <= 0 || x0 < 0 || x0 + w > tb.ActualWidth + 1) return;

        // Crop rect is in the snapshot's DIP space, which is LAYER space.
        double cropW = Math.Min(w, tb.ActualWidth - x0) * _sx;
        var img = Ghost.CropImage(_snapshot, new Rect(x0 * _sx, 0, cropW, h * _sy), _dpi);
        if (img == null) return;

        // The letter leaves the word (its slot keeps the width, so nothing re-flows) ...
        _dropped.Add(index);
        RebuildInlines();

        // ... and the glyph becomes a falling object.
        var g = new Glyph { Image = img, Index = index, Home = new Point(_tbOrigin.X + x0 * _sx, _tbOrigin.Y) };
        var group = new TransformGroup();
        group.Children.Add(g.Rot);
        group.Children.Add(g.Tr);
        img.RenderTransformOrigin = new Point(0.5, 0.5);
        img.RenderTransform = group;
        Canvas.SetLeft(img, g.Home.X);
        Canvas.SetTop(img, g.Home.Y);

        var layer = ctx.Host.GhostLayer;
        if (layer == null) return;
        layer.Children.Add(img);
        _glyphs.Add(g);

        await FallAsync(ctx, g, h * _sy, ct).ConfigureAwait(true);
    }

    private async Task FallAsync(PossessionContext ctx, Glyph g, double glyphHeight, CancellationToken ct)
    {
        try
        {
            // The glyph is already in the ghost layer, and the rubble floor is a sibling of it, so
            // every distance below is layer space and needs no scaling.
            var floor = ctx.Host.RubbleFloor;
            var floorRect = floor != null ? PossessionVisual.BoundsOf(ctx.Host, floor) : Rect.Empty;
            Point floorPos = floorRect.IsEmpty ? new Point(0, 0) : new Point(floorRect.X, floorRect.Y);
            double floorH = floorRect.IsEmpty ? 0 : floorRect.Height;
            double layerH = ctx.Host.GhostLayer?.ActualHeight ?? ctx.Host.Window?.ActualHeight ?? 0;

            double floorTop = (floor != null && floorH > 0)
                ? floorPos.Y
                : Math.Max(g.Home.Y + 80, layerH - 48);

            double landY = floorTop + (floorH > glyphHeight ? Rand(0, floorH - glyphHeight) : 0);
            double dy = Math.Max(30, landY - g.Home.Y);
            double dx = Amp(Rand(-14, 14));
            double angle = Amp(Rand(-35, 35));
            double fallMs = Math.Clamp(560 + dy * 0.6, 560, 1100);

            PossAnim.To(g.Tr, TranslateTransform.YProperty, dy, fallMs, PossAnim.Gravity);
            PossAnim.To(g.Tr, TranslateTransform.XProperty, dx, fallMs, PossAnim.EaseOut);
            PossAnim.To(g.Rot, RotateTransform.AngleProperty, angle, fallMs, PossAnim.EaseOut);
            if (!await PossAnim.DelayAsync(fallMs + 20, ct).ConfigureAwait(true)) return;

            if (!Photosafe)
            {
                PossAnim.To(g.Tr, TranslateTransform.YProperty, dy - 10, 160, PossAnim.EaseOut);
                if (!await PossAnim.DelayAsync(170, ct).ConfigureAwait(true)) return;
                PossAnim.To(g.Tr, TranslateTransform.YProperty, dy, 200, PossAnim.Gravity);
                if (!await PossAnim.DelayAsync(210, ct).ConfigureAwait(true)) return;
            }

            // Park it: the animations are settled into plain values and the glyph is handed to the
            // rubble canvas at the same absolute spot, where it STAYS.
            PossAnim.Settle(g.Tr, TranslateTransform.XProperty, dx);
            PossAnim.Settle(g.Tr, TranslateTransform.YProperty, dy);
            PossAnim.Settle(g.Rot, RotateTransform.AngleProperty, angle);

            if (floor != null && floorH > 0)
            {
                var layer = ctx.Host.GhostLayer;
                try { layer?.Children.Remove(g.Image); } catch { }
                Canvas.SetLeft(g.Image, g.Home.X - floorPos.X);
                Canvas.SetTop(g.Image, g.Home.Y - floorPos.Y);
                floor.Children.Add(g.Image);
                g.InRubble = true;
            }
        }
        catch (Exception ex) { App.Logger?.Warning("Possession drop fall failed: {Error}", ex.Message); }
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        double ms = UndoMs(duration, 400, 900);
        if (ms > 0) ms = Math.Min(ms, 600);

        try
        {
            var layer = Ctx?.Host.GhostLayer;
            var floor = Ctx?.Host.RubbleFloor;

            foreach (var g in _glyphs)
            {
                // Back into the ghost layer first, so a clipped rubble canvas cannot eat the flight.
                if (g.InRubble && layer != null)
                {
                    try { floor?.Children.Remove(g.Image); } catch { }
                    Canvas.SetLeft(g.Image, g.Home.X);
                    Canvas.SetTop(g.Image, g.Home.Y);
                    try { layer.Children.Add(g.Image); } catch { }
                    g.InRubble = false;
                }
                if (ms <= 0) continue;
                PossAnim.To(g.Tr, TranslateTransform.XProperty, 0, ms, PossAnim.EaseInOut);
                PossAnim.To(g.Tr, TranslateTransform.YProperty, 0, ms, PossAnim.EaseInOut);
                PossAnim.To(g.Rot, RotateTransform.AngleProperty, 0, ms, PossAnim.EaseInOut);
            }

            if (ms > 0 && _glyphs.Count > 0)
                await PossAnim.DelayAsync(ms + 30, CancellationToken.None).ConfigureAwait(true);

            foreach (var g in _glyphs)
            {
                try { layer?.Children.Remove(g.Image); } catch { }
                try { floor?.Children.Remove(g.Image); } catch { }
            }
        }
        catch (Exception ex) { App.Logger?.Warning("Possession drop undo failed: {Error}", ex.Message); }

        try
        {
            if (_tb != null && _originalText != null) _tb.Text = _originalText;   // exact string, one Run
        }
        catch (Exception ex) { App.Logger?.Warning("Possession drop text restore failed: {Error}", ex.Message); }

        _glyphs.Clear();
        _dropped.Clear();
        _snapshot = null;
        _originalText = null;
        _tb = null;
    }

    // ---- text plumbing -------------------------------------------------------------------------

    /// <summary>Rebuild the TextBlock out of Runs where the dropped characters keep their width but
    /// paint nothing. Same font, same size, same weight: only the ink is gone.</summary>
    private void RebuildInlines()
    {
        var tb = _tb;
        var text = _originalText;
        if (tb == null || text == null) return;
        try
        {
            tb.Inlines.Clear();
            int i = 0;
            while (i < text.Length)
            {
                bool gone = _dropped.Contains(i);
                int j = i;
                while (j < text.Length && _dropped.Contains(j) == gone) j++;
                var run = new Run(text.Substring(i, j - i));
                if (gone) run.Foreground = Brushes.Transparent;
                tb.Inlines.Add(run);
                i = j;
            }
        }
        catch (Exception ex) { App.Logger?.Warning("Possession drop rebuild failed: {Error}", ex.Message); }
    }

    private int PickIndex(string text)
    {
        var options = new List<int>();
        for (int i = 1; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i])) continue;
            if (_dropped.Contains(i)) continue;
            options.Add(i);
        }
        return options.Count == 0 ? -1 : options[Rng.Next(options.Count)];
    }

    private static int CountEligible(string text)
    {
        int n = 0;
        for (int i = 1; i < text.Length; i++) if (!char.IsWhiteSpace(text[i])) n++;
        return n;
    }

    /// <summary>Width of the first <paramref name="count"/> characters as the TextBlock draws them.</summary>
    private static double MeasureTo(TextBlock tb, string text, int count, DpiScale dpi)
    {
        if (count <= 0) return 0;
        count = Math.Min(count, text.Length);
        try
        {
            var ft = new FormattedText(
                text.Substring(0, count),
                CultureInfo.CurrentUICulture,
                tb.FlowDirection,
                new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch),
                tb.FontSize,
                Brushes.Black,
                dpi.PixelsPerDip);
            return ft.WidthIncludingTrailingWhitespace;
        }
        catch { return 0; }
    }

    /// <summary>Where the single line of text starts inside the TextBlock, and whether it fits at all
    /// (wrapped or clipped text would put our glyph crops in the wrong place, so we decline it).</summary>
    private static bool TryMeasureLine(TextBlock tb, out double startX, out double total)
    {
        startX = 0;
        total = 0;
        try
        {
            var dpi = VisualTreeHelper.GetDpi(tb);
            var text = tb.Text;
            total = MeasureTo(tb, text, text.Length, dpi);
            if (total <= 0) return false;

            double inner = tb.ActualWidth - tb.Padding.Left - tb.Padding.Right;
            if (inner <= 0 || total > inner + 1.5) return false;

            startX = tb.Padding.Left;
            if (tb.TextAlignment == TextAlignment.Center) startX += Math.Max(0, (inner - total) / 2);
            else if (tb.TextAlignment == TextAlignment.Right) startX += Math.Max(0, inner - total);
            return true;
        }
        catch { return false; }
    }
}
