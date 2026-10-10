using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using ConditioningControlPanel.Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Localization
{
    /// <summary>
    /// {loc:StrBare key}: the same live binding as {loc:Str key}, with the leading icon cluster(s)
    /// (and a trailing mirror) stripped through <see cref="IconText"/>. Use it where the icon is drawn
    /// by a separate fx:IconGlyph, or in tooltips. The JSON keeps its emoji for the WPF head.
    /// </summary>
    public sealed class StrBareExtension : StrExtension
    {
        public StrBareExtension() { }
        public StrBareExtension(string key) : base(key) { }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var value = base.ProvideValue(serviceProvider);
            if (value is Binding b) b.Converter = Bare.Instance;
            return value;
        }

        private sealed class Bare : IValueConverter
        {
            public static readonly Bare Instance = new();
            public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => IconText.Bare(value as string);
            public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
        }
    }
}
