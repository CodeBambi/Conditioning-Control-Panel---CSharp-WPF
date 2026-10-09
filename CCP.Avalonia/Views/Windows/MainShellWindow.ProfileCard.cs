// PORTED from ConditioningControlPanel/MainWindow/MainWindow.ProfileCard.cs (417 lines) - the
// part of it that is presentation over ported controls, which is most of the file.
//
// The Trainer Card itself is Views/Tabs/DiscordTabView; this partial is the shell-side painter
// for the surfaces that card cannot fill on its own: whose card is on screen, the Showcase's
// unlock meter, the community rail's sharing footer, and the Privacy & Sharing dialog.
//
// TWO NAMESCOPE HAZARDS, both live here:
//   1. This window loads with AvaloniaXamlLoader.Load, so its generated x:Name fields are never
//      assigned - the tab is reached with Named<T>() (MainShellWindow.TabNavigation.cs).
//   2. DiscordTabView loads the SAME way, so ITS x:Name fields are null too. Every control below
//      is therefore reached with page.FindControl<T>(name), never `page.TxtProfileNextUp`. That
//      compiles either way; only one of them draws.
//
// ProfileAchievementTile is NOT redeclared here: this head already carries it, as a public class
// beside DiscordTabView, because the axaml's two DataTemplates name it (x:DataType).
//
// Still head-side, each with the exact symbol and where it lives today:
//   EnsureProfileMeFirst        - ported onto DiscordTabView (with the whole card read), called on
//                                 every show of the discord tab (MainShellWindow.TabNavigation.cs).
//   RefreshProfileStatBadges    - Services.ModResourceResolver.ResolveImage
//                                 (ConditioningControlPanel/Services/ModResourceResolver.cs)
//                                 decodes to System.Windows.Media.ImageSource and falls back to a
//                                 pack:// URI. CoreModArt.OverridePath answers the override half,
//                                 but this head ships no Resources/achievements/*.png to fall back
//                                 to, so there is nothing to paint yet.
//   UpdateProfileXpMeter        - ported onto DiscordTabView.SetXpMeter (Core XpCurve), with the
//                                 descent-bonus suffix (Core DescentReceipt).
//   RefreshProfileDescentReceipt / OwnDescentReceiptKind - ported below (Core DescentReceipt,
//                                 DescentCycleXp.XpBonusFor), called from SetProfileViewingSelf as WPF.
//   FindNextAchievementName     - Models.Achievement.All
//                                 (ConditioningControlPanel/Models/Achievement.cs).
//   RefreshProfileSpiralPlate   - MainShellWindow.ProfileSpiral.cs, still a stub.
//
// Callers: DiscordTabView (own card, search, lookup) calls SetProfileViewingSelf and
// UpdateProfileShowcase; BtnProfilePrivacy_Click opens the Privacy dialog.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Descent;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>The Profile tab. Resolved on every read - see hazard 1 in the header.</summary>
        internal Tabs.DiscordTabView? ProfilePage => Named<Tabs.DiscordTabView>("DiscordTab");

        /// <summary>
        /// Whose card is on screen. Read by the empty-pin placeholder rule below: the four ☆
        /// plates carry second-person copy and must never appear over somebody else's profile.
        /// Defaults to true because the tab opens on your own card.
        /// </summary>
        private bool _profileViewingSelf = true;

        /// <summary>Shown only on YOUR card, and only while nothing is pinned. Both callers go
        /// through here so the rule cannot drift.</summary>
        private bool PinPlaceholdersVisible(bool hasPins) => !hasPins && _profileViewingSelf;

        /// <summary>
        /// Shows or hides the header's "back to me" chip, and with it every self-only surface on
        /// the card. Someone else's card is on screen ⇒ the chip is the way home, and Customize
        /// and Privacy step aside because they always edit YOUR loadout whoever's card is up
        /// (bug #1113).
        /// </summary>
        internal void SetProfileViewingSelf(bool isSelf)
        {
            try
            {
                _profileViewingSelf = isSelf;

                var page = ProfilePage;
                if (page is null) return;

                var customize = page.FindControl<Button>("BtnProfileCustomize");
                if (customize is not null) customize.IsVisible = isSelf;
                var privacy = page.FindControl<Button>("BtnProfilePrivacy");
                if (privacy is not null) privacy.IsVisible = isSelf;

                // The migration receipt rides the same switch: a searched card must never wear your descent.
                RefreshProfileDescentReceipt(page);

                var back = page.FindControl<Button>("BtnProfileBackToMe");
                if (back is not null) back.IsVisible = !isSelf;

                // WPF MainWindow.ProfileCard.cs:98: the spiral plate is yours alone, so it follows the same switch.
                RefreshProfileSpiralPlate();
            }
            catch (Exception ex) { Log.Debug("SetProfileViewingSelf: {E}", ex.Message); }
        }

        /// <summary>WPF OwnDescentReceiptKind: None for a searched card or an account the server has not acked.</summary>
        internal DescentReceiptKind OwnDescentReceiptKind()
        {
            if (!_profileViewingSelf) return DescentReceiptKind.None;
            var s = CoreSettings.Current;
            return DescentReceipt.Resolve(s.DescentMigrationCompleted, s.DescentMigrationChoice);
        }

        /// <summary>WPF RefreshProfileDescentReceipt: the permanent record of the migration under the hero XP
        /// bar; the percent is the multiplier ProgressionBank actually applies.</summary>
        private void RefreshProfileDescentReceipt(Tabs.DiscordTabView page)
        {
            var pill = page.FindControl<Border>("ProfileDescentReceipt");
            if (pill is null) return;
            var kind = OwnDescentReceiptKind();
            if (kind == DescentReceiptKind.None)
            {
                pill.IsVisible = false;
                return;
            }
            var percent = DescentReceipt.BonusPercentText(
                DescentCycleXp.XpBonusFor(CoreSettings.Current));
            var (label, tip) = kind == DescentReceiptKind.Cycle
                ? (Loc.GetF("profile_cycle_receipt_cycle", percent), Loc.GetF("profile_cycle_receipt_tip_cycle", percent))
                : (Loc.Get("profile_cycle_receipt_restore"), Loc.Get("profile_cycle_receipt_tip_restore"));
            if (page.FindControl<TextBlock>("ProfileDescentReceiptText") is { } text) text.Text = label;
            ToolTip.SetTip(pill, tip);
            pill.IsVisible = true;
        }

        /// <summary>First still-locked achievement in declaration order, mod-aware, hidden and
        /// patron-exclusive entries skipped (they are not "next up" for everyone). WPF FindNextAchievementName.</summary>
        internal static string? FindNextAchievementName(HashSet<string> unlockedIds)
        {
            try
            {
                var next = global::ConditioningControlPanel.Models.Achievement.All.Values
                    .FirstOrDefault(a => !a.IsHidden && !a.IsExclusive && !unlockedIds.Contains(a.Id));
                return next == null ? null : CoreMods.MakeModAware(next.Name);
            }
            catch { return null; }
        }

        /// <summary>
        /// Updates the Showcase's expander header, unlock bar, summary and "next up" line.
        ///
        /// <para><paramref name="unlockedIds"/> is only available for the viewer's own card (the
        /// leaderboard hands out a count, not a list). It is carried unused for now because the
        /// only thing that reads it is FindNextAchievementName, which needs Models.Achievement.All
        /// - so the "next up" line is HIDDEN rather than guessed at, exactly as WPF hides it when
        /// the list is null.</para>
        /// </summary>
        internal void UpdateProfileShowcase(int unlocked, int total, HashSet<string>? unlockedIds)
        {
            try
            {
                var page = ProfilePage;
                if (page is null) return;

                // The two counts can arrive from different universes: your own card passes
                // free-only unlocked / free-only total, a searched card passes the leaderboard's
                // raw AchievementsCount (patron exclusives and hidden entries included) against
                // the same free-only total, so a heavy patron arrives with unlocked > total.
                // Clamp once, here, so the header, the bar and the summary cannot disagree
                // ("54 of 46 · 100%").
                if (unlocked < 0) unlocked = 0;
                if (total > 0 && unlocked > total) unlocked = total;

                var header = page.FindControl<TextBlock>("TxtProfileAllAchievementsHeader");
                if (header is not null) header.Text = Loc.GetF("profile_showcase_all_count", unlocked);

                var fraction = total > 0 ? (double)unlocked / total : 0;
                fraction = Math.Clamp(fraction, 0, 1);
                SetProfileMeter(page, "ProfileUnlockBar", fraction);

                var summary = page.FindControl<TextBlock>("TxtProfileUnlockSummary");
                if (summary is not null)
                {
                    summary.Text = total > 0
                        ? Loc.GetF("profile_showcase_progress", unlocked, total, (int)Math.Round(fraction * 100))
                        : string.Empty;
                }

                var nextUp = page.FindControl<TextBlock>("TxtProfileNextUp");
                if (nextUp is not null)
                {
                    // WPF MainWindow.ProfileCard.cs:315: unknown (someone else's card) says nothing.
                    var next = unlockedIds == null ? null : FindNextAchievementName(unlockedIds);
                    nextUp.Text = string.IsNullOrEmpty(next) ? string.Empty : Loc.GetF("profile_showcase_next_up", next);
                    nextUp.IsVisible = !string.IsNullOrEmpty(next);
                }

                // The four empty pin plates step aside as soon as something is pinned - and never
                // appear at all on someone else's card, because their copy is addressed to you.
                var placeholders = page.FindControl<StackPanel>("ProfilePinnedPlaceholders");
                if (placeholders is not null)
                {
                    var hasPins = page.FindControl<ItemsControl>("ProfilePinnedShowcase")?.ItemsSource
                                      is IEnumerable src && src.Cast<object>().Any();
                    placeholders.IsVisible = PinPlaceholdersVisible(hasPins);
                }
            }
            catch (Exception ex) { Log.Debug("UpdateProfileShowcase: {E}", ex.Message); }
        }

        /// <summary>
        /// The community rail's footer line: how many of the ten sharing toggles are on. Read
        /// straight from settings rather than from the checkboxes, so it is correct before the
        /// Privacy dialog has ever been opened and the panel's controls have been touched.
        /// </summary>
        internal void UpdateProfileSharingSummary()
        {
            try
            {
                var text = ProfilePage?.FindControl<TextBlock>("TxtProfileSharingSummary");
                if (text is null) return;

                var s = CoreSettings.Current;
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
                text.Text = Loc.GetF("profile_sharing_summary", on, flags.Length - on);
            }
            catch (Exception ex) { Log.Debug("UpdateProfileSharingSummary: {E}", ex.Message); }
        }

        /// <summary>
        /// Opens the relocated sharing controls. The dialog BORROWS DiscordTabView's single
        /// long-lived <c>PrivacyPanel</c> instance, so the toggles inside it are the very controls
        /// the shell keeps writing to - opening and closing changes nothing but where they render.
        ///
        /// <para>async void, and ShowDialog needs an owner: Avalonia's is awaitable where WPF's
        /// blocks. The summary is repainted in the finally either way, as WPF does.</para>
        /// </summary>
        internal async void OpenProfilePrivacyDialog()
        {
            try
            {
                var page = ProfilePage;
                if (page is null) return;
                await new ProfilePrivacyDialog(page.PrivacyPanel).ShowDialogSafe(this);
            }
            catch (Exception ex) { Log.Error(ex, "OpenProfilePrivacyDialog failed"); }
            finally { UpdateProfileSharingSummary(); }
        }

        /// <summary>
        /// One two-column star meter's fill. WPF names the two ColumnDefinitions
        /// (ProfileXpFillCol / ProfileXpRestCol); an Avalonia ColumnDefinition is not a
        /// StyledElement and cannot carry an x:Name (AVLN2000), so the axaml names the GRID and
        /// the columns are written by index - the same two GridLengths.
        /// </summary>
        private static void SetProfileMeter(Control page, string gridName, double fraction)
        {
            var grid = page.FindControl<Grid>(gridName);
            if (grid is null || grid.ColumnDefinitions.Count < 2) return;
            grid.ColumnDefinitions[0].Width = new GridLength(fraction, GridUnitType.Star);
            grid.ColumnDefinitions[1].Width = new GridLength(1 - fraction, GridUnitType.Star);
        }
    }
}
