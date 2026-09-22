using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// Circe's bill as a paper receipt: cream stock, ink, mono figures, a dashed tear line and a
    /// stamped NET. Deliberately the one thing on the page that is not the app's dark velvet - a
    /// receipt is a physical object and it should read as one, the way a till roll does. No purple
    /// glow, no accent brushes, no theme resources at all: every colour here is a literal, so the
    /// paper looks like paper under every mod skin and the control realizes with no app resources
    /// loaded (which is also what lets a render test build one).
    ///
    /// <para>Takes a <see cref="TabBill"/> and nothing else, so the at-close bill window can show
    /// the same object later without a second layout.</para>
    /// </summary>
    public sealed class ChasterReceiptView : ContentControl
    {
        private static readonly Brush Paper = Frozen(0xF4, 0xEE, 0xE2);
        private static readonly Brush PaperEdge = Frozen(0xD8, 0xCE, 0xBB);
        private static readonly Brush Ink = Frozen(0x2E, 0x2A, 0x26);
        private static readonly Brush FadedInk = Frozen(0x6B, 0x63, 0x59);
        private static readonly Brush StampInk = Frozen(0xB2, 0x3A, 0x48);
        private static readonly Brush CreditInk = Frozen(0x1F, 0x6B, 0x5A);
        private static readonly FontFamily Mono = new("Consolas, Courier New");

        private readonly StackPanel _lines = new();
        private readonly StackPanel _totals = new();
        private readonly TextBlock _date = new();
        private readonly Border _stamp;
        private readonly TextBlock _stampFigure;

        public ChasterReceiptView()
        {
            _date.FontFamily = Mono;
            _date.FontSize = 10;
            _date.Foreground = FadedInk;
            _date.Margin = new Thickness(0, 2, 0, 8);
            _date.HorizontalAlignment = HorizontalAlignment.Center;

            _stampFigure = new TextBlock
            {
                FontFamily = Mono,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = StampInk,
            };
            _stamp = new Border
            {
                BorderBrush = StampInk,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 10, 0, 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                Opacity = 0.85,
                Child = _stampFigure,
                // A stamp is pressed by hand, so it sits a little crooked. Static: a render
                // transform, never an animation, so nothing here moves under MotionFx Off.
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(-5),
            };

            var head = new TextBlock
            {
                FontFamily = Mono,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Ink,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            head.SetBinding(TextBlock.TextProperty, Bound("chaster_bill_title"));

            var body = new StackPanel();
            body.Children.Add(head);
            body.Children.Add(_date);
            body.Children.Add(Tear());
            body.Children.Add(_lines);
            body.Children.Add(Tear());
            body.Children.Add(_totals);
            body.Children.Add(_stamp);

            Content = new Border
            {
                Background = Paper,
                BorderBrush = PaperEdge,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(16, 12, 16, 12),
                MaxWidth = 380,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = body,
            };
        }

        /// <summary>Paint a bill. An empty bill is one muted line, not an empty receipt.</summary>
        public void Show(TabBill? bill)
        {
            _lines.Children.Clear();
            _totals.Children.Clear();
            _date.Text = DateTime.Now.ToString("g");

            if (bill == null || bill.IsEmpty)
            {
                var empty = new TextBlock
                {
                    FontFamily = Mono,
                    FontSize = 12,
                    Foreground = FadedInk,
                    Margin = new Thickness(0, 8, 0, 8),
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                empty.SetBinding(TextBlock.TextProperty, Bound("chaster_bill_empty"));
                _lines.Children.Add(empty);
                _stamp.Visibility = Visibility.Collapsed;
                return;
            }

            foreach (var line in bill.Lines)
                _lines.Children.Add(Row(Loc.Get(TabPageText.NameKey(line.EventId)) + (line.Count > 1 ? "  x" + line.Count : ""),
                    CircesTab.Format(line.Seconds), line.Seconds < 0 ? CreditInk : Ink));

            if (bill.AddedSeconds != 0)
                _totals.Children.Add(Row(Loc.Get("chaster_bill_added"), CircesTab.Format(bill.AddedSeconds), FadedInk, small: true));
            if (bill.EarnedBackSeconds != 0)
                _totals.Children.Add(Row(Loc.Get("chaster_bill_earned"), CircesTab.Format(bill.EarnedBackSeconds), FadedInk, small: true));
            if (bill.PushedSeconds != 0)
                _totals.Children.Add(Row(Loc.Get("chaster_bill_pushed_label"), CircesTab.Format(bill.PushedSeconds), FadedInk, small: true));

            _stamp.Visibility = Visibility.Visible;
            _stampFigure.Text = Loc.Get("chaster_bill_stamp") + " " + CircesTab.Format(bill.NetSeconds);
        }

        private static Grid Row(string name, string figure, Brush figureInk, bool small = false)
        {
            var row = new Grid { Margin = new Thickness(0, small ? 1 : 3, 0, small ? 1 : 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = name,
                FontFamily = Mono,
                FontSize = small ? 10 : 11,
                Foreground = small ? FadedInk : Ink,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 12, 0),
            });
            var value = new TextBlock
            {
                Text = figure,
                FontFamily = Mono,
                FontSize = small ? 10 : 11,
                FontWeight = small ? FontWeights.Normal : FontWeights.SemiBold,
                Foreground = figureInk,
            };
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            return row;
        }

        private static UIElement Tear() => new Line
        {
            X1 = 0,
            X2 = 1,
            Stretch = Stretch.Fill,
            Stroke = PaperEdge,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 3 },
            Margin = new Thickness(0, 6, 0, 6),
        };

        /// <summary>Live-bound so a language switch repaints the paper without rebuilding it.</summary>
        private static System.Windows.Data.Binding Bound(string key) =>
            new($"[{key}]") { Source = LocalizationManager.Instance, Mode = System.Windows.Data.BindingMode.OneWay };

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
