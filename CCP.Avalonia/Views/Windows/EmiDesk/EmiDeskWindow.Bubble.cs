using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// Her speech bubble (row E4, visual half): WPF EmiDeskWindow.Bubble.cs :40-493. Navy fill,
    /// pink 1 px ink, pixel font, anchored at 58 % of her width with its bottom at 96 % of her
    /// height up, a 10 px tail, width 1.5x her body clamped 220..380, font 11 at a 220 px body
    /// (floor 10), line height 1.4x. Every chain frame that carries a bubble lands here through
    /// <see cref="OnBubbleTextCore"/>.
    /// ponytail: the Blipese voice (WPF EmiVox.cs), the book-side flip and the
    /// monitor work-area clamp are not ported yet; the bubble is clamped to her own window.
    /// </summary>
    public partial class EmiDeskWindow
    {
        private static readonly IBrush BubbleFill = new ImmutableSolidColorBrush(Color.FromRgb(0x18, 0x18, 0x32));
        private static readonly IBrush BubbleInk = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));

        private const double BubbleLeftFrac = 0.58;
        private const double BubbleBottomFrac = 0.96;
        private const double BubbleFontAtDefaultWidth = 11.0;
        private const double BubbleFontFloor = 10.0;
        private const double BubbleFontRefWidth = 220.0;
        private const double BubbleMinWidth = 220.0;
        private const double BubbleMaxWidth = 380.0;
        private const double BubbleWidthOfBody = 1.5;
        private const double BubbleEdgeGap = 2.0;

        /// <summary>WPF EmiFace.PixelFont: Press Start 2P (shipped), then the mono chain.</summary>
        private static readonly FontFamily PixelFont = new(
            "avares://CCP.Avalonia/Resources/emi/fonts#Press Start 2P, Noto Sans Mono, Cascadia Mono, Consolas, Courier New, monospace");

        private Border? _bubble;
        private TextBlock? _bubbleText;
        private Polygon? _bubbleTail;

        /// <summary>The bubble's text while it is up, else null (test seam).</summary>
        internal string? BubbleText => _bubble is { IsVisible: true } ? _bubbleText?.Text : null;

        partial void OnBubbleTextCore(string? text)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) { HideBubble(); return; }
                ShowBubble(text!);
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] bubble paint failed"); }
        }

        private void EnsureBubble()
        {
            if (_bubble != null) return;
            _bubbleText = new TextBlock
            {
                Foreground = BubbleInk,
                FontFamily = PixelFont,
                TextWrapping = TextWrapping.Wrap,
                IsHitTestVisible = false
            };
            _bubble = new Border
            {
                Background = BubbleFill,
                BorderBrush = BubbleInk,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 8, 10, 8),
                IsHitTestVisible = false,
                Child = _bubbleText,
                IsVisible = false
            };
            _bubbleTail = new Polygon
            {
                Fill = BubbleFill,
                Stroke = BubbleInk,
                StrokeThickness = 1,
                IsHitTestVisible = false,
                IsVisible = false
            };
            _bubbleCanvas.Children.Add(_bubbleTail);
            _bubbleCanvas.Children.Add(_bubble);
            _bubble.SizeChanged += (_, _) => LayoutBubble();
        }

        private void ShowBubble(string text)
        {
            EnsureBubble();
            if (_bubble == null || _bubbleText == null) return;
            _bubbleText.Text = text;
            _bubble.IsVisible = true;
            if (_bubbleTail != null) _bubbleTail.IsVisible = true;
            LayoutBubble();
        }

        private void HideBubble()
        {
            if (_bubble != null) _bubble.IsVisible = false;
            if (_bubbleTail != null) _bubbleTail.IsVisible = false;
        }

        private void LayoutBubble()
        {
            try
            {
                if (_bubble == null || _bubbleText == null || !_bubble.IsVisible) return;
                double bw = BodyWidth;
                double bh = bw * BodyAspect;

                double fs = Math.Max(BubbleFontFloor, Math.Round(BubbleFontAtDefaultWidth * bw / BubbleFontRefWidth));
                _bubbleText.FontSize = fs;
                _bubbleText.LineHeight = Math.Round(fs * 1.4);
                _bubble.MaxWidth = Math.Max(BubbleMinWidth, Math.Min(BubbleMaxWidth, bw * BubbleWidthOfBody));

                _bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var size = new Size(Math.Max(_bubble.DesiredSize.Width, _bubble.Bounds.Width),
                                    Math.Max(_bubble.DesiredSize.Height, _bubble.Bounds.Height));

                double bodyX = OverlayPadX, bodyY = OverlayPad;
                double left = bodyX + bw * BubbleLeftFrac;
                double bottom = bodyY + bh - bh * BubbleBottomFrac;
                double top = bottom - size.Height;

                // Off her own window's right edge: flip to the mirrored anchor (WPF EmiBubblePlacement).
                bool flip = false;
                double winW = double.IsNaN(Width) ? Bounds.Width : Width;
                if (left + size.Width > winW - BubbleEdgeGap)
                {
                    flip = true;
                    left = bodyX + bw * (1.0 - BubbleLeftFrac) - size.Width;
                }
                if (left < BubbleEdgeGap) left = BubbleEdgeGap;
                if (top < 2) top = 2;

                Canvas.SetLeft(_bubble, Math.Round(left));
                Canvas.SetTop(_bubble, Math.Round(top));
                LayoutTail(left, top + size.Height, size.Width, flip);
                LayoutChips();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] bubble layout failed"); }
        }

        private void LayoutTail(double left, double bubbleBottom, double bubbleWidth, bool flip)
        {
            if (_bubbleTail == null) return;
            const double w = 10, h = 10;
            double x = flip ? left + bubbleWidth - w : left;
            double y = bubbleBottom - 1;
            _bubbleTail.Points = flip
                ? new Point[] { new(0, 0), new(w, 0), new(w, h) }
                : new Point[] { new(w, 0), new(0, 0), new(0, h) };
            Canvas.SetLeft(_bubbleTail, Math.Round(x));
            Canvas.SetTop(_bubbleTail, Math.Round(y));
        }
    }
}
