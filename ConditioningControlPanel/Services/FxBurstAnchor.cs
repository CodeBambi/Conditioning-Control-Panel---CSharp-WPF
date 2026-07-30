using System;
using System.Windows;

namespace ConditioningControlPanel.Services
{
    /// <summary>Where on an anchor's bounds an event burst should originate.</summary>
    public enum FxBurstSpot
    {
        /// <summary>Dead centre. The default for cards, tiles and toasts.</summary>
        Center,

        /// <summary>The middle of the right edge - a progress bar's cap.</summary>
        RightEdge,

        /// <summary>The middle of the left edge.</summary>
        LeftEdge,

        /// <summary>Centre of the top edge - a banner that is about to be read.</summary>
        TopCenter,

        /// <summary>Centre of the bottom edge.</summary>
        BottomCenter,
    }

    /// <summary>
    /// The pure half of the PR-5 event-burst plumbing: given an anchor's bounds already mapped
    /// into the burst layer's coordinate space, decide whether a burst should fire at all and
    /// where its origin sits.
    ///
    /// This is deliberately separate from the WPF call that produces those bounds
    /// (<c>anchor.TransformToVisual(layer)</c>, which is the only correct way to get them - the
    /// whole main-window UI lives inside a Viewbox, so raw layout arithmetic would be off by the
    /// window's scale factor). Keeping the decision pure means the part that can silently go
    /// wrong - firing a burst for an anchor that is scrolled out of view, or emitting outside the
    /// canvas where nothing would ever be drawn - is testable without a window.
    /// </summary>
    public static class FxBurstAnchor
    {
        /// <summary>
        /// How far outside the layer an anchor may sit and still count as on-screen. A bar whose
        /// cap is a couple of pixels past the edge is still worth celebrating; one scrolled fully
        /// out of the viewport is not.
        /// </summary>
        private const double EdgeSlackPx = 2.0;

        /// <summary>
        /// Resolves the burst origin for <paramref name="anchorInLayer"/> (the anchor's bounds in
        /// burst-layer coordinates) against a layer of <paramref name="layerSize"/>.
        /// Returns false - and emits nothing - when the layer has no usable surface, the anchor
        /// has collapsed to nothing, or the anchor does not overlap the layer at all.
        /// The returned point is always inside the layer, so a partially clipped anchor still
        /// bursts somewhere the user can see.
        /// </summary>
        public static bool TryResolve(Rect anchorInLayer, Size layerSize, FxBurstSpot spot, out Point origin)
        {
            origin = default;

            if (double.IsNaN(layerSize.Width) || double.IsNaN(layerSize.Height)) return false;
            if (layerSize.Width <= 1 || layerSize.Height <= 1) return false;

            if (anchorInLayer.IsEmpty) return false;
            if (double.IsNaN(anchorInLayer.X) || double.IsNaN(anchorInLayer.Y) ||
                double.IsNaN(anchorInLayer.Width) || double.IsNaN(anchorInLayer.Height)) return false;
            if (double.IsInfinity(anchorInLayer.X) || double.IsInfinity(anchorInLayer.Y) ||
                double.IsInfinity(anchorInLayer.Width) || double.IsInfinity(anchorInLayer.Height)) return false;
            // A zero-size anchor is a control that has never been measured (a collapsed tab, a
            // tile that has not been laid out): there is nothing on screen to burst from.
            if (anchorInLayer.Width <= 0 || anchorInLayer.Height <= 0) return false;

            // Off-screen anchors (scrolled out of a list, on a hidden tab that still has a
            // transform) get nothing. The slack keeps a bar cap sitting a hair past the edge.
            if (anchorInLayer.Right < EdgeSlackPx || anchorInLayer.Bottom < EdgeSlackPx) return false;
            if (anchorInLayer.Left > layerSize.Width - EdgeSlackPx) return false;
            if (anchorInLayer.Top > layerSize.Height - EdgeSlackPx) return false;

            double x = spot switch
            {
                FxBurstSpot.RightEdge => anchorInLayer.Right,
                FxBurstSpot.LeftEdge => anchorInLayer.Left,
                _ => anchorInLayer.Left + anchorInLayer.Width / 2.0,
            };
            double y = spot switch
            {
                FxBurstSpot.TopCenter => anchorInLayer.Top,
                FxBurstSpot.BottomCenter => anchorInLayer.Bottom,
                _ => anchorInLayer.Top + anchorInLayer.Height / 2.0,
            };

            origin = new Point(Clamp(x, layerSize.Width), Clamp(y, layerSize.Height));
            return true;

            static double Clamp(double v, double max) => Math.Max(0, Math.Min(max, v));
        }
    }
}
