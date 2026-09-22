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
    /// <para>This partial is the DATA: what each figure, pill, link and row says. The motion (the
    /// bursts, rings, sheen, the cards arriving, the chain's comet, the trailer's drift) is
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
        private static readonly Brush ChainBrush = Frozen(Color.FromRgb(0x6E, 0x66, 0x86));
        private static readonly FontFamily Display = new("/Fonts/#Fredoka, Segoe UI");
        private static readonly FontFamily Mono = new("Consolas, Courier New");

        /// <summary>Half a minute is as fine as the hero needs; a running safety hold is counted
        /// in seconds because it is the one number the player is sitting there waiting out.</summary>
        private static readonly TimeSpan SlowTick = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan HoldTick = TimeSpan.FromSeconds(1);

        /// <summary>The chain draws one link a day up to this many, then the links shrink so a
        /// long lock still fits on one line.</summary>
        internal const int ChainWideLinks = 18;
        internal const int ChainMaxLinks = 40;

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
        private readonly DispatcherTimer _trailerOpen;
        private readonly DispatcherTimer _trailerClose;
        private ToggleButton? _trailerRow;
        private bool _trailerShown;
        private bool _overTrailer;
        private int _chainLinks;

        /// <summary>The first number of the countdown and its value, for the count-up on show.</summary>
        private (TextBlock Block, int Value)? _clockLead;

        public ChasterTabView()
        {
            InitializeComponent();
            _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = SlowTick };
            _tick.Tick += (_, _) => { RefreshHero(); RefreshDay(animate: false); };
            _trailerOpen = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TrailerOpenDelay };
            _trailerOpen.Tick += (_, _) => { _trailerOpen.Stop(); OpenTrailer(); };
            _trailerClose = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TrailerCloseGrace };
            _trailerClose.Tick += (_, _) => { _trailerClose.Stop(); if (!_overTrailer) HideTrailer(); };
            // Only listen while the page is on screen: Booked fires on every priced event.
            IsVisibleChanged += (_, _) => { Subscribe(IsVisible); if (!IsVisible) HideTrailer(); };
            Chain.SizeChanged += (_, _) => PlaceChainTag();
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
            PaperTag.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
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
            RefreshDay(animate);
            RefreshRun();
            if (ReceiptHost.Visibility == Visibility.Visible) BuildBill();
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
                BuildChain(null);
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
            BuildChain(left);
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

        /// <summary>The paper tag: the tab's amount in ink, when it lands, and a stamp that says
        /// UNPAID while there is something to pay, CLEAR at zero, CREDIT below it.</summary>
        internal void RefreshTag(int balance)
        {
            TxtTagAmount.Text = balance == 0 ? CircesTab.Format(0, signed: false) : CircesTab.Format(balance);
            TxtTagAmount.Foreground = balance > 0 ? Frozen(Color.FromRgb(0xC8, 0x24, 0x4A))
                : balance < 0 ? Frozen(Color.FromRgb(0x1E, 0x8A, 0x6E)) : Frozen(Color.FromRgb(0x24, 0x1A, 0x2E));
            TxtTagLands.Text = balance > 0 ? Loc.GetF("chaster_tag_lands", "00:00") : Loc.Get("chaster_tag_credit_lands");
            TxtTagStamp.Text = Loc.Get(balance > 0 ? "chaster_tag_unpaid" : balance < 0 ? "chaster_tag_credit" : "chaster_tag_clear");
            var stampColour = balance > 0 ? Color.FromRgb(0xC8, 0x24, 0x4A) : balance < 0 ? Color.FromRgb(0x1E, 0x8A, 0x6E) : Color.FromRgb(0x6E, 0x66, 0x86);
            TagStamp.BorderBrush = Frozen(stampColour);
            TxtTagStamp.Foreground = Frozen(stampColour);
            TxtChainTag.Text = CircesTab.Format(balance);
            ChainTag.Visibility = balance > 0 && _chainLinks > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ============================== 2. the chain ==============================

        /// <summary>How many links the chain draws for a lock with this long left: one a day,
        /// starting with tonight, plus the one that opens. No lock, no chain.</summary>
        internal static int ChainLinksFor(TimeSpan? remaining)
        {
            if (remaining is not { } left || left <= TimeSpan.Zero) return 0;
            var days = (int)Math.Ceiling(left.TotalDays);
            return Math.Clamp(days, 1, ChainMaxLinks - 1) + 1;
        }

        /// <summary>One link a day until the lock opens. Tonight's link is red and carries the
        /// tab's tag; the last is dashed gold, the one that opens. Past a couple of weeks the links
        /// shrink so a long lock still reads as one chain.</summary>
        internal void BuildChain(TimeSpan? remaining)
        {
            var links = ChainLinksFor(remaining);
            _chainLinks = links;
            Chain.Children.Clear();
            FxChainReset();
            if (links == 0)
            {
                ChainRow.Visibility = Visibility.Collapsed;
                ChainTag.Visibility = Visibility.Collapsed;
                return;
            }
            ChainRow.Visibility = Visibility.Visible;
            var wide = links <= ChainWideLinks;
            double w = wide ? 34 : 20, h = wide ? 18 : 12, overlap = wide ? -6 : -4;
            for (var i = 0; i < links; i++)
            {
                var last = i == links - 1;
                var tonight = i == 0;
                FrameworkElement link;
                if (last)
                {
                    link = new Rectangle
                    {
                        Width = w, Height = h, RadiusX = h / 2, RadiusY = h / 2,
                        Stroke = JackpotBrush, StrokeThickness = 2.2, StrokeDashArray = new DoubleCollection { 3, 2 },
                        ToolTip = Loc.Get("chaster_chain_open"),
                    };
                }
                else
                {
                    link = new Border
                    {
                        Width = w, Height = h, CornerRadius = new CornerRadius(h / 2),
                        BorderThickness = new Thickness(tonight ? 3 : 2.2),
                        BorderBrush = tonight ? CostBrush : ChainBrush,
                        Background = tonight ? new SolidColorBrush(Color.FromArgb(0x33, CostColour.R, CostColour.G, CostColour.B)) : null,
                        ToolTip = tonight ? Loc.Get("chaster_chain_tonight") : Loc.GetF("chaster_chain_day", i + 1),
                    };
                }
                // every other link sits a touch lower, so the row reads as links woven, not beads
                link.Margin = new Thickness(i == 0 ? 0 : overlap, (i & 1) == 1 ? h * 0.35 : 0, 0, 0);
                link.VerticalAlignment = VerticalAlignment.Top;
                Chain.Children.Add(link);
                if (tonight) FxChainTonight(link);
            }
            RefreshTag(App.Chaster?.BalanceSeconds ?? 0);
            PlaceChainTag();
        }

        /// <summary>Park the tag under the first link, wherever the centred chain put it.</summary>
        private void PlaceChainTag()
        {
            try
            {
                if (Chain.Children.Count == 0 || Chain.Children[0] is not FrameworkElement first || !first.IsVisible) return;
                var bounds = first.TransformToVisual(ChainRow).TransformBounds(new Rect(0, 0, first.ActualWidth, first.ActualHeight));
                var x = bounds.X + bounds.Width / 2 - ChainTag.ActualWidth / 2;
                ChainTag.Margin = new Thickness(Math.Max(0, x), bounds.Bottom + 6, 0, 0);
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        // ============================== 3. today, and this run ==============================

        internal void RefreshDay(bool animate = true)
        {
            var chaster = App.Chaster;
            if (chaster == null) return;
            var today = chaster.TodayAddedSeconds;
            var cap = CircesTab.Format(CircesTab.DailyCapSeconds, signed: false);
            TxtToday.Text = CircesTab.Format(today, signed: false);
            TxtTodayCap.Text = "/ " + cap;
            TxtTodaySub.Text = Loc.GetF("chaster_stat_today_sub", cap);
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
            if (ReferenceEquals(sender, PaperTag)) FxPop(PaperTag, 1.05);
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
            App.Settings?.Save();
            ApplyPriceToggles();
            RefreshPresets();
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
