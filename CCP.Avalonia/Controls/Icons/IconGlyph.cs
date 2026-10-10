using System;
using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// THE way the Avalonia head shows an icon (decision 2026-10-10, Fluent UI System Icons):
    ///
    ///   &lt;fx:IconGlyph Kind="LockClosed" Variant="Filled" Size="20"/&gt;
    ///
    /// The only type that hosts the icon package (FluentIcons.Avalonia SymbolIcon). A plain Control,
    /// not a templated subclass: a property-only ControlTheme on a templated control draws nothing
    /// (CLAUDE.md trap 4). Foreground is inherited, so icons follow mod accents; set it only when the
    /// colour means something (IconBrushes). Decorative by default (AccessibilityView Raw); setting
    /// AutomationProperties.Name makes it Content. <see cref="Emoji"/> resolves a data-driven glyph
    /// through <see cref="IconMap"/>; an unmapped one is drawn as the emoji text, never lost.
    /// Never put an IconGlyph instance in a Setter or a shared resource: a Control has one parent.
    /// </summary>
    public sealed class IconGlyph : Control
    {
        public static readonly StyledProperty<IconKind> KindProperty =
            AvaloniaProperty.Register<IconGlyph, IconKind>(nameof(Kind), IconKind.Circle);
        public static readonly StyledProperty<IconVariant> VariantProperty =
            AvaloniaProperty.Register<IconGlyph, IconVariant>(nameof(Variant));
        public static readonly StyledProperty<double> SizeProperty =
            AvaloniaProperty.Register<IconGlyph, double>(nameof(Size), 16);
        public static readonly StyledProperty<IconBrand> BrandProperty =
            AvaloniaProperty.Register<IconGlyph, IconBrand>(nameof(Brand));
        public static readonly StyledProperty<string?> EmojiProperty =
            AvaloniaProperty.Register<IconGlyph, string?>(nameof(Emoji));
        public static readonly StyledProperty<IBrush?> ForegroundProperty =
            TextElement.ForegroundProperty.AddOwner<IconGlyph>();

        public IconKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
        public IconVariant Variant { get => GetValue(VariantProperty); set => SetValue(VariantProperty, value); }
        public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
        public IconBrand Brand { get => GetValue(BrandProperty); set => SetValue(BrandProperty, value); }
        public string? Emoji { get => GetValue(EmojiProperty); set => SetValue(EmojiProperty, value); }
        public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

        /// <summary>True when <see cref="Emoji"/> had no mapping and the emoji text is drawn instead.</summary>
        public bool IsFallback { get; private set; }

        private static readonly ConcurrentDictionary<string, byte> LoggedMisses = new();
        private Control? _child;
        private IDisposable? _brush;

        static IconGlyph()
        {
            AutomationProperties.AccessibilityViewProperty.OverrideDefaultValue<IconGlyph>(AccessibilityView.Raw);
            AffectsMeasure<IconGlyph>(SizeProperty);
        }

        public IconGlyph() => Rebuild();

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == EmojiProperty) ResolveEmoji();
            else if (change.Property == KindProperty || change.Property == VariantProperty
                     || change.Property == SizeProperty || change.Property == BrandProperty) Rebuild();
            else if (change.Property == AutomationProperties.NameProperty)
                SetCurrentValue(AutomationProperties.AccessibilityViewProperty,
                    string.IsNullOrEmpty(change.GetNewValue<string?>()) ? AccessibilityView.Raw : AccessibilityView.Content);
        }

        private void ResolveEmoji()
        {
            _brush?.Dispose();
            _brush = null;
            var e = Emoji;
            if (string.IsNullOrEmpty(e)) { IsFallback = false; Rebuild(); return; }
            if (IconMap.TryGet(e, out var hit))
            {
                IsFallback = false;
                SetCurrentValue(BrandProperty, hit.Brand);
                SetCurrentValue(VariantProperty, hit.Variant);
                SetCurrentValue(KindProperty, hit.Kind);
                // The emoji carried the colour; keep it unless the caller set one.
                if (hit.Brush != null && !IsSet(ForegroundProperty))
                    _brush = Bind(ForegroundProperty, this.GetResourceObservable(hit.Brush), BindingPriority.Style);
            }
            else
            {
                IsFallback = true;
                if (LoggedMisses.TryAdd(e, 0)) Log.Debug("IconGlyph: no icon mapped for {Emoji}; drawing the emoji", e);
            }
            Rebuild();
        }

        private void Rebuild()
        {
            Control next;
            double size = Size;
            if (IsFallback)
                next = _child is TextBlock t0 ? t0 : new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            else if (Brand == IconBrand.Spiral)
                next = _child is Path p0 ? p0 : new Path { Data = SpiralGlyph.UnitSpiral, Stretch = Stretch.Uniform, StrokeLineCap = PenLineCap.Round };
            else
                next = _child is FluentIcons.Avalonia.SymbolIcon s0 ? s0 : new FluentIcons.Avalonia.SymbolIcon();

            switch (next)
            {
                case TextBlock t:
                    t.Text = Emoji;
                    t.FontSize = size * 0.85;
                    break;
                case Path p:
                    p.Width = p.Height = size;
                    p.StrokeThickness = System.Math.Max(1, size / (Variant == IconVariant.Filled ? 7 : 10));
                    p[!Shape.StrokeProperty] = this[!ForegroundProperty];
                    break;
                case FluentIcons.Avalonia.SymbolIcon s:
                    s.Symbol = Kind;
                    s.IconVariant = Variant;
                    s.FontSize = size;
                    s.Width = s.Height = size;
                    break;
            }
            if (!ReferenceEquals(next, _child))
            {
                if (_child != null) { VisualChildren.Remove(_child); LogicalChildren.Remove(_child); }
                _child = next;
                LogicalChildren.Add(next);
                VisualChildren.Add(next);
            }
            InvalidateMeasure();
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new ControlAutomationPeer(this);
    }
}
