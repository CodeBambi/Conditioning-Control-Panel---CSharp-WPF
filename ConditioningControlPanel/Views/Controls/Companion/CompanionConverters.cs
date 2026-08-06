using System;
using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    // =====================================================================================
    //  Companion tab redesign — converters used by CompanionTheme.xaml and the zone controls.
    //
    //  These live here (and are instanced inside CompanionTheme.xaml) rather than relying on
    //  the app-level Converters.xaml because DataTemplates defined inside a ResourceDictionary
    //  resolve StaticResource against that dictionary's own scope — Known Issues #4. Every
    //  key is prefixed "Cmp" so it can never collide with an app-level key.
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
            => Binding.DoNothing;

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

    /// <summary>bool -> Visible/Collapsed. Pass "Hidden" as parameter to collapse to Hidden instead.</summary>
    public sealed class CompanionBoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool b = value is bool v && v;
            if (Invert) b = !b;
            if (b) return Visibility.Visible;
            return string.Equals(parameter as string, "Hidden", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Hidden
                : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool b = value is Visibility vis && vis == Visibility.Visible;
            return Invert ? !b : b;
        }
    }

    /// <summary>null / empty string / empty collection -> Collapsed. Anything else -> Visible.</summary>
    public sealed class CompanionEmptyToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool has = HasContent(value);
            if (Invert) has = !has;
            return has ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;

        internal static bool HasContent(object? value)
        {
            switch (value)
            {
                case null: return false;
                case string s: return !string.IsNullOrWhiteSpace(s);
                case int i: return i != 0;
                case ICollection c: return c.Count > 0;
                case IEnumerable e: return e.GetEnumerator().MoveNext();
                default: return true;
            }
        }
    }

    /// <summary>
    /// bool -> double, for the "she's asleep" desaturation and other dim states.
    /// Parameter is "trueOpacity|falseOpacity" (default "1.0|0.45").
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
            => Binding.DoNothing;
    }

    /// <summary>
    /// Enum (or anything with a meaningful ToString) equality against the ConverterParameter.
    /// Drives the segmented dials: each RadioButton binds IsChecked two-way to the same enum
    /// property with a different parameter, so the strip needs no code-behind. ConvertBack
    /// returns <see cref="Binding.DoNothing"/> on uncheck so the group never clears the source.
    /// </summary>
    public sealed class CompanionEnumEqualsConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value != null && parameter != null &&
               string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);

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
            return Binding.DoNothing;
        }
    }

    /// <summary>Same as <see cref="CompanionEnumEqualsConverter"/> but yields Visibility.</summary>
    public sealed class CompanionEnumToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool match = value != null && parameter != null &&
                         string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
            if (Invert) match = !match;
            return match ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
