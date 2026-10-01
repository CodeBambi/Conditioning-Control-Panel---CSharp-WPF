using System.Windows;
using ConditioningControlPanel.Controls;

namespace ConditioningControlPanel.Features
{
    public partial class SplitFeatureCard
    {
        /// <summary>Super effect riding half A, as a <c>SuperEffect</c> name. See FeatureCard.Super.</summary>
        public static readonly DependencyProperty SuperAProperty =
            DependencyProperty.Register(nameof(SuperA), typeof(string), typeof(SplitFeatureCard),
                new PropertyMetadata(null, (d, _) => ((SplitFeatureCard)d).ApplySuper(halfA: true)));

        /// <summary>Super effect riding half B, as a <c>SuperEffect</c> name.</summary>
        public static readonly DependencyProperty SuperBProperty =
            DependencyProperty.Register(nameof(SuperB), typeof(string), typeof(SplitFeatureCard),
                new PropertyMetadata(null, (d, _) => ((SplitFeatureCard)d).ApplySuper(halfA: false)));

        public string? SuperA { get => (string?)GetValue(SuperAProperty); set => SetValue(SuperAProperty, value); }
        public string? SuperB { get => (string?)GetValue(SuperBProperty); set => SetValue(SuperBProperty, value); }

        private SuperTileBadge? _superBadgeA, _superBadgeB;

        /// <summary>
        /// The seam runs top-right to bottom-left, so half A owns the top-left corner and half B the
        /// bottom-right. Each switch sits just inside its half's corner, below A's title and "?" and
        /// above B's, at 80% so it stays clear of the seam.
        /// </summary>
        private void ApplySuper(bool halfA)
        {
            ref var slot = ref halfA ? ref _superBadgeA : ref _superBadgeB;
            if (slot != null)
            {
                ContentRoot.Children.Remove(slot);
                slot = null;
            }
            if (Controls.SuperTileBadge.Parse(halfA ? SuperA : SuperB) is not { } effect) return;
            var badge = new SuperTileBadge(effect, compact: true)
            {
                HorizontalAlignment = halfA ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                VerticalAlignment = halfA ? VerticalAlignment.Top : VerticalAlignment.Bottom,
                Margin = halfA ? new Thickness(8, 58, 0, 0) : new Thickness(0, 0, 8, 58),
            };
            badge.Switch.LockedClick += (_, e) =>
            {
                e.Handled = true;
                RaiseEvent(new RoutedEventArgs(halfA ? ClickAEvent : ClickBEvent, this));
            };
            slot = badge;
            ContentRoot.Children.Add(badge);
        }
    }
}
