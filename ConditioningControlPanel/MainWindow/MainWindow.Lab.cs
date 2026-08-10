using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using NAudio.Wave;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Lab tab: AI lab session controls and state.
    public partial class MainWindow
    {
        #region Lab

        private void InitializeLockdown()
        {
            if (App.Lockdown == null) return;

            App.Lockdown.LockdownActivated += OnLockdownActivated;
            App.Lockdown.LockdownDeactivated += OnLockdownDeactivated;
            App.Lockdown.CountdownTick += OnLockdownTick;
        }

        internal void BtnActivateLockdown_Click(object sender, RoutedEventArgs e)
        {
            if (App.Lockdown == null) return;

            // Hard gate. RefreshPremiumGate only collapses a Border over this card, so the button
            // stays reachable by keyboard focus and by automation - and this handler is the one
            // that takes the keys away for an hour.
            if (!TierGate.DemandPremium("Lockdown Mode")) return;

            // Get duration from combo box
            var selectedItem = LockdownTab.CmbLockdownDuration.SelectedItem as ComboBoxItem;
            if (selectedItem?.Tag is not string minutesStr || !int.TryParse(minutesStr, out var minutes))
                return;

            var duration = TimeSpan.FromMinutes(minutes);

            // Show double warning with clear consequences
            var confirmed = WarningDialog.ShowDoubleWarning(this, "Lockdown Mode",
                "- You will be LOCKED IN for " + minutes + " minutes\n" +
                "- Strict Lock will be FORCED ON\n" +
                "- Panic Key will be DISABLED\n" +
                "- Alt+F4, Alt+Tab, Windows key, and Escape will be BLOCKED\n" +
                "- You CANNOT close or minimize the application\n" +
                "- The only escape is waiting for the timer to expire\n" +
                "  (or Ctrl+Alt+Del → Task Manager as a safety valve)");

            if (!confirmed) return;

            App.Lockdown.Activate(duration);
        }

        internal void BtnStartQuiz_Click(object sender, RoutedEventArgs e)
        {
            // Prevent opening multiple quiz windows — focus existing one instead
            var existingQuiz = Application.Current.Windows.OfType<QuizWindow>().FirstOrDefault();
            if (existingQuiz != null)
            {
                existingQuiz.Activate();
                existingQuiz.Focus();
                return;
            }

            if (App.Ai == null || !App.Ai.IsAvailable)
            {
                MessageBox.Show(Loc.Get("msg_you_need_to_be_logged_in_to_use_the_ai_quiz"), "Login Required",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var fullscreen = GradedIntakeTab.ChkQuizFullscreen?.IsChecked == true;
            var playDrone = GradedIntakeTab.ChkQuizDrone?.IsChecked == true;
            var quizWindow = new QuizWindow(fullscreen, playDrone);
            quizWindow.Closed += (s, args) => RefreshPastQuizzes();
            quizWindow.Show();
        }

        /// <summary>
        /// Exclusives → "Graded Intake" web-core rework. Hosts the decoupled intake page
        /// (Resources/web/intake) in a WebView2 window via <see cref="Services.Quiz.IntakeHostService"/>,
        /// which drafts a themed CCP session from the run's QuizRunResult. Gated the same way as the
        /// classic AI quiz above (App.Ai.IsAvailable = cloud identity or Patreon AI access), since the
        /// intake's server AI accent uses the same Patreon-bearer gate.
        /// </summary>
        internal void BtnStartIntake_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Already open? Just focus it — never re-launch a live run.
                if (Services.Quiz.IntakeHostService.IsActive)
                {
                    Services.Quiz.IntakeHostService.Launch();
                    return;
                }

                // Tier-1 gate, now pass-aware. Patrons are unchanged; a free account gets one
                // run a week (IntakePassService). GradedIntakeGate already paints the matching
                // state over the launch zone - this is the belt-and-braces check behind it,
                // matching how the other Exclusives short-circuit their own entry points.
                var pass = App.IntakePass;
                if (pass == null || !pass.CanStartIntake)
                {
                    if (pass?.State == Services.IntakePassState.NeedsLogin)
                    {
                        MessageBox.Show(Loc.Get("msg_you_need_to_be_logged_in_to_use_the_ai_quiz"), "Login Required",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        // Free and already ran this week - the upsell is the honest answer.
                        ShowAppInfoPopup();
                    }
                    return;
                }

                if (App.Ai == null || !App.Ai.IsAvailable)
                {
                    MessageBox.Show(Loc.Get("msg_you_need_to_be_logged_in_to_use_the_ai_quiz"), "Login Required",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // First-ever run: don't duck the control panel. The intake normally minimizes
                // MainWindow to get it out of the way, which is right for a returning user and
                // reads as "the app just crashed" for someone opening it for the first time.
                var firstEver = App.IntakePunchCard?.HasEverCompletedIntake == false;
                Services.Quiz.IntakeHostService.Launch(duckMainWindow: !firstEver);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "BtnStartIntake_Click failed");
                MessageBox.Show("Couldn't start Graded Intake:\n\n" + ex.Message, "Graded Intake",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Lab → "Beta Inspection Bureau" labeling game. The page is served live from
        /// cclabs.app/bureau and hosted in WebView2 via <see cref="Services.Bureau.BureauHostService"/>;
        /// the host supplies auth, server proxying and local frame decoding. Requires a logged-in
        /// account (UnifiedId + auth token) — the page itself shows the clearance gate if missing.
        /// </summary>
        internal void BtnStartBureau_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Services.Bureau.BureauHostService.Launch();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "BtnStartBureau_Click failed");
                MessageBox.Show("Couldn't open the Inspection Bureau:\n\n" + ex.Message,
                    Services.Bureau.BureauHostService.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Lab → "Goon Game" card. Opens the 1v1 duel client (Resources/web/goon) in a WebView2
        /// window via <see cref="Services.GoonGame.GoonHostService"/>, which supplies identity, the
        /// server bridge and the asset manifest. No entitlement check here on purpose: the card is
        /// an unconditional door, and the lobby/server do the gating (the transfer-your-own-media
        /// half is the only premium part, and GoonHostService advertises that capability itself).
        /// Launch() is idempotent — a live duel is re-focused rather than relaunched — so there is
        /// no IsActive guard to duplicate.
        /// </summary>
        internal void BtnStartGoon_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Services.GoonGame.GoonHostService.Launch();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "BtnStartGoon_Click failed");
                MessageBox.Show("Couldn't open the Goon Game:\n\n" + ex.Message,
                    Services.GoonGame.GoonHostService.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Lab → Chaos Mode hero card. Opens the setup/lobby window where the user
        /// configures the run; BEGIN CHAOS there persists settings and launches via
        /// <see cref="App.Chaos"/> (which owns the countdown, HUD and loop).
        /// Modeless on purpose: ShowDialog would disable every other app window,
        /// including the loadout sidebar that opens beside the Warren.
        /// </summary>
        internal void BtnStartChaos_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Tier 2 door, checked here rather than left to the Lab smokescreen: the overlay
                // covers one tab, and the descent is reachable from the hero card, Quick Start and
                // (Phase 6) a Play card that will not have an overlay at all.
                if (!TierGate.DemandLab("Down the Rabbit Hole")) return;

                // DtRH browser game (default ON since M6): the whole experience lives in the web
                // page — hub, run and all. The legacy WPF path below stays for the Lab toggle and
                // as the automatic fallback when the page reported a WebGL boot-error this session.
                bool webPath = App.Settings?.Current?.ChaosWebGameEnabled == true
                               && !Services.Chaos.DtrhHostService.BootFailedThisSession;

                // Already down the hole? Just focus it — never re-pick a save mid-session.
                if (webPath && Services.Chaos.DtrhHostService.IsActive)
                {
                    Services.Chaos.DtrhHostService.Launch();
                    return;
                }
                if (!webPath && (App.Chaos == null || App.Chaos.IsRunning)) return;

                // Save slots: choose which of the three local saves to descend into, right before
                // the hole opens. Cancel backs out; the pick becomes the live slot for this session.
                var slot = ChaosSlotPickerWindow.Pick(this);
                if (slot == null) return;
                Services.Chaos.ChaosMeta.SwitchSlot(slot.Value);

                if (webPath)
                {
                    Services.Chaos.DtrhHostService.Launch();
                    return;
                }
                // Happy path run 1: the Dollhouse stays shut until the first descent is done.
                // FALL IN drops straight into the scripted naked run instead.
                if (Services.Chaos.ChaosMeta.State.RunsCompleted == 0)
                {
                    App.Chaos!.StartRun(Services.Chaos.ChaosHappyPath.BuildFirstRunConfig());
                    return;
                }
                if (ChaosHubWindow.Current != null) { ChaosHubWindow.Current.Activate(); return; }
                var hub = new ChaosHubWindow { Owner = this };
                hub.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "BtnStartChaos_Click failed");
                MessageBox.Show("Couldn't start Down the Rabbit Hole:\n\n" + ex.Message, "Down the Rabbit Hole",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Exclusives → "For You" spotlight. Opens the TikTok-style feed window (WebView2).
        /// Reached through ShowTab("fyp"), which intercepts the key rather than switching tabs.
        /// The card itself never blocks, so premium is enforced here.
        /// </summary>
        internal void OpenFypFeed()
        {
            try
            {
                if (App.Patreon?.HasPremiumAccess != true)
                {
                    ShowAppInfoPopup();
                    return;
                }
                Services.Fyp.FypHostService.Launch();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "OpenFypFeed failed");
                MessageBox.Show("Couldn't open the For You feed:\n\n" + ex.Message, "For You",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Quick Start: launch a Chaos run with the saved settings, bypassing the modal hub.
        /// Mirrors what BEGIN CHAOS does after SaveToSettings (StartRun reads ChaosRunConfig.FromSettings),
        /// just without the dialog.
        /// </summary>
        internal void BtnQuickStartChaos_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Same tier-2 door as the hero card - Quick Start skips the picker, not the gate.
                if (!TierGate.DemandLab("Down the Rabbit Hole")) return;

                // DtRH browser game: same surface as the hero card — see BtnStartChaos_Click.
                // Quick Start skips the save picker by design (that's the "quick" part) and reuses
                // the last-chosen slot, already live in ChaosMeta.State.
                if (App.Settings?.Current?.ChaosWebGameEnabled == true
                    && !Services.Chaos.DtrhHostService.BootFailedThisSession)
                {
                    Services.Chaos.DtrhHostService.Launch();
                    return;
                }
                if (App.Chaos == null || App.Chaos.IsRunning) return;
                // Happy path run 1: the quick start drops into the same scripted naked run.
                if (Services.Chaos.ChaosMeta.State.RunsCompleted == 0)
                {
                    App.Chaos.StartRun(Services.Chaos.ChaosHappyPath.BuildFirstRunConfig());
                    return;
                }
                App.Chaos.StartRun();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "BtnQuickStartChaos_Click failed");
                MessageBox.Show("Couldn't start Down the Rabbit Hole:\n\n" + ex.Message, "Down the Rabbit Hole",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>True once <see cref="Services.IntakePassService.PassStateChanged"/> has been
        /// wired up. The service is created in App.OnStartup, but MainWindow's own constructor and
        /// the tab-navigation path both run refreshes that can beat any fixed hook-up point, so the
        /// subscription is attached lazily from the refresh itself and this flag stops it stacking
        /// duplicate handlers. Never unsubscribed on purpose: both the publisher (an App singleton)
        /// and the subscriber (MainWindow) live for the whole process, so there is no leak to
        /// collect - only a handler that would have to be re-attached for nothing.</summary>
        private bool _intakePassHooked;

        /// <summary>
        /// Paints the Graded Intake page's gate. Four states, not two, since the weekly free pass
        /// landed - see <see cref="Services.IntakePassState"/>:
        ///
        /// * <b>Premium</b> - no gate, no banner. Patrons never learn the pass exists.
        /// * <b>Available</b> - also NO gate: the user may genuinely run it, so nothing is dimmed.
        ///   The in-content banner announces the pass instead.
        /// * <b>Spent</b> - gate with the "next one in N days" copy and the retakes upsell.
        /// * <b>NeedsLogin</b> - gate asking them to sign in, because the pass is per-account.
        ///
        /// Mirrors the Blink Trainer treatment for the closed states: the overlay provides the
        /// visual, IsEnabled stops keyboard tab-through behind it. Pop Quiz sits outside the gated
        /// Border and stays reachable for everyone.
        ///
        /// Runs on every Exclusives navigation and on every Patreon refresh, and can therefore fire
        /// before the tab's template has realised, so every element is null-checked and the whole
        /// body is defensive: a gate that throws takes the tab switch down with it.
        /// </summary>
        internal void RefreshGradedIntakeGate()
        {
            if (GradedIntakeTab == null) return;

            // Cheap and idempotent; done here rather than at startup so it survives whichever
            // refresh happens to be first (see _intakePassHooked).
            EnsureIntakePassHooked();

            try
            {
                // Fall back to the pre-pass behaviour if the service somehow never came up:
                // premium keeps its unlocked page, and everyone else gets the closed door.
                // "Spent" rather than "NeedsLogin" for the fallback deliberately - it matches
                // IntakePassService's own fail-closed default, so a broken service never shows
                // a signed-in user a sign-in prompt they can't act on.
                var state = App.IntakePass?.State
                            ?? (App.Patreon?.HasPremiumAccess == true
                                ? Services.IntakePassState.Premium
                                : Services.IntakePassState.Spent);

                var open = state == Services.IntakePassState.Premium
                           || state == Services.IntakePassState.Available;

                if (GradedIntakeTab.GradedIntakeGate != null)
                {
                    GradedIntakeTab.GradedIntakeGate.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
                    // FX (PR-4a): the shared animated gate treatment. Decoration only - it never
                    // touches Visibility, the three-state copy, or the pass logic above.
                    Controls.PremiumGateFx.Attach(GradedIntakeTab.GradedIntakeGate);
                }
                if (GradedIntakeTab.GradedIntakeGatedContent != null)
                    GradedIntakeTab.GradedIntakeGatedContent.IsEnabled = open;
                if (GradedIntakeTab.GradedIntakePassBanner != null)
                    GradedIntakeTab.GradedIntakePassBanner.Visibility =
                        state == Services.IntakePassState.Available ? Visibility.Visible : Visibility.Collapsed;

                // Gate copy. Only written for the two closed states: repainting it while the gate
                // is collapsed would be wasted work, and leaving the last closed state's strings in
                // place is invisible by definition.
                if (state == Services.IntakePassState.NeedsLogin)
                {
                    SetGradedIntakeGateCopy(
                        Loc.Get("intake_gate_login_headline"),
                        Loc.Get("intake_gate_login_body"),
                        Loc.Get("intake_gate_login_cta"));
                }
                else if (state == Services.IntakePassState.Spent)
                {
                    // Two keys rather than one with a {0}: DaysUntilNextPass floors at 1, and
                    // "unlocks in 1 days" is the sort of thing that gets screenshotted. Languages
                    // with richer plural rules can still diverge further in their own files.
                    var days = Services.IntakePassService.DaysUntilNextPass;
                    var body = days == 1
                        ? Loc.Get("intake_gate_spent_body_one_day")
                        : Loc.GetF("intake_gate_spent_body", days);

                    SetGradedIntakeGateCopy(
                        Loc.Get("intake_gate_spent_headline"),
                        body,
                        Loc.Get("intake_gate_spent_cta"));
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("RefreshGradedIntakeGate failed: {E}", ex.Message);
            }
        }

        /// <summary>Writes the shared gate card's three text slots. Split out only so the two
        /// closed states above read as data rather than as three near-identical null checks each.</summary>
        private void SetGradedIntakeGateCopy(string headline, string body, string cta)
        {
            if (GradedIntakeTab == null) return;
            if (GradedIntakeTab.TxtGradedIntakeGateHeadline != null)
                GradedIntakeTab.TxtGradedIntakeGateHeadline.Text = headline;
            if (GradedIntakeTab.TxtGradedIntakeGateBody != null)
                GradedIntakeTab.TxtGradedIntakeGateBody.Text = body;
            if (GradedIntakeTab.BtnGradedIntakeGateUnlock != null)
                GradedIntakeTab.BtnGradedIntakeGateUnlock.Content = cta;
        }

        /// <summary>
        /// Attaches the pass-state listener once, so the gate repaints the moment a run consumes
        /// the week's pass. Without it the page keeps showing the "your pass is ready" banner until
        /// the user navigates away and back, which reads as the intake not having counted.
        /// No-ops (and stays un-hooked, so a later refresh retries) while App.IntakePass is null.
        /// </summary>
        private void EnsureIntakePassHooked()
        {
            var pass = App.IntakePass;
            if (_intakePassHooked || pass == null) return;
            _intakePassHooked = true;
            pass.PassStateChanged += OnIntakePassStateChanged;
        }

        /// <summary>
        /// The pass is consumed from the intake's result path, which may or may not be on the UI
        /// thread depending on how the WebView2 message landed, so bounce through the dispatcher
        /// and bail if the app is already tearing down (see the Known Issues note about event
        /// handlers firing against closed windows).
        /// </summary>
        private void OnIntakePassStateChanged(object? sender, EventArgs e)
        {
            try
            {
                if (Application.Current?.Dispatcher == null) return;
                if (Dispatcher.HasShutdownStarted) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { RefreshGradedIntakeGate(); }
                    catch (Exception ex) { App.Logger?.Debug("OnIntakePassStateChanged repaint: {E}", ex.Message); }
                }));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("OnIntakePassStateChanged: {E}", ex.Message);
            }
        }

        private void RefreshPastQuizzes()
        {
            try
            {
                // REBRAND: the classic quiz is hidden behind the Graded Intake, so its
                // past-runs list has nothing to advertise. Bail before touching visibility —
                // the panel is Collapsed in XAML and this method was the only thing that
                // ever un-collapsed it. History for the intake lives in the intake page.
                // (Existing quiz history on disk is untouched; unhide and this lights up again.)
                if (GradedIntakeTab?.BtnStartQuiz?.Visibility != Visibility.Visible) return;

                var history = QuizService.LoadHistory();
                GradedIntakeTab.PastQuizzesList.Children.Clear();

                if (history.Count == 0)
                {
                    GradedIntakeTab.TxtPastQuizzesHeader.Visibility = Visibility.Collapsed;
                    GradedIntakeTab.PastQuizzesPanel.Visibility = Visibility.Collapsed;
                    return;
                }

                GradedIntakeTab.TxtPastQuizzesHeader.Visibility = Visibility.Visible;
                GradedIntakeTab.PastQuizzesPanel.Visibility = Visibility.Visible;

                // Trend summary at top — show latest archetype + trend per category that has history.
                // Group by TrendKey (CategoryId string), not the enum: custom categories all
                // collapse to Category=Sissy and would clobber the built-in Sissy stat (#518/#521).
                var categories = history.Select(QuizService.TrendKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var cat in categories)
                {
                    var trend = QuizService.GetScoreTrend(history, cat);
                    if (trend == null) continue;

                    // Extract archetype from latest profile text
                    var latestEntry = history.FirstOrDefault(h =>
                        string.Equals(QuizService.TrendKey(h), cat, StringComparison.OrdinalIgnoreCase));
                    var archetype = "";
                    if (latestEntry != null)
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(latestEntry.ProfileText, @"You are a (.+?)\.");
                        if (match.Success) archetype = match.Groups[1].Value;
                    }

                    var arrow = trend.Direction switch
                    {
                        TrendDirection.Up => "\u2191",
                        TrendDirection.Down => "\u2193",
                        TrendDirection.Flat => "\u2192",
                        _ => ""
                    };
                    var catDisplay = latestEntry != null ? QuizService.DisplayName(latestEntry) : cat;
                    var trendLabel = trend.Direction == TrendDirection.FirstQuiz
                        ? $"{catDisplay}: {trend.LatestPercent}%"
                        : $"{catDisplay}: {trend.LatestPercent}% {arrow}{Math.Abs(trend.DeltaPercent)}%";
                    if (!string.IsNullOrEmpty(archetype))
                        trendLabel += $" · {archetype}";

                    var trendRow = new TextBlock
                    {
                        Text = trendLabel,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)),
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(8, 3, 8, 3)
                    };
                    GradedIntakeTab.PastQuizzesList.Children.Add(trendRow);
                }

                foreach (var entry in history)
                {
                    var pct = entry.MaxScore > 0 ? (int)Math.Round((double)entry.TotalScore / entry.MaxScore * 100) : 0;
                    var catName = QuizService.DisplayName(entry);
                    var label = $"{entry.TakenAt:MMM d}  ·  {catName}  ·  {entry.TotalScore}/{entry.MaxScore} ({pct}%)";

                    var row = new Border
                    {
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Padding = new Thickness(8, 5, 8, 5),
                        Background = System.Windows.Media.Brushes.Transparent
                    };

                    var txt = new TextBlock
                    {
                        Text = label,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB8)),
                        FontSize = 11.5
                    };
                    row.Child = txt;

                    var captured = entry;
                    row.MouseLeftButtonDown += (s, args) =>
                    {
                        // Close any existing report window before opening a new one
                        foreach (var w in Application.Current.Windows.OfType<QuizReportWindow>().ToList())
                            w.Close();
                        new QuizReportWindow(captured) { Owner = this }.Show();
                    };
                    row.MouseEnter += (s, args) =>
                    {
                        if (s is Border b) b.Background = new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
                    };
                    row.MouseLeave += (s, args) =>
                    {
                        if (s is Border b) b.Background = System.Windows.Media.Brushes.Transparent;
                    };

                    GradedIntakeTab.PastQuizzesList.Children.Add(row);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MainWindow: Failed to refresh past quizzes");
            }
        }

        // ============ POP QUIZ HANDLERS ============

        internal void ChkPopQuizEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current == null) return;
            App.Settings.Current.PopQuizEnabled = GradedIntakeTab.ChkPopQuizEnabled.IsChecked == true;
        }

        internal void SliderPopQuizFrequency_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (App.Settings?.Current == null || GradedIntakeTab.TxtPopQuizFrequency == null) return;
            var val = (int)Math.Round(e.NewValue);
            App.Settings.Current.PopQuizFrequency = val;
            GradedIntakeTab.TxtPopQuizFrequency.Text = $"{val}/session hr";
        }

        internal void BtnTestPopQuiz_Click(object sender, RoutedEventArgs e)
        {
            App.PopQuiz?.TestPopQuiz();
        }

        private void OnLockdownActivated()
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    // Enable system key suppression on the keyboard hook (the setter also
                    // installs the hook if panic key / keyword triggers never started it)
                    if (_keyboardHook != null)
                    {
                        _keyboardHook.SuppressSystemKeys = true;
                        if (!_keyboardHook.IsInstalled)
                            App.Logger?.Warning("Lockdown: keyboard hook could not be installed - Esc/Win/Alt-Tab will NOT be blocked this session");
                    }

                    // Gray out strict lock and panic key toggles
                    if (SettingsTab.ChkStrictLock != null)
                    {
                        SettingsTab.ChkStrictLock.IsEnabled = false;
                        SettingsTab.ChkStrictLock.Opacity = 0.4;
                        SettingsTab.ChkStrictLock.ToolTip = Loc.Get("tooltip_you_are_in_lockdown_mode_there_is_no_escape");
                    }
                    if (SettingsTab.ChkNoPanic != null)
                    {
                        SettingsTab.ChkNoPanic.IsEnabled = false;
                        SettingsTab.ChkNoPanic.Opacity = 0.4;
                        SettingsTab.ChkNoPanic.ToolTip = Loc.Get("tooltip_you_are_in_lockdown_mode_there_is_no_escape");
                    }

                    // Swap UI panels
                    if (LockdownTab.LockdownSetupPanel != null) LockdownTab.LockdownSetupPanel.Visibility = Visibility.Collapsed;
                    if (LockdownTab.LockdownActivePanel != null) LockdownTab.LockdownActivePanel.Visibility = Visibility.Visible;

                    // Reset secret exit state
                    _lockdownTimerClickCount = 0;
                    if (LockdownTab.TxtLockdownExit != null)
                    {
                        LockdownTab.TxtLockdownExit.Visibility = Visibility.Collapsed;
                        LockdownTab.TxtLockdownExit.Text = "";
                    }

                    // Apply blood-red theme
                    ApplyLockdownTheme();

                    // Play activation flash animation
                    PlayLockdownActivationAnimation();

                    App.Logger?.Information("Lockdown UI activated");
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Error activating lockdown UI");
                }
            });
        }

        private void OnLockdownDeactivated()
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    // Disable system key suppression. Lockdown may have installed the hook
                    // itself; drop it again unless another feature still needs it (mirrors
                    // the startup install condition). LockdownService restores the user's
                    // real PanicKeyEnabled before raising this event, so the check is safe.
                    if (_keyboardHook != null)
                    {
                        _keyboardHook.SuppressSystemKeys = false;
                        if (App.Settings.Current.PanicKeyEnabled != true &&
                            App.Settings.Current.KeywordTriggersEnabled != true)
                            _keyboardHook.Stop();
                    }

                    // Restore strict lock and panic key toggles
                    if (SettingsTab.ChkStrictLock != null)
                    {
                        SettingsTab.ChkStrictLock.IsEnabled = true;
                        SettingsTab.ChkStrictLock.Opacity = 1.0;
                        SettingsTab.ChkStrictLock.ToolTip = null;
                    }
                    if (SettingsTab.ChkNoPanic != null)
                    {
                        SettingsTab.ChkNoPanic.IsEnabled = true;
                        SettingsTab.ChkNoPanic.Opacity = 1.0;
                        SettingsTab.ChkNoPanic.ToolTip = null;
                    }

                    // Swap UI panels back
                    if (LockdownTab.LockdownSetupPanel != null) LockdownTab.LockdownSetupPanel.Visibility = Visibility.Visible;
                    if (LockdownTab.LockdownActivePanel != null) LockdownTab.LockdownActivePanel.Visibility = Visibility.Collapsed;

                    // Hide secret exit
                    if (LockdownTab.TxtLockdownExit != null)
                        LockdownTab.TxtLockdownExit.Visibility = Visibility.Collapsed;

                    // Restore normal theme
                    RestoreLockdownTheme();

                    App.Logger?.Information("Lockdown UI deactivated");
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Error deactivating lockdown UI");
                }
            });
        }

        private void OnLockdownTick(TimeSpan remaining)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (LockdownTab.TxtLockdownTimer != null)
                {
                    if (remaining.TotalHours >= 1)
                        LockdownTab.TxtLockdownTimer.Text = remaining.ToString(@"h\:mm\:ss");
                    else
                        LockdownTab.TxtLockdownTimer.Text = remaining.ToString(@"mm\:ss");
                }
            });
        }

        internal void TxtLockdownTimer_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var now = DateTime.Now;

            // Reset click count if more than 1 second since last click
            if ((now - _lockdownTimerLastClick).TotalMilliseconds > 1000)
                _lockdownTimerClickCount = 0;

            _lockdownTimerLastClick = now;
            _lockdownTimerClickCount++;

            if (_lockdownTimerClickCount >= 5 && LockdownTab.TxtLockdownExit != null)
            {
                LockdownTab.TxtLockdownExit.Visibility = Visibility.Visible;
                LockdownTab.TxtLockdownExit.Focus();
                _lockdownTimerClickCount = 0;
            }
        }

        internal void TxtLockdownExit_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            if (LockdownTab.TxtLockdownExit != null)
            {
                var phrase = LockdownTab.TxtLockdownExit.Text;
                var success = App.Lockdown?.TryExitWithPhrase(phrase) ?? false;

                if (!success)
                {
                    // Wrong phrase — clear and hide
                    LockdownTab.TxtLockdownExit.Text = "";
                    LockdownTab.TxtLockdownExit.Visibility = Visibility.Collapsed;
                }
            }
        }

        // --- Lockdown Theme ---

        private static readonly Color LockdownCrimson = (Color)ColorConverter.ConvertFromString("#DC143C");
        private static readonly Color LockdownDarkRed = (Color)ColorConverter.ConvertFromString("#8B0000");
        private static readonly Color LockdownPanelBg = (Color)ColorConverter.ConvertFromString("#1A0A0A");
        private static readonly Color LockdownWindowBg = (Color)ColorConverter.ConvertFromString("#100505");

        private void ApplyLockdownTheme()
        {
            try
            {
                // Save current values for restoration
                _preLockdownWindowBg = Background;
                _preLockdownTitleBarBg = TitleBarBorder?.Background;

                // Window background
                Background = new SolidColorBrush(LockdownWindowBg);

                // Title bar
                if (TitleBarBorder != null)
                    TitleBarBorder.Background = new SolidColorBrush(LockdownDarkRed);

                // Player title and glow
                if (TxtPlayerTitle != null)
                {
                    TxtPlayerTitle.Foreground = new SolidColorBrush(LockdownCrimson);
                    if (TxtPlayerTitle.Effect is DropShadowEffect glow)
                        glow.Color = LockdownCrimson;
                }

                // Header version
                if (TxtHeaderVersion != null)
                    TxtHeaderVersion.Foreground = new SolidColorBrush(LockdownCrimson);

                // Level label
                if (TxtLevelLabel != null)
                    TxtLevelLabel.Foreground = new SolidColorBrush(LockdownCrimson);

                // XP bar
                if (XPBar != null)
                    XPBar.Background = new SolidColorBrush(LockdownCrimson);

                // Banner texts
                if (TxtBannerPrimary != null)
                    TxtBannerPrimary.Foreground = new SolidColorBrush(LockdownCrimson);
                if (TxtBannerSecondary != null)
                    TxtBannerSecondary.Foreground = new SolidColorBrush(LockdownCrimson);
                if (TxtBannerTertiary != null)
                    TxtBannerTertiary.Foreground = new SolidColorBrush(LockdownCrimson);

                // Lockdown card border → red glow
                if (LockdownTab.LockdownCardBorder != null)
                {
                    LockdownTab.LockdownCardBorder.BorderBrush = new SolidColorBrush(LockdownCrimson);
                    LockdownTab.LockdownCardBorder.Background = new SolidColorBrush(LockdownPanelBg);
                }

                // Update Application-level resource brushes (affects styled controls)
                var res = Application.Current.Resources;
                res["PinkBrush"] = new SolidColorBrush(LockdownCrimson);
                res["DarkPinkBrush"] = new SolidColorBrush(LockdownDarkRed);
                res["TransparentPinkBrush"] = new SolidColorBrush(Color.FromArgb(0x30, 0xDC, 0x14, 0x3C));
                res["PinkButtonHoveredBrush"] = new SolidColorBrush(LockdownCrimson);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to apply lockdown theme");
            }
        }

        private void RestoreLockdownTheme()
        {
            try
            {
                // Restore saved values
                if (_preLockdownWindowBg != null)
                    Background = _preLockdownWindowBg;
                if (_preLockdownTitleBarBg != null && TitleBarBorder != null)
                    TitleBarBorder.Background = _preLockdownTitleBarBg;

                // Restore lockdown card to normal gradient border using mod colors
                if (LockdownTab.LockdownCardBorder != null)
                {
                    var accentHex = App.Mods?.GetAccentColorHex() ?? "#FF69B4";
                    var secondaryHex = App.Mods?.GetSecondaryColorHex() ?? "#9B59B6";
                    var accentColor = (Color)ColorConverter.ConvertFromString(accentHex);
                    var secondaryColor = (Color)ColorConverter.ConvertFromString(secondaryHex);

                    var borderBrush = new LinearGradientBrush
                    {
                        StartPoint = new System.Windows.Point(0, 0),
                        EndPoint = new System.Windows.Point(1, 1)
                    };
                    borderBrush.GradientStops.Add(new GradientStop(accentColor, 0));
                    borderBrush.GradientStops.Add(new GradientStop(secondaryColor, 1));
                    LockdownTab.LockdownCardBorder.BorderBrush = borderBrush;

                    var bgBrush = new LinearGradientBrush
                    {
                        StartPoint = new System.Windows.Point(0, 0),
                        EndPoint = new System.Windows.Point(1, 1)
                    };
                    bgBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#1A1A32"), 0));
                    bgBrush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#201A38"), 1));
                    LockdownTab.LockdownCardBorder.Background = bgBrush;
                }

                // Re-apply mode-aware theme colors (restores all resource brushes + named elements)
                RefreshThemeAwareElements();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to restore lockdown theme");
            }
        }

        private void PlayLockdownActivationAnimation()
        {
            try
            {
                // Create a full-screen red flash overlay
                var flash = new System.Windows.Controls.Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(180, 220, 20, 60)), // semi-transparent crimson
                    IsHitTestVisible = false
                };

                RootGrid.Children.Add(flash);

                // Fade out over 600ms
                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(600),
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };

                fadeOut.Completed += (_, _) =>
                {
                    try { RootGrid.Children.Remove(flash); } catch { }
                };

                flash.BeginAnimation(OpacityProperty, fadeOut);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to play lockdown animation");
            }
        }

        #endregion
    }
}
