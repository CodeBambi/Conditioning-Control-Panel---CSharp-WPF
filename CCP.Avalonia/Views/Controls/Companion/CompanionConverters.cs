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
}
