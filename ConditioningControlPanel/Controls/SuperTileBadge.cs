using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// What a dashboard tile wears for its Super effect: the <see cref="SuperSwitch"/> and, for a
    /// free account in the effect's free week, the mint "free this week" tag (mockup `.week`).
    /// Full tiles stack the tag over the switch; a split half (<see cref="Compact"/>) shrinks the
    /// switch to 80% and pins a short "free" tag on its corner so the pair stays inside its half.
    /// </summary>
    public sealed class SuperTileBadge : Grid
    {
        private static readonly Color Mint = Color.FromRgb(0x5F, 0xFF, 0xD0);

        public SuperSwitch Switch { get; } = new();

        private readonly Border _tag;
        private readonly TextBlock _tagText;
        private readonly bool _compact;

        public SuperTileBadge(SuperEffect effect, bool compact)
        {
            _compact = compact;
            Switch.Effect = effect;
            Panel.SetZIndex(this, 12);

            _tagText = new TextBlock
            {
                FontSize = compact ? 8.5 : 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Mint),
            };
            _tag = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = compact ? new Thickness(4, 0, 4, 1) : new Thickness(6, 1, 6, 2),
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x0B, 0x2A, 0x24)),
                BorderBrush = new SolidColorBrush(Mint),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
                Child = _tagText,
            };

            if (compact)
            {
                Switch.LayoutTransform = new ScaleTransform(0.8, 0.8);
                _tag.HorizontalAlignment = HorizontalAlignment.Right;
                _tag.VerticalAlignment = VerticalAlignment.Top;
                _tag.Margin = new Thickness(0, -10, -6, 0);
                Children.Add(Switch);
                Children.Add(_tag);
            }
            else
            {
                var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
                _tag.HorizontalAlignment = HorizontalAlignment.Right;
                _tag.Margin = new Thickness(0, 0, 0, 4);
                Switch.HorizontalAlignment = HorizontalAlignment.Right;
                stack.Children.Add(_tag);
                stack.Children.Add(Switch);
                Children.Add(stack);
            }

            Loaded += (_, _) =>
            {
                SuperAccess.Changed += OnChanged;
                SuperPreview.StateChanged += Refresh;
                Refresh();
            };
            Unloaded += (_, _) =>
            {
                SuperAccess.Changed -= OnChanged;
                SuperPreview.StateChanged -= Refresh;
            };
        }

        private void OnChanged(SuperEffect e)
        {
            if (e != Switch.Effect) return;
            if (Dispatcher.CheckAccess()) Refresh();
            else Dispatcher.BeginInvoke(Refresh);
        }

        /// <summary>The tag shows to a free account whose effect is this week's, until the try is spent.</summary>
        public void Refresh()
        {
            var effect = Switch.Effect;
            bool show = !TierGate.HasPremium && SuperPreview.ThisWeek == effect
                        && (SuperPreview.Trying == effect || !SuperPreview.UsedThisWeek);
            _tagText.Text = Loc.Get(_compact ? "super_free_week_short" : "super_free_week");
            _tag.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Parse a tile's <c>Super</c> attribute. Null for empty or unknown names.</summary>
        public static SuperEffect? Parse(string? name)
            => !string.IsNullOrWhiteSpace(name) && Enum.TryParse<SuperEffect>(name, ignoreCase: true, out var e) ? e : null;
    }
}
