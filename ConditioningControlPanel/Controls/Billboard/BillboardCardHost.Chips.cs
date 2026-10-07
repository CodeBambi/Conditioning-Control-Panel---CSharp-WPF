using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ConditioningControlPanel.Controls.Depth;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;

namespace ConditioningControlPanel.Controls.Billboard
{
    /// <summary>
    /// The chip row and the hold. One labelled chip per card in the cycle, raised by the depth law;
    /// the current one sits pressed in and fills left to right as its hold runs. The fill IS the
    /// clock: its animation ending is what moves the deck on, so the bar and the change can never
    /// disagree, and pausing the bar pauses the deck.
    /// </summary>
    public sealed partial class BillboardCardHost
    {
        private static readonly Color ChipPlate = Color.FromRgb(0x12, 0x13, 0x27);
        private static readonly Color ChipDim = Color.FromRgb(0x9d, 0x9b, 0xc6);

        private ScaleTransform? _fill;
        private FrameworkElement? _currentChip;
        private double _holdProgress;
        private int _holdToken;
        private bool _holdRunning;

        /// <summary>How far the current hold has run (0..1). Tests and the desk read it.</summary>
        internal double HoldProgress => _fill?.ScaleX ?? _holdProgress;

        /// <summary>The chips on screen, in deck order.</summary>
        internal UIElementCollection ChipButtons => _chips.Children;

        private void RebuildChips()
        {
            StopHold();
            _chips.Children.Clear();
            _fill = null;
            _currentChip = null;
            var cards = _deck.Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                int index = i;
                var card = cards[i];
                bool current = i == _deck.Index;
                var chip = BuildChip(card, current, out var fill);
                chip.Click += (_, _) => OnChipClick(index);
                _chips.Children.Add(chip);
                if (current)
                {
                    _fill = fill;
                    _currentChip = chip;
                }
            }
            if (_currentChip != null && MotionFx.AllowTransitions) Tick(_currentChip);
        }

        private Button BuildChip(DeckCard card, bool current, out ScaleTransform fill)
        {
            var hue = HueOf(card);
            var tint = ChipTint(card) ?? (current ? Colors.White : ChipDim);

            var label = new TextBlock
            {
                Text = ChipLabel(card),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(current && ChipTint(card) == null ? Color.FromRgb(0xec, 0xea, 0xff) : tint),
                Margin = new Thickness(12, 4, 12, 5),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 140,
            };

            fill = new ScaleTransform(0, 1);
            var fillRect = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(0x38, hue.R, hue.G, hue.B)),
                RenderTransformOrigin = new Point(0, 0.5),
                RenderTransform = fill,
                IsHitTestVisible = false,
                Visibility = current ? Visibility.Visible : Visibility.Collapsed,
            };
            var sheen = new Border { IsHitTestVisible = false };
            sheen.SetResourceReference(Border.BackgroundProperty, current ? "DepthPressedShade" : "DepthRaisedSheen");

            var inner = new Grid();
            inner.Children.Add(fillRect);
            inner.Children.Add(sheen);
            inner.Children.Add(label);

            var face = new Border
            {
                CornerRadius = new CornerRadius(999),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(ChipPlate),
                Child = inner,
                ClipToBounds = true,
                RenderTransform = new TranslateTransform(0, current ? DepthRules.ActiveSinkPx : 0),
            };
            if (current) face.BorderBrush = new SolidColorBrush(hue);
            else face.SetResourceReference(Border.BorderBrushProperty, "DepthRaisedBevel");
            face.SizeChanged += (_, _) =>
            {
                if (face.ActualWidth <= 0) return;
                var r = new RectangleGeometry(new Rect(0, 0, face.ActualWidth, face.ActualHeight), face.ActualHeight / 2, face.ActualHeight / 2);
                r.Freeze();
                inner.Clip = r;
            };

            var drop = new Border
            {
                CornerRadius = new CornerRadius(999),
                Margin = new Thickness(0, DepthRules.RaisedPx, 0, -DepthRules.RaisedPx),
                IsHitTestVisible = false,
                Visibility = current ? Visibility.Hidden : Visibility.Visible,
            };
            drop.SetResourceReference(Border.BackgroundProperty, "DepthDropBand");

            var root = new Grid { RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
            root.Children.Add(drop);
            root.Children.Add(face);

            var button = new Button
            {
                Template = BareTemplate(),
                Content = root,
                Cursor = Cursors.Hand,
                Margin = new Thickness(3, 0, 3, DepthRules.RaisedPx),
                Focusable = true,
                ToolTip = card.Spec.Title,
            };
            AutomationProperties.SetName(button, string.IsNullOrEmpty(card.Spec.Title) ? label.Text : card.Spec.Title);
            if (!current)
            {
                // Depth law: hover lifts a raised chip.
                button.MouseEnter += (_, _) => ((TranslateTransform)face.RenderTransform).Y = -DepthRules.HoverLiftPx / 2;
                button.MouseLeave += (_, _) => ((TranslateTransform)face.RenderTransform).Y = 0;
            }
            button.IsKeyboardFocusedChanged += (_, _) =>
            {
                if (button.IsKeyboardFocused) face.BorderBrush = Brushes.White;
                else if (current) face.BorderBrush = new SolidColorBrush(hue);
                else face.SetResourceReference(Border.BorderBrushProperty, "DepthRaisedBevel");
            };
            return button;
        }

        /// <summary>The chip's own colour by kind: the board pink, Basic gold, Prime cyan.</summary>
        internal static Color? ChipTint(DeckCard card) => card.Spec.Kind switch
        {
            BillboardCardKind.Board => Color.FromRgb(0xff, 0x4f, 0xa8),
            BillboardCardKind.Showcase when card.Spec.Badge == BillboardBadge.Prime => Color.FromRgb(0x5f, 0xe3, 0xff),
            BillboardCardKind.Showcase => Color.FromRgb(0xff, 0xc9, 0x4a),
            _ => null,
        };

        private void OnChipClick(int index)
        {
            if (_folding) return;
            if (index == _deck.Index) { RestartHold(); UpdateRunning(); return; }
            Show(_deck.Select(index), animate: true, sound: true);
        }

        /// <summary>The current chip's small "tick" on landing (scale to 1.12 and back, 300 ms).</summary>
        private static void Tick(FrameworkElement chip)
        {
            if (chip is not Button { Content: FrameworkElement root } || root.RenderTransform is not ScaleTransform s) return;
            var bump = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(300) };
            bump.KeyFrames.Add(new EasingDoubleKeyFrame(1.12, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            bump.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300)), CubicBezierEase.Thud()));
            s.BeginAnimation(ScaleTransform.ScaleXProperty, bump);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, bump);
        }

        /// <summary>Snooze: the current chip pops off the row (scale to 0 and fade, 320 ms ease-in).</summary>
        private void PopCurrentChip()
        {
            if (_currentChip is not Button { Content: FrameworkElement root } || !MotionFx.AllowTransitions) return;
            if (root.RenderTransform is not ScaleTransform s) return;
            var dur = TimeSpan.FromMilliseconds(DashboardBillboard.SnoozeFoldMs);
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };
            s.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, dur) { EasingFunction = ease });
            s.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0, dur) { EasingFunction = ease });
            _currentChip.BeginAnimation(OpacityProperty, new DoubleAnimation(0, dur) { EasingFunction = ease });
        }

        // ---- the hold ----------------------------------------------------------------------------

        /// <summary>A new card: the hold starts from nothing.</summary>
        private void RestartHold()
        {
            StopHold();
            _holdProgress = 0;
            if (_fill != null) _fill.ScaleX = 0;
        }

        /// <summary>Runs the fill from where it stands to full over the time that is left.</summary>
        private void ResumeHold()
        {
            if (_holdRunning || _fill == null) return;
            _holdRunning = true;
            int token = ++_holdToken;
            double from = Math.Clamp(_holdProgress, 0, 1);
            var anim = new DoubleAnimation(from, 1, TimeSpan.FromMilliseconds(Math.Max(1, DashboardBillboard.RemainingMs(from))))
            {
                FillBehavior = FillBehavior.HoldEnd,
            };
            anim.Completed += (_, _) =>
            {
                if (token != _holdToken || !_holdRunning) return;
                _holdRunning = false;
                _holdProgress = 1;
                Advance();
            };
            _fill.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        }

        /// <summary>Holds the fill where it is (hover, focus, another tab, Motion Off).</summary>
        private void PauseHold()
        {
            if (!_holdRunning) return;
            _holdRunning = false;
            _holdToken++;
            if (_fill == null) return;
            double now = _fill.ScaleX;
            _fill.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _fill.ScaleX = now;
            _holdProgress = now;
        }

        private void StopHold()
        {
            _holdRunning = false;
            _holdToken++;
            _fill?.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        }
    }
}
