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
/// Renders bouncing text phrases that drift across the screen.
/// The service controls the text pool, positions, and velocities.
/// </summary>
public sealed class BouncingTextLayer : BaseLayer
{
    private readonly List<BouncingTextItem> _items = new();
    private readonly object _sync = new();
    // Reused paint (UCE rule: no per-frame allocations). Only touched inside Render under _sync.
    private readonly SKPaint _paint = new()
    {
        IsAntialias = true,
        TextAlign = SKTextAlign.Left
    };

    public override int ZIndex => CompositorLayers.BouncingText;

    public override bool IsActive
    {
        get
        {
            lock (_sync) { return _items.Count > 0; }
        }
    }

    /// <summary>Screen bounds for edge collision (updated by service).</summary>
    public double MinX { get; set; } = 0;
    public double MinY { get; set; } = 0;
    public double MaxX { get; set; } = 1920;
    public double MaxY { get; set; } = 1080;

    public void AddText(string text, Color color, int fontSize, double opacity)
    {
        lock (_sync)
        {
            _items.Add(new BouncingTextItem(text, color, fontSize, opacity));
        }
    }

    public void Clear()
    {
        lock (_sync) { _items.Clear(); }
    }

    /// <summary>Service-driven update: set position directly (used by service's own animation loop).</summary>
    public void UpdatePosition(int index, double x, double y)
    {
        lock (_sync)
        {
            if (index >= 0 && index < _items.Count)
            {
                _items[index].X = x;
                _items[index].Y = y;
            }
        }
    }

    public override void Update(TimeSpan deltaTime)
    {
        // Service drives positions via UpdatePosition(); layer just handles cleanup
        lock (_sync)
        {
            _items.RemoveAll(i => i.IsOffScreen);
        }
    }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
    {
        // Render under _sync with a reused paint: no snapshot list, no per-frame allocations
        // (UCE rule). The service mutates items on the UI thread under the same lock.
        lock (_sync)
        {
            foreach (var item in _items)
            {
                _paint.Color = new SKColor(item.Color.R, item.Color.G, item.Color.B, (byte)(item.Opacity * 255));
                _paint.TextSize = item.FontSize;
                canvas.DrawText(item.Text, (float)item.X, (float)item.Y, _paint);
            }
        }
    }

    private sealed class BouncingTextItem
    {
        public string Text { get; }
        public Color Color { get; }
        public int FontSize { get; }
        public double Opacity { get; }
        public double X { get; set; }
        public double Y { get; set; }
        public bool IsOffScreen { get; set; }

        public BouncingTextItem(string text, Color color, int fontSize, double opacity)
        {
            Text = text;
            Color = color;
            FontSize = fontSize;
            Opacity = opacity;
        }
    }
}
