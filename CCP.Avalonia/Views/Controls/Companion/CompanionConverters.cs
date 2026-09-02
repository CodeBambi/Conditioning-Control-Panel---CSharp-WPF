using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Data;
using Avalonia;
using System.Globalization;
using System;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
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

    /// <para>The WPF file's visibility converters do NOT cross: Avalonia binds IsVisible to a bool,
    /// so <c>CmpBoolToVis</c> is a plain binding, <c>CmpBoolToVisInverse</c> is <c>{Binding !X}</c>,
    /// <c>CmpHasContentToVis</c> is <c>ObjectConverters.IsNotNull</c>, and <c>CmpEnumToVis</c> is
    /// this class again.</para>
    /// </summary>
    public sealed class CompanionEnumEqualsConverter : IValueConverter
    {
        /// <summary>Negate the match. Ported from the WPF CompanionEnumToVisibilityConverter, which
        /// this class also stands in for (CmpEnumToVis / CmpEnumToVisInverse) now that the result
        /// is a bool for IsVisible rather than a Visibility.</summary>
        public bool Invert { get; set; }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool match = Matches(value?.ToString(), parameter?.ToString());
            return Invert ? !match : match;
        }

        /// <summary>
        /// The parameter may name SEVERAL states, pipe-separated (<c>ConverterParameter=Locked|Dormant</c>):
        /// the match is "value is any of these". A single name behaves exactly as before.
        /// </summary>
        internal static bool Matches(string? value, string? parameter)
        {
            if (value == null || string.IsNullOrEmpty(parameter)) return false;
            foreach (var name in parameter!.Split('|'))
            {
                if (string.Equals(value, name.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b && b && parameter != null)
            {
                try
                {
                    var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
                    if (t.IsEnum) return Enum.Parse(t, parameter.ToString()!, ignoreCase: true);
                }
                catch (ArgumentException) { /* unparseable parameter — leave the source alone */ }
                return parameter;
            }
            return BindingOperations.DoNothing;
        }
    }

    /// <summary>
    /// bool -> double, for the "she's asleep" desaturation and other dim states.
    /// Parameter is "trueOpacity|falseOpacity" (default "1.0|0.45"). Verbatim from the WPF head.
    /// </summary>
    public sealed class CompanionBoolToOpacityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            double on = 1.0, off = 0.45;
            if (parameter is string p)
            {
                var parts = p.Split('|');
                if (parts.Length == 2)
                {
                    double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out on);
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out off);
                }
            }
            return value is bool b && b ? on : off;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => BindingOperations.DoNothing;
    }

    /// <summary>
    /// int equality against the ConverterParameter, for radio strips over an int property.
    /// Unlike <see cref="CompanionEnumEqualsConverter"/> it PARSES the parameter and returns
    /// DoNothing when it cannot, so a privacy retention radio can never light up without the
    /// source changing. Verbatim from the WPF head.
    /// </summary>
    public sealed class CompanionIntEqualsConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value != null && TryParse(parameter, out var wanted) &&
               value is int actual && actual == wanted;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b && b && TryParse(parameter, out var wanted)) return wanted;
            return BindingOperations.DoNothing;
        }

        private static bool TryParse(object? parameter, out int value)
            => int.TryParse(parameter?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
