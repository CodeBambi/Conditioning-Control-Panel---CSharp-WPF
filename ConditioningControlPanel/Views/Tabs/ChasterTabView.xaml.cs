using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// Circe's tab, the page. Reads <see cref="ChasterService"/>, writes four settings, decides
    /// nothing: every rule (what books, what is capped, what reaches the lock) lives in
    /// Services/Chaster and is tested there, and every string the page composes comes out of
    /// <see cref="TabPageText"/> or <see cref="TabPresets"/> so it is tested without a window.
    /// </summary>
    public partial class ChasterTabView : UserControl
    {
        private static readonly Brush CostBrush = Frozen(0xFF, 0x8F, 0xA3);
        private static readonly Brush EarnBrush = Frozen(0x5F, 0xFF, 0xD0);
        private static readonly Brush PresetOnBrush = Frozen(0xFF, 0x8F, 0xA3);
        private static readonly Brush PresetOffBrush = Frozen(0x77, 0x72, 0x80);

        /// <summary>Half a minute is as fine as the hero needs; a running safety hold is counted
        /// in seconds because it is the one number the player is sitting there waiting out.</summary>
        private static readonly TimeSpan SlowTick = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan HoldTick = TimeSpan.FromSeconds(1);

        private bool _loading;
        private bool _rowsBuilt;
        private bool _subscribed;
        private readonly Dictionary<string, CheckBox> _priceToggles = new();
        private readonly DispatcherTimer _tick;

        public ChasterTabView()
        {
            InitializeComponent();
            _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = SlowTick };
            _tick.Tick += (_, _) => { RefreshHero(); RefreshDay(); };
            // Only listen while the page is on screen: Booked fires on every priced event.
            IsVisibleChanged += (_, _) => Subscribe(IsVisible);
        }

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
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
            }
            else
            {
                chaster.Booked -= OnBooked;
                chaster.LinkChanged -= OnLinkChanged;
                chaster.LockChanged -= OnLockChanged;
                _tick.Stop();
            }
        }

        // All three events arrive on whatever thread found out.
        private void OnBooked(string eventId, TabBooking booking) => Dispatcher.BeginInvoke(new Action(RefreshNumbers));
        private void OnLinkChanged() => Dispatcher.BeginInvoke(new Action(OnTabShown));
        private void OnLockChanged() => Dispatcher.BeginInvoke(new Action(RefreshHero));

        private void Refresh()
        {
            var chaster = App.Chaster;
            var linked = chaster?.IsLinked == true;
            UnlinkedPanel.Visibility = linked ? Visibility.Collapsed : Visibility.Visible;
            LinkedPanel.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
            BtnLink.IsEnabled = chaster != null;
            ShowLinking(chaster?.IsLinking == true);
            if (!linked) return;

            var settings = App.Settings?.Current;
            _loading = true;
            try { ChkTab.IsChecked = settings?.ChasterTabEnabled == true; }
            finally { _loading = false; }
            ConsentCard.Visibility = Visibility.Collapsed;

            // A set the player built by hand is worth showing, so open Customize on it. A preset,
            // or nothing at all, reads better as chips.
            var match = TabPresets.Match(settings?.ChasterPrices);
            if (match == TabPresets.Custom) ShowCustomize(true);
            // A tab that is on with nothing priced does nothing, so open the list for them.
            if (match == null) PricesExpander.IsExpanded = true;
            RefreshPresets();
            RefreshHero();
            RefreshNumbers();
        }

        private void RefreshNumbers()
        {
            var chaster = App.Chaster;
            if (chaster == null) return;
            var balance = chaster.BalanceSeconds;
            TxtBalance.Text = balance == 0 ? CircesTab.Format(0, signed: false) : CircesTab.Format(balance);
            TxtBalance.Foreground = balance > 0 ? CostBrush : balance < 0 ? EarnBrush : (Brush)FindResource("TextLightBrush");
            TxtBalanceCaption.Text = Loc.Get(balance < 0 ? "chaster_credit_caption" : "chaster_balance_caption");
            RefreshDay();
            if (Receipt.IsVisible) BuildBill();
        }

        // ============================== 1. the lock ==============================

        internal void RefreshHero()
        {
            var chaster = App.Chaster;
            if (chaster == null) return;
            var snapshot = chaster.Lock;
            var lookup = chaster.LockLookup;

            var title = snapshot == null ? null
                : string.IsNullOrWhiteSpace(snapshot.Title) ? Loc.Get("chaster_lock_untitled") : snapshot.Title;
            TxtHeroTitle.Text = title ?? "";
            TxtHeroTitle.Visibility = title == null ? Visibility.Collapsed : Visibility.Visible;

            // No countdown means no clock at all: a placeholder glyph is one more thing to
            // translate and says less than the state line right under it already says.
            var left = snapshot?.Remaining(DateTime.UtcNow);
            if (left is { } remaining)
            {
                var parts = TabPageText.Countdown(remaining);
                TxtHeroClock.Text = parts.Count == 0
                    ? Loc.Get("chaster_left_soon")
                    : string.Join(" ", parts.Select(p => Loc.GetF(p.Key, p.Value)));
                HeroClockRow.Visibility = Visibility.Visible;
            }
            else HeroClockRow.Visibility = Visibility.Collapsed;

            var ends = snapshot is { TimerHidden: false, EndsAtUtc: { } endUtc } ? endUtc.ToLocalTime() : (DateTime?)null;
            TxtHeroEnds.Text = ends is { } when ? Loc.GetF("chaster_hero_ends", when.ToString("g")) : "";
            TxtHeroEnds.Visibility = ends == null ? Visibility.Collapsed : Visibility.Visible;

            var stateKey = TabPageText.HeroState(lookup, snapshot);
            TxtHeroState.Text = stateKey == null ? "" : Loc.Get(stateKey);
            TxtHeroState.Visibility = stateKey == null ? Visibility.Collapsed : Visibility.Visible;
        }

        // ============================== 2. today ==============================

        internal void RefreshDay()
        {
            var chaster = App.Chaster;
            if (chaster == null) return;
            var today = chaster.TodayAddedSeconds;
            TxtToday.Text = Loc.GetF("chaster_today",
                CircesTab.Format(today, signed: false),
                CircesTab.Format(CircesTab.DailyCapSeconds, signed: false));

            var filled = TabPageText.CapFraction(today);
            CapFilled.Width = new GridLength(filled, GridUnitType.Star);
            CapRest.Width = new GridLength(1 - filled, GridUnitType.Star);

            var hold = chaster.SafetyHoldRemaining;
            if (hold > TimeSpan.Zero)
            {
                TxtHold.Text = Loc.GetF("chaster_hold", $"{(int)hold.TotalMinutes}:{hold.Seconds:00}");
                TxtHold.Visibility = Visibility.Visible;
            }
            else TxtHold.Visibility = Visibility.Collapsed;

            // Only spin at a second while there is a second-by-second number to show.
            var wanted = hold > TimeSpan.Zero ? HoldTick : SlowTick;
            if (_tick.Interval != wanted) _tick.Interval = wanted;
        }

        // ============================== link ==============================

        private void ShowLinking(bool linking)
        {
            BtnLink.Visibility = linking ? Visibility.Collapsed : Visibility.Visible;
            LinkingRow.Visibility = linking ? Visibility.Visible : Visibility.Collapsed;
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

        private async void BtnUnlink_Click(object sender, RoutedEventArgs e)
        {
            try { if (App.Chaster is { } chaster) await chaster.UnlinkAsync(); }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster unlink from the page"); }
        }

        // ============================== 3. the switch, and the one consent ==============================

        private void ChkTab_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || App.Settings?.Current is not { } settings) return;
            var wanted = ChkTab.IsChecked == true;

            // The first time anyone switches this on, the four facts come first. Inline, not a
            // modal: a modal is something to dismiss, and this is something to read.
            if (wanted && !settings.ChasterConsentSeen)
            {
                _loading = true;
                try { ChkTab.IsChecked = false; }
                finally { _loading = false; }
                ConsentCard.Visibility = Visibility.Visible;
                return;
            }

            ConsentCard.Visibility = Visibility.Collapsed;
            settings.ChasterTabEnabled = wanted;
            App.Settings?.Save();
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
            ConsentCard.Visibility = Visibility.Collapsed;
            // On with nothing priced does nothing at all, so put the sets in front of them.
            if (TabPresets.Match(settings.ChasterPrices) == null) PricesExpander.IsExpanded = true;
        }

        // ============================== the lock picker ==============================

        private async Task LoadLocksAsync()
        {
            var chaster = App.Chaster;
            if (chaster?.IsLinked != true) return;
            try
            {
                var locks = await chaster.GetLocksAsync();
                if (locks == null)
                {
                    // Chaster is away, or the link just died (LinkChanged repaints for that).
                    LockRow.Visibility = Visibility.Collapsed;
                    return;
                }
                LockRow.Visibility = Visibility.Visible;
                CmbLock.Visibility = locks.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
                TxtLock.Visibility = locks.Count > 1 ? Visibility.Collapsed : Visibility.Visible;
                if (locks.Count == 0) TxtLock.Text = Loc.Get("chaster_lock_none");
                else if (locks.Count == 1) TxtLock.Text = TitleOf(locks[0]);
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
            settings.ChasterLockId = id;
            App.Settings?.Save();
            // The hero is showing the old lock until this lands.
            _ = App.Chaster?.RefreshLockAsync();
        }

        // ============================== 4. prices: presets first ==============================

        private void Preset_Click(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current is not { } settings) return;
            if ((sender as Button)?.Tag is not string id) return;
            var ids = TabPresets.Apply(id);
            if (ids.Count == 0) return;
            settings.ChasterPrices = new List<string>(ids);
            App.Settings?.Save();
            ApplyPriceToggles();
            RefreshPresets();
        }

        private void BtnCustomize_Click(object sender, RoutedEventArgs e) =>
            ShowCustomize(CustomizeHost.Visibility != Visibility.Visible);

        private void ShowCustomize(bool show)
        {
            if (show) ApplyPriceToggles();
            CustomizeHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Light the chip for the set that is on, and say in one line what it does.</summary>
        internal void RefreshPresets()
        {
            var match = TabPresets.Match(App.Settings?.Current?.ChasterPrices);
            PaintPreset(BtnPresetGentle, match == TabPresets.Gentle);
            PaintPreset(BtnPresetStrict, match == TabPresets.Strict);
            PaintPreset(BtnPresetCirce, match == TabPresets.Circe);
            BtnPresetCustom.Visibility = match == TabPresets.Custom ? Visibility.Visible : Visibility.Collapsed;
            PaintPreset(BtnPresetCustom, true);
            TxtPresetHint.Text = match == null ? Loc.Get("chaster_prices_hint") : Loc.Get(TabPresets.HintKey(match));
        }

        private static void PaintPreset(Button button, bool on)
        {
            button.BorderBrush = on ? PresetOnBrush : PresetOffBrush;
            button.Foreground = on ? PresetOnBrush : PresetOffBrush;
            button.Opacity = on ? 1 : 0.75;
        }

        /// <summary>Push the saved set onto the switches. Never the other way round: the settings
        /// list is the truth and the rows are a view of it.</summary>
        internal void ApplyPriceToggles()
        {
            BuildPriceRows();
            var on = new HashSet<string>(App.Settings?.Current?.ChasterPrices ?? new List<string>(), StringComparer.Ordinal);
            _loading = true;
            try { foreach (var (id, toggle) in _priceToggles) toggle.IsChecked = on.Contains(id); }
            finally { _loading = false; }
        }

        internal void BuildPriceRows()
        {
            if (_rowsBuilt) return;
            _rowsBuilt = true;
            var (costs, earnBacks) = TabPageText.Split(TabPrices.All);
            foreach (var price in costs) CostRows.Children.Add(PriceRow(price, CostBrush));
            foreach (var price in earnBacks) EarnRows.Children.Add(PriceRow(price, EarnBrush));
        }

        private UIElement PriceRow(TabPrice price, Brush figureBrush)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextLightBrush");
            name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding($"[{TabPageText.NameKey(price.Id)}]")
            {
                Source = LocalizationManager.Instance,
                Mode = System.Windows.Data.BindingMode.OneWay,
            });
            row.Children.Add(name);

            var figure = new TextBlock
            {
                Text = TabPageText.Price(price, Loc.Get("chaster_each")),
                FontFamily = new FontFamily("Consolas, Courier New"),
                FontSize = 12,
                Foreground = figureBrush,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(figure, 1);
            row.Children.Add(figure);

            var toggle = new CheckBox { Style = (Style)FindResource("ToggleStyle"), VerticalAlignment = VerticalAlignment.Center, Tag = price.Id };
            toggle.Checked += PriceToggle_Changed;
            toggle.Unchecked += PriceToggle_Changed;
            Grid.SetColumn(toggle, 2);
            row.Children.Add(toggle);
            _priceToggles[price.Id] = toggle;
            if (price.Id != CircesMisses.EventId) return row;

            // The one row that charges for staying away says exactly how, right under its switch.
            var hint = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 60, 4) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            hint.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("[chaster_misses_hint]")
            {
                Source = LocalizationManager.Instance,
                Mode = System.Windows.Data.BindingMode.OneWay,
            });
            var stack = new StackPanel();
            stack.Children.Add(row);
            stack.Children.Add(hint);
            return stack;
        }

        private void PriceToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || App.Settings?.Current is not { } settings) return;
            if ((sender as CheckBox)?.Tag is not string id) return;
            // A new list every time: the service reads the setting fresh on each event, maybe
            // from another thread, and must never see a list that is being edited.
            var next = new List<string>(settings.ChasterPrices ?? new List<string>());
            next.Remove(id);
            if (((CheckBox)sender).IsChecked == true) next.Add(id);
            settings.ChasterPrices = next;
            App.Settings?.Save();
            // One hand-flipped switch can land exactly on a preset, or step off one. Say which.
            RefreshPresets();
        }

        // ============================== 5. this run's bill ==============================

        private void BillExpander_Expanded(object sender, RoutedEventArgs e) => BuildBill();

        internal void BuildBill() => Receipt.Show(App.Chaster?.Bill());
    }
}
