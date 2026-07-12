using System;
using System.Collections.Generic;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Core.Services.Compositor;

/// <summary>
/// Immutable snapshot of the compositor's per-frame CAPTURE MASK: the union of the painted
/// regions of every NON-AMBIENT active layer (the 2026-07-09 per-region click-through rule:
/// only the theme color filter, the spiral, and the Blink Trainer are ambient 'tinted glass'
/// the user works through; every other active layer captures pointer input over the region
/// it paints). Built by <see cref="CaptureMaskBuilder"/> once per engine tick on the UI
/// thread, then read lock-free from the low-level mouse hook's callback thread to decide
/// whether a click should be SWALLOWED (point inside the mask) or PASSED to the app behind
/// (ambient-only or bare-desktop region).
///
/// COORDINATES: rects are PHYSICAL virtual-desktop pixels — the same space as
/// <see cref="ScreenInfo.Bounds"/> and the WH_MOUSE_LL <c>HookPoint</c> — so the hook
/// compares raw coordinates directly with no DPI conversion.
///
/// THREADING: fully immutable; safe for concurrent reads. The compositor publishes a new
/// instance via a volatile reference swap; readers always see a consistent snapshot.
/// </summary>
public sealed class CaptureMask
{
    /// <summary>
    /// Shared empty instance. Returned by <see cref="CaptureMaskBuilder.Build"/> when no
    /// non-ambient active layer contributed any region, so the ambient regions / bare
    /// desktop remain click-through.
    /// </summary>
    public static CaptureMask Empty { get; } = new(Array.Empty<PixelRect>());

    private readonly PixelRect[] _rects;

    /// <summary>Construct from an already-validated rect array (takes ownership).</summary>
    internal CaptureMask(PixelRect[] rects)
    {
        _rects = rects ?? Array.Empty<PixelRect>();
    }

    /// <summary>Number of capture regions in the mask (not the painted area — the rect count).</summary>
    public int Count => _rects.Length;

    /// <summary>
    /// True when the point (PHYSICAL px) lies inside any capture region. O(rect-count); the
    /// hook thread runs under the system-wide low-level mouse timeout, so this stays linear
    /// with no allocation. Edge-inclusive (a click exactly on a region boundary is captured).
    /// </summary>
    public bool Contains(double x, double y)
    {
        var rects = _rects;
        for (int i = 0; i < rects.Length; i++)
        {
            var r = rects[i];
            // Width/Height are guaranteed > 0 by the builder; the explicit check defends
            // against a caller hand-constructing a degenerate rect.
            if (r.Width <= 0 || r.Height <= 0) continue;
            if (x >= r.X && x <= r.Right && y >= r.Y && y <= r.Bottom)
                return true;
        }
        return false;
    }
}

/// <summary>
/// Accumulates <see cref="PixelRect"/> capture regions into a <see cref="CaptureMask"/>.
/// Reused across engine ticks (call <see cref="Reset"/> at the top of each tick) so the
/// per-frame build allocates only the final rect array + one <see cref="CaptureMask"/>.
/// Not thread-safe — the compositor builds on the UI thread only.
/// </summary>
public sealed class CaptureMaskBuilder
{
    private readonly List<PixelRect> _rects = new();

    /// <summary>Clear accumulated regions; ready to build a fresh frame's mask.</summary>
    public void Reset() => _rects.Clear();

    /// <summary>Number of regions accumulated since the last <see cref="Reset"/>.</summary>
    public int Count => _rects.Count;

    /// <summary>
    /// Add a capture region (PHYSICAL px). Degenerate rects (zero or negative width/height)
    /// are silently skipped so a layer that has nothing painted does not contribute a
    /// zero-area region. Empty rects never block, but keeping them out keeps the mask small.
    /// </summary>
    public void Add(in PixelRect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        _rects.Add(rect);
    }

    /// <summary>
    /// Convenience: add a region from raw coordinates. Skips degenerate rects.
    /// </summary>
    public void Add(double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        _rects.Add(new PixelRect(x, y, width, height));
    }

    /// <summary>
    /// Freeze the accumulated regions into an immutable <see cref="CaptureMask"/> and reset
    /// the builder. Returns <see cref="CaptureMask.Empty"/> (shared singleton) when nothing
    /// was added, so an idle/ambient-only frame allocates nothing.
    /// </summary>
    public CaptureMask Build()
    {
        if (_rects.Count == 0) return CaptureMask.Empty;
        var arr = _rects.ToArray();
        _rects.Clear();
        return new CaptureMask(arr);
    }
}
