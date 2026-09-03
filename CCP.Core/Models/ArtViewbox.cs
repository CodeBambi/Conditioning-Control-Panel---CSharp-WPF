namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// A normalized viewbox: the sub-region of a source image an art surface shows, in 0..1
    /// units of that image. X and Y are the top-left corner, Width and Height the extent.
    ///
    /// <para><b>Why this and not <c>System.Windows.Rect</c>.</b> The framing table and the
    /// resolve rules are pure data and pure arithmetic, and they belong in Core next to the mod
    /// manifest that carries them. A bare <c>Rect</c> resolves to <c>System.Windows.Rect</c>,
    /// which a net8.0 assembly cannot name - the trap CLAUDE.md warns about - and it was the only
    /// thing keeping the table, and behind it the settings model, in the WPF head.</para>
    ///
    /// <para>Deliberately not a geometry type: no operators, no intersection, no union. Nothing
    /// here needs them. The WPF head converts at its brush boundary with <c>ToRect()</c>, one
    /// extension in the head; every other consumer only reads Width and Height.</para>
    /// </summary>
    public readonly record struct ArtViewbox(double X, double Y, double Width, double Height);
}
