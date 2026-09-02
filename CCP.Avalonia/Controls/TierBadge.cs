using System;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// THE TIER BADGE - a neon sign stamped on the corner of a card's art. Ported from the WPF
    /// head's <c>Controls/TierBadge.cs</c>; a code-only control there, a code-only control here.
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
    /// <para><b>Deviations from the WPF original</b>, all of them forced by what this head has:</para>
    /// <list type="bullet">
    ///   <item>ponytail: the three sign PNGs (<c>tier_badge_t1/t2.png</c>, <c>free_today_stamp.png</c>)
    ///     are <c>pack://</c> resources of the WPF head and this head ships no <c>Resources/</c>
    ///     art, so each sign is drawn as a vector stand-in - a rounded plate in the tier's livery
    ///     carrying the words the PNG bakes in, at the PNG's own aspect (2.045 / 2.344 / 1.991).
    ///     Swap the two plates for an <c>Image</c> on an <c>avares://</c> bitmap when the art
    ///     moves; every other number here already matches the original.</item>
    ///   <item>ponytail: needs <c>MotionFx</c> (the reduced-motion gate) and <c>PerformanceProfile</c>
    ///     (AllowGlow / MaxGlowBlurRadius), both still in the WPF head, so this badge always hums
    ///     and always wears its glow. Restore the gates in <see cref="AmbientAllowed"/> and
    ///     <see cref="GlowAllowed"/> when they move to Core - the call sites are already there.</item>
    ///   <item>WPF <c>Timeline.SetDesiredFrameRate(24)</c> has no Avalonia twin; the ambient clocks
    ///     run at the compositor's rate.</item>
    /// </list>
    ///
    /// <para>Tier livery is commerce chrome: constant across mods, never tinted by FxTheme.</para>
    /// </summary>
    public sealed class TierBadge : Grid
    {
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
        /// and no more - the "counter-tilt" of the design spec is mostly in the PNG. The vector
        /// stand-in has no baked lean of its own, so it carries that 8 degrees in
        /// <see cref="StampBakedTilt"/> and the composed angle is the one the original renders.
        /// </summary>
        private const double StampExtraTiltT1 = 2.0;
        private const double StampExtraTiltT2 = -2.0;
        private const double StampBakedTilt = -8.0;

        private static readonly Color GlowT1 = Color.FromRgb(0xFF, 0xD2, 0x7A);
        private static readonly Color GlowT2 = Color.FromRgb(0xBD, 0xEF, 0xFF);

        /// <summary>How far the tier badge dims under a re-stamp. Owner: "slightly visible
        /// behind" - recognisable, but plainly overruled.</summary>
        private const double DimmedTierOpacity = 0.35;

        /// <summary>The blur the WPF badge lands on when PerformanceProfile is unavailable.</summary>
        private const double GlowBlur = 18;

        /// <summary>Aspect ratios of the three sign PNGs, so the vector plates match their boxes.</summary>
        private const double AspectT1 = 2.045;
        private const double AspectT2 = 2.344;
        private const double AspectStamp = 1.991;

        /// <summary>The sign plates, standing in for the PNGs (see the class remarks).</summary>
        private readonly Border _tierSign;
        private readonly Border _stampSign;
        private readonly TextBlock _tierWords;
        private readonly TextBlock _stampWords;
        private readonly Ellipse _glintA;
        private readonly Ellipse _glintB;

        /// <summary>
        /// The wobble's moving parts. Written directly to park the badge (see <see cref="StopMotion"/>)
        /// and animated by the clocks in <see cref="StartMotion"/>.
        ///
        /// <para><b>Animate the SIGN, never the transform.</b> Avalonia's <c>TransformAnimator</c> is
        /// handed the host <c>Visual</c> and resolves the transform itself, walking that visual's
        /// <c>RenderTransform</c> for the child whose type matches the animated property's owner. So
        /// a <c>ScaleTransform.ScaleX</c> keyframe is run against <c>_tierSign</c>, and the animator
        /// finds <c>_tierScale</c> inside its <c>TransformGroup</c>. Pass the transform itself and it
        /// casts straight to <c>Visual</c> and throws <c>InvalidCastException</c> - into the
        /// surrounding <c>catch</c>, which is how this control shipped with silently dead motion.</para>
        ///
        /// <para>The two tier clocks land on different children (scale on one, angle on the other),
        /// so they compose rather than race. Do NOT collapse the group into
        /// <c>TransformOperations</c> to make it one property: <c>TransformAnimator</c> bails out on
        /// a <c>TransformOperations</c> render transform, and no animator is registered for
        /// <c>ITransform</c> at all, so keyframing <c>Visual.RenderTransform</c> throws too
        /// (verified against Avalonia 12.1.1).</para>
        /// </summary>
        private readonly ScaleTransform _tierScale = new(1, 1);
        private readonly RotateTransform _tierRotate = new(0);
        private readonly ScaleTransform _stampScale = new(1, 1);
        private readonly RotateTransform _stampRotate = new(0);

        private CancellationTokenSource? _ambient;
        private CancellationTokenSource? _thunk;
        private double _appliedWidth = -1;
        private bool _stampShown;
        private bool _motionRunning;

        public TierBadge()
        {
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Top;
            IsHitTestVisible = false;

            // Hidden until somebody names a tier. Tier's default is already 0, so setting it to 0
            // raises no change notification and ApplyState would never run - which is exactly the
            // state a badge declared in XAML and painted later (the vault spotlight, the dashboard
            // reveal face) sits in until its first refresh.
            IsVisible = false;

            _tierWords = new TextBlock
            {
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            };

            _tierSign = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                RenderTransformOrigin = RelativePoint.Center,
                RenderTransform = new TransformGroup { Children = { _tierScale, _tierRotate } },
                Child = _tierWords,
            };
            Children.Add(_tierSign);

            // Tier 2's glints, parked invisible. Placed against the badge's own box in
            // ApplyWidth so they follow the art when the host card resizes.
            _glintA = BuildGlint();
            _glintB = BuildGlint();
            Children.Add(_glintA);
            Children.Add(_glintB);

            // The stamp's own words are baked into free_today_stamp.png upstream: no loc key
            // exists for them and this port invents none.
            _stampWords = new TextBlock
            {
                Text = "FREE TODAY",
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC2, 0xDE)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            _stampSign = new Border
            {
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(3),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x4F, 0xA3)),
                Background = new SolidColorBrush(Color.FromArgb(0x59, 0x8C, 0x0F, 0x45)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                RenderTransformOrigin = RelativePoint.Center,
                RenderTransform = new TransformGroup { Children = { _stampScale, _stampRotate } },
                IsVisible = false,
                Child = _stampWords,
            };
            Children.Add(_stampSign);

            Loaded += (_, _) => { ApplyState(); StartMotion(); };
            Unloaded += (_, _) => StopMotion();
        }

        // =====================================================================================
        //  properties
        // =====================================================================================

        /// <summary>Which badge to wear: 1 = BASIC SUBJECT (gold), 2+ = PRIME SUBJECT (diamond),
        /// 0 = none (the badge hides itself). The VISUAL tier, never an entitlement check.</summary>
        public static readonly StyledProperty<int> TierProperty =
            AvaloniaProperty.Register<TierBadge, int>(nameof(Tier), 0);

        public int Tier
        {
            get => GetValue(TierProperty);
            set => SetValue(TierProperty, value);
        }

        /// <summary>
        /// Per-host ceiling on the badge's rendered width, overriding <see cref="MaxBadgeWidth"/>.
        /// NaN (the default) means "use the shared ceiling", which is what every card wants.
        ///
        /// It exists for a host that overlays the badge on its own text rather than on open art.
        /// The DTRH hero on the Play tab is the one such surface: its title block is vertically
        /// centred in a 200px band, so at the full 190px ceiling the sign hangs ~81px down the
        /// left edge and lands squarely on "DOWN THE RABBIT HOLE" (user report, v6.8.6). Capping
        /// the width there lifts the whole sign clear instead of hiding either element.
        /// </summary>
        public static readonly StyledProperty<double> MaxWidthOverrideProperty =
            AvaloniaProperty.Register<TierBadge, double>(nameof(MaxWidthOverride), double.NaN);

        public double MaxWidthOverride
        {
            get => GetValue(MaxWidthOverrideProperty);
            set => SetValue(MaxWidthOverrideProperty, value);
        }

        /// <summary>True on the day this card's feature is the daily free pick: the tier badge
        /// dims and the FREE TODAY stamp lands on top of it.</summary>
        public static readonly StyledProperty<bool> FreeTodayProperty =
            AvaloniaProperty.Register<TierBadge, bool>(nameof(FreeToday), false);

        public bool FreeToday
        {
            get => GetValue(FreeTodayProperty);
            set => SetValue(FreeTodayProperty, value);
        }

        /// <summary>
        /// Test seam: forces the motion decision instead of asking <c>MotionFx</c>, which in a test
        /// host answers from a null <c>App.Settings</c> and would always say Full. Null (the
        /// default) means "ask the app", which is what ships.
        /// </summary>
        internal bool? MotionOverride { get; set; }

        /// <summary>True while this badge is actually running clocks. Test seam; keeps the WPF
        /// name, hence `new` - Avalonia already has an <c>IsAnimating(AvaloniaProperty)</c> method,
        /// which this badge has no caller for.</summary>
        internal new bool IsAnimating => _motionRunning;

        internal Border TierSign => _tierSign;
        internal Border StampSign => _stampSign;
        internal double TierTilt => Tier >= 2 ? TiltT2 : TiltT1;

        private bool AmbientAllowed
        {
            get
            {
                if (MotionOverride is bool forced) return forced;
                // ponytail: needs MotionFx.AllowAmbientLoops, wired when it moves to Core
                return true;
            }
        }

        // ponytail: needs PerformanceProfile.AllowGlow, wired when it moves to Core
        private static bool GlowAllowed => true;

        // =====================================================================================
        //  state
        // =====================================================================================

        /// <summary>
        /// The WPF original hangs on three <c>PropertyChangedCallback</c>s plus
        /// <c>IsVisibleChanged</c>; Avalonia routes the lot through one override.
        /// </summary>
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (_tierSign is null) return;   // a base setter fired during construction

            if (change.Property == TierProperty
                || change.Property == MaxWidthOverrideProperty
                || change.Property == FreeTodayProperty)
            {
                ApplyState();
                StartMotion();
            }
            else if (change.Property == IsVisibleProperty)
            {
                if (!IsVisible) { StopMotion(); return; }
                StartMotion();
                // The stamp lands when it APPEARS, which for a surface built while hidden (the
                // dashboard's reveal face, a vault card on a tab nobody has opened) is here rather
                // than at the state change - otherwise the thunk is spent behind a hidden panel
                // and the stamp is simply already there when the plate turns over.
                if (FreeToday && _stampSign.IsVisible) PlayStampThunk();
            }
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
                    IsVisible = false;
                    return;
                }

                IsVisible = true;
                PaintSign(tier);
                _tierRotate.Angle = TierTilt;

                bool restamped = FreeToday;

                // The tier badge behind a stamp is dimmed and DEAD - a neon sign that has been
                // papered over does not keep humming. Its glow comes off with its brightness.
                _tierSign.Opacity = restamped ? DimmedTierOpacity : 1.0;
                if (restamped || !GlowAllowed)
                {
                    _tierSign.Effect = null;
                }
                else if (_tierSign.Effect is not DropShadowEffect)
                {
                    _tierSign.Effect = new DropShadowEffect
                    {
                        Color = tier >= 2 ? GlowT2 : GlowT1,
                        BlurRadius = GlowBlur,
                        OffsetX = 0,
                        OffsetY = 0,
                        Opacity = 1.0,
                    };
                }

                bool glints = tier >= 2 && !restamped;
                _glintA.IsVisible = glints;
                _glintB.IsVisible = glints;
                if (!glints)
                {
                    _glintA.Opacity = 0;
                    _glintB.Opacity = 0;
                }

                ApplyStamp(restamped, tier);
                // The tier decides the plate's aspect, so a tier change has to re-run ApplyWidth
                // even at an unchanged width. (WPF got the height from the bitmap and did not.)
                _appliedWidth = -1;
                InvalidateMeasure();
            }
            catch (Exception ex) { Log.Debug("TierBadge.ApplyState: {E}", ex.Message); }
        }

        /// <summary>
        /// The livery the PNG bakes in: plate, rim, ink and the words.
        /// ponytail: the whole method collapses to <c>_tierImage.Source = &lt;avares bitmap&gt;</c>
        /// when the art ships on this head.
        /// </summary>
        private void PaintSign(int tier)
        {
            bool prime = tier >= 2;
            var ink = prime ? GlowT2 : GlowT1;
            _tierSign.BorderBrush = new SolidColorBrush(ink);
            _tierSign.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x0D, 0x0B, 0x14));
            _tierWords.Foreground = new SolidColorBrush(ink);
            // Baked into the art upstream, so there is no loc key and this port invents none.
            _tierWords.Text = prime ? "PRIME\nSUBJECT" : "BASIC\nSUBJECT";
        }

        private void ApplyStamp(bool restamped, int tier)
        {
            if (!restamped)
            {
                _thunk?.Cancel();
                _thunk = null;
                _stampSign.IsVisible = false;
                _stampSign.Opacity = 1;
                _stampScale.ScaleX = _stampScale.ScaleY = 1.0;
                _stampShown = false;
                return;
            }

            _stampRotate.Angle = StampBakedTilt + (tier >= 2 ? StampExtraTiltT2 : StampExtraTiltT1);
            _stampSign.IsVisible = true;

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
                // ponytail: needs MotionFx.AllowTransitions, wired when it moves to Core
                bool allow = MotionOverride ?? true;

                _thunk?.Cancel();
                _thunk = null;

                if (!allow)
                {
                    _stampSign.Opacity = 1;
                    _stampScale.ScaleX = _stampScale.ScaleY = 1.0;
                    return;
                }

                _thunk = new CancellationTokenSource();
                var fade = new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(180),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, 0d) } },
                        new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, 1d) } },
                    },
                };
                _ = fade.RunAsync(_stampSign, _thunk.Token);

                var punch = new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(260),
                    Easing = new BackEaseOut(),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame
                        {
                            Cue = new Cue(0d),
                            Setters =
                            {
                                new Setter(ScaleTransform.ScaleXProperty, 1.25),
                                new Setter(ScaleTransform.ScaleYProperty, 1.25),
                            },
                        },
                        new KeyFrame
                        {
                            Cue = new Cue(1d),
                            Setters =
                            {
                                new Setter(ScaleTransform.ScaleXProperty, 1.0),
                                new Setter(ScaleTransform.ScaleYProperty, 1.0),
                            },
                        },
                    },
                };
                // The SIGN, not _stampScale - the animator resolves the transform (see the fields).
                _ = punch.RunAsync(_stampSign, _thunk.Token);
            }
            catch (Exception ex) { Log.Debug("TierBadge.PlayStampThunk: {E}", ex.Message); }
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
                if (Tier <= 0 || !IsVisible) return;
                if (IsLoaded && !IsEffectivelyVisible) return;
                if (!AmbientAllowed) return;

                _motionRunning = true;
                _ambient = new CancellationTokenSource();
                var token = _ambient.Token;
                double period = Tier >= 2 ? BreathT2 : BreathT1;

                // Scale: starts at rest and swells. Half a period each way (Alternate), so one
                // full breath is exactly `period`.
                var swell = new Animation
                {
                    Duration = TimeSpan.FromSeconds(period / 2),
                    IterationCount = IterationCount.Infinite,
                    PlaybackDirection = PlaybackDirection.Alternate,
                    Easing = new SineEaseInOut(),
                    Children =
                    {
                        new KeyFrame
                        {
                            Cue = new Cue(0d),
                            Setters =
                            {
                                new Setter(ScaleTransform.ScaleXProperty, 1.0),
                                new Setter(ScaleTransform.ScaleYProperty, 1.0),
                            },
                        },
                        new KeyFrame
                        {
                            Cue = new Cue(1d),
                            Setters =
                            {
                                new Setter(ScaleTransform.ScaleXProperty, WobbleScale),
                                new Setter(ScaleTransform.ScaleYProperty, WobbleScale),
                            },
                        },
                    },
                };
                // The SIGN, not _tierScale - the animator resolves the transform (see the fields).
                _ = swell.RunAsync(_tierSign, token);

                // Rotation: the same period, keyframed to START at its maximum - which is the 90
                // degree phase offset the spec asks for. The badge then "breathes and settles"
                // instead of pumping scale and angle together.
                double tilt = TierTilt;
                var sway = new Animation
                {
                    Duration = TimeSpan.FromSeconds(period),
                    IterationCount = IterationCount.Infinite,
                    Easing = new SineEaseInOut(),
                };
                void Key(double at, double angle) =>
                    sway.Children.Add(new KeyFrame
                    {
                        Cue = new Cue(at),
                        Setters = { new Setter(RotateTransform.AngleProperty, angle) },
                    });
                Key(0.00, tilt + WobbleDegrees);
                Key(0.25, tilt);
                Key(0.50, tilt - WobbleDegrees);
                Key(0.75, tilt);
                Key(1.00, tilt + WobbleDegrees);
                // Same sign as the swell: the two clocks reach different children of its group.
                _ = sway.RunAsync(_tierSign, token);

                if (FreeToday) return;   // a papered-over sign hums no more; the stamp is the star

                StartGlowBreath(period, token);
                if (Tier >= 2) StartGlints(period, token);
                else StartFlickerTic(token);
            }
            catch (Exception ex) { Log.Debug("TierBadge.StartMotion: {E}", ex.Message); }
        }

        /// <summary>The neon hum: the glow's opacity swells 0.55 -> 1.0 on the wobble's period.</summary>
        private void StartGlowBreath(double period, CancellationToken token)
        {
            if (_tierSign.Effect is not DropShadowEffect glow) return;
            var hum = new Animation
            {
                Duration = TimeSpan.FromSeconds(period / 2),
                IterationCount = IterationCount.Infinite,
                PlaybackDirection = PlaybackDirection.Alternate,
                Easing = new SineEaseInOut(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(DropShadowEffect.OpacityProperty, 0.55) } },
                    new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(DropShadowEffect.OpacityProperty, 1.0) } },
                },
            };
            _ = hum.RunAsync(glow, token);
        }

        /// <summary>
        /// Tier 1's tic: once per border-shimmer lap, a quick two-step flicker (1 -> 0.85 -> 1
        /// inside 180ms). A real sign's fault, deliberately NOT a strobe - the rest of the 6.5s
        /// cycle is flat.
        /// </summary>
        private void StartFlickerTic(CancellationToken token)
        {
            var tic = new Animation
            {
                Duration = TimeSpan.FromSeconds(FlickerCycle),
                IterationCount = IterationCount.Infinite,
                Easing = new LinearEasing(),
            };
            void Key(double seconds, double value) =>
                tic.Children.Add(new KeyFrame
                {
                    // WPF keys in absolute time; Avalonia keys in a 0..1 cue over the same duration.
                    Cue = new Cue(Math.Clamp(seconds / FlickerCycle, 0, 1)),
                    Setters = { new Setter(OpacityProperty, value) },
                });
            Key(0.00, 1.00);
            Key(FlickerCycle - 0.20, 1.00);
            Key(FlickerCycle - 0.14, 0.85);
            Key(FlickerCycle - 0.08, 1.00);
            Key(FlickerCycle - 0.04, 0.88);
            Key(FlickerCycle, 1.00);
            _ = tic.RunAsync(_tierSign, token);
        }

        /// <summary>Tier 2's two glints per cycle, at fixed offsets - precomputed keyframes, never
        /// a runtime <c>Random</c>, so the same badge sparkles the same way twice.</summary>
        private void StartGlints(double period, CancellationToken token)
        {
            Pop(_glintA, period, 0.22);
            Pop(_glintB, period, 0.64);

            void Pop(Ellipse glint, double cycle, double at)
            {
                var life = 0.30 / cycle;
                var anim = new Animation
                {
                    Duration = TimeSpan.FromSeconds(cycle),
                    IterationCount = IterationCount.Infinite,
                    Easing = new LinearEasing(),
                };
                void Key(double t, double v) =>
                    anim.Children.Add(new KeyFrame
                    {
                        Cue = new Cue(Math.Clamp(t, 0, 1)),
                        Setters = { new Setter(OpacityProperty, v) },
                    });
                Key(0, 0);
                Key(at, 0);
                Key(at + (life / 2), 1);
                Key(at + life, 0);
                Key(1, 0);
                _ = anim.RunAsync(glint, token);
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
                _ambient?.Cancel();
                _ambient = null;

                _tierScale.ScaleX = _tierScale.ScaleY = 1.0;
                _tierRotate.Angle = TierTilt;
                _tierSign.Opacity = FreeToday ? DimmedTierOpacity : 1.0;

                if (_tierSign.Effect is DropShadowEffect glow) glow.Opacity = 1.0;

                foreach (var glint in new[] { _glintA, _glintB }) glint.Opacity = 0;
            }
            catch (Exception ex) { Log.Debug("TierBadge.StopMotion: {E}", ex.Message); }
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
            double ceiling = MaxWidthOverride;
            if (double.IsNaN(ceiling) || ceiling <= 0) ceiling = MaxBadgeWidth;
            // The floor gives way to a tighter ceiling: a host that asked for a smaller sign than
            // MinBadgeWidth means it, and clamping back up would put the overlap straight back.
            double floor = Math.Min(MinBadgeWidth, ceiling);

            double target = (double.IsInfinity(available) || double.IsNaN(available) || available <= 0)
                ? Math.Clamp(FallbackWidth, floor, ceiling)
                : Math.Clamp(available * WidthFraction, floor, ceiling);

            if (Math.Abs(target - _appliedWidth) > 0.5)
            {
                _appliedWidth = target;
                ApplyWidth(target);
            }
            return base.MeasureOverride(constraint);
        }

        private void ApplyWidth(double width)
        {
            // WPF gets the height from Stretch=Uniform on the bitmap; a vector plate has to be
            // told, so each sign keeps its PNG's aspect (see the class remarks).
            _tierSign.Width = width;
            _tierSign.Height = width / (Tier >= 2 ? AspectT2 : AspectT1);
            _tierWords.FontSize = Math.Max(7, Math.Round(width * 0.125));

            // The stamp is deliberately a touch bigger than what it covers, and lands down-left of
            // it, so it reads as a second pass with a real rubber stamp rather than a swapped layer.
            _stampSign.Width = width * 1.06;
            _stampSign.Height = width * 1.06 / AspectStamp;
            _stampSign.Margin = new Thickness(0, width * 0.09, width * 0.10, 0);
            _stampWords.FontSize = Math.Max(7, Math.Round(width * 0.15));

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
            IsVisible = false,
            IsHitTestVisible = false,
        };
    }
}
