using System;
using System.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The Play door's painter (UX restructure, Phase 6).
    ///
    /// <para>The card wall itself is <c>Views\Tabs\PlayTabView.xaml</c> (frame + slots) and
    /// <c>PlayTabView.Cards.cs</c> (click shims). This file is everything those two are not allowed
    /// to be: the live state on the wall — tier lockbands, the Graded Intake's four pass states and
    /// the Goon perk line.</para>
    ///
    /// <para><b>2026-08-12 relayout.</b> Five cards left the Play page (Available Subjects, Mantras,
    /// Deeper, the Inspection Bureau, the Showcase shelf), and three pieces of this painter went
    /// with them: the <c>EnableDeeper</c> visibility flip, the Bureau's account chip, and the Bureau
    /// folder-stamp one-shot. All three read named parts of cards that no longer exist, so they
    /// could not simply be left pointing at nothing. Nothing was un-shipped — every one of those
    /// features keeps its own door — and the wall's motion budget is now ZERO one-shots on top of
    /// the view's single ambient loop.</para>
    ///
    /// <para><b>The bands are presentation, not enforcement.</b> Every verdict below comes from
    /// <see cref="TierGate"/>, which is the same truth the launch handlers consult
    /// (<c>BtnStartChaos_Click</c> → <c>DemandLab</c>, <c>BtnActivateLockdown_Click</c> →
    /// <c>DemandPremium</c>, …), so the band on the card, the refusal the click produces and the
    /// copy in the toast cannot drift apart the way the Lab smokescreen and the launch handlers
    /// once did. The bands are <c>IsHitTestVisible="False"</c> in the view, so a locked click still
    /// lands on the real handler and still raises the "See tiers" toast. That is the whole point:
    /// this door has no smokescreen, because it mixes free, Tier 1 and Tier 2 in one wall.</para>
    ///
    /// <para><b>Two cards deliberately have no band</b> — Goon (joining is free by design; the two
    /// paid rungs are named in a sub-line instead of hidden behind a padlock) and Loom (free, and a
    /// signpost to the Studio rather than a launch).</para>
    ///
    /// <para><b>Motion budget: nothing.</b> No clock, Forever or one-shot, is started here. The
    /// door's single ambient loop is <c>RabbitHoleFx</c>, owned and registered by the view.</para>
    /// </summary>
    public partial class MainWindow
    {
        // ---- tuning ----------------------------------------------------------------------

        /// <summary>Opacity of a Goon perk this account has not bought. Dimmed, never hidden and
        /// never disabled: naming the perk is the point (GoonHostService.cs:888-889).</summary>
        private const double GoonPerkLockedOpacity = 0.42;

        // ---- the painter -----------------------------------------------------------------

        /// <summary>
        /// Repaints every live thing on the Play wall. Idempotent, defensive, and never throws — it
        /// runs inside <c>ShowTab</c>, where an exception would take the tab switch down with it.
        ///
        /// <para>Called from three places, which between them cover every way the answers change:
        /// <c>case "play"</c> in ShowTab (the user arrived), <c>UpdatePatreonUI</c> (entitlement
        /// landed, or was lost — including the free-user logout that raises no TierChanged at all),
        /// and the <c>IntakePassService.PassStateChanged</c> hook that
        /// <see cref="EnsureIntakePassHooked"/> already installs for the intake page's own gate.
        /// That third one matters: the weekly pass can be spent by a run finishing in another
        /// window, and a band claiming "weekly pass" over a chip the user may legitimately open is
        /// exactly as wrong as one that vanishes a week late.</para>
        /// </summary>
        internal void RefreshPlayCards()
        {
            var tab = PlayTab;
            if (tab == null) return;

            // The intake half of this method reads IntakePassService; hook its change event on the
            // same lazy, idempotent terms the page's gate does, so a pass spent elsewhere repaints
            // the wall too. Cheap and safe to call on every refresh.
            EnsureIntakePassHooked();

            try
            {
                // --- tier lockbands ---------------------------------------------------------
                // Feature names come from the SAME loc keys the cards themselves render, so the
                // band, the click's refusal and the card title are one string in every language -
                // a Japanese user no longer reads a Japanese refusal about an English subject.
                // "Down the Rabbit Hole" stays a literal: it is the brand name, identical in all
                // nine files, so a key would only add a row nobody translates.
                var dtrhVerdict = TierGate.RequiresLab("Down the Rabbit Hole", "dtrh");
                SetLockband(tab.PlayLockDtrh, dtrhVerdict);
                // The DTRH band is hit-test invisible on purpose (FALL IN / Quick Drop refuse in
                // their handlers), but the card's two settings checkboxes write
                // ChaosAnnouncerEnabled / ChaosWebGameEnabled straight through a TwoWay binding
                // with no handler to refuse in. Disable them alongside the band so a free account
                // cannot click through the padlock and rewrite two Descent settings.
                if (tab.ChkPlayChaosAnnouncer != null)
                    tab.ChkPlayChaosAnnouncer.IsEnabled = dtrhVerdict.Allowed;
                if (tab.ChkPlayChaosWebGame != null)
                    tab.ChkPlayChaosWebGame.IsEnabled = dtrhVerdict.Allowed;
                SetLockband(tab.PlayLockGaze, TierGate.RequiresLab(Loc.Get("label_gaze_minigame")));
                SetLockband(tab.PlayLockFocusGaze, TierGate.RequiresLab(Loc.Get("label_focus_gaze")));
                SetLockband(tab.PlayLockRemote, TierGate.RequiresPremium(Loc.Get("tab_remote_control"), "remote"));
                SetLockband(tab.PlayLockLockdown, TierGate.RequiresPremium(Loc.Get("tab_lockdown_mode")));
                SetLockband(tab.PlayLockBlink, TierGate.RequiresPremium(Loc.Get("tab_blink_trainer")));
                SetLockband(tab.PlayLockFyp, TierGate.RequiresPremium(Loc.Get("tab_fyp"), "fyp"));

                // --- Goon: name the rungs, gate nothing -------------------------------------
                // Joining is free and stays free. The T1 send half and the T2 host half are the
                // ONLY paid parts, they are enforced in GoonHostService and on the server, and this
                // line is the honest label for them. Dimming, never disabling: a dead ad is a worse
                // ad, and the click below it opens the lobby either way.
                var canSend = App.Patreon?.HasPremiumAccess == true;
                var canHost = App.Patreon?.HasLabAccess == true;
                if (tab.TxtPlayGoonPerkSend != null)
                    tab.TxtPlayGoonPerkSend.Opacity = canSend ? 1.0 : GoonPerkLockedOpacity;
                if (tab.TxtPlayGoonPerkHost != null)
                    tab.TxtPlayGoonPerkHost.Opacity = canHost ? 1.0 : GoonPerkLockedOpacity;

                // --- Graded Intake: four states, not two ------------------------------------
                RefreshPlayIntakeCard();

                // Nothing else on this wall is conditional. The Deeper master-switch flip and the
                // Bureau's account chip used to live here; both cards left the page on 2026-08-12
                // and the Deeper flag is still honoured where it always mattered - BtnDeeper's
                // visibility on the rail (MainWindow.Settings.cs:153 / MainWindow.DeeperTab.cs:132).
                // Do not reintroduce a second reader of EnableDeeper without a card to hide.
            }
            catch (Exception ex) { App.Logger?.Debug("RefreshPlayCards: {E}", ex.Message); }
        }

        /// <summary>
        /// The Graded Intake card's four pass states, read from the same
        /// <see cref="Services.IntakePassService"/> the page's gate reads so the card and the page
        /// can never disagree about the week.
        ///
        /// <list type="bullet">
        /// <item><b>Premium</b> — no band, no state line. Patrons never learn the pass exists.</item>
        /// <item><b>Available</b> — no band (they may genuinely run it); the line announces the pass
        /// and the second button walks them to Home, where the logo tile's flip ceremony is what
        /// hands one out.</item>
        /// <item><b>Spent</b> — band + the page's own "next one in N days" copy, reused verbatim so
        /// the two surfaces say one sentence.</item>
        /// <item><b>NeedsLogin</b> — band + the sign-in copy; the pass is per-account.</item>
        /// </list>
        ///
        /// <para>The band is still only decoration: every state's click navigates, because a spent
        /// user has to be able to read WHY, and the page's gate is the thing that explains it.</para>
        /// </summary>
        private void RefreshPlayIntakeCard()
        {
            var tab = PlayTab;
            if (tab == null) return;

            // Same fallback as RefreshGradedIntakeGate, and fail-closed-ish for the same reason:
            // premium keeps its open card, everyone else gets the closed one, and "Spent" rather
            // than "NeedsLogin" so a broken service never shows a signed-in user a sign-in prompt
            // they cannot act on.
            var state = App.IntakePass?.State
                        ?? (App.Patreon?.HasPremiumAccess == true
                            ? IntakePassState.Premium
                            : IntakePassState.Spent);

            var open = state == IntakePassState.Premium || state == IntakePassState.Available;
            SetLockbandVisible(tab.PlayLockIntake, !open);

            if (tab.BtnPlayIntakePassHome != null)
            {
                tab.BtnPlayIntakePassHome.Visibility =
                    state == IntakePassState.Available ? Visibility.Visible : Visibility.Collapsed;
            }

            if (tab.TxtPlayIntakeState == null) return;
            switch (state)
            {
                case IntakePassState.Available:
                    tab.TxtPlayIntakeState.Text = Loc.Get("pl6_intake_state_available");
                    tab.TxtPlayIntakeState.Visibility = Visibility.Visible;
                    break;
                case IntakePassState.Spent:
                    // Two keys rather than one with a {0}: DaysUntilNextPass floors at 1, and
                    // "unlocks in 1 days" is the sort of thing that gets screenshotted.
                    var days = IntakePassService.DaysUntilNextPass;
                    tab.TxtPlayIntakeState.Text = days == 1
                        ? Loc.Get("intake_gate_spent_body_one_day")
                        : Loc.GetF("intake_gate_spent_body", days);
                    tab.TxtPlayIntakeState.Visibility = Visibility.Visible;
                    break;
                case IntakePassState.NeedsLogin:
                    tab.TxtPlayIntakeState.Text = Loc.Get("intake_gate_login_body");
                    tab.TxtPlayIntakeState.Visibility = Visibility.Visible;
                    break;
                default:   // Premium
                    tab.TxtPlayIntakeState.Text = string.Empty;
                    tab.TxtPlayIntakeState.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        // ---- Mantra (G11 rescue) ---------------------------------------------------------

        /// <summary>
        /// <b>NO CALLER as of 2026-08-12.</b> The Play page's Mantras card was the only one, and the
        /// card came off that page in the relayout; <c>MantraWindow</c> is back to the state the G11
        /// rescue found it in (a window nothing opens). This helper is deliberately LEFT IN PLACE
        /// rather than deleted, because it is the whole rescue: it is the only code in the repo that
        /// knows the window needs <c>StartSession(n)</c> to have run before it loads, and re-homing
        /// the typed mantra game anywhere (a Deeper hub entry, a Library door card, a quest reward)
        /// costs exactly one <c>StartMantraSession(reps)</c> call. Delete this and that knowledge is
        /// gone again. Owner call: where the game should live now.
        ///
        /// <para>Opens the mantra minigame. The two steps are in this order because
        /// <c>MantraWindow.Window_Loaded</c> reads <c>CurrentMantra</c> and <c>TargetCount</c> off
        /// the service — it has always assumed a session was already running, which is precisely why
        /// a window with no caller could not simply be given one.
        ///
        /// <para>Nothing about the window changes: the anti-cheat hardening from #734 (the pasting
        /// handler, <c>IsUndoEnabled = false</c>, the shared
        /// <c>LockCardWindow.IsBlockedInputGesture</c> guard on the TEXTBOX's PreviewKeyDown, and
        /// <c>ContextMenu="{x:Null}"</c>) lives inside <c>MantraWindow</c> and is untouched by this
        /// rescue. <c>MantraService.StartSession</c> clamps the count to 1..100 itself.</para>
        ///
        /// <para>Free by design — no tier bar. Mantras already credit XP and quests from the voice
        /// path for every account (<c>AutonomyService</c> → <c>CreditExternalMantra</c>); gating the
        /// typed game would be gating the cheaper half of something already given away.</para>
        /// </summary>
        internal void StartMantraSession(int targetReps)
        {
            try
            {
                if (App.Mantra == null) return;

                // Already typing? Focus it rather than restarting - a second StartSession would
                // reset Completions and Streak mid-run, i.e. silently delete the user's progress.
                foreach (var w in Application.Current.Windows)
                {
                    if (w is MantraWindow live)
                    {
                        live.Activate();
                        live.Focus();
                        return;
                    }
                }

                App.Mantra.StartSession(targetReps);
                new MantraWindow { Owner = this }.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "StartMantraSession failed");
                MessageBox.Show("Couldn't start the mantra session:\n\n" + ex.Message, "Mantras",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ---- FX ---------------------------------------------------------------------------
        //
        // Nothing. PlayBureauStamp - the manila-folder "thunk", one 1.15 overshoot per session -
        // went with the Bureau card on 2026-08-12; it animated PlayBureauFolderArt, a named part of
        // that card, so it had nowhere left to land. If the Bureau card is ever re-homed onto a
        // surface that wants it back, the animation is in this file's history
        // (`git log -p -- ConditioningControlPanel/MainWindow/MainWindow.PlayTab.cs`) and the two
        // rules it obeyed are the ones to carry with it: element.BeginAnimation, never
        // Storyboard.SetTargetName (which silently no-ops across the tab UserControl namescopes),
        // and skip entirely under MotionLevel.Off.
    }
}
