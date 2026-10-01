using System.Windows;
using ConditioningControlPanel.Controls;

namespace ConditioningControlPanel.Features
{
    public partial class FeatureCard
    {
        /// <summary>
        /// The Super effect this tile carries, as a <c>SuperEffect</c> name ("Vortex"). Set in XAML on
        /// the eight base-effect tiles; empty everywhere else. The tile then wears a
        /// <see cref="SuperTileBadge"/> in its bottom-right corner. Nothing about the base effect changes.
        /// </summary>
        public static readonly DependencyProperty SuperProperty =
            DependencyProperty.Register(nameof(Super), typeof(string), typeof(FeatureCard),
                new PropertyMetadata(null, (d, _) => ((FeatureCard)d).ApplySuper()));

        public string? Super
        {
            get => (string?)GetValue(SuperProperty);
            set => SetValue(SuperProperty, value);
        }

        private SuperTileBadge? _superBadge;

        private void ApplySuper()
        {
            if (_superBadge != null)
            {
                ContentRoot.Children.Remove(_superBadge);
                _superBadge = null;
            }
            if (SuperTileBadge.Parse(Super) is not { } effect) return;
            _superBadge = new SuperTileBadge(effect, compact: false)
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 8, 8),
            };
            // A locked click opens the feature, the way a plain click on the tile does.
            _superBadge.Switch.LockedClick += (_, e) =>
            {
                e.Handled = true;
                RaiseEvent(new RoutedEventArgs(ClickEvent, this));
            };
            ContentRoot.Children.Add(_superBadge);
        }
    }
}
