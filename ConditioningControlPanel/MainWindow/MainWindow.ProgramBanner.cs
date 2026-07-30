using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Program;

namespace ConditioningControlPanel
{
    // -------------------------------------------------------------------------------------------
    // Dashboard "Today" card - banner art and ambient FX.
    //
    // Split out of MainWindow.ProgramsTab.cs because this is pure decoration: the card's DATA is
    // painted there, and nothing in here may change what the card says or whether it is shown.
    //
    // ENTRY POINT: ApplyProgramBannerArt(program, accent), called once per repaint from
    // RefreshProgramTodayCard(). Until that call exists the decoration host stays Collapsed and the
    // card renders exactly as it did before this file - that is the designed fallback, not an
    // accident, and it is also what happens when no banner PNG resolves.
    //
    // THE SHAPE OF THE CANVAS is the whole design constraint. The card measures 1459 x 58.92 in the
    // fixed 1489 x 901 design canvas - a 24.8:1 strip. An illustration cropped into that is a
    // meaningless horizontal sliver (this is exactly how the previous banner batch failed), so the
    // art contract here is a PRE-FLATTENED 24.6:1 texture: full-colour, vertically homogeneous, with
    // the left ~45% already faded to alpha 0 in the file so the title and day line sit on clean
    // surface. See Resources/programs/banner_*.png and the converter that produces them. Nothing is
    // cropped at runtime, so the artwork's horizontal composition survives intact.
    //
    // COST BUDGET: eight idle animations total (fog 1, sheen 1, sparkles 5, accent breath 1), every
    // one of them a transform or an opacity - no per-frame CPU raster, no bitmap effects. They are
    // started and stopped from ProgramTodayCard.IsVisible, so a dashboard the user cannot see costs
    // nothing.
    // -------------------------------------------------------------------------------------------
    public partial class MainWindow
    {
        #region Dashboard program banner

        /// <summary>Folder inside Resources/ (and inside a mod's resources/) that holds banner art.</summary>
        private const string ProgramBannerFolder = "programs";

        /// <summary>
        /// Art opacity. Low enough that 13px body text stays legible where the two overlap, high
        /// enough that the right two-thirds of the card reads as themed rather than tinted.
        /// </summary>
        private const double ProgramBannerArtOpacity = 0.45;

        /// <summary>Extra travel the art gets on hover, in design units. Deliberately small.</summary>
        private const double ProgramBannerParallax = 10;

        private bool _programBannerHooked;
        private bool _programBannerFxRunning;
        private bool _programBannerSparklesBuilt;

        // -----------------------------------------------------------------------------------
        // Entry point
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Dresses the dashboard Today card for a program: resolves its banner art, rebuilds the
        /// accent-derived FX brushes, and starts the ambient animation if the card is on screen.
        ///
        /// Safe to call on every repaint - resolved images are frozen and cached by
        /// <see cref="ModResourceResolver"/>, the brushes are frozen, and the animations are only
        /// (re)started when they are not already running.
        /// </summary>
        /// <param name="program">The running program, or null to strip the decoration.</param>
        /// <param name="accent">The program's accent brush, as already computed by the caller.</param>
        internal void ApplyProgramBannerArt(ProgramDefinition? program, Brush? accent)
        {
            try
            {
                var dash = SettingsTab;
                var decor = dash?.ProgramTodayDecor;
                if (dash == null || decor == null) return;

                EnsureProgramBannerHooks(dash);

                if (program == null)
                {
                    StopProgramBannerFx();
                    decor.Visibility = Visibility.Collapsed;
                    return;
                }

                var color = ProgramBannerAccentColor(accent);

                // Art. Missing art is a normal state: the layer collapses and only the FX remain,
                // which still reads as a finished card rather than a broken one.
                var art = ResolveProgramBannerArt(program);
                if (art != null)
                {
                    var brush = new ImageBrush(art)
                    {
                        Stretch = Stretch.UniformToFill,
                        AlignmentX = AlignmentX.Center,
                        AlignmentY = AlignmentY.Center
                    };
                    brush.Freeze();
                    dash.ProgramTodayArt.Fill = brush;
                    dash.ProgramTodayArt.Opacity = ProgramBannerArtOpacity;
                    dash.ProgramTodayArt.Visibility = Visibility.Visible;
                }
                else
                {
                    dash.ProgramTodayArt.Fill = null;
                    dash.ProgramTodayArt.Visibility = Visibility.Collapsed;
                }

                dash.ProgramTodayFog.Fill = ProgramBannerFogBrush(color);
                dash.ProgramTodaySheen.Fill = ProgramBannerSheenBrush();
                BuildProgramBannerSparkles(dash, color);

                decor.Visibility = Visibility.Visible;
                LayoutProgramBannerDecor(dash);
                StartProgramBannerFx();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "ApplyProgramBannerArt failed");
            }
        }

        // -----------------------------------------------------------------------------------
        // Wiring
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// One-shot event hookup. Done here rather than from a Loaded handler in the view because
        /// the view's code-behind belongs to the dashboard, not to this feature, and because the
        /// hooks are meaningless until a program is actually running.
        /// </summary>
        private void EnsureProgramBannerHooks(Views.Tabs.SettingsTabView dash)
        {
            if (_programBannerHooked) return;

            var card = dash.ProgramTodayCard;
            if (card == null) return;

            // IsVisible is false both when the card itself is Collapsed and when the Dashboard tab
            // is - one hook covers "no program", "another tab", and "window closing" alike.
            card.IsVisibleChanged += ProgramBannerCard_IsVisibleChanged;
            card.MouseEnter += ProgramBannerCard_MouseEnter;
            card.MouseLeave += ProgramBannerCard_MouseLeave;
            dash.ProgramTodayDecor.SizeChanged += ProgramBannerDecor_SizeChanged;

            _programBannerHooked = true;
        }

        private void ProgramBannerCard_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                if (e.NewValue is true) StartProgramBannerFx();
                else StopProgramBannerFx();
            }
            catch { /* visibility bookkeeping must never throw */ }
        }

        /// <summary>
        /// Re-clips and re-places the decoration, and picks up the FX that the first
        /// ApplyProgramBannerArt could not start: on the very first paint the host has not been
        /// measured yet, so ActualWidth is 0 and StartProgramBannerFx correctly declines.
        /// </summary>
        private void ProgramBannerDecor_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                LayoutProgramBannerDecor(SettingsTab);
                StartProgramBannerFx();
            }
            catch { /* decoration */ }
        }

        /// <summary>
        /// Sizes and clips the decoration to the card's current box.
        ///
        /// The rounded Clip is load-bearing, not polish: the fog is wider than the card and the
        /// sheen slides past both ends, so without it they paint into the four corner squares that
        /// sit outside the border's 10px radius. Radius 9 = the border's 10 minus its 1px thickness.
        /// </summary>
        private void LayoutProgramBannerDecor(Views.Tabs.SettingsTabView? dash)
        {
            var decor = dash?.ProgramTodayDecor;
            if (dash == null || decor == null) return;

            var w = decor.ActualWidth;
            var h = decor.ActualHeight;
            if (w < 1 || h < 1) { decor.Clip = null; return; }

            var clip = new RectangleGeometry(new Rect(0, 0, w, h), 9, 9);
            clip.Freeze();
            decor.Clip = clip;

            // The fog is deliberately oversized so that at either end of its travel it still covers
            // the card - a fog band with a visible edge is a moving rectangle, not fog.
            dash.ProgramTodayFog.Width = w * 1.6;

            PositionProgramBannerSparkles(dash, w, h);
        }

        // -----------------------------------------------------------------------------------
        // Art lookup
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Banner art for a program. The lookup chain lives in <see cref="ProgramArt.Banner"/> so
        /// the browse catalogue resolves the exact same art; the Today card keeps the generic
        /// fallback because it shows one program at a time.
        /// </summary>
        /// <returns>A frozen ImageSource, or null - the caller collapses the art layer.</returns>
        private static ImageSource? ResolveProgramBannerArt(ProgramDefinition? program)
            => ProgramArt.Banner(program);

        // -----------------------------------------------------------------------------------
        // Accent-derived brushes
        // -----------------------------------------------------------------------------------

        private static Color ProgramBannerAccentColor(Brush? accent)
        {
            if (accent is SolidColorBrush solid) return solid.Color;
            return Color.FromRgb(0xFF, 0x69, 0xB4);
        }

        private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        /// <summary>Accent lightened toward white - a sparkle at full accent reads as a dot, not a glint.</summary>
        private static Color TowardWhite(Color c, double amount)
        {
            byte Mix(byte v) => (byte)Math.Round(v + (255 - v) * amount);
            return Color.FromRgb(Mix(c.R), Mix(c.G), Mix(c.B));
        }

        /// <summary>
        /// Two soft accent bands separated by clear gaps. Two rather than one so the drift never
        /// settles into an obvious back-and-forth of a single blob.
        /// </summary>
        private static Brush ProgramBannerFogBrush(Color accent)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            brush.GradientStops.Add(new GradientStop(WithAlpha(accent, 0x00), 0.00));
            brush.GradientStops.Add(new GradientStop(WithAlpha(accent, 0x22), 0.24));
            brush.GradientStops.Add(new GradientStop(WithAlpha(accent, 0x00), 0.48));
            brush.GradientStops.Add(new GradientStop(WithAlpha(accent, 0x18), 0.74));
            brush.GradientStops.Add(new GradientStop(WithAlpha(accent, 0x00), 1.00));
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// White, not accent: the sheen is a highlight passing over the card, and tinting it makes
        /// it read as a second fog band instead.
        /// </summary>
        private static Brush ProgramBannerSheenBrush()
        {
            var white = Colors.White;
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            brush.GradientStops.Add(new GradientStop(WithAlpha(white, 0x00), 0.0));
            brush.GradientStops.Add(new GradientStop(WithAlpha(white, 0x1A), 0.5));
            brush.GradientStops.Add(new GradientStop(WithAlpha(white, 0x00), 1.0));
            brush.Freeze();
            return brush;
        }

        // -----------------------------------------------------------------------------------
        // FX - each block below is independently removable.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Starts the ambient FX. No-op unless the card is genuinely on screen and laid out:
        /// animating a control whose template has not been applied is one of this project's
        /// standing crash sources.
        /// </summary>
        private void StartProgramBannerFx()
        {
            if (_programBannerFxRunning) return;

            try
            {
                var dash = SettingsTab;
                var card = dash?.ProgramTodayCard;
                var decor = dash?.ProgramTodayDecor;
                if (dash == null || card == null || decor == null) return;
                if (!card.IsLoaded || card.Template == null) return;
                if (!card.IsVisible || decor.Visibility != Visibility.Visible) return;

                var w = decor.ActualWidth;
                if (w < 80) return;

                // -- FX 1: drifting fog -------------------------------------------------------
                dash.ProgramTodayFog.Visibility = Visibility.Visible;
                dash.ProgramTodayFogSlide.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(0, -w * 0.55, TimeSpan.FromSeconds(26))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                    });

                // -- FX 2: sheen sweep --------------------------------------------------------
                // One keyframe animation rather than a Storyboard with a delay: the long flat tail
                // IS the pause, so there is exactly one clock and nothing to schedule.
                var sweep = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(11) };
                sweep.KeyFrames.Add(new LinearDoubleKeyFrame(-160, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                sweep.KeyFrames.Add(new LinearDoubleKeyFrame(w + 60, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.7))));
                sweep.KeyFrames.Add(new DiscreteDoubleKeyFrame(-160, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.75))));
                sweep.KeyFrames.Add(new LinearDoubleKeyFrame(-160, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(11))));
                sweep.RepeatBehavior = RepeatBehavior.Forever;
                dash.ProgramTodaySheen.Visibility = Visibility.Visible;
                dash.ProgramTodaySheenSlide.BeginAnimation(TranslateTransform.XProperty, sweep);

                // -- FX 3: sparkles -----------------------------------------------------------
                StartProgramBannerSparkles(dash);

                // -- FX 4: accent-bar breath --------------------------------------------------
                // The obvious target is the day NUMBER, but the day line is one composed string
                // built in MainWindow.ProgramsTab.cs; the accent bar is the card's other
                // program-identity mark and it costs one animation on an element that already
                // exists.
                dash.ProgramTodayAccent.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0.55, 1.0, TimeSpan.FromSeconds(3.4))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                    });

                _programBannerFxRunning = true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Program banner FX start failed");
            }
        }

        /// <summary>Stops every FX clock and parks the layers. Must never throw - it runs on teardown.</summary>
        private void StopProgramBannerFx()
        {
            if (!_programBannerFxRunning) return;
            _programBannerFxRunning = false;

            try
            {
                var dash = SettingsTab;
                if (dash == null) return;

                dash.ProgramTodayFogSlide.BeginAnimation(TranslateTransform.XProperty, null);
                dash.ProgramTodayFog.Visibility = Visibility.Collapsed;

                dash.ProgramTodaySheenSlide.BeginAnimation(TranslateTransform.XProperty, null);
                dash.ProgramTodaySheen.Visibility = Visibility.Collapsed;

                foreach (var child in dash.ProgramTodaySparkles.Children)
                {
                    if (child is UIElement sparkle) sparkle.BeginAnimation(UIElement.OpacityProperty, null);
                }
                dash.ProgramTodaySparkles.Visibility = Visibility.Collapsed;

                dash.ProgramTodayAccent.BeginAnimation(UIElement.OpacityProperty, null);
                dash.ProgramTodayAccent.Opacity = 1;

                dash.ProgramTodayArtSlide.BeginAnimation(TranslateTransform.XProperty, null);
                dash.ProgramTodayArtSlide.X = 0;
            }
            catch { /* stopping decoration must never throw */ }
        }

        // -- FX 3 detail: sparkles ----------------------------------------------------------

        /// <summary>
        /// Five ellipses, built once and re-tinted per program. Five is the whole budget: each one
        /// carries a single opacity clock, so the sparkle layer is five animations and no layout.
        /// </summary>
        private void BuildProgramBannerSparkles(Views.Tabs.SettingsTabView dash, Color accent)
        {
            var host = dash.ProgramTodaySparkles;
            var fill = new SolidColorBrush(TowardWhite(accent, 0.55));
            fill.Freeze();

            if (!_programBannerSparklesBuilt)
            {
                double[] sizes = { 4, 3, 5, 3, 4 };
                foreach (var size in sizes)
                {
                    host.Children.Add(new Ellipse
                    {
                        Width = size,
                        Height = size,
                        Opacity = 0,
                        IsHitTestVisible = false
                    });
                }
                _programBannerSparklesBuilt = true;
            }

            foreach (var child in host.Children)
            {
                if (child is Ellipse dot) dot.Fill = fill;
            }
        }

        /// <summary>
        /// Sparkle placement, as fractions of the card box. They live in the right 55% only - the
        /// left is the title's, and a glint behind 11px bold text is noise.
        /// </summary>
        private void PositionProgramBannerSparkles(Views.Tabs.SettingsTabView dash, double w, double h)
        {
            double[] fx = { 0.52, 0.63, 0.72, 0.84, 0.93 };
            double[] fy = { 0.30, 0.68, 0.22, 0.56, 0.40 };

            var host = dash.ProgramTodaySparkles;
            for (var i = 0; i < host.Children.Count && i < fx.Length; i++)
            {
                if (host.Children[i] is not Ellipse dot) continue;
                Canvas.SetLeft(dot, w * fx[i]);
                Canvas.SetTop(dot, h * fy[i]);
            }
        }

        private void StartProgramBannerSparkles(Views.Tabs.SettingsTabView dash)
        {
            var host = dash.ProgramTodaySparkles;
            host.Visibility = Visibility.Visible;

            for (var i = 0; i < host.Children.Count; i++)
            {
                if (host.Children[i] is not Ellipse dot) continue;

                // 0.45s in, 0.85s out, then dark for the rest of the 6s cycle. Staggered so no two
                // ever peak together, which is what separates "sparkle" from "blinking lights".
                var twinkle = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromSeconds(6),
                    BeginTime = TimeSpan.FromSeconds(i * 1.15),
                    RepeatBehavior = RepeatBehavior.Forever
                };
                twinkle.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                twinkle.KeyFrames.Add(new EasingDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.45)))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
                twinkle.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.3))));
                twinkle.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(6))));

                dot.BeginAnimation(UIElement.OpacityProperty, twinkle);
            }
        }

        // -- FX 5: hover parallax ------------------------------------------------------------

        /// <summary>
        /// A small push on the art under the cursor. The whole card is a link and this is the only
        /// FX that says so; it costs nothing when the pointer is elsewhere.
        /// </summary>
        private void ProgramBannerCard_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
            => NudgeProgramBannerArt(-ProgramBannerParallax);

        private void ProgramBannerCard_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
            => NudgeProgramBannerArt(0);

        private void NudgeProgramBannerArt(double to)
        {
            try
            {
                var dash = SettingsTab;
                if (dash?.ProgramTodayArtSlide == null) return;
                if (dash.ProgramTodayArt.Visibility != Visibility.Visible) return;

                dash.ProgramTodayArtSlide.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(to, TimeSpan.FromMilliseconds(260))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    });
            }
            catch { /* decoration */ }
        }

        #endregion
    }
}
