// PORTED from ConditioningControlPanel/Views/Tabs/ChasterTabView.Ladder.cs and ChasterTabView.Scrap.cs:
// the heads-up clock (time CCP added), the player's own raffle card that unrolls under it, the two
// opt-ins and the month's top ten on the pinned scrap. All rules are Core (ChasterLadder, ChasterRaffle,
// ChasterService.Ladder); this file only draws them.
// ponytail: WPF's SetWindowPos not-topmost fix is not needed - an Avalonia Popup is owned by its
// window, never system-topmost. Tweens are one DispatcherTimer each with WPF's easing formulas.
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class ChasterTabView
    {
        private static readonly TimeSpan LadderCloseGrace = TimeSpan.FromMilliseconds(260);
        private static readonly TimeSpan LadderStale = TimeSpan.FromSeconds(60);
        private const double ScrapTiltDegrees = 2.5;

        private DispatcherTimer? _ladderClose;
        private DispatcherTimer? _addedRoll;
        private bool _ladderPinned, _overLadder, _ladderLoading, _ladderBoardLoading;
        private RaffleCard? _raffleCard;
        private LadderBoard? _ladderBoard;
        private string? _scrapSignature;
        private DateTime _ladderFetchedUtc = DateTime.MinValue;
        private long _addedShown = -1;
        private TopLevel? _ladderHost;
        // Avalonia generates no fields for x:Named transforms, so the rigs are made here.
        private readonly ScaleTransform AddedClockScale = new();
        private readonly ScaleTransform LadderUnroll = new();
        private readonly RotateTransform LadderScrapTilt = new(ScrapTiltDegrees);
        private readonly TranslateTransform LadderScrapDrop = new();
        private readonly RotateTransform AddedClockHand = new();

        private static bool Motion => AmbientFxCanvas.Env.AllowTransitions;

        private void LadderInit()
        {
            AddedClock.RenderTransform = AddedClockScale;
            LadderCard.RenderTransform = LadderUnroll;
            AddedClockHandBar.RenderTransform = AddedClockHand;
            LadderScrap.RenderTransform = new TransformGroup { Children = { LadderScrapTilt, LadderScrapDrop } };
            _ladderClose = new DispatcherTimer { Interval = LadderCloseGrace };
            _ladderClose.Tick += (_, _) =>
            {
                _ladderClose.Stop();
                if (!_ladderPinned && !_overLadder && !AddedClock.IsPointerOver) HideLadder();
            };
            PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && !IsVisible) HideLadder(); };
            DetachedFromVisualTree += (_, _) => HideLadder();
            LadderPopup.Opened += (_, _) => UnrollLadder();
            _loading = true;
            try
            {
                ChkRafflePost.IsChecked = CoreSettings.Current.ChasterRafflePostDays;
                ChkLadderName.IsChecked = CoreSettings.Current.ChasterLadderShowName;
            }
            finally { _loading = false; }
            PaintScrap();
        }

        /// <summary>--render-all has no service: draw the row with a sample board so the frame proves it.</summary>
        private void LadderRenderSample()
        {
            if (!RenderProof.Rendering || ChasterHead.Service != null) return;
            AddedRow.IsVisible = true;
            PaintAddedClock(5 * 3600 + 42 * 60);
            TxtAddedMonth.Text = Loc.GetF("chaster_added_month", ChasterLadder.FormatClock(3 * 3600));
            PaintScrap(new LadderBoard("2026-10", new[] { new LadderRow(1, "Alpha", true, 9000, false), new LadderRow(2, "Locked 1A", false, 5000, false) },
                new LadderRow(23, "Me", true, 100, true), false));
            // the numbers, the open receipt and the calendar sheet, so --render-view draws them
            NumbersPanel.IsVisible = ReceiptHost.IsVisible = true;
            TxtRun.Text = CircesTab.Format(240);
            TxtBalanceCaption.Text = Loc.Get("chaster_balance_caption");
            Receipt.Show(new TabBill(new[] { new TabBillLine("attention", 2, 300), new TabBillLine("typo", 1, -60) }, 300, -60, 0));
            BuildCalendar((DateTime.Today.AddDays(-3), DateTime.Today.AddDays(4)), DateTime.Today);
        }

        /// <summary>WPF LadderOnShown + ScrapOnShown: a page open re-reads the card and the board.</summary>
        private void LadderOnShown()
        {
            ScrapSettle();
            if (ChasterHead.Service?.IsLinked != true) { PaintScrap(); return; }
            _ = FetchScrapAsync();
            _ = FetchLadderAsync(force: true);
        }

        // ============================== the clock ==============================

        /// <summary>WPF RefreshAdded: rolls the clock up whenever the total grew.</summary>
        internal void RefreshAdded()
        {
            var chaster = ChasterHead.Service;
            if (chaster == null) return;
            var total = chaster.AddedLifetimeSeconds;
            TxtAddedMonth.Text = Loc.GetF("chaster_added_month", ChasterLadder.FormatClock(chaster.AddedThisMonthSeconds));
            var from = _addedShown;
            _addedShown = total;
            if (from < 0 || total <= from || !Motion) { PaintAddedClock(total); return; }
            _addedRoll?.Stop();
            _addedRoll = Tween(700, 0, t =>
            {
                PaintAddedClock(from + (long)Math.Round((total - from) * (1 - Math.Pow(1 - t, 3))));
                // then the face pops
                if (t >= 1) Tween(420, 0, p => AddedClockScale.ScaleX = AddedClockScale.ScaleY = 1.12 - 0.12 * ElasticOut(p, 2, 5));
            });
        }

        private void PaintAddedClock(long seconds)
        {
            TxtAddedClock.Text = ChasterLadder.FormatClock(seconds);
            AddedClockHand.Angle = seconds % 3600 / 3600.0 * 360.0; // one turn of the hand per hour added
        }

        // ============================== hover, pin, dismiss ==============================

        private void HoverLift(bool on)
        {
            var from = AddedClockScale.ScaleX;
            var target = on ? 1.02 : 1.0;
            if (!Motion) { AddedClockScale.ScaleX = AddedClockScale.ScaleY = target; return; }
            Tween(150, 0, t => AddedClockScale.ScaleX = AddedClockScale.ScaleY = from + (target - from) * t);
        }

        private void AddedClock_PointerEntered(object? sender, PointerEventArgs e)
        {
            _ladderClose?.Stop();
            HoverLift(true);
            if (!LadderPopup.IsOpen) OpenLadder();
        }

        private void AddedClock_PointerExited(object? sender, PointerEventArgs e)
        {
            HoverLift(false);
            if (_ladderPinned) return;
            _ladderClose?.Stop();
            _ladderClose?.Start();
        }

        private void AddedClock_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            e.Handled = true;
            if (_ladderPinned) { HideLadder(); return; }
            _ladderPinned = true;
            _ladderClose?.Stop();
            if (!LadderPopup.IsOpen) OpenLadder();
            TxtAddedChevron.RenderTransform = new RotateTransform(180);
            ArmOutsideClose(true);
        }

        private void LadderPopup_PointerEntered(object? sender, PointerEventArgs e)
        {
            _overLadder = true;
            _ladderClose?.Stop();
        }

        private void LadderPopup_PointerExited(object? sender, PointerEventArgs e)
        {
            _overLadder = false;
            if (_ladderPinned) return;
            _ladderClose?.Stop();
            _ladderClose?.Start();
        }

        private void ArmOutsideClose(bool on)
        {
            var host = on ? TopLevel.GetTopLevel(this) : _ladderHost;
            if (host == null) return;
            if (on)
            {
                _ladderHost = host;
                host.AddHandler(PointerPressedEvent, LadderHost_PointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
                if (host is Window w) w.Deactivated += LadderHost_Deactivated;
            }
            else
            {
                host.RemoveHandler(PointerPressedEvent, LadderHost_PointerPressed);
                if (host is Window w) w.Deactivated -= LadderHost_Deactivated;
                _ladderHost = null;
            }
        }

        // A press outside the clock and outside the card (an overlay popup shares the window) closes it.
        private void LadderHost_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (AddedClock.IsPointerOver || e.Source is Visual v && (v == LadderCard || LadderCard.IsVisualAncestorOf(v))) return;
            HideLadder();
        }

        private void LadderHost_Deactivated(object? sender, EventArgs e) => HideLadder();

        private void OpenLadder()
        {
            try
            {
                PaintLadder();
                LadderPopup.IsOpen = true;
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] ladder open"); }
            if (DateTime.UtcNow - _ladderFetchedUtc > LadderStale) _ = FetchLadderAsync(force: false);
        }

        internal void HideLadder()
        {
            _ladderClose?.Stop();
            _overLadder = false;
            if (_ladderPinned) ArmOutsideClose(false);
            _ladderPinned = false;
            TxtAddedChevron.RenderTransform = null;
            LadderPopup.IsOpen = false;
        }

        // The card unrolls down from the clock like a paper scroll; the rows follow one by one.
        private void UnrollLadder()
        {
            if (!Motion) { LadderUnroll.ScaleY = 1; return; }
            Tween(360, 0, t => LadderUnroll.ScaleY = 0.05 + 0.95 * BackOut(t, 0.35));
            var i = 0;
            foreach (var row in LadderRows.Children)
            {
                var slide = new TranslateTransform(0, -8);
                row.RenderTransform = slide;
                row.Opacity = 0;
                var delay = 90 + 45 * i++;
                Tween(220, delay, t => row.Opacity = t);
                Tween(260, delay, t => slide.Y = -8 + 8 * (1 - Math.Pow(1 - t, 3)));
            }
        }

        // ============================== the card ==============================

        private async Task FetchLadderAsync(bool force)
        {
            var chaster = ChasterHead.Service;
            if (chaster == null || _ladderLoading) return;
            if (!force && DateTime.UtcNow - _ladderFetchedUtc < LadderStale) return;
            _ladderLoading = true;
            try
            {
                var card = await Task.Run(() => chaster.RaffleAsync());
                _raffleCard = card;
                _ladderFetchedUtc = DateTime.UtcNow;
                PaintLadder();
                if (LadderPopup.IsOpen) UnrollLadder();
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] raffle fetch"); }
            finally { _ladderLoading = false; }
        }

        private static readonly IBrush PipCounted = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x8A));
        private static readonly IBrush PipToday = Brushes.Transparent;
        private static readonly IBrush PipMissed = new SolidColorBrush(Color.FromArgb(0x33, 0xA8, 0x98, 0xB8));
        private static readonly IBrush PipAhead = new SolidColorBrush(Color.FromArgb(0x66, 0x3A, 0x2C, 0x52));
        private static readonly IBrush PipRing = new SolidColorBrush(Color.FromRgb(0xFF, 0xC0, 0xCB));

        /// <summary>Paints a given card (tests, the render path).</summary>
        internal void ShowRaffleCard(RaffleCard? card)
        {
            _raffleCard = card;
            PaintLadder();
        }

        private void PaintLadder()
        {
            var card = _raffleCard;
            RafflePips.Children.Clear();
            string? state = null;
            LadderRows.IsVisible = card != null;
            if (card == null)
            {
                state = Loc.Get("chaster_raffle_off");
                TxtRaffleDay.Text = "";
            }
            else PaintRaffle(card);
            // Why this lock is not counted, when the server said so.
            if (ChasterHead.Service?.LastLadderVerify is { Ok: false, Reason: { } reason })
                state = (state == null ? "" : state + " ") + Loc.Get(WhyKey(reason));
            TxtLadderState.Text = state ?? "";
            TxtLadderState.IsVisible = state != null;
        }

        private void PaintRaffle(RaffleCard card)
        {
            var day = ChasterRaffle.DayShown(card);
            TxtRaffleDay.Text = day > 0 ? Loc.GetF("chaster_raffle_day", day, card.DaysInMonth) : "";
            var pips = ChasterRaffle.Pips(card);
            for (var i = 0; i < pips.Count; i++)
            {
                var pip = new Border
                {
                    Width = 16, Height = 16, Margin = new Thickness(0, 0, 4, 4), CornerRadius = new CornerRadius(8),
                    Background = pips[i] switch
                    {
                        RafflePip.Counted => PipCounted,
                        RafflePip.Today => PipToday,
                        RafflePip.Missed => PipMissed,
                        _ => PipAhead,
                    },
                    BorderBrush = PipRing,
                    BorderThickness = new Thickness(pips[i] == RafflePip.Today ? 1.5 : 0),
                };
                ToolTip.SetTip(pip, (i + 1).ToString());
                RafflePips.Children.Add(pip);
            }
            TxtRaffleDays.Text = Loc.GetF("chaster_raffle_days", ChasterRaffle.DaysCounted(card), card.NeedDays);
            TxtRaffleTotal.Text = Loc.GetF("chaster_raffle_total", ChasterLadder.FormatClock(card.TotalSeconds), ChasterLadder.FormatClock(card.NeedSeconds));
            var (key, arg) = ChasterRaffle.StatusText(card);
            TxtRaffleStatus.Text = arg == null ? Loc.Get(key) : Loc.GetF(key, arg);
            var (bg, fg) = ChasterRaffle.Status(card) switch
            {
                RaffleStatus.InTheDraw or RaffleStatus.Ticket => (0x555FFFD0u, 0xFF5FFFD0u),
                RaffleStatus.Out or RaffleStatus.Missed => (0x33A898B8u, 0xFFC9B8D8u),
                _ => (0x44FF6B8Au, 0xFFFFC0CBu),
            };
            RaffleStatusChip.Background = new SolidColorBrush(Color.FromUInt32(bg));
            TxtRaffleStatus.Foreground = new SolidColorBrush(Color.FromUInt32(fg));
            var due = ChasterRaffle.TicketDue(card);
            TxtRaffleTicketOn.Text = due is { } d ? Loc.GetF("chaster_raffle_ticket_on", d.ToString("MMM d", CultureInfo.CurrentUICulture)) : "";
            TxtRaffleTicketOn.IsVisible = due != null;
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

        // WPF opens ChasterRaffle.RulesUrl (a static page, not an API) or copies it.
        private void LnkRaffleRules_Click(object? sender, RoutedEventArgs e) => _ = OpenAsync(ChasterRaffle.RulesUrl);

        // ============================== the scrap (WPF ChasterTabView.Scrap.cs) ==============================

        private async Task FetchScrapAsync()
        {
            var chaster = ChasterHead.Service;
            if (chaster == null || _ladderBoardLoading) return;
            _ladderBoardLoading = true;
            try
            {
                _ladderBoard = await Task.Run(() => chaster.LadderAsync());
                PaintScrap();
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] ladder board"); }
            finally { _ladderBoardLoading = false; }
        }

        private void PaintScrap() => PaintScrap(ChasterHead.Service?.IsLinked == true ? _ladderBoard : null);

        /// <summary>Draws a board on the scrap; null is the quiet line (no board, or not linked).</summary>
        internal void PaintScrap(LadderBoard? board)
        {
            LadderScrapRows.Children.Clear();
            string? quiet = null;
            if (board == null) quiet = Loc.Get("chaster_ladder_off");
            else if (board.Rows.Count == 0 && board.You == null) quiet = Loc.Get("chaster_ladder_empty");
            else
            {
                foreach (var row in board.Rows) LadderScrapRows.Children.Add(ScrapRow(row));
                if (ChasterLadder.OwnRowBelow(board) is { } mine)
                {
                    LadderScrapRows.Children.Add(new TextBlock
                    {
                        Text = "\u00B7 \u00B7 \u00B7", FontSize = 11, Foreground = new SolidColorBrush(Color.FromUInt32(0x99241A2E)),
                        HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 1),
                    });
                    LadderScrapRows.Children.Add(ScrapRow(mine));
                }
            }
            TxtLadderScrapState.Text = quiet ?? "";
            TxtLadderScrapState.IsVisible = quiet != null;
            // A board that moved nudges the scrap on its pin.
            var sig = board == null ? null : string.Join("|", board.Rows.Concat(board.You is { } y ? new[] { y } : Array.Empty<LadderRow>())
                .Select(r => $"{r.Rank}:{r.Name}:{r.AddedSeconds}:{r.You}"));
            if (_scrapSignature != null && sig != null && sig != _scrapSignature) ScrapSwing(ScrapTiltDegrees + 2.2, 340);
            if (sig != null) _scrapSignature = sig;
        }

        private static Control ScrapRow(LadderRow row)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 1), ColumnDefinitions = new ColumnDefinitions("24,*,Auto") };
            var mono = new FontFamily("Consolas, Courier New");
            grid.Children.Add(new TextBlock
            {
                Text = row.Rank.ToString(), FontFamily = mono, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromUInt32(0x99241A2E)),
            });
            var name = new TextBlock
            {
                Text = row.Name, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x24, 0x1A, 0x2E)),
                TextTrimming = TextTrimming.CharacterEllipsis, FontStyle = row.Named ? FontStyle.Normal : FontStyle.Italic,
                FontWeight = row.You ? FontWeight.Bold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
            };
            var time = new TextBlock
            {
                Text = ChasterLadder.FormatClock(row.AddedSeconds), FontFamily = mono, FontSize = 11, FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x18, 0x30)), VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(name, 1);
            Grid.SetColumn(time, 2);
            grid.Children.Add(name);
            grid.Children.Add(time);
            // my own row: a pink highlighter swipe, a touch crooked
            return new Border
            {
                Child = grid, Padding = new Thickness(3, 1, 3, 1), CornerRadius = new CornerRadius(2),
                Background = row.You ? new SolidColorBrush(Color.FromUInt32(0x70FF6B8A)) : Brushes.Transparent,
                RenderTransform = row.You ? new RotateTransform(-0.8) : null,
            };
        }

        /// <summary>Page open: the scrap drops onto its pin and settles.</summary>
        private void ScrapSettle()
        {
            if (!Motion) { LadderScrapTilt.Angle = ScrapTiltDegrees; LadderScrapDrop.Y = 0; return; }
            Tween(380, 0, t => LadderScrapDrop.Y = -10 + 10 * BackOut(t, 0.4));
            Tween(700, 0, t => LadderScrapTilt.Angle = ScrapTiltDegrees + 6 - 6 * ElasticOut(t, 2, 4));
        }

        private void LadderScrap_PointerEntered(object? sender, PointerEventArgs e) => ScrapSwing(ScrapTiltDegrees + 0.9, 300);

        // WPF keyframes: to the peak at 45 % (quadratic out), home at 100 % (sine in-out).
        private void ScrapSwing(double to, int ms)
        {
            if (AmbientFxCanvas.Env.Level != MotionLevel.Full) return;
            Tween(ms, 0, t => LadderScrapTilt.Angle = t < 0.45
                ? ScrapTiltDegrees + (to - ScrapTiltDegrees) * (1 - Math.Pow(1 - t / 0.45, 2))
                : to + (ScrapTiltDegrees - to) * (0.5 - Math.Cos(Math.PI * (t - 0.45) / 0.55) / 2));
        }

        // ============================== the opt-ins ==============================

        private void ChkRafflePost_Changed(object? sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var post = ChkRafflePost.IsChecked == true;
            CoreSettings.Current.ChasterRafflePostDays = post;
            CoreSettings.Save();
            _ = SendOptInAsync(c => c.SetRafflePostDaysAsync(post), () => FetchLadderAsync(force: true));
        }

        private void ChkLadderName_Changed(object? sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var show = ChkLadderName.IsChecked == true;
            CoreSettings.Current.ChasterLadderShowName = show;
            CoreSettings.Save();
            _ = SendOptInAsync(c => c.SetLadderShowNameAsync(show), FetchScrapAsync);
        }

        // Best effort: if the server misses it, the next read sends it again.
        private static async Task SendOptInAsync(Func<ChasterService, Task<bool>> send, Func<Task> reread)
        {
            if (ChasterHead.Service is not { } chaster) return;
            try { if (await Task.Run(() => send(chaster))) await reread(); }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] ladder opt-in"); }
        }

        // ============================== tweening ==============================

        /// <summary>Runs <paramref name="step"/> with t 0..1 over <paramref name="ms"/>, after <paramref name="delayMs"/>.</summary>
        private static DispatcherTimer Tween(double ms, double delayMs, Action<double> step)
        {
            var started = DateTime.UtcNow.AddMilliseconds(delayMs);
            var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (_, _) =>
            {
                var t = Math.Clamp((DateTime.UtcNow - started).TotalMilliseconds / ms, -1, 1);
                if (t < 0) return;
                step(t);
                if (t >= 1) timer.Stop();
            };
            timer.Start();
            return timer;
        }

        // WPF BackEase / ElasticEase (EaseOut = 1 - EaseIn(1 - t)), same parameters as the original.
        private static double BackOut(double t, double amplitude)
        {
            var u = 1 - t;
            return 1 - (u * u * u - u * amplitude * Math.Sin(Math.PI * u));
        }

        private static double ElasticOut(double t, int oscillations, double springiness)
        {
            var u = 1 - t;
            var expo = (Math.Exp(springiness * u) - 1) / (Math.Exp(springiness) - 1);
            return 1 - expo * Math.Sin((2 * Math.PI * oscillations + Math.PI / 2) * u);
        }
    }
}
