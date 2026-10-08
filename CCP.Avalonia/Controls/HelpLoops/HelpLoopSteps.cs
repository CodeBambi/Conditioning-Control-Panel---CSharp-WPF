using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Controls.HelpLoops
{
    /// <summary>The step chips under a loop (WPF Controls/HelpLoops/HelpLoopSteps.cs): one per
    /// <see cref="HelpLoopStep"/>, localized, the current one lit in the accent. Brushes swap only
    /// when a chip's state flips, not every frame.</summary>
    public sealed class HelpLoopSteps : WrapPanel
    {
        private static readonly IBrush OffFill = LoopPalette.Solid("#150f26");
        private static readonly IBrush OnFill = LoopPalette.Solid("#2b1838");
        private static readonly IBrush OffBorder = LoopPalette.Solid("#342a55");
        private static readonly IBrush OffText = LoopPalette.Solid("#a497c4");
        private static readonly IBrush OnText = LoopPalette.Solid("#f3ecff");

        private readonly List<(Border Chip, TextBlock Text, HelpLoopStep Step)> _chips = new();
        private readonly IBrush _accent;

        public HelpLoopSteps(HelpLoopView view, IBrush accent)
        {
            _accent = accent;
            Orientation = Orientation.Horizontal;
            Margin = new Thickness(12, 8, 12, 0);
            foreach (var step in view.Scene.Steps)
            {
                var text = new TextBlock { FontSize = 12, Foreground = OffText, TextWrapping = TextWrapping.NoWrap };
                text.Bind(TextBlock.TextProperty, new Binding($"[{step.LocKey}]")
                {
                    Source = LocalizationManager.Instance,
                    Mode = BindingMode.OneWay,
                });
                var chip = new Border
                {
                    Background = OffFill,
                    BorderBrush = OffBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 2, 8, 3),
                    Margin = new Thickness(0, 0, 6, 6),
                    Child = text,
                };
                Children.Add(chip);
                _chips.Add((chip, text, step));
            }
            view.TimeChanged += Update;
            Update(view.CurrentTime);
        }

        /// <summary>True when the chip for <paramref name="index"/> is lit (tests read this).</summary>
        internal bool IsLit(int index) => ReferenceEquals(_chips[index].Chip.BorderBrush, _accent);

        private void Update(double t)
        {
            foreach (var (chip, text, step) in _chips)
            {
                bool on = t >= step.StartMs && t < step.EndMs;
                var border = on ? _accent : OffBorder;
                if (ReferenceEquals(chip.BorderBrush, border)) continue;
                chip.BorderBrush = border;
                chip.Background = on ? OnFill : OffFill;
                text.Foreground = on ? OnText : OffText;
            }
        }
    }
}
