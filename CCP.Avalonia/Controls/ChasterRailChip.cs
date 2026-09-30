// PORTED from ConditioningControlPanel/Controls/ChasterRailChip.cs: the padlock at the foot of the
// rail with the lock's clock under it; opens Circe's tab; dimmed, never hidden, while unlinked.
// ponytail: no hover PEEK (WPF FillPeek), idle breath/swing (StartIdle), mood-pip pop or Pulse
// (the booked flash is not on this head); the glow is a static BoxShadow.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Controls
{
    public sealed class ChasterRailChip : Grid
    {
        public const string TabKey = "chaster";
        private const double RingSize = 40;
        private const string NavRailStaticTextTag = "navrailstatic";

        private static readonly Geometry ShackleShut = Geometry.Parse("M7.5,11 V7.6 a4.5,4.5 0 0 1 9,0 V11");
        private static readonly Geometry ShackleOpen = Geometry.Parse("M12.5,11 V7.6 a4.5,4.5 0 0 1 9,0 V11.5");
        private static readonly Geometry BodyGeometry = Geometry.Parse(
            "M5,10.5 h14 a2,2 0 0 1 2,2 v7.5 a2,2 0 0 1 -2,2 h-14 a2,2 0 0 1 -2,-2 v-7.5 a2,2 0 0 1 2,-2 Z "
            + "M12,13.6 a1.6,1.6 0 0 0 -0.8,3 v2 h1.6 v-2 a1.6,1.6 0 0 0 -0.8,-3 Z");
        private static readonly Geometry BodyGlint = Geometry.Parse("M5.2,11.6 h13.6 a0.9,0.9 0 0 1 0,1.8 h-13.6 a0.9,0.9 0 0 1 0,-1.8 Z");

        private static readonly Color Ink = Color.FromRgb(0xFF, 0x7A, 0xC0);
        private static readonly Color RingPink = Color.FromArgb(0xCC, 0xFF, 0x69, 0xB4);
        private static readonly Color Ice = Color.FromRgb(0x8F, 0xD8, 0xFF);
        private static readonly Color RingIce = Color.FromArgb(0xCC, 0x8F, 0xD8, 0xFF);
        private static readonly Color RingAmber = Color.FromArgb(0xCC, 0xE0, 0xB0, 0x52);
        private static readonly Color Grey = Color.FromRgb(0x9A, 0xA0, 0xB8);
        private static readonly Color RingGrey = Color.FromArgb(0x88, 0x9A, 0xA0, 0xB8);
        private static readonly Color BadgeRed = Color.FromRgb(0xFF, 0x2D, 0x55);
        private static readonly Color CreditMint = Color.FromRgb(0x5F, 0xFF, 0xD0);
        private static readonly IBrush Keyline = new SolidColorBrush(Color.FromRgb(0x17, 0x12, 0x2A));
        private static readonly FontFamily Mono = new("Consolas, Courier New");

        /// <summary>WPF CircesMoodMeter.ColourOf, shared with the tab's mood line.</summary>
        internal static Color MoodColour(MoodLevel level) => level switch
        {
            MoodLevel.Warm => Color.FromRgb(0xE0, 0xB0, 0x52),
            MoodLevel.Hot => Color.FromRgb(0xFF, 0x6B, 0x8A),
            MoodLevel.Smoking => Color.FromRgb(0xFF, 0x3D, 0x71),
            _ => CreditMint,
        };

        private readonly SolidColorBrush _inkBrush = new(Ink);
        private readonly GradientStop _bodyTop = new(Color.FromRgb(0xFF, 0xC4, 0xE4), 0);
        private readonly GradientStop _bodyBottom = new(Ink, 1);
        private readonly SolidColorBrush _ringBrush = new(RingPink);
        private readonly SolidColorBrush _badgeBrush = new(BadgeRed);
        private readonly SolidColorBrush _badgeInk = new(Colors.White);
        private readonly SolidColorBrush _moodBrush = new(CreditMint);
        private readonly Path _shackle;
        private readonly Border _ring;
        private readonly TextBlock _clock;
        private readonly Border _badge;
        private readonly TextBlock _badgeText;
        private readonly Border _moodPip;
        private DispatcherTimer? _tick;
        private ChasterService? _wired;

        public ChasterRailChip()
        {
            Height = 62;
            Margin = new Thickness(0, 6, 0, 2);
            Cursor = new Cursor(StandardCursorType.Hand);
            Background = Brushes.Transparent; // the whole row takes the click, not just the ring
            ClipToBounds = true;

            var art = new Canvas { Width = 24, Height = 24 };
            _shackle = new Path
            {
                Data = ShackleShut, Stroke = _inkBrush, StrokeThickness = 2.8, StrokeLineCap = PenLineCap.Round,
            };
            art.Children.Add(_shackle);
            var bodyFill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            };
            bodyFill.GradientStops.Add(_bodyTop);
            bodyFill.GradientStops.Add(_bodyBottom);
            art.Children.Add(new Path { Data = BodyGeometry, Fill = bodyFill, Stroke = Keyline, StrokeThickness = 0.6 });
            art.Children.Add(new Path { Data = BodyGlint, Fill = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)) });

            _ring = new Border
            {
                Width = RingSize, Height = RingSize, CornerRadius = new CornerRadius(RingSize / 2),
                BorderThickness = new Thickness(2), BorderBrush = _ringBrush,
                Background = new RadialGradientBrush
                {
                    GradientOrigin = new RelativePoint(0.4, 0.3, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.FromRgb(0x3A, 0x22, 0x4E), 0), new GradientStop(Color.FromRgb(0x17, 0x12, 0x2A), 1) },
                },
                HorizontalAlignment = HorizontalAlignment.Center,
                BoxShadow = BoxShadows.Parse("0 0 12 0 #8CFF69B4"),
                Child = new Viewbox { Width = 27, Height = 27, Child = art },
            };
            _clock = new TextBlock
            {
                FontFamily = Mono, FontSize = 10, FontWeight = FontWeight.Bold, Foreground = _inkBrush,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0),
                IsHitTestVisible = false, Tag = NavRailStaticTextTag, IsVisible = false,
            };
            var column = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
                Children = { _ring, _clock },
            };
            Children.Add(column);

            _badgeText = new TextBlock
            {
                FontFamily = Mono, FontSize = 8.5, FontWeight = FontWeight.Bold, Foreground = _badgeInk,
                Margin = new Thickness(3, 0), VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, Tag = NavRailStaticTextTag,
            };
            _badge = new Border
            {
                Height = 13, MinWidth = 13, CornerRadius = new CornerRadius(6.5), Background = _badgeBrush,
                BorderBrush = Keyline, BorderThickness = new Thickness(1.2),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 2, 0), IsVisible = false, IsHitTestVisible = false, Child = _badgeText,
            };
            Children.Add(_badge);
            _moodPip = new Border
            {
                Width = 9, Height = 9, CornerRadius = new CornerRadius(4.5), Background = _moodBrush,
                BorderBrush = Keyline, BorderThickness = new Thickness(1.2),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 5, RingSize - 8, 0), IsVisible = false, IsHitTestVisible = false,
            };
            Children.Add(_moodPip);

            PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                (TopLevel.GetTopLevel(this) as MainShellWindow)?.ShowTab(TabKey);
            };
            AttachedToVisualTree += (_, _) => Wire();
            DetachedFromVisualTree += (_, _) => Unwire();
        }

        internal string ClockText => _clock.IsVisible ? _clock.Text ?? "" : "";

        private void Wire()
        {
            _wired = ChasterHead.Service;
            if (_wired != null)
            {
                _wired.LockChanged += OnServiceChanged;
                _wired.LinkChanged += OnServiceChanged;
                _wired.Booked += OnBooked;
            }
            _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _tick.Tick += (_, _) => Apply();
            _tick.Start();
            Apply();
        }

        private void Unwire()
        {
            if (_wired != null)
            {
                _wired.LockChanged -= OnServiceChanged;
                _wired.LinkChanged -= OnServiceChanged;
                _wired.Booked -= OnBooked;
                _wired = null;
            }
            _tick?.Stop();
            _tick = null;
        }

        private void OnServiceChanged() => Dispatcher.UIThread.Post(Apply, DispatcherPriority.Background);
        private void OnBooked(string eventId, TabBooking booking) => OnServiceChanged();

        /// <summary>WPF Apply (:365): the readout, the palette, the shackle, the dim, the badge, the pip.</summary>
        internal void Apply()
        {
            try
            {
                var chaster = ChasterHead.Service;
                var now = DateTime.UtcNow;
                var snapshot = chaster?.Lock;
                var balance = chaster?.BalanceSeconds ?? 0;
                var clock = LockClockText.State(chaster?.LockLookup ?? LockLookup.Unlinked, snapshot,
                    chaster?.IsLinked == true, chaster?.SafetyHoldRemaining ?? TimeSpan.Zero, now, paused: chaster?.IsPaused == true);

                _clock.Text = clock.State switch
                {
                    LockClockState.Frozen => Loc.Get("chaster_chip_frozen"),
                    LockClockState.Held => Loc.GetF("chaster_chip_hold", clock.Text),
                    LockClockState.Paused => Loc.GetF("chaster_chip_paused", clock.Text == "" || clock.Text == LockClockText.HiddenMark
                        ? clock.Text : LiveText(snapshot, balance, now, clock.Text)).Trim(),
                    LockClockState.Locked or LockClockState.Away => LiveText(snapshot, balance, now, clock.Text),
                    _ => clock.Text,
                };
                _clock.IsVisible = !string.IsNullOrEmpty(_clock.Text);

                var (ink, ring) = clock.State switch
                {
                    LockClockState.Frozen => (Ice, RingIce),
                    LockClockState.Away => (Ink, RingAmber),
                    LockClockState.Held or LockClockState.Paused => (Grey, RingGrey),
                    _ => (Ink, RingPink),
                };
                _inkBrush.Color = ink;
                _bodyBottom.Color = ink;
                _bodyTop.Color = Color.FromRgb((byte)(ink.R + (255 - ink.R) * 0.55), (byte)(ink.G + (255 - ink.G) * 0.55), (byte)(ink.B + (255 - ink.B) * 0.55));
                _ringBrush.Color = ring;
                // A safety hold is not a lock state: the shackle keeps what the lookup said.
                _shackle.Data = clock.State is LockClockState.Locked or LockClockState.Frozen or LockClockState.Hidden
                    or LockClockState.Away or LockClockState.Held or LockClockState.Paused ? ShackleShut : ShackleOpen;
                Opacity = clock.State == LockClockState.Unlinked ? 0.5 : 1.0;

                if (_tick != null)
                {
                    var want = clock.State == LockClockState.Held || LiveLockClock.Ticks(snapshot) ? TimeSpan.FromSeconds(1) : TimeSpan.FromMinutes(1);
                    if (_tick.Interval != want) _tick.Interval = want;
                }

                var badge = LiveLockClock.Badge(balance);
                _badge.IsVisible = badge.Length > 0;
                _badgeText.Text = badge;
                _badgeBrush.Color = balance > 0 ? BadgeRed : CreditMint;
                _badgeInk.Color = balance > 0 ? Colors.White : Color.FromRgb(0x10, 0x20, 0x1A);

                var mood = chaster?.Mood;
                if (mood is { } m) _moodBrush.Color = MoodColour(m.Level);
                _moodPip.IsVisible = mood is { Level: not MoodLevel.Calm };
            }
            catch (Exception ex) { Serilog.Log.Debug("[Chaster] rail chip paint: {E}", ex.Message); }
        }

        private static string LiveText(LockSnapshot? snapshot, int balance, DateTime now, string fallback)
        {
            if (LiveLockClock.Remaining(snapshot, balance, now) is not { } left) return fallback;
            return left <= TimeSpan.Zero ? Loc.Get("chaster_chip_ready") : LiveLockClock.Compact(left);
        }
    }
}
