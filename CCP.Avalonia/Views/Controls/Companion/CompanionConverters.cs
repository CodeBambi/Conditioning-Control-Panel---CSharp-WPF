using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    // =====================================================================================
    //  PORTED (PARTIAL) from ConditioningControlPanel/Views/Controls/Companion/CompanionConverters.cs.
    //
    //  Only the converters the ported zone controls still need cross. The WPF file also carries
    //  CompanionBoolToVisibilityConverter, CompanionEmptyToVisibilityConverter and friends -
    //  those exist to produce a System.Windows.Visibility and have no Avalonia counterpart,
    //  because Avalonia binds IsVisible to a bool directly.
    // =====================================================================================

    /// <summary>
    /// 0..1 fraction to a star <see cref="GridLength"/>. This is the Trainer Card bar recipe:
    /// the filled part of a gauge is a star-width column, so it needs no ActualWidth maths,
    /// causes no layout-thrash binding, and survives any resize.
    /// </summary>
    public sealed class FractionToStarConverter : IValueConverter
    {
        /// <summary>When true the converter returns the *remainder* (1 - fraction).</summary>
        public bool Invert { get; set; }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            double f = ToFraction(value);
            if (Invert) f = 1.0 - f;
            return new GridLength(f, GridUnitType.Star);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => BindingOperations.DoNothing;

        /// <summary>Clamps anything sane-looking into 0..1. Never throws, never returns NaN.</summary>
        internal static double ToFraction(object? value)
        {
            double f;
            switch (value)
            {
                case double d: f = d; break;
                case float ff: f = ff; break;
                case int i: f = i; break;
                case long l: f = l; break;
                case decimal m: f = (double)m; break;
                default: return 0.0;
            }
            if (double.IsNaN(f) || double.IsInfinity(f)) return 0.0;
            if (f < 0.0) return 0.0;
            if (f > 1.0) return 1.0;
            return f;
        }
    }
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
