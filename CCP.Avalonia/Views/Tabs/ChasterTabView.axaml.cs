// PORTED (first read-only slice) from ConditioningControlPanel/Views/Tabs/ChasterTabView.xaml.cs
// (1,379 lines) and ChasterTabView.Mood.cs. Real: OnTabShown / Refresh / RefreshHero (:119-260),
// the live hero clock (:301), the ends line (:277), the pills (:372), the account chip (:871),
// the link flow (:830-863) on the Core loopback OAuth, the lock pick (:962-1053) and the fact cap.
// ponytail: Circe's mood is a line (WPF chaster_mood_peek wording), not the CircesMoodMeter heat
// row, and CirceSays lines are not shown. The ground (spiral/glow/ambient), hero art, paper tag,
// calendar, LockTitle letters, the numbers, the receipt, limits, menu,
// presets, ladder, trailer and all Fx (ChasterTabView.Fx.cs: FxSwitch/FxConsentShown/FxConsentOk
// bursts included) are later slices. Unlink, the switch + consent and pause are real (slice 2).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class ChasterTabView : UserControl
    {
        private static readonly Color EarnColour = Color.FromRgb(0x5F, 0xFF, 0xD0);
        private static readonly Color IceColour = Color.FromRgb(0x9F, 0xD8, 0xFF);
        private static readonly Color AmberColour = Color.FromRgb(0xFF, 0xC9, 0x8A);
        private static readonly Color MutedColour = Color.FromRgb(0xA8, 0xA2, 0xB8);
        private static readonly Color CostColour = Color.FromRgb(0xFF, 0x6B, 0x8A);
        private static readonly Color PauseGold = Color.FromRgb(0xE0, 0xB0, 0x52);
        private static readonly FontFamily Display = new("Fredoka, Segoe UI");

        private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
        private ChasterService? _subscribed;
        private string _clockShape = "";
        private readonly List<TextBlock> _clockNumbers = new();
        private bool _loading;

        public ChasterTabView()
        {
            InitializeComponent();
            _tick.Tick += (_, _) => { PaintHeroClock(); PaintChasterChip(ChasterHead.Service); };
            AttachedToVisualTree += (_, _) => Subscribe(true);
            DetachedFromVisualTree += (_, _) => Subscribe(false);
            Refresh();
        }

        /// <summary>ShowTab calls this on every visit. Cheap parts now; the lock list and the lock
        /// are network calls and fill in when they land.</summary>
        public void OnTabShown()
        {
            Refresh();
            _ = LoadLocksAsync();
            _ = ChasterHead.Service?.RefreshLockAsync();
        }

        private void Subscribe(bool on)
        {
            if (on && _subscribed == null && ChasterHead.Service is { } c)
            {
                _subscribed = c;
                c.LinkChanged += OnLinkChanged;
                c.LockChanged += OnLockChanged;
                c.Booked += OnBooked;
                _tick.Start();
            }
            else if (!on && _subscribed is { } s)
            {
                s.LinkChanged -= OnLinkChanged;
                s.LockChanged -= OnLockChanged;
                s.Booked -= OnBooked;
                _subscribed = null;
                _tick.Stop();
            }
        }

        // All three arrive on whatever thread found out.
        private void OnLinkChanged() => Dispatcher.UIThread.Post(OnTabShown);
        private void OnLockChanged() => Dispatcher.UIThread.Post(RefreshHero);
        private void OnBooked(string eventId, TabBooking booking) => Dispatcher.UIThread.Post(RefreshHero);

        internal void Refresh()
        {
            var chaster = ChasterHead.Service;
            var linked = chaster?.IsLinked == true;
            UnlinkedPanel.IsVisible = !linked;
            FactRow.IsVisible = !linked;
            AccountStrip.IsVisible = linked;
            SwitchPill.IsVisible = linked;
            PausePill.IsVisible = linked;
            PaintPause(CoreSettings.Current.ChasterPaused);
            BtnLink.IsEnabled = chaster != null;
            ShowLinking(chaster?.IsLinking == true);
            TxtFactCap1.Text = TxtFactCap2.Text = CircesTab.Format((chaster?.Caps ?? TabLimits.Default).DailySeconds, signed: false);
            if (!linked) LockRow.IsVisible = false;
            RefreshHero();
            ConsentCard.IsVisible = false;
            if (!linked) return;
            var on = CoreSettings.Current.ChasterTabEnabled;
            _loading = true;
            try { ChkTab.IsChecked = on; }
            finally { _loading = false; }
            PaintSwitch(on);
        }

        // ---- the switch, and the one consent (WPF :910-958) ----

        private void PaintSwitch(bool on)
        {
            TxtSwitchState.Text = Loc.Get(on ? "chaster_switch_on" : "chaster_switch_off");
            TxtSwitchState.Foreground = on ? new SolidColorBrush(CostColour) : Brush("TextMutedBrush");
            SwitchPill.BorderBrush = on ? new SolidColorBrush(CostColour) : Brush("GlassBorderBrush");
        }

        private void ChkTab_Changed(object? sender, RoutedEventArgs e)
        {
            if (_loading) return;
            var wanted = ChkTab.IsChecked == true;
            PaintSwitch(wanted);
            var settings = CoreSettings.Current;
            // The first time anyone switches this on, the four facts come first. Inline, not a modal.
            if (wanted && !settings.ChasterConsentSeen)
            {
                _loading = true;
                try { ChkTab.IsChecked = false; }
                finally { _loading = false; }
                ConsentCard.IsVisible = true;
                return;
            }
            ConsentCard.IsVisible = false;
            settings.ChasterTabEnabled = wanted;
            CoreSettings.Save();
            RefreshHero();
        }

        private void BtnConsentOk_Click(object? sender, RoutedEventArgs e)
        {
            var settings = CoreSettings.Current;
            settings.ChasterConsentSeen = true;
            settings.ChasterTabEnabled = true;
            CoreSettings.Save();
            _loading = true;
            try { ChkTab.IsChecked = true; }
            finally { _loading = false; }
            PaintSwitch(true);
            RefreshHero();
            ConsentCard.IsVisible = false;
        }

        // ---- pause (WPF :1053-1072) ----

        private void PaintPause(bool paused)
        {
            TxtPause.Text = Loc.Get(paused ? "chaster_paused" : "chaster_pause");
            PauseBarA.IsVisible = PauseBarB.IsVisible = !paused;
            PlayArrow.IsVisible = paused;
            TxtPause.Foreground = paused ? new SolidColorBrush(PauseGold) : Brush("TextLightBrush");
            PausePill.BorderBrush = paused ? new SolidColorBrush(PauseGold) : Brush("GlassBorderBrush");
            ToolTip.SetTip(PausePill, Loc.Get(paused ? "chaster_paused_tip" : "chaster_pause_tip"));
        }

        private void BtnPause_Click(object? sender, RoutedEventArgs e)
        {
            var settings = CoreSettings.Current;
            settings.ChasterPaused = !settings.ChasterPaused;
            CoreSettings.Save();
            PaintPause(settings.ChasterPaused);
            ChasterHead.Service?.NoteChoiceChanged();
            RefreshHero();
        }

        // ---- unlink (WPF :865, ConfirmAndUnlinkAsync :896) ----

        private async void BtnUnlink_Click(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is Window owner) await ConfirmAndUnlinkAsync(owner);
        }

        /// <summary>One way out, asked once. The service revokes the grant and clears the stored
        /// token (SecretChasterTokenStore); LinkChanged repaints the page.</summary>
        internal static async Task<bool> ConfirmAndUnlinkAsync(Window owner, Func<Window, string, string, Task<bool>>? ask = null)
        {
            var chaster = ChasterHead.Service;
            if (chaster == null || !chaster.IsLinked) return false;
            ask ??= (o, title, body) => Dialogs.MessageDialog.ConfirmAsync(o, title, body);
            if (!await ask(owner, Loc.Get("chaster_unlink_confirm_title"), Loc.Get("chaster_unlink_confirm_body"))) return false;
            try { await chaster.UnlinkAsync(); return true; }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] unlink"); return false; }
        }

        internal void RefreshHero()
        {
            var chaster = ChasterHead.Service;
            var snapshot = chaster?.Lock;
            var linked = chaster?.IsLinked == true;
            PaintChasterChip(chaster);
            RefreshMood(chaster);
            HeroTitle.IsVisible = linked;
            HeroPills.Children.Clear();
            if (!linked)
            {
                SetupHint.IsVisible = false;
                HeroClockRow.IsVisible = false;
                TxtHeroEnds.IsVisible = false;
                HeroPills.IsVisible = false;
                return;
            }
            var key = TabPageText.SetupHint(true, chaster!.LockLookup, snapshot != null, CoreSettings.Current.ChasterTabEnabled);
            SetupHint.Text = key == null ? "" : Loc.Get(key);
            SetupHint.IsVisible = key != null;
            PaintHeroClock();
            var ends = LiveLockClock.EndsAt(snapshot, chaster.BalanceSeconds, DateTime.UtcNow)?.ToLocalTime();
            TxtHeroEnds.Text = ends is { } when ? Loc.GetF("chaster_hero_ends", when.ToString("ddd d MMM HH:mm")) : "";
            TxtHeroEnds.IsVisible = ends != null;
            RefreshPills(chaster.LockLookup, snapshot, chaster.SafetyHoldRemaining);
        }

        /// <summary>WPF PaintHeroClock (:301), without the count-up on show: d h m s off the last snapshot.</summary>
        internal void PaintHeroClock()
        {
            var chaster = ChasterHead.Service;
            var snapshot = chaster?.IsLinked == true ? chaster.Lock : null;
            if (LiveLockClock.Remaining(snapshot, chaster?.BalanceSeconds ?? 0, DateTime.UtcNow) is not { } remaining)
            {
                HeroClock.Children.Clear();
                _clockNumbers.Clear();
                _clockShape = "";
                HeroClockRow.IsVisible = false;
                return;
            }
            HeroClockRow.IsVisible = true;
            if (remaining <= TimeSpan.Zero)
            {
                if (_clockShape == "ready") return;
                HeroClock.Children.Clear();
                _clockNumbers.Clear();
                _clockShape = "ready";
                HeroClock.Children.Add(new TextBlock
                {
                    Text = Loc.Get("chaster_clock_ready"), FontFamily = Display, FontSize = 44, FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(EarnColour), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4),
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
                foreach (var part in parts)
                {
                    var number = new TextBlock
                    {
                        Text = part.Value, FontFamily = Display, FontSize = 62, FontWeight = FontWeight.Bold,
                        Foreground = Brush("TextLightBrush"), VerticalAlignment = VerticalAlignment.Bottom,
                    };
                    HeroClock.Children.Add(number);
                    HeroClock.Children.Add(new TextBlock
                    {
                        Text = Loc.Get("chaster_unit_" + part.Unit), FontFamily = Display, FontSize = 22, FontWeight = FontWeight.SemiBold,
                        Foreground = Brush("TextMutedBrush"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(3, 0, 14, 11),
                    });
                    _clockNumbers.Add(number);
                }
                return;
            }
            for (var i = 0; i < parts.Count && i < _clockNumbers.Count; i++)
                if (_clockNumbers[i].Text != parts[i].Value) _clockNumbers[i].Text = parts[i].Value;
        }

        private IBrush? Brush(string key) =>
            this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.White;

        internal string HeroClockText => string.Concat(HeroClock.Children.OfType<TextBlock>().Select(t => t.Text));

        private void RefreshPills(LockLookup lookup, LockSnapshot? snapshot, TimeSpan hold)
        {
            var state = TabPageText.HeroState(lookup, snapshot);
            switch (state)
            {
                case "chaster_state_frozen": HeroPills.Children.Add(Pill(Loc.Get("chaster_pill_frozen"), IceColour)); break;
                case "chaster_state_hidden": HeroPills.Children.Add(Pill(Loc.Get("chaster_pill_hidden"), MutedColour)); break;
                case "chaster_state_no_lock": HeroPills.Children.Add(Pill(Loc.Get("chaster_pill_nolock"), AmberColour, "chaster_state_no_lock")); break;
                case "chaster_state_pick": HeroPills.Children.Add(Pill(Loc.Get("chaster_pill_pick"), AmberColour, "chaster_state_pick")); break;
                case "chaster_state_away": HeroPills.Children.Add(Pill(Loc.Get("chaster_pill_away"), AmberColour, "chaster_state_away")); break;
            }
            if (snapshot?.IsTestLock == true || state == "chaster_state_test")
                HeroPills.Children.Add(Pill(Loc.Get("chaster_pill_test"), MutedColour));
            if (snapshot != null)
                HeroPills.Children.Insert(0, Pill(string.IsNullOrWhiteSpace(snapshot.Title) ? Loc.Get("chaster_lock_untitled") : snapshot.Title!, MutedColour));
            if (hold > TimeSpan.Zero)
                HeroPills.Children.Add(Pill(Loc.Get("chaster_stat_hold") + " " + $"{(int)hold.TotalMinutes}:{hold.Seconds:00}", MutedColour, "chaster_hold"));
            HeroPills.IsVisible = HeroPills.Children.Count > 0;
        }

        private static Border Pill(string text, Color colour, string? tipKey = null)
        {
            var pill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x26, colour.R, colour.G, colour.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, colour.R, colour.G, colour.B)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11),
                Padding = new Thickness(10, 3), Margin = new Thickness(0, 0, 8, 6),
                Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(colour) },
            };
            if (tipKey != null) ToolTip.SetTip(pill, Loc.Get(tipKey));
            return pill;
        }

        internal IEnumerable<string> PillTexts => HeroPills.Children.OfType<Border>().Select(b => (b.Child as TextBlock)?.Text ?? "");

        /// <summary>Circe's mood (null with the heat row off, as WPF hides the meter).</summary>
        private void RefreshMood(ChasterService? chaster)
        {
            var mood = chaster?.Mood;
            TxtMood.IsVisible = mood != null;
            if (mood is not { } m) return;
            TxtMood.Text = Loc.GetF("chaster_mood_peek", Loc.Get(m.WordKey), m.FactorText);
            TxtMood.Foreground = new SolidColorBrush(ChasterRailChip.MoodColour(m.Level));
        }


        private void PaintChasterChip(ChasterService? chaster)
        {
            var snapshot = chaster?.Lock;
            TxtAccountLock.Text = snapshot == null ? Loc.Get("chaster_account_nolock")
                : string.IsNullOrWhiteSpace(snapshot.Title) ? Loc.Get("chaster_lock_untitled") : snapshot.Title!;
            var left = chaster?.IsLinked == true ? snapshot?.Remaining(DateTime.UtcNow) : null;
            TxtAccountLeft.Text = left is { } l ? (snapshot!.IsFrozen ? "❄ " : "") + ChasterWebLinks.Short(l) : "";
            TxtAccountLeft.IsVisible = left is not null;
            ToolTip.SetTip(BtnChasterSite, Loc.Get(snapshot == null ? "chaster_site_make_tip" : "chaster_site_open_tip"));
        }

        private void BtnChasterSite_Click(object? sender, RoutedEventArgs e)
        {
            var chaster = ChasterHead.Service;
            // A sandbox never opens the real chaster.app: the same rule as the consent page.
            if (ChasterHead.BrowserUrl(ChasterWebLinks.For(chaster?.IsLinked == true ? chaster.Lock : null)) is { } url) _ = OpenAsync(url);
        }

        /// <summary>WPF BrowserLauncher.OpenUrlOrPrompt: on failure the link goes to the clipboard.</summary>
        private async Task OpenAsync(string url)
        {
            var top = TopLevel.GetTopLevel(this);
            try { if (top?.Launcher is { } l && await l.LaunchUriAsync(new Uri(url))) return; }
            catch (Exception ex) { Serilog.Log.Warning(ex, "[Chaster] could not open the browser"); }
            try { if (top?.Clipboard is { } c) await c.SetTextAsync(url); } catch { }
        }

        // ---- link ----

        private void ShowLinking(bool linking)
        {
            BtnLink.IsVisible = !linking;
            LinkingRow.IsVisible = linking;
        }

        private async void BtnLink_Click(object? sender, RoutedEventArgs e)
        {
            var chaster = ChasterHead.Service;
            if (chaster == null || chaster.IsLinking) return;
            TxtLinkNote.IsVisible = false;
            ShowLinking(true);
            try
            {
                var outcome = await chaster.LinkAsync(url =>
                {
                    // A sandbox never opens the real consent page: only its loopback stand-in.
                    if (ChasterHead.BrowserUrl(url) is { } open) Dispatcher.UIThread.Post(() => _ = OpenAsync(open));
                    else Serilog.Log.Information("[Chaster] sandboxed profile without a loopback {Var}: consent page not opened", ChasterHead.EnvVar);
                });
                var note = outcome switch
                {
                    LinkOutcome.Denied => "chaster_link_denied",
                    LinkOutcome.TimedOut => "chaster_link_timeout",
                    LinkOutcome.Failed => "chaster_link_failed",
                    _ => null,
                };
                if (note != null)
                {
                    TxtLinkNote.Text = Loc.Get(note);
                    TxtLinkNote.IsVisible = true;
                }
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] link from the page"); }
            finally { ShowLinking(false); }
        }

        private void BtnCancelLink_Click(object? sender, RoutedEventArgs e) => ChasterHead.Service?.CancelLink();

        // ---- the pick: nothing is pushed to a lock the player did not pick ----

        internal async Task LoadLocksAsync()
        {
            var chaster = ChasterHead.Service;
            if (chaster?.IsLinked != true) return;
            try
            {
                var locks = await chaster.GetLocksAsync();
                if (ChasterHead.Service != chaster || !chaster.IsLinked) return;
                var chosen = CoreSettings.Current.ChasterLockId;
                if (locks == null || locks.Count == 0 || (locks.Count == 1 && locks[0].Id == chosen))
                {
                    LockRow.IsVisible = false;
                    return;
                }
                LockRow.IsVisible = true;
                var oneTap = locks.Count == 1;
                CmbLock.IsVisible = !oneTap;
                BtnUseLock.IsVisible = oneTap;
                if (oneTap)
                {
                    TxtUseLock.Text = Loc.GetF("chaster_lock_use", TitleOf(locks[0]));
                    BtnUseLock.Tag = locks[0].Id;
                    return;
                }
                _loading = true;
                try
                {
                    CmbLock.Items.Clear();
                    if (locks.All(l => l.Id != chosen))
                        CmbLock.Items.Add(new ComboBoxItem { Content = Loc.Get("chaster_lock_pick"), IsSelected = true });
                    foreach (var l in locks)
                        CmbLock.Items.Add(new ComboBoxItem { Content = TitleOf(l), Tag = l.Id, IsSelected = l.Id == chosen });
                }
                finally { _loading = false; }
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] lock list for the page"); }
        }

        private static string TitleOf(ChasterLock l) => string.IsNullOrWhiteSpace(l.Title) ? Loc.Get("chaster_lock_untitled") : l.Title!;

        private void CmbLock_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_loading && (CmbLock.SelectedItem as ComboBoxItem)?.Tag is string id) PickLock(id);
        }

        private void BtnUseLock_Click(object? sender, RoutedEventArgs e)
        {
            if (BtnUseLock.Tag is string id) PickLock(id);
        }

        internal void PickLock(string id)
        {
            CoreSettings.Current.ChasterLockId = id;
            CoreSettings.Save();
            BtnUseLock.IsVisible = false;
            _ = RepickAsync();
        }

        private async Task RepickAsync()
        {
            var chaster = ChasterHead.Service;
            if (chaster == null) return;
            try
            {
                await chaster.RefreshLockAsync();
                chaster.NoteChoiceChanged();
                await Dispatcher.UIThread.InvokeAsync(() => { _ = LoadLocksAsync(); RefreshHero(); });
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] lock pick"); }
        }
    }
}
