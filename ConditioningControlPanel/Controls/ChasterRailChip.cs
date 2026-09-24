using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
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
    /// <para><b>THE CLOCK TICKS HERE FIRST</b> (owner, 2026-09-23). Eyes are on CCP, so the
    /// countdown under the padlock runs every second off the last lock snapshot plus whatever the
    /// tab will add (<see cref="LiveLockClock"/>); the phone catches up at the next sync. No extra
    /// Chaster calls: the snapshot keeps its own cadence. Hovering the chip opens the PEEK, a big
    /// card beside the rail with the time left in a large number, so it reads at a glance.</para>
    ///
    /// <para><b>THE CLOCK STAYS WHEN THE RAIL SHUTS.</b> <c>CacheNavRailParts</c> (MainWindow.NavRail.cs)
    /// walks the whole rail and fades every TextBlock it finds with the rail's labels. The readout
    /// and the badge figure are therefore tagged <c>navrailstatic</c> and written to fit the SHUT
    /// rail: 10px mono under the padlock, "+3m" in the badge. (The badge used to carry "+3:00" and
    /// ride the fade, which left an empty pink blob over the padlock whenever the rail was shut.)</para>
    ///
    /// <para><b>Literal colours.</b> The padlock is the app's own pink and does not take the
    /// active mod's accent, so nothing here is a DynamicResource. Every animated brush is built
    /// per instance.</para>
    /// </summary>
    public sealed class ChasterRailChip : Grid
    {
        public const string TabKey = "chaster";

        private const double ChipHeight = 62;
        private const double RingSize = 40;

        /// <summary>The tag that keeps a rail TextBlock out of the global label fade. The const
        /// itself is private to MainWindow.NavRail.cs (NavRailStaticTextTag); restated here because
        /// a rail control must not need the window to paint itself.</summary>
        private const string NavRailStaticTextTag = "navrailstatic";

        // A padlock in a 24 box: the shackle, then the body with a keyhole cut out of it.
        private static readonly Geometry ShackleShut = FrozenGeometry("M7.5,11 V7.6 a4.5,4.5 0 0 1 9,0 V11");

        /// <summary>The same shackle, hinged open to the right of the body.</summary>
        private static readonly Geometry ShackleOpen = FrozenGeometry("M12.5,11 V7.6 a4.5,4.5 0 0 1 9,0 V11.5");

        private static readonly Geometry BodyGeometry = FrozenGeometry(
            "M5,10.5 h14 a2,2 0 0 1 2,2 v7.5 a2,2 0 0 1 -2,2 h-14 a2,2 0 0 1 -2,-2 v-7.5 a2,2 0 0 1 2,-2 Z "
            + "M12,13.6 a1.6,1.6 0 0 0 -0.8,3 v2 h1.6 v-2 a1.6,1.6 0 0 0 -0.8,-3 Z");

        /// <summary>The glint across the top of the body: what makes it read as a solid thing.</summary>
        private static readonly Geometry BodyGlint = FrozenGeometry("M5.2,11.6 h13.6 a0.9,0.9 0 0 1 0,1.8 h-13.6 a0.9,0.9 0 0 1 0,-1.8 Z");

        // ============================== the palette (literals, always) ==============================

        private static readonly Color Ink = Color.FromRgb(0xFF, 0x7A, 0xC0);
        private static readonly Color RingPink = Color.FromArgb(0xCC, 0xFF, 0x69, 0xB4);

        /// <summary>Frozen. The one state that is not pink, because a stopped clock has to read as
        /// a different thing at a glance and not as a dimmer version of a running one.</summary>
        private static readonly Color Ice = Color.FromRgb(0x8F, 0xD8, 0xFF);
        private static readonly Color RingIce = Color.FromArgb(0xCC, 0x8F, 0xD8, 0xFF);

        /// <summary>Away: the fuse's amber, meaning "this number is old", never "something is wrong".</summary>
        private static readonly Color RingAmber = Color.FromArgb(0xCC, 0xE0, 0xB0, 0x52);

        private static readonly Color Grey = Color.FromRgb(0x9A, 0xA0, 0xB8);
        private static readonly Color RingGrey = Color.FromArgb(0x88, 0x9A, 0xA0, 0xB8);

        /// <summary>The pending badge. A real red with white ink, so it reads as a notification pill
        /// and not as more of the padlock's pink.</summary>
        private static readonly Color BadgeRed = Color.FromRgb(0xFF, 0x2D, 0x55);
        private static readonly Color CreditMint = Color.FromRgb(0x5F, 0xFF, 0xD0);
        private static readonly Color DebtRed = Color.FromRgb(0xFF, 0x6B, 0x8A);

        private static readonly Brush TileFill = FrozenBrush(new RadialGradientBrush(
            Color.FromRgb(0x3A, 0x22, 0x4E), Color.FromRgb(0x17, 0x12, 0x2A)) { GradientOrigin = new Point(0.4, 0.3) });
        private static readonly Brush Keyline = FrozenBrush(new SolidColorBrush(Color.FromRgb(0x17, 0x12, 0x2A)));
        private static readonly FontFamily Display = new("/Fonts/#Fredoka, Segoe UI");

        // ============================== parts ==============================

        private readonly SolidColorBrush _inkBrush = new(Ink);
        private readonly GradientStop _bodyTop = new(Color.FromRgb(0xFF, 0xC4, 0xE4), 0);
        private readonly GradientStop _bodyBottom = new(Ink, 1);
        private readonly SolidColorBrush _ringBrush = new(RingPink);
        private readonly SolidColorBrush _clockBrush = new(Ink);
        private readonly SolidColorBrush _badgeBrush = new(BadgeRed);
        private readonly SolidColorBrush _badgeInk = new(Colors.White);
        private readonly DropShadowEffect _glow = new() { Color = Color.FromRgb(0xFF, 0x69, 0xB4), BlurRadius = 12, ShadowDepth = 0, Opacity = 0.55 };

        private readonly Path _shackle;
        private readonly Border _ring;
        private readonly Canvas _lockArt;
        private readonly RotateTransform _swing = new();
        private readonly TextBlock _clock;
        private readonly Border _badge;
        private readonly TextBlock _badgeText;

        // the peek: the big number beside the rail
        private readonly Popup _peek;
        private readonly Border _peekCard;
        private readonly ScaleTransform _peekScale = new(1, 1);
        private readonly TextBlock _peekTitle;
        private readonly StackPanel _peekNumber;
        private readonly TextBlock _peekLead;
        private readonly TextBlock _peekEnds;
        private readonly TextBlock _peekPending;
        private readonly TextBlock _peekNote;

        private DispatcherTimer? _tick;
        private bool _wired;
        private bool _idleRunning;
        private LockClockState _state = LockClockState.Unlinked;

        public ChasterRailChip()
        {
            Height = ChipHeight;
            Margin = new Thickness(0, 6, 0, 2);
            Cursor = Cursors.Hand;
            Background = Brushes.Transparent; // the whole row takes the click, not just the ring
            ClipToBounds = true;

            // ---- the padlock: gradient body, a glint, a thick shackle ----
            _lockArt = new Canvas { Width = 24, Height = 24, RenderTransformOrigin = new Point(0.5, 0.2), RenderTransform = _swing };
            _shackle = new Path
            {
                Data = ShackleShut,
                Stroke = _inkBrush,
                StrokeThickness = 2.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            _lockArt.Children.Add(_shackle);
            var bodyFill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            bodyFill.GradientStops.Add(_bodyTop);
            bodyFill.GradientStops.Add(_bodyBottom);
            _lockArt.Children.Add(new Path { Data = BodyGeometry, Fill = bodyFill, Stroke = Keyline, StrokeThickness = 0.6 });
            _lockArt.Children.Add(new Path { Data = BodyGlint, Fill = FrozenBrush(new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF))) });

            _ring = new Border
            {
                Width = RingSize,
                Height = RingSize,
                CornerRadius = new CornerRadius(RingSize / 2),
                BorderThickness = new Thickness(2),
                BorderBrush = _ringBrush,
                Background = TileFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                Effect = _glow,
                Child = new Viewbox { Width = 27, Height = 27, Child = _lockArt },
            };

            _clock = new TextBlock
            {
                FontFamily = Services.UI.FontGuard.Mono,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = _clockBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0),
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
            column.Children.Add(_ring);
            column.Children.Add(_clock);
            Children.Add(column);

            // ---- the pending badge: a small red pill on the ring's top right, inside the chip ----
            _badgeText = new TextBlock
            {
                FontFamily = Services.UI.FontGuard.Mono,
                FontSize = 8.5,
                FontWeight = FontWeights.Bold,
                Foreground = _badgeInk,
                Margin = new Thickness(3, 0, 3, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Tag = NavRailStaticTextTag,
                Text = string.Empty,
            };
            _badge = new Border
            {
                Height = 13,
                MinWidth = 13,
                CornerRadius = new CornerRadius(6.5),
                Background = _badgeBrush,
                BorderBrush = Keyline,
                BorderThickness = new Thickness(1.2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 2, 0),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                Child = _badgeText,
            };
            Children.Add(_badge);

            // ---- the peek ----
            _peekTitle = new TextBlock
            {
                FontFamily = Display, FontSize = 14, FontWeight = FontWeights.SemiBold,
                Foreground = FrozenBrush(new SolidColorBrush(Color.FromRgb(0xFF, 0xB8, 0xDC))),
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 380,
            };
            _peekNumber = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            _peekLead = new TextBlock
            {
                FontSize = 13, Margin = new Thickness(2, -4, 0, 6),
                Foreground = FrozenBrush(new SolidColorBrush(Color.FromRgb(0xB8, 0xB0, 0xCC))),
            };
            _peekEnds = new TextBlock
            {
                FontSize = 13, Foreground = FrozenBrush(new SolidColorBrush(Color.FromRgb(0xE8, 0xE0, 0xF4))),
            };
            _peekPending = new TextBlock
            {
                FontFamily = Display, FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 0),
                Foreground = FrozenBrush(new SolidColorBrush(DebtRed)),
            };
            _peekNote = new TextBlock
            {
                FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 380,
                Foreground = FrozenBrush(new SolidColorBrush(Color.FromRgb(0xB8, 0xB0, 0xCC))),
            };
            var peekStack = new StackPanel();
            peekStack.Children.Add(_peekTitle);
            peekStack.Children.Add(_peekNumber);
            peekStack.Children.Add(_peekLead);
            peekStack.Children.Add(_peekEnds);
            peekStack.Children.Add(_peekPending);
            peekStack.Children.Add(_peekNote);

            var peekRim = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            peekRim.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x69, 0xB4), 0));
            peekRim.GradientStops.Add(new GradientStop(Color.FromRgb(0xB9, 0x9C, 0xFF), 1));
            peekRim.Freeze();
            _peekCard = new Border
            {
                Background = FrozenBrush(new LinearGradientBrush(Color.FromArgb(0xF4, 0x2A, 0x16, 0x3C), Color.FromArgb(0xF4, 0x14, 0x10, 0x26), 90)),
                BorderBrush = peekRim,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(22, 14, 26, 16),
                Margin = new Thickness(6, 16, 24, 24), // room for the glow inside the popup
                RenderTransformOrigin = new Point(0, 0.5),
                RenderTransform = _peekScale,
                IsHitTestVisible = false,
                Effect = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0x69, 0xB4), BlurRadius = 22, ShadowDepth = 0, Opacity = 0.45 },
                Child = peekStack,
            };
            _peek = new Popup
            {
                PlacementTarget = this,
                Placement = PlacementMode.Right,
                VerticalOffset = -40,
                HorizontalOffset = 6,
                AllowsTransparency = true,
                StaysOpen = true,
                Focusable = false,
                IsHitTestVisible = false,
                PopupAnimation = PopupAnimation.None,
                Child = _peekCard,
            };

            MouseEnter += (_, _) => OpenPeek();
            MouseLeave += (_, _) => ClosePeek();
            MouseLeftButtonUp += (_, _) => { ClosePeek(); Open(); };
            Loaded += (_, _) => Wire();
            Unloaded += (_, _) => { ClosePeek(); Unwire(); };
        }

        /// <summary>The hover card. Exposed for the render tests.</summary>
        internal Popup Peek => _peek;

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

                // One clock for the readout, the badge and the peek. A second while anything ticks
                // (a running lock, a hold, an open peek); a minute otherwise. Local arithmetic
                // only, never a call.
                _tick = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
                {
                    Interval = TimeSpan.FromSeconds(1),
                };
                _tick.Tick += (_, _) => Apply();
                _tick.Start();
            }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] rail chip could not wire: {E}", ex.Message); }
            Apply();
            StartIdle();
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
            StopIdle();
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
                var now = DateTime.UtcNow;
                var hold = chaster?.SafetyHoldRemaining ?? TimeSpan.Zero;
                var snapshot = chaster?.Lock;
                var balance = chaster?.BalanceSeconds ?? 0;
                var clock = LockClockText.State(chaster?.LockLookup ?? LockLookup.Unlinked, snapshot,
                    chaster?.IsLinked == true, hold, now, paused: chaster?.IsPaused == true);
                _state = clock.State;

                _clock.Text = clock.State switch
                {
                    LockClockState.Frozen => Loc.Get("chaster_chip_frozen"),
                    LockClockState.Held => Loc.GetF("chaster_chip_hold", clock.Text),
                    LockClockState.Paused => Loc.GetF("chaster_chip_paused", clock.Text == "" || clock.Text == LockClockText.HiddenMark
                        ? clock.Text : LiveText(snapshot, balance, now, clock.Text)).Trim(),
                    LockClockState.Locked or LockClockState.Away => LiveText(snapshot, balance, now, clock.Text),
                    _ => clock.Text,
                };
                _clock.Visibility = string.IsNullOrEmpty(_clock.Text) ? Visibility.Collapsed : Visibility.Visible;

                var (ink, ring) = Palette(clock.State);
                _inkBrush.Color = ink;
                _clockBrush.Color = ink;
                _bodyBottom.Color = ink;
                _bodyTop.Color = Lighten(ink, 0.55);
                _ringBrush.Color = ring;
                _glow.Color = Color.FromRgb(ring.R, ring.G, ring.B);
                _shackle.Data = IsShut(clock.State) ? ShackleShut : ShackleOpen;

                // Dim, not hidden: an unlinked padlock is still the door to the page that explains
                // what it is, which is the whole reason it ships visible.
                Opacity = clock.State == LockClockState.Unlinked ? 0.5 : 1.0;

                if (_tick != null)
                {
                    var ticking = clock.State == LockClockState.Held || LiveLockClock.Ticks(snapshot) || _peek.IsOpen;
                    var want = ticking ? TimeSpan.FromSeconds(1) : TimeSpan.FromMinutes(1);
                    if (_tick.Interval != want) _tick.Interval = want;
                }

                ApplyBadge(balance);
                if (_peek.IsOpen) FillPeek();
            }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] rail chip paint: {E}", ex.Message); }
        }

        /// <summary>The live readout under the padlock, or the old text when there is no number
        /// (hidden timer, no end date). Zero reads as "ready": the key is out.</summary>
        private static string LiveText(LockSnapshot? snapshot, int balance, DateTime now, string fallback)
        {
            if (LiveLockClock.Remaining(snapshot, balance, now) is not { } left) return fallback;
            return left <= TimeSpan.Zero ? Loc.Get("chaster_chip_ready") : LiveLockClock.Compact(left);
        }

        /// <summary>Whether the padlock is drawn shut. A safety hold is NOT a lock state, so the
        /// shackle keeps whatever the last lookup said: held only greys the chip out.</summary>
        private static bool IsShut(LockClockState state) => state is LockClockState.Locked
            or LockClockState.Frozen or LockClockState.Hidden or LockClockState.Away or LockClockState.Held
            or LockClockState.Paused;

        private static (Color Ink, Color Ring) Palette(LockClockState state) => state switch
        {
            LockClockState.Frozen => (Ice, RingIce),
            LockClockState.Away => (Ink, RingAmber),
            LockClockState.Held or LockClockState.Paused => (Grey, RingGrey),
            _ => (Ink, RingPink),
        };

        private static Color Lighten(Color c, double t) => Color.FromRgb(
            (byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));

        private void ApplyBadge(int balanceSeconds)
        {
            var text = LiveLockClock.Badge(balanceSeconds);
            if (text.Length == 0)
            {
                _badge.Visibility = Visibility.Collapsed;
                _badgeText.Text = string.Empty;
                return;
            }

            _badgeBrush.Color = balanceSeconds > 0 ? BadgeRed : CreditMint;
            _badgeInk.Color = balanceSeconds > 0 ? Colors.White : Color.FromRgb(0x10, 0x20, 0x1A);
            _badgeText.Text = text;
            _badge.Visibility = Visibility.Visible;
        }

        // ============================== the peek ==============================

        private void OpenPeek()
        {
            try
            {
                FillPeek();
                _peek.IsOpen = true;
                if (_tick != null && _tick.Interval != TimeSpan.FromSeconds(1)) _tick.Interval = TimeSpan.FromSeconds(1);
                if (!MotionFx.AllowTransitions)
                {
                    _peekScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    _peekScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    _peekCard.BeginAnimation(OpacityProperty, null);
                    return;
                }
                var spring = new ElasticEase { Oscillations = 1, Springiness = 5, EasingMode = EasingMode.EaseOut };
                var grow = new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(460)) { EasingFunction = spring };
                _peekScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                _peekScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                _peekCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
            }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] rail chip peek: {E}", ex.Message); }
        }

        private void ClosePeek()
        {
            try { _peek.IsOpen = false; }
            catch { /* a popup closing during teardown has nothing to say */ }
        }

        /// <summary>Everything the old tooltip said, with the time left as the one big thing.</summary>
        internal void FillPeek()
        {
            var chaster = App.Chaster;
            var now = DateTime.UtcNow;
            _peekNumber.Children.Clear();
            _peekEnds.Text = _peekPending.Text = _peekNote.Text = _peekLead.Text = string.Empty;

            if (chaster == null || !chaster.IsLinked)
            {
                _peekTitle.Text = Loc.Get("chaster_title");
                BigWord(Loc.Get("chaster_hero_title"), Color.FromRgb(0xFF, 0x9A, 0xCB));
                _peekNote.Text = Loc.Get("chaster_chip_unlinked");
                Collapse();
                return;
            }

            var snapshot = chaster.Lock;
            var balance = chaster.BalanceSeconds;
            _peekTitle.Text = snapshot == null ? Loc.Get("chaster_title")
                : string.IsNullOrWhiteSpace(snapshot.Title) ? Loc.Get("chaster_lock_untitled") : snapshot.Title!;

            var hold = chaster.SafetyHoldRemaining;
            if (snapshot == null)
            {
                BigWord(Loc.Get(chaster.LockLookup == LockLookup.Ambiguous ? "chaster_pill_pick" : "chaster_pill_nolock"),
                    Color.FromRgb(0xE0, 0xB0, 0x52));
                _peekNote.Text = chaster.LockLookup switch
                {
                    LockLookup.Ambiguous => Loc.Get("chaster_lock_pick"),
                    LockLookup.Away => Loc.Get("chaster_chip_away_tip"),
                    _ => Loc.Get("chaster_lock_none"),
                };
            }
            else if (snapshot.TimerHidden)
            {
                BigWord(Loc.Get("chaster_pill_hidden"), Grey);
                _peekNote.Text = Loc.Get("chaster_chip_hidden_tip");
            }
            else if (LiveLockClock.Remaining(snapshot, balance, now) is { } left)
            {
                if (left <= TimeSpan.Zero) BigWord(Loc.Get("chaster_clock_ready"), CreditMint);
                else
                {
                    BigNumber(left, snapshot.IsFrozen ? Ice : Color.FromRgb(0xFF, 0xF0, 0xF8));
                    _peekLead.Text = Loc.Get("chaster_peek_left");
                }
                // The same end the clock counts to: Chaster's own plus what the tab will add.
                if (LiveLockClock.EndsAt(snapshot, balance, now) is { } ends)
                    _peekEnds.Text = Loc.GetF("chaster_chip_ends", ends.ToLocalTime().ToString("ddd d MMM HH:mm"));
                if (snapshot.IsFrozen) _peekNote.Text = Loc.Get("chaster_chip_frozen_tip");
                else if (chaster.LockLookup == LockLookup.Away) _peekNote.Text = Loc.Get("chaster_chip_away_tip");
            }
            else BigWord(Loc.Get("chaster_pill_hidden"), Grey);

            if (LiveLockClock.PendingAdd(balance) > 0)
                _peekPending.Text = Loc.GetF("chaster_peek_pending", CircesTab.Format(balance));
            if (chaster.IsPaused) _peekNote.Text = Loc.Get("chaster_chip_paused_tip");
            if (hold > TimeSpan.Zero)
                _peekNote.Text = Loc.GetF("chaster_chip_hold_tip", LockClockText.HoldClock(hold));
            Collapse();
        }

        private void Collapse()
        {
            foreach (var t in new[] { _peekLead, _peekEnds, _peekPending, _peekNote })
                t.Visibility = string.IsNullOrEmpty(t.Text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void BigNumber(TimeSpan left, Color ink)
        {
            var brush = FrozenBrush(new SolidColorBrush(ink));
            var unitBrush = FrozenBrush(new SolidColorBrush(Color.FromRgb(0xFF, 0x9A, 0xCB)));
            foreach (var (value, unit) in LiveLockClock.Parts(left))
            {
                _peekNumber.Children.Add(new TextBlock
                {
                    Text = value, FontFamily = Display, FontSize = 64, FontWeight = FontWeights.Bold,
                    Foreground = brush, VerticalAlignment = VerticalAlignment.Bottom,
                });
                _peekNumber.Children.Add(new TextBlock
                {
                    Text = Loc.Get("chaster_unit_" + unit), FontFamily = Display, FontSize = 24, FontWeight = FontWeights.SemiBold,
                    Foreground = unitBrush, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 12, 12),
                });
            }
        }

        private void BigWord(string word, Color ink) => _peekNumber.Children.Add(new TextBlock
        {
            Text = word, FontFamily = Display, FontSize = 44, FontWeight = FontWeights.Bold,
            Foreground = FrozenBrush(new SolidColorBrush(ink)),
        });

        // ============================== idle flair ==============================

        /// <summary>A slow breath on the rim glow and a small swing of the padlock every few
        /// seconds, so the chip reads as alive next to the launcher's juice. Ambient loops only.</summary>
        private void StartIdle()
        {
            try
            {
                if (_idleRunning || !MotionFx.AllowAmbientLoops) return;
                _idleRunning = true;
                _glow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0.35, 0.8, TimeSpan.FromMilliseconds(2200))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
                var swing = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(7), RepeatBehavior = RepeatBehavior.Forever };
                swing.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(5600))));
                swing.KeyFrames.Add(new EasingDoubleKeyFrame(-9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(5780)), new SineEase()));
                swing.KeyFrames.Add(new EasingDoubleKeyFrame(7, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(6000)), new SineEase()));
                swing.KeyFrames.Add(new EasingDoubleKeyFrame(-4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(6220)), new SineEase()));
                swing.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(6500)), new SineEase()));
                _swing.BeginAnimation(RotateTransform.AngleProperty, swing);
            }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] rail chip idle: {E}", ex.Message); }
        }

        private void StopIdle()
        {
            _idleRunning = false;
            _glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
            _swing.BeginAnimation(RotateTransform.AngleProperty, null);
        }

        // ============================== the pulse ==============================

        /// <summary>
        /// A 250 ms tint on the ring, for whoever just booked something at this chip (the flashing
        /// figure lives in its own control and calls this, so the two read as one event). Silent
        /// under MotionFx Off, where the resting colour is all there is.
        /// </summary>
        public void Pulse(Color tint)
        {
            try
            {
                if (!MotionFx.AllowTransitions) return;
                var rest = Palette(_state).Ring;
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

        private static Brush FrozenBrush(Brush brush)
        {
            brush.Freeze();
            return brush;
        }

        private static Geometry FrozenGeometry(string data)
        {
            var g = Geometry.Parse(data);
            g.Freeze();
            return g;
        }
    }
}
