using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// The heads-up clock (time CCP added, all time, added only) and the player's own Locktober
    /// raffle card that unrolls under it. The clock is this PC's own count; the card is the
    /// server's reading of the lock's Chaster history, so the two can differ and the card is the
    /// one that counts. No one else's numbers are ever on it.
    ///
    /// <para>Popup rules (AGENTS.md): hover opens with StaysOpen and no capture; a click on the
    /// clock pins; while pinned, a click anywhere outside or the app losing focus closes it. A
    /// Popup is topmost, so it is dropped to not-topmost the moment it opens.</para>
    /// </summary>
    public partial class ChasterTabView
    {
        private static readonly TimeSpan LadderCloseGrace = TimeSpan.FromMilliseconds(260);
        private static readonly TimeSpan LadderStale = TimeSpan.FromSeconds(60);

        private DispatcherTimer? _ladderClose;
        private DispatcherTimer? _addedRoll;
        private bool _ladderPinned;
        private bool _overLadder;
        private bool _ladderLoading;
        private RaffleCard? _raffleCard;
        private DateTime _ladderFetchedUtc = DateTime.MinValue;
        private long _addedShown = -1;
        private Window? _ladderHostWindow;

        private void LadderInit()
        {
            _ladderClose = new DispatcherTimer(DispatcherPriority.Normal) { Interval = LadderCloseGrace };
            _ladderClose.Tick += (_, _) =>
            {
                _ladderClose.Stop();
                if (!_ladderPinned && !_overLadder && !AddedClock.IsMouseOver) HideLadder();
            };
            IsVisibleChanged += (_, _) => { if (!IsVisible) HideLadder(); };
            _loading = true;
            try { ChkRafflePost.IsChecked = App.Settings?.Current?.ChasterRafflePostDays == true; }
            finally { _loading = false; }
        }

        /// <summary>From OnTabShown: a page open is one of the two moments the server re-reads
        /// the lock (the other is a push landing). Throttled inside the service.</summary>
        private void LadderOnShown()
        {
            if (App.Chaster?.IsLinked != true) return;
            _ = FetchLadderAsync(force: true);
        }

        // ============================== the clock ==============================

        /// <summary>From RefreshNumbers. Rolls the clock up to the new total whenever it grew (a
        /// push lands on a quiet refresh, and that is exactly the moment worth showing).</summary>
        internal void RefreshAdded()
        {
            var chaster = App.Chaster;
            if (chaster == null) return;
            var total = chaster.AddedLifetimeSeconds;
            TxtAddedMonth.Text = string.Format(Loc.Get("chaster_added_month"), ChasterLadder.FormatClock(chaster.AddedThisMonthSeconds));
            var from = _addedShown;
            _addedShown = total;
            if (from < 0 || total <= from || !MotionFx.AllowTransitions)
            {
                PaintAddedClock(total);
                return;
            }
            RollAddedClock(from, total);
        }

        private void PaintAddedClock(long seconds)
        {
            TxtAddedClock.Text = ChasterLadder.FormatClock(seconds);
            // one turn of the hand per hour added
            AddedClockHand.Angle = seconds % 3600 / 3600.0 * 360.0;
        }

        // The number counts up like a stopwatch catching up, then the face pops.
        private void RollAddedClock(long from, long to)
        {
            _addedRoll?.Stop();
            var started = DateTime.UtcNow;
            const double ms = 700;
            _addedRoll = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(30) };
            _addedRoll.Tick += (_, _) =>
            {
                var t = Math.Min(1, (DateTime.UtcNow - started).TotalMilliseconds / ms);
                var eased = 1 - Math.Pow(1 - t, 3);
                PaintAddedClock(from + (long)Math.Round((to - from) * eased));
                if (t < 1) return;
                _addedRoll?.Stop();
                PopAddedClock();
            };
            _addedRoll.Start();
        }

        private void PopAddedClock()
        {
            if (!MotionFx.AllowTransitions) return;
            var pop = new DoubleAnimation(1.12, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = new ElasticEase { Oscillations = 2, Springiness = 5, EasingMode = EasingMode.EaseOut } };
            AddedClockScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            AddedClockScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }

        // ============================== hover, pin, dismiss ==============================

        private void AddedClock_MouseEnter(object sender, MouseEventArgs e)
        {
            _ladderClose?.Stop();
            MotionFx.HoverLift(AddedClock, true);
            if (!LadderPopup.IsOpen) OpenLadder();
        }

        private void AddedClock_MouseLeave(object sender, MouseEventArgs e)
        {
            MotionFx.HoverLift(AddedClock, false);
            if (_ladderPinned) return;
            _ladderClose?.Stop();
            _ladderClose?.Start();
        }

        private void AddedClock_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_ladderPinned) { HideLadder(); return; }
            _ladderPinned = true;
            _ladderClose?.Stop();
            if (!LadderPopup.IsOpen) OpenLadder();
            TxtAddedChevron.RenderTransform = new RotateTransform(180);
            ArmOutsideClose(true);
        }

        private void LadderPopup_MouseEnter(object sender, MouseEventArgs e)
        {
            _overLadder = true;
            _ladderClose?.Stop();
        }

        private void LadderPopup_MouseLeave(object sender, MouseEventArgs e)
        {
            _overLadder = false;
            if (_ladderPinned) return;
            _ladderClose?.Stop();
            _ladderClose?.Start();
        }

        private void ArmOutsideClose(bool on)
        {
            var window = on ? Window.GetWindow(this) : _ladderHostWindow;
            if (window == null) return;
            if (on)
            {
                _ladderHostWindow = window;
                window.PreviewMouseDown += LadderHost_PreviewMouseDown;
                window.Deactivated += LadderHost_Deactivated;
            }
            else
            {
                window.PreviewMouseDown -= LadderHost_PreviewMouseDown;
                window.Deactivated -= LadderHost_Deactivated;
                _ladderHostWindow = null;
            }
        }

        // The popup is its own HWND, so a click inside it never reaches the window: anything that
        // does, outside the clock, is a click somewhere else.
        private void LadderHost_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (AddedClock.IsMouseOver) return;
            HideLadder();
        }

        private void LadderHost_Deactivated(object? sender, EventArgs e) => HideLadder();

        private void OpenLadder()
        {
            try
            {
                LadderPopup.PlacementTarget = AddedClock;
                PaintLadder();
                LadderPopup.IsOpen = true;
                UnrollLadder();
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster ladder open"); }
            if (DateTime.UtcNow - _ladderFetchedUtc > LadderStale) _ = FetchLadderAsync(force: false);
        }

        internal void HideLadder()
        {
            _ladderClose?.Stop();
            _overLadder = false;
            if (_ladderPinned) ArmOutsideClose(false);
            _ladderPinned = false;
            TxtAddedChevron.RenderTransform = Transform.Identity;
            try { LadderPopup.IsOpen = false; }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster ladder close"); }
        }

        // A Popup is TOPMOST (PR #1701): one left open would float over every other app.
        private void LadderPopup_Opened(object? sender, EventArgs e)
        {
            try
            {
                if (PresentationSource.FromVisual(LadderCard) is HwndSource src && src.Handle != IntPtr.Zero)
                    SetWindowPos(src.Handle, HwndNotTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster ladder not topmost"); }
        }

        private static readonly IntPtr HwndNotTopmost = new(-2);
        private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoActivate = 0x0010;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        // The card unrolls down from the clock like a paper scroll; the rows follow one by one.
        private void UnrollLadder()
        {
            if (!MotionFx.AllowTransitions)
            {
                LadderUnroll.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                LadderUnroll.ScaleY = 1;
                return;
            }
            var unroll = new DoubleAnimation(0.05, 1, TimeSpan.FromMilliseconds(360)) { EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut } };
            LadderUnroll.BeginAnimation(ScaleTransform.ScaleYProperty, unroll);
            var i = 0;
            foreach (UIElement row in LadderRows.Children)
            {
                if (row is not FrameworkElement fe) continue;
                var delay = TimeSpan.FromMilliseconds(90 + 45 * i++);
                fe.Opacity = 0;
                var slide = new TranslateTransform(0, -8);
                fe.RenderTransform = slide;
                fe.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { BeginTime = delay });
                slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-8, 0, TimeSpan.FromMilliseconds(260)) { BeginTime = delay, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
        }

        // ============================== the card ==============================

        private async Task FetchLadderAsync(bool force)
        {
            var chaster = App.Chaster;
            if (chaster == null || _ladderLoading) return;
            if (!force && DateTime.UtcNow - _ladderFetchedUtc < LadderStale) return;
            _ladderLoading = true;
            try
            {
                var card = await Task.Run(() => chaster.RaffleAsync());
                _raffleCard = card;
                _ladderFetchedUtc = DateTime.UtcNow;
                await Dispatcher.InvokeAsync(() =>
                {
                    PaintLadder();
                    if (LadderPopup.IsOpen) UnrollLadder();
                });
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster raffle fetch"); }
            finally { _ladderLoading = false; }
        }

        private static readonly Brush PipCounted = Frozen(Color.FromRgb(0xFF, 0x6B, 0x8A));
        private static readonly Brush PipToday = Frozen(Color.FromArgb(0x00, 0, 0, 0));
        private static readonly Brush PipMissed = Frozen(Color.FromArgb(0x33, 0xA8, 0x98, 0xB8));
        private static readonly Brush PipAhead = Frozen(Color.FromArgb(0x66, 0x3A, 0x2C, 0x52));
        private static readonly Brush PipRing = Frozen(Color.FromRgb(0xFF, 0xC0, 0xCB));
        private static readonly Brush ChipIn = Frozen(Color.FromArgb(0x55, 0x5F, 0xFF, 0xD0));
        private static readonly Brush ChipInText = Frozen(Color.FromRgb(0x5F, 0xFF, 0xD0));
        private static readonly Brush ChipWait = Frozen(Color.FromArgb(0x44, 0xFF, 0x6B, 0x8A));
        private static readonly Brush ChipWaitText = Frozen(Color.FromRgb(0xFF, 0xC0, 0xCB));
        private static readonly Brush ChipOut = Frozen(Color.FromArgb(0x33, 0xA8, 0x98, 0xB8));
        private static readonly Brush ChipOutText = Frozen(Color.FromRgb(0xC9, 0xB8, 0xD8));

        private void PaintLadder()
        {
            var card = _raffleCard;
            RafflePips.Children.Clear();
            string? state = null;
            if (card == null)
            {
                state = Loc.Get("chaster_raffle_off");
                LadderRows.Visibility = Visibility.Collapsed;
                TxtRaffleDay.Text = "";
            }
            else
            {
                LadderRows.Visibility = Visibility.Visible;
                PaintRaffle(card);
            }
            // Why this lock is not counted, when the server said so.
            if (App.Chaster?.LastLadderVerify is { Ok: false, Reason: { } reason })
                state = (state == null ? "" : state + " ") + Loc.Get(WhyKey(reason));
            TxtLadderState.Text = state ?? "";
            TxtLadderState.Visibility = state == null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void PaintRaffle(RaffleCard card)
        {
            var day = ChasterRaffle.DayShown(card);
            TxtRaffleDay.Text = day > 0 ? string.Format(Loc.Get("chaster_raffle_day"), day, card.DaysInMonth) : "";

            // The month as a strip of pips, lit = counted. 16 px dots wrap 31 days onto two lines.
            var pips = ChasterRaffle.Pips(card);
            for (var i = 0; i < pips.Count; i++)
            {
                var pip = pips[i];
                RafflePips.Children.Add(new Border
                {
                    Width = 16, Height = 16, Margin = new Thickness(0, 0, 4, 4), CornerRadius = new CornerRadius(8),
                    Background = pip switch
                    {
                        RafflePip.Counted => PipCounted,
                        RafflePip.Today => PipToday,
                        RafflePip.Missed => PipMissed,
                        _ => PipAhead,
                    },
                    BorderBrush = PipRing,
                    BorderThickness = new Thickness(pip == RafflePip.Today ? 1.5 : 0),
                    ToolTip = (i + 1).ToString(),
                });
            }

            TxtRaffleDays.Text = string.Format(Loc.Get("chaster_raffle_days"), ChasterRaffle.DaysCounted(card), card.NeedDays);
            TxtRaffleTotal.Text = string.Format(Loc.Get("chaster_raffle_total"),
                ChasterLadder.FormatClock(card.TotalSeconds), ChasterLadder.FormatClock(card.NeedSeconds));

            var (key, arg) = ChasterRaffle.StatusText(card);
            TxtRaffleStatus.Text = arg == null ? Loc.Get(key) : string.Format(Loc.Get(key), arg);
            var (bg, fg) = ChasterRaffle.Status(card) switch
            {
                RaffleStatus.InTheDraw or RaffleStatus.Ticket => (ChipIn, ChipInText),
                RaffleStatus.Out or RaffleStatus.Missed => (ChipOut, ChipOutText),
                _ => (ChipWait, ChipWaitText),
            };
            RaffleStatusChip.Background = bg;
            TxtRaffleStatus.Foreground = fg;

            if (ChasterRaffle.TicketDue(card) is { } due)
            {
                TxtRaffleTicketOn.Text = string.Format(Loc.Get("chaster_raffle_ticket_on"), due.ToString("MMM d", CultureInfo.CurrentUICulture));
                TxtRaffleTicketOn.Visibility = Visibility.Visible;
            }
            else TxtRaffleTicketOn.Visibility = Visibility.Collapsed;
        }

        internal static string WhyKey(string reason) => reason switch
        {
            "test_lock" => "chaster_ladder_why_test_lock",
            "hidden_logs" => "chaster_ladder_why_hidden_logs",
            "too_new" => "chaster_ladder_why_too_new",
            "chaster_taken" => "chaster_ladder_why_chaster_taken",
            "not_wearer" => "chaster_ladder_why_not_wearer",
            _ => "chaster_ladder_why_other",
        };

        // PLACEHOLDER target until the owner publishes the final rules (ChasterRaffle.RulesUrl).
        private void LnkRaffleRules_Click(object sender, RoutedEventArgs e) =>
            Helpers.BrowserLauncher.OpenUrlOrPrompt(ChasterRaffle.RulesUrl, Loc.Get("chaster_raffle_rules"));

        // ============================== the opt-in ==============================

        private void ChkRafflePost_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || App.Settings?.Current is not { } settings) return;
            var post = ChkRafflePost.IsChecked == true;
            settings.ChasterRafflePostDays = post;
            App.Settings?.Save();
            _ = SendRafflePostAsync(post);
        }

        // Best effort: if the server misses it, the next card read sends it again.
        private async Task SendRafflePostAsync(bool post)
        {
            var chaster = App.Chaster;
            if (chaster == null) return;
            try
            {
                if (await Task.Run(() => chaster.SetRafflePostDaysAsync(post))) await FetchLadderAsync(force: true);
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster raffle post days"); }
        }
    }
}
