using System;
using ConditioningControlPanel.Core.Services.AvatarTube;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the pure AvatarTube geometry math for the 2026-07-11 core rebuild
/// (owner-confirmed contract, board REBUILD SPEC 2026-07-11 / Engram obs #7):
/// - screen-fit scale = WPF CalculateScaleFactor parity
///   (WPF AvatarTube/AvatarTubeWindow.Windowing.cs:425-461),
/// - attached scale-with-main-window with hysteresis so a FIXED window size
///   yields ONE stable scale (kills the telemetry 0.527&lt;-&gt;0.738 flip, obs #6),
/// - attached anchor = WPF UpdatePosition math in physical px (Windowing.cs:587-620),
/// - detached free-resize clamp (owner-widened beyond WPF's 0.5-1.5),
/// - transient-geometry classification (no logical-bounds guard regression).
/// </summary>
public class TubeGeometryMathTests
{
    // ================================================================
    // ComputeScreenScale (WPF Windowing.cs:441-445)

    [Fact]
    public void ScreenScale_1080p_MatchesWpfWidthLimitedValue()
    {
        // 1920x1040 work area at 100% DPI: min(0.85*1040/1020=0.8667, 0.3*1920/780=0.7385) -> 0.738...
        double s = TubeGeometryMath.ComputeScreenScale(1920, 1040);
        Assert.Equal(0.7385, s, 3);
    }

    [Fact]
    public void ScreenScale_TallScreen_IsHeightLimited()
    {
        // Narrow-but-tall: width term dominates the min() and hits the 0.4 floor.
        // min(0.85*2000/1020=1.667, 0.3*900/780=0.346) -> clamped to 0.4.
        double s = TubeGeometryMath.ComputeScreenScale(900, 2000);
        Assert.Equal(TubeGeometryMath.MinScreenScale, s);
    }

    [Fact]
    public void ScreenScale_HugeScreen_CapsAtOne()
    {
        double s = TubeGeometryMath.ComputeScreenScale(10000, 10000);
        Assert.Equal(TubeGeometryMath.MaxScreenScale, s);
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(1000, 0)]
    [InlineData(-5, 700)]
    [InlineData(double.NaN, 700)]
    public void ScreenScale_InvalidMetrics_FallBackToSafeDefault(double w, double h)
    {
        Assert.Equal(TubeGeometryMath.FallbackScreenScale, TubeGeometryMath.ComputeScreenScale(w, h));
    }

    // ================================================================
    // QuantizeParentRatio

    [Fact]
    public void ParentRatio_DefaultMainWindowHeight_IsExactlyOne()
    {
        Assert.Equal(1.0, TubeGeometryMath.QuantizeParentRatio(1000.0));
    }

    [Fact]
    public void ParentRatio_QuantizesSubPixelJitterToSameValue()
    {
        // Sub-pixel layout jitter must not produce a new ratio (scale stability).
        double a = TubeGeometryMath.QuantizeParentRatio(1000.2);
        double b = TubeGeometryMath.QuantizeParentRatio(1000.4);
        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(double.NaN)]
    public void ParentRatio_InvalidHeight_CollapsesToDefault(double h)
    {
        Assert.Equal(1.0, TubeGeometryMath.QuantizeParentRatio(h));
    }

    // ================================================================
    // ComposeAttachedScale (screenScale * ratio, floored 0.30, capped at screenScale)

    [Fact]
    public void AttachedScale_AtDefaultParentSize_EqualsScreenScale()
    {
        Assert.Equal(0.738, TubeGeometryMath.ComposeAttachedScale(0.738, 1.0), 3);
    }

    [Fact]
    public void AttachedScale_LargerParent_NeverExceedsScreenFitCap()
    {
        // Maximized main window (ratio > 1) must not blow past the screen-fit cap.
        Assert.Equal(0.738, TubeGeometryMath.ComposeAttachedScale(0.738, 1.35), 3);
    }

    [Fact]
    public void AttachedScale_SmallerParent_ScalesDownProportionally()
    {
        Assert.Equal(0.738 * 0.8, TubeGeometryMath.ComposeAttachedScale(0.738, 0.8), 3);
    }

    [Fact]
    public void AttachedScale_TinyParent_FloorsAtAbsoluteMin()
    {
        Assert.Equal(TubeGeometryMath.AbsoluteMinScale, TubeGeometryMath.ComposeAttachedScale(0.738, 0.1));
    }

    // ================================================================
    // ShouldApplyScale (hysteresis dead-band -> one stable value per window size)

    [Fact]
    public void ScaleHysteresis_WithinDeadband_IsRejected()
    {
        Assert.False(TubeGeometryMath.ShouldApplyScale(0.738, 0.738));
        Assert.False(TubeGeometryMath.ShouldApplyScale(0.738, 0.738 + TubeGeometryMath.ScaleDeadband / 2));
        Assert.False(TubeGeometryMath.ShouldApplyScale(0.738, 0.738 - TubeGeometryMath.ScaleDeadband / 2));
    }

    [Fact]
    public void ScaleHysteresis_RealResize_IsAccepted()
    {
        // The telemetry flip pair 0.527 <-> 0.738 (obs #6) is far outside the dead-band:
        // it must be accepted as a REAL change when it comes from a genuine resize...
        Assert.True(TubeGeometryMath.ShouldApplyScale(0.738, 0.527));
        // ...and the first-ever computation (NaN current) always applies.
        Assert.True(TubeGeometryMath.ShouldApplyScale(double.NaN, 0.738));
    }

    // ================================================================
    // ComputeDetachedScale (free resize, owner-widened clamp, screen-fit cap)

    [Fact]
    public void DetachedScale_DefaultZoom_EqualsScreenScale()
    {
        Assert.Equal(0.738, TubeGeometryMath.ComputeDetachedScale(0.738, 1.0, 2000), 3);
    }

    [Fact]
    public void DetachedScale_ClampsUserZoomToWidenedBounds()
    {
        // Below the floor -> clamped to MinUserScale.
        Assert.Equal(0.738 * TubeGeometryMath.MinUserScale,
            TubeGeometryMath.ComputeDetachedScale(0.738, 0.01, 100000), 3);
        // Above the cap -> clamped to MaxUserScale (with a work area tall enough not to cap first).
        Assert.Equal(0.738 * TubeGeometryMath.MaxUserScale,
            TubeGeometryMath.ComputeDetachedScale(0.738, 99.0, 100000), 3);
    }

    [Fact]
    public void DetachedScale_NeverTallerThanWorkArea()
    {
        // 0.738 * 2.5 = 1.845 would be 1882px tall on a 1040px work area -> capped to fit.
        double s = TubeGeometryMath.ComputeDetachedScale(0.738, TubeGeometryMath.MaxUserScale, 1040);
        Assert.Equal(1040 / TubeGeometryMath.DesignHeight, s, 6);
        Assert.True(TubeGeometryMath.DesignHeight * s <= 1040 + 0.001);
    }

    // ================================================================
    // ComputeAttachedAnchor (WPF Windowing.cs:608-612 in physical px)

    [Fact]
    public void Anchor_At100PercentDpi_MatchesWpfFormula()
    {
        // parent at (1000, 200), client 1600x1000, scale 0.738, tube = design*scale.
        double scale = 0.738;
        double tubeW = TubeGeometryMath.DesignWidth * scale;   // 575.64
        double tubeH = TubeGeometryMath.DesignHeight * scale;  // 752.76
        var (left, top) = TubeGeometryMath.ComputeAttachedAnchor(
            1000, 200, 1000, tubeW, tubeH, scale, 1.0);

        // WPF: newLeft = parent.Left - tubeW - (-350*scale) = 1000 - 575.64 + 258.3 = 682.66
        Assert.Equal(1000 - (int)Math.Round(tubeW + TubeGeometryMath.BaseOffsetFromParent * scale), left);
        // WPF: newTop = parent.Top + (parentH - tubeH)/2 + 20*scale = 200 + 123.62 + 14.76
        Assert.Equal(200 + (int)Math.Round((1000 - tubeH) / 2.0 + TubeGeometryMath.VerticalOffset * scale), top);
    }

    [Fact]
    public void Anchor_At150PercentDpi_ScalesLogicalTermsToPhysical()
    {
        double scale = 0.738;
        double tubeW = TubeGeometryMath.DesignWidth * scale;
        double tubeH = TubeGeometryMath.DesignHeight * scale;
        var (left150, top150) = TubeGeometryMath.ComputeAttachedAnchor(0, 0, 1000, tubeW, tubeH, scale, 1.5);

        // All parent-relative offsets are logical -> the physical result applies the
        // 1.5x factor INSIDE the rounding (single round, WPF formula in physical px).
        Assert.Equal(-(int)Math.Round((tubeW + TubeGeometryMath.BaseOffsetFromParent * scale) * 1.5), left150);
        Assert.Equal((int)Math.Round(((1000 - tubeH) / 2.0 + TubeGeometryMath.VerticalOffset * scale) * 1.5), top150);
    }

    [Fact]
    public void Anchor_VerticallyCentersTubeOnParentClient()
    {
        // Tube exactly as tall as the parent client -> top = parentY + 20*scale (the WPF nudge).
        double scale = 1.0;
        var (_, top) = TubeGeometryMath.ComputeAttachedAnchor(
            0, 500, TubeGeometryMath.DesignHeight, 780, TubeGeometryMath.DesignHeight, scale, 1.0);
        Assert.Equal(500 + (int)TubeGeometryMath.VerticalOffset, top);
    }

    // ================================================================
    // ClassifyParentGeometry (transient-only skip; no logical-bounds regression)

    [Fact]
    public void Geometry_SettledParent_IsValid()
    {
        Assert.Equal(TubeParentGeometryState.Valid,
            TubeGeometryMath.ClassifyParentGeometry(false, 1600, 1000, 100, 100));
    }

    [Fact]
    public void Geometry_NegativePositions_AreStillValid()
    {
        // Multi-monitor setups legitimately produce large negative physical origins.
        // The WPF logical -500/-2000 guard must NOT be reintroduced (obs #6 lesson).
        Assert.Equal(TubeParentGeometryState.Valid,
            TubeGeometryMath.ClassifyParentGeometry(false, 1600, 1000, -5000, -1200));
    }

    [Fact]
    public void Geometry_Minimized_SkipsWithoutRetry()
    {
        Assert.Equal(TubeParentGeometryState.SkipMinimized,
            TubeGeometryMath.ClassifyParentGeometry(true, 1600, 1000, 100, 100));
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(1600, 0)]
    [InlineData(double.NaN, 1000)]
    public void Geometry_EmptyClientSize_IsTransient(double w, double h)
    {
        Assert.Equal(TubeParentGeometryState.SkipTransient,
            TubeGeometryMath.ClassifyParentGeometry(false, w, h, 100, 100));
    }

    [Fact]
    public void Geometry_MinimizedParkingSentinel_IsTransient()
    {
        Assert.Equal(TubeParentGeometryState.SkipTransient,
            TubeGeometryMath.ClassifyParentGeometry(false, 1600, 1000, -32000, -32000));
    }

    // ================================================================
    // ClampToWorkArea

    [Fact]
    public void Clamp_InsideWorkArea_IsUnchanged()
    {
        Assert.Equal((100, 100), TubeGeometryMath.ClampToWorkArea(100, 100, 0, 0, 1920, 1080, 400, 500));
    }

    [Fact]
    public void Clamp_BeyondEdges_SnapsToWorkArea()
    {
        // Past the bottom-right -> pinned so the window stays fully visible.
        Assert.Equal((1920 - 400, 1080 - 500),
            TubeGeometryMath.ClampToWorkArea(5000, 5000, 0, 0, 1920, 1080, 400, 500));
        // Before the top-left -> pinned to the origin.
        Assert.Equal((0, 0), TubeGeometryMath.ClampToWorkArea(-999, -999, 0, 0, 1920, 1080, 400, 500));
    }

    [Fact]
    public void Clamp_WindowLargerThanWorkArea_PinsToOrigin()
    {
        Assert.Equal((0, 0), TubeGeometryMath.ClampToWorkArea(300, 300, 0, 0, 1920, 1080, 3000, 3000));
    }
}
