using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// Circe's mood on the lock hero: a small upright meter, mint at the bottom, gold, then red at
    /// the top, the mood word under it and what the next repeat costs. Hidden when there is no mood
    /// (heat row off, tab off, not linked). A change pops the word and slides the fill, at
    /// MotionLevel Full only; Reduced and Off snap.
    ///
    /// <para><b>Literal colours</b>, like the rail chip: the mood is the tab's own palette, never
    /// the active mod's accent.</para>
    /// </summary>
    public sealed class CircesMoodMeter : StackPanel
    {
        public const double TrackHeight = 96;
        private const double TrackWidth = 20;

        internal static readonly Color CalmMint = Color.FromRgb(0x5F, 0xFF, 0xD0);
        internal static readonly Color WarmGold = Color.FromRgb(0xE0, 0xB0, 0x52);
        internal static readonly Color HotRed = Color.FromRgb(0xFF, 0x6B, 0x8A);
        internal static readonly Color SmokingRed = Color.FromRgb(0xFF, 0x3D, 0x71);

        /// <summary>The mood's own colour, shared with the rail chip's pip.</summary>
        public static Color ColourOf(MoodLevel level) => level switch
        {
            MoodLevel.Warm => WarmGold,
            MoodLevel.Hot => HotRed,
            MoodLevel.Smoking => SmokingRed,
            _ => CalmMint,
        };

        private readonly TextBlock _label;
        private readonly Border _cover;
        private readonly TextBlock _word;
        private readonly TextBlock _factor;
        private readonly SolidColorBrush _wordBrush = new(CalmMint);
        private readonly ScaleTransform _pop = new(1, 1);
        private readonly DropShadowEffect _glow = new() { Color = CalmMint, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.45 };
        private CircesMood? _shown;

        public CircesMoodMeter()
        {
            Orientation = Orientation.Vertical;
            HorizontalAlignment = HorizontalAlignment.Center;
            Visibility = Visibility.Collapsed;

            _label = new TextBlock
            {
                FontFamily = Services.UI.FontGuard.Mono,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = Frozen(Color.FromRgb(0xA9, 0xA3, 0xC4)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 5),
            };
            Children.Add(_label);

            // The full gradient, with a dark cover pulled down from the top: the fill never
            // stretches the gradient, so CALM stays mint and only SMOKING reaches red.
            var gradient = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(0, 0) };
            gradient.GradientStops.Add(new GradientStop(CalmMint, 0));
            gradient.GradientStops.Add(new GradientStop(WarmGold, 0.5));
            gradient.GradientStops.Add(new GradientStop(HotRed, 0.85));
            gradient.GradientStops.Add(new GradientStop(SmokingRed, 1));
            gradient.Freeze();
            _cover = new Border
            {
                VerticalAlignment = VerticalAlignment.Top,
                Height = TrackHeight,
                Background = Frozen(Color.FromArgb(0xF2, 0x1A, 0x12, 0x30)),
            };
            var body = new Grid { Width = TrackWidth, Height = TrackHeight };
            body.Children.Add(new Border { Background = gradient });
            body.Children.Add(_cover);
            var track = new Border
            {
                Width = TrackWidth + 3,
                Height = TrackHeight + 3,
                CornerRadius = new CornerRadius((TrackWidth + 3) / 2),
                BorderThickness = new Thickness(1.5),
                BorderBrush = Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center,
                ClipToBounds = true,
                Effect = _glow,
                Child = body,
            };
            // Round the ends of what is inside the rim, not just the rim.
            body.Clip = new RectangleGeometry(new Rect(0, 0, TrackWidth, TrackHeight), TrackWidth / 2, TrackWidth / 2);
            Children.Add(track);

            _word = new TextBlock
            {
                FontFamily = new FontFamily("/Fonts/#Fredoka, Segoe UI"),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = _wordBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 7, 0, 0),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = _pop,
            };
            Children.Add(_word);
            _factor = new TextBlock
            {
                FontFamily = Services.UI.FontGuard.Mono,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Frozen(WarmGold),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0),
            };
            Children.Add(_factor);
        }

        /// <summary>What the word says right now. For the render tests.</summary>
        internal string Word => _word.Text;

        /// <summary>The dark cover's height at rest (the fill is the rest of the track). For the tests.</summary>
        internal double CoverHeight(CircesMood mood) => TrackHeight * (1 - mood.Fill);

        /// <summary>Paint today's mood, or hide when there is none.</summary>
        public void Apply(CircesMood? mood)
        {
            if (mood is not { } m)
            {
                Visibility = Visibility.Collapsed;
                _shown = null;
                return;
            }

            var changed = _shown is { } was && was != m;
            var wasVisible = Visibility == Visibility.Visible;
            Visibility = Visibility.Visible;
            var colour = ColourOf(m.Level);
            _label.Text = Loc.Get("chaster_mood_label");
            _word.Text = Loc.Get(m.WordKey);
            _wordBrush.Color = colour;
            _glow.Color = colour;
            _factor.Text = m.FactorText;
            ToolTip = Loc.GetF("chaster_mood_tip", m.FactorText);

            var target = CoverHeight(m);
            var animate = changed && wasVisible && MotionFx.Level == MotionLevel.Full;
            if (animate)
            {
                _cover.BeginAnimation(HeightProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(420))
                {
                    EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut },
                });
                var spring = new ElasticEase { Oscillations = 1, Springiness = 4, EasingMode = EasingMode.EaseOut };
                var grow = new DoubleAnimation(1.35, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = spring };
                _pop.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                _pop.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                _glow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(1, 0.45, TimeSpan.FromMilliseconds(600)));
            }
            else
            {
                _cover.BeginAnimation(HeightProperty, null);
                _cover.Height = target;
            }
            _shown = m;
        }

        private static Brush Frozen(Color c)
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
        }
    }
}
