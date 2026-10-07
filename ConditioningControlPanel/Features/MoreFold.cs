using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// Folds a panel's power-user rows behind one "More options" link (house rule: lead with the
    /// capability, keep the knobs one click away). The rows are this panel's children in XAML.
    /// Code-only on purpose: a UserControl with its own XAML is a namescope, and WPF then refuses
    /// x:Name on the host's rows inside it. Closed by default; the host sets <see cref="IsOpen"/>
    /// when a row inside is off its default, so a changed setting is never hidden.
    /// </summary>
    public class MoreFold : StackPanel
    {
        public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
            nameof(IsOpen), typeof(bool), typeof(MoreFold),
            new PropertyMetadata(false, (d, _) => ((MoreFold)d).Apply()));

        public bool IsOpen
        {
            get => (bool)GetValue(IsOpenProperty);
            set => SetValue(IsOpenProperty, value);
        }

        private readonly TextBlock _toggle;
        private readonly Run _label = new();

        public MoreFold()
        {
            var link = new Hyperlink(_label) { TextDecorations = null };
            link.SetResourceReference(TextElement.ForegroundProperty, "TextMutedBrush");
            link.Click += (_, _) => IsOpen = !IsOpen;
            link.MouseEnter += (_, _) => link.TextDecorations = TextDecorations.Underline;
            link.MouseLeave += (_, _) => link.TextDecorations = null;

            _toggle = new TextBlock(link)
            {
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 6, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.Hand,
            };
            Children.Add(_toggle);
            Apply();
        }

        protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
        {
            base.OnVisualChildrenChanged(visualAdded, visualRemoved);
            if (visualAdded is UIElement el && !ReferenceEquals(el, _toggle))
                el.Visibility = IsOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Apply()
        {
            foreach (UIElement child in Children)
                if (!ReferenceEquals(child, _toggle))
                    child.Visibility = IsOpen ? Visibility.Visible : Visibility.Collapsed;

            // A binding, not a string: a language switch must relabel it live.
            BindingOperations.SetBinding(_label, Run.TextProperty,
                new Binding($"[{(IsOpen ? "fold_fewer_options" : "fold_more_options")}]")
                { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
        }
    }
}
