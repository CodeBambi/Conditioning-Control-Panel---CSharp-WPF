// PORTED from ConditioningControlPanel/MainWindow/MainWindow.ProfileCosmetics.cs (532 lines) -
// the Trainer Card's cosmetics painter, sorted member by member. TWO of its twenty members cross,
// and both are rules rather than paint.
//
// ONE SERVICE BLOCKS SEVENTEEN OF THE OTHERS: Services.CosmeticsCatalog
// (ConditioningControlPanel/Services/Profile/CosmeticsCatalog.cs). It owns SanitizeOwn,
// SanitizeViewed, GetAvatarImage, GetBannerImage and TryGetAccentColor - both what a loadout is
// ALLOWED to contain and what it looks like. The sanitize half is portable logic; the four
// image/colour members decode to System.Windows.Media types, so the class cannot move as it
// stands - it splits, or it grows a seam. UPDATE 2026-10-09: Helpers/ModArt now answers the image
// half, so banner, accent, title and pins ARE painted here (the partial class at the bottom).
// The notes below describe the state before that. Until then ApplyOwnProfileCosmetics,
// ApplyViewedProfileCosmetics, ApplyProfileCosmetics, ApplyProfileAvatarPreset,
// ApplyProfileBanner, ApplyProfileAccent, ApplyProfilePins, RefreshShowcasePinArt and
// ToggleOwnAchievementPin are blocked at the door (the wardrobe half of the apply path is in
// MainShellWindow.ProfileWardrobe.cs). Models.ProfileCosmetics itself IS in Core, so the model is not what is missing.
//
// THE REST, each with the exact symbol and where it lives today:
//   ApplyProfileTitle          - Models.Achievement.All / .LocalizedName
//   ResolveAchievementTitle      (ConditioningControlPanel/Models/Achievement.cs). CoreMods
//                                .MakeModAware answers the mod-aware half; the roster does not
//                                exist here, so every id would resolve to null.
//   OpenProfileCustomizeDialog - PORTED to MainShellWindow.ProfileWardrobe.cs: settings save,
//   PersistOwnCosmetics          wardrobe repaint and the push (Core SyncPush.PushCosmeticsAsync).
//   ToggleOwnAchievementPin    - App.Achievements again, plus CosmeticsCatalog's pin cap.
//   FlashPinCapNotice          - portable, but only ever fires from ToggleOwnAchievementPin.
//   SetProfilePictureLoad      - clears _appliedPresetAvatar, an ImageSource only
//                                ApplyProfileAvatarPreset reads. A setter without its only reader
//                                is a token method.
//   DefaultHeroBorderColor     - belongs with ApplyProfileAccent.
//
// WHAT IS REAL HERE: ProfilePictureLoad and ProfileAvatarSlot.PresetMayClaim - the avatar slot's
// CLAIM RULE (bug #847), kept as a pure function so it can be reasoned about without standing a
// Window up, and carrying no WPF type at all. They belong in CCP.Core, which this layer does not
// own; restored here verbatim so the layer that ports ApplyProfileAvatarPreset finds the rule
// rather than re-deriving it from the bug report. NO CALLER YET - that method is blocked above.

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// Where the real profile picture stands for the card on screen. The preset avatar and the
    /// picture share one slot, so "no picture" and "no picture YET" have to be distinguishable -
    /// an empty bubble on its own means neither (#847).
    /// </summary>
    internal enum ProfilePictureLoad
    {
        /// <summary>A load is in flight; the empty slot belongs to it and nothing else may take it.</summary>
        Pending,
        /// <summary>A real picture is in the slot.</summary>
        Loaded,
        /// <summary>The load finished with nothing: no avatar, sharing off, or a lookup that failed.</summary>
        None
    }

    /// <summary>
    /// The avatar slot's claim rule, kept as a pure function so it can be reasoned about (and
    /// tested) without standing a Window up. See MainWindow.ApplyProfileAvatarPreset.
    /// </summary>
    internal static class ProfileAvatarSlot
    {
        /// <summary>Whether the preset avatar may write the shared avatar slot right now.</summary>
        internal static bool PresetMayClaim(bool slotHoldsOurPreset, bool slotHasPicture, ProfilePictureLoad load)
        {
            // Already ours: swapping one preset for another, or clearing back to the blank circle.
            if (slotHoldsOurPreset) return true;
            // Someone's real picture is in there. It always wins.
            if (slotHasPicture) return false;
            // Empty - but only a load that has DEFINITIVELY come back with nothing hands it over.
            return load == ProfilePictureLoad.None;
        }
    }

    public partial class MainShellWindow
    {
        // The paint half of WPF ApplyProfileCosmetics: banner, accent, title and the four pins. The
        // art comes from Helpers/ModArt (banner brush, achievement PNGs) and the ids arrive already
        // sanitized (MainShellWindow.ProfileWardrobe.cs), which is what CosmeticsCatalog did in WPF.
        // STILL OWED: ApplyProfileAvatarPreset (the claim rule above has no caller yet), the accent
        // glow around the card (the card clips its own bounds) and ToggleOwnAchievementPin.

        /// <summary>The hero border at rest, i.e. no accent equipped (matches DiscordTabView.axaml).</summary>
        private static readonly Color DefaultHeroBorderColor = Color.Parse("#FF69B4");

        private List<string> _appliedPinIds = new();

        /// <summary>WPF ApplyProfileCosmetics minus the wardrobe (which the caller paints next).</summary>
        private void ApplyProfileCardCosmetics(ProfileCosmetics cosmetics)
        {
            ApplyProfileBanner(cosmetics.BannerId);
            ApplyProfileAccent(cosmetics.Accent);
            ApplyProfileTitle(cosmetics.TitleId);
            ApplyProfilePins(cosmetics.PinnedAchievements);
        }

        /// <summary>Banner art behind the hero. A null id or art that will not load clears the layer and the
        /// gradient underneath shows, so "no banner" and "broken banner" look the same. Top-anchored: the
        /// plates keep their subject in the upper part and a centre crop cuts it off.</summary>
        private void ApplyProfileBanner(string? bannerId)
        {
            try
            {
                var layer = ProfilePage?.FindControl<Border>("ProfileHeroBanner");
                if (layer == null) return;
                layer.Background = ModArt.Banner(bannerId, 1024, AlignmentY.Top);
            }
            catch (Exception ex) { Log.Debug("ApplyProfileBanner: {E}", ex.Message); }
        }

        /// <summary>Tints the hero border and the three shelf headers. The OG ring is never touched.</summary>
        private void ApplyProfileAccent(string? accent)
        {
            try
            {
                var page = ProfilePage;
                if (page == null) return;
                var hasAccent = !string.IsNullOrWhiteSpace(accent) && Color.TryParse(accent, out _);
                var color = hasAccent ? Color.Parse(accent!) : DefaultHeroBorderColor;

                if (page.FindControl<Border>("ProfileHeroCard") is { } card)
                    card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, color.R, color.G, color.B));

                var header = new SolidColorBrush(hasAccent ? color : Colors.White);
                foreach (var name in new[] { "TxtProfileRecordHeader", "TxtProfileShowcaseHeader", "TxtProfileCommunityHeader" })
                    if (page.FindControl<TextBlock>(name) is { } block) block.Foreground = header;
            }
            catch (Exception ex) { Log.Debug("ApplyProfileAccent: {E}", ex.Message); }
        }

        /// <summary>The gold line under the badges: the achievement's own name, worn as a title.</summary>
        private void ApplyProfileTitle(string? titleId)
        {
            try
            {
                var block = ProfilePage?.FindControl<TextBlock>("TxtProfileEquippedTitle");
                if (block == null) return;
                var name = ResolveAchievementTitle(titleId);
                block.Text = name ?? string.Empty;
                block.IsVisible = !string.IsNullOrEmpty(name);
            }
            catch (Exception ex) { Log.Debug("ApplyProfileTitle: {E}", ex.Message); }
        }

        /// <summary>An achievement id as a wearable title, or null when the id is unknown.</summary>
        internal static string? ResolveAchievementTitle(string? achievementId)
        {
            if (string.IsNullOrWhiteSpace(achievementId)) return null;
            if (!Achievement.All.TryGetValue(achievementId!, out var achievement)) return null;
            return CoreMods.MakeModAware(achievement.TitleName);
        }

        /// <summary>Fills the Showcase's four featured slots. The empty plates step aside as soon as anything
        /// is pinned and come back with the last unpin, on your own card only. A pin whose art is missing is
        /// simply not shown.</summary>
        private void ApplyProfilePins(List<string>? pinnedIds)
        {
            try
            {
                var page = ProfilePage;
                var showcase = page?.FindControl<ItemsControl>("ProfilePinnedShowcase");
                if (page == null || showcase == null) return;

                _appliedPinIds = pinnedIds != null ? new List<string>(pinnedIds) : new List<string>();
                var items = new List<Tabs.ProfileAchievementTile>();
                foreach (var id in _appliedPinIds)
                {
                    if (!Achievement.All.TryGetValue(id, out var achievement)) continue;
                    var image = ModArt.TryLoad($"achievements/{achievement.ImageName}", 176);
                    if (image == null) continue;
                    items.Add(new Tabs.ProfileAchievementTile(id, ResolveAchievementTitle(id) ?? achievement.Name, image));
                }
                showcase.ItemsSource = items.Count > 0 ? items : null;

                if (page.FindControl<StackPanel>("ProfilePinnedPlaceholders") is { } placeholders)
                    placeholders.IsVisible = PinPlaceholdersVisible(items.Count > 0);
            }
            catch (Exception ex) { Log.Debug("ApplyProfilePins: {E}", ex.Message); }
        }
    }
}
