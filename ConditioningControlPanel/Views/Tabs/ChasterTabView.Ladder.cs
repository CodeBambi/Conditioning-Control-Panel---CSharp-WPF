using System;
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
    /// The heads-up clock (time CCP added, all time, added only) and the month's ladder that
    /// unrolls under it. The clock is this PC's own count; the ladder is the server's reading of
    /// the lock's Chaster history, so the two can differ and the ladder is the one that counts.
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
        private LadderBoard? _ladderBoard;
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
            try { ChkLadderName.IsChecked = App.Settings?.Current?.ChasterLadderShowName == true; }
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

        // ============================== the board ==============================

        private async Task FetchLadderAsync(bool force)
        {
            var chaster = App.Chaster;
            if (chaster == null || _ladderLoading) return;
            if (!force && DateTime.UtcNow - _ladderFetchedUtc < LadderStale) return;
            _ladderLoading = true;
            try
            {
                var board = await Task.Run(() => chaster.LadderAsync());
                _ladderBoard = board;
                _ladderFetchedUtc = DateTime.UtcNow;
                await Dispatcher.InvokeAsync(() =>
                {
                    PaintLadder();
                    if (LadderPopup.IsOpen) UnrollLadder();
                });
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster ladder fetch"); }
            finally { _ladderLoading = false; }
        }

        private void PaintLadder()
        {
            LadderRows.Children.Clear();
            var board = _ladderBoard;
            string? state = null;
            if (board == null) state = Loc.Get("chaster_ladder_off");
            else
            {
                foreach (var row in board.Rows) LadderRows.Children.Add(BuildLadderRow(row));
                if (ChasterLadder.OwnRowBelow(board) is { } mine)
                {
                    LadderRows.Children.Add(new TextBlock { Text = "...", Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0x98, 0xB8)), Margin = new Thickness(12, 0, 0, 0) });
                    LadderRows.Children.Add(BuildLadderRow(mine));
                }
                if (board.Rows.Count == 0) state = Loc.Get("chaster_ladder_empty");
            }
            // Why this lock is not on it, when the server said so.
            if (App.Chaster?.LastLadderVerify is { Ok: false, Reason: { } reason })
                state = (state == null ? "" : state + " ") + Loc.Get(WhyKey(reason));
            TxtLadderState.Text = state ?? "";
            TxtLadderState.Visibility = state == null ? Visibility.Collapsed : Visibility.Visible;
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

        private static readonly Brush[] PodiumBrushes =
        {
            Frozen(Color.FromRgb(0xE0, 0xB0, 0x52)),
            Frozen(Color.FromRgb(0xC8, 0xC8, 0xD8)),
            Frozen(Color.FromRgb(0xCD, 0x8A, 0x5A)),
        };

        private FrameworkElement BuildLadderRow(LadderRow row)
        {
            var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var rankBrush = row.Rank <= 3 ? PodiumBrushes[row.Rank - 1] : Frozen(Color.FromRgb(0xA8, 0x98, 0xB8));
            var rank = new TextBlock { Text = row.Rank.ToString(), FontFamily = new FontFamily("/Fonts/#Fredoka, Segoe UI"), FontSize = 15, FontWeight = FontWeights.Bold, Foreground = rankBrush, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(rank);

            var name = new TextBlock
            {
                Text = row.You ? $"{row.Name} ({Loc.Get("chaster_ladder_you")})" : row.Name,
                FontSize = 13, FontWeight = row.You ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = row.Named ? FontStyles.Normal : FontStyles.Italic,
                Foreground = row.Named || row.You ? Frozen(Color.FromRgb(0xF3, 0xEA, 0xF7)) : Frozen(Color.FromRgb(0xB9, 0xA9, 0xC9)),
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);

            var figure = new TextBlock { Text = ChasterLadder.FormatClock(row.AddedSeconds), FontFamily = Mono, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = CostBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            Grid.SetColumn(figure, 2);
            grid.Children.Add(figure);

            return new Border
            {
                Child = grid,
                Padding = new Thickness(10, 5, 10, 5),
                CornerRadius = new CornerRadius(10),
                Background = row.You ? Frozen(Color.FromArgb(0x44, 0xFF, 0x6B, 0x8A)) : Brushes.Transparent,
                BorderBrush = row.You ? Frozen(Color.FromArgb(0xAA, 0xFF, 0x6B, 0x8A)) : Brushes.Transparent,
                BorderThickness = new Thickness(row.You ? 1.2 : 0),
            };
        }

        // ============================== the opt-in ==============================

        private void ChkLadderName_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || App.Settings?.Current is not { } settings) return;
            var show = ChkLadderName.IsChecked == true;
            settings.ChasterLadderShowName = show;
            App.Settings?.Save();
            _ = SendLadderNameAsync(show);
        }

        // Best effort: if the server misses it, the next board read sends it again.
        private async Task SendLadderNameAsync(bool show)
        {
            var chaster = App.Chaster;
            if (chaster == null) return;
            try
            {
                if (await Task.Run(() => chaster.SetLadderShowNameAsync(show))) await FetchLadderAsync(force: true);
            }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster ladder name"); }
        }
    }
}
