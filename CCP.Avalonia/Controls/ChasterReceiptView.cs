// PORTED from ConditioningControlPanel/Controls/ChasterReceiptView.cs: Circe's bill as a paper
// receipt (cream stock, ink, mono figures, dashed tear lines, a crooked NET stamp, her verdict).
// Literal colours only, like WPF, so the paper reads as paper under every skin.
// Texts are set from Loc at Show time; a language switch while shown repaints the last bill (WPF's
// tab does it through RefreshNumbers -> BuildBill, ChasterTabView.xaml.cs:219), same verdict line.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Controls
{
    public sealed class ChasterReceiptView : ContentControl
    {
        private static readonly IBrush Paper = new SolidColorBrush(Color.FromRgb(0xF4, 0xEE, 0xE2));
        private static readonly IBrush PaperEdge = new SolidColorBrush(Color.FromRgb(0xD8, 0xCE, 0xBB));
        private static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(0x2E, 0x2A, 0x26));
        private static readonly IBrush FadedInk = new SolidColorBrush(Color.FromRgb(0x6B, 0x63, 0x59));
        private static readonly IBrush StampInk = new SolidColorBrush(Color.FromRgb(0xB2, 0x3A, 0x48));
        private static readonly IBrush CreditInk = new SolidColorBrush(Color.FromRgb(0x1F, 0x6B, 0x5A));
        private static readonly FontFamily Mono = new("Consolas, Courier New, monospace");

        private readonly StackPanel _lines = new();
        private readonly StackPanel _totals = new();
        private readonly TextBlock _head = new() { FontFamily = Mono, FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center };
        private readonly TextBlock _date = new() { FontFamily = Mono, FontSize = 10, Foreground = FadedInk, Margin = new Thickness(0, 2, 0, 8), HorizontalAlignment = HorizontalAlignment.Center };
        private readonly TextBlock _stampFigure = new() { FontFamily = Mono, FontSize = 15, FontWeight = FontWeight.Bold, Foreground = StampInk };
        private readonly Border _stamp;
        private readonly TextBlock _verdictText = new()
        {
            FontSize = 12.5, FontWeight = FontWeight.SemiBold, FontStyle = FontStyle.Italic, Foreground = Ink,
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), MaxWidth = 300,
        };
        private readonly StackPanel _verdict = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), IsVisible = false };
        private CirceMoment? _verdictMoment;
        private string? _verdictKey;
        private TabBill? _bill;
        private bool _shown;

        private void OnLanguageChanged(object? sender, EventArgs e) =>
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (_shown) Show(_bill); });

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
            base.OnDetachedFromVisualTree(e);
        }

        public ChasterReceiptView()
        {
            _stamp = new Border
            {
                BorderBrush = StampInk, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(3),
                Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 10, 0, 2),
                HorizontalAlignment = HorizontalAlignment.Right, Opacity = 0.85, Child = _stampFigure,
                RenderTransform = new RotateTransform(-5),   // pressed by hand; static, never animated
            };
            _verdict.Children.Add(Face(24));
            _verdict.Children.Add(_verdictText);

            var body = new StackPanel();
            body.Children.Add(_head);
            body.Children.Add(_date);
            body.Children.Add(Tear());
            body.Children.Add(_lines);
            body.Children.Add(Tear());
            body.Children.Add(_totals);
            body.Children.Add(_stamp);
            body.Children.Add(_verdict);
            Content = new Border
            {
                Background = Paper, BorderBrush = PaperEdge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2),
                Padding = new Thickness(16, 12, 16, 12), MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Left, Child = body,
            };
        }

        /// <summary>Paint a bill. An empty bill is one muted line, not an empty receipt (WPF Show).</summary>
        public void Show(TabBill? bill)
        {
            _bill = bill;
            _shown = true;
            _lines.Children.Clear();
            _totals.Children.Clear();
            _head.Text = Loc.Get("chaster_bill_title");
            _date.Text = DateTime.Now.ToString("g");

            if (bill == null || bill.IsEmpty)
            {
                _lines.Children.Add(new TextBlock
                {
                    Text = Loc.Get("chaster_bill_empty"), FontFamily = Mono, FontSize = 12, Foreground = FadedInk,
                    Margin = new Thickness(0, 8, 0, 8), HorizontalAlignment = HorizontalAlignment.Center,
                });
                _stamp.IsVisible = false;
                _verdict.IsVisible = false;
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

            _stamp.IsVisible = true;
            _stampFigure.Text = Loc.Get("chaster_bill_stamp") + " " + CircesTab.Format(bill.NetSeconds);

            var moment = CirceLines.Verdict(bill.NetSeconds);
            if (moment != _verdictMoment || _verdictKey == null)
            {
                _verdictMoment = moment;
                _verdictKey = CirceLines.Shared.Pick(moment, DateTime.UtcNow); // the bill always wins
            }
            _verdictText.Text = _verdictKey == null ? string.Empty : Loc.Get(_verdictKey);
            _verdict.IsVisible = _verdictKey != null;
        }

        /// <summary>The stamped NET line as painted, for the tests.</summary>
        internal string StampText => _stamp.IsVisible ? _stampFigure.Text ?? "" : "";

        private static Grid Row(string name, string figure, IBrush figureInk, bool small = false)
        {
            var row = new Grid { Margin = new Thickness(0, small ? 1 : 3, 0, small ? 1 : 3), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(new TextBlock
            {
                Text = name, FontFamily = Mono, FontSize = small ? 10 : 11, Foreground = small ? FadedInk : Ink,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 12, 0),
            });
            var value = new TextBlock
            {
                Text = figure, FontFamily = Mono, FontSize = small ? 10 : 11,
                FontWeight = small ? FontWeight.Normal : FontWeight.SemiBold, Foreground = figureInk,
            };
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            return row;
        }

        private static Control Tear() => new Line
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), Stretch = Stretch.Fill,
            Stroke = PaperEdge, StrokeThickness = 1, StrokeDashArray = new global::Avalonia.Collections.AvaloniaList<double> { 3, 3 },
            Margin = new Thickness(0, 6, 0, 6),
        };

        /// <summary>WPF CirceSays.Face: a lilac-to-pink disc with an ink padlock.</summary>
        private static Control Face(double size)
        {
            var ink = new SolidColorBrush(Color.FromRgb(0x24, 0x1A, 0x2E));
            var art = new Canvas { Width = 14, Height = 15 };
            art.Children.Add(new Path { Data = Geometry.Parse("M4,7 V5 a3,3 0 0 1 6,0 V7"), Stroke = ink, StrokeThickness = 1.8 });
            art.Children.Add(new Rectangle { Width = 10, Height = 7.5, RadiusX = 2, RadiusY = 2, Fill = ink, Margin = new Thickness(2, 6.5, 0, 0) });
            return new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), VerticalAlignment = VerticalAlignment.Center,
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.FromRgb(0xD9, 0xC4, 0xFF), 0), new GradientStop(Color.FromRgb(0xFF, 0x8F, 0xB1), 1) },
                },
                Child = new Viewbox { Width = size * 0.58, Height = size * 0.58, Child = art },
            };
        }
    }
}
