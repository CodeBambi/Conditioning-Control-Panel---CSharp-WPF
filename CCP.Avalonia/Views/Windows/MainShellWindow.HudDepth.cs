// PORTED from WPF 7.1.5 MainWindow/MainWindow.HudDepth.cs (nav polish wave 10 + 11 + 13), plus the
// XP-row juice of MainWindow.HeroFx.cs (PopLevelChip, AnimateXpMeniscus, ApplyXpMeniscusPulse) and
// MainWindow.ChromeFx.cs (ApplyXpSheen), and the title-bar version line of MainWindow.xaml.cs.
//
// The XAML carries the recipe (the band is a recess, the XP track a groove with a tube in it, the
// chip and the stat pills raised coins, the marquee a sunken drum); this file only re-tints the
// shadows from code, because a shadow takes the colour of what casts it:
//   * PaintDepthHud(hue): the recess, the groove and the drum shade, from the live SECTION hue
//     (Core NavStripRules.Accent). WPF calls it from PaintSectionWash; here the HUD listens to the
//     wash line (SectionWashLine's Background is repainted on every section change) so the nav
//     partial stays untouched. Static paint, no clocks.
//   * PaintHudChipDepth(colour): the LVL chip's drop band in the chip's own colour, from the
//     theme's PinkColor (the mod accent; Lockdown repaints PinkColor too).
// Numbers come from Core HudDepthRules / DepthRules; this head only draws.

using System;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Depth;
using ConditioningControlPanel.Nav;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private bool _hudInitialized;
        private double _xpMeniscusFillWidth;
        private CancellationTokenSource? _xpMeniscusPulse, _xpSheenLoop, _levelChipPop;

        /// <summary>One-time wiring for the header and XP row (OnLoaded). Guarded as a whole: a
        /// chrome flourish that cannot build must never stop the shell from opening.</summary>
        private void InitializeHeaderHud()
        {
            if (_hudInitialized) return;
            _hudInitialized = true;
            try
            {
                // The title bar says the version (nav polish wave 4 cut the header's own tag).
                var version = CoreReleaseContent.AppVersion;
                if (Named<TextBlock>("TxtTitleBarVersion") is { } title && !string.IsNullOrEmpty(version))
                {
                    title.Text = $"Conditioning Control Panel v{version}";
                    Title = title.Text;
                }

                if (Named<Border>("SectionWashLine") is { } line)
                    line.PropertyChanged += (_, e) =>
                    {
                        if (e.Property == Border.BackgroundProperty) PaintDepthHud(NavStripRules.Accent(_washSection));
                    };
                PaintDepthHud(NavStripRules.Accent(_washSection ?? "home"));

                if (Named<Border>("XPBar") is { } bar)
                {
                    bar.PropertyChanged += (_, e) =>
                    {
                        if (e.Property == BoundsProperty) ApplyXpBeadRule(Named<Ellipse>("XPTubeBead"), bar.Bounds.Width);
                    };
                    // The first fill may already be laid out by now (HookLevelDisplay runs first).
                    ApplyXpBeadRule(Named<Ellipse>("XPTubeBead"), double.IsNaN(bar.Width) ? bar.Bounds.Width : bar.Width);
                    AnimateXpMeniscus(double.IsNaN(bar.Width) ? bar.Bounds.Width : bar.Width, animate: false);
                }

                AmbientFxCanvas.Env.MotionGateChanged += OnHudMotionGateChanged;
                Activated += OnHudActivationChanged;
                Deactivated += OnHudActivationChanged;
                PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty) OnHudActivationChanged(this, EventArgs.Empty); };
                Closed += (_, _) =>
                {
                    AmbientFxCanvas.Env.MotionGateChanged -= OnHudMotionGateChanged;
                    _xpMeniscusPulse?.Cancel();
                    _xpSheenLoop?.Cancel();
                };

                InitializeSparkleWallet();
                RefreshPremiumSpark();
                ApplyBannerFxLoops();
                ApplyXpSheen();
            }
            catch (Exception ex) { Log.Debug("InitializeHeaderHud: {E}", ex.Message); }
        }

        private void OnHudMotionGateChanged()
        {
            try
            {
                RefreshPremiumSpark();
                ApplyBannerFxLoops();
                ApplyXpSheen();
                ApplyXpMeniscusPulse();
            }
            catch (Exception ex) { Log.Debug("OnHudMotionGateChanged: {E}", ex.Message); }
        }

        private void OnHudActivationChanged(object? sender, EventArgs e)
        {
            try
            {
                ApplyBannerFxLoops();
                ApplyXpSheen();
                ApplyXpMeniscusPulse();
                // Tier has no change event in Core; coming back to the window (after a sign-in
                // dialog, a browser Patreon round trip) is when it can have moved. Idempotent.
                if (IsActive) RefreshPremiumSpark();
                if (!IsActive) Named<global::ConditioningControlPanel.Avalonia.Controls.SparkleWallet>("HeaderSparkleWallet")?.ClosePopup();
            }
            catch (Exception ex) { Log.Debug("OnHudActivationChanged: {E}", ex.Message); }
        }

        /// <summary>WPF ChromeAmbientAllowed: the window is active and ambient loops are allowed.</summary>
        private bool HudAmbientAllowed => Pr4aAmbientAllowed && AmbientFxCanvas.Env.AllowAmbientLoops;

        // ============================== shades ==============================

        /// <summary>The HUD's recess and the XP groove take the section hue in their inner shade
        /// (WellTopAlpha on top edges, WellLeftAlpha on the groove's left lip), and the drum with them.</summary>
        internal void PaintDepthHud(uint hue)
        {
            try
            {
                var top = HudDepthRules.WellTop(hue);
                var left = HudDepthRules.WellLeft(hue);
                if (Named<Rectangle>("HudBandWellTop") is { } well) well.Fill = Fade(top, vertical: true);
                if (Named<Border>("XPGrooveTop") is { } gTop) gTop.Background = Fade(top, vertical: true);
                if (Named<Border>("XPGrooveLeft") is { } gLeft) gLeft.Background = Fade(left, vertical: false);
                PaintBannerDepth(hue);
                PaintHudChipDepth(ChipColour());
            }
            catch (Exception ex) { Log.Debug("PaintDepthHud: {E}", ex.Message); }
        }

        /// <summary>Polish wave 13: the marquee drum's shade in the section hue.</summary>
        private void PaintBannerDepth(uint hue)
        {
            if (Named<Border>("BannerDrumShade") is { } shade)
            {
                var brush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                };
                foreach (var (argb, offset) in HudDepthRules.DrumStops(hue))
                    brush.GradientStops.Add(new GradientStop(C(argb), offset));
                shade.Background = brush;
            }
            if (Named<Border>("BannerDrumLip") is { } lip)
            {
                var shadow = HudDepthRules.WellLeft(hue);
                lip.Background = new LinearGradientBrush
                {
                    // The well's left band, 10 px wide whatever the banner's width (absolute points).
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
                    EndPoint = new RelativePoint(HudDepthRules.DrumLipPx, 0, RelativeUnit.Absolute),
                    GradientStops = { new GradientStop(C(shadow), 0), new GradientStop(C(shadow & 0x00FFFFFF), 1) },
                };
            }
        }

        /// <summary>The LVL chip casts a shadow in its own colour (pulled from ink by ShadowHuePull).</summary>
        internal void PaintHudChipDepth(Color chip)
        {
            try
            {
                if (Named<Border>("LevelChipDrop") is not { } drop) return;
                uint argb = ((uint)chip.A << 24) | ((uint)chip.R << 16) | ((uint)chip.G << 8) | chip.B;
                drop.Background = Fade(HudDepthRules.ChipDrop(argb), vertical: true);
            }
            catch (Exception ex) { Log.Debug("PaintHudChipDepth: {E}", ex.Message); }
        }

        private Color ChipColour() =>
            this.TryFindResource("PinkColor", out var found) && found is Color c ? c : Color.FromRgb(0xFF, 0x69, 0xB4);

        /// <summary>A shadow fading from full at the contact edge to nothing.</summary>
        private static LinearGradientBrush Fade(uint shadow, bool vertical) => new()
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = vertical ? new RelativePoint(0, 1, RelativeUnit.Relative) : new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(C(shadow), 0), new GradientStop(C(shadow & 0x00FFFFFF), 1) },
        };

        private static Color C(uint argb) =>
            Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

        // ============================== the tube's bead + meniscus ==============================

        /// <summary>Shows or hides the bead for a fill this wide. Opacity, not visibility: the bead
        /// keeps no layout of its own to give back (WPF Visibility.Hidden).</summary>
        internal static void ApplyXpBeadRule(Control? bead, double fillWidth)
        {
            if (bead == null) return;
            var want = HudDepthRules.XpBeadVisible(fillWidth) ? 1.0 : 0.0;
            if (bead.Opacity != want) bead.Opacity = want;
        }

        /// <summary>Moves the glow dot to the fill's new tip, on the fill's own clock and curve.</summary>
        private void AnimateXpMeniscus(double toWidth, bool animate)
        {
            try
            {
                _xpMeniscusFillWidth = toWidth;
                if (Named<Border>("XPMeniscus") is not { RenderTransform: TranslateTransform slide } dot) return;
                var target = HudDepthRules.XpMeniscusX(toWidth, double.IsNaN(dot.Width) ? HudDepthRules.XpMeniscusPx : dot.Width);
                slide.Transitions = animate && AmbientFxCanvas.Env.AllowTransitions
                    ? new Transitions
                    {
                        new DoubleTransition
                        {
                            Property = TranslateTransform.XProperty,
                            Duration = TimeSpan.FromMilliseconds(HudDepthRules.XpMeniscusSlideMs),
                            Easing = new QuadraticEaseOut(),
                        },
                    }
                    : null;
                slide.X = target;
                ApplyXpMeniscusPulse();
            }
            catch (Exception ex) { Log.Debug("AnimateXpMeniscus: {E}", ex.Message); }
        }

        /// <summary>The dot's own soft pulse. Ambient, so it parks at a still opacity (not zero: a
        /// still dot on the surface is the reduced-motion version of a breathing one) and hides
        /// outright when there is no fill to sit on.</summary>
        private void ApplyXpMeniscusPulse()
        {
            try
            {
                if (Named<Border>("XPMeniscus") is not { } dot) return;
                _xpMeniscusPulse?.Cancel();
                _xpMeniscusPulse = null;
                var ambient = HudAmbientAllowed;
                var rest = HudDepthRules.XpMeniscusOpacity(_xpMeniscusFillWidth, ambientAllowed: false);
                if (rest <= 0 || !ambient) { dot.Opacity = rest; return; }
                _xpMeniscusPulse = new CancellationTokenSource();
                _ = new Animation
                {
                    Duration = TimeSpan.FromSeconds(HudDepthRules.XpMeniscusPulseSeconds),
                    IterationCount = IterationCount.Infinite,
                    PlaybackDirection = PlaybackDirection.Alternate,
                    Easing = new SineEaseInOut(),
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, HudDepthRules.XpMeniscusMinOpacity) } },
                        new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, HudDepthRules.XpMeniscusMaxOpacity) } },
                    },
                }.RunAsync(dot, _xpMeniscusPulse.Token);
            }
            catch (Exception ex) { Log.Debug("ApplyXpMeniscusPulse: {E}", ex.Message); }
        }

        /// <summary>Slow gloss travelling along the XP fill: one 6 s pass on repeat while ambient
        /// loops are allowed, parked at Opacity 0 otherwise (WPF ApplyXpSheen's three stops ride
        /// together, so the band slides as one: an offset tween on the brush's transform).</summary>
        private void ApplyXpSheen()
        {
            try
            {
                if (Named<Border>("XPBarSheen") is not { } sheen) return;
                _xpSheenLoop?.Cancel();
                _xpSheenLoop = null;
                if (sheen.Background is not LinearGradientBrush band) return;
                if (!HudAmbientAllowed) { sheen.Opacity = 0; band.Transform = null; return; }
                sheen.Opacity = 1;
                var slide = new TranslateTransform();
                band.Transform = slide;
                _xpSheenLoop = new CancellationTokenSource();
                // The stops start 0.30 left of the fill and travel 1.30 of its width per pass.
                var width = Math.Max(1, sheen.Bounds.Width);
                _ = new Animation
                {
                    Duration = TimeSpan.FromSeconds(HudDepthRules.XpSheenSeconds),
                    IterationCount = IterationCount.Infinite,
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0), Setters = { new Setter(TranslateTransform.XProperty, 0.0) } },
                        new KeyFrame { Cue = new Cue(1), Setters = { new Setter(TranslateTransform.XProperty, width * 1.30) } },
                    },
                }.RunAsync(slide, _xpSheenLoop.Token);
            }
            catch (Exception ex) { Log.Debug("ApplyXpSheen: {E}", ex.Message); }
        }

        // ============================== the LVL chip pop ==============================

        /// <summary>The LVL chip's pop on a level-up: scale to 1.35 and tilt -4 degrees in the first
        /// 30%, then settle with a BackEase. Off at motion Off.</summary>
        internal void PopLevelChip()
        {
            try
            {
                if (!AmbientFxCanvas.Env.AllowTransitions) return;
                if (Named<Border>("LevelChip")?.RenderTransform is not TransformGroup { Children.Count: 2 } g
                    || g.Children[0] is not ScaleTransform scale || g.Children[1] is not RotateTransform rotate) return;
                _levelChipPop?.Cancel();
                _levelChipPop = new CancellationTokenSource();
                var span = TimeSpan.FromMilliseconds(HudDepthRules.LevelChipPopMs);
                Animation Make(AvaloniaProperty p, double peak, double rest)
                {
                    // Quadratic out to the peak in the first 30%, BackEase out (amplitude 0.6) home.
                    var anim = new Animation { Duration = span };
                    const int steps = 12;
                    for (int i = 0; i <= steps; i++)
                    {
                        var u = (double)i / steps;
                        double v;
                        if (u <= 0.3) { var k = u / 0.3; v = rest + (peak - rest) * (1 - (1 - k) * (1 - k)); }
                        else { var k = (u - 0.3) / 0.7; var t = 1 - k; v = peak + (rest - peak) * (1 - (t * t * t - t * 0.6 * Math.Sin(t * Math.PI))); }
                        anim.Children.Add(new KeyFrame { Cue = new Cue(u), Setters = { new Setter(p, v) } });
                    }
                    return anim;
                }
                _ = Make(ScaleTransform.ScaleXProperty, HudDepthRules.LevelChipPopScale, 1).RunAsync(scale, _levelChipPop.Token);
                _ = Make(ScaleTransform.ScaleYProperty, HudDepthRules.LevelChipPopScale, 1).RunAsync(scale, _levelChipPop.Token);
                _ = Make(RotateTransform.AngleProperty, HudDepthRules.LevelChipPopDegrees, 0).RunAsync(rotate, _levelChipPop.Token);
            }
            catch (Exception ex) { Log.Debug("PopLevelChip: {E}", ex.Message); }
        }
    }
}
