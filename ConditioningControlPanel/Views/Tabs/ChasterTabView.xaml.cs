using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// Circe's tab, the page. Reads <see cref="ChasterService"/>, writes four settings, decides
    /// nothing: every rule (what books, what is capped, what reaches the lock) lives in
    /// Services/Chaster and is tested there, and every string the page composes comes out of
    /// <see cref="TabPageText"/> or <see cref="TabPresets"/> so it is tested without a window.
    ///
    /// <para>This partial is the DATA: what each figure, pill and chip says. The motion (the
    /// bursts, rings, sheen, the cards arriving) is <c>ChasterTabView.Fx.cs</c>, which this file
    /// only ever calls into through the <c>Fx*</c> hooks, every one of which is safe to call with
    /// nothing on screen and does nothing under MotionFx Off.</para>
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
        private static readonly FontFamily Display = new("/Fonts/#Fredoka, Segoe UI");
        private static readonly FontFamily Mono = new("Consolas, Courier New");

        /// <summary>Half a minute is as fine as the hero needs; a running safety hold is counted
        /// in seconds because it is the one number the player is sitting there waiting out.</summary>
        private static readonly TimeSpan SlowTick = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan HoldTick = TimeSpan.FromSeconds(1);

        private bool _loading;
        private bool _chipsBuilt;
        private bool _subscribed;
        private double _capFraction;
        private readonly Dictionary<string, ToggleButton> _priceToggles = new();
        private readonly DispatcherTimer _tick;

        /// <summary>The first number of the countdown and its value, for the count-up on show.</summary>
        private (TextBlock Block, int Value)? _clockLead;

        public ChasterTabView()
        {
            InitializeComponent();
            _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = SlowTick };
            _tick.Tick += (_, _) => { RefreshHero(); RefreshDay(animate: false); };
            // Only listen while the page is on screen: Booked fires on every priced event.
            IsVisibleChanged += (_, _) => Subscribe(IsVisible);
            FxInit();
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
            FxOnShown();
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

        private void OnLockChanged() => Dispatcher.BeginInvoke(new Action(RefreshHero));

        private void Refresh()
        {
            var chaster = App.Chaster;
            var linked = chaster?.IsLinked == true;
            UnlinkedPanel.Visibility = linked ? Visibility.Collapsed : Visibility.Visible;
            FactRow.Visibility = linked ? Visibility.Collapsed : Visibility.Visible;
            LinkedPanel.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
            SwitchPill.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
            BtnLink.IsEnabled = chaster != null;
            ShowLinking(chaster?.IsLinking == true);
            RefreshHero();
            if (!linked) return;

            var settings = App.Settings?.Current;
            var on = settings?.ChasterTabEnabled == true;
            _loading = true;
            try { ChkTab.IsChecked = on; }
            finally { _loading = false; }
            PaintSwitch(on);
            ConsentCard.Visibility = Visibility.Collapsed;

            // A set the player built by hand is worth showing, so the chips open on it. A preset,
            // or nothing at all, reads better as the four tiles alone.
            ShowCustomize(TabPresets.Match(settings?.ChasterPrices) == TabPresets.Custom);
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
            RefreshDay(animate);
            RefreshRun();
            if (ReceiptHost.Visibility == Visibility.Visible) BuildBill();
        }

        private Brush FigureBrush(int seconds) =>
            seconds > 0 ? CostBrush : seconds < 0 ? EarnBrush : (Brush)FindResource("TextLightBrush");

        // ============================== 1. the lock ==============================

        internal void RefreshHero()
        {
            var chaster = App.Chaster;
            var snapshot = chaster?.Lock;
            var lookup = chaster?.LockLookup ?? LockLookup.Unlinked;
            var linked = chaster?.IsLinked == true;
            _clockLead = null;

            if (!linked)
            {
                // The hero is the ask: nothing about a lock it cannot know.
                TxtHeroTitle.Visibility = Visibility.Collapsed;
                HeroClockRow.Visibility = Visibility.Collapsed;
                TxtHeroEnds.Visibility = Visibility.Collapsed;
                HeroPills.Children.Clear();
                HeroPills.Visibility = Visibility.Collapsed;
                LockRow.Visibility = Visibility.Collapsed;
                return;
            }

            var title = snapshot == null ? null
                : string.IsNullOrWhiteSpace(snapshot.Title) ? Loc.Get("chaster_lock_untitled") : snapshot.Title;
            TxtHeroTitle.Text = (title ?? "").ToUpperInvariant();
            TxtHeroTitle.Visibility = title == null ? Visibility.Collapsed : Visibility.Visible;

            // No countdown means no clock at all: the pills under it say why.
            HeroClock.Children.Clear();
            var left = snapshot?.Remaining(DateTime.UtcNow);
            if (left is { } remaining)
            {
                var parts = TabPageText.Countdown(remaining);
                if (parts.Count == 0)
                {
                    HeroClock.Children.Add(new TextBlock
                    {
                        Text = Loc.Get("chaster_left_soon"), FontFamily = Display, FontSize = 34, FontWeight = FontWeights.Bold,
                        Foreground = (Brush)FindResource("TextLightBrush"), VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, 0, 6),
                    });
                }
                foreach (var part in parts)
                {
                    var number = new TextBlock
                    {
                        Text = part.Value.ToString(), FontFamily = Display, FontSize = 58, FontWeight = FontWeights.Bold,
                        Foreground = (Brush)FindResource("TextLightBrush"), VerticalAlignment = VerticalAlignment.Bottom,
                        RenderTransformOrigin = new Point(0, 1),
                    };
                    var unit = new TextBlock
                    {
                        Text = Loc.Get(UnitKey(part.Key)), FontFamily = Display, FontSize = 22, FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)FindResource("TextMutedBrush"), VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(3, 0, 14, 11),
                    };
                    HeroClock.Children.Add(number);
                    HeroClock.Children.Add(unit);
                    _clockLead ??= (number, part.Value);
                }
                HeroClockRow.Visibility = Visibility.Visible;
            }
            else HeroClockRow.Visibility = Visibility.Collapsed;

            var ends = snapshot is { TimerHidden: false, EndsAtUtc: { } endUtc } ? endUtc.ToLocalTime() : (DateTime?)null;
            TxtHeroEnds.Text = ends is { } when ? Loc.GetF("chaster_hero_ends", when.ToString("ddd d MMM HH:mm")) : "";
            TxtHeroEnds.Visibility = ends == null ? Visibility.Collapsed : Visibility.Visible;

            RefreshPills(lookup, snapshot, chaster!.SafetyHoldRemaining);
        }

        /// <summary>The single-letter units under the big digits: the long keys are the chip's
        /// and the tooltip's, the hero has room for a letter and no more.</summary>
        private static string UnitKey(string longKey) =>
            longKey.Contains("day") ? "chaster_unit_d" : longKey.Contains("hour") ? "chaster_unit_h" : "chaster_unit_m";

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

        // ============================== 2. today, and this run ==============================

        internal void RefreshDay(bool animate = true)
        {
            var chaster = App.Chaster;
            if (chaster == null) return;
            var today = chaster.TodayAddedSeconds;
            TxtToday.Text = CircesTab.Format(today, signed: false);
            TxtTodayCap.Text = "/ " + CircesTab.Format(CircesTab.DailyCapSeconds, signed: false);
            _capFraction = TabPageText.CapFraction(today);
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
            var open = ReceiptHost.Visibility != Visibility.Visible;
            if (open) BuildBill();
            ReceiptHost.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            TxtRunChevron.RenderTransform = new RotateTransform(open ? 180 : 0);
            FxPop(StatRun, 1.04);
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

        private async void BtnUnlink_Click(object sender, RoutedEventArgs e)
        {
            try { if (App.Chaster is { } chaster) await chaster.UnlinkAsync(); }
            catch (Exception ex) { Diag.Swallowed(ex, "chaster unlink from the page"); }
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
                // Chaster is away, or the link just died (LinkChanged repaints for that). One lock:
                // the hero already names it. None: the pill says so.
                if (locks == null || locks.Count < 2)
                {
                    LockRow.Visibility = Visibility.Collapsed;
                    return;
                }
                LockRow.Visibility = Visibility.Visible;
                FillLockPicker(locks);
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

        // ============================== 3. prices: tiles first, chips behind ==============================

        private void Preset_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as ToggleButton)?.Tag is not string id || sender is not ToggleButton tile) return;
            if (id == TabPresets.Custom)
            {
                // The fourth tile opens and closes the chips; it never rewrites the set.
                ShowCustomize(CustomizeHost.Visibility != Visibility.Visible);
                RefreshPresets();
                FxPreset(tile, CustomColour);
                return;
            }
            if (App.Settings?.Current is not { } settings) return;
            var ids = TabPresets.Apply(id);
            if (ids.Count == 0) return;
            settings.ChasterPrices = new List<string>(ids);
            App.Settings?.Save();
            ApplyPriceToggles();
            RefreshPresets();
            FxPreset(tile, PresetColour(id));
        }

        internal static Color PresetColour(string id) => id switch
        {
            TabPresets.Gentle => EarnColour,
            TabPresets.Strict => CostColour,
            TabPresets.Circe => JackpotColour,
            _ => CustomColour,
        };

        private void ShowCustomize(bool show)
        {
            if (show) ApplyPriceToggles();
            CustomizeHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Light the tile for the set that is on. The fourth tile lights for a hand-built
        /// set, and while the chips are open.</summary>
        internal void RefreshPresets()
        {
            var match = TabPresets.Match(App.Settings?.Current?.ChasterPrices);
            BtnPresetGentle.IsChecked = match == TabPresets.Gentle;
            BtnPresetStrict.IsChecked = match == TabPresets.Strict;
            BtnPresetCirce.IsChecked = match == TabPresets.Circe;
            BtnPresetCustom.IsChecked = match == TabPresets.Custom || CustomizeHost.Visibility == Visibility.Visible;
        }

        /// <summary>Push the saved set onto the chips. Never the other way round: the settings
        /// list is the truth and the chips are a view of it.</summary>
        internal void ApplyPriceToggles()
        {
            BuildPriceRows();
            var on = new HashSet<string>(App.Settings?.Current?.ChasterPrices ?? new List<string>(), StringComparer.Ordinal);
            _loading = true;
            try { foreach (var (id, chip) in _priceToggles) chip.IsChecked = on.Contains(id); }
            finally { _loading = false; }
        }

        internal void BuildPriceRows()
        {
            if (_chipsBuilt) return;
            _chipsBuilt = true;
            var (costs, earnBacks) = TabPageText.Split(TabPrices.All);
            foreach (var price in costs) CostRows.Children.Add(Chip(price, CostColour));
            foreach (var price in earnBacks) EarnRows.Children.Add(Chip(price, EarnColour));
        }

        /// <summary>A chip is a name and a price. The one row that charges for staying away says
        /// how in its tooltip, and nowhere on the page.</summary>
        private ToggleButton Chip(TabPrice price, Color colour)
        {
            var brush = new SolidColorBrush(colour);
            brush.Freeze();
            var name = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextLightBrush");
            name.SetBinding(TextBlock.TextProperty, Bound(TabPageText.NameKey(price.Id)));
            var figure = new TextBlock
            {
                Text = TabPageText.Price(price, Loc.Get("chaster_each")),
                FontFamily = Mono, FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = brush, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(name);
            row.Children.Add(figure);

            var chip = new ToggleButton
            {
                Style = (Style)FindResource("CirceChip"),
                Tag = price.Id,
                Background = brush,
                BorderBrush = brush,
                Content = row,
            };
            if (price.Id == CircesMisses.EventId)
            {
                var tip = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
                tip.SetBinding(TextBlock.TextProperty, Bound("chaster_misses_hint"));
                chip.ToolTip = tip;
            }
            chip.Click += PriceToggle_Changed;
            _priceToggles[price.Id] = chip;
            return chip;
        }

        private static Binding Bound(string key) =>
            new($"[{key}]") { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay };

        private void PriceToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || App.Settings?.Current is not { } settings) return;
            if (sender is not ToggleButton chip || chip.Tag is not string id) return;
            var on = chip.IsChecked == true;
            // A new list every time: the service reads the setting fresh on each event, maybe
            // from another thread, and must never see a list that is being edited.
            var next = new List<string>(settings.ChasterPrices ?? new List<string>());
            next.Remove(id);
            if (on) next.Add(id);
            settings.ChasterPrices = next;
            App.Settings?.Save();
            // One hand-flipped chip can land exactly on a preset, or step off one. Say which.
            RefreshPresets();
            FxChip(chip, on, ((SolidColorBrush)chip.BorderBrush).Color);
        }
    }
}
