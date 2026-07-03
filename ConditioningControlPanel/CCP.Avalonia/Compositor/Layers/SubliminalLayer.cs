using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Core.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// Renders subliminal text flashes on the compositor. Supports multiple concurrent flashes
/// (queued), configurable colors, background opacity, target opacity, and duration.
///
/// Opacity contract (WPF parity, SubliminalService.cs:583 + AnimateSubliminal :928-940):
/// the caller passes targetOpacity = (override ?? settings.SubliminalOpacity)/100; the whole
/// card (background AND text) is scaled by it, with a 50ms fade-in and 50ms fade-out envelope
/// around the hold duration, exactly like WPF's whole-window Opacity storyboard.
/// </summary>
public sealed class SubliminalLayer : BaseLayer
{
    /// <summary>WPF AnimateSubliminal fade-in/out duration (50ms each side of the hold).</summary>
    private const double FadeMs = 50;
    /// <summary>Background alpha at full opacity (pre-existing port constant).</summary>
    private const byte BgBaseAlpha = 180;

    private readonly List<SubliminalItem> _items = new();
    private readonly object _sync = new();
    // Reused paints (UCE rule: no per-frame allocations). Only touched inside Render under _sync.
    private readonly SKPaint _bgPaint = new();
    private readonly SKPaint _textPaint = new()
    {
        TextSize = 120,
        IsAntialias = true,
        TextAlign = SKTextAlign.Center
    };

    public override int ZIndex => CompositorLayers.Subliminal;

    public override bool IsActive
    {
        get
        {
            lock (_sync) { return _items.Count > 0; }
        }
    }

    /// <summary>
    /// Queue a new subliminal flash. Multiple flashes can be active concurrently.
    /// <paramref name="durationMs"/> is the hold time; the 50ms fades are added on top
    /// (WPF parity: fade-in + hold + fade-out storyboard). <paramref name="targetOpacity"/>
    /// scales the whole card, 0..1.
    /// </summary>
    public void Flash(string text, Color bgColor, Color textColor, int durationMs, bool bgTransparent, double targetOpacity = 1.0)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (_sync)
        {
            _items.Add(new SubliminalItem(text, bgColor, textColor, durationMs, bgTransparent, Math.Clamp(targetOpacity, 0.0, 1.0)));
        }
    }

    public void Clear()
    {
        lock (_sync) { _items.Clear(); }
    }

    public override void Update(TimeSpan deltaTime)
    {
        lock (_sync)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                _items[i].Remaining -= deltaTime;
                if (_items[i].Remaining <= TimeSpan.Zero)
                    _items.RemoveAt(i);
            }
        }
    }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
    {
        // Render under _sync (like SpiralLayer): no snapshot list, no per-frame allocations.
        lock (_sync)
        {
            if (_items.Count == 0) return;

            // Draw most recent on top (last in list)
            var item = _items[^1];
            var alphaScale = item.TargetOpacity * item.Envelope();
            if (alphaScale <= 0) return;

            if (!item.BgTransparent)
            {
                _bgPaint.Color = new SKColor(item.BgColor.R, item.BgColor.G, item.BgColor.B,
                    (byte)(BgBaseAlpha * alphaScale));
                canvas.DrawRect(ToSkRect(bounds), _bgPaint);
            }

            _textPaint.Color = new SKColor(item.TextColor.R, item.TextColor.G, item.TextColor.B,
                (byte)(255 * alphaScale));

            var centerX = bounds.X + bounds.Width / 2.0f;
            var centerY = bounds.Y + bounds.Height / 2.0f;
            canvas.DrawText(item.Text, (float)centerX, (float)centerY + _textPaint.TextSize / 3, _textPaint);
        }
    }

    private sealed class SubliminalItem
    {
        public string Text { get; }
        public Color BgColor { get; }
        public Color TextColor { get; }
        public bool BgTransparent { get; }
        public double TargetOpacity { get; }
        public TimeSpan Total { get; }
        public TimeSpan Remaining { get; set; }

        public SubliminalItem(string text, Color bgColor, Color textColor, int durationMs, bool bgTransparent, double targetOpacity)
        {
            Text = text;
            BgColor = bgColor;
            TextColor = textColor;
            BgTransparent = bgTransparent;
            TargetOpacity = targetOpacity;
            // WPF parity: total visible time = 50ms fade-in + hold + 50ms fade-out.
            Total = TimeSpan.FromMilliseconds(durationMs + 2 * FadeMs);
            Remaining = Total;
        }

        /// <summary>0..1 fade envelope: ramp up over the first 50ms, hold, ramp down over the last 50ms.</summary>
        public double Envelope()
        {
            var elapsedMs = (Total - Remaining).TotalMilliseconds;
            var remainingMs = Remaining.TotalMilliseconds;
            if (elapsedMs < FadeMs) return Math.Clamp(elapsedMs / FadeMs, 0.0, 1.0);
            if (remainingMs < FadeMs) return Math.Clamp(remainingMs / FadeMs, 0.0, 1.0);
            return 1.0;
        }
    }
}
