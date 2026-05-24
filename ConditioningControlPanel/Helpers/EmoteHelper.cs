using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ConditioningControlPanel.Helpers
{
    /// <summary>
    /// Attached-property helpers used by the remote-control emote picker's
    /// custom-text TextBox watermark. The watermark template binds to these
    /// two attached properties via HintOrPlaceholderConverter, which prefers
    /// the hint when non-empty and falls back to the placeholder otherwise.
    ///
    /// Why split into two properties:
    ///   - LastSentEmoteHint is session-only state, updated by code-behind
    ///     after each successful send. Not persisted.
    ///   - PlaceholderText is the localized default ("Type a message…"),
    ///     set once in XAML.
    ///
    /// The TextBox watermark is the only consumer for now, but the helpers
    /// are generic enough to reuse elsewhere if another picker wants the
    /// same ghost-of-last-sent pattern.
    /// </summary>
    public static class EmoteHelper
    {
        public static readonly DependencyProperty LastSentEmoteHintProperty =
            DependencyProperty.RegisterAttached(
                "LastSentEmoteHint",
                typeof(string),
                typeof(EmoteHelper),
                new PropertyMetadata(string.Empty));

        public static string GetLastSentEmoteHint(DependencyObject d)
            => (string)d.GetValue(LastSentEmoteHintProperty);

        public static void SetLastSentEmoteHint(DependencyObject d, string value)
            => d.SetValue(LastSentEmoteHintProperty, value ?? string.Empty);

        public static readonly DependencyProperty PlaceholderTextProperty =
            DependencyProperty.RegisterAttached(
                "PlaceholderText",
                typeof(string),
                typeof(EmoteHelper),
                new PropertyMetadata(string.Empty));

        public static string GetPlaceholderText(DependencyObject d)
            => (string)d.GetValue(PlaceholderTextProperty);

        public static void SetPlaceholderText(DependencyObject d, string value)
            => d.SetValue(PlaceholderTextProperty, value ?? string.Empty);
    }

    /// <summary>
    /// Picks the first non-empty string. Used by the emote custom-text
    /// watermark to prefer LastSentEmoteHint over PlaceholderText.
    /// </summary>
    public class HintOrPlaceholderConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null) return string.Empty;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] is string s && !string.IsNullOrEmpty(s)) return s;
            }
            return string.Empty;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
