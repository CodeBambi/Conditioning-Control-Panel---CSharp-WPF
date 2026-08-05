using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Phase 2 of the Profile redesign ("Own It"): paints an equipped loadout onto the Trainer
    /// Card and opens the Customize dialog.
    ///
    /// One rule runs through all of it — a cosmetic can never cost someone their card. Every
    /// unknown id, missing asset and null service falls back to the Phase 1 look instead of
    /// throwing, and every apply is total: it always writes BOTH the "on" and the "off" state, so
    /// switching from a decorated profile to a bare one leaves nothing of the previous card behind.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>The hero border at rest, i.e. no accent equipped (matches DiscordTabView.xaml).</summary>
        private static readonly Color DefaultHeroBorderColor =
            (Color)ColorConverter.ConvertFromString("#FF69B4");

        // ============================== apply ==============================

        /// <summary>
        /// Paints the viewer's own loadout, straight from settings. Used by every own-card render
        /// path so the card looks the same whether it arrived via My Profile, the me-first open or
        /// a search that happened to land on yourself.
        /// </summary>
        internal void ApplyOwnProfileCosmetics()
        {
            try
            {
                ApplyProfileCosmetics(CosmeticsCatalog.SanitizeOwn(App.Settings?.Current?.ProfileCosmetics));
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyOwnProfileCosmetics: {E}", ex.Message); }
        }

        /// <summary>
        /// Paints someone else's loadout as it arrived from /user/lookup. Their unlock list is not
        /// knowable, so pins/titles are validated against the achievement catalogue only.
        /// </summary>
        internal void ApplyViewedProfileCosmetics(ProfileCosmetics? cosmetics)
        {
            try
            {
                ApplyProfileCosmetics(CosmeticsCatalog.SanitizeViewed(cosmetics));
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyViewedProfileCosmetics: {E}", ex.Message); }
        }

        /// <summary>
        /// The single render path. <paramref name="cosmetics"/> must already be sanitized; pass an
        /// empty object (never null) to strip the card back to its Phase 1 look.
        /// </summary>
        private void ApplyProfileCosmetics(ProfileCosmetics cosmetics)
        {
            if (DiscordTab == null) return;

            ApplyProfileBanner(cosmetics.BannerId);
            ApplyProfileAccent(cosmetics.Accent);
            ApplyProfileTitle(cosmetics.TitleId);
            ApplyProfilePins(cosmetics.PinnedAchievements);
        }

        /// <summary>
        /// Banner art behind the hero. A null id or an asset that failed to load clears the Image,
        /// revealing the gradient Border the layout keeps painted underneath — so "no banner" and
        /// "broken banner" look identical and neither looks broken.
        /// </summary>
        private void ApplyProfileBanner(string? bannerId)
        {
            try
            {
                var image = DiscordTab?.ProfileHeroBanner;
                if (image == null) return;
                image.Source = CosmeticsCatalog.GetBannerImage(bannerId);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyProfileBanner: {E}", ex.Message); }
        }

        /// <summary>
        /// Tints the hero border/glow and the three shelf column headers. The OG ring is NOT
        /// touched: it sits behind the card in its own container and stays earned-only gold.
        /// </summary>
        private void ApplyProfileAccent(string? accent)
        {
            try
            {
                if (DiscordTab == null) return;

                var hasAccent = CosmeticsCatalog.TryGetAccentColor(accent, out var color);
                var accentColor = hasAccent ? color : DefaultHeroBorderColor;

                if (DiscordTab.ProfileHeroCard != null)
                {
                    var border = new SolidColorBrush(Color.FromArgb(0x99, accentColor.R, accentColor.G, accentColor.B));
                    if (border.CanFreeze) border.Freeze();
                    DiscordTab.ProfileHeroCard.BorderBrush = border;

                    // Glow only when something is actually equipped — the default card is flat.
                    DiscordTab.ProfileHeroCard.Effect = hasAccent
                        ? new System.Windows.Media.Effects.DropShadowEffect
                        {
                            Color = accentColor,
                            BlurRadius = 22,
                            ShadowDepth = 0,
                            Opacity = 0.5
                        }
                        : null;
                }

                var headerBrush = new SolidColorBrush(hasAccent ? accentColor : Colors.White);
                if (headerBrush.CanFreeze) headerBrush.Freeze();
                foreach (var header in new[]
                         {
                             DiscordTab.TxtProfileRecordHeader,
                             DiscordTab.TxtProfileShowcaseHeader,
                             DiscordTab.TxtProfileCommunityHeader
                         })
                {
                    if (header != null) header.Foreground = headerBrush;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyProfileAccent: {E}", ex.Message); }
        }

        /// <summary>
        /// The gold line under the badges. Titles reuse the achievement's existing localized name
        /// (<c>achievement_&lt;id&gt;_name</c>), so no title needs a language-file key of its own.
        /// </summary>
        private void ApplyProfileTitle(string? titleId)
        {
            try
            {
                var block = DiscordTab?.TxtProfileEquippedTitle;
                if (block == null) return;

                var name = ResolveAchievementTitle(titleId);
                if (string.IsNullOrEmpty(name))
                {
                    block.Text = string.Empty;
                    block.Visibility = Visibility.Collapsed;
                    return;
                }

                block.Text = name;
                block.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyProfileTitle: {E}", ex.Message); }
        }

        /// <summary>
        /// An achievement id rendered as a wearable title, or null when the id is unknown.
        /// Falls back to the achievement's built-in English name if its localization key is
        /// missing (LocalizationManager echoes the key back in that case, which would put a raw
        /// <c>achievement_x_name</c> on someone's profile).
        /// </summary>
        internal static string? ResolveAchievementTitle(string? achievementId)
        {
            if (string.IsNullOrWhiteSpace(achievementId)) return null;
            if (!Achievement.All.TryGetValue(achievementId!, out var achievement)) return null;

            var localized = achievement.LocalizedName;
            var name = string.IsNullOrWhiteSpace(localized) || localized == $"achievement_{achievement.Id}_name"
                ? achievement.Name
                : localized;

            return App.Mods?.MakeModAware(name) ?? name;
        }

        /// <summary>
        /// Fills the Showcase's four featured slots. The empty ☆ plates step aside as soon as
        /// anything is pinned and come back when the last pin is removed — that toggle lives here
        /// as well as in UpdateProfileShowcase because the two run in either order depending on
        /// which render path got there first.
        /// </summary>
        private void ApplyProfilePins(List<string> pinnedIds)
        {
            try
            {
                var showcase = DiscordTab?.ProfilePinnedShowcase;
                if (showcase == null) return;

                var items = new List<object>();
                foreach (var id in pinnedIds ?? new List<string>())
                {
                    if (!Achievement.All.TryGetValue(id, out var achievement)) continue;
                    var image = LoadAchievementImage(achievement.ImageName);
                    if (image == null) continue;   // art missing => the slot simply is not shown
                    items.Add(new
                    {
                        Name = ResolveAchievementTitle(id) ?? achievement.Name,
                        Image = image
                    });
                }

                showcase.ItemsSource = items.Count > 0 ? items : null;

                if (DiscordTab?.ProfilePinnedPlaceholders != null)
                {
                    DiscordTab.ProfilePinnedPlaceholders.Visibility =
                        items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyProfilePins: {E}", ex.Message); }
        }

        // ============================== customize dialog ==============================

        /// <summary>
        /// Opens the customization kit. Only ever edits YOUR loadout — the button lives on the
        /// hero, so a searched profile can be on screen; the dialog therefore always reads and
        /// writes settings, then repaints your own card and pushes the change.
        /// </summary>
        internal void OpenProfileCustomizeDialog()
        {
            try
            {
                var current = CosmeticsCatalog.SanitizeOwn(App.Settings?.Current?.ProfileCosmetics);
                var unlocked = App.Achievements?.Progress?.UnlockedAchievements;

                var dialog = new ProfileCustomizeDialog(current, unlocked) { Owner = this };
                if (dialog.ShowDialog() != true) return;

                var chosen = CosmeticsCatalog.SanitizeOwn(dialog.Result);
                if (App.Settings?.Current != null)
                {
                    App.Settings.Current.ProfileCosmetics = chosen;
                    App.Settings.Save();
                }

                App.Logger?.Information("Profile cosmetics saved: banner={Banner}, accent={Accent}, title={Title}, pins={Pins}",
                    chosen.BannerId ?? "none", chosen.Accent ?? "none", chosen.TitleId ?? "none",
                    chosen.PinnedAchievements.Count);

                // Repaint immediately; the card on screen is the point of the dialog.
                ApplyOwnProfileCosmetics();

                // And push it, so other people see it. Fire-and-forget: a failed sync is not worth
                // blocking the UI over — the next periodic sync carries the same payload.
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        if (App.ProfileSync != null) await App.ProfileSync.SyncProfileAsync();
                    }
                    catch (Exception ex) { App.Logger?.Debug("Cosmetics sync push failed: {E}", ex.Message); }
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "OpenProfileCustomizeDialog failed");
            }
        }
    }
}
