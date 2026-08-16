using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// THE FUSE RAIL CHIP — a gold clock in the nav rail's pinned bottom stack that appears seven
    /// days before the migration ceremony, shakes harder the closer it gets, and leaves for good the
    /// moment this account takes their ceremony.
    ///
    /// <para><b>SELF-WIRING, like <see cref="SpiralRailHost"/>.</b> MainWindow.xaml adds the element
    /// and nothing else: no init call, no line in the startup sequence, no partial to keep in step.
    /// A Collapsed element still raises Loaded, so the fuse-dark case reaches <see cref="Wire"/>,
    /// reads <see cref="DescentFusePhase.Dark"/> and returns having built no clock and measured no
    /// pixels. That is every install on today's server.</para>
    ///
    /// <para><b>FUSEGOLD ONLY. The accent is untouchable.</b> Every colour in this file is a literal
    /// (see <c>DescentFuseChrome</c>'s class doc and MainWindow.DescentFuse.cs's FuseGold note):
    /// the fuse is the APP's own colour, not the mod's, and a chip that took the active mod's pink
    /// would make the countdown look like part of whatever skin happened to be loaded. Do not
    /// "helpfully" swap these for DynamicResource accents.</para>
    ///
    /// <para><b>It is NOT a door medallion.</b> It deliberately does not carry the
    /// <c>Viewbox Grid.Column="0" Width="40" Height="40"</c> shape a rail door has — partly because
    /// it is not one (CacheNavDoorRows never walks it, ChromeFx never lights it, there is no "you
    /// are here" ring for a countdown), and partly because NavRailFlyoutTests scrapes MainWindow.xaml
    /// for exactly that markup and counts eight. This control is code-built, so it contributes no
    /// markup at all.</para>
    ///
    /// <para><b>The label rides the rail's own fade.</b> The chip's second column holds a mono digit
    /// readout that is only legible while the rail is open. It is NOT tagged
    /// <c>navdoorlabel</c>: that tag is how <c>CacheNavDoorRows</c> finds a DOOR's label host among a
    /// door Button's content children, and this is not a door button — the tag would do nothing. What
    /// does work is doing nothing at all: <c>CacheNavRailParts</c> walks the whole NavSidebar visual
    /// tree and adds every TextBlock it finds to the global label fade, and this control's children
    /// exist from its constructor, so the digits fade in and out with every other rail label for
    /// free. See MainWindow.NavRail.cs.</para>
    ///
    /// <para><b>Transform and opacity only, built per instance.</b> The wobble is a RotateTransform
    /// on the glyph and an Opacity on a ring; nothing here touches layout, and both are constructed
    /// in this instance's constructor rather than set from a Style — a Style-set transform is ONE
    /// shared frozen instance across every element that wears it, which is the trap
    /// CacheNavDoorRows documents at MainWindow.NavRail.cs:418.</para>
    /// </summary>
    public sealed class DescentFuseRailChip : Grid
    {
        // ============================== the palette (literals, always) ==============================

        /// <summary>The fuse's gold. Same value as MainWindow.DescentFuse.cs's FuseGold, restated
        /// rather than shared because that one is private to a MainWindow partial and this control
        /// must not need a window to paint itself.</summary>
        private static readonly Color FuseGold = Color.FromRgb(0xE0, 0xB0, 0x52);

        private static readonly Brush GoldBrush = Frozen(new SolidColorBrush(FuseGold));
        private static readonly Brush TileFill = Frozen(new SolidColorBrush(Color.FromRgb(0x23, 0x1E, 0x33)));
        private static readonly Brush TileEdge = Frozen(new SolidColorBrush(Color.FromRgb(0x8A, 0x6D, 0x3B)));
        private static readonly Brush RingEdge = Frozen(new SolidColorBrush(FuseGold));
        private static readonly Brush NeutralDigits = Frozen(new SolidColorBrush(Color.FromRgb(0xC9, 0xC4, 0xD6)));
        private static readonly Brush FaintDigits = Frozen(new SolidColorBrush(Color.FromArgb(0x8A, 0xC9, 0xC4, 0xD6)));

        // ============================== the shape ==============================

        /// <summary>Door rhythm: the rail's medallion column is 56 wide and its tiles are 44 with an
        /// r12 corner, so the chip lands on the same centre line as every row above it.</summary>
        private const double MedallionColumn = 56;
        private const double TileSize = 44;
        private const double RingSize = 50;

        // ============================== the wobble ==============================

        /// <summary>One burst: a damped shake that spends itself in a bit over half a second.</summary>
        private const double BurstPeakDegrees = 8.0;

        /// <summary>Whisper's cadence. Long enough that catching it feels like noticing something.</summary>
        private const double RareBurstPeriodSeconds = 20.0;

        /// <summary>Clock through Candle. The same gesture, more often.</summary>
        private const double FrequentBurstPeriodSeconds = 9.0;

        /// <summary>Vigil's continuous tremble: +/- 3 degrees, a full cycle every nine tenths.</summary>
        private const double TrembleDegrees = 3.0;
        private const double TrembleHalfCycleSeconds = 0.45;

        /// <summary>Terminal. Same amplitude, four times the rate — the last ten minutes are the one
        /// time this countdown raises its voice, and it does it with speed, not with size.</summary>
        private const double ViolentHalfCycleSeconds = 0.11;

        // ============================== the flash-out ==============================
        //
        // Owner's script for zero, 2026-08-16: "Timer goes to 0, starts flashing, and we run the
        // cerimony." The chip does not simply vanish when the countdown it carries runs out - it
        // spends two and a half seconds going out loudly, and only then collapses. Opacity and
        // transform ONLY, like everything else in this file.

        /// <summary>The whole goodbye. Long enough to be seen from the corner of an eye, short
        /// enough that the ceremony (which the director opens on the same instant) is not kept
        /// waiting behind a rail element throwing a fit.</summary>
        private const double FlashOutSeconds = 2.5;

        /// <summary>The shake, in device-independent pixels either side of centre. Six is roughly
        /// twice the tremble's visual throw and it is the one moment this chip is allowed to be
        /// alarming.</summary>
        private const double FlashShakePixels = 6.0;

        /// <summary>The glyph's rotation during the flash-out. Two and a half times the ordinary
        /// tremble.</summary>
        private const double FlashSpinDegrees = 7.5;

        /// <summary>The flicker and the shake are the loudest thing in the rail, so they get the
        /// urgent clock rather than the ambient one.</summary>
        private const int FlashFrameRate = 50;

        private const double RingIdleOpacity = 0.30;
        private const double RingBreathLo = 0.18;
        private const double RingBreathHi = 0.55;
        private const double RingFlareLo = 0.25;
        private const double RingFlareHi = 0.90;
        private const double RingBreathSeconds = 1.9;
        private const double RingFlareSeconds = 0.28;

        /// <summary>Capped like every other ambient clock in the app (MotionFx.AmbientFrameRate).</summary>
        private const int AmbientFrameRate = 30;

        /// <summary>The terminal tremble only: a 0.22s cycle at 30fps is seven frames and reads as a
        /// stutter rather than a shake.</summary>
        private const int UrgentFrameRate = 50;

        // ============================== the parts ==============================

        private readonly Border _ring;
        private readonly Path _glyph;
        private readonly RotateTransform _rotate = new(0);

        /// <summary>The flash-out's shake, on the WHOLE chip rather than on the glyph. Built here
        /// per instance for the same reason <see cref="_rotate"/> is: a transform set from a Style
        /// is one shared frozen instance across every element wearing it.</summary>
        private readonly TranslateTransform _shake = new(0, 0);

        private readonly TextBlock _label;

        private TextBlock? _tipDigits;
        private TextBlock? _tipPresence;
        private ToolTip? _tip;
        private DispatcherTimer? _flashTimer;

        private bool _wired;
        private bool _animating;
        private bool _flashingOut;
        private DescentFusePhase _phase = DescentFusePhase.Dark;

        public DescentFuseRailChip()
        {
            // Dark by default. A build with no cached ceremony timestamp measures as if this element
            // were not in the tree at all, which is the same promise SpiralRailHost makes.
            Visibility = Visibility.Collapsed;
            Margin = new Thickness(0, 2, 0, 4);
            Cursor = Cursors.Hand;
            Background = Brushes.Transparent;   // hit-test the whole row, not just the glyph
            RenderTransform = _shake;           // the flash-out's shake; identity until then

            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MedallionColumn) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // ---- the ring: a thin gold hairline OUTSIDE the tile, so its opacity can breathe
            // without touching the tile's own edge.
            _ring = new Border
            {
                Width = RingSize,
                Height = RingSize,
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                BorderBrush = RingEdge,
                Opacity = 0,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            SetColumn(_ring, 0);
            Children.Add(_ring);

            _glyph = new Path
            {
                Width = 22,
                Height = 22,
                Stretch = Stretch.Uniform,
                Fill = GoldBrush,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Data = BuildClockGeometry(),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = _rotate,
            };

            var tile = new Border
            {
                Width = TileSize,
                Height = TileSize,
                CornerRadius = new CornerRadius(12),
                Background = TileFill,
                BorderThickness = new Thickness(1),
                BorderBrush = TileEdge,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = _glyph,
            };
            SetColumn(tile, 0);
            Children.Add(tile);

            // ---- the open-rail readout. Opacity 0 is the SHUT state and is authored here so a rail
            // whose Loaded hook never ran (InitializeNavRail is in a try/catch for exactly that
            // reason) does not leave digits floating over the page.
            _label = new TextBlock
            {
                // Cascadia first, Consolas behind it: the digits must not reflow as they count down,
                // and a proportional fallback would jitter the readout every second.
                FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
                FontSize = 12,
                Foreground = GoldBrush,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(2, 0, 8, 0),
                Opacity = 0,
                IsHitTestVisible = false,
                Text = string.Empty,
            };
            SetColumn(_label, 1);
            Children.Add(_label);

            MouseLeftButtonUp += (_, _) => OpenSpiralRoom();
            Loaded += (_, _) => Wire();
            Unloaded += (_, _) => Unwire();
        }

        // ============================== wiring ==============================

        /// <summary>
        /// Subscribe to the countdown and paint the state we are already in. Idempotent.
        ///
        /// <para>The seed matters: the service announces its first phase in <c>Start()</c>, which
        /// runs in App.OnStartup before MainWindow exists, so that announcement had no subscribers.
        /// <see cref="DescentCountdownService.LastAnnouncedPhase"/> is how a late subscriber catches
        /// up — the same trick MainWindow.DescentFuse.cs uses.</para>
        /// </summary>
        private void Wire()
        {
            if (_wired) return;
            _wired = true;
            try
            {
                var fuse = App.DescentCountdown;
                if (fuse is null) { Apply(DescentFusePhase.Dark); return; }

                fuse.PhaseChanged += OnPhaseChanged;
                fuse.Tick += OnTick;
                Apply(fuse.LastAnnouncedPhase);
            }
            catch (Exception ex) { App.Logger?.Debug("[Fuse] rail chip could not wire: {E}", ex.Message); }
        }

        private void Unwire()
        {
            if (!_wired) return;
            _wired = false;
            try
            {
                var fuse = App.DescentCountdown;
                if (fuse != null)
                {
                    fuse.PhaseChanged -= OnPhaseChanged;
                    fuse.Tick -= OnTick;
                }
            }
            catch { /* teardown races are not worth a log line */ }
            // A chip torn out of the rail mid-goodbye finishes the goodbye immediately rather than
            // leaving a DispatcherTimer holding a reference to an unloaded element.
            if (_flashingOut) FinishFlashOut();
            StopWobble();
        }

        private void OnPhaseChanged(object? sender, DescentFusePhaseChangedEventArgs e)
        {
            // CLAUDE.md async rules 6/8: a queued event can land on a dispatcher that is going away.
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;
            try { Apply(e.Current); }
            catch (Exception ex) { App.Logger?.Debug("[Fuse] rail chip phase repaint: {E}", ex.Message); }
        }

        private void OnTick(object? sender, TimeSpan remaining)
        {
            if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;
            try
            {
                if (Visibility != Visibility.Visible) return;
                // Mid-goodbye the digits are not a readout any more, they are part of the flicker.
                // Retyping them would also be the one thing that could make the chip reflow.
                if (_flashingOut) return;

                var text = DescentFuseCopy.TMinus(remaining);
                _label.Text = text;

                // Retyped IN PLACE, never rebuilt: a tooltip torn down and re-created under a
                // stationary pointer flickers once a second. Same rule as BuildFuseTooltip.
                if (_tipDigits != null) _tipDigits.Text = text;
                ApplyPresence();
            }
            catch (Exception ex) { App.Logger?.Debug("[Fuse] rail chip tick: {E}", ex.Message); }
        }

        // ============================== the state ==============================

        /// <summary>
        /// The whole chip, computed from scratch for a phase — so arriving at any phase (a jump
        /// backwards when the owner moves the date, or straight to Dark when the kill switch fires)
        /// lands on a correct, complete state rather than on the accumulated result of whatever
        /// transitions happened to be taken.
        /// </summary>
        private void Apply(DescentFusePhase phase)
        {
            var previous = _phase;
            _phase = phase;

            // The flash-out owns the chip for its two and a half seconds. A repaint that arrived
            // mid-goodbye (a tick, a settings write) must not restart the clocks under it or, worse,
            // collapse it early and swallow the one moment the countdown was built to have.
            if (_flashingOut) return;

            bool show = SpiralRoom.FuseChipVisible(
                phase, App.Settings?.Current?.DescentMigrationCompleted == true);

            if (!show)
            {
                // ZERO, LIVE, WITH SOMEBODY WATCHING. The gate above has already decided the chip is
                // leaving; this decides whether it leaves loudly. The Visibility test is the third
                // guard on top of SpiralRoom.ShouldFlashOutAtZero's two: an account that took its
                // ceremony during the final hour has a chip that was collapsed by the migration flag
                // several minutes ago, and it has nothing left to flash.
                if (Visibility == Visibility.Visible &&
                    SpiralRoom.ShouldFlashOutAtZero(previous, phase))
                {
                    BeginFlashOut();
                    return;
                }

                HideChip();
                return;
            }

            Visibility = Visibility.Visible;

            var remaining = App.DescentCountdown?.Remaining ?? TimeSpan.Zero;
            var text = DescentFuseCopy.TMinus(remaining);
            _label.Text = text;

            ToolTip = BuildTooltip();
            if (_tipDigits != null)
            {
                _tipDigits.Text = text;
                // The corner readout turns its digits gold at Terminal and so does this: the last
                // ten minutes are the only time the countdown changes colour.
                _tipDigits.Foreground = phase >= DescentFusePhase.Terminal ? GoldBrush : NeutralDigits;
            }
            ApplyPresence();

            StartWobble(SpiralRoom.WobbleFor(phase));
        }

        /// <summary>The chip, gone and cleaned up. The tooltip is dropped rather than kept because a
        /// countdown that has ended has nothing left to say under a pointer.</summary>
        private void HideChip()
        {
            Visibility = Visibility.Collapsed;
            StopWobble();
            ToolTip = null;
            _tip = null;
            _tipDigits = null;
            _tipPresence = null;
            _label.Text = string.Empty;
        }

        /// <summary>
        /// "N falling with you", from Vigil on, and only when the server actually said N.
        ///
        /// <para><b>Zero is a reading</b> (server lane, PR #44): the server OMITS the field when out
        /// of window rather than sending 0, so a 0 that arrives is a real measurement and is shown.
        /// Only null collapses the line. Never invented client-side — a fabricated count is a lie
        /// about other people.</para>
        /// </summary>
        private void ApplyPresence()
        {
            if (_tipPresence is null) return;

            var count = _phase >= DescentFusePhase.Vigil ? App.DescentCountdown?.VigilCount : null;
            if (count is null or < 0)
            {
                _tipPresence.Visibility = Visibility.Collapsed;
                return;
            }

            _tipPresence.Text = DescentFuseCopy.Presence(count.Value);
            _tipPresence.Visibility = Visibility.Visible;
        }

        // ============================== the tooltip ==============================

        /// <summary>
        /// Built ONCE and kept, so <see cref="OnTick"/> can retype the digits in place rather than
        /// tearing the tooltip down under the pointer. Same pattern (and the same house chrome) as
        /// MainWindow.DescentFuse.cs's BuildFuseTooltip.
        ///
        /// <para><c>HasDropShadow=false</c> because App.xaml forces it app-wide; do not override.</para>
        /// </summary>
        private ToolTip BuildTooltip()
        {
            if (_tip != null) return _tip;

            _tipDigits = new TextBlock
            {
                FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
                FontSize = 15,
                Foreground = NeutralDigits,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            var subtitle = new TextBlock
            {
                Text = DescentFuseCopy.ChipSubtitle,
                FontSize = 9,
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = FaintDigits,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            System.Windows.Documents.Typography.SetCapitals(subtitle, FontCapitals.SmallCaps);

            _tipPresence = new TextBlock
            {
                FontSize = 10,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = FaintDigits,
                HorizontalAlignment = HorizontalAlignment.Center,
                Visibility = Visibility.Collapsed,
            };

            var stack = new StackPanel();
            stack.Children.Add(_tipDigits);
            stack.Children.Add(subtitle);
            stack.Children.Add(_tipPresence);

            _tip = new ToolTip
            {
                Content = stack,
                Background = Frozen(new SolidColorBrush(Color.FromArgb(0xE0, 0x0A, 0x05, 0x14))),
                BorderBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0xE0, 0xB0, 0x52))),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 4, 9, 4),
                HasDropShadow = false,
            };
            return _tip;
        }

        // ============================== the wobble ==============================

        /// <summary>
        /// Start (or re-start) the clocks for a tier. Reduced motion gets a STATIC chip, never a
        /// missing one — the countdown is information, and the shake is the only part of it that is
        /// decoration.
        /// </summary>
        private void StartWobble(FuseWobbleTier tier)
        {
            StopWobble();
            if (tier == FuseWobbleTier.None) return;

            if (!MotionFx.AllowAmbientLoops)
            {
                _rotate.Angle = 0;
                _ring.Opacity = RingIdleOpacity;
                return;
            }

            _animating = true;

            switch (tier)
            {
                case FuseWobbleTier.Rare:
                    _ring.Opacity = RingIdleOpacity;
                    BeginBurst(RareBurstPeriodSeconds);
                    break;

                case FuseWobbleTier.Frequent:
                    _ring.Opacity = RingIdleOpacity;
                    BeginBurst(FrequentBurstPeriodSeconds);
                    break;

                case FuseWobbleTier.Tremble:
                    BeginTremble(TrembleHalfCycleSeconds, AmbientFrameRate);
                    BeginRing(RingBreathLo, RingBreathHi, RingBreathSeconds, AmbientFrameRate);
                    break;

                case FuseWobbleTier.Violent:
                    BeginTremble(ViolentHalfCycleSeconds, UrgentFrameRate);
                    BeginRing(RingFlareLo, RingFlareHi, RingFlareSeconds, UrgentFrameRate);
                    break;
            }
        }

        /// <summary>
        /// One damped shake per period, then stillness until the next one. A key-framed clock rather
        /// than a timer: a DispatcherTimer restarting a Storyboard every twenty seconds is a wake-up
        /// the app has to keep paying for, and this is one clock the compositor already owns.
        /// </summary>
        private void BeginBurst(double periodSeconds)
        {
            var anim = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(periodSeconds),
                RepeatBehavior = RepeatBehavior.Forever,
            };
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };

            void At(double seconds, double angle) =>
                anim.KeyFrames.Add(new EasingDoubleKeyFrame(angle,
                    KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds)), ease));

            At(0.00, 0);
            At(0.06, BurstPeakDegrees);
            At(0.16, -BurstPeakDegrees * 0.80);
            At(0.26, BurstPeakDegrees * 0.55);
            At(0.36, -BurstPeakDegrees * 0.32);
            At(0.46, BurstPeakDegrees * 0.15);
            At(0.58, 0);
            // Flat to the end of the period. Both ends are zero, so this is stillness, not a jump.
            anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(periodSeconds))));

            Timeline.SetDesiredFrameRate(anim, AmbientFrameRate);
            _rotate.BeginAnimation(RotateTransform.AngleProperty, anim);
        }

        private void BeginTremble(double halfCycleSeconds, int frameRate)
        {
            var anim = new DoubleAnimation(-TrembleDegrees, TrembleDegrees,
                TimeSpan.FromSeconds(halfCycleSeconds))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(anim, frameRate);
            _rotate.BeginAnimation(RotateTransform.AngleProperty, anim);
        }

        private void BeginRing(double from, double to, double seconds, int frameRate)
        {
            var anim = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(anim, frameRate);
            _ring.BeginAnimation(OpacityProperty, anim);
        }

        /// <summary>
        /// Kill both clocks and park the parts at rest. Passing null to BeginAnimation is what
        /// releases the animation's HOLD on the property — without it the last animated value sticks
        /// and a chip hidden mid-shake would come back tilted.
        /// </summary>
        private void StopWobble()
        {
            try
            {
                _rotate.BeginAnimation(RotateTransform.AngleProperty, null);
                _rotate.Angle = 0;
                _ring.BeginAnimation(OpacityProperty, null);
                _ring.Opacity = 0;
                BeginAnimation(OpacityProperty, null);
                Opacity = 1;
                _shake.BeginAnimation(TranslateTransform.XProperty, null);
                _shake.X = 0;
            }
            catch (Exception ex) { App.Logger?.Debug("[Fuse] rail chip stop: {E}", ex.Message); }
            _animating = false;
        }

        /// <summary>
        /// Test/debug seam: is a clock actually running right now?
        ///
        /// <para>The flash-out counts. It is the loudest clock this control ever runs, and a seam
        /// that read "not wobbling" through two and a half seconds of flicker would be answering a
        /// different question than the one it is named for.</para>
        /// </summary>
        internal bool IsWobbling => _animating || _flashingOut;

        /// <summary>Test/debug seam: is the chip specifically in its two-and-a-half second goodbye
        /// (as opposed to an ordinary tremble)?</summary>
        internal bool IsFlashingOut => _flashingOut;

        // ============================== the flash-out ==============================

        /// <summary>
        /// ZERO. Flicker hard, shake hard, flare the ring, and then leave — the chip's whole reason
        /// to exist just ran out, and the ceremony the director opens on this same instant is what
        /// it hands over to.
        ///
        /// <para><b>Three clocks and a timer, not a Storyboard.Completed.</b> The completion has to
        /// fire even if one of the animations is released early (a rail teardown, a reduced-motion
        /// flip mid-flash), and a DispatcherTimer is the one clock that is not attached to any of
        /// the properties being animated. It is also what makes the collapse unconditional: however
        /// this goes, the chip is gone <see cref="FlashOutSeconds"/> from now.</para>
        ///
        /// <para><b>Reduced motion still gets the moment, quietly.</b> No flicker and no shake —
        /// but the chip does not simply blink out either: it fades over the same duration, so the
        /// rail does not reflow under a subject's hand with no warning at all.</para>
        /// </summary>
        private void BeginFlashOut()
        {
            if (_flashingOut) return;
            _flashingOut = true;

            try
            {
                StopWobble();
                _animating = false;
                Visibility = Visibility.Visible;
                ToolTip = null;   // nothing to read under a pointer while this happens

                DescentRoomSfx.PlayZeroFlashOut();

                if (MotionFx.AllowTransitions) BeginFlashClocks();
                else BeginQuietFade();

                _flashTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
                { Interval = TimeSpan.FromSeconds(FlashOutSeconds) };
                _flashTimer.Tick += OnFlashOutDone;
                _flashTimer.Start();

                App.Logger?.Information("[Fuse] the rail chip is flashing out - zero has arrived.");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Fuse] rail chip flash-out could not start: {E}", ex.Message);
                FinishFlashOut();
            }
        }

        /// <summary>The three clocks: the flicker, the shake, and the ring's flare.</summary>
        private void BeginFlashClocks()
        {
            var span = TimeSpan.FromSeconds(FlashOutSeconds);

            // ---- the flicker. Discrete, never eased: a countdown going out is a contact failing,
            // and an eased fade in and out of every blink reads as a pulse instead.
            var flicker = new DoubleAnimationUsingKeyFrames { Duration = span };
            double at = 0;
            bool lit = true;
            while (at < FlashOutSeconds)
            {
                flicker.KeyFrames.Add(new DiscreteDoubleKeyFrame(lit ? 1.0 : 0.12,
                    KeyTime.FromTimeSpan(TimeSpan.FromSeconds(at))));
                lit = !lit;
                // Accelerating: the blinks are a fifth of a second apart at the start and a
                // twentieth by the end, so the chip is visibly running out rather than blinking at
                // a fixed rate until somebody turns it off.
                at += 0.20 - 0.15 * (at / FlashOutSeconds);
            }
            flicker.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(span)));
            Timeline.SetDesiredFrameRate(flicker, FlashFrameRate);
            BeginAnimation(OpacityProperty, flicker);

            // ---- the shake. Sideways only: the rail is a vertical stack, and a chip travelling up
            // and down would read as the rows above it moving.
            var shake = new DoubleAnimation(-FlashShakePixels, FlashShakePixels,
                TimeSpan.FromSeconds(0.045))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            };
            Timeline.SetDesiredFrameRate(shake, FlashFrameRate);
            _shake.BeginAnimation(TranslateTransform.XProperty, shake);

            var spin = new DoubleAnimation(-FlashSpinDegrees, FlashSpinDegrees,
                TimeSpan.FromSeconds(0.07))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            };
            Timeline.SetDesiredFrameRate(spin, FlashFrameRate);
            _rotate.BeginAnimation(RotateTransform.AngleProperty, spin);

            // ---- the ring's flare: one hard swell up front, then it rides down with the flicker.
            var flare = new DoubleAnimationUsingKeyFrames { Duration = span };
            var ease = new SineEase { EasingMode = EasingMode.EaseOut };
            flare.KeyFrames.Add(new EasingDoubleKeyFrame(RingFlareLo,
                KeyTime.FromTimeSpan(TimeSpan.Zero), ease));
            flare.KeyFrames.Add(new EasingDoubleKeyFrame(1.0,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.14)), ease));
            flare.KeyFrames.Add(new EasingDoubleKeyFrame(0.55,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(FlashOutSeconds * 0.6)), ease));
            flare.KeyFrames.Add(new EasingDoubleKeyFrame(0.0,
                KeyTime.FromTimeSpan(span), ease));
            Timeline.SetDesiredFrameRate(flare, FlashFrameRate);
            _ring.BeginAnimation(OpacityProperty, flare);
        }

        /// <summary>Reduced motion's version of the same moment: one fade, no flicker, no shake.</summary>
        private void BeginQuietFade()
        {
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(FlashOutSeconds))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            BeginAnimation(OpacityProperty, fade);
        }

        /// <summary>
        /// The deadline. Unlike every other queued callback in this control there is no
        /// shutting-down bail here on purpose: the finish is the teardown, it only sets properties
        /// on this element, and skipping it on a dispatcher that is going away would leave a stopped
        /// timer holding a subscription to a chip that is on its way out anyway.
        /// </summary>
        private void OnFlashOutDone(object? sender, EventArgs e) => FinishFlashOut();

        /// <summary>
        /// The goodbye is over: release every clock, park the parts, and collapse for good.
        /// Idempotent, and the ONE exit from the flash-out — the timer, the failure path and the
        /// teardown all come through here.
        /// </summary>
        private void FinishFlashOut()
        {
            _flashingOut = false;

            if (_flashTimer != null)
            {
                try
                {
                    _flashTimer.Stop();
                    _flashTimer.Tick -= OnFlashOutDone;
                }
                catch (Exception ex) { App.Logger?.Debug("[Fuse] rail chip flash stop: {E}", ex.Message); }
                _flashTimer = null;
            }

            try { HideChip(); }
            catch (Exception ex) { App.Logger?.Debug("[Fuse] rail chip flash finish: {E}", ex.Message); }
        }

        // ============================== the door ==============================

        /// <summary>
        /// The chip is a door into the Spiral Room, and nothing else — no menu, no popup, no second
        /// meaning for a right click. During the fog era the room is the fog, which is exactly where
        /// somebody who just clicked a countdown should land.
        /// </summary>
        private static void OpenSpiralRoom()
        {
            try
            {
                if (Application.Current?.MainWindow is ConditioningControlPanel.MainWindow main)
                    main.ShowTab(SpiralRoom.TabKey);
            }
            catch (Exception ex) { App.Logger?.Debug("[Fuse] rail chip door: {E}", ex.Message); }
        }

        // ============================== the glyph ==============================

        /// <summary>
        /// A small alarm clock, built as geometry rather than parsed from a path string: the ring is
        /// two concentric ellipses under <see cref="FillRule.EvenOdd"/>, which also makes the hands
        /// drawn INSIDE the hole come back out filled (two crossings becomes three). Frozen, so the
        /// whole figure is one immutable object shared by every instance of this control.
        /// </summary>
        private static Geometry BuildClockGeometry()
        {
            var g = new GeometryGroup { FillRule = FillRule.EvenOdd };

            var dial = new Point(12, 13.2);
            g.Children.Add(new EllipseGeometry(dial, 7.4, 7.4));
            g.Children.Add(new EllipseGeometry(dial, 5.9, 5.9));

            // The hands MEET rather than overlap: two rectangles that shared area would cancel each
            // other out under EvenOdd and leave a notch at the pivot.
            g.Children.Add(new RectangleGeometry(new Rect(11.35, 8.7, 1.3, 5.15)));
            g.Children.Add(new RectangleGeometry(new Rect(12.65, 12.55, 3.5, 1.3)));

            // Bells, clear of the outer ring for the same EvenOdd reason.
            g.Children.Add(new EllipseGeometry(new Point(6.8, 4.7), 2.0, 2.0));
            g.Children.Add(new EllipseGeometry(new Point(17.2, 4.7), 2.0, 2.0));

            g.Freeze();
            return g;
        }

        private static Brush Frozen(SolidColorBrush b) { b.Freeze(); return b; }
    }
}
