using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Depth;
using ConditioningControlPanel.Motion;
using ConditioningControlPanel.Services.Billboard;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard
{
    /// <summary>
    /// The dot row and the hold (WPF 7.1.5 BillboardDeckView.Chips; owner, 2026-10-07: "the pills
    /// under the slideshow should just be dots"). One small round dot per card in the cycle, raised
    /// by the depth law and tinted with the card's hue; the current one sits pressed in, widens into
    /// a short capsule and fills left to right as its hold runs. The fill IS the clock: its tween
    /// ending is what moves the deck on, so the bar and the change can never disagree, and pausing
    /// the bar pauses the deck. A dot's tooltip and automation name are the card's name.
    /// </summary>
    public sealed partial class BillboardDeckView
    {
        private static readonly Color ChipPlate = Color.FromRgb(0x12, 0x13, 0x27);

        private ScaleTransform? _fill;
        private Control? _currentChip;
        private double _holdProgress;
        private int _holdToken;
        private bool _holdRunning;

        /// <summary>How far the current hold has run (0..1). Tests and the desk read it.</summary>
        internal double HoldProgress => _fill?.ScaleX ?? _holdProgress;

        /// <summary>True while the hold fill runs (tests).</summary>
        internal bool HoldRunning => _holdRunning;

        /// <summary>The dots on screen, in deck order.</summary>
        internal global::Avalonia.Controls.Controls ChipButtons => _chips.Children;

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
            if (_currentChip != null && BoardMotion.AllowTransitions) Tick(_currentChip);
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
                RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                RenderTransform = fill,
                IsHitTestVisible = false,
                IsVisible = current,
            };
            var sheen = new Border { IsHitTestVisible = false };
            Res(sheen, Border.BackgroundProperty, current ? "DepthPressedShade" : "DepthRaisedSheen");

            var inner = new Panel();
            inner.Children.Add(fillRect);
            inner.Children.Add(sheen);

            // Resting dots wear their card's hue mixed into the plate; the current track is darker.
            var rest = Blend(ChipPlate, tint, current ? 0.22 : 0.62);
            var hover = Blend(ChipPlate, tint, 0.9);
            var faceFill = new SolidColorBrush(rest);
            var faceMove = new TranslateTransform(0, current ? DepthRules.ActiveSinkPx : 0);
            var face = new Border
            {
                Width = w,
                Height = DotPx,
                CornerRadius = new CornerRadius(DotPx / 2),
                BorderThickness = new Thickness(1),
                Background = faceFill,
                Child = inner,
                ClipToBounds = true, // the rounded border clips the fill to the capsule
                RenderTransform = faceMove,
            };
            if (current) face.BorderBrush = new SolidColorBrush(Blend(tint, Colors.White, 0.25));
            else Res(face, Border.BorderBrushProperty, "DepthRaisedBevel");

            // Depth law: a raised dot carries its drop band; the current one sits pressed in.
            var drop = new Border
            {
                Width = w,
                Height = DotPx,
                CornerRadius = new CornerRadius(DotPx / 2),
                Margin = new Thickness(0, DepthRules.RaisedPx, 0, -DepthRules.RaisedPx),
                IsHitTestVisible = false,
                Opacity = current ? 0 : 1,
            };
            Res(drop, Border.BackgroundProperty, "DepthDropBand");

            var grow = new ScaleTransform(1, 1);
            var dot = new Panel
            {
                Margin = new Thickness(DotPadX, DotPadY, DotPadX, DotPadY),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = RelativePoint.Center,
                RenderTransform = grow,
            };
            dot.Children.Add(drop);
            dot.Children.Add(face);

            // The root keeps the landing "tick" and the snooze pop (Tick / PopCurrentChip scale it);
            // the dot inside it takes the hover growth, so the two never fight.
            var root = new Panel
            {
                Background = Brushes.Transparent,
                RenderTransformOrigin = RelativePoint.Center,
                RenderTransform = new ScaleTransform(1, 1),
            };
            root.Children.Add(dot);

            var name = ChipLabel(card);
            if (string.IsNullOrWhiteSpace(name)) name = card.Spec.Title ?? string.Empty;
            var button = new Button
            {
                Template = BareTemplate(),
                Content = root,
                Cursor = new Cursor(StandardCursorType.Hand),
                Margin = new Thickness(0, 0, 0, DepthRules.RaisedPx),
                Focusable = true,
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
            };
            ToolTip.SetTip(button, name);
            AutomationProperties.SetName(button, name);

            void Hover(bool on)
            {
                double to = on ? DotHoverScale : 1;
                if (BoardMotion.AllowTransitions)
                    _tw.To(grow, "s", () => grow.ScaleX, v => { grow.ScaleX = v; grow.ScaleY = v; }, null, to,
                        on ? 140 : 180, on ? Easings.Thud : Easings.QuadOut);
                else
                {
                    _tw.Stop(grow, "s");
                    grow.ScaleX = grow.ScaleY = to;
                }
                if (!current)
                {
                    faceFill.Color = on ? hover : rest;
                    // Depth law: hover lifts a raised dot.
                    faceMove.Y = on ? -DepthRules.HoverLiftPx / 2 : 0;
                }
            }
            button.PointerEntered += (_, _) => Hover(true);
            button.PointerExited += (_, _) => Hover(false);
            button.GotFocus += (_, e) => { if (e.NavigationMethod == NavigationMethod.Tab) face.BorderBrush = Brushes.White; };
            button.LostFocus += (_, _) =>
            {
                if (current) face.BorderBrush = new SolidColorBrush(Blend(tint, Colors.White, 0.25));
                else Res(face, Border.BorderBrushProperty, "DepthRaisedBevel");
            };

            // Landing: the current dot opens from a dot into its capsule.
            if (current && BoardMotion.AllowTransitions)
                _tw.To(face, "w", () => face.Width, v => { face.Width = v; drop.Width = v; }, DotPx, DotCurrentPx, 260, Easings.Thud);
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
            Show(_deck.Select(index), animate: true);
        }

        /// <summary>Test seam: a dot pressed.</summary>
        internal void ChipForTests(int index) => OnChipClick(index);

        /// <summary>The current dot's small "tick" on landing (scale to 1.12 and back, 300 ms).</summary>
        private void Tick(Control chip)
        {
            if (chip is not Button { Content: Control root } || root.RenderTransform is not ScaleTransform s) return;
            _tw.To(s, "s", () => s.ScaleX, v => { s.ScaleX = v; s.ScaleY = v; }, 1, 1.12, 120, Easings.QuadOut,
                done: () => _tw.To(s, "s", () => s.ScaleX, v => { s.ScaleX = v; s.ScaleY = v; }, null, 1, 180, Easings.Thud));
        }

        /// <summary>Snooze: the current dot pops off the row (scale to 0 and fade, 320 ms ease-in).</summary>
        private void PopCurrentChip()
        {
            if (_currentChip is not Button { Content: Control root } || !BoardMotion.AllowTransitions) return;
            if (root.RenderTransform is not ScaleTransform s) return;
            var chip = _currentChip;
            int ms = DashboardBillboard.SnoozeFoldMs;
            _tw.To(s, "s", () => s.ScaleX, v => { s.ScaleX = v; s.ScaleY = v; }, null, 0, ms, Easings.QuadIn);
            _tw.To(chip, "opacity", () => chip.Opacity, v => chip.Opacity = v, null, 0, ms, Easings.QuadIn);
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
            var fill = _fill;
            double from = Math.Clamp(_holdProgress, 0, 1);
            _tw.To(fill, "hold", () => fill.ScaleX, v => fill.ScaleX = v, from, 1,
                Math.Max(1, DashboardBillboard.RemainingMs(from)), done: () =>
                {
                    if (token != _holdToken || !_holdRunning) return;
                    _holdRunning = false;
                    _holdProgress = 1;
                    Advance();
                });
        }

        /// <summary>Holds the fill where it is (hover, focus, another tab, Motion Off).</summary>
        private void PauseHold()
        {
            if (!_holdRunning) return;
            _holdRunning = false;
            _holdToken++;
            if (_fill == null) return;
            _tw.Stop(_fill, "hold");
            _holdProgress = _fill.ScaleX;
        }

        private void StopHold()
        {
            _holdRunning = false;
            _holdToken++;
            if (_fill != null) _tw.Stop(_fill, "hold");
        }
    }
}
