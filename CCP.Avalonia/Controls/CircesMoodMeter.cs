// PORTED from ConditioningControlPanel/Controls/CircesMoodMeter.cs: Circe's mood on the lock hero,
// a small upright meter (mint, gold, then red at the top), the mood word and what the next repeat
// costs. Hidden with no mood. A change pops the word and slides the fill at MotionLevel Full only.
// Literal colours, like the rail chip: the tab's own palette, never the mod's accent.
using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Controls
{
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
        private readonly DropShadowEffect _glow = new() { Color = CalmMint, BlurRadius = 14, OffsetX = 0, OffsetY = 0, Opacity = 0.45 };
        private CircesMood? _shown;
        private System.Threading.CancellationTokenSource? _anim;

        public CircesMoodMeter()
        {
            Orientation = Orientation.Vertical;
            HorizontalAlignment = HorizontalAlignment.Center;
            IsVisible = false;

            _label = new TextBlock
            {
                FontFamily = new FontFamily("Consolas, Courier New"), FontSize = 9.5, FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA9, 0xA3, 0xC4)),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 5),
            };
            Children.Add(_label);

            // The full gradient, with a dark cover pulled down from the top: the fill never
            // stretches the gradient, so CALM stays mint and only SMOKING reaches red.
            var gradient = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(CalmMint, 0), new GradientStop(WarmGold, 0.5), new GradientStop(HotRed, 0.85), new GradientStop(SmokingRed, 1) },
            };
            _cover = new Border
            {
                VerticalAlignment = VerticalAlignment.Top, Height = TrackHeight,
                Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x1A, 0x12, 0x30)),
            };
            var body = new Grid { Width = TrackWidth, Height = TrackHeight };
            body.Children.Add(new Border { Background = gradient });
            body.Children.Add(_cover);
            // Round the ends of what is inside the rim, not just the rim.
            body.Clip = new RectangleGeometry(new Rect(0, 0, TrackWidth, TrackHeight), TrackWidth / 2, TrackWidth / 2);
            Children.Add(new Border
            {
                Width = TrackWidth + 3, Height = TrackHeight + 3, CornerRadius = new CornerRadius((TrackWidth + 3) / 2),
                BorderThickness = new Thickness(1.5), BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center, ClipToBounds = true, Effect = _glow, Child = body,
            });

            _word = new TextBlock
            {
                FontFamily = new FontFamily("Fredoka, Segoe UI"), FontSize = 13, FontWeight = FontWeight.Bold, Foreground = _wordBrush,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 7, 0, 0),
                RenderTransformOrigin = RelativePoint.Center, RenderTransform = _pop,
            };
            Children.Add(_word);
            _factor = new TextBlock
            {
                FontFamily = new FontFamily("Consolas, Courier New"), FontSize = 11, FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(WarmGold), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 0),
            };
            Children.Add(_factor);
        }

        /// <summary>What the word says right now. For the tests.</summary>
        internal string Word => _word.Text ?? "";
        internal string Factor => _factor.Text ?? "";
        internal Color WordColour => _wordBrush.Color;
        internal Border Cover => _cover;

        /// <summary>The dark cover's height at rest (the fill is the rest of the track).</summary>
        internal static double CoverHeight(CircesMood mood) => TrackHeight * (1 - mood.Fill);

        /// <summary>Paint today's mood, or hide when there is none.</summary>
        public void Apply(CircesMood? mood)
        {
            if (mood is not { } m)
            {
                IsVisible = false;
                _shown = null;
                return;
            }

            var changed = _shown is { } was && was != m;
            var wasVisible = IsVisible;
            IsVisible = true;
            var colour = ColourOf(m.Level);
            _label.Text = Loc.Get("chaster_mood_label");
            _word.Text = Loc.Get(m.WordKey);
            _wordBrush.Color = colour;
            _glow.Color = colour;
            _factor.Text = m.FactorText;
            ToolTip.SetTip(this, Loc.GetF("chaster_mood_tip", m.FactorText));

            var target = CoverHeight(m);
            _anim?.Cancel();
            _anim = null;
            if (changed && wasVisible && AmbientFxCanvas.Env.Level == MotionLevel.Full)
            {
                var run = _anim = new System.Threading.CancellationTokenSource();
                var from = _cover.Height;
                _cover.Height = target;   // the rest value; the animation plays over it
                _ = Tween(_cover, HeightProperty, from, target, 420, new BackEaseOut(), run);
                _ = Tween(_word, ScaleTransform.ScaleXProperty, 1.35, 1d, 420, new ElasticEaseOut(), run);
                _ = Tween(_word, ScaleTransform.ScaleYProperty, 1.35, 1d, 420, new ElasticEaseOut(), run);
                _ = Tween(_glow, DropShadowEffect.OpacityProperty, 1d, 0.45, 600, new LinearEasing(), run);
            }
            else _cover.Height = target;
            _shown = m;
        }

        internal static System.Threading.Tasks.Task Tween(Animatable target, AvaloniaProperty property, object from, object to, int ms,
            Easing easing, System.Threading.CancellationTokenSource run)
        {
            var a = new Animation { Duration = TimeSpan.FromMilliseconds(ms), Easing = easing };
            a.Children.Add(new KeyFrame { Cue = new Cue(0), Setters = { new Setter(property, from) } });
            a.Children.Add(new KeyFrame { Cue = new Cue(1), Setters = { new Setter(property, to) } });
            return a.RunAsync(target, run.Token);
        }
    }
}
