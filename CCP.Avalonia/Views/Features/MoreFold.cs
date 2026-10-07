using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// WPF Features/MoreFold.cs: a panel's power-user rows behind one "More options" link. The rows
    /// are this panel's children in XAML (a plain StackPanel, so the host's x:Name rows still
    /// resolve). Closed by default; the host sets <see cref="IsOpen"/> when a row inside is off its
    /// default, so a changed setting is never hidden.
    /// </summary>
    public class MoreFold : StackPanel
    {
        public static readonly StyledProperty<bool> IsOpenProperty = AvaloniaProperty.Register<MoreFold, bool>(nameof(IsOpen));

        public bool IsOpen
        {
            get => GetValue(IsOpenProperty);
            set => SetValue(IsOpenProperty, value);
        }

        internal TextBlock Toggle { get; }

        public MoreFold()
        {
            Toggle = new TextBlock
            {
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 6, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = new Cursor(StandardCursorType.Hand),
                Background = Brushes.Transparent,
            };
            Toggle.Bind(TextBlock.ForegroundProperty, Toggle.GetResourceObservable("TextMutedBrush"));
            Toggle.PointerPressed += (_, e) => { IsOpen = !IsOpen; e.Handled = true; };
            Toggle.PointerEntered += (_, _) => Toggle.TextDecorations = TextDecorations.Underline;
            Toggle.PointerExited += (_, _) => Toggle.TextDecorations = null;
            Children.Add(Toggle);
            Children.CollectionChanged += (_, _) => Apply();
            Apply();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsOpenProperty) Apply();
        }

        private void Apply()
        {
            foreach (var child in Children)
                if (!ReferenceEquals(child, Toggle)) child.IsVisible = IsOpen;
            Toggle.Bind(TextBlock.TextProperty,
                (Binding)new StrExtension(IsOpen ? "fold_fewer_options" : "fold_more_options").ProvideValue(null!));
        }
    }
}
