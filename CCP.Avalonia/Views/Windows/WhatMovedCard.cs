// PORTED from ConditioningControlPanel/Windows/WhatMovedCard.xaml + .xaml.cs (WPF 7.1.5, nav rework
// 2026-10-06). Code-only: one small card, five rows. Deviations, all forced:
//   DropShadowEffect on the card  -> a BoxShadow on the border (no Effect on chrome, port rule)
//   Image + EmojiToImageSource    -> a TextBlock (Avalonia draws colour emoji)
//   LinearGradient "0,0"/"0.4,1"  -> relative points (a bare pair is pixels here)
// Owned, non-modal. Show me closes the card, then navigates. Read mode is the Help replay.

using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public sealed class WhatMovedCard : Window
    {
        private readonly Action<WhatMovedRow>? _showMe;

        /// <summary>True when opened from Help: a replay never touches the one-time counter.</summary>
        public bool ReadMode { get; }

        internal StackPanel Rows { get; } = new();

        public WhatMovedCard() : this(null, readMode: true) { }

        public WhatMovedCard(Action<WhatMovedRow>? showMe, bool readMode)
        {
            _showMe = showMe;
            ReadMode = readMode;

            Title = Loc.Get("whatmoved_title");
            Width = 520;
            SizeToContent = SizeToContent.Height;
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var title = new TextBlock { Text = Loc.Get("whatmoved_title"), FontSize = 24, FontWeight = FontWeight.ExtraBold };
            title[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("PinkBrush");
            var x = new Button
            {
                Name = "BtnX",
                Content = new TextBlock { Text = "✕", FontSize = 13, Foreground = new SolidColorBrush(Color.Parse("#99FFFFFF")) },
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(4, 0),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            AutomationProperties.SetName(x, Loc.Get("whatmoved_close"));
            x.Click += (_, _) => Close();

            foreach (var row in WhatMovedPlan.Rows) Rows.Children.Add(BuildRow(row));

            var done = new Button
            {
                Name = "BtnDone", Content = Loc.Get("whatmoved_close"), HorizontalAlignment = HorizontalAlignment.Right,
                Padding = new Thickness(18, 7), Margin = new Thickness(0, 8, 0, 0),
            };
            ApplyTheme(done, "SecondaryButton");
            done.Click += (_, _) => Close();

            var card = new Border
            {
                Name = "CardRoot",
                CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Margin = new Thickness(18),
                BoxShadow = BoxShadows.Parse("0 4 28 0 #99000000"),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0.4, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.Parse("#FF2C1450"), 0),
                        new GradientStop(Color.Parse("#FF1C1233"), 0.45),
                        new GradientStop(Color.Parse("#FF100A1E"), 1),
                    },
                },
                Child = new StackPanel
                {
                    Margin = new Thickness(26, 22),
                    Children =
                    {
                        new Grid { Children = { title, x } },
                        new TextBlock
                        {
                            Text = Loc.Get("whatmoved_intro"), Margin = new Thickness(0, 6, 0, 16), FontSize = 13,
                            Foreground = new SolidColorBrush(Color.Parse("#CCFFFFFF")), TextWrapping = TextWrapping.Wrap,
                        },
                        Rows,
                        done,
                    },
                },
            };
            card[!Border.BorderBrushProperty] = new DynamicResourceExtension("PinkBrush");
            Content = card;

            // Enter closes. Escape is the panic key's job and reaches the ladder on its own; the card
            // closes with it so the press does not leave a card over a page being torn down.
            AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key != Key.Enter && e.Key != Key.Escape) return;
                Close();
                if (e.Key == Key.Enter) e.Handled = true;
            }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        private Control BuildRow(WhatMovedRow row)
        {
            var glyph = new TextBlock
            {
                Text = row.Glyph, FontSize = 16, Foreground = Brushes.White, Width = 22,
                TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
                FontFamily = new FontFamily("Segoe UI Emoji, Noto Color Emoji, Segoe UI Symbol, Segoe UI"),
            };
            var text = new TextBlock
            {
                Text = row.Text, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            };
            var show = new Button
            {
                Content = Loc.Get("whatmoved_show"), Tag = row, Margin = new Thickness(12, 0, 0, 0),
                Padding = new Thickness(12, 5), FontSize = 12, Cursor = new Cursor(StandardCursorType.Hand),
            };
            ApplyTheme(show, "PinkButton");
            AutomationProperties.SetName(show, row.ShowMeName);
            show.Click += (_, _) => ShowMe(row);
            Grid.SetColumn(text, 1);
            Grid.SetColumn(show, 2);
            var border = new Border
            {
                Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(12, 9), CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.Parse("#1FFFFFFF")),
                BorderBrush = new SolidColorBrush(Color.Parse("#26FFFFFF")), BorderThickness = new Thickness(1),
                Tag = row,
                Child = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Children = { glyph, text, show } },
            };
            AutomationProperties.SetName(border, row.Text);
            return border;
        }

        /// <summary>Show me: the card closes first, then the shell navigates and glows the target.</summary>
        internal void ShowMe(WhatMovedRow row)
        {
            var go = _showMe;
            Close();
            try { go?.Invoke(row); }
            catch (Exception ex) { Log.Warning(ex, "What moved: Show me {Id} failed", row.Id); }
        }

        private static void ApplyTheme(Button b, string key)
        {
            if (Application.Current is { } app && app.TryFindResource(key, out var theme) && theme is ControlTheme t) b.Theme = t;
        }
    }
}
