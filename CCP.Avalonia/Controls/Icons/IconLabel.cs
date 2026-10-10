using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// Icon + text from one label string, e.g. a Core loc value "⚙ System":
    ///
    ///   &lt;fx:IconLabel Key="section_system"/&gt;        (re-splits on a language change)
    ///   IconLabel.ForKey("btn_reroll_with_count", n)   (code-built; Loc.GetF, then split)
    ///   &lt;fx:IconLabel Text="..." Kind="Play"/&gt;       (Kind overrides the mapped icon)
    ///
    /// Each leading icon cluster becomes an <see cref="IconGlyph"/> ("⭐⭐ Hard" draws two stars); a
    /// trailing mirror of the first icon draws it on both sides. An unmapped prefix keeps its emoji
    /// text (IconGlyph fallback), and a string without one shows text only. Font size, weight and
    /// Foreground inherit; the icon defaults to the inherited font size.
    /// </summary>
    public sealed class IconLabel : StackPanel
    {
        public static readonly StyledProperty<string?> KeyProperty =
            AvaloniaProperty.Register<IconLabel, string?>(nameof(Key));
        public static readonly StyledProperty<string?> TextProperty =
            AvaloniaProperty.Register<IconLabel, string?>(nameof(Text));
        public static readonly StyledProperty<IconKind?> KindProperty =
            AvaloniaProperty.Register<IconLabel, IconKind?>(nameof(Kind));
        public static readonly StyledProperty<double> IconSizeProperty =
            AvaloniaProperty.Register<IconLabel, double>(nameof(IconSize), double.NaN);

        public string? Key { get => GetValue(KeyProperty); set => SetValue(KeyProperty, value); }
        public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
        public IconKind? Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
        public double IconSize { get => GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }

        /// <summary>Loc.GetF arguments for <see cref="Key"/> (code-built labels).</summary>
        public object[]? Args { get; set; }

        /// <summary>The text shown after the icon(s).</summary>
        public TextBlock Label { get; } = new() { VerticalAlignment = VerticalAlignment.Center };

        public IconLabel()
        {
            Orientation = Orientation.Horizontal;
            Spacing = 6;
            Rebuild();
        }

        public static IconLabel ForKey(string key, params object[] args) => new() { Args = args.Length > 0 ? args : null, Key = key };

        /// <summary>The source string: Text, else the loc value of Key.</summary>
        public string Source => Text ?? (string.IsNullOrEmpty(Key) ? "" : Args is { } a ? Loc.GetF(Key, a) : Loc.Get(Key));

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == KeyProperty || change.Property == TextProperty || change.Property == KindProperty
                || change.Property == IconSizeProperty || change.Property == TextElement.FontSizeProperty) Rebuild();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;   // P41: detach below
            Rebuild();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnLanguageChanged(object? sender, EventArgs e) => Rebuild();

        private void Rebuild()
        {
            var split = IconText.Split(Source);
            double size = double.IsNaN(IconSize) ? GetValue(TextElement.FontSizeProperty) : IconSize;
            Children.Clear();
            var icons = split.Icons.Length == 0 && Kind.HasValue ? new[] { "" } : split.Icons;
            foreach (var c in icons) Children.Add(Glyph(c, size));
            Label.Text = split.Rest;
            Label.IsVisible = split.Rest.Length > 0;
            Children.Add(Label);
            if (split.Mirror) Children.Add(Glyph(split.Icons[0], size));
        }

        private IconGlyph Glyph(string cluster, double size)
        {
            var g = new IconGlyph { Size = size, VerticalAlignment = VerticalAlignment.Center };
            if (Kind is { } k) g.Kind = k;
            else g.Emoji = cluster;
            return g;
        }
    }
}
