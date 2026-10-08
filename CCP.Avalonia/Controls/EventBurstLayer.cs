using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// WPF MainWindow.EventFx.cs FireBurstAt: a fixed 560 px <see cref="AmbientFxCanvas"/> moved so its centre sits
    /// on the anchor, on the window's overlay layer (as WPF's EventFxHost spans the window). Refused like WPF
    /// EventFxAllowed (EventFx.cs:118-126): particles allowed, anchor shown, window visible, not minimised and active.
    /// One per surface that celebrates (achievement tiles, quest bars).
    /// </summary>
    internal sealed class EventBurstLayer
    {
        private const double Box = 560;
        private AmbientFxCanvas? _layer;

        /// <summary>Bursts fired (test hook).</summary>
        internal int Count { get; private set; }

        /// <summary>Bursts <paramref name="count"/> sparks on <paramref name="anchor"/>'s centre, or on its right
        /// edge (FxBurstSpot.RightEdge: the cap of a bar that just filled). False when refused.</summary>
        internal bool Fire(Control anchor, int count, bool rightEdge = false)
        {
            if (!Env.AllowParticles || !anchor.IsEffectivelyVisible || anchor.Bounds.Width <= 0) return false;
            if (TopLevel.GetTopLevel(anchor) is not Window { IsVisible: true, IsActive: true } w || w.WindowState == WindowState.Minimized) return false;
            if (OverlayLayer.GetOverlayLayer(anchor) is not { } host) return false;
            var spot = new Point(rightEdge ? anchor.Bounds.Width : anchor.Bounds.Width / 2, anchor.Bounds.Height / 2);
            if (anchor.TranslatePoint(spot, host) is not { } at) return false;
            if (_layer == null || _layer.Parent != host)
            {
                (_layer?.Parent as Panel)?.Children.Remove(_layer!);
                _layer ??= new AmbientFxCanvas { Width = Box, Height = Box, IsHitTestVisible = false };
                host.Children.Add(_layer);
            }
            Canvas.SetLeft(_layer, at.X - Box / 2);
            Canvas.SetTop(_layer, at.Y - Box / 2);
            _layer.UpdateLayout();
            _layer.Burst(Box / 2, Box / 2, Env.GlowColor, count);
            Count++;
            return true;
        }
    }
}
