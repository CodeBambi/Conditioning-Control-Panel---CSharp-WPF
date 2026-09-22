using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// The padlock at the foot of the nav rail, with the lock's own clock under it. Opens Circe's
    /// tab (<c>ShowTab("chaster")</c>).
    ///
    /// <para>It took the slot the spiral medallion had (owner, 2026-09-21): the Spiral Room is
    /// still one click away from the profile, and this page had no door at all. Always visible,
    /// linked or not, so the feature can be found; the page itself says what it is to a player
    /// who has never heard of Chaster.</para>
    ///
    /// <para>Code-built and vector on purpose, the same as the chip it replaced: it is NOT a door
    /// medallion, so it has no NavDoorMap row, no mod art slot (it looks the same under every mod)
    /// and none of the 40px door Viewbox markup NavRailFlyoutTests counts.</para>
    ///
    /// <para><b>THE CLOCK STAYS WHEN THE RAIL SHUTS.</b> <c>CacheNavRailParts</c> (MainWindow.NavRail.cs)
    /// walks the whole rail and fades every TextBlock it finds with the rail's labels, which would
    /// take the countdown away in the state the rail is in almost all of the time. The readout is
    /// therefore tagged <c>navrailstatic</c>, the same tag the search pill's lens carries, and is
    /// written to fit the SHUT rail: 10px mono, two fields at most ("12d 4h"), inside the 56px
    /// collapsed width. It is the reason the chip is here rather than a plain door, so it does not
    /// get to be the part that disappears. The pending badge's FIGURE is the opposite call and does
    /// ride the fade - see <see cref="_badgeText"/>.</para>
    ///
    /// <para><b>Literal colours, transform and opacity only.</b> Same rule as the fuse chip: the
    /// padlock is the app's own pink and does not take the active mod's accent, so nothing here is
    /// a DynamicResource. Nothing here touches layout either, and every animated brush is built per
    /// instance - a brush set from a Style is ONE shared frozen instance across every element
    /// wearing it, and these get animated.</para>
    /// </summary>
    public sealed class ChasterRailChip : Grid
    {
        public const string TabKey = "chaster";

        private const double ChipHeight = 60;
        private const double BadgeSize = 40;

        /// <summary>The tag that keeps a rail TextBlock out of the global label fade. The const
        /// itself is private to MainWindow.NavRail.cs (NavRailStaticTextTag); restated here because
        /// a rail control must not need the window to paint itself.</summary>
        private const string NavRailStaticTextTag = "navrailstatic";

        // A padlock in a 24 box: the shackle, then the body with a keyhole cut out of it.
        private const string ShackleData = "M7.5,11 V8 a4.5,4.5 0 0 1 9,0 V11";

        /// <summary>The same shackle, hinged open to the right of the body. Not a rotation: a
        /// rotated shackle reads as a broken padlock, and an open one still has to look attached at
        /// the hinge.</summary>
        private const string OpenShackleData = "M12.5,11 V8 a4.5,4.5 0 0 1 9,0 V11.5";

        private const string BodyData = "M5,11 h14 a1.5,1.5 0 0 1 1.5,1.5 v7 a1.5,1.5 0 0 1 -1.5,1.5 h-14 a1.5,1.5 0 0 1 -1.5,-1.5 v-7 a1.5,1.5 0 0 1 1.5,-1.5 Z "
                                        + "M12,14 a1.4,1.4 0 0 0 -0.7,2.6 v1.6 h1.4 v-1.6 a1.4,1.4 0 0 0 -0.7,-2.6 Z";

        // ============================== the palette (literals, always) ==============================

        private static readonly Color Ink = Color.FromRgb(0xFF, 0x9A, 0xCB);
        private static readonly Color RingPink = Color.FromArgb(0x66, 0xFF, 0x69, 0xB4);

        /// <summary>Frozen. The one state that is not pink, because a stopped clock has to read as
        /// a different thing at a glance and not as a dimmer version of a running one.</summary>
        private static readonly Color Ice = Color.FromRgb(0x8F, 0xD8, 0xFF);
        private static readonly Color RingIce = Color.FromArgb(0x88, 0x8F, 0xD8, 0xFF);

        /// <summary>Away: the fuse's amber, meaning "this number is old", never "something is wrong".</summary>
        private static readonly Color RingAmber = Color.FromArgb(0xAA, 0xE0, 0xB0, 0x52);

        private static readonly Color Grey = Color.FromRgb(0x9A, 0xA0, 0xB8);
        private static readonly Color RingGrey = Color.FromArgb(0x77, 0x9A, 0xA0, 0xB8);
        private static readonly Color DebtRed = Color.FromRgb(0xFF, 0x6B, 0x8A);
        private static readonly Color CreditMint = Color.FromRgb(0x5F, 0xFF, 0xD0);

        private static readonly Brush TileFill = Frozen(Color.FromRgb(0x25, 0x25, 0x42));
        private static readonly Brush BadgeInk = Frozen(Color.FromRgb(0x18, 0x12, 0x20));

        // ============================== parts ==============================

        private readonly SolidColorBrush _inkBrush = new(Ink);
        private readonly SolidColorBrush _ringBrush = new(RingPink);
        private readonly SolidColorBrush _clockBrush = new(Ink);
        private readonly SolidColorBrush _badgeBrush = new(DebtRed);

        private readonly Path _shackle;
        private readonly TextBlock _clock;
        private readonly Border _badge;
        private readonly TextBlock _badgeText;

        private DispatcherTimer? _tick;
        private bool _wired;
        private LockClockState _state = LockClockState.Unlinked;

        public ChasterRailChip()
        {
            Height = ChipHeight;
            Margin = new Thickness(0, 6, 0, 2);
            Cursor = Cursors.Hand;
            Background = Brushes.Transparent; // the whole row takes the click, not just the ring

            var lockArt = new Canvas { Width = 24, Height = 24 };
            _shackle = new Path
            {
                Data = Geometry.Parse(ShackleData),
                Stroke = _inkBrush,
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            lockArt.Children.Add(_shackle);
            lockArt.Children.Add(new Path { Data = Geometry.Parse(BodyData), Fill = _inkBrush });

            var ring = new Border
            {
                Width = BadgeSize,
                Height = BadgeSize,
                CornerRadius = new CornerRadius(BadgeSize / 2),
                BorderThickness = new Thickness(1.5),
                BorderBrush = _ringBrush,
                Background = TileFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new Viewbox { Width = 22, Height = 22, Child = lockArt },
            };

            // 10px, not 11: the shut rail is 56px wide and "12d 4h" has to sit inside it with the
            // chip's own margins still around it.
            _clock = new TextBlock
            {
                FontFamily = Services.UI.FontGuard.Mono,
                FontSize = 10,
                Foreground = _clockBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0),
                IsHitTestVisible = false,
                Tag = NavRailStaticTextTag,
                Text = string.Empty,
                Visibility = Visibility.Collapsed,
            };

            var column = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            column.Children.Add(ring);
            column.Children.Add(_clock);
            Children.Add(column);

            // The pending badge rides the ring's top right corner. Collapsed at rest, so a tab with
            // nothing on it is a plain padlock.
            _badgeText = new TextBlock
            {
                FontFamily = Services.UI.FontGuard.Mono,
                FontSize = 9,
                Foreground = BadgeInk,
                Margin = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                // Deliberately NOT navrailstatic: "+12:30" is wider than the shut rail can hold, so
                // the figure rides the label fade and reads when the rail is out. The badge's own
                // colour is what says "something is on the tab" while the rail is shut.
                Text = string.Empty,
            };
            _badge = new Border
            {
                Height = 13,
                MinWidth = 13,
                CornerRadius = new CornerRadius(6.5),
                Background = _badgeBrush,
                // Right-aligned to the chip's own 56px edge, never offset past it: a badge hung off
                // the ring's corner clipped to "+5:4" in the shut rail.
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 0, 0),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                Child = _badgeText,
            };
            Children.Add(_badge);

            ToolTip = Loc.Get("chaster_title");
            ToolTipOpening += (_, _) => RebuildTooltip();
            MouseLeftButtonUp += (_, _) => Open();
            Loaded += (_, _) => Wire();
            Unloaded += (_, _) => Unwire();
        }

        // ============================== wiring ==============================

        private void Wire()
        {
            if (_wired) return;
            _wired = true;
            try
            {
                var chaster = App.Chaster;
                if (chaster != null)
                {
                    chaster.LockChanged += OnServiceChanged;
                    chaster.LinkChanged += OnServiceChanged;
                    chaster.Booked += OnBooked;
                }

                // One clock for both readouts. A minute is all a countdown that prints minutes
                // needs; it drops to a second only while a safety hold counts down in m:ss, the one
                // state on this chip where seconds are shown.
                _tick = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
                {
                    Interval = TimeSpan.FromMinutes(1),
                };
                _tick.Tick += (_, _) => Apply();
                _tick.Start();
            }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] rail chip could not wire: {E}", ex.Message); }
            Apply();
        }

        private void Unwire()
        {
            if (!_wired) return;
            _wired = false;
            try
            {
                var chaster = App.Chaster;
                if (chaster != null)
                {
                    chaster.LockChanged -= OnServiceChanged;
                    chaster.LinkChanged -= OnServiceChanged;
                    chaster.Booked -= OnBooked;
                }
            }
            catch { /* teardown races are not worth a log line */ }
            _tick?.Stop();
            _tick = null;
        }

        /// <summary>The service raises on whatever thread it was working on, so everything lands
        /// back on the dispatcher before it touches a brush.</summary>
        private void OnServiceChanged()
        {
            try
            {
                if (Dispatcher.HasShutdownStarted) return;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Apply));
            }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] rail chip repaint: {E}", ex.Message); }
        }

        private void OnBooked(string eventId, TabBooking booking) => OnServiceChanged();

        // ============================== painting ==============================

        private void Apply()
        {
            try
            {
                var chaster = App.Chaster;
                var hold = chaster?.SafetyHoldRemaining ?? TimeSpan.Zero;
                var clock = LockClockText.State(chaster?.LockLookup ?? LockLookup.Unlinked, chaster?.Lock,
                    chaster?.IsLinked == true, hold, DateTime.UtcNow);
                _state = clock.State;

                _clock.Text = clock.State switch
                {
                    LockClockState.Frozen => Loc.Get("chaster_chip_frozen"),
                    LockClockState.Held => Loc.GetF("chaster_chip_hold", clock.Text),
                    _ => clock.Text,
                };
                _clock.Visibility = string.IsNullOrEmpty(_clock.Text) ? Visibility.Collapsed : Visibility.Visible;

                var (ink, ring) = Palette(clock.State);
                _inkBrush.Color = ink;
                _clockBrush.Color = ink;
                _ringBrush.Color = ring;
                _shackle.Data = Geometry.Parse(IsShut(clock.State) ? ShackleData : OpenShackleData);

                // Dim, not hidden: an unlinked padlock is still the door to the page that explains
                // what it is, which is the whole reason it ships visible.
                Opacity = clock.State == LockClockState.Unlinked ? 0.45 : 1.0;

                if (_tick != null)
                {
                    var want = clock.State == LockClockState.Held ? TimeSpan.FromSeconds(1) : TimeSpan.FromMinutes(1);
                    if (_tick.Interval != want) _tick.Interval = want;
                }

                ApplyBadge(chaster?.BalanceSeconds ?? 0);
            }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] rail chip paint: {E}", ex.Message); }
        }

        /// <summary>Whether the padlock is drawn shut. A safety hold is NOT a lock state, so the
        /// shackle keeps whatever the last lookup said rather than flapping open for ten minutes:
        /// held only greys the chip out.</summary>
        private static bool IsShut(LockClockState state) => state is LockClockState.Locked
            or LockClockState.Frozen or LockClockState.Hidden or LockClockState.Away or LockClockState.Held;

        private static (Color Ink, Color Ring) Palette(LockClockState state) => state switch
        {
            LockClockState.Frozen => (Ice, RingIce),
            LockClockState.Away => (Ink, RingAmber),
            LockClockState.Held => (Grey, RingGrey),
            _ => (Ink, RingPink),
        };

        private void ApplyBadge(int balanceSeconds)
        {
            if (balanceSeconds == 0)
            {
                _badge.Visibility = Visibility.Collapsed;
                _badgeText.Text = string.Empty;
                return;
            }

            _badgeBrush.Color = balanceSeconds > 0 ? DebtRed : CreditMint;
            _badgeText.Text = CircesTab.Format(balanceSeconds);
            _badge.Visibility = Visibility.Visible;
        }

        // ============================== the tooltip ==============================

        /// <summary>Built at open time rather than kept in step: the tab moves on its own clock and
        /// the language can change under it, and a tooltip nobody is looking at is the one thing
        /// here that can afford to be built late.</summary>
        private void RebuildTooltip()
        {
            try
            {
                var chaster = App.Chaster;
                var text = new StringBuilder(Loc.Get("chaster_title"));

                if (chaster == null || !chaster.IsLinked)
                {
                    Line(text, Loc.Get("chaster_chip_unlinked"));
                    ToolTip = text.ToString();
                    return;
                }

                var hold = chaster.SafetyHoldRemaining;
                if (hold > TimeSpan.Zero) Line(text, Loc.GetF("chaster_chip_hold_tip", LockClockText.HoldClock(hold)));

                switch (chaster.LockLookup)
                {
                    case LockLookup.None:
                        Line(text, Loc.Get("chaster_lock_none"));
                        break;
                    case LockLookup.Ambiguous:
                        Line(text, Loc.Get("chaster_lock_pick"));
                        break;
                    case LockLookup.Away:
                        Line(text, Loc.Get("chaster_chip_away_tip"));
                        break;
                }

                if (chaster.Lock is { } snapshot)
                {
                    Line(text, string.IsNullOrWhiteSpace(snapshot.Title) ? Loc.Get("chaster_lock_untitled") : snapshot.Title!);
                    if (snapshot.TimerHidden) Line(text, Loc.Get("chaster_chip_hidden_tip"));
                    else if (snapshot.EndsAtUtc is { } ends) Line(text, Loc.GetF("chaster_chip_ends", ends.ToLocalTime().ToString("g")));
                    if (snapshot.IsFrozen) Line(text, Loc.Get("chaster_chip_frozen_tip"));
                }

                Line(text, Loc.GetF("chaster_today", CircesTab.Format(chaster.TodayAddedSeconds, signed: false),
                    CircesTab.Format(CircesTab.DailyCapSeconds, signed: false)));

                var balance = chaster.BalanceSeconds;
                if (balance != 0) Line(text, Loc.GetF("chaster_chip_pending", CircesTab.Format(balance)));

                ToolTip = text.ToString();
            }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] rail chip tooltip: {E}", ex.Message); }
        }

        private static void Line(StringBuilder text, string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            text.Append('\n').Append(line);
        }

        // ============================== the pulse ==============================

        /// <summary>
        /// A 250 ms tint on the ring, for whoever just booked something at this chip (the flashing
        /// figure lives in its own control and calls this, so the two read as one event).
        ///
        /// <para>Colour only, and nothing else about the chip moves: the rail is full of small
        /// things and a chip that jumps pulls the eye off the page. Silent under MotionFx Off,
        /// where the resting colour is all there is.</para>
        /// </summary>
        public void Pulse(Color tint)
        {
            try
            {
                if (!MotionFx.AllowTransitions) return;
                var rest = Palette(_state).Ring;
                // The brush holds the literal, and the clock runs from the tint back to it with
                // FillBehavior.Stop, so the next Apply is never fighting a held animation.
                _ringBrush.Color = rest;
                _ringBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
                {
                    From = tint,
                    To = rest,
                    Duration = TimeSpan.FromMilliseconds(250),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop,
                });
            }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] rail chip pulse: {E}", ex.Message); }
        }

        // ============================== the door ==============================

        private static void Open()
        {
            try
            {
                // MainWindow can be the launcher window, and is null in the tray.
                var main = App.MainWindowRef ?? Application.Current?.MainWindow as ConditioningControlPanel.MainWindow;
                main?.ShowTab(TabKey);
            }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] rail chip: {E}", ex.Message); }
        }

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
