using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ConditioningControlPanel.Views.Controls
{
    // ------------------------------------------------------------------------
    // Converters. They live here (and are instantiated in the UserControl's own
    // Resources root) because a converter declared inside a nested Grid.Resources
    // is invisible to a DataTemplate — a trap this codebase has hit before.
    // ------------------------------------------------------------------------

    /// <summary>true -> the connected colour, false -> the muted colour. Parameter-free so it can
    /// be reused for dots and chips alike.</summary>
    public sealed class BoolToStatusBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var on = value is bool b && b;
            var key = on ? "SuccessGreenBrush" : "TextDimBrush";
            try
            {
                if (Application.Current?.Resources[key] is System.Windows.Media.Brush brush) return brush;
            }
            catch { }
            return on ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>Empty / whitespace string -> Collapsed.</summary>
    public sealed class EmptyStringToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
