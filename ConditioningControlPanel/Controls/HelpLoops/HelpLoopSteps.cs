using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Controls.HelpLoops
{
    /// <summary>
    /// The three step chips under a help loop. A chip lights (accent border) while the loop's time
    /// is inside its step. Labels bind to <see cref="LocalizationManager"/> so they follow a
    /// language switch like the popover caption did.
    /// </summary>
    public sealed class HelpLoopSteps : WrapPanel
    {
        private static readonly Brush OffFill = LoopPalette.Solid("#150f26");
        private static readonly Brush OnFill = LoopPalette.Solid("#2b1838");
        private static readonly Brush OffBorder = LoopPalette.Solid("#342a55");
        private static readonly Brush OffText = LoopPalette.Solid("#a497c4");
        private static readonly Brush OnText = LoopPalette.Solid("#f3ecff");

        private readonly List<(Border Chip, TextBlock Text, HelpLoopStep Step)> _chips = new();
        private HelpLoopView? _view;
        private Brush _accent = new SolidColorBrush(LoopPalette.DefaultAccent);

        public HelpLoopSteps()
        {
            Orientation = Orientation.Horizontal;
            Margin = new Thickness(12, 8, 12, 0);
            Loaded += (_, _) =>
            {
                _accent = HelpTooltipBuilder.FindThemeResource<Brush>(this, "PinkBrush") ?? _accent;
                if (_view != null) Update(_view.CurrentTime);
            };
        }

        public HelpLoopSteps(HelpLoopView view) : this() => Attach(view);

        /// <summary>Binds the strip to a view (rebuilds on scene change, lights on every frame).</summary>
        public void Attach(HelpLoopView view)
        {
            if (_view != null)
            {
                _view.TimeChanged -= Update;
                _view.SceneChanged -= Rebuild;
            }
            _view = view;
            _view.TimeChanged += Update;
            _view.SceneChanged += Rebuild;
            Rebuild();
        }

        private void Rebuild()
        {
            Children.Clear();
            _chips.Clear();
            var scene = _view?.Scene;
            if (scene == null) return;
            foreach (var step in scene.Steps)
            {
                var text = new TextBlock { FontSize = 12, Foreground = OffText, TextWrapping = TextWrapping.NoWrap };
                text.SetBinding(TextBlock.TextProperty, new Binding($"[{step.LocKey}]")
                {
                    Source = LocalizationManager.Instance,
                    Mode = BindingMode.OneWay
                });
                var chip = new Border
                {
                    Background = OffFill,
                    BorderBrush = OffBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 2, 8, 3),
                    Margin = new Thickness(0, 0, 6, 6),
                    Child = text
                };
                Children.Add(chip);
                _chips.Add((chip, text, step));
            }
            Update(_view!.CurrentTime);
        }

        private void Update(double t)
        {
            foreach (var (chip, text, step) in _chips)
            {
                bool on = t >= step.StartMs && t < step.EndMs;
                var border = on ? _accent : OffBorder;
                if (!ReferenceEquals(chip.BorderBrush, border))
                {
                    chip.BorderBrush = border;
                    chip.Background = on ? OnFill : OffFill;
                    text.Foreground = on ? OnText : OffText;
                }
            }
        }
    }
}
