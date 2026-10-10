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
    /// The dot row and the hold (owner, 2026-10-07: "the pills under the slideshow should just be
    /// dots"). One small round dot per card in the cycle, raised by the depth law and tinted with
    /// the card's hue; the current one sits pressed in, widens into a short capsule and fills left
    /// to right as its hold runs. The fill IS the clock: its animation ending is what moves the
    /// deck on, so the bar and the change can never disagree, and pausing the bar pauses the deck.
    /// A dot's tooltip and automation name are the card's name.
    /// </summary>
    public sealed partial class BillboardCardHost
    {
        private static readonly Color ChipPlate = Color.FromRgb(0x12, 0x13, 0x27);

        private ScaleTransform? _fill;
        private FrameworkElement? _currentChip;
        private double _holdProgress;
        private int _holdToken;
        private bool _holdRunning;

        /// <summary>How far the current hold has run (0..1). Tests and the desk read it.</summary>
        internal double HoldProgress => _fill?.ScaleX ?? _holdProgress;

        /// <summary>The dots on screen, in deck order.</summary>
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

        /// <summary>A resting dot's size in px (round).</summary>
        internal const double DotPx = 8;

        /// <summary>The current card's dot widens into a capsule this long; the hold fills it.</summary>
        internal const double DotCurrentPx = 26;

        /// <summary>Hover grows a dot a touch.</summary>
        internal const double DotHoverScale = 1.3;

        /// <summary>Clear room around a dot so an 8 px target is still easy to hit.</summary>
        private const double DotPadX = 5, DotPadY = 6;

        private Button BuildChip(DeckCard card, bool current, out ScaleTransform fill)
        {
            var hue = HueOf(card);
            var tint = ChipTint(card) ?? hue;
            double w = current ? DotCurrentPx : DotPx;

            // The fill (current only): the hue running left to right over a dim track of itself.
            fill = new ScaleTransform(0, 1);
            var fillRect = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(0xee, tint.R, tint.G, tint.B)),
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

            // Resting dots wear their card's hue mixed into the plate; the current track is darker.
            var rest = Blend(ChipPlate, tint, current ? 0.22 : 0.62);
            var hover = Blend(ChipPlate, tint, 0.9);
            var faceFill = new SolidColorBrush(rest);
            var face = new Border
            {
                Width = w,
                Height = DotPx,
                CornerRadius = new CornerRadius(DotPx / 2),
                BorderThickness = new Thickness(1),
                Background = faceFill,
                Child = inner,
                RenderTransform = new TranslateTransform(0, current ? DepthRules.ActiveSinkPx : 0),
            };
            if (current) face.BorderBrush = new SolidColorBrush(Blend(tint, Colors.White, 0.25));
            else face.SetResourceReference(Border.BorderBrushProperty, "DepthRaisedBevel");
            face.SizeChanged += (_, _) =>
            {
                if (face.ActualWidth <= 0) return;
                var r = new RectangleGeometry(new Rect(0, 0, face.ActualWidth, face.ActualHeight), face.ActualHeight / 2, face.ActualHeight / 2);
                r.Freeze();
                inner.Clip = r;
            };

            // Depth law: a raised dot carries its drop band; the current one sits pressed in.
            var drop = new Border
            {
                Width = w,
                Height = DotPx,
                CornerRadius = new CornerRadius(DotPx / 2),
                Margin = new Thickness(0, DepthRules.RaisedPx, 0, -DepthRules.RaisedPx),
                IsHitTestVisible = false,
                Visibility = current ? Visibility.Hidden : Visibility.Visible,
            };
            drop.SetResourceReference(Border.BackgroundProperty, "DepthDropBand");

            var dot = new Grid
            {
                Margin = new Thickness(DotPadX, DotPadY, DotPadX, DotPadY),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
            };
            dot.Children.Add(drop);
            dot.Children.Add(face);

            // The root keeps the landing "tick" and the snooze pop (Tick / PopCurrentChip scale it);
            // the dot inside it takes the hover growth, so the two never fight.
            var root = new Grid
            {
                Background = Brushes.Transparent,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
            };
            root.Children.Add(dot);

            var name = ChipLabel(card);
            if (string.IsNullOrWhiteSpace(name)) name = card.Spec.Title ?? string.Empty;
            var button = new Button
            {
                Template = BareTemplate(),
                Content = root,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 0, DepthRules.RaisedPx),
                Focusable = true,
                ToolTip = name,
            };
            AutomationProperties.SetName(button, name);

            var grow = (ScaleTransform)dot.RenderTransform;
            void Hover(bool on)
            {
                double to = on ? DotHoverScale : 1;
                if (MotionFx.AllowTransitions)
                {
                    var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(on ? 140 : 180))
                    {
                        EasingFunction = on ? CubicBezierEase.Thud() : new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    };
                    grow.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                    grow.BeginAnimation(ScaleTransform.ScaleYProperty, a);
                }
                else
                {
                    grow.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    grow.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    grow.ScaleX = grow.ScaleY = to;
                }
                if (!current)
                {
                    faceFill.Color = on ? hover : rest;
                    // Depth law: hover lifts a raised dot.
                    ((TranslateTransform)face.RenderTransform).Y = on ? -DepthRules.HoverLiftPx / 2 : 0;
                }
            }
            button.MouseEnter += (_, _) => Hover(true);
            button.MouseLeave += (_, _) => Hover(false);
            button.IsKeyboardFocusedChanged += (_, _) =>
            {
                if (button.IsKeyboardFocused) face.BorderBrush = Brushes.White;
                else if (current) face.BorderBrush = new SolidColorBrush(Blend(tint, Colors.White, 0.25));
                else face.SetResourceReference(Border.BorderBrushProperty, "DepthRaisedBevel");
            };

            // Landing: the current dot opens from a dot into its capsule.
            if (current && MotionFx.AllowTransitions)
            {
                var open = new DoubleAnimation(DotPx, DotCurrentPx, TimeSpan.FromMilliseconds(260)) { EasingFunction = CubicBezierEase.Thud() };
                face.BeginAnimation(WidthProperty, open);
                drop.BeginAnimation(WidthProperty, open);
            }
            return button;
        }

        private static Color Blend(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromRgb(
                (byte)Math.Round(a.R + (b.R - a.R) * t),
                (byte)Math.Round(a.G + (b.G - a.G) * t),
                (byte)Math.Round(a.B + (b.B - a.B) * t));
        }

        /// <summary>The dot's own colour by kind: the board pink, Basic gold, Prime cyan (else the card hue).</summary>
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

        /// <summary>The current dot's small "tick" on landing (scale to 1.12 and back, 300 ms).</summary>
        private static void Tick(FrameworkElement chip)
        {
            if (chip is not Button { Content: FrameworkElement root } || root.RenderTransform is not ScaleTransform s) return;
            var bump = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(300) };
            bump.KeyFrames.Add(new EasingDoubleKeyFrame(1.12, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            bump.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300)), CubicBezierEase.Thud()));
            s.BeginAnimation(ScaleTransform.ScaleXProperty, bump);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, bump);
        }

        /// <summary>Snooze: the current dot pops off the row (scale to 0 and fade, 320 ms ease-in).</summary>
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
