namespace ConditioningControlPanel.Core.Services.AvatarTube;

/// <summary>
/// Result of validating live parent-window geometry before an anchor write.
/// Only genuinely transient/invalid parent geometry may skip a reposition —
/// the WPF logical -500/5000 bounds guard must never come back (it silently
/// rejected valid physical-pixel anchors and stranded the tube).
/// </summary>
public enum TubeParentGeometryState
{
    /// <summary>Geometry is settled and safe to anchor against.</summary>
    Valid,

    /// <summary>Parent is minimized — skip, no retry (restore events re-anchor).</summary>
    SkipMinimized,

    /// <summary>Geometry is transiently invalid (empty client size / -32000 parking sentinel) — skip and retry shortly.</summary>
    SkipTransient,
}

/// <summary>
/// Pure geometry math for the AvatarTube window (2026-07-11 core rebuild,
/// owner-confirmed contract: attached = scale-with-main-window, detached =
/// free independent resize, stable size/position — no oscillation, no storm).
///
/// Extracted into CCP.Core so the scale/anchor rules are unit-testable without
/// the Avalonia dispatcher. The Avalonia-side TubeGeometryController is the
/// SINGLE writer of the tube window's Position and size; it consumes only
/// these functions for every computation.
///
/// WPF ground truth: ConditioningControlPanel/AvatarTube/AvatarTubeWindow.Windowing.cs
/// (design constants :41-49, CalculateScaleFactor :425-461, UpdatePosition :587-620).
/// </summary>
public static class TubeGeometryMath
{
    /// <summary>Design canvas width the AXAML is authored against (WPF Windowing.cs:41).</summary>
    public const double DesignWidth = 780;

    /// <summary>Design canvas height the AXAML is authored against (WPF Windowing.cs:42).</summary>
    public const double DesignHeight = 1020;

    /// <summary>Horizontal overlap into the parent window; negative = overlap (WPF Windowing.cs:46).</summary>
    public const double BaseOffsetFromParent = -350;

    /// <summary>Vertical offset from the parent's vertical center (WPF Windowing.cs:49).</summary>
    public const double VerticalOffset = 20;

    /// <summary>
    /// The main window's DEFAULT client height (MainWindow.axaml declares Height="1000").
    /// At the default main-window size parentRatio == 1.0 and the attached scale equals
    /// the WPF screen-fit scale — i.e. the tube looks exactly like WPF at default size
    /// and grows/shrinks proportionally as the main window resizes (owner-approved
    /// deviation from WPF's screen-only scaling, obs #7 / board REBUILD SPEC 2026-07-11).
    /// </summary>
    public const double ReferenceParentHeight = 1000.0;

    /// <summary>Hard floor so a tiny main window never shrinks the tube into an unreadable sliver.</summary>
    public const double AbsoluteMinScale = 0.30;

    /// <summary>WPF screen-fit clamp floor (WPF Windowing.cs:445 — Math.Max(0.4, ...)).</summary>
    public const double MinScreenScale = 0.4;

    /// <summary>WPF screen-fit clamp cap (WPF Windowing.cs:445 — Math.Min(1.0, ...)).</summary>
    public const double MaxScreenScale = 1.0;

    /// <summary>Fallback screen-fit scale when screen metrics are unavailable (WPF Windowing.cs:457).</summary>
    public const double FallbackScreenScale = 0.7;

    /// <summary>
    /// Hysteresis dead-band for the attached scale: a candidate scale within this
    /// distance of the current scale is NOT applied, so a fixed main-window size
    /// always settles to ONE value (kills the telemetry 0.527&lt;-&gt;0.738 flip,
    /// obs #6 avatartube-rootcause-2026-07-11).
    /// </summary>
    public const double ScaleDeadband = 0.008;

    /// <summary>Detached manual-zoom floor (owner-widened from WPF's 0.5, board REBUILD SPEC).</summary>
    public const double MinUserScale = 0.25;

    /// <summary>Detached manual-zoom cap (owner-widened from WPF's 1.5, board REBUILD SPEC).</summary>
    public const double MaxUserScale = 2.5;

    /// <summary>Manual-zoom step per Ctrl+wheel / grow / shrink action (WPF Windowing.cs:62).</summary>
    public const double UserScaleStep = 0.25;

    /// <summary>Win32 parks minimized windows at (-32000,-32000); anything at or beyond this is transient.</summary>
    public const int MinimizedSentinel = -30000;

    /// <summary>
    /// WPF CalculateScaleFactor parity (Windowing.cs:425-461): screen-fit scale from the
    /// primary screen working area in LOGICAL units (physical / scaling). This is both
    /// the "default look" anchor and the hard cap for the attached scale.
    /// </summary>
    public static double ComputeScreenScale(double workWidthLogical, double workHeightLogical)
    {
        if (double.IsNaN(workWidthLogical) || double.IsNaN(workHeightLogical)
            || workWidthLogical <= 0 || workHeightLogical <= 0)
        {
            return FallbackScreenScale;
        }

        double maxHeightScale = (workHeightLogical * 0.85) / DesignHeight; // WPF Windowing.cs:441
        double maxWidthScale = (workWidthLogical * 0.3) / DesignWidth;     // WPF Windowing.cs:442
        double scale = Math.Min(maxHeightScale, maxWidthScale);            // WPF Windowing.cs:444
        return Math.Clamp(scale, MinScreenScale, MaxScreenScale);          // WPF Windowing.cs:445
    }

    /// <summary>
    /// Parent-height ratio for the attached scale-with-main-window contract, quantized
    /// to 3 decimals so sub-pixel layout jitter cannot produce a new scale value.
    /// Invalid heights collapse to 1.0 (= the default look).
    /// </summary>
    public static double QuantizeParentRatio(double parentClientHeight)
    {
        if (double.IsNaN(parentClientHeight) || parentClientHeight <= 0)
            return 1.0;
        return Math.Round(parentClientHeight / ReferenceParentHeight, 3);
    }

    /// <summary>
    /// Composed attached scale: screen-fit scale scaled by the parent ratio, floored at
    /// <see cref="AbsoluteMinScale"/> and hard-capped at the screen-fit scale so the tube
    /// never exceeds the monitor (board REBUILD SPEC invariant 2).
    /// </summary>
    public static double ComposeAttachedScale(double screenScale, double parentRatio)
        => Math.Clamp(screenScale * parentRatio, AbsoluteMinScale, screenScale);

    /// <summary>
    /// Hysteresis gate: apply a new scale only when it moved beyond the dead-band.
    /// A fixed window size therefore yields exactly ONE stable scale (no A&lt;-&gt;B flip).
    /// </summary>
    public static bool ShouldApplyScale(double currentScale, double candidateScale)
        => double.IsNaN(currentScale) || Math.Abs(candidateScale - currentScale) >= ScaleDeadband;

    /// <summary>
    /// Detached effective scale: screen-fit anchor x free user zoom (owner contract:
    /// "as small or big as we want", independent of the main window), capped only so the
    /// tube never renders taller than the working area it sits on.
    /// </summary>
    public static double ComputeDetachedScale(double screenScale, double userScale, double workHeightLogical)
    {
        double clampedUser = Math.Clamp(
            double.IsNaN(userScale) || userScale <= 0 ? 1.0 : userScale,
            MinUserScale, MaxUserScale);
        double effective = screenScale * clampedUser;
        if (!double.IsNaN(workHeightLogical) && workHeightLogical > 0)
        {
            double screenFitCap = workHeightLogical / DesignHeight;
            effective = Math.Min(effective, screenFitCap);
        }
        return Math.Max(effective, 0.1);
    }

    /// <summary>
    /// Attached anchor position in PHYSICAL pixels: left-docked with a -350*scale overlap
    /// into the parent's left edge, vertically centered on the parent client area plus a
    /// +20*scale nudge (WPF UpdatePosition, Windowing.cs:608-612). Logical inputs (tube
    /// size, parent client height) are converted through the parent's render scaling
    /// because Window.Position is physical while sizes are logical.
    /// </summary>
    public static (int Left, int Top) ComputeAttachedAnchor(
        int parentX,
        int parentY,
        double parentClientHeight,
        double tubeWidthLogical,
        double tubeHeightLogical,
        double finalScale,
        double renderScaling)
    {
        double s = renderScaling > 0 && !double.IsNaN(renderScaling) ? renderScaling : 1.0;
        double scaledOffset = BaseOffsetFromParent * finalScale; // WPF Windowing.cs:608
        int left = parentX - (int)Math.Round((tubeWidthLogical + scaledOffset) * s); // WPF Windowing.cs:611
        int top = parentY + (int)Math.Round(
            (((parentClientHeight - tubeHeightLogical) / 2.0) + VerticalOffset * finalScale) * s); // WPF Windowing.cs:612
        return (left, top);
    }

    /// <summary>
    /// Classifies live parent geometry before an anchor write. Only genuinely transient
    /// states may skip: minimized (no retry — restore events re-anchor), empty client
    /// size, or the Win32 -32000 minimized parking sentinel (both retryable). Everything
    /// else is Valid — never re-introduce the WPF logical bounds guard in physical space.
    /// </summary>
    public static TubeParentGeometryState ClassifyParentGeometry(
        bool parentMinimized,
        double parentClientWidth,
        double parentClientHeight,
        int parentX,
        int parentY)
    {
        if (parentMinimized)
            return TubeParentGeometryState.SkipMinimized;
        if (parentClientWidth <= 0 || parentClientHeight <= 0
            || double.IsNaN(parentClientWidth) || double.IsNaN(parentClientHeight))
            return TubeParentGeometryState.SkipTransient;
        if (parentX <= MinimizedSentinel || parentY <= MinimizedSentinel)
            return TubeParentGeometryState.SkipTransient;
        return TubeParentGeometryState.Valid;
    }

    /// <summary>
    /// Seeds the parent-height feed before the first SizeChanged event arrives
    /// (obs #6 retest-2 fix 1: the startup 0.527&lt;-&gt;0.738 settle came from reading a
    /// not-yet-settled parent client size at tube construction). The DECLARED height
    /// (the Window.Height property - axaml value or restored size) is preferred: at
    /// construction time the live ClientSize still holds the platform's pre-layout
    /// default (~714 on Win32, measured), which is garbage, while Height already holds
    /// the real target. Live ClientSize is only the fallback when no height is declared.
    /// </summary>
    public static double ResolveSeedParentHeight(double liveClientHeight, double declaredHeight)
    {
        if (!double.IsNaN(declaredHeight) && declaredHeight > 0)
            return declaredHeight;
        if (!double.IsNaN(liveClientHeight) && liveClientHeight > 0)
            return liveClientHeight;
        return double.NaN;
    }

    /// <summary>
    /// Parent ratio with transient-read immunity (obs #6 retest-2 fix 1): an invalid or
    /// transient parent height (NaN/zero/negative — minimized, mid-restore, pre-layout)
    /// KEEPS the last applied ratio instead of collapsing to 1.0. Collapsing to 1.0 was
    /// the only path by which the attached scale could jump to the screen-fit cap and
    /// produce the 0.527&lt;-&gt;0.738 re-settle without a real resize.
    /// </summary>
    public static double ResolveParentRatio(double parentClientHeight, double lastRatio)
    {
        if (double.IsNaN(parentClientHeight) || parentClientHeight <= 0)
            return double.IsNaN(lastRatio) || lastRatio <= 0 ? 1.0 : lastRatio;
        return QuantizeParentRatio(parentClientHeight);
    }

    /// <summary>
    /// Detached free-drag position in PHYSICAL screen pixels, BOTH axes (owner contract
    /// obs #7 / retest-2 fix 3a). WPF parity: OnMouseMove computes
    /// Left/Top = dragStart + (currentScreenPoint - dragStartScreenPoint)
    /// (WPF AvatarTube/AvatarTubeWindow.Windowing.cs:1748-1766) — screen-space deltas,
    /// immune to the window moving under the pointer mid-drag.
    /// </summary>
    public static (int X, int Y) ComputeDragPosition(
        int startWindowX, int startWindowY,
        int startPointerX, int startPointerY,
        int currentPointerX, int currentPointerY)
        => (startWindowX + (currentPointerX - startPointerX),
            startWindowY + (currentPointerY - startPointerY));

    /// <summary>
    /// Detached corner-drag resize (NEW owner contract 2026-07-11, obs #6 fix 3c — no WPF
    /// precedent; WPF only had Ctrl+wheel/menu zoom). The user scale is driven by the
    /// vertical drag delta against the window's starting physical height (aspect is fixed
    /// by the uniform viewbox, so one axis fully determines the size). Grips on the TOP
    /// edge grow the tube when dragged UP. Result is clamped to the detached zoom range.
    /// </summary>
    public static double ComputeCornerResizeUserScale(
        double startUserScale, double startHeightPhysical, double deltaYPhysical, bool topEdge)
    {
        if (double.IsNaN(startUserScale) || startUserScale <= 0)
            startUserScale = 1.0;
        if (double.IsNaN(startHeightPhysical) || startHeightPhysical <= 0
            || double.IsNaN(deltaYPhysical))
        {
            return Math.Clamp(startUserScale, MinUserScale, MaxUserScale);
        }

        double dh = topEdge ? -deltaYPhysical : deltaYPhysical;
        double factor = (startHeightPhysical + dh) / startHeightPhysical;
        if (double.IsNaN(factor) || double.IsInfinity(factor) || factor <= 0)
            factor = double.Epsilon; // fully collapsed drag -> clamp floor below
        return Math.Clamp(startUserScale * factor, MinUserScale, MaxUserScale);
    }

    /// <summary>
    /// Keeps the corner OPPOSITE the dragged grip anchored during a detached corner
    /// resize (obs #6 fix 3c): a top-edge grip keeps the bottom edge fixed, a left-edge
    /// grip keeps the right edge fixed. All values in physical pixels.
    /// </summary>
    public static (int X, int Y) ComputeCornerAnchorPosition(
        int startX, int startY,
        double startWidthPhysical, double startHeightPhysical,
        double newWidthPhysical, double newHeightPhysical,
        bool anchorRight, bool anchorBottom)
    {
        int x = anchorRight ? startX + (int)Math.Round(startWidthPhysical - newWidthPhysical) : startX;
        int y = anchorBottom ? startY + (int)Math.Round(startHeightPhysical - newHeightPhysical) : startY;
        return (x, y);
    }

    /// <summary>
    /// Clamps a physical-pixel window origin into a physical working area so a detached
    /// drag/zoom can never strand the tube fully off-screen.
    /// </summary>
    public static (int X, int Y) ClampToWorkArea(
        int posX, int posY,
        int workX, int workY, int workRight, int workBottom,
        int windowWidthPhysical, int windowHeightPhysical)
    {
        int w = Math.Max(1, windowWidthPhysical);
        int h = Math.Max(1, windowHeightPhysical);
        int maxX = Math.Max(workX, workRight - w);
        int maxY = Math.Max(workY, workBottom - h);
        return (Math.Clamp(posX, workX, maxX), Math.Clamp(posY, workY, maxY));
    }
}
