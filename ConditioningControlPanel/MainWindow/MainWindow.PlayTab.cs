using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The Play door's painter (UX restructure, Phase 6).
    ///
    /// <para>The card wall itself is <c>Views\Tabs\PlayTabView.xaml</c> (frame + slots) and
    /// <c>PlayTabView.Cards.cs</c> (click shims). This file is everything those two are not allowed
    /// to be: the live state on the wall — tier lockbands, the Graded Intake's four pass states, the
    /// Goon perk line, the Deeper master switch, the Bureau's account chip — plus the two pieces of
    /// glue the door owes (the Mantra rescue's session start, and the Bureau folder stamp).</para>
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
    /// <para><b>Three cards deliberately have no band</b> — Goon (joining is free by design; the two
    /// paid rungs are named in a sub-line instead of hidden behind a padlock), Bureau (not
    /// tier-gated at all; the page enforces its own clearance) and Mantra / Available Subjects /
    /// Showcase / Loom (free).</para>
    ///
    /// <para><b>Motion budget: one one-shot.</b> No Forever clock is started here. The door's single
    /// ambient loop is <c>RabbitHoleFx</c>, owned and registered by the view; the folder stamp below
    /// is a ~380ms settle that runs once per session and skips itself entirely under
    /// <c>MotionLevel.Off</c>.</para>
    /// </summary>
    public partial class MainWindow
    {
        // ---- tuning ----------------------------------------------------------------------

        /// <summary>Overshoot the Bureau folder lands from. Restored to the digit from the version
        /// that shipped before the card was deleted on 0804.</summary>
        private const double BureauStampScale = 1.15;
        private const double BureauStampMs = 380;

        /// <summary>Opacity of a Goon perk this account has not bought. Dimmed, never hidden and
        /// never disabled: naming the perk is the point (GoonHostService.cs:888-889).</summary>
        private const double GoonPerkLockedOpacity = 0.42;

        private bool _bureauStampPlayed;

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
                // Feature names are the plain-English literals every other TierGate call site
                // already uses ("Down the Rabbit Hole" from MainWindow.Lab.cs:216, "The Gaze
                // Minigame" from MainWindow.LabTab.cs:706, and RailFeatureName's set), so the same
                // refusal never gets two phrasings. They are deliberately not localised - see the
                // doc block on RailFeatureName.
                SetLockband(tab.PlayLockDtrh, TierGate.RequiresLab("Down the Rabbit Hole"));
                SetLockband(tab.PlayLockGaze, TierGate.RequiresLab("The Gaze Minigame"));
                SetLockband(tab.PlayLockFocusGaze, TierGate.RequiresLab("Focus Gaze"));
                SetLockband(tab.PlayLockRemote, TierGate.RequiresPremium("Remote Control"));
                SetLockband(tab.PlayLockLockdown, TierGate.RequiresPremium("Lockdown Mode"));
                SetLockband(tab.PlayLockBlink, TierGate.RequiresPremium("Blink Trainer"));
                SetLockband(tab.PlayLockFyp, TierGate.RequiresPremium("For You"));

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

                // --- Deeper: one flag, two entries ------------------------------------------
                // The same AppSettings.EnableDeeper that drives BtnDeeper's visibility on the rail.
                // If this card outlived the master switch it would be a second, divergent door.
                if (tab.PlayDeeperCard != null)
                {
                    tab.PlayDeeperCard.Visibility = App.Settings?.Current?.EnableDeeper == true
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }

                // --- Bureau: not a tier question, an account one ----------------------------
                // Same condition BureauHostService.HasAuth uses (UnifiedId + AuthToken); read here
                // rather than exposed, because the service's own copy is the authority and a second
                // public property would be a second answer. Says so on the card instead of letting
                // the click open a window that shows nothing but a clearance wall.
                if (tab.PlayBureauNeedsAccount != null)
                {
                    var s = App.Settings?.Current;
                    var hasAccount = !string.IsNullOrEmpty(s?.UnifiedId) && !string.IsNullOrEmpty(s?.AuthToken);
                    tab.PlayBureauNeedsAccount.Visibility = hasAccount ? Visibility.Collapsed : Visibility.Visible;
                }

                // --- the one-shot ------------------------------------------------------------
                // One dispatcher hop at Normal priority (never Loaded - that queue is starved in
                // this window and the callback would simply never run): on the first navigation the
                // visibility flag has not finished propagating down to the folder art, so checking
                // art.IsVisible synchronously here would read false on the very first visit and
                // spend the one-shot on nothing.
                Dispatcher.BeginInvoke(new Action(PlayBureauStamp),
                                       System.Windows.Threading.DispatcherPriority.Normal);
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
        /// Opens the mantra minigame. The two steps are in this order because
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

        // ---- FX: the Bureau folder stamp -------------------------------------------------

        /// <summary>
        /// The Bureau folder lands with a thunk the first time it is seen in a session: one 1.15
        /// overshoot settling to 1.0 with a fade, on the RenderTransform only, so nothing around it
        /// moves. Restored with the card it belongs to (it was deleted on 0804 rather than left
        /// pointing at a control that no longer existed).
        ///
        /// <para>Once per session, and skipped entirely under <c>MotionLevel.Off</c> — a one-shot
        /// needs no park hook, which is why it may live outside the ambient registry. It uses
        /// <c>element.BeginAnimation</c>, never <c>Storyboard.SetTargetName</c>, which silently
        /// no-ops across the tab UserControl namescopes these elements live in.</para>
        /// </summary>
        private void PlayBureauStamp()
        {
            if (_bureauStampPlayed) return;
            try
            {
                var art = PlayTab?.PlayBureauFolderArt;
                if (art == null || !art.IsVisible) return;
                _bureauStampPlayed = true;
                if (!MotionFx.AllowTransitions) return;

                var scale = new ScaleTransform(BureauStampScale, BureauStampScale);
                art.RenderTransformOrigin = new Point(0.5, 0.5);
                art.RenderTransform = scale;

                var settle = new DoubleAnimation(BureauStampScale, 1.0,
                                                 TimeSpan.FromMilliseconds(BureauStampMs))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, settle);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, settle);

                var fade = new DoubleAnimation(0.35, 1.0, TimeSpan.FromMilliseconds(BureauStampMs))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                };
                art.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            catch (Exception ex) { App.Logger?.Debug("PlayBureauStamp: {E}", ex.Message); }
        }
    }
}
