using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// Circe's tab, the page. Reads <see cref="ChasterService"/>, writes three settings, decides
    /// nothing: every rule (what books, what is capped, what reaches the lock) lives in
    /// Services/Chaster and is tested there.
    /// </summary>
    public partial class ChasterTabView : UserControl
    {
        private static readonly Brush CostBrush = Frozen(0xFF, 0x8F, 0xA3);
        private static readonly Brush EarnBrush = Frozen(0x5F, 0xFF, 0xD0);

        private bool _loading;
        private bool _rowsBuilt;
        private bool _subscribed;
        private readonly Dictionary<string, CheckBox> _priceToggles = new();

        public ChasterTabView()
        {
            InitializeComponent();
            // Only listen while the page is on screen: Booked fires on every priced event.
            IsVisibleChanged += (_, _) => Subscribe(IsVisible);
        }

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        /// <summary>ShowTab calls this on every visit. Cheap parts run now; the lock list is a
        /// network call and fills in when it lands.</summary>
        public void OnTabShown()
        {
            Refresh();
            _ = LoadLocksAsync();
        }

        private void Subscribe(bool on)
        {
            var chaster = App.Chaster;
            if (chaster == null || on == _subscribed) return;
            _subscribed = on;
            if (on) { chaster.Booked += OnBooked; chaster.LinkChanged += OnLinkChanged; }
            else { chaster.Booked -= OnBooked; chaster.LinkChanged -= OnLinkChanged; }
        }

        // Both events arrive on whatever thread found out.
        private void OnBooked(string eventId, TabBooking booking) => Dispatcher.BeginInvoke(new Action(RefreshNumbers));
        private void OnLinkChanged() => Dispatcher.BeginInvoke(new Action(OnTabShown));

        private void Refresh()
        {
            var chaster = App.Chaster;
            var linked = chaster?.IsLinked == true;
            UnlinkedPanel.Visibility = linked ? Visibility.Collapsed : Visibility.Visible;
            LinkedPanel.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
            BtnLink.IsEnabled = chaster != null;
            ShowLinking(chaster?.IsLinking == true);
            if (!linked) return;

            BuildPriceRows();
            var settings = App.Settings?.Current;
            _loading = true;
            try
            {
                ChkTab.IsChecked = settings?.ChasterTabEnabled == true;
                var on = new HashSet<string>(settings?.ChasterPrices ?? new List<string>(), StringComparer.Ordinal);
                foreach (var (id, toggle) in _priceToggles) toggle.IsChecked = on.Contains(id);
                // A tab that is on with nothing priced does nothing, so open the list for them.
                if (on.Count == 0) PricesExpander.IsExpanded = true;
            }
            finally { _loading = false; }
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
            TxtToday.Text = Loc.GetF("chaster_today",
                CircesTab.Format(chaster.TodayAddedSeconds, signed: false),
                CircesTab.Format(CircesTab.DailyCapSeconds, signed: false));
            if (BillRows.IsVisible) BuildBill();
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

        // ============================== the switch and the lock ==============================

        private void ChkTab_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || App.Settings?.Current is not { } settings) return;
            settings.ChasterTabEnabled = ChkTab.IsChecked == true;
            App.Settings?.Save();
        }

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
        }

        // ============================== prices ==============================

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
            return row;
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
        }

        // ============================== this run's bill ==============================

        private void BillExpander_Expanded(object sender, RoutedEventArgs e) => BuildBill();

        private void BuildBill()
        {
            var chaster = App.Chaster;
            if (chaster == null) return;
            var bill = chaster.Bill();
            BillRows.Children.Clear();
            if (bill.IsEmpty)
            {
                BillRows.Children.Add(Muted(Loc.Get("chaster_bill_empty")));
                return;
            }
            foreach (var line in bill.Lines)
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var figure = new TextBlock
                {
                    Text = CircesTab.Format(line.Seconds),
                    FontFamily = new FontFamily("Consolas, Courier New"),
                    FontSize = 12,
                    Foreground = line.Seconds > 0 ? CostBrush : EarnBrush,
                };
                DockPanel.SetDock(figure, Dock.Right);
                row.Children.Add(figure);
                var name = new TextBlock { FontSize = 12, Text = Loc.Get(TabPageText.NameKey(line.EventId)) + (line.Count > 1 ? "  x" + line.Count : "") };
                name.SetResourceReference(TextBlock.ForegroundProperty, "TextLightBrush");
                row.Children.Add(name);
                BillRows.Children.Add(row);
            }
            BillRows.Children.Add(Muted(Loc.GetF("chaster_bill_net", CircesTab.Format(bill.NetSeconds))));
            if (bill.PushedSeconds > 0)
                BillRows.Children.Add(Muted(Loc.GetF("chaster_bill_pushed", CircesTab.Format(bill.PushedSeconds, signed: false))));
        }

        private TextBlock Muted(string text)
        {
            var block = new TextBlock { Text = text, FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
            block.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            return block;
        }
    }
}
