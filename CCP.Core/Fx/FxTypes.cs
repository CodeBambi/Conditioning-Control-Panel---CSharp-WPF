using System;

namespace ConditioningControlPanel.Fx
{
    /// <summary>A neutral 2D point / vector (WPF Point, Avalonia Point).</summary>
    public readonly record struct Vec2(double X, double Y);

    /// <summary>A neutral rectangle (WPF Rect, Avalonia Rect).</summary>
    public readonly record struct RectD(double Left, double Top, double Width, double Height)
    {
        public double Right => Left + Width;
        public double Bottom => Top + Height;
    }

    /// <summary>
    /// The composable ambient layers of the WPF 7.1.5 AmbientFxCanvas, values verbatim (EdgeDrift
    /// is 1 &lt;&lt; 6 and EdgeFog 1 &lt;&lt; 7, pinned by the WPF tests). A head maps these onto its
    /// own canvas enum.
    /// </summary>
    [Flags]
    public enum AmbientFxLayers
    {
        None = 0,
        FogDrift = 1 << 0,
        AuroraWash = 1 << 1,
        DustField = 1 << 2,
        SheenSweep = 1 << 3,
        GlowBreath = 1 << 4,
        Embers = 1 << 5,
        /// <summary>Section-hued motes drifting clockwise along a window-edge strip (nav polish 9).</summary>
        EdgeDrift = 1 << 6,
        /// <summary>Soft section-hued puffs drifting and breathing along a window-edge strip (polish 11).</summary>
        EdgeFog = 1 << 7,
        /// <summary>The Premium page's motes (polish 12 round 2).</summary>
        VaultMotes = 1 << 8,
    }

    /// <summary>Which window edge a strip lines. Order is load-bearing (SectionEdgeRules.LiftLegStart).</summary>
    public enum EdgeSide
    {
        Top,
        Right,
        Bottom,
        Left,
    }
}
