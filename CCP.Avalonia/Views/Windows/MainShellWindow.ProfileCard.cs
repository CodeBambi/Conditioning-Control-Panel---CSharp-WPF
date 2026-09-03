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
//   EnsureProfileMeFirst        - calls DiscordTabView.BtnViewMyProfile_Click, which forwards to
//                                 MainWindow's leaderboard-then-local profile fetch
//                                 (ConditioningControlPanel/MainWindow/MainWindow.Browser.cs).
//   RefreshProfileStatBadges    - Services.ModResourceResolver.ResolveImage
//                                 (ConditioningControlPanel/Services/ModResourceResolver.cs)
//                                 decodes to System.Windows.Media.ImageSource and falls back to a
//                                 pack:// URI. CoreModArt.OverridePath answers the override half,
//                                 but this head ships no Resources/achievements/*.png to fall back
//                                 to, so there is nothing to paint yet.
//   UpdateProfileXpMeter        - ProgressionService.GetXPForLevel
//                                 (ConditioningControlPanel/Services/Progression/ProgressionService.cs).
//                                 CoreProgression carries AddXP only, not the level curve.
//   RefreshProfileDescentReceipt- DescentReceipt / DescentMigration.ActiveCycleXpBonus
//                                 (ConditioningControlPanel/Services/Descent/). SetProfileViewingSelf
//                                 below still enforces the half that is portable: a searched card
//                                 wears no receipt.
//   OwnDescentReceiptKind       - the same two types.
//   FindNextAchievementName     - Models.Achievement.All
//                                 (ConditioningControlPanel/Models/Achievement.cs).
//   RefreshProfileSpiralPlate   - MainShellWindow.ProfileSpiral.cs, still a stub.
//
// No caller yet for any member below: the buttons that invoke them are DiscordTabView's inert
// handlers (BtnProfilePrivacy_Click, BtnProfileSearch_Click) and MainShellWindow.Browser.cs's
// profile render, none of which this layer owns.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
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

                // The migration receipt rides the same switch: a searched card must never wear
                // your descent. WPF re-resolves the pill through RefreshProfileDescentReceipt;
                // only its "what does a self card show" half is blocked (see the header), and
                // hiding it on a stranger's card is the half that must not wait for that.
                if (!isSelf)
                {
                    var receipt = page.FindControl<Border>("ProfileDescentReceipt");
                    if (receipt is not null) receipt.IsVisible = false;
                }

                var back = page.FindControl<Button>("BtnProfileBackToMe");
                if (back is not null) back.IsVisible = !isSelf;
            }
            catch (Exception ex) { Log.Debug("SetProfileViewingSelf: {E}", ex.Message); }
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
                    // ponytail: FindNextAchievementName needs Models.Achievement.All
                    // (ConditioningControlPanel/Models/Achievement.cs). Until it is reachable the
                    // answer is "unknown", and WPF's own rule for unknown is to say nothing.
                    _ = unlockedIds;
                    nextUp.Text = string.Empty;
                    nextUp.IsVisible = false;
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
                await new ProfilePrivacyDialog(page.PrivacyPanel).ShowDialog(this);
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
