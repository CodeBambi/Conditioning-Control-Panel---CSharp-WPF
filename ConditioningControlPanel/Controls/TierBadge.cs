using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// THE TIER BADGE - a neon sign stamped on the corner of a card's art.
    ///
    /// <para>Owner direction (0813): these are NEON SIGNS on dark glass, not metal plaques, so the
    /// FX is light that BREATHES rather than light that reflects. The badge hums (a glow whose
    /// opacity swells 0.55 -> 1.0), it wobbles very slightly as it hums, Tier 1 throws the odd
    /// two-step flicker tic a real sign would, and Tier 2 catches a pair of glints per cycle.</para>
    ///
    /// <para><b>The art carries the words.</b> "BASIC SUBJECT" (gold) and "PRIME SUBJECT" (ice
    /// cyan) are baked into the PNGs, which is why this feature adds no localisation keys: there is
    /// no text to translate. It also means the badge must never be the only thing saying a card is
    /// gated - it is chrome on top of the lockbands and the entitlement chips, and it reads the
    /// tier, never decides it.</para>
    ///
    /// <para><b>FREE TODAY is a RE-STAMP, not a swap.</b> When the card's feature is the daily free
    /// pick, the tier badge stays but dims right down and the pink stamp lands on top of it, offset
    /// down-left - "this costs Tier 1... except today". The stamp lands with a one-shot thunk on
    /// the state CHANGE only, never on a layout pass.</para>
    ///
    /// <para><b>Gating.</b> Every clock here is ambient by the app's definition, so all of it hangs
    /// on <see cref="MotionFx.AllowAmbientLoops"/>, the glow additionally on
    /// <see cref="PerformanceProfile.AllowGlow"/>, and every timeline is capped at
    /// <see cref="AmbientFrameRate"/>. Reduced motion degrades to a STATIC tilted badge - never to
    /// a slower loop - and the clocks park whenever the badge stops being visible.</para>
    ///
    /// <para>Tier livery is commerce chrome: constant across mods, never tinted by FxTheme.</para>
    /// </summary>
    public sealed class TierBadge : Grid
    {
        private const int AmbientFrameRate = 24;

        /// <summary>Share of the host card's width the badge takes, and the clamps around it: a
        /// 336px vault card gets ~151px of badge, a 1300px hero band is held to the ceiling rather
        /// than growing a billboard.</summary>
        private const double WidthFraction = 0.45;
        private const double MinBadgeWidth = 88;
        private const double MaxBadgeWidth = 190;
        private const double FallbackWidth = 140;

        /// <summary>Static lean, mirrored between tiers so a wall of mixed badges reads
        /// hand-stamped rather than templated.</summary>
        private const double TiltT1 = -7.0;
        private const double TiltT2 = 6.0;

        /// <summary>Breathing period per tier (seconds).</summary>
        private const double BreathT1 = 4.2;
        private const double BreathT2 = 3.6;

        /// <summary>Wobble amounts: a 3% swell and a 1.2 degree sway around the static tilt.</summary>
        private const double WobbleScale = 1.03;
        private const double WobbleDegrees = 1.2;

        /// <summary>Tier 1's neon tic: one two-step flicker per border-shimmer lap.</summary>
        private const double FlickerCycle = 6.5;

        /// <summary>
        /// The stamp art is ALREADY pre-tilted about 8 degrees, so code adds a token counter-lean
        /// and no more - the "counter-tilt" of the design spec is mostly in the PNG.
        /// </summary>
        private const double StampExtraTiltT1 = 2.0;
        private const double StampExtraTiltT2 = -2.0;

        private static readonly Color GlowT1 = Color.FromRgb(0xFF, 0xD2, 0x7A);
        private static readonly Color GlowT2 = Color.FromRgb(0xBD, 0xEF, 0xFF);

        /// <summary>How far the tier badge dims under a re-stamp. Owner: "slightly visible
        /// behind" - recognisable, but plainly overruled.</summary>
        private const double DimmedTierOpacity = 0.35;

        private readonly Image _tierImage;
        private readonly Image _stampImage;
        private readonly Ellipse _glintA;
        private readonly Ellipse _glintB;

        private readonly ScaleTransform _tierScale = new(1, 1);
        private readonly RotateTransform _tierRotate = new(0);
        private readonly ScaleTransform _stampScale = new(1, 1);
        private readonly RotateTransform _stampRotate = new(0);

        private double _appliedWidth = -1;
        private bool _stampShown;
        private bool _motionRunning;

        public TierBadge()
        {
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Top;
            IsHitTestVisible = false;
            SnapsToDevicePixels = true;

            // Collapsed until somebody names a tier. Tier's DP default is already 0, so setting it
            // to 0 raises no change notification and ApplyState would never run - which is exactly
            // the state a badge declared in XAML and painted later (the vault spotlight, the
            // dashboard reveal face) sits in until its first refresh.
            Visibility = Visibility.Collapsed;

            _tierImage = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new TransformGroup { Children = { _tierScale, _tierRotate } },
            };
            Children.Add(_tierImage);

            // Tier 2's glints, parked invisible. Placed against the badge's own box in
            // ApplyWidth so they follow the art when the host card resizes.
            _glintA = BuildGlint();
            _glintB = BuildGlint();
            Children.Add(_glintA);
            Children.Add(_glintB);

            _stampImage = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new TransformGroup { Children = { _stampScale, _stampRotate } },
                Visibility = Visibility.Collapsed,
            };
            Children.Add(_stampImage);

            Loaded += (_, _) => { ApplyState(); StartMotion(); };
            Unloaded += (_, _) => StopMotion();
            IsVisibleChanged += (_, e) =>
            {
                if (e.NewValue is not true) { StopMotion(); return; }
                StartMotion();
                // The stamp lands when it APPEARS, which for a surface built while hidden (the
                // dashboard's reveal face, a vault card on a tab nobody has opened) is here rather
                // than at the state change - otherwise the thunk is spent behind a collapsed
                // panel and the stamp is simply already there when the plate turns over.
                if (FreeToday && _stampImage.Visibility == Visibility.Visible) PlayStampThunk();
            };
        }

        // =====================================================================================
        //  properties
        // =====================================================================================

        /// <summary>Which badge to wear: 1 = BASIC SUBJECT (gold), 2+ = PRIME SUBJECT (diamond),
        /// 0 = none (the badge collapses itself). The VISUAL tier, never an entitlement check.</summary>
        public static readonly DependencyProperty TierProperty =
            DependencyProperty.Register(nameof(Tier), typeof(int), typeof(TierBadge),
                new PropertyMetadata(0, OnVisualChanged));

        public int Tier
        {
            get => (int)GetValue(TierProperty);
            set => SetValue(TierProperty, value);
        }

        /// <summary>True on the day this card's feature is the daily free pick: the tier badge
        /// dims and the FREE TODAY stamp lands on top of it.</summary>
        public static readonly DependencyProperty FreeTodayProperty =
            DependencyProperty.Register(nameof(FreeToday), typeof(bool), typeof(TierBadge),
                new PropertyMetadata(false, OnVisualChanged));

        public bool FreeToday
        {
            get => (bool)GetValue(FreeTodayProperty);
            set => SetValue(FreeTodayProperty, value);
        }

        /// <summary>
        /// Test seam: forces the motion decision instead of asking <see cref="MotionFx"/>, which in
        /// a test host answers from a null <c>App.Settings</c> and would always say Full. Null (the
        /// default) means "ask the app", which is what ships.
        /// </summary>
        internal bool? MotionOverride { get; set; }

        /// <summary>True while this badge is actually running clocks. Test seam.</summary>
        internal bool IsAnimating => _motionRunning;

        internal Image TierImage => _tierImage;
        internal Image StampImage => _stampImage;
        internal double TierTilt => Tier >= 2 ? TiltT2 : TiltT1;

        private bool AmbientAllowed
        {
            get
            {
                if (MotionOverride is bool forced) return forced;
                try { return MotionFx.AllowAmbientLoops; }
                catch { return false; }
            }
        }

        private bool GlowAllowed
        {
            get
            {
                try { return PerformanceProfile.AllowGlow(PerformanceProfile.CurrentTier); }
                catch { return false; }
            }
        }

        // =====================================================================================
        //  state
        // =====================================================================================

        private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TierBadge badge) return;
            badge.ApplyState();
            badge.StartMotion();
        }

        /// <summary>
        /// Paints the resting look: art, tilt, glow, and the re-stamp composition. Motion is a
        /// separate concern (<see cref="StartMotion"/>), so this is exactly what a reduced-motion
        /// user sees.
        /// </summary>
        private void ApplyState()
        {
            try
            {
                int tier = Tier;
                if (tier <= 0)
                {
                    Visibility = Visibility.Collapsed;
                    return;
                }

                var art = TierArt(tier);
                if (art == null)
                {
                    // Missing art must cost the card nothing but the badge.
                    Visibility = Visibility.Collapsed;
                    return;
                }

                Visibility = Visibility.Visible;
                _tierImage.Source = art;
                _tierRotate.Angle = TierTilt;

                bool restamped = FreeToday;

                // The tier badge behind a stamp is dimmed and DEAD - a neon sign that has been
                // papered over does not keep humming. Its glow comes off with its brightness.
                _tierImage.Opacity = restamped ? DimmedTierOpacity : 1.0;
                if (restamped || !GlowAllowed)
                {
                    if (_tierImage.Effect is DropShadowEffect old)
                        old.BeginAnimation(DropShadowEffect.OpacityProperty, null);
                    _tierImage.ClearValue(EffectProperty);
                }
                else if (_tierImage.Effect is not DropShadowEffect)
                {
                    double blur;
                    try { blur = Math.Min(22, PerformanceProfile.MaxGlowBlurRadius(PerformanceProfile.CurrentTier)); }
                    catch { blur = 18; }
                    _tierImage.Effect = new DropShadowEffect
                    {
                        Color = tier >= 2 ? GlowT2 : GlowT1,
                        BlurRadius = blur,
                        ShadowDepth = 0,
                        Opacity = 1.0,
                    };
                }

                var glintVisibility = (tier >= 2 && !restamped) ? Visibility.Visible : Visibility.Collapsed;
                _glintA.Visibility = glintVisibility;
                _glintB.Visibility = glintVisibility;
                if (glintVisibility == Visibility.Collapsed)
                {
                    _glintA.Opacity = 0;
                    _glintB.Opacity = 0;
                }

                ApplyStamp(restamped, tier);
                InvalidateMeasure();
            }
            catch (Exception ex) { App.Logger?.Debug("TierBadge.ApplyState: {E}", ex.Message); }
        }

        private void ApplyStamp(bool restamped, int tier)
        {
            if (!restamped)
            {
                _stampImage.Visibility = Visibility.Collapsed;
                _stampImage.BeginAnimation(OpacityProperty, null);
                _stampScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                _stampScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                _stampScale.ScaleX = _stampScale.ScaleY = 1.0;
                _stampShown = false;
                return;
            }

            var stampArt = StampArt();
            if (stampArt == null)
            {
                // No stamp art: undo the dimming rather than showing a badge that has been faded
                // out for a stamp that never arrives.
                _tierImage.Opacity = 1.0;
                _stampImage.Visibility = Visibility.Collapsed;
                return;
            }

            _stampImage.Source = stampArt;
            _stampRotate.Angle = tier >= 2 ? StampExtraTiltT2 : StampExtraTiltT1;
            _stampImage.Visibility = Visibility.Visible;

            // The thunk fires on the state CHANGE only. A repaint (mod switch, entitlement
            // refresh, tab revisit) must not re-slam the stamp onto the card every time.
            if (_stampShown) return;
            _stampShown = true;
            PlayStampThunk();
        }

        /// <summary>The stamp's entrance: 1.25 -> 1.0 with a back-ease overshoot and a fade,
        /// 260ms. Interaction motion, not ambient - so it only asks AllowTransitions, and it is
        /// skipped outright when motion is off.</summary>
        private void PlayStampThunk()
        {
            try
            {
                bool allow;
                if (MotionOverride is bool forced) allow = forced;
                else { try { allow = MotionFx.AllowTransitions; } catch { allow = false; } }

                if (!allow)
                {
                    _stampImage.Opacity = 1;
                    _stampScale.ScaleX = _stampScale.ScaleY = 1.0;
                    return;
                }

                var duration = TimeSpan.FromMilliseconds(260);
                var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };

                _stampImage.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));

                var punch = new DoubleAnimation(1.25, 1.0, duration) { EasingFunction = ease };
                _stampScale.BeginAnimation(ScaleTransform.ScaleXProperty, punch);
                _stampScale.BeginAnimation(ScaleTransform.ScaleYProperty, punch);
            }
            catch (Exception ex) { App.Logger?.Debug("TierBadge.PlayStampThunk: {E}", ex.Message); }
        }

        // =====================================================================================
        //  motion
        // =====================================================================================

        /// <summary>
        /// Starts the hum: the breathing glow, the wobble, and the tier's own tic (Tier 1 flicker /
        /// Tier 2 glints). Always parks first, because this is called from every repaint and a
        /// second Begin on a badge that is already breathing would stack clocks.
        /// </summary>
        public void StartMotion()
        {
            StopMotion();
            try
            {
                if (Tier <= 0 || Visibility != Visibility.Visible) return;
                if (!IsVisible && IsLoaded) return;
                if (!AmbientAllowed) return;

                _motionRunning = true;
                double period = Tier >= 2 ? BreathT2 : BreathT1;

                // Scale: starts at rest and swells. Half-period each way, so one full breath is
                // exactly `period`.
                var swell = new DoubleAnimation(1.0, WobbleScale, TimeSpan.FromSeconds(period / 2))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(swell, AmbientFrameRate);
                _tierScale.BeginAnimation(ScaleTransform.ScaleXProperty, swell);
                _tierScale.BeginAnimation(ScaleTransform.ScaleYProperty, swell);

                // Rotation: the same period, keyframed to START at its maximum - which is the 90
                // degree phase offset the spec asks for. The badge then "breathes and settles"
                // instead of pumping scale and angle together.
                double tilt = TierTilt;
                var sway = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromSeconds(period),
                    RepeatBehavior = RepeatBehavior.Forever,
                };
                void Key(double at, double angle) =>
                    sway.KeyFrames.Add(new EasingDoubleKeyFrame(angle,
                        KeyTime.FromTimeSpan(TimeSpan.FromSeconds(period * at)),
                        new SineEase { EasingMode = EasingMode.EaseInOut }));
                Key(0.00, tilt + WobbleDegrees);
                Key(0.25, tilt);
                Key(0.50, tilt - WobbleDegrees);
                Key(0.75, tilt);
                Key(1.00, tilt + WobbleDegrees);
                Timeline.SetDesiredFrameRate(sway, AmbientFrameRate);
                _tierRotate.BeginAnimation(RotateTransform.AngleProperty, sway);

                if (FreeToday) return;   // a papered-over sign hums no more; the stamp is the star

                StartGlowBreath(period);
                if (Tier >= 2) StartGlints(period);
                else StartFlickerTic();
            }
            catch (Exception ex) { App.Logger?.Debug("TierBadge.StartMotion: {E}", ex.Message); }
        }

        /// <summary>The neon hum: the glow's opacity swells 0.55 -> 1.0 on the wobble's period.</summary>
        private void StartGlowBreath(double period)
        {
            if (_tierImage.Effect is not DropShadowEffect glow) return;
            var hum = new DoubleAnimation(0.55, 1.0, TimeSpan.FromSeconds(period / 2))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(hum, AmbientFrameRate);
            glow.BeginAnimation(DropShadowEffect.OpacityProperty, hum);
        }

        /// <summary>
        /// Tier 1's tic: once per border-shimmer lap, a quick two-step flicker (1 -> 0.85 -> 1
        /// inside 180ms). A real sign's fault, deliberately NOT a strobe - the rest of the 6.5s
        /// cycle is flat.
        /// </summary>
        private void StartFlickerTic()
        {
            var tic = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(FlickerCycle),
                RepeatBehavior = RepeatBehavior.Forever,
            };
            void Key(double seconds, double value) =>
                tic.KeyFrames.Add(new LinearDoubleKeyFrame(value,
                    KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds))));
            Key(0.00, 1.00);
            Key(FlickerCycle - 0.20, 1.00);
            Key(FlickerCycle - 0.14, 0.85);
            Key(FlickerCycle - 0.08, 1.00);
            Key(FlickerCycle - 0.04, 0.88);
            Key(FlickerCycle, 1.00);
            Timeline.SetDesiredFrameRate(tic, AmbientFrameRate);
            _tierImage.BeginAnimation(OpacityProperty, tic);
        }

        /// <summary>Tier 2's two glints per cycle, at fixed offsets - precomputed keyframes, never
        /// a runtime <c>Random</c>, so the same badge sparkles the same way twice.</summary>
        private void StartGlints(double period)
        {
            Pop(_glintA, period, 0.22);
            Pop(_glintB, period, 0.64);

            void Pop(Ellipse glint, double cycle, double at)
            {
                var life = 0.30 / cycle;
                var anim = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromSeconds(cycle),
                    RepeatBehavior = RepeatBehavior.Forever,
                };
                void Key(double t, double v) =>
                    anim.KeyFrames.Add(new LinearDoubleKeyFrame(v,
                        KeyTime.FromTimeSpan(TimeSpan.FromSeconds(cycle * Math.Clamp(t, 0, 1)))));
                Key(0, 0);
                Key(at, 0);
                Key(at + (life / 2), 1);
                Key(at + life, 0);
                Key(1, 0);
                Timeline.SetDesiredFrameRate(anim, AmbientFrameRate);
                glint.BeginAnimation(OpacityProperty, anim);
            }
        }

        /// <summary>
        /// Parks every clock and leaves the badge at its resting state: static tilt, no swell, glow
        /// at full. This IS the reduced-motion look, so it must be complete rather than "wherever
        /// the animation happened to stop".
        /// </summary>
        public void StopMotion()
        {
            try
            {
                _motionRunning = false;

                _tierScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                _tierScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                _tierScale.ScaleX = _tierScale.ScaleY = 1.0;

                _tierRotate.BeginAnimation(RotateTransform.AngleProperty, null);
                _tierRotate.Angle = TierTilt;

                _tierImage.BeginAnimation(OpacityProperty, null);
                _tierImage.Opacity = FreeToday ? DimmedTierOpacity : 1.0;

                if (_tierImage.Effect is DropShadowEffect glow)
                {
                    glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
                    glow.Opacity = 1.0;
                }

                foreach (var glint in new[] { _glintA, _glintB })
                {
                    glint.BeginAnimation(OpacityProperty, null);
                    glint.Opacity = 0;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("TierBadge.StopMotion: {E}", ex.Message); }
        }

        // =====================================================================================
        //  layout
        // =====================================================================================

        /// <summary>
        /// The badge sizes itself off the HOST's width (42-48% of the card, clamped), because the
        /// same control hangs on a 336px vault card and on a 1300px hero band. Done in measure
        /// rather than from a parent SizeChanged hook so it is correct on the very first pass, and
        /// guarded by a dead-band so setting a child's width from inside a measure cannot loop.
        /// </summary>
        protected override Size MeasureOverride(Size constraint)
        {
            double available = constraint.Width;
            double target = (double.IsInfinity(available) || double.IsNaN(available) || available <= 0)
                ? FallbackWidth
                : Math.Clamp(available * WidthFraction, MinBadgeWidth, MaxBadgeWidth);

            if (Math.Abs(target - _appliedWidth) > 0.5)
            {
                _appliedWidth = target;
                ApplyWidth(target);
            }
            return base.MeasureOverride(constraint);
        }

        private void ApplyWidth(double width)
        {
            _tierImage.Width = width;

            // The stamp is deliberately a touch bigger than what it covers, and lands down-left of
            // it, so it reads as a second pass with a real rubber stamp rather than a swapped layer.
            _stampImage.Width = width * 1.06;
            _stampImage.Margin = new Thickness(0, width * 0.09, width * 0.10, 0);

            // Glints sit on the sign's own box; sized off the badge so they stay proportional.
            double dot = Math.Max(3, width * 0.035);
            _glintA.Width = _glintA.Height = dot;
            _glintB.Width = _glintB.Height = dot * 0.8;
            _glintA.Margin = new Thickness(0, width * 0.10, width * 0.12, 0);
            _glintB.Margin = new Thickness(0, width * 0.30, width * 0.72, 0);
        }

        private static Ellipse BuildGlint() => new()
        {
            Width = 4,
            Height = 4,
            Opacity = 0,
            Fill = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };

        // =====================================================================================
        //  art
        // =====================================================================================

        private static ImageSource? _artT1;
        private static ImageSource? _artT2;
        private static ImageSource? _artStamp;
        private static bool _artT1Tried, _artT2Tried, _artStampTried;

        private static ImageSource? TierArt(int tier)
        {
            if (tier >= 2)
            {
                if (!_artT2Tried) { _artT2Tried = true; _artT2 = Load("tier_badge_t2.png"); }
                return _artT2;
            }
            if (!_artT1Tried) { _artT1Tried = true; _artT1 = Load("tier_badge_t1.png"); }
            return _artT1;
        }

        private static ImageSource? StampArt()
        {
            if (!_artStampTried) { _artStampTried = true; _artStamp = Load("free_today_stamp.png"); }
            return _artStamp;
        }

        /// <summary>
        /// Loads a badge PNG once, frozen and shared by every badge in the app. Never throws:
        /// missing art collapses the badge (see <see cref="ApplyState"/>) and the card is otherwise
        /// untouched, which is the right failure for chrome.
        /// </summary>
        private static ImageSource? Load(string file)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri("pack://application:,,,/Resources/features/" + file, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                // Never drawn wider than MaxBadgeWidth, and the stamp at 1.06x of it; decode to
                // twice that so it stays crisp on a 200% display without carrying a 900px bitmap.
                bmp.DecodePixelWidth = 420;
                bmp.EndInit();
                if (bmp.PixelWidth <= 0) return null;
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("TierBadge art missing: {File} ({E})", file, ex.Message);
                return null;
            }
        }
    }
}
