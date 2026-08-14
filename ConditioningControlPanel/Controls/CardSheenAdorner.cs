using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// A slow "glass catches the light" sheen that travels across exactly ONE card.
    ///
    /// It is drawn in the adorner layer rather than inside the card on purpose: the cards it
    /// decorates are a mix of XAML Borders and code-built Borders whose Child is already a Grid or
    /// a StackPanel, and injecting a band into each of them would mean rewriting three unrelated
    /// card builders, fighting every card's Padding (a child band stops at the padding box, not the
    /// card edge), and remembering to tear the band back out when the selection moves. An adorner
    /// costs the card nothing - no element in its visual tree, no layout pass - and moving the
    /// sheen is a detach plus an attach.
    ///
    /// House rules (FX plan section 2):
    ///   * ambient clock. One 12s cycle = ~1.3s of travel and ~10.7s at rest, which is the "slow,
    ///     >= 10s period" the plan asks for and one clock rather than a timer plus a one-shot.
    ///   * the caller gates on <see cref="MotionFx.AllowAmbientLoops"/> before attaching; this type
    ///     never starts a clock it was not asked for, and <see cref="Stop"/> parks it invisible.
    ///   * colour comes from <see cref="FxTheme"/>, re-read on EVERY <see cref="Start"/>, so a mod
    ///     switch re-tints the sheen as soon as the caller restarts it. It used to be read once in
    ///     the constructor and baked into the stops, which this comment already claimed it was not:
    ///     launching under Dronification's green and switching to BambiSleep left every vault card
    ///     glimmering green until the app was restarted, because the adorners are built once and
    ///     then only ever re-Started. The stops and the brush are deliberately left unfrozen for
    ///     this - freezing either would make the re-tint throw.
    ///   * every animation is started with BeginAnimation on the GradientStop itself - the stops
    ///     live in a brush that is not in any name scope, so Storyboard.SetTargetName could not
    ///     reach them even if we wanted it to.
    /// </summary>
    internal sealed class CardSheenAdorner : Adorner
    {
        /// <summary>Seconds for one pass of the band, and the full cycle including its rest. The
        /// long flat tail IS the pause - one clock, nothing to schedule.</summary>
        private const double TravelSeconds = 1.3;
        private const double CycleSeconds = 12.0;
        private const int AmbientFrameRate = 24;
        private const byte PeakAlpha = 0x4A;

        // Rest offsets: the whole band sits before the brush's 0 mark, so every visible offset
        // resolves to the transparent tail colour and the card renders exactly as it shipped.
        private static readonly double[] RestOffsets = { -0.34, -0.17, 0.0 };
        private const double Travel = 1.40;

        private readonly GradientStop[] _stops;
        private readonly LinearGradientBrush _brush;
        private readonly double _cornerRadius;

        internal CardSheenAdorner(UIElement adorned, double cornerRadius) : base(adorned)
        {
            IsHitTestVisible = false;
            _cornerRadius = Math.Max(0, cornerRadius);

            _stops = new[]
            {
                new GradientStop(Colors.Transparent, RestOffsets[0]),
                new GradientStop(Colors.Transparent, RestOffsets[1]),
                new GradientStop(Colors.Transparent, RestOffsets[2]),
            };
            // Angled rather than vertical: a band that leans reads as light across glass, a
            // straight one reads as a loading bar.
            _brush = new LinearGradientBrush(new GradientStopCollection(_stops),
                                             new Point(0, 0), new Point(1, 0.65));
            // The band rests off the visible range until Start(), so this only matters for the
            // one frame between construction and the caller's Start - but a transparent brush on
            // screen is the wrong kind of wrong, so paint it now as well.
            ApplyTint();
        }

        /// <summary>
        /// Rewrites the three stops from the active mod's <see cref="FxTheme.GlowColor"/>. Called
        /// from the constructor and again from every <see cref="Start"/>, which is what makes a
        /// mod switch land: the offsets are animated, the colours never are, so writing them while
        /// a clock is running is safe.
        /// </summary>
        private void ApplyTint()
        {
            var tint = FxTheme.GlowColor;
            var clear = Color.FromArgb(0, tint.R, tint.G, tint.B);
            _stops[0].Color = clear;
            _stops[1].Color = Color.FromArgb(PeakAlpha, tint.R, tint.G, tint.B);
            _stops[2].Color = clear;
        }

        /// <summary>Starts (or restarts) the travelling band. The caller owns the ambient gate.</summary>
        internal void Start()
        {
            try
            {
                ApplyTint();

                for (int i = 0; i < _stops.Length; i++)
                {
                    double from = RestOffsets[i];
                    var sweep = new DoubleAnimationUsingKeyFrames
                    {
                        Duration = TimeSpan.FromSeconds(CycleSeconds),
                        RepeatBehavior = RepeatBehavior.Forever,
                    };
                    sweep.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                    sweep.KeyFrames.Add(new LinearDoubleKeyFrame(from + Travel,
                        KeyTime.FromTimeSpan(TimeSpan.FromSeconds(TravelSeconds))));
                    sweep.KeyFrames.Add(new DiscreteDoubleKeyFrame(from,
                        KeyTime.FromTimeSpan(TimeSpan.FromSeconds(TravelSeconds + 0.05))));
                    sweep.KeyFrames.Add(new LinearDoubleKeyFrame(from,
                        KeyTime.FromTimeSpan(TimeSpan.FromSeconds(CycleSeconds))));
                    Timeline.SetDesiredFrameRate(sweep, AmbientFrameRate);
                    _stops[i].BeginAnimation(GradientStop.OffsetProperty, sweep);
                }
            }
            catch (Exception ex) { App.Logger?.Debug("CardSheenAdorner.Start: {E}", ex.Message); }
        }

        /// <summary>Drops the clock and parks the band off the visible range.</summary>
        internal void Stop()
        {
            try
            {
                for (int i = 0; i < _stops.Length; i++)
                {
                    _stops[i].BeginAnimation(GradientStop.OffsetProperty, null);
                    _stops[i].Offset = RestOffsets[i];
                }
            }
            catch (Exception ex) { App.Logger?.Debug("CardSheenAdorner.Stop: {E}", ex.Message); }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            try
            {
                var size = AdornedElement.RenderSize;
                if (size.Width <= 0 || size.Height <= 0) return;
                drawingContext.DrawRoundedRectangle(_brush, null, new Rect(size),
                                                    _cornerRadius, _cornerRadius);
            }
            catch (Exception ex) { App.Logger?.Debug("CardSheenAdorner.OnRender: {E}", ex.Message); }
        }
    }
}
