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
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel
{
    // Lab tab: AI lab session controls and state.
    public partial class MainWindow
    {
        #region Lab

        private void InitializeLockdown()
        {
            // Before the null check: the haunted-UI host is the window's own stage (ghost layer +
            // rubble floor + the target registry) and it has to exist whether or not the lockdown
            // service came up. This is the one hook Possession owns in the startup ladder -
            // MainWindow.Possession.cs does the rest.
            InitializePossessionHost();

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
            if (!TierGate.DemandPremium(Loc.Get("tab_lockdown_mode"))) return;

            // Get duration from combo box
            var selectedItem = LockdownTab.CmbLockdownDuration.SelectedItem as ComboBoxItem;
            if (selectedItem?.Tag is not string minutesStr || !int.TryParse(minutesStr, out var minutes))
                return;

            var duration = TimeSpan.FromMinutes(minutes);

            // Show double warning with clear consequences. Built line by line rather than as one
            // literal because the three Safeties are toggles now: a dialog that still promised a
            // forced Strict Lock after the user unticked it would be the app lying about the one
            // screen whose entire job is informed consent. Possession gets its own paragraph for the
            // same reason - "was that a bug?" has to be answerable BEFORE the room starts moving,
            // not only from the ember glow afterwards (POSSESSION.md, "clarity in front").
            var cfg = App.Settings?.Current;
            var warn = new System.Text.StringBuilder();
            warn.Append("- You will be LOCKED IN for ").Append(minutes).Append(" minutes\n");
            if (cfg?.LockdownForceStrictLock == true)
                warn.Append("- Strict Lock will be FORCED ON\n");
            if (cfg?.LockdownDisablePanicKey == true)
                warn.Append("- Panic Key will be DISABLED\n");
            if (cfg?.LockdownBlockSystemKeys == true)
                warn.Append("- Alt+F4, Alt+Tab, the Windows key and Ctrl+Esc will be BLOCKED\n");
            warn.Append("- You CANNOT close the application (minimizing still works)\n");
            warn.Append("- The only escape is waiting for the timer to expire\n");
            warn.Append("  (or Ctrl+Alt+Del → Task Manager as a safety valve)");
            // The Emergency Exit is a GAMBLE, and consent has to say so before the timer starts:
            // pressing it can end the lockdown early, and it can just as easily hand back a fresh
            // full-length one (EMERGENCY_EXIT.md, verdict `sendback`).
            warn.Append("\n- The Emergency Exit button is a gamble: win its little game and you may leave early,\n")
                .Append("  lose it and the timer restarts at its FULL length");
            if (cfg?.LockdownDoseKeeperEnabled == true)
                warn.Append("\n- Nothing running? Lockdown starts the engine and picks features for you\n")
                    .Append("  (and switches them back on if you turn them all off - one more each time)");

            if (cfg?.LockdownPossessionEnabled == true)
            {
                var intensityName = cfg.LockdownPossessionIntensity switch
                {
                    0 => "Gentle",
                    2 => "Full Doki",
                    _ => "Eerie",
                };
                warn.Append("\n- Possession is ON (").Append(intensityName)
                    .Append("): the app's own UI will misbehave, on purpose.\n")
                    .Append("  Ember glow = it was Lockdown, not a bug. Nothing you see is real damage.");
                if (cfg.LockdownPossessionIntensity == 2)
                    warn.Append("\n- Full Doki adds themed fake-crash scares. They are theatre.");
            }

            var confirmed = WarningDialog.ShowDoubleWarning(this, "Lockdown Mode", warn.ToString());

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
                        // Free and already ran this week - the upsell is the honest answer, but SAY
                        // it: ShowAppInfoPopup() is a tab switch now, so on its own it just moves
                        // the user to Settings · Account with no explanation. Same copy the gate
                        // card carries, same "See tiers" destination, now with a reason attached.
                        var days = Services.IntakePassService.DaysUntilNextPass;
                        var body = days == 1
                            ? Loc.Get("intake_gate_spent_body_one_day")
                            : Loc.GetF("intake_gate_spent_body", days);
                        App.Notifications?.Show(body, Services.NotificationType.Warning,
                            TimeSpan.FromSeconds(8), Loc.Get("intake_gate_spent_cta"),
                            () => ShowAppInfoPopup());
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
                // (Phase 6) a Play card that will not have an overlay at all. Keyed: on a
                // server-declared DtRH drop day (DailyFreeService, off-pool override) the door
                // opens for everyone.
                if (!TierGate.DemandLab("Down the Rabbit Hole", "dtrh")) return;

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
        /// Play tab, DtRH card -> "The Caucus Race": the kart run on the descent's media, hosted
        /// as its own WebView2 window (CaucusHostService). Same tier-2 door as FALL IN, checked
        /// here for the same reason: the card's lockband is decoration, the handler is the wall.
        /// </summary>
        internal void BtnStartRace_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!TierGate.DemandLab("Down the Rabbit Hole", "dtrh")) return;
                Services.Chaos.CaucusHostService.Launch();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "BtnStartRace_Click failed");
                MessageBox.Show("Couldn't start The Caucus Race:\n\n" + ex.Message, "The Caucus Race",
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
                // Say no out loud. ShowAppInfoPopup() is a tab switch since Phase 8 (Settings ·
                // Account), not a popup over the page, so a bare call here teleported a free
                // account off the Play door with no dialog, no toast and nothing tying the jump
                // to the card they clicked. TierGate raises the 8s refusal naming the feature and
                // its "See tiers" action lands on the same page - on purpose, and after being told.
                // Same feature name the card's own lockband paints, read from the same key
                // (MainWindow.PlayTab.cs: RequiresPremium(Loc.Get("tab_fyp"))), so band and
                // refusal cannot drift apart or disagree in a translated UI.
                if (!TierGate.DemandPremium(Loc.Get("tab_fyp"), "fyp")) return;

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
        /// Play → The Arcademy strip. Opens the webview mini-game hub (Resources/web/arcademy) via
        /// <see cref="Services.Arcademy.ArcademyHostService"/>, the sibling of the DtRH, Intake and
        /// Goon hosts. Every gate lives in <c>Launch()</c> (T2 through <see cref="TierGate"/>, then
        /// AudioOnlySession, then idempotency) for the same reason the DtRH card leaves its refusal
        /// to <c>BtnStartChaos_Click</c>: the card's lockband is decoration, and the one code path
        /// that actually opens the door has to be the one that can say no.
        /// </summary>
        internal void BtnStartArcademy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // The page's LAST attempt failed (WebView2 runtime missing, WebGL refused, or the
                // shell never answered its 45s progress deadline). DtRH consumes its own
                // BootFailedThisSession at BtnStartChaos_Click, but there the flag DEGRADES to the
                // native game, so the user still gets a feature. Here there is nothing to degrade
                // to, and half of what sets the flag is transient (a cold WebView2 start, a machine
                // under load, a stalled driver), so refusing outright would cost a paying user the
                // headline feature over a stall that a second click would very likely clear.
                // So: warn, and let them choose. Saying yes is the old behaviour, saying no spares
                // them a second black window. A boot that succeeds clears the flag in OnPageReady.
                if (Services.Arcademy.ArcademyHostService.BootFailedThisSession
                    && !Services.Arcademy.ArcademyHostService.IsActive)
                {
                    var again = MessageBox.Show(Loc.Get("arcademy_boot_failed_this_session"),
                        Services.Arcademy.ArcademyHostService.ProductName,
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    App.Logger?.Information("Arcademy: previous boot failed; user chose {Choice}",
                        again == MessageBoxResult.Yes ? "retry" : "not now");
                    if (again != MessageBoxResult.Yes) return;
                }

                Services.Arcademy.ArcademyHostService.Launch();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "BtnStartArcademy_Click failed");
                MessageBox.Show(Loc.GetF("arcademy_open_failed_body", ex.Message),
                    Services.Arcademy.ArcademyHostService.ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
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
                if (!TierGate.DemandLab("Down the Rabbit Hole", "dtrh")) return;

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
                    // PHASE 6: the Play door's Graded Intake card carries the same four states, so
                    // a pass consumed by a run finishing in another window has to repaint the card
                    // too - otherwise the wall keeps offering a week that is already spent.
                    // Separate try so a card failure cannot cost the page its repaint.
                    try { RefreshPlayCards(); }
                    catch (Exception ex) { App.Logger?.Debug("OnIntakePassStateChanged play repaint: {E}", ex.Message); }
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
                    // The system-key block is a SAFETY TOGGLE now, not a law: a user who unticked it
                    // on the card was promised in the warning dialog that Win / Alt+Tab keep working,
                    // and hard-coding true here is exactly how that promise would quietly rot.
                    var blockSysKeys = App.Settings?.Current?.LockdownBlockSystemKeys == true;
                    if (_keyboardHook != null)
                    {
                        _keyboardHook.SuppressSystemKeys = blockSysKeys;
                        if (blockSysKeys && !_keyboardHook.IsInstalled)
                            App.Logger?.Warning("Lockdown: keyboard hook could not be installed - Win/Alt-Tab will NOT be blocked this session");
                    }

                    // Gray out strict lock and panic key toggles.
                    // PHASE 8: re-pointed from the deleted LegacyDashboardHost twin
                    // (SettingsTab.ChkStrictLock) to the LIVE editor - the Studio rack's Video
                    // panel. This is not cosmetic: LockdownService forces StrictLockEnabled true on
                    // activate and restores it on exit, but VideoFeatureControl.ChkStrict_Changed
                    // writes the setting directly, so a reachable toggle would let the user turn
                    // strict lock back off mid-lockdown. Greying the twin nobody could see never
                    // stopped that; greying this one does.
                    // Grey only what is actually in force. A toggle greyed for a safety the user
                    // switched off would be a lock with nothing behind it - and the tripwire that
                    // fires on "tried to flip a greyed safety" would then be reporting a fiction.
                    if (App.Settings?.Current?.LockdownForceStrictLock == true)
                    {
                        var strictChk = StudioTab?.PanelVideo?.ChkStrict;
                        if (strictChk != null)
                        {
                            strictChk.IsEnabled = false;
                            strictChk.Opacity = 0.4;
                            strictChk.ToolTip = Loc.Get("tooltip_you_are_in_lockdown_mode_there_is_no_escape");
                        }
                    }
                    if (App.Settings?.Current?.LockdownDisablePanicKey == true && AppSettingsTab.ChkNoPanic != null)
                    {
                        AppSettingsTab.ChkNoPanic.IsEnabled = false;
                        AppSettingsTab.ChkNoPanic.Opacity = 0.4;
                        AppSettingsTab.ChkNoPanic.ToolTip = Loc.Get("tooltip_you_are_in_lockdown_mode_there_is_no_escape");
                    }

                    // Swap UI panels
                    if (LockdownTab.LockdownSetupPanel != null) LockdownTab.LockdownSetupPanel.Visibility = Visibility.Collapsed;
                    if (LockdownTab.LockdownActivePanel != null) LockdownTab.LockdownActivePanel.Visibility = Visibility.Visible;
                    LockdownTab.StartEmergencyExitPulse();

                    // The badge: the exit affordance for every tab that is not this one.
                    SetLockdownBadge(true, App.Lockdown?.Remaining ?? TimeSpan.Zero);

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

                    HookPossessionReadout();
                    HookLockdownRestart();
                    ShowPossessionRulesIfFirstTime();

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

                    UnhookPossessionReadout();
                    UnhookLockdownRestart();

                    // Restore strict lock and panic key toggles unconditionally: greying is
                    // conditional on activate, un-greying must not be, or a lockdown run with a
                    // safety switched off would leave a toggle that some EARLIER run disabled stuck
                    // that way forever.
                    var strictChk = StudioTab?.PanelVideo?.ChkStrict;
                    if (strictChk != null)
                    {
                        strictChk.IsEnabled = true;
                        strictChk.Opacity = 1.0;
                        strictChk.ToolTip = null;
                    }
                    if (AppSettingsTab.ChkNoPanic != null)
                    {
                        AppSettingsTab.ChkNoPanic.IsEnabled = true;
                        AppSettingsTab.ChkNoPanic.Opacity = 1.0;
                        AppSettingsTab.ChkNoPanic.ToolTip = null;
                    }

                    // Swap UI panels back
                    LockdownTab.StopEmergencyExitPulse();
                    if (LockdownTab.LockdownSetupPanel != null) LockdownTab.LockdownSetupPanel.Visibility = Visibility.Visible;
                    if (LockdownTab.LockdownActivePanel != null) LockdownTab.LockdownActivePanel.Visibility = Visibility.Collapsed;

                    SetLockdownBadge(false, TimeSpan.Zero);

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
                    LockdownTab.TxtLockdownTimer.Text = FormatLockdownClock(remaining);
                if (TxtLockdownBadgeTime != null)
                    TxtLockdownBadgeTime.Text = FormatLockdownClock(remaining);
            });
        }

        private static string FormatLockdownClock(TimeSpan remaining) =>
            remaining.TotalHours >= 1 ? remaining.ToString(@"h\:mm\:ss") : remaining.ToString(@"mm\:ss");

        // --- The lockdown badge -------------------------------------------------------
        // A crimson pill in the title bar's status row. It exists because the Lockdown page is the
        // only place the timer and the Emergency Exit live, and a haunted user on the Assets tab
        // should not have to remember which door leads back. One click, every tab, always the same
        // destination.

        /// <summary>Shows or hides the badge and seeds its clock.</summary>
        private void SetLockdownBadge(bool active, TimeSpan remaining)
        {
            try
            {
                if (LockdownBadge != null)
                    LockdownBadge.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
                if (active && TxtLockdownBadgeTime != null)
                    TxtLockdownBadgeTime.Text = FormatLockdownClock(remaining);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Lockdown: badge visibility failed");
            }
        }

        /// <summary>
        /// The badge is a signpost, not a button that ends anything: it navigates to the Lockdown
        /// page, where the huge Emergency Exit and the secret phrase both live. Nothing here can
        /// end a lockdown, which is what lets it sit one pixel from the window's close button.
        /// </summary>
        private void LockdownBadge_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                e.Handled = true;
                ShowTab("lockdown");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Lockdown: badge navigation failed");
            }
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
                    // Wrong phrase - clear and hide, and trip the wire. Typing at the secret exit is
                    // the most deliberate escape attempt there is, so the room gets to notice.
                    LockdownTab.TxtLockdownExit.Text = "";
                    LockdownTab.TxtLockdownExit.Visibility = Visibility.Collapsed;
                    try { App.Lockdown?.NotifyEscapeAttempt(Services.Possession.EscapeKinds.WrongPhrase); }
                    catch (Exception ex) { App.Logger?.Warning(ex, "Lockdown: wrong-phrase tripwire failed"); }
                }
            }
        }

        // --- Possession readout -------------------------------------------------------
        // The five pips and the rung word under the timer. Owned here rather than in the card's own
        // code-behind because the director's event lives on the window's lifetime, and an unhooked
        // handler on a UserControl that is shown/hidden rather than rebuilt is how you leak one
        // subscription per lockdown.

        /// <summary>Stored so the unsubscribe removes the SAME delegate it added - a fresh lambda at
        /// -= is a no-op and the handler survives every lockdown for the life of the process.</summary>
        private Action<PossessionRung>? _possessionRungHandler;

        private static readonly Color PossessionEmber = (Color)ColorConverter.ConvertFromString("#FF8A5C");
        private static readonly Color PossessionEmberDim = (Color)ColorConverter.ConvertFromString("#33FF8A5C");

        private void HookPossessionReadout()
        {
            try
            {
                // Drop any stale subscription FIRST. This used to be a call to
                // UnhookPossessionReadout() placed after the paint below, which is what made the
                // readout invisible for the whole of every lockdown: Unhook does not only
                // unsubscribe, it also blanks the text and collapses both controls, so the row was
                // painted and then immediately hidden again on the very same pass. Nothing ever
                // put it back, because UpdatePossessionReadout only wrote Text and pip colours.
                // Splitting the unsubscribe out is the fix; the defensive un-collapse in
                // UpdatePossessionReadout is the belt to its braces.
                DetachPossessionRungHandler();

                var on = App.Settings?.Current?.LockdownPossessionEnabled == true;

                if (LockdownTab.TxtPossessionRung != null)
                    LockdownTab.TxtPossessionRung.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                if (LockdownTab.PossessionPips != null)
                    LockdownTab.PossessionPips.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

                if (!on) return;

                // Everything starts at Settle, including the readout - the director raises
                // RungChanged only when the rung MOVES, so nobody else would paint rung 0.
                UpdatePossessionReadout(PossessionRung.Settle);

                if (App.Possession == null) return;

                _possessionRungHandler = rung => Dispatcher.BeginInvoke(() => UpdatePossessionReadout(rung));
                App.Possession.RungChanged += _possessionRungHandler;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Possession: failed to hook the rung readout");
            }
        }

        /// <summary>Unsubscribe only. Never touches the visuals - see the note in
        /// HookPossessionReadout for what happens when those two jobs share one method.</summary>
        private void DetachPossessionRungHandler()
        {
            try
            {
                if (_possessionRungHandler != null && App.Possession != null)
                    App.Possession.RungChanged -= _possessionRungHandler;
                _possessionRungHandler = null;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Possession: failed to detach the rung handler");
            }
        }

        private void UnhookPossessionReadout()
        {
            try
            {
                DetachPossessionRungHandler();

                if (LockdownTab.TxtPossessionRung != null)
                {
                    LockdownTab.TxtPossessionRung.Text = "";
                    LockdownTab.TxtPossessionRung.Visibility = Visibility.Collapsed;
                }
                if (LockdownTab.PossessionPips != null)
                {
                    foreach (var child in LockdownTab.PossessionPips.Children)
                        if (child is Border pip) pip.Background = new SolidColorBrush(PossessionEmberDim);
                    LockdownTab.PossessionPips.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Possession: failed to clear the rung readout");
            }
        }

        private void UpdatePossessionReadout(PossessionRung rung)
        {
            try
            {
                var index = (int)rung;

                if (LockdownTab.TxtPossessionRung != null)
                {
                    LockdownTab.TxtPossessionRung.Text =
                        Loc.GetF("lockdown_poss_readout_fmt", Loc.Get("lockdown_poss_rung_" + index));
                    // Paint implies show. A rung change that lands on a collapsed row is the exact
                    // shape of the bug this readout shipped with, and it costs nothing to refuse it.
                    LockdownTab.TxtPossessionRung.Visibility = Visibility.Visible;
                }
                if (LockdownTab.PossessionPips != null)
                    LockdownTab.PossessionPips.Visibility = Visibility.Visible;

                if (LockdownTab.PossessionPips == null) return;
                for (int i = 0; i < LockdownTab.PossessionPips.Children.Count; i++)
                {
                    if (LockdownTab.PossessionPips.Children[i] is not Border pip) continue;
                    pip.Background = new SolidColorBrush(i <= index ? PossessionEmber : PossessionEmberDim);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Possession: failed to paint the rung readout");
            }
        }

        // --- Timer restart (Emergency Exit "sendback") --------------------------------
        // RestartTimer rewinds the clock to its FULL duration without ending the lockdown, so every
        // readout that caches a number has to be told. CountdownTick would repaint the digits within
        // a second on its own; the point of doing it here is that the second in between must not
        // show the OLD remaining time next to a room that has just reset itself to Settle.

        /// <summary>Same delegate-identity discipline as the rung handler.</summary>
        private Action<string>? _lockdownRestartHandler;

        private void HookLockdownRestart()
        {
            try
            {
                UnhookLockdownRestart();
                if (App.Lockdown == null) return;

                _lockdownRestartHandler = reason => Dispatcher.BeginInvoke(() => OnLockdownTimerRestarted(reason));
                App.Lockdown.TimerRestarted += _lockdownRestartHandler;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Lockdown: failed to hook TimerRestarted");
            }
        }

        private void UnhookLockdownRestart()
        {
            try
            {
                if (_lockdownRestartHandler != null && App.Lockdown != null)
                    App.Lockdown.TimerRestarted -= _lockdownRestartHandler;
                _lockdownRestartHandler = null;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Lockdown: failed to unhook TimerRestarted");
            }
        }

        /// <summary>
        /// The clock went back to full and the director dropped its rung to Settle. Repaint both,
        /// plus the badge, so the page and the title bar agree with the room in the same frame.
        /// </summary>
        private void OnLockdownTimerRestarted(string reason)
        {
            try
            {
                var remaining = App.Lockdown?.Remaining ?? TimeSpan.Zero;

                if (LockdownTab.TxtLockdownTimer != null)
                    LockdownTab.TxtLockdownTimer.Text = FormatLockdownClock(remaining);
                if (TxtLockdownBadgeTime != null)
                    TxtLockdownBadgeTime.Text = FormatLockdownClock(remaining);

                if (App.Settings?.Current?.LockdownPossessionEnabled == true)
                    UpdatePossessionReadout(PossessionRung.Settle);

                App.Logger?.Information("Lockdown: timer restarted ({Reason}) - readout reset to Settle", reason);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Lockdown: failed to repaint after a timer restart");
            }
        }

        /// <summary>
        /// PREVIEW ONLY (Services/Dev/PossessionPreview.cs). Dresses the Lockdown card and the title
        /// bar exactly as a running lockdown would, so the huge Emergency Exit button, the rung
        /// readout and the badge can be photographed - WITHOUT touching LockdownService. No keyboard
        /// hook, no safeties, no timer: the numbers are props and every call is reversible.
        /// </summary>
        internal void PreviewShowLockdownActivePanel(bool on)
        {
            try
            {
                if (LockdownTab.LockdownSetupPanel != null)
                    LockdownTab.LockdownSetupPanel.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
                if (LockdownTab.LockdownActivePanel != null)
                    LockdownTab.LockdownActivePanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

                if (on)
                {
                    if (LockdownTab.TxtLockdownTimer != null) LockdownTab.TxtLockdownTimer.Text = "09:41";
                    LockdownTab.StartEmergencyExitPulse();

                    if (LockdownTab.TxtPossessionRung != null)
                        LockdownTab.TxtPossessionRung.Visibility = Visibility.Visible;
                    if (LockdownTab.PossessionPips != null)
                        LockdownTab.PossessionPips.Visibility = Visibility.Visible;
                    UpdatePossessionReadout(PossessionRung.Melt);

                    SetLockdownBadge(true, TimeSpan.FromSeconds(581));
                }
                else
                {
                    LockdownTab.StopEmergencyExitPulse();
                    UnhookPossessionReadout();
                    SetLockdownBadge(false, TimeSpan.Zero);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "PossessionPreview: could not dress the lockdown active panel");
            }
        }

        /// <summary>
        /// First run only: the warden states the rules before the room starts moving. Card AND bark,
        /// because the card is the one that survives being clicked through in half a second and the
        /// bark is the one in her voice - POSSESSION.md decision 7.
        /// </summary>
        private void ShowPossessionRulesIfFirstTime()
        {
            try
            {
                var s = App.Settings?.Current;
                if (s == null) return;
                if (!s.LockdownPossessionEnabled || s.LockdownPossessionIntroSeen) return;

                FeatureIntroPopup.ShowIfFirstTime("possession", this);
                App.Bark?.NotifyPossessionRules();

                s.LockdownPossessionIntroSeen = true;
                App.Settings?.Save();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Possession: failed to show the first-run rules");
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
