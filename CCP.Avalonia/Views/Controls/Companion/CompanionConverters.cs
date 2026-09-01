using Avalonia;
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    // =====================================================================================
    //  PORTED (PARTIAL) from ConditioningControlPanel/Views/Controls/Companion/CompanionConverters.cs.
    //  Only what a ported zone actually needs is here; the rest of the WPF file is Visibility
    //  plumbing Avalonia does not need (IsVisible binds to a bool directly, and the built-in
    //  ObjectConverters / StringConverters cover the null and empty tests).
    // =====================================================================================

    /// <summary>
    /// The port of the WPF theme's <c>CmpFractionToStar</c> / <c>CmpFractionToRemainderStar</c>
    /// pair: split a Grid into <c>fraction*</c> and <c>(1-fraction)*</c> columns so a thumb sits
    /// at a 0..1 position along a track.
    ///
    /// <para>It is an attached property rather than the original's two value converters because
    /// neither half of that design survives the move. Avalonia's <see cref="ColumnDefinition"/>
    /// derives from <c>AvaloniaObject</c>, not <c>StyledElement</c>, so it has no DataContext and
    /// a binding on its <c>Width</c> has no source; and <see cref="Grid.ColumnDefinitions"/> is a
    /// plain CLR collection property, not a styled one, so it cannot be bound either (the XAML
    /// compiler rejects it outright — AVLN3000). Setting the collection from a property-changed
    /// handler is the one place left that is both bindable and in the visual tree, and it produces
    /// exactly the original layout.</para>
    /// </summary>
    public static class CompanionGrid
    {
        /// <summary>
        /// 0..1. Default is NaN, not 0, so that binding a genuine 0.0 still raises a change and
        /// builds the columns — with 0.0 as the default it would not, and the track would render
        /// with no columns at all.
        /// </summary>
        public static readonly AttachedProperty<double> StarFractionProperty =
            AvaloniaProperty.RegisterAttached<Grid, double>(
                "StarFraction", typeof(CompanionGrid), double.NaN);

        static CompanionGrid()
        {
            StarFractionProperty.Changed.AddClassHandler<Grid>((grid, e) =>
            {
                double f = Clamp(e.NewValue as double? ?? double.NaN);
                grid.ColumnDefinitions = new ColumnDefinitions
                {
                    new ColumnDefinition(new GridLength(f, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(1.0 - f, GridUnitType.Star)),
                };
            });
        }

        public static void SetStarFraction(Grid grid, double value) => grid.SetValue(StarFractionProperty, value);

        public static double GetStarFraction(Grid grid) => grid.GetValue(StarFractionProperty);

        /// <summary>Clamps anything sane-looking into 0..1. Never throws, never returns NaN.</summary>
        internal static double Clamp(double f)
        {
            if (double.IsNaN(f) || double.IsInfinity(f)) return 0.0;
            if (f < 0.0) return 0.0;
            if (f > 1.0) return 1.0;
            return f;
        }
    }
}
