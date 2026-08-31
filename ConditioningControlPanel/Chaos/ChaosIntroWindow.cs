using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel;

/// <summary>
/// "The invitation" — a one-time, spoiler-free intro card shown the FIRST time the
/// Dollhouse ever opens. Hero art on top (assets/Chaos/guide/intro.png, vector
/// fallback when absent), three verb lines that teach only what the first minute
/// needs, one button. Everything else stays for finding out down there.
/// Modal over the hub; the caller marks <see cref="ChaosMetaState.SeenIntroGuide"/>.
/// </summary>
public sealed class ChaosIntroWindow : Window
{
    public ChaosIntroWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.Height;
        Width = 560;
        ShowInTaskbar = false;

        var pink = Color.FromRgb(0xE8, 0x43, 0x93);
        var stack = new StackPanel();

        // ---- hero art (rounded-top clip; graceful skip when the file is absent) ----
        var hero = ChaosArt.Resolve("guide", "intro");
        if (hero != null)
        {
            var img = new Image { Source = hero, Stretch = Stretch.UniformToFill, Height = 220 };
            var heroHost = new Border
            {
                Height = 220,
                CornerRadius = new CornerRadius(14, 14, 0, 0),
                Child = img,
            };
            // Clip to the rounded card top — Border.CornerRadius alone doesn't clip children.
            heroHost.Loaded += (_, _) =>
            {
                try
                {
                    heroHost.Clip = new RectangleGeometry(
                        new Rect(0, 0, heroHost.ActualWidth, heroHost.ActualHeight), 14, 14);
                }
                catch { }
            };
            stack.Children.Add(heroHost);
        }

        // ---- title ----
        var title = new TextBlock
        {
            Text = "🐇 DOWN THE RABBIT HOLE",
            Foreground = Brushes.White,
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, hero != null ? 18 : 30, 0, 2),
            Effect = new DropShadowEffect { Color = pink, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.8 },
        };
        stack.Children.Add(title);
        stack.Children.Add(new TextBlock
        {
            Text = "you don't have to understand it. you just have to fall.",
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xB0, 0xB0, 0xC8)),
            FontStyle = FontStyles.Italic,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16),
        });

        // ---- the three verb lines (everything the first minute needs, nothing more) ----
        var rules = new StackPanel { Margin = new Thickness(46, 0, 46, 4) };
        // Each rule colour-codes its input VERB (LEFT-CLICK / HOLD / RIGHT-CLICK) in the same hue
        // as its glyph, so the action and the gesture read as one colour. A pill chip makes the
        // verb pop out of the sentence; the action phrase stays bold-white, the rest dim.
        void Rule(string glyph, Color color, string? verb, string head, string rest)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 7) };
            row.Children.Add(new TextBlock
            {
                Text = glyph,
                FontSize = 22,
                Width = 38,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(color),
            });
            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            // Colour-coded input verb pill (skipped for the rabbit line, which has no gesture).
            if (!string.IsNullOrEmpty(verb))
            {
                col.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, color.R, color.G, color.B)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(7, 1, 7, 2),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 3),
                    Child = new TextBlock
                    {
                        Text = verb,
                        Foreground = new SolidColorBrush(color),
                        FontWeight = FontWeights.Bold,
                        FontSize = 11,
                        FontFamily = new FontFamily("Consolas, Courier New"),
                    },
                });
            }
            var text = new TextBlock
            {
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 380,
            };
            text.Inlines.Add(new System.Windows.Documents.Run(head)
            { Foreground = Brushes.White, FontWeight = FontWeights.Bold });
            text.Inlines.Add(new System.Windows.Documents.Run(rest)
            { Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xC8, 0xC8, 0xDE)) });
            col.Children.Add(text);
            row.Children.Add(col);
            rules.Children.Add(row);
        }
        Rule("🫧", Color.FromRgb(0xFF, 0xD0, 0xE8), "LEFT-CLICK", "pop the treats. ", "a click is enough. they feed your streak.");
        Rule("◉", Color.FromRgb(0xFF, 0xD2, 0x28), "PRESS & HOLD", "hold the burning ones. ", "press and keep pressing until they snap. let one finish its trance and it goes off.");
        Rule("🌊", Color.FromRgb(0x7A, 0xE0, 0xFF), "RIGHT-CLICK", "ripple the water. ", "a right-click near the bubbles sends out a wave — treats pop, trances snap, rabbits go flying. it takes a while to gather another.");
        Rule("🐇", Color.FromRgb(0xFF, 0x69, 0xB4), null, "follow the white rabbit. ", "everything else down there is yours to find out.");
        stack.Children.Add(rules);

        // ---- the one button ----
        var btn = new Button
        {
            Content = "i understand. take me down",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 13, 0, 13),
            Margin = new Thickness(46, 16, 46, 26),
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = new LinearGradientBrush(
                Color.FromRgb(0xE8, 0x43, 0x93), Color.FromRgb(0x8B, 0x5C, 0xF6),
                new Point(0, 0), new Point(1, 1)),
        };
        btn.Resources.Add(typeof(Border), new Style(typeof(Border))
        { Setters = { new Setter(Border.CornerRadiusProperty, new CornerRadius(12)) } });
        btn.Click += (_, _) => { DialogResult = true; Close(); };
        stack.Children.Add(btn);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x12, 0x2A)),
            BorderBrush = new SolidColorBrush(pink),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(16),
            Child = stack,
        };

        // Soft fade-in so the card arrives like an invitation, not a popup.
        Opacity = 0;
        Loaded += (_, _) =>
        {
            try
            {
                BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420))
                    { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } });
            }
            catch { Opacity = 1; }
        };
    }
}
