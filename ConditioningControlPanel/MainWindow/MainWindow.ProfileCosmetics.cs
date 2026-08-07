using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
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
            ApplyProfileAvatarPreset(cosmetics.AvatarId);
            ApplyProfileAccent(cosmetics.Accent);
            ApplyProfileTitle(cosmetics.TitleId);
            ApplyProfilePins(cosmetics.PinnedAchievements);
            // Phase 3: the worn decoration and the two card charms (MainWindow.ProfileWardrobe),
            // with the wearer's saved editor transforms.
            ApplyProfileWardrobe(cosmetics);
        }

        /// <summary>
        /// The preset image THIS method last put into the avatar bubble. Lets a later apply tell
        /// "slot holds our preset" apart from "slot holds a real Discord picture" - a real picture
        /// always wins and is never overwritten or cleared from here.
        /// </summary>
        private ImageSource? _appliedPresetAvatar;

        /// <summary>
        /// Preset avatar for the bubble (resolution order: shared Discord picture, then preset,
        /// then blank circle). Every card render sets the bubble's ImageSource from the picture
        /// BEFORE cosmetics are applied, so at this point: a non-null image that is not ours is a
        /// real picture (leave it), and null or our own previous preset means the slot is ours to
        /// fill, swap, or clear.
        /// </summary>
        private void ApplyProfileAvatarPreset(string? avatarId)
        {
            try
            {
                var bubble = DiscordTab?.ProfileViewerAvatar;
                if (bubble == null) return;

                var current = bubble.ImageSource;
                var slotIsOurs = current == null ||
                                 (_appliedPresetAvatar != null && ReferenceEquals(current, _appliedPresetAvatar));
                if (!slotIsOurs) return;

                var art = CosmeticsCatalog.GetAvatarImage(avatarId);
                bubble.ImageSource = art;
                _appliedPresetAvatar = art;
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyProfileAvatarPreset: {E}", ex.Message); }
        }

        /// <summary>
        /// Banner art behind the hero. A null id or an asset that failed to load clears the layer,
        /// revealing the gradient Border the layout keeps painted underneath — so "no banner" and
        /// "broken banner" look identical and neither looks broken.
        ///
        /// Painted as an <see cref="ImageBrush"/> on a Border rather than assigned to an Image:
        /// the hero lives in a StackPanel inside a ScrollViewer, where an unconstrained
        /// UniformToFill Image reports its own aspect as DesiredSize and drives the card height
        /// (most of the banners shipped before this pool stretched the card past a full screen).
        /// A brush paints into the arranged bounds and contributes nothing to measure.
        ///
        /// The vertical alignment is Top, not Center, and the art is authored for it: the twelve
        /// scenes are 2400x620 with every subject in the upper portion and the bottom of the frame
        /// left as plain floor and falloff. The band is always shorter than 620 scales to, so
        /// UniformToFill crops - and Top means it only ever crops the deliberately empty bottom,
        /// never the subject. Flipping this back to Center silently cuts the top off all twelve.
        /// </summary>
        private void ApplyProfileBanner(string? bannerId)
        {
            try
            {
                var layer = DiscordTab?.ProfileHeroBanner;
                if (layer == null) return;

                var art = CosmeticsCatalog.GetBannerImage(bannerId);
                if (art == null)
                {
                    layer.Background = null;
                    return;
                }

                var brush = new ImageBrush(art)
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    // Top-anchored: banner art tends to put its subject in the upper
                    // half, and a center crop on the wide hero band cut it off. The
                    // 2400x620 plates are composed against this anchor - their bottom
                    // third is deliberately plain falloff - so flipping it back to
                    // Center would crop every one of them at both ends.
                    AlignmentY = AlignmentY.Top
                };
                if (brush.CanFreeze) brush.Freeze();
                layer.Background = brush;
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
                    items.Add(new ProfileAchievementTile
                    {
                        Id = id,
                        Name = ResolveAchievementTitle(id) ?? achievement.Name,
                        Image = image
                    });
                }

                showcase.ItemsSource = items.Count > 0 ? items : null;

                if (DiscordTab?.ProfilePinnedPlaceholders != null)
                {
                    // Self-gated: the plates read "Pin your four proudest unlocks here from
                    // Customize", which is nonsense over a stranger's card — and DisplayProfileEntry
                    // renders every searched profile with an empty loadout first, before the
                    // /user/lookup round-trip lands, so an ungated row would flash on every search.
                    DiscordTab.ProfilePinnedPlaceholders.Visibility =
                        PinPlaceholderVisibility(items.Count > 0);
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

                // The wardrobe editor's stage shows your avatar - but only when YOUR card is on
                // screen; a searched profile's picture is not yours to arrange hats on.
                var avatar = _profileViewingSelf ? DiscordTab?.ProfileViewerAvatar?.ImageSource : null;

                // The wardrobe stage mocks the card at ITS aspect ratio: charm placement is stored
                // as a fraction of card width/height, so a preview drawn at any other shape shows a
                // composition no viewer will ever see. Zero (card never arranged) is handled by
                // WardrobeStageGeometry.
                var card = DiscordTab?.ProfileHeroCard;

                var dialog = new ProfileCustomizeDialog(
                    current, unlocked, avatar, card?.ActualWidth ?? 0, card?.ActualHeight ?? 0)
                { Owner = this };
                if (dialog.ShowDialog() != true) return;

                PersistOwnCosmetics(CosmeticsCatalog.SanitizeOwn(dialog.Result));
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "OpenProfileCustomizeDialog failed");
            }
        }

        /// <summary>
        /// The single "this is my loadout now" path: saves to settings, repaints the card and
        /// pushes the change. Used by the Customize dialog and by the Showcase's click-to-pin.
        /// <paramref name="chosen"/> must already be sanitized.
        /// </summary>
        private void PersistOwnCosmetics(ProfileCosmetics chosen)
        {
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.ProfileCosmetics = chosen;
                App.Settings.Save();
            }

            App.Logger?.Information(
                "Profile cosmetics saved: banner={Banner}, accent={Accent}, title={Title}, pins={Pins}, deco={Deco}, charms={Charms}",
                chosen.BannerId ?? "none", chosen.Accent ?? "none", chosen.TitleId ?? "none",
                chosen.PinnedAchievements.Count, chosen.AvatarDeco ?? "none", chosen.Charms.Count);

            // Repaint immediately; the card on screen is the point.
            ApplyOwnProfileCosmetics();

            // An empty loadout normally rides as null ("no change") so a fresh machine cannot
            // wipe the account before it has read it — but an explicit save of an empty loadout
            // IS the unequip-everything gesture, so flag this one push as an explicit clear.
            if (chosen.IsEmpty && App.ProfileSync != null) App.ProfileSync.PendingCosmeticsClear = true;

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

        // ============================== click-to-pin ==============================

        /// <summary>
        /// Left-click on a Showcase tile (a featured pin, or any achievement in the "All
        /// achievements" grid) toggles that achievement's pin - the quick path beside the
        /// Customize dialog's pin picker. Own card only: the grid is only ever populated for the
        /// viewer's own profile, and the guard makes the rule explicit anyway.
        /// </summary>
        internal void ToggleOwnAchievementPin(string? achievementId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(achievementId)) return;
                if (!_profileViewingSelf) return;

                // Only unlocked achievements are pinnable (same rule as the dialog).
                var unlocked = App.Achievements?.Progress?.UnlockedAchievements;
                if (unlocked == null || !unlocked.Contains(achievementId!)) return;

                var current = CosmeticsCatalog.SanitizeOwn(App.Settings?.Current?.ProfileCosmetics);

                if (current.PinnedAchievements.Contains(achievementId!))
                {
                    current.PinnedAchievements.Remove(achievementId!);
                }
                else
                {
                    if (current.PinnedAchievements.Count >= ProfileCosmetics.MaxPinnedAchievements)
                    {
                        FlashPinCapNotice();
                        return;
                    }
                    current.PinnedAchievements.Add(achievementId!);
                }

                PersistOwnCosmetics(current);
            }
            catch (Exception ex) { App.Logger?.Debug("ToggleOwnAchievementPin: {E}", ex.Message); }
        }

        private DispatcherTimer? _pinCapNoticeTimer;

        /// <summary>
        /// A dead click at the pin cap reads as a broken tile (same lesson as the dialog), so the
        /// unlock-summary line briefly says why before restoring itself.
        /// </summary>
        private void FlashPinCapNotice()
        {
            try
            {
                var summary = DiscordTab?.TxtProfileUnlockSummary;
                if (summary == null) return;

                _pinCapNoticeTimer?.Stop();
                var original = summary.Text;
                var originalBrush = summary.Foreground;

                summary.Text = Localization.Loc.GetF(
                    "profile_customize_pins_full", ProfileCosmetics.MaxPinnedAchievements);
                summary.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5C7A"));

                _pinCapNoticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
                _pinCapNoticeTimer.Tick += (_, _) =>
                {
                    _pinCapNoticeTimer?.Stop();
                    try
                    {
                        summary.Text = original;
                        summary.Foreground = originalBrush;
                    }
                    catch { /* card may have re-rendered meanwhile - its own update wins */ }
                };
                _pinCapNoticeTimer.Start();
            }
            catch (Exception ex) { App.Logger?.Debug("FlashPinCapNotice: {E}", ex.Message); }
        }
    }
}
