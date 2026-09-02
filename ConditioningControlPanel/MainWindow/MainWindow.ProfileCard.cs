using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Descent;

namespace ConditioningControlPanel
{
    /// <summary>
    /// One tile in the Showcase (a featured pin or an "All achievements" grid entry). A named
    /// class rather than the anonymous {Name, Image} it grew from, because click-to-pin needs the
    /// achievement id back out of the clicked tile's DataContext.
    /// </summary>
    internal sealed class ProfileAchievementTile
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public System.Windows.Media.ImageSource? Image { get; init; }
    }

    /// <summary>
    /// Drives the surfaces the Trainer Card layout added in Phase 1 of the Profile redesign:
    /// the hero XP meter, the Showcase's unlock progress + "next up" line, the community rail's
    /// sharing summary, the "back to me" chip, the me-first open, and the Privacy &amp; Sharing
    /// dialog. Everything that already had a render path (name, avatar, stats, badges, OG border)
    /// is untouched and still lives in MainWindow.Browser.cs.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>The Profile tab opens on the logged-in user's own card exactly once per run.</summary>
        private bool _profileMeFirstDone;

        /// <summary>
        /// Whose card is on screen. Set by <see cref="SetProfileViewingSelf"/> and read by both
        /// pin-placeholder toggles: the four empty ☆ plates carry second-person copy ("Pin your
        /// four proudest unlocks here from Customize"), which must never appear over somebody
        /// else's profile. Defaults to true because the tab opens on your own card.
        /// </summary>
        private bool _profileViewingSelf = true;

        /// <summary>
        /// Visibility for the empty-pin placeholder row: shown only on YOUR card, and only while
        /// nothing is pinned. Both callers below go through here so the rule cannot drift.
        /// </summary>
        private Visibility PinPlaceholderVisibility(bool hasPins)
            => !hasPins && _profileViewingSelf ? Visibility.Visible : Visibility.Collapsed;

        // ============================== me-first ==============================

        /// <summary>
        /// First time the tab is shown, render the viewer's own card instead of the "search for a
        /// user" placeholder. Deliberately the same entry point the My Profile button uses, so the
        /// leaderboard-then-local fallback behaves identically.
        ///
        /// Posted at <see cref="DispatcherPriority.Normal"/> — Loaded priority is starved in this
        /// app and the call would silently never run.
        /// </summary>
        internal void EnsureProfileMeFirst()
        {
            if (_profileMeFirstDone) return;
            _profileMeFirstDone = true;
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    try
                    {
                        if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;
                        if (DiscordTab?.ProfileCardWrapper == null) return;
                        // A card may already be on screen if something rendered one before the tab
                        // was first opened - never stomp it.
                        if (DiscordTab.ProfileCardWrapper.Visibility == Visibility.Visible) return;
                        BtnViewMyProfile_Click(this, new RoutedEventArgs());
                    }
                    catch (Exception ex) { App.Logger?.Debug("EnsureProfileMeFirst inner: {E}", ex.Message); }
                }));
            }
            catch (Exception ex) { App.Logger?.Debug("EnsureProfileMeFirst: {E}", ex.Message); }
        }

        /// <summary>
        /// Shows or hides the header's "back to me" chip. Someone else's card is on screen ⇒ the
        /// chip is the way home. Also the single switch every self-only surface on the card reads,
        /// so a new one is gated here rather than by re-deriving "is this mine" at its own site.
        /// </summary>
        internal void SetProfileViewingSelf(bool isSelf)
        {
            try
            {
                _profileViewingSelf = isSelf;
                // THE SPIRAL PLATE rides the same switch (MainWindow.ProfileSpiral.cs): a
                // searched card must never wear your descent. Called BEFORE the early
                // return below, so a missing "back to me" chip cannot strand the plate on
                // somebody else's card.
                RefreshProfileSpiralPlate();
                // So does the migration receipt, and for the same reason.
                RefreshProfileDescentReceipt();
                // Bug #1113: Customize and Privacy always edit YOUR loadout and YOUR sharing
                // toggles whoever's card is up, so over a searched profile they read as an offer
                // to edit that stranger's. Same switch, same reason, and set before the early
                // return below so a missing chip cannot strand them either.
                var selfOnly = isSelf ? Visibility.Visible : Visibility.Collapsed;
                if (DiscordTab?.BtnProfileCustomize != null)
                    DiscordTab.BtnProfileCustomize.Visibility = selfOnly;
                if (DiscordTab?.BtnProfilePrivacy != null)
                    DiscordTab.BtnProfilePrivacy.Visibility = selfOnly;
                if (DiscordTab?.BtnProfileBackToMe == null) return;
                DiscordTab.BtnProfileBackToMe.Visibility = isSelf ? Visibility.Collapsed : Visibility.Visible;
            }
            catch (Exception ex) { App.Logger?.Debug("SetProfileViewingSelf: {E}", ex.Message); }
        }

        // ============================== record stat badges ==============================

        /// <summary>Whether the ModChanged repaint hook for the six Record badges is installed.</summary>
        private bool _profileStatBadgesModHooked;

        /// <summary>
        /// Repaints the Record's six stat badges through <see cref="Services.ModResourceResolver"/>.
        /// They borrow achievement art, and both shipped .ccpmod archives override all 58
        /// achievement PNGs - but XAML can only name the embedded pack URI, so without this the
        /// ledger stayed pink under every mod. Same contract as LoadFeatureImages: the resolver
        /// falls back to the embedded art, and a null (art missing entirely) leaves the XAML source
        /// alone rather than blanking the badge.
        ///
        /// Called from the tab's Loaded handler, which is the only guaranteed pass - and hooks
        /// ModChanged once, because a mod switch from the top bar gives a user already sitting on
        /// this tab no Loaded, no tab change and no visibility change. ModChanged (not
        /// ApplyActiveModChange) is the app's authoritative signal and fires for every caller of
        /// ModService.ActivateMod; the resource cache is cleared before it is raised, so the
        /// re-resolve below sees the new mod's art.
        /// </summary>
        internal void RefreshProfileStatBadges()
        {
            try
            {
                var tab = DiscordTab;
                if (tab == null) return;

                var badgeMap = new (string resourcePath, Image? image)[]
                {
                    ("achievements/lv_10.png", tab.ImgStatXp),
                    ("achievements/pop_the_Thought.png", tab.ImgStatBubbles),
                    ("achievements/10_hours_pink.png", tab.ImgStatVideos),
                    ("achievements/retinal_burn.png", tab.ImgStatGifs),
                    ("achievements/total_lockdown.png", tab.ImgStatLockCards),
                    ("achievements/spiral_eyes.png", tab.ImgStatAchievements),
                };
                foreach (var (path, image) in badgeMap)
                {
                    if (image == null) continue;
                    var art = Services.ModResourceResolver.ResolveImage(path);
                    if (art != null) image.Source = art;
                }

                if (!_profileStatBadgesModHooked && App.Mods != null)
                {
                    _profileStatBadgesModHooked = true;
                    App.Mods.ModChanged += (_, _) =>
                    {
                        var d = Application.Current?.Dispatcher;
                        if (d == null || d.HasShutdownStarted) return;
                        d.BeginInvoke(new Action(RefreshProfileStatBadges));
                    };
                }
            }
            catch (Exception ex) { App.Logger?.Debug("RefreshProfileStatBadges: {E}", ex.Message); }
        }

        // ============================== hero XP meter ==============================

        /// <summary>
        /// Fills the hero's XP bar. <paramref name="levelXp"/> is progress inside the current
        /// level (NOT lifetime XP - the ledger's XP row shows that). The fill is a star-width
        /// grid column, so this is a pure ratio and never touches ActualWidth.
        /// </summary>
        internal void UpdateProfileXpMeter(int level, double levelXp)
        {
            try
            {
                if (DiscordTab == null) return;
                var needed = App.Progression?.GetXPForLevel(Math.Max(1, level)) ?? 0;
                var have = Math.Max(0, levelXp);
                if (needed > 0 && have > needed) have = needed;

                var fraction = needed > 0 ? have / needed : 0;
                if (double.IsNaN(fraction) || double.IsInfinity(fraction)) fraction = 0;
                fraction = Math.Clamp(fraction, 0, 1);

                if (DiscordTab.ProfileXpFillCol != null && DiscordTab.ProfileXpRestCol != null)
                {
                    DiscordTab.ProfileXpFillCol.Width = new GridLength(fraction, GridUnitType.Star);
                    DiscordTab.ProfileXpRestCol.Width = new GridLength(1 - fraction, GridUnitType.Star);
                }
                if (DiscordTab.TxtProfileXpProgress != null)
                {
                    // The Cycle bonus rides the readout itself, not just the chip beside it: the
                    // XP numbers are where somebody looks when they wonder whether the bonus is
                    // real, and the suffix is the cheapest possible answer. Own card only, via
                    // the same kind the chip uses.
                    var kind = OwnDescentReceiptKind();
                    var bonus = DescentMigration.ActiveCycleXpBonus;
                    DiscordTab.TxtProfileXpProgress.Text =
                        DescentReceipt.ShowsXpMultiplier(kind, bonus)
                            ? Loc.GetF("profile_xp_progress_boosted", $"{have:N0}", $"{needed:N0}",
                                       DescentReceipt.BonusPercentText(bonus))
                            : Loc.GetF("profile_xp_progress", $"{have:N0}", $"{needed:N0}");
                }
            }
            catch (Exception ex) { App.Logger?.Debug("UpdateProfileXpMeter: {E}", ex.Message); }
        }

        // ============================== the migration receipt ==============================

        /// <summary>
        /// Which receipt the card on screen is owed. <see cref="DescentReceiptKind.None"/> for a
        /// searched card, for an account that never went through the ceremony, and for one whose
        /// choice the server has not acked yet — the three cases where drawing a mark would be a
        /// claim we cannot stand behind.
        /// </summary>
        private DescentReceiptKind OwnDescentReceiptKind()
        {
            if (!_profileViewingSelf) return DescentReceiptKind.None;
            var settings = App.Settings?.Current;
            if (settings == null) return DescentReceiptKind.None;
            return DescentReceipt.Resolve(settings.DescentMigrationCompleted, settings.DescentMigrationChoice);
        }

        /// <summary>
        /// Paint the permanent record of the migration choice under the hero XP bar.
        ///
        /// <para>THE POINT OF THE WHOLE THING (v6.9.1): the ceremony is one night and one
        /// irreversible question, and until this chip existed the app never mentioned the answer
        /// again. The Cycle bonus was live in the ledger and invisible on the screen, which reads
        /// from the outside exactly like a bonus that was never granted.</para>
        ///
        /// <para>The percent is derived from <see cref="DescentMigration.ActiveCycleXpBonus"/> —
        /// the multiplier <c>AddXP</c> actually applies — and never from a literal, so retuning
        /// the constant retunes the chip with it.</para>
        /// </summary>
        internal void RefreshProfileDescentReceipt()
        {
            try
            {
                var pill = DiscordTab?.ProfileDescentReceipt;
                if (pill == null) return;

                var kind = OwnDescentReceiptKind();
                if (kind == DescentReceiptKind.None)
                {
                    pill.Visibility = Visibility.Collapsed;
                    return;
                }

                var percent = DescentReceipt.BonusPercentText(DescentMigration.ActiveCycleXpBonus);
                var (label, tip) = kind == DescentReceiptKind.Cycle
                    ? (Loc.GetF("profile_cycle_receipt_cycle", percent),
                       Loc.GetF("profile_cycle_receipt_tip_cycle", percent))
                    : (Loc.Get("profile_cycle_receipt_restore"),
                       Loc.Get("profile_cycle_receipt_tip_restore"));

                if (DiscordTab!.ProfileDescentReceiptText != null)
                    DiscordTab.ProfileDescentReceiptText.Text = label;
                pill.ToolTip = tip;
                pill.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { App.Logger?.Debug("RefreshProfileDescentReceipt: {E}", ex.Message); }
        }

        // ============================== showcase ==============================

        /// <summary>
        /// Updates the Showcase's expander header, unlock bar, summary and "next up" line.
        /// <paramref name="unlockedIds"/> is only available for the viewer's own card (the
        /// leaderboard hands out a count, not a list) - without it the "next up" line is hidden
        /// rather than guessed at.
        /// </summary>
        internal void UpdateProfileShowcase(int unlocked, int total, HashSet<string>? unlockedIds)
        {
            try
            {
                if (DiscordTab == null) return;

                // The two counts can arrive from different universes. Your own card passes
                // free-only unlocked / free-only total; a searched card passes the leaderboard's
                // AchievementsCount, which is the raw length of the server's array (patron
                // exclusives and hidden entries included) against the same free-only total. A
                // heavy patron therefore arrives with unlocked > total. Clamp once, here, so the
                // expander header, the bar and the summary can never disagree ("54 of 46 · 100%").
                if (unlocked < 0) unlocked = 0;
                if (total > 0 && unlocked > total) unlocked = total;

                if (DiscordTab.TxtProfileAllAchievementsHeader != null)
                    DiscordTab.TxtProfileAllAchievementsHeader.Text = Loc.GetF("profile_showcase_all_count", unlocked);

                var fraction = total > 0 ? (double)unlocked / total : 0;
                fraction = Math.Clamp(fraction, 0, 1);
                if (DiscordTab.ProfileUnlockFillCol != null && DiscordTab.ProfileUnlockRestCol != null)
                {
                    DiscordTab.ProfileUnlockFillCol.Width = new GridLength(fraction, GridUnitType.Star);
                    DiscordTab.ProfileUnlockRestCol.Width = new GridLength(1 - fraction, GridUnitType.Star);
                }
                if (DiscordTab.TxtProfileUnlockSummary != null)
                {
                    DiscordTab.TxtProfileUnlockSummary.Text = total > 0
                        ? Loc.GetF("profile_showcase_progress", unlocked, total, (int)Math.Round(fraction * 100))
                        : string.Empty;
                }

                if (DiscordTab.TxtProfileNextUp != null)
                {
                    var next = unlockedIds == null ? null : FindNextAchievementName(unlockedIds);
                    if (string.IsNullOrEmpty(next))
                    {
                        DiscordTab.TxtProfileNextUp.Text = string.Empty;
                        DiscordTab.TxtProfileNextUp.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        DiscordTab.TxtProfileNextUp.Text = Loc.GetF("profile_showcase_next_up", next);
                        DiscordTab.TxtProfileNextUp.Visibility = Visibility.Visible;
                    }
                }

                // The four empty pin plates step aside as soon as Phase 2 pins something — and
                // never appear at all on someone else's card (their copy is addressed to you).
                if (DiscordTab.ProfilePinnedPlaceholders != null)
                {
                    var hasPins = DiscordTab.ProfilePinnedShowcase?.ItemsSource is System.Collections.IEnumerable src
                                  && src.Cast<object>().Any();
                    DiscordTab.ProfilePinnedPlaceholders.Visibility = PinPlaceholderVisibility(hasPins);
                }
            }
            catch (Exception ex) { App.Logger?.Debug("UpdateProfileShowcase: {E}", ex.Message); }
        }

        /// <summary>First still-locked achievement in declaration order, mod-aware, hidden and
        /// patron-exclusive entries skipped (they are not "next up" for everyone).</summary>
        private static string? FindNextAchievementName(HashSet<string> unlockedIds)
        {
            try
            {
                var next = Models.Achievement.All.Values
                    .FirstOrDefault(a => !a.IsHidden && !a.IsExclusive && !unlockedIds.Contains(a.Id));
                if (next == null) return null;
                return App.Mods?.MakeModAware(next.Name) ?? next.Name;
            }
            catch { return null; }
        }

        // ============================== sharing summary ==============================

        /// <summary>
        /// The rail's footer line: how many of the ten sharing toggles are on. Read straight from
        /// settings rather than from the checkboxes, so it is correct before the dialog has ever
        /// been opened and the panel's controls have been touched.
        /// </summary>
        internal void UpdateProfileSharingSummary()
        {
            try
            {
                if (DiscordTab?.TxtProfileSharingSummary == null) return;
                var s = App.Settings?.Current;
                if (s == null) return;

                var flags = new[]
                {
                    s.DiscordRichPresenceEnabled,
                    s.DiscordShowLevelInPresence,
                    s.ShowOnlineStatus,
                    s.DiscordShareAchievements,
                    s.DiscordShareLevelUps,
                    s.AllowDiscordDm,
                    s.ShareProfilePicture,
                    s.GoonShareAvatar,
                    s.GoonShareDiscordDm,
                    s.GoonRichPresence,
                };
                var on = flags.Count(f => f);
                DiscordTab.TxtProfileSharingSummary.Text =
                    Loc.GetF("profile_sharing_summary", on, flags.Length - on);
            }
            catch (Exception ex) { App.Logger?.Debug("UpdateProfileSharingSummary: {E}", ex.Message); }
        }

        // ============================== privacy dialog ==============================

        /// <summary>
        /// Opens the relocated sharing controls. The dialog borrows DiscordTabView's single
        /// <c>ProfilePrivacyPanel</c> instance, so the toggles inside it are the very controls
        /// MainWindow keeps writing to - opening and closing changes nothing but where they render.
        /// </summary>
        internal void OpenProfilePrivacyDialog()
        {
            try
            {
                var panel = DiscordTab?.PrivacyPanel;
                if (panel == null) return;

                var dialog = new ProfilePrivacyDialog(panel) { Owner = this };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "OpenProfilePrivacyDialog failed");
            }
            finally
            {
                UpdateProfileSharingSummary();
            }
        }
    }
}
