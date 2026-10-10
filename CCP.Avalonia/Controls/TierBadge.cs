using System;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Helpers;
using Serilog;
using SkiaSharp;

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
    ///   <item>The three sign PNGs DO ship on this head - <c>CCP.Avalonia.csproj</c> links
    ///     <c>..\Assets\features\*.png</c> to <c>avares://CCP.Avalonia/Resources/features/</c> -
    ///     so the badge draws the real art, loaded once for the app the way the WPF original loads
    ///     its <c>pack://</c> copy. Direct, NOT through <c>Helpers.ModArt</c>: tier livery is
    ///     commerce chrome and the WPF badge deliberately does not route it through
    ///     <c>ModResourceResolver</c>, so a mod cannot restyle an entitlement badge. The vector
    ///     stand-in stays behind it as the missing-art fallback (WPF collapses the badge instead;
    ///     keeping the plate is strictly the gentler failure and the code was already here).</item>
    ///   <item>The reduced-motion and glow gates are wired: <see cref="AmbientAllowed"/> and
    ///     <see cref="GlowAllowed"/> ask <c>AmbientFxCanvas.Env</c>, this head's copy of
    ///     <c>MotionFx</c>/<c>PerformanceProfile</c> over the real <c>CoreSettings.Current</c>.
    ///     ponytail: the one half still missing is WPF's cap of MotionLevel to Reduced on the OS
    ///     animation flag, which Avalonia does not expose and which can only ever REMOVE motion.</item>
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

        /// <summary>The blur the WPF badge lands on when PerformanceProfile throws.</summary>
        private const double GlowBlur = 18;

        /// <summary>Aspect ratios of the three sign PNGs, used only when the art fails to load and
        /// the vector plates stand in for it. With the art present the bitmap's own aspect wins.</summary>
        private const double AspectT1 = 2.045;
        private const double AspectT2 = 2.344;
        private const double AspectStamp = 1.991;

        /// <summary>The sign plates. Each hosts the real PNG when it loads and the vector
        /// stand-in when it does not (see the class remarks); the Border stays either way,
        /// because it is what carries the transforms, the glow and the layout box.</summary>
        private readonly Border _tierSign;
        /// <summary>The neon glow: the sign's own silhouette, blurred ONCE into a bitmap and laid
        /// under it. WPF wears a DropShadowEffect; here an Effect would make the sign an offscreen
        /// layer re-rendered on every beat (FX law), so the hum breathes this picture's Opacity.</summary>
        private readonly Image _glow;
        private readonly Border _stampSign;
        private readonly Image _tierImage;
        private readonly Image _stampImage;
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

            _tierImage = BuildSignImage();
            _stampImage = BuildSignImage();

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
            _glow = new Image
            {
                Stretch = Stretch.Fill,
                IsHitTestVisible = false,
                IsVisible = false,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                RenderTransformOrigin = RelativePoint.Center,
                // The SAME group as the sign: the glow box is the sign box padded evenly, so the
                // two share a centre and swell and sway as one.
                RenderTransform = _tierSign.RenderTransform,
            };
            Children.Add(_glow);
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

            Loaded += (_, _) =>
            {
                ApplyState(); StartMotion();
                _visibilityWatch?.Dispose();
                _visibilityWatch = EffectiveVisibility.Watch(this, OnEffectiveVisibilityChanged);
                // Live motion kill-switch (WPF CmbMotionLevel_SelectionChanged re-arms/stops the badges).
                AmbientFxCanvas.Env.MotionGateChanged -= StartMotion;
                AmbientFxCanvas.Env.MotionGateChanged += StartMotion;
            };
            Unloaded += (_, _) =>
            {
                AmbientFxCanvas.Env.MotionGateChanged -= StartMotion;
                StopMotion(); _visibilityWatch?.Dispose(); _visibilityWatch = null;
            };
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
        /// Test seam: forces the motion decision instead of asking the live settings. Null (the
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

        /// <summary>
        /// MotionFx.AllowAmbientLoops, through this head's copy of it
        /// (<see cref="AmbientFxCanvas.Env"/> over the real <c>CoreSettings.Current</c>).
        /// Read at Loaded / a state change / IsVisible / <c>Env.MotionGateChanged</c>, never polled,
        /// the way WPF re-armed from MainWindow.UiUpdates.CmbMotionLevel_SelectionChanged.
        /// </summary>
        private bool AmbientAllowed => MotionOverride ?? AmbientFxCanvas.Env.AllowAmbientLoops;

        /// <summary>PerformanceProfile.AllowGlow at the live tier, through the same copy.</summary>
        private static bool GlowAllowed => AmbientFxCanvas.Env.AllowGlow(AmbientFxCanvas.Env.CurrentTier);

        /// <summary>The WPF badge's blur: the tier's ceiling, itself capped at 22.</summary>
        private static double GlowBlurRadius
        {
            get
            {
                try { return Math.Min(22, AmbientFxCanvas.Env.MaxGlowBlurRadius(AmbientFxCanvas.Env.CurrentTier)); }
                catch { return GlowBlur; }
            }
        }

        // =====================================================================================
        //  state
        // =====================================================================================

        // A badge on a hidden tab (nine on Play) must park like WPF's IsVisibleChanged parked it.
        private IDisposable? _visibilityWatch;

        private void OnEffectiveVisibilityChanged()
        {
            if (!IsEffectivelyVisible) StopMotion();
            else if (!_motionRunning) StartMotion();
        }

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
                _tierSign.Effect = null;
                _glow.IsVisible = !restamped && GlowAllowed;
                _glow.Opacity = 1.0;

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
        /// The sign itself: the tier's PNG, or the vector plate that stands in for a PNG that
        /// would not load - plate, rim, ink and the words the art bakes in.
        /// </summary>
        private void PaintSign(int tier)
        {
            if (TierArt(tier) is { } art)
            {
                _tierImage.Source = art;
                // The art carries its own rim and ground; a Border rim around it would frame the
                // sign in a rectangle the PNG does not have.
                _tierSign.Child = _tierImage;
                _tierSign.Background = null;
                _tierSign.BorderThickness = default;
                return;
            }

            bool prime = tier >= 2;
            var ink = prime ? GlowT2 : GlowT1;
            _tierSign.Child = _tierWords;
            _tierSign.BorderThickness = new Thickness(2);
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

            // The PNG is already pre-tilted ~8 degrees, so with the real art the code adds only the
            // token counter-lean, exactly as the WPF badge does. The vector plate has no baked
            // lean, so it carries those 8 degrees itself.
            var stampArt = StampArt();
            if (stampArt != null)
            {
                _stampImage.Source = stampArt;
                _stampSign.Child = _stampImage;
                _stampSign.Background = null;
                _stampSign.BorderThickness = default;
            }
            else
            {
                _stampSign.Child = _stampWords;
                _stampSign.BorderThickness = new Thickness(3);
            }

            _stampRotate.Angle = (stampArt != null ? 0 : StampBakedTilt)
                                 + (tier >= 2 ? StampExtraTiltT2 : StampExtraTiltT1);
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
                bool allow = MotionOverride ?? AmbientFxCanvas.Env.AllowTransitions;

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
                if (!IsLoaded || !IsEffectivelyVisible) return;
                if (!AmbientAllowed) return;

                _motionRunning = true;
                _beatPeriod = Tier >= 2 ? BreathT2 : BreathT1;
                _beatPrime = Tier >= 2;
                // A papered-over sign hums no more; the stamp is the star.
                _beatHum = !FreeToday;
                double tilt = TierTilt;
                // Rotation: the same period, keyframed to START at its maximum - which is the 90
                // degree phase offset the spec asks for. The badge then "breathes and settles"
                // instead of pumping scale and angle together.
                _swayKeys = new[]
                {
                    (0.00, tilt + WobbleDegrees), (0.25, tilt), (0.50, tilt - WobbleDegrees),
                    (0.75, tilt), (1.00, tilt + WobbleDegrees),
                };
                // One loop on the window's shared 30 fps beat. Four infinite Animations here kept
                // every window showing a badge composing at 60 Hz (nine badges on Play).
                _beat ??= new BeatLoop(this, StepMotion);
                _beat.Start();
            }
            catch (Exception ex) { Log.Debug("TierBadge.StartMotion: {E}", ex.Message); }
        }

        private BeatLoop? _beat;
        private double _beatPeriod = BreathT1;
        private bool _beatPrime, _beatHum;
        private (double At, double Value)[] _swayKeys = Array.Empty<(double, double)>();

        /// <summary>Tier 1's tic, WPF's keys in absolute seconds: once per 6.5 s lap a quick two-step
        /// flicker (1 -> 0.85 -> 1 -> 0.88 -> 1 inside 200 ms). A real sign's fault, NOT a strobe.</summary>
        private static readonly (double At, double Value)[] FlickerKeys =
        {
            (0.00, 1.00), (FlickerCycle - 0.20, 1.00), (FlickerCycle - 0.14, 0.85),
            (FlickerCycle - 0.08, 1.00), (FlickerCycle - 0.04, 0.88), (FlickerCycle, 1.00),
        };

        /// <summary>One beat of the hum. Writes plain properties only (two transforms, three opacities).</summary>
        private void StepMotion(double t)
        {
            if (!_motionRunning) return;
            double period = _beatPeriod;

            // Scale: starts at rest and swells; half a period each way, so one breath is `period`.
            double breath = BeatLoop.Breath(t, period / 2);
            _tierScale.ScaleX = _tierScale.ScaleY = 1.0 + ((WobbleScale - 1.0) * breath);
            _tierRotate.Angle = BeatLoop.Keys(BeatLoop.Saw(t, period), _swayKeys, sine: true);

            if (!_beatHum) return;

            // The neon hum: the glow swells 0.55 -> 1.0 on the wobble's period.
            double glow = 0.55 + (0.45 * breath);
            if (_beatPrime)
            {
                // Tier 2's two glints per cycle, at fixed offsets (never a runtime Random).
                double at = BeatLoop.Saw(t, period), life = 0.30 / period;
                _glintA.Opacity = Glint(at, 0.22, life);
                _glintB.Opacity = Glint(at, 0.64, life);
            }
            else
            {
                double flicker = Math.Clamp(BeatLoop.Keys(t % FlickerCycle, FlickerKeys), 0, 1);
                _tierSign.Opacity = flicker;
                glow *= flicker;   // in WPF the glow is the sign's own Effect, so it dips with it
            }
            _glow.Opacity = Math.Clamp(glow, 0, 1);
        }

        /// <summary>A glint's life: 0 until <paramref name="start"/>, up to 1 at half its life, back to 0.</summary>
        private static double Glint(double at, double start, double life)
        {
            double u = (at - start) / life;
            if (u <= 0 || u >= 1) return 0;
            return u < 0.5 ? u * 2 : (1 - u) * 2;
        }

        // The glow pictures, one per (tier, size, blur): a handful in the whole app.
        private static readonly System.Collections.Generic.Dictionary<(int, int, int, int), Bitmap?> GlowCache = new();

        /// <summary>
        /// The sign's silhouette in the tier's glow colour, blurred the way Skia blurs a drop shadow
        /// of that radius, with <paramref name="pad"/> of room on every side. Never throws.
        /// </summary>
        private static Bitmap? GlowArt(int tier, double width, double height, double blur, double pad)
        {
            if (tier <= 0 || width <= 0 || height <= 0 || double.IsNaN(width) || double.IsNaN(height)) return null;
            var key = (tier >= 2 ? 2 : 1, (int)Math.Round(width), (int)Math.Round(height), (int)Math.Round(blur));
            if (GlowCache.TryGetValue(key, out var cached)) return cached;
            Bitmap? made = null;
            try
            {
                int w = key.Item2, h = key.Item3, p = (int)pad;
                var tint = tier >= 2 ? GlowT2 : GlowT1;
                float sigma = blur <= 0 ? 0f : (0.288675f * (float)blur) + 0.5f;
                using var surface = SKSurface.Create(new SKImageInfo(w + (2 * p), h + (2 * p), SKColorType.Bgra8888, SKAlphaType.Premul));
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);
                using var paint = new SKPaint { IsAntialias = true };
                paint.ColorFilter = SKColorFilter.CreateBlendMode(new SKColor(tint.R, tint.G, tint.B), SKBlendMode.SrcIn);
                if (sigma > 0) paint.ImageFilter = SKImageFilter.CreateBlur(sigma, sigma);
                var box = new SKRect(p, p, p + w, p + h);
                using var art = LoadSkia(tier >= 2 ? "tier_badge_t2.png" : "tier_badge_t1.png");
                if (art != null) canvas.DrawBitmap(art, box, paint);
                else canvas.DrawRoundRect(box, 6, 6, paint);   // the vector plate's silhouette
                using var image = surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = data.AsStream();
                made = new Bitmap(stream);
            }
            catch (Exception ex) { Log.Debug("TierBadge.GlowArt: {E}", ex.Message); }
            GlowCache[key] = made;
            return made;
        }

        private static SKBitmap? LoadSkia(string file)
        {
            try
            {
                var uri = new Uri("avares://CCP.Avalonia/Resources/features/" + file);
                if (!AssetLoader.Exists(uri)) return null;
                using var stream = AssetLoader.Open(uri);
                return SKBitmap.Decode(stream);
            }
            catch { return null; }
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
                _beat?.Stop();

                _tierScale.ScaleX = _tierScale.ScaleY = 1.0;
                _tierRotate.Angle = TierTilt;
                _tierSign.Opacity = FreeToday ? DimmedTierOpacity : 1.0;

                _glow.Opacity = 1.0;

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
            // WPF gets the height from Stretch=Uniform on the bitmap; a Border has to be told, so
            // each sign is boxed at its art's own aspect - and at the measured constant when the
            // art is missing and the vector plate is standing in.
            _tierSign.Width = width;
            _tierSign.Height = width / Aspect(TierArt(Tier), Tier >= 2 ? AspectT2 : AspectT1);
            _tierWords.FontSize = Math.Max(7, Math.Round(width * 0.125));

            // The glow: the sign's box padded by the blur on every side, hung so the centres agree.
            double blur = GlowBlurRadius;
            double pad = Math.Ceiling(blur) + 2;
            _glow.Width = _tierSign.Width + (2 * pad);
            _glow.Height = _tierSign.Height + (2 * pad);
            _glow.Margin = new Thickness(0, -pad, -pad, 0);
            _glow.Source = GlowArt(Tier, _tierSign.Width, _tierSign.Height, blur, pad);

            // The stamp is deliberately a touch bigger than what it covers, and lands down-left of
            // it, so it reads as a second pass with a real rubber stamp rather than a swapped layer.
            _stampSign.Width = width * 1.06;
            _stampSign.Height = width * 1.06 / Aspect(StampArt(), AspectStamp);
            _stampSign.Margin = new Thickness(0, width * 0.09, width * 0.10, 0);
            _stampWords.FontSize = Math.Max(7, Math.Round(width * 0.15));

            // Glints sit on the sign's own box; sized off the badge so they stay proportional.
            double dot = Math.Max(3, width * 0.035);
            _glintA.Width = _glintA.Height = dot;
            _glintB.Width = _glintB.Height = dot * 0.8;
            _glintA.Margin = new Thickness(0, width * 0.10, width * 0.12, 0);
            _glintB.Margin = new Thickness(0, width * 0.30, width * 0.72, 0);
        }

        /// <summary>The bitmap's own aspect, or the measured constant when there is no bitmap.</summary>
        private static double Aspect(Bitmap? art, double fallback)
        {
            var size = art?.Size ?? default;
            return size.Width > 0 && size.Height > 0 ? size.Width / size.Height : fallback;
        }

        private static Image BuildSignImage() => new()
        {
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false,
        };

        // =====================================================================================
        //  art - loaded once for the whole app, exactly as the WPF badge loads its pack:// copy
        // =====================================================================================

        private static Bitmap? _artT1, _artT2, _artStamp;
        private static bool _artT1Tried, _artT2Tried, _artStampTried;

        internal static Bitmap? TierArt(int tier)
        {
            if (tier >= 2)
            {
                if (!_artT2Tried) { _artT2Tried = true; _artT2 = Load("tier_badge_t2.png"); }
                return _artT2;
            }
            if (!_artT1Tried) { _artT1Tried = true; _artT1 = Load("tier_badge_t1.png"); }
            return _artT1;
        }

        private static Bitmap? StampArt()
        {
            if (!_artStampTried) { _artStampTried = true; _artStamp = Load("free_today_stamp.png"); }
            return _artStamp;
        }

        /// <summary>
        /// Loads a badge PNG once and shares it with every badge in the app. Never throws: art
        /// that will not load costs the card nothing but the sign's photograph, and the vector
        /// plate takes over (see <see cref="PaintSign"/>).
        ///
        /// <para>Deliberately NOT <c>Helpers.ModArt.TryLoad</c>: that helper probes the active
        /// mod's override first, and tier livery is commerce chrome that stays constant across
        /// mods - the WPF badge reaches straight past ModResourceResolver for the same reason.</para>
        ///
        /// <para>ponytail: the fields are plain statics, written from the UI thread on the first
        /// badge to ask. Every caller is a measure or an apply pass, so there is no second writer;
        /// make them Lazy if a background loader ever wants one.</para>
        /// </summary>
        private static Bitmap? Load(string file)
        {
            try
            {
                var uri = new Uri("avares://CCP.Avalonia/Resources/features/" + file);
                if (!AssetLoader.Exists(uri)) return null;
                using var stream = AssetLoader.Open(uri);
                // Never drawn wider than MaxBadgeWidth, and the stamp at 1.06x of it; decode to
                // twice that so it stays crisp on a 200% display without carrying a 900px bitmap.
                return Bitmap.DecodeToWidth(stream, 420);
            }
            catch (Exception ex)
            {
                Log.Warning("TierBadge art missing: {File} ({E})", file, ex.Message);
                return null;
            }
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
