using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// Circe's tab, the page. Reads <see cref="ChasterService"/>, writes four settings, decides
    /// nothing: every rule (what books, what is capped, what reaches the lock) lives in
    /// Services/Chaster and is tested there, and every string the page composes comes out of
    /// <see cref="TabPageText"/>, <see cref="TabPresets"/> or <see cref="TabMenuCopy"/> so it is
    /// tested without a window.
    ///
    /// <para>This partial is the DATA: what each figure, pill, square and row says. The motion (the
    /// bursts, rings, sheen, the cards arriving, the calendar's comet, the trailer's drift) is
    /// <c>ChasterTabView.Fx.cs</c>, which this file only ever calls into through the <c>Fx*</c>
    /// hooks, every one of which is safe to call with nothing on screen and does nothing under
    /// MotionFx Off.</para>
    /// </summary>
    public partial class ChasterTabView : UserControl
    {
        internal static readonly Color CostColour = Color.FromRgb(0xFF, 0x6B, 0x8A);
        internal static readonly Color EarnColour = Color.FromRgb(0x5F, 0xFF, 0xD0);
        internal static readonly Color JackpotColour = Color.FromRgb(0xE0, 0xB0, 0x52);
        internal static readonly Color CustomColour = Color.FromRgb(0xC9, 0xA6, 0xFF);
        private static readonly Color IceColour = Color.FromRgb(0x9F, 0xD8, 0xFF);
        private static readonly Color AmberColour = Color.FromRgb(0xFF, 0xC9, 0x8A);
        private static readonly Color MutedColour = Color.FromRgb(0xA8, 0xA2, 0xB8);

        private static readonly Brush CostBrush = Frozen(CostColour);
        private static readonly Brush EarnBrush = Frozen(EarnColour);
        private static readonly Brush JackpotBrush = Frozen(JackpotColour);
        private static readonly FontFamily Display = new("/Fonts/#Fredoka, Segoe UI");
        private static readonly FontFamily Mono = new("Consolas, Courier New");

        /// <summary>Half a minute is as fine as the hero needs; a running safety hold is counted
        /// in seconds because it is the one number the player is sitting there waiting out.</summary>
        private static readonly TimeSpan SlowTick = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan HoldTick = TimeSpan.FromSeconds(1);

        /// <summary>The hover-to-trailer delay, and the grace for the pointer to cross from the
        /// row to its popup.</summary>
        private static readonly TimeSpan TrailerOpenDelay = TimeSpan.FromMilliseconds(150);
        private static readonly TimeSpan TrailerCloseGrace = TimeSpan.FromMilliseconds(140);

        private bool _loading;
        private bool _menuBuilt;
        private bool _subscribed;
        private double _capFraction;
        private readonly Dictionary<string, ToggleButton> _priceToggles = new();
        private readonly Dictionary<string, Border> _rowDims = new();
        private readonly DispatcherTimer _tick;
        private readonly DispatcherTimer _clockTick;
        private readonly DispatcherTimer _trailerOpen;
        private readonly DispatcherTimer _trailerClose;
        private ToggleButton? _trailerRow;
        private bool _trailerShown;
        private bool _overTrailer;
        /// <summary>What the calendar was built for, so a tick that changes nothing redraws nothing.</summary>
        private (DateTime Start, DateTime End, DateTime Today)? _calendarKey;
        private FrameworkElement? _tonightCell;
        private FrameworkElement? _tonightMark;
        private FrameworkElement? _keyCell;
        /// <summary>The crosses, padlocks and key in calendar order, for the draw-in.</summary>
        private readonly List<FrameworkElement> _calendarDraws = new();

        /// <summary>The first number of the countdown and its value, for the count-up on show.</summary>
        private (TextBlock Block, int Value)? _clockLead;

        public ChasterTabView()
        {
            InitializeComponent();
            _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = SlowTick };
            _tick.Tick += (_, _) => { RefreshHero(); RefreshDay(animate: false); };
            // The hero's clock ticks every second on its own timer, apart from the slow page tick:
            // it only rewrites digits, never the calendar or the pills.
            _clockTick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _clockTick.Tick += (_, _) => { try { PaintHeroClock(); } catch (Exception ex) { Diag.Swallowed(ex, "chaster hero clock"); } };
            _trailerOpen = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TrailerOpenDelay };
            _trailerOpen.Tick += (_, _) => { _trailerOpen.Stop(); OpenTrailer(); };
            _trailerClose = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TrailerCloseGrace };
            _trailerClose.Tick += (_, _) => { _trailerClose.Stop(); if (!_overTrailer) HideTrailer(); };
            // Only listen while the page is on screen: Booked fires on every priced event.
            IsVisibleChanged += (_, _) => { Subscribe(IsVisible); if (!IsVisible) HideTrailer(); };
            Calendar.SizeChanged += (_, _) => PlaceCalendarTag();
            // the lock's name fits the hero's inner width: it shrinks, it never wraps
            HeroCard.SizeChanged += (_, _) => HeroTitle.FitWidth = Math.Max(0, HeroCard.ActualWidth - 52);
            // No runtime, dead process, bad page: the still picture under the browser is the trailer.
            TrailerWeb.Failed += (_, _) => TrailerWeb.Visibility = Visibility.Collapsed;
            FxInit();
            LadderInit();
        }

        private static Brush Frozen(Color c)
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
        }

        /// <summary>ShowTab calls this on every visit. Cheap parts run now; the lock list and the
        /// lock itself are network calls and fill in when they land.</summary>
        public void OnTabShown()
        {
            Refresh();
            _ = LoadLocksAsync();
            _ = App.Chaster?.RefreshLockAsync();
            _leadHeldUntilUtc = DateTime.UtcNow.AddSeconds(1.2); // the count-up owns the lead number
            FxOnShown();
            LadderOnShown();
        }

        private void Subscribe(bool on)
        {
            var chaster = App.Chaster;
            if (chaster == null || on == _subscribed) return;
            _subscribed = on;
            if (on)
            {
                chaster.Booked += OnBooked;
                chaster.LinkChanged += OnLinkChanged;
                chaster.LockChanged += OnLockChanged;
                _tick.Start();
                _clockTick.Start();
            }
            else
            {
                chaster.Booked -= OnBooked;
                chaster.LinkChanged -= OnLinkChanged;
                chaster.LockChanged -= OnLockChanged;
                _tick.Stop();
                _clockTick.Stop();
            }
        }

        // All three events arrive on whatever thread found out.
        private void OnBooked(string eventId, TabBooking booking) => Dispatcher.BeginInvoke(new Action(() =>
        {
            RefreshNumbers();
            if (booking.AppliedSeconds != 0) FxBooked(booking.AppliedSeconds, eventId);
        }));

        private void OnLinkChanged() => Dispatcher.BeginInvoke(new Action(() =>
        {
            OnTabShown();
            if (App.Chaster?.IsLinked == true) FxLinked();
        }));

        // A push moves the lock AND empties the tab, so both halves are re-read.
        private void OnLockChanged() => Dispatcher.BeginInvoke(new Action(() =>
        {
            RefreshHero();
            if (App.Chaster?.IsLinked == true) RefreshNumbers(animate: false);
        }));

        private void Refresh()
        {
            var chaster = App.Chaster;
            var linked = chaster?.IsLinked == true;
            UnlinkedPanel.Visibility = linked ? Visibility.Collapsed : Visibility.Visible;
            FactRow.Visibility = linked ? Visibility.Collapsed : Visibility.Visible;
            LinkedPanel.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
            SwitchPill.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
            PausePill.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
            PaintPause(App.Settings?.Current?.ChasterPaused == true);
            AccountStrip.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
            PaperTag.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
            BtnLink.IsEnabled = chaster != null;
            ShowLinking(chaster?.IsLinking == true);
            RefreshLimits();
            RefreshHero();
            if (!linked) return;

            var settings = App.Settings?.Current;
            var on = settings?.ChasterTabEnabled == true;
            _loading = true;
            try { ChkTab.IsChecked = on; }
            finally { _loading = false; }
            PaintSwitch(on);
            ConsentCard.Visibility = Visibility.Collapsed;

            ApplyPriceToggles();
            RefreshPresets();
            RefreshNumbers(animate: false);
        }

        private void RefreshNumbers(bool animate = true)
        {
            var chaster = App.Chaster;
            if (chaster == null) return;
            var balance = chaster.BalanceSeconds;
            TxtBalance.Text = balance == 0 ? CircesTab.Format(0, signed: false) : CircesTab.Format(balance);
            TxtBalance.Foreground = FigureBrush(balance);
            TxtBalanceCaption.Text = Loc.Get(balance < 0 ? "chaster_credit_caption" : "chaster_balance_caption");
            RefreshTag(balance);
            if (chaster.IsLinked) PaintHeroEnds(chaster.Lock, balance);
            RefreshDay(animate);
            RefreshRun();
            RefreshAdded();
            if (_billOpen) BuildBill();
        }

        private Brush FigureBrush(int seconds) =>
            seconds > 0 ? CostBrush : seconds < 0 ? EarnBrush : (Brush)FindResource("TextLightBrush");

        // ============================== 1. the lock, and the tag on it ==============================

        internal void RefreshHero()
        {
            var chaster = App.Chaster;
            var snapshot = chaster?.Lock;
            var lookup = chaster?.LockLookup ?? LockLookup.Unlinked;
            var linked = chaster?.IsLinked == true;
            TxtAccountLock.Text = AccountLockLine(chaster);

            if (!linked)
            {
                // The hero is the ask: nothing about a lock it cannot know.
                HeroTitle.Visibility = Visibility.Collapsed;
                HeroClockRow.Visibility = Visibility.Collapsed;
                TxtHeroEnds.Visibility = Visibility.Collapsed;
                HeroPills.Children.Clear();
                HeroPills.Visibility = Visibility.Collapsed;
                LockRow.Visibility = Visibility.Collapsed;
                BuildCalendar(null);
                return;
            }

            // The hero's big title is the season, not the lock's own name (owner, 2026-09-23):
            // a default self lock is called "Self-lock", which is a poor thing to set in 88px
            // candy. The real name rides a pill under the clock.
            HeroTitle.Text = snapshot == null ? "" : Loc.Get("chaster_hero_title");
            HeroTitle.Visibility = snapshot == null ? Visibility.Collapsed : Visibility.Visible;

            PaintHeroClock();

            PaintHeroEnds(snapshot, chaster!.BalanceSeconds);

            RefreshPills(lookup, snapshot, chaster!.SafetyHoldRemaining);
            BuildCalendar(snapshot);
        }

        /// <summary>The "Ends" line counts to the same end the live clock does: Chaster's own end
        /// plus what the tab will add. The tooltip names the two parts.</summary>
        private void PaintHeroEnds(LockSnapshot? snapshot, int balance)
        {
            var ends = LiveLockClock.EndsAt(snapshot, balance, DateTime.UtcNow)?.ToLocalTime();
            TxtHeroEnds.Text = ends is { } when ? Loc.GetF("chaster_hero_ends", when.ToString("ddd d MMM HH:mm")) : "";
            TxtHeroEnds.Visibility = ends == null ? Visibility.Collapsed : Visibility.Visible;
            var pending = LiveLockClock.PendingAdd(balance);
            TxtHeroEnds.ToolTip = ends != null && pending > 0 && snapshot?.EndsAtUtc is { } own
                ? Loc.GetF("chaster_hero_ends_tip", own.ToLocalTime().ToString("ddd d MMM HH:mm"), CircesTab.Format(pending))
                : null;
        }

        /// <summary>The unit set the clock was last built for ("dhms", "hms", "ms"), so a second
        /// that changes only digits updates text and rebuilds nothing.</summary>
        private string _clockShape = "";
        private readonly List<TextBlock> _clockNumbers = new();
        /// <summary>Until when the lead number belongs to the count-up on show.</summary>
        private DateTime _leadHeldUntilUtc;

        /// <summary>
        /// The hero's live clock, d h m s, ticking every second (owner, 2026-09-23: CCP's clock is
        /// the first to move; the phone catches up at the next sync). Local arithmetic only, the
        /// same <see cref="LiveLockClock"/> the rail chip uses: the last snapshot plus what the
        /// tab will add. No lock, a hidden timer or no end date: no clock, the pills say why.
        /// </summary>
        internal void PaintHeroClock()
        {
            var chaster = App.Chaster;
            var snapshot = chaster?.IsLinked == true ? chaster.Lock : null;
            var left = LiveLockClock.Remaining(snapshot, chaster?.BalanceSeconds ?? 0, DateTime.UtcNow);
            if (left is not { } remaining)
            {
                HeroClock.Children.Clear();
                _clockNumbers.Clear();
                _clockShape = "";
                _clockLead = null;
                HeroClockRow.Visibility = Visibility.Collapsed;
                return;
            }

            HeroClockRow.Visibility = Visibility.Visible;
            if (remaining <= TimeSpan.Zero)
            {
                if (_clockShape == "ready") return;
                HeroClock.Children.Clear();
                _clockNumbers.Clear();
                _clockShape = "ready";
                _clockLead = null;
                HeroClock.Children.Add(new TextBlock
                {
                    Text = Loc.Get("chaster_clock_ready"), FontFamily = Display, FontSize = 44, FontWeight = FontWeights.Bold,
                    Foreground = EarnBrush, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4),
                });
                return;
            }

            var parts = LiveLockClock.Parts(remaining);
            var shape = string.Concat(parts.Select(p => p.Unit));
            if (shape != _clockShape)
            {
                HeroClock.Children.Clear();
                _clockNumbers.Clear();
                _clockShape = shape;
                _clockLead = null;
                foreach (var part in parts)
                {
                    var number = new TextBlock
                    {
                        Text = part.Value, FontFamily = Display, FontSize = 62, FontWeight = FontWeights.Bold,
                        Foreground = (Brush)FindResource("TextLightBrush"), VerticalAlignment = VerticalAlignment.Bottom,
                        RenderTransformOrigin = new Point(0, 1),
                    };
                    var unit = new TextBlock
                    {
                        Text = Loc.Get("chaster_unit_" + part.Unit), FontFamily = Display, FontSize = 22, FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)FindResource("TextMutedBrush"), VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(3, 0, 14, 11),
                    };
                    HeroClock.Children.Add(number);
                    HeroClock.Children.Add(unit);
                    _clockNumbers.Add(number);
                    if (_clockLead == null && int.TryParse(part.Value, out var lead)) _clockLead = (number, lead);
                }
                _leadHeldUntilUtc = DateTime.UtcNow.AddSeconds(1.2);
                return;
            }

            var now = DateTime.UtcNow;
            for (var i = 0; i < parts.Count && i < _clockNumbers.Count; i++)
            {
                if (i == 0 && now < _leadHeldUntilUtc) continue; // the count-up is still running it
                if (_clockNumbers[i].Text != parts[i].Value) _clockNumbers[i].Text = parts[i].Value;
            }
        }

        /// <summary>One pill per thing worth a word: the state, a test lock, a running hold.</summary>
        private void RefreshPills(LockLookup lookup, LockSnapshot? snapshot, TimeSpan hold)
        {
            HeroPills.Children.Clear();
            var state = TabPageText.HeroState(lookup, snapshot);
            switch (state)
            {
                case "chaster_state_frozen": HeroPills.Children.Add(Pill(Loc.Get("chaster_pill_frozen"), IceColour)); break;
                case "chaster_state_hidden": HeroPills.Children.Add(Pill(Loc.Get("chaster_pill_hidden"), MutedColour)); break;
                case "chaster_state_no_lock": HeroPills.Children.Add(Pill(Loc.Get("chaster_pill_nolock"), AmberColour, "chaster_state_no_lock")); break;
                case "chaster_state_pick": HeroPills.Children.Add(Pill(Loc.Get("chaster_pill_pick"), AmberColour, "chaster_state_pick")); break;
                case "chaster_state_away": HeroPills.Children.Add(Pill(Loc.Get("chaster_pill_away"), AmberColour, "chaster_state_away")); break;
            }
            if (snapshot?.IsTestLock == true && state != "chaster_state_test")
                HeroPills.Children.Add(Pill(Loc.Get("chaster_pill_test"), MutedColour));
            else if (state == "chaster_state_test")
                HeroPills.Children.Add(Pill(Loc.Get("chaster_pill_test"), MutedColour));
            if (snapshot != null)
                HeroPills.Children.Insert(0, Pill(string.IsNullOrWhiteSpace(snapshot.Title) ? Loc.Get("chaster_lock_untitled") : snapshot.Title!, MutedColour));
            if (hold > TimeSpan.Zero)
                HeroPills.Children.Add(Pill(Loc.Get("chaster_stat_hold") + " " + $"{(int)hold.TotalMinutes}:{hold.Seconds:00}", MutedColour, "chaster_hold"));
            HeroPills.Visibility = HeroPills.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private static Border Pill(string text, Color colour, string? tipKey = null)
        {
            var pill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x26, colour.R, colour.G, colour.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, colour.R, colour.G, colour.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 8, 6),
                Child = new TextBlock
                {
                    Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(colour),
                },
            };
            if (tipKey != null) pill.ToolTip = Loc.Get(tipKey);
            return pill;
        }

        /// <summary>The paper tag: the tab's amount in ink, when it lands, and a stamp that says
        /// UNPAID while there is something to pay, CLEAR at zero, CREDIT below it.</summary>
        internal void RefreshTag(int balance)
        {
            TxtTagAmount.Text = balance == 0 ? CircesTab.Format(0, signed: false) : CircesTab.Format(balance);
            TxtTagAmount.Foreground = balance > 0 ? Frozen(Color.FromRgb(0xC8, 0x24, 0x4A))
                : balance < 0 ? Frozen(Color.FromRgb(0x1E, 0x8A, 0x6E)) : Frozen(Color.FromRgb(0x24, 0x1A, 0x2E));
            var chaster = App.Chaster;
            var line = TabPageText.Tag(balance, chaster?.PushableTodaySeconds ?? 0, chaster?.IsPaused == true,
                !string.IsNullOrEmpty(App.Settings?.Current?.ChasterLockId) && chaster?.LockLookup != LockLookup.Ambiguous);
            TxtTagLands.Text = line.Today == null ? Loc.Get(line.Key) : Loc.GetF(line.Key, line.Today, line.Later!);
            TxtTagStamp.Text = Loc.Get(balance > 0 ? "chaster_tag_unpaid" : balance < 0 ? "chaster_tag_credit" : "chaster_tag_clear");
            var stampColour = balance > 0 ? Color.FromRgb(0xC8, 0x24, 0x4A) : balance < 0 ? Color.FromRgb(0x1E, 0x8A, 0x6E) : Color.FromRgb(0x6E, 0x66, 0x86);
            TagStamp.BorderBrush = Frozen(stampColour);
            TxtTagStamp.Foreground = Frozen(stampColour);
            TxtCalendarTag.Text = CircesTab.Format(balance);
            CalendarTag.Visibility = balance > 0 && _tonightCell != null ? Visibility.Visible : Visibility.Collapsed;
        }

        // ============================== 2. the calendar ==============================

        internal const double CellSize = 44;
        private const double CrossInset = 10;
        private static readonly Color InkColour = Color.FromRgb(0x24, 0x1A, 0x2E);
        private static readonly Color MarkerColour = Color.FromRgb(0xC8, 0x24, 0x4A);
        private static readonly Brush Ink = Frozen(InkColour);
        private static readonly Brush Marker = Frozen(MarkerColour);
        private static readonly Brush Rule = Frozen(Color.FromArgb(0x38, 0x24, 0x1A, 0x2E));
        private static readonly Brush Foil = MakeFoil();
        private static readonly Geometry KeyGlyph = Geometry.Parse(
            "M 8,0 A 8,8 0 1 0 8,16 A 8,8 0 1 0 8,0 Z M 8,4.5 A 3.5,3.5 0 1 0 8,11.5 A 3.5,3.5 0 1 0 8,4.5 Z M 15,5.5 H 42 V 11 H 38.5 V 8.5 H 34.5 V 12.5 H 30.5 V 8.5 H 15 Z");
        private static ImageBrush? _padlockMask;

        /// <summary>The days of this lock in local time, first to last, for the calendar. Nothing
        /// when there is no lock, no end, or a hidden timer. A lock that never said when it
        /// started counts from today, so the calendar is what is left of it.</summary>
        internal static (DateTime Start, DateTime End)? CalendarSpan(LockSnapshot? snapshot, DateTime localToday)
        {
            if (snapshot is not { TimerHidden: false, EndsAtUtc: { } endUtc }) return null;
            var end = endUtc.ToLocalTime();
            var start = snapshot.StartedAtUtc is { } startUtc ? startUtc.ToLocalTime() : localToday;
            return (start, end);
        }

        internal void BuildCalendar(LockSnapshot? snapshot) => BuildCalendar(CalendarSpan(snapshot, DateTime.Now), DateTime.Now);

        /// <summary>A sheet off a wall calendar: one ruled square a day, a served day crossed out
        /// in red marker, tonight's number circled in red ink with the tab's tag under it, the
        /// days still to serve stamped with a small padlock, and a gold sticker after the last for
        /// the day it opens. Rebuilt only when the lock or the day changes: the hero's tick calls
        /// this every half minute and must not redraw a sheet that has not moved.</summary>
        internal void BuildCalendar((DateTime Start, DateTime End)? span, DateTime today)
        {
            if (span is not { } lockSpan)
            {
                ClearCalendar();
                return;
            }
            var key = (lockSpan.Start.Date, lockSpan.End.Date, today.Date);
            if (_calendarKey == key && CalendarRow.Visibility == Visibility.Visible)
            {
                RefreshTag(App.Chaster?.BalanceSeconds ?? 0);
                return;
            }
            _calendarKey = key;
            Calendar.Children.Clear();
            FxCalendarReset();

            var cells = LockCalendar.CellsFor(lockSpan.Start, lockSpan.End, today);
            var elided = LockCalendar.ElidedDays(lockSpan.Start, lockSpan.End);
            foreach (var day in cells)
            {
                var dayNumber = (day.Date - lockSpan.Start.Date).Days + 1;
                var square = Cell(day, dayNumber, elided);
                Calendar.Children.Add(square);
                if (day.Today) _tonightCell = square;
                if (day.IsKey) _keyCell = square;
            }
            CalendarRow.Visibility = Visibility.Visible;
            RefreshTag(App.Chaster?.BalanceSeconds ?? 0);
            PlaceCalendarTag();
            if (IsVisible) { FxCalendarDrawIn(); StartCalendarLoops(); }
        }

        private void ClearCalendar()
        {
            _calendarKey = null;
            Calendar.Children.Clear();
            FxCalendarReset();
            CalendarRow.Visibility = Visibility.Collapsed;
            CalendarTag.Visibility = Visibility.Collapsed;
        }

        /// <summary>A day on the sheet: a ruled square with its number in the corner, and on it a
        /// marker cross (served), a padlock stamp (still to serve), a red ring (tonight) or the
        /// gold sticker (the key).</summary>
        private FrameworkElement Cell(LockDay day, int dayNumber, int elided)
        {
            var plate = new Grid { Width = CellSize, Height = CellSize };
            var square = new Border
            {
                BorderBrush = Rule, BorderThickness = new Thickness(0, 0, 1, 1),
                Child = plate, SnapsToDevicePixels = true, Background = Brushes.Transparent,
            };
            if (!day.IsKey)
            {
                plate.Children.Add(new TextBlock
                {
                    Text = day.DayOfMonth.ToString(), FontSize = 10.5, FontWeight = FontWeights.Bold, FontFamily = Mono,
                    Foreground = Ink, Opacity = day.Served ? 0.5 : 0.8,
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(4, 2, 0, 0),
                });
            }

            if (day.IsKey)
            {
                plate.Children.Add(Sticker());
                square.ToolTip = Loc.Get("chaster_chain_open");
            }
            else if (day.Served)
            {
                var cross = Cross(dayNumber);
                plate.Children.Add(cross);
                _calendarDraws.Add(cross);
                square.ToolTip = Loc.GetF("chaster_cal_served", dayNumber);
            }
            else if (day.Today)
            {
                _tonightMark = Ring(dayNumber);
                plate.Children.Add(_tonightMark);
                square.ToolTip = Loc.Get("chaster_chain_tonight");
            }
            else
            {
                plate.Children.Add(new Rectangle
                {
                    Width = 8, Height = 11, Fill = Ink, Opacity = 0.5, OpacityMask = PadlockMask(),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 5, 4),
                });
                square.ToolTip = Loc.GetF("chaster_cal_locked", dayNumber);
            }

            if (day.Elided)
            {
                // the first square shown stands for every day before it
                plate.Children.Add(new TextBlock
                {
                    Text = "...", FontSize = 11, FontWeight = FontWeights.Bold, FontFamily = Mono, Foreground = Ink, Opacity = 0.7,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 0, 4, 0), IsHitTestVisible = false,
                });
                square.ToolTip = Loc.GetF("chaster_cal_elided", elided);
            }
            return square;
        }

        /// <summary>A crossed-out day in red marker: two strokes that bend a little and do not
        /// quite meet, different from one day to the next, with a splat where the pen lifted.
        /// Each stroke carries its length in Tag so the draw-in can dash it.</summary>
        private static Canvas Cross(int seed)
        {
            var wobble = (seed % 3) - 1;          // -1, 0, 1
            var lean = ((seed * 7) % 5) - 2;      // -2 .. 2
            double a = CrossInset + lean * 0.5, b = CellSize - CrossInset - lean * 0.5, mid = CellSize / 2;
            var canvas = new Canvas { Width = CellSize, Height = CellSize, IsHitTestVisible = false };
            var p1 = new[] { new Point(a, a + wobble), new Point(mid + wobble * 0.8, mid - 0.6), new Point(b, b - wobble) };
            var p2 = new[] { new Point(b - lean * 0.4, a), new Point(mid - wobble * 0.7, mid + 0.8), new Point(a + lean * 0.4, b) };
            canvas.Children.Add(Stroke(p1));
            canvas.Children.Add(Stroke(p2));
            // the splat where the second stroke ends
            var splat = new Ellipse { Width = 4.6, Height = 4.2, Fill = Marker, Opacity = 0.9 };
            Canvas.SetLeft(splat, p2[^1].X - 2.3 + lean * 0.3);
            Canvas.SetTop(splat, p2[^1].Y - 2.1 + wobble * 0.4);
            canvas.Children.Add(splat);
            return canvas;
        }

        private static Path Stroke(Point[] points)
        {
            var figure = new PathFigure { StartPoint = points[0] };
            for (var i = 1; i < points.Length; i++) figure.Segments.Add(new LineSegment(points[i], true));
            var geometry = new PathGeometry { Figures = { figure } };
            geometry.Freeze();
            double length = 0;
            for (var i = 1; i < points.Length; i++) length += (points[i] - points[i - 1]).Length;
            return new Path
            {
                Data = geometry, Tag = length,
                Stroke = Marker, StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round, Opacity = 0.9,
            };
        }

        /// <summary>Tonight's number ringed in red ink: two passes of a slightly lopsided ellipse,
        /// the second lighter, the way a pen goes round twice.</summary>
        private static Canvas Ring(int seed)
        {
            var canvas = new Canvas
            {
                Width = CellSize, Height = CellSize, IsHitTestVisible = false,
                RenderTransformOrigin = new Point(12.5 / CellSize, 9.5 / CellSize), RenderTransform = new ScaleTransform(1, 1),
            };
            var tilt = -9 + (seed % 4) * 2;
            var first = new EllipseGeometry(new Point(12.5, 9.5), 11.5, 8.2) { Transform = new RotateTransform(tilt, 12.5, 9.5) };
            var second = new EllipseGeometry(new Point(13.2, 10), 12, 7.6) { Transform = new RotateTransform(tilt + 11, 13.2, 10) };
            canvas.Children.Add(new Path { Data = first, Stroke = Marker, StrokeThickness = 1.9, Opacity = 0.9 });
            canvas.Children.Add(new Path { Data = second, Stroke = Marker, StrokeThickness = 1.2, Opacity = 0.5 });
            return canvas;
        }

        /// <summary>The day it opens: a gold foil sticker with the key pressed into it.</summary>
        private static Border Sticker()
        {
            var sticker = new Border
            {
                Width = 30, Height = 19, CornerRadius = new CornerRadius(4), Background = Foil,
                BorderBrush = Frozen(Color.FromArgb(0x80, 0xFF, 0xF4, 0xC0)), BorderThickness = new Thickness(0.8),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(-7),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1, Opacity = 0.4, RenderingBias = RenderingBias.Performance },
                Child = new Path
                {
                    Data = KeyGlyph, Fill = Frozen(Color.FromRgb(0x4A, 0x32, 0x10)), Stretch = Stretch.Uniform, Width = 20, Height = 8,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.85,
                },
            };
            return sticker;
        }

        private static Brush MakeFoil()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0xFF, 0xF0, 0xB0), 0),
                    new GradientStop(Color.FromRgb(0xE0, 0xB0, 0x52), 0.45),
                    new GradientStop(Color.FromRgb(0xB8, 0x86, 0x2E), 0.8),
                    new GradientStop(Color.FromRgb(0xF3, 0xD2, 0x7A), 1),
                },
            };
            brush.Freeze();
            return brush;
        }

        private static ImageBrush PadlockMask()
        {
            if (_padlockMask != null) return _padlockMask;
            var mask = new ImageBrush { Stretch = Stretch.Uniform };
            try { mask.ImageSource = new BitmapImage(new Uri(LockTitle.PadlockArt)); }
            catch (Exception ex) { Diag.Swallowed(ex, "calendar padlock art"); }
            mask.Freeze();
            _padlockMask = mask;
            return mask;
        }

        /// <summary>Park the tag under tonight's square, wherever the sheet put it.</summary>
        private void PlaceCalendarTag()
        {
            try
            {
                if (_tonightCell is not { } cell || cell.ActualWidth <= 0) return;
                var bounds = cell.TransformToVisual(CalendarRow).TransformBounds(new Rect(0, 0, cell.ActualWidth, cell.ActualHeight));
                var x = bounds.X + bounds.Width / 2 - CalendarTag.ActualWidth / 2;
                CalendarTag.Margin = new Thickness(Math.Max(0, x), bounds.Bottom - 14, 0, 0);
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private void Sheet_MouseEnter(object sender, MouseEventArgs e) => FxSheetFlutter();

        // ============================== 3. today, and this run ==============================

        // ============================== the two limits ==============================

        private void BtnLimits_Click(object sender, RoutedEventArgs e) =>
            LimitsCard.Visibility = LimitsCard.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

        private void RefreshLimits()
        {
            var caps = App.Chaster?.Caps ?? TabLimits.Default;
            var settings = App.Settings?.Current;
            _loading = true;
            try
            {
                // The sliders sit where the player put them: a raise still waiting shows at its
                // waiting figure, and the line under the number says when it lands.
                SliderDayLimit.Value = Wanted(settings?.ChasterDayLimit, caps.DailySeconds / 60);
                SliderBacklogLimit.Value = Wanted(settings?.ChasterBacklogLimit, caps.BacklogSeconds / 60);
                ChkRelock.IsChecked = App.Settings?.Current?.ChasterRelockPastEnd == true;
            }
            finally { _loading = false; }
            PaintLimits(caps);
        }

        private static int Wanted(LimitSetting? setting, int inForce)
        {
            if (setting is not { } s) return inForce;
            var settled = LimitChange.Settle(s, DateTime.UtcNow);
            return settled.HasPending ? settled.PendingMinutes : inForce;
        }

        private void PaintLimits(TabLimits caps)
        {
            TxtDayLimit.Text = CircesTab.Format(caps.DailySeconds, signed: false);
            TxtBacklogLimit.Text = CircesTab.Format(caps.BacklogSeconds, signed: false);
            TxtFactCap1.Text = TxtFactCap2.Text = CircesTab.Format(caps.DailySeconds, signed: false);
            var settings = App.Settings?.Current;
            PaintPending(TxtDayPending, settings?.ChasterDayLimit);
            PaintPending(TxtBacklogPending, settings?.ChasterBacklogLimit);
        }

        private static void PaintPending(TextBlock line, LimitSetting? setting)
        {
            var s = setting is { } v ? LimitChange.Settle(v, DateTime.UtcNow) : default;
            if (!s.HasPending) { line.Visibility = Visibility.Collapsed; return; }
            line.Text = Loc.GetF("chaster_limit_pending", CircesTab.Format(s.PendingMinutes * 60, signed: false),
                s.PendingAtUtc!.Value.ToLocalTime().ToString("ddd HH:mm"));
            line.Visibility = Visibility.Visible;
        }

        private void LimitSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || !IsLoaded) return;
            var settings = App.Settings?.Current;
            if (settings == null) return;
            // The backlog never sits under the day: dragging the day past it carries it along.
            var wanted = TabLimits.FromMinutes((int)SliderDayLimit.Value, (int)SliderBacklogLimit.Value);
            // Down applies now; up waits a day (LimitChange). Each limit is asked on its own.
            var now = DateTime.UtcNow;
            settings.ChasterDayLimit = LimitChange.Request(settings.ChasterDayLimit, wanted.DailySeconds / 60, now);
            settings.ChasterBacklogLimit = LimitChange.Request(settings.ChasterBacklogLimit, wanted.BacklogSeconds / 60, now);
            if ((int)SliderBacklogLimit.Value != wanted.BacklogSeconds / 60)
            {
                _loading = true;
                try { SliderBacklogLimit.Value = wanted.BacklogSeconds / 60; }
                finally { _loading = false; }
            }
            var caps = App.Chaster?.Caps ?? wanted;
            PaintLimits(caps);
            RefreshDay(animate: true);
            App.Settings?.Save();
        }

        private void ChkRelock_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || !IsLoaded) return;
            var settings = App.Settings?.Current;
            if (settings == null) return;
            settings.ChasterRelockPastEnd = ChkRelock.IsChecked == true;
            App.Settings?.Save();
        }

        internal void RefreshDay(bool animate = true)
        {
            var chaster = App.Chaster;
            if (chaster == null) return;
            var today = chaster.TodayAddedSeconds;
            var capSeconds = chaster.Caps.DailySeconds;
            var cap = CircesTab.Format(capSeconds, signed: false);
            TxtToday.Text = CircesTab.Format(today, signed: false);
            TxtTodayCap.Text = "/ " + cap;
            TxtTodaySub.Text = Loc.GetF("chaster_stat_today_sub", cap);
            _capFraction = TabPageText.CapFraction(today, capSeconds);
            LayoutCap(animate);

            // Only spin at a second while there is a second-by-second number to show.
            var wanted = chaster.SafetyHoldRemaining > TimeSpan.Zero ? HoldTick : SlowTick;
            if (_tick.Interval != wanted) _tick.Interval = wanted;
        }

        /// <summary>The meter is a fill the code widens against the track's measured width, so it
        /// can be animated; the track's SizeChanged keeps it right when the card resizes.</summary>
        private void LayoutCap(bool animate)
        {
            var width = Math.Max(0, CapTrack.ActualWidth * _capFraction);
            if (animate && Math.Abs(width - CapFill.Width) > 0.5)
            {
                CapBloom.Width = width;
                MotionFx.BarFill(CapFill, double.IsNaN(CapFill.Width) ? 0 : CapFill.Width, width, CapBloom);
            }
            else
            {
                CapFill.BeginAnimation(WidthProperty, null);
                CapFill.Width = width;
                CapBloom.Width = width;
            }
        }

        private void CapTrack_SizeChanged(object sender, SizeChangedEventArgs e) => LayoutCap(animate: false);

        internal void RefreshRun()
        {
            var net = App.Chaster?.Bill()?.NetSeconds ?? 0;
            TxtRun.Text = net == 0 ? CircesTab.Format(0, signed: false) : CircesTab.Format(net);
            TxtRun.Foreground = FigureBrush(net);
        }

        private void StatRun_Click(object sender, RoutedEventArgs e)
        {
            var open = !_billOpen;
            _billOpen = open;
            if (open) BuildBill();
            FxPop(StatRun, 1.04);
            FxTagTug();
            FxBill(open);
        }

        internal void BuildBill() => Receipt.Show(App.Chaster?.Bill());

        // ============================== link ==============================

        private void ShowLinking(bool linking)
        {
            BtnLink.Visibility = linking ? Visibility.Collapsed : Visibility.Visible;
            LinkingRow.Visibility = linking ? Visibility.Visible : Visibility.Collapsed;
            FxLinking(linking);
        }

        private async void BtnLink_Click(object sender, RoutedEventArgs e)
        {
            var chaster = App.Chaster;
            if (chaster == null || chaster.IsLinking) return;
            TxtLinkNote.Visibility = Visibility.Collapsed;
            ShowLinking(true);
            try
            {
                var outcome = await chaster.LinkAsync(url => Helpers.BrowserLauncher.OpenUrlOrPrompt(url, "link Chaster"));
                var note = outcome switch
                {
                    LinkOutcome.Denied => "chaster_link_denied",
                    LinkOutcome.TimedOut => "chaster_link_timeout",
                    LinkOutcome.Failed => "chaster_link_failed",
                    _ => null, // Linked repaints through LinkChanged; a cancel needs no comment
                };
                if (note != null)
                {
                    TxtLinkNote.Text = Loc.Get(note);
                    TxtLinkNote.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster link from the page"); }
            finally { ShowLinking(false); }
        }

        private void BtnCancelLink_Click(object sender, RoutedEventArgs e) => App.Chaster?.CancelLink();

        private async void BtnUnlink_Click(object sender, RoutedEventArgs e) =>
            await ConfirmAndUnlinkAsync(Window.GetWindow(this));

        /// <summary>The account line in the strip and in Settings: the lock's name, or that there is none.</summary>
        internal static string AccountLockLine(ChasterService? chaster)
        {
            var snapshot = chaster?.Lock;
            if (snapshot == null) return Loc.Get("chaster_account_nolock");
            return string.IsNullOrWhiteSpace(snapshot.Title) ? Loc.Get("chaster_lock_untitled") : snapshot.Title!;
        }

        /// <summary>One way out, asked once: the page strip and Settings both come here.</summary>
        internal static async Task<bool> ConfirmAndUnlinkAsync(Window? owner)
        {
            var chaster = App.Chaster;
            if (chaster == null || !chaster.IsLinked) return false;
            var body = Loc.Get("chaster_unlink_confirm_body");
            var title = Loc.Get("chaster_unlink_confirm_title");
            var answer = owner != null
                ? MessageBox.Show(owner, body, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
                : MessageBox.Show(body, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return false;
            try { await chaster.UnlinkAsync(); return true; }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster unlink"); return false; }
        }

        // ============================== the switch, and the one consent ==============================

        private void PaintSwitch(bool on)
        {
            TxtSwitchState.Text = Loc.Get(on ? "chaster_switch_on" : "chaster_switch_off");
            TxtSwitchState.Foreground = on ? CostBrush : (Brush)FindResource("TextMutedBrush");
            SwitchPill.BorderBrush = on ? CostBrush : (Brush)FindResource("GlassBorderBrush");
        }

        private void ChkTab_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var wanted = ChkTab.IsChecked == true;
            PaintSwitch(wanted);
            if (App.Settings?.Current is not { } settings) return;

            // The first time anyone switches this on, the four facts come first. Inline, not a
            // modal: a modal is something to dismiss, and this is something to read.
            if (wanted && !settings.ChasterConsentSeen)
            {
                _loading = true;
                try { ChkTab.IsChecked = false; }
                finally { _loading = false; }
                ConsentCard.Visibility = Visibility.Visible;
                FxConsentShown();
                return;
            }

            ConsentCard.Visibility = Visibility.Collapsed;
            settings.ChasterTabEnabled = wanted;
            App.Settings?.Save();
            FxSwitch(wanted);
        }

        private void BtnConsentOk_Click(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current is not { } settings) return;
            settings.ChasterConsentSeen = true;
            settings.ChasterTabEnabled = true;
            App.Settings?.Save();
            _loading = true;
            try { ChkTab.IsChecked = true; }
            finally { _loading = false; }
            PaintSwitch(true);
            FxConsentOk();
            ConsentCard.Visibility = Visibility.Collapsed;
        }

        // ============================== the lock picker ==============================

        private async Task LoadLocksAsync()
        {
            var chaster = App.Chaster;
            if (chaster?.IsLinked != true) return;
            try
            {
                var locks = await chaster.GetLocksAsync();
                // Chaster is away, or the link just died (LinkChanged repaints for that). None: the
                // pill says so. Otherwise the pick shows whenever it is not made: nothing is ever
                // pushed to a lock the player did not pick, not even the only one.
                if (locks == null || locks.Count == 0)
                {
                    LockRow.Visibility = Visibility.Collapsed;
                    return;
                }
                var chosen = App.Settings?.Current?.ChasterLockId;
                var picked = locks.Any(l => l.Id == chosen);
                if (picked && locks.Count == 1)
                {
                    LockRow.Visibility = Visibility.Collapsed;
                    return;
                }
                LockRow.Visibility = Visibility.Visible;
                var oneTap = locks.Count == 1;
                CmbLock.Visibility = oneTap ? Visibility.Collapsed : Visibility.Visible;
                UseLockPill.Visibility = oneTap ? Visibility.Visible : Visibility.Collapsed;
                if (oneTap)
                {
                    TxtUseLock.Text = Loc.GetF("chaster_lock_use", TitleOf(locks[0]));
                    BtnUseLock.Tag = locks[0].Id;
                }
                else FillLockPicker(locks);
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster lock list for the page"); }
        }

        private static string TitleOf(ChasterLock l) =>
            string.IsNullOrWhiteSpace(l.Title) ? Loc.Get("chaster_lock_untitled") : l.Title!;

        private void FillLockPicker(IReadOnlyList<ChasterLock> locks)
        {
            var chosen = App.Settings?.Current?.ChasterLockId;
            _loading = true;
            try
            {
                CmbLock.Items.Clear();
                // With two locks and none chosen nothing is ever pushed, so say what to do.
                if (locks.All(l => l.Id != chosen))
                    CmbLock.Items.Add(new ComboBoxItem { Content = Loc.Get("chaster_lock_pick"), Tag = null, IsSelected = true });
                foreach (var l in locks)
                    CmbLock.Items.Add(new ComboBoxItem { Content = TitleOf(l), Tag = l.Id, IsSelected = l.Id == chosen });
            }
            finally { _loading = false; }
        }

        private void CmbLock_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || App.Settings?.Current is not { } settings) return;
            if ((CmbLock.SelectedItem as ComboBoxItem)?.Tag is not string id) return;
            PickLock(id);
        }

        private void BtnUseLock_Click(object sender, RoutedEventArgs e)
        {
            if (BtnUseLock.Tag is string id) PickLock(id);
        }

        private void PickLock(string id)
        {
            if (App.Settings?.Current is not { } settings) return;
            settings.ChasterLockId = id;
            App.Settings?.Save();
            UseLockPill.Visibility = Visibility.Collapsed;
            // The hero is showing the old lock until this lands; a waiting balance can go now.
            _ = RepickAsync();
        }

        private async Task RepickAsync()
        {
            var chaster = App.Chaster;
            if (chaster == null) return;
            try
            {
                await chaster.RefreshLockAsync();
                chaster.NoteChoiceChanged();
                await Dispatcher.InvokeAsync(() => { _ = LoadLocksAsync(); RefreshNumbers(animate: false); });
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster lock pick"); }
        }

        // ============================== pause ==============================

        private void PaintPause(bool paused)
        {
            TxtPause.Text = Loc.Get(paused ? "chaster_paused" : "chaster_pause");
            TxtPause.Foreground = paused ? Frozen(Color.FromRgb(0xE0, 0xB0, 0x52)) : (Brush)FindResource("TextLightBrush");
            PausePill.BorderBrush = paused ? Frozen(Color.FromRgb(0xE0, 0xB0, 0x52)) : (Brush)FindResource("GlassBorderBrush");
            PausePill.ToolTip = Loc.Get(paused ? "chaster_paused_tip" : "chaster_pause_tip");
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current is not { } settings) return;
            settings.ChasterPaused = !settings.ChasterPaused;
            App.Settings?.Save();
            PaintPause(settings.ChasterPaused);
            App.Chaster?.NoteChoiceChanged();
            RefreshNumbers(animate: false);
        }

        // ============================== 4. the keys ==============================

        private void Preset_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as ToggleButton)?.Tag is not string id || sender is not ToggleButton tile) return;
            if (id == TabPresets.Custom)
            {
                // The fourth key is a mirror of a hand-built set; pressing it rewrites nothing.
                RefreshPresets();
                FxPreset(tile, CustomColour);
                return;
            }
            if (App.Settings?.Current is not { } settings) return;
            var ids = TabPresets.Apply(id);
            if (ids.Count == 0) return;
            settings.ChasterPrices = new List<string>(ids);
            // The preset sets the stakes too: a lower limit now, a higher one after its day.
            if (TabPresets.Find(id) is { } preset)
                (settings.ChasterDayLimit, settings.ChasterBacklogLimit) =
                    TabPresets.RequestLimits(preset, settings.ChasterDayLimit, settings.ChasterBacklogLimit, DateTime.UtcNow);
            App.Settings?.Save();
            ApplyPriceToggles();
            RefreshPresets();
            RefreshLimits();
            RefreshDay(animate: true);
            FxPreset(tile, PresetColour(id));
            FxKeyTurned(LitRows());
        }

        internal static Color PresetColour(string id) => id switch
        {
            TabPresets.Gentle => EarnColour,
            TabPresets.Strict => CostColour,
            TabPresets.Circe => JackpotColour,
            _ => CustomColour,
        };

        /// <summary>Light the key for the set that is on. The fourth key lights for a hand-built set.</summary>
        internal void RefreshPresets()
        {
            var match = TabPresets.Match(App.Settings?.Current?.ChasterPrices);
            BtnPresetGentle.IsChecked = match == TabPresets.Gentle;
            BtnPresetStrict.IsChecked = match == TabPresets.Strict;
            BtnPresetCirce.IsChecked = match == TabPresets.Circe;
            BtnPresetCustom.IsChecked = match == TabPresets.Custom;
            Stakes(TxtStakesGentle, TabPresets.Gentle);
            Stakes(TxtStakesStrict, TabPresets.Strict);
            Stakes(TxtStakesCirce, TabPresets.Circe);
        }

        /// <summary>"Worst month +4 days": what the preset can cost past the lock end.</summary>
        private static void Stakes(TextBlock line, string presetId)
        {
            if (TabPresets.Find(presetId) is not { } preset) return;
            var (value, days) = TabPresets.WorstMonth(preset);
            line.Text = Loc.GetF(days ? "chaster_preset_stakes_days" : "chaster_preset_stakes_hours", value);
        }

        /// <summary>Push the saved set onto the rows. Never the other way round: the settings
        /// list is the truth and the rows are a view of it.</summary>
        internal void ApplyPriceToggles()
        {
            BuildMenu();
            var on = new HashSet<string>(App.Settings?.Current?.ChasterPrices ?? new List<string>(), StringComparer.Ordinal);
            _loading = true;
            try
            {
                foreach (var (id, row) in _priceToggles)
                {
                    row.IsChecked = on.Contains(id);
                    PaintRowLit(id, on.Contains(id));
                }
            }
            finally { _loading = false; }
        }

        private IEnumerable<ToggleButton> LitRows() =>
            _priceToggles.Values.Where(r => r.IsChecked == true);

        // ============================== 5. the menu ==============================

        internal void BuildMenu()
        {
            if (_menuBuilt) return;
            _menuBuilt = true;
            var (costs, earnBacks) = TabPageText.Split(TabPrices.All);
            foreach (var price in costs) CostRows.Children.Add(Row(price, CostColour));
            foreach (var price in earnBacks) EarnRows.Children.Add(Row(price, EarnColour));
        }

        /// <summary>A row is a picture, a short name, where it happens, a tier sign when the
        /// feature needs one, and the price on a stamp. The one row that charges for staying away
        /// says how in its tooltip, and nowhere on the page.</summary>
        private ToggleButton Row(TabPrice price, Color colour)
        {
            var brush = Frozen(colour);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // the picture, dimmed to a shade while the row is off
            var thumb = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(8), ClipToBounds = true, Margin = new Thickness(0, 0, 10, 0) };
            var art = TabMenuCopy.ArtFor(price.Id);
            var dim = new Border { CornerRadius = new CornerRadius(8), Background = Frozen(Color.FromRgb(0x1A, 0x12, 0x30)), Opacity = 0.62, IsHitTestVisible = false };
            var plate = new Grid();
            if (art != null)
            {
                var image = new Image { Stretch = Stretch.UniformToFill };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                try { image.Source = new BitmapImage(new Uri("pack://application:,,,/Resources/" + art)); }
                catch (Exception ex) { Diag.Swallowed(ex, "chaster row art " + art); }
                plate.Children.Add(image);
            }
            else plate.Background = Frozen(Color.FromArgb(0x30, colour.R, colour.G, colour.B));
            plate.Children.Add(dim);
            thumb.Child = plate;
            Grid.SetColumn(thumb, 0);
            grid.Children.Add(thumb);
            _rowDims[price.Id] = dim;

            // the words: a short name, and where it happens with the tier sign beside it
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock
            {
                Text = TabMenuCopy.ShortName(price.Id, Loc.Get), FontFamily = Display, FontSize = 13.5, FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextLightBrush");
            words.Children.Add(name);
            var whereRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 0) };
            var where = new TextBlock { FontSize = 10.5, Opacity = 0.85, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            where.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            where.SetBinding(TextBlock.TextProperty, Bound(TabMenuCopy.WhereKey(price.Id)));
            whereRow.Children.Add(where);
            var tier = TabMenuCopy.BadgeTier(price.Gate);
            if (tier > 0)
            {
                var badge = new TierBadge { Tier = tier, MaxWidthOverride = 46, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                whereRow.Children.Add(badge);
            }
            words.Children.Add(whereRow);
            Grid.SetColumn(words, 1);
            grid.Children.Add(words);

            // the stamp
            var stamp = new TextBlock
            {
                Text = TabPageText.Price(price, Loc.Get("chaster_each")),
                FontFamily = Mono, FontSize = 12.5, FontWeight = FontWeights.Bold,
                Foreground = brush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            Grid.SetColumn(stamp, 2);
            grid.Children.Add(stamp);

            var row = new ToggleButton
            {
                Style = (Style)FindResource("CirceRow"),
                Tag = price.Id,
                Background = brush,
                BorderBrush = brush,
                Content = grid,
            };
            if (price.Id == CircesMisses.EventId)
            {
                var tip = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
                tip.SetBinding(TextBlock.TextProperty, Bound("chaster_misses_hint"));
                row.ToolTip = tip;
            }
            row.Click += PriceToggle_Changed;
            row.MouseEnter += Row_MouseEnter;
            row.MouseLeave += Row_MouseLeave;
            _priceToggles[price.Id] = row;
            return row;
        }

        private void PaintRowLit(string id, bool on)
        {
            if (_rowDims.TryGetValue(id, out var dim)) dim.Opacity = on ? 0 : 0.62;
        }

        private static Binding Bound(string key) =>
            new($"[{key}]") { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay };

        private void PriceToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || App.Settings?.Current is not { } settings) return;
            if (sender is not ToggleButton row || row.Tag is not string id) return;
            var on = row.IsChecked == true;
            // A new list every time: the service reads the setting fresh on each event, maybe
            // from another thread, and must never see a list that is being edited.
            var next = new List<string>(settings.ChasterPrices ?? new List<string>());
            next.Remove(id);
            if (on) next.Add(id);
            settings.ChasterPrices = next;
            App.Settings?.Save();
            PaintRowLit(id, on);
            // One hand-flipped row can land exactly on a preset, or step off one. Say which.
            RefreshPresets();
            FxRow(row, on, ((SolidColorBrush)row.BorderBrush).Color);
        }

        // ============================== the trailer ==============================

        private void Row_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not ToggleButton row) return;
            _trailerClose.Stop();
            _trailerRow = row;
            if (_trailerShown) OpenTrailer();
            else { _trailerOpen.Stop(); _trailerOpen.Start(); }
        }

        private void Row_MouseLeave(object sender, MouseEventArgs e)
        {
            _trailerOpen.Stop();
            _trailerClose.Stop();
            _trailerClose.Start();
        }

        private void Trailer_MouseEnter(object sender, MouseEventArgs e)
        {
            _overTrailer = true;
            _trailerClose.Stop();
        }

        private void Trailer_MouseLeave(object sender, MouseEventArgs e)
        {
            _overTrailer = false;
            _trailerClose.Stop();
            _trailerClose.Start();
        }

        /// <summary>The id the trailer is aimed at while it is up, for the tests. Null when closed.
        /// Kept apart from the popup's own IsOpen: without a window behind it a Popup may refuse
        /// to open, and the dressing must still be right.</summary>
        internal string? TrailerId => _trailerShown ? _trailerRow?.Tag as string : null;

        /// <summary>Aim the one popup at the hovered row and dress it for that row's price.</summary>
        internal void OpenTrailer(ToggleButton? row = null)
        {
            row ??= _trailerRow;
            if (row?.Tag is not string id || TabPrices.Find(id) is not { } price) return;
            _trailerRow = row;
            try
            {
                var art = TabMenuCopy.ArtFor(id);
                TrailerArt.Source = art == null ? null : new BitmapImage(new Uri("pack://application:,,,/Resources/" + art));
                TxtTrailerFlavour.Text = Loc.Get(TabMenuCopy.FlavourKey(id));
                TxtTrailerWhy.Text = Loc.Get(TabMenuCopy.WhyKey(id));
                var tier = TabMenuCopy.BadgeTier(price.Gate);
                TrailerBadgeHost.Child = tier > 0 ? new TierBadge { Tier = tier, MaxWidthOverride = 64 } : null;
                // The saved scene, in the one shared browser; the picture stays under it as the
                // loading frame and takes over if the browser cannot.
                if (ChasterTrailerView.BrowserEnabled && !TrailerWeb.HasFailed)
                {
                    TrailerWeb.Visibility = Visibility.Visible;
                    TrailerWeb.Show(TabMenuCopy.VignetteFor(id));
                }
                else TrailerWeb.Visibility = Visibility.Collapsed;
                Trailer.PlacementTarget = row;
                _trailerShown = true;
                FxTrailerStart(price);
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster trailer"); }
            try { Trailer.IsOpen = true; }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster trailer open"); }
        }

        internal void HideTrailer()
        {
            _trailerOpen.Stop();
            _trailerClose.Stop();
            _overTrailer = false;
            if (!_trailerShown) return;
            _trailerShown = false;
            FxTrailerStop();
            try { Trailer.IsOpen = false; }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster trailer close"); }
        }
    }
}
