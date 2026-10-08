// PORTED from ConditioningControlPanel/MainWindow/MainWindow.ProfileWardrobe.cs (Phase 3 of the
// Profile redesign): the equipped avatar decoration and the two card charms, with the wearer's saved
// editor transforms. Art and ids are Core WardrobeCatalog (decoded by Helpers.ModArt.Wardrobe),
// geometry is Core WardrobeStageGeometry - the same call the editor stage makes.
//
// Entry points differ from WPF only in who calls them: WPF routes through ApplyProfileCosmetics,
// which also paints banner/avatar preset/accent/title/pins via Services.CosmeticsCatalog - still
// WPF-only (see MainShellWindow.ProfileCosmetics.cs). So this head paints the wardrobe half alone:
// ApplyOwnProfileWardrobe / ApplyViewedProfileWardrobe are the wardrobe slice of WPF's
// ApplyOwnProfileCosmetics / ApplyViewedProfileCosmetics, sanitized with the same sets
// CosmeticsCatalog.SanitizeOwn/SanitizeViewed pass for the wardrobe fields.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>The charm loadout currently painted, kept for re-layout on card resize.</summary>
        private List<string>? _appliedCharmIds;
        private Dictionary<string, CosmeticTransform>? _appliedCharmTransforms;
        private bool _charmResizeHooked;

        /// <summary>WPF CosmeticsCatalog.SanitizeOwn, wardrobe fields: registry slots + achievement gates
        /// against YOUR unlocks, plus the Core CosmeticsPool banner/avatar ids, so nothing unknown is saved or pushed.</summary>
        internal static ProfileCosmetics SanitizeOwnWardrobe(ProfileCosmetics? raw)
        {
            var progress = App.Achievements?.Progress?.UnlockedAchievements;
            var unlocked = progress != null ? new HashSet<string>(progress, StringComparer.Ordinal) : null;
            return ProfileCosmetics.Sanitize(raw, CosmeticsPool.BannerIds, new HashSet<string>(Achievement.All.Keys, StringComparer.Ordinal),
                unlocked, WardrobeCatalog.DecoIds(), WardrobeCatalog.CharmIds(), CosmeticsPool.AvatarIds, WardrobeCatalog.AchievementGates());
        }

        /// <summary>Your own loadout, straight from settings (WPF ApplyOwnProfileCosmetics, wardrobe half).</summary>
        internal void ApplyOwnProfileWardrobe()
        {
            try { ApplyProfileWardrobe(SanitizeOwnWardrobe(CoreSettings.Current.ProfileCosmetics)); }
            catch (Exception ex) { Log.Debug("ApplyOwnProfileWardrobe: {E}", ex.Message); }
        }

        /// <summary>Someone else's loadout from /user/lookup, or null to strip the card
        /// (WPF ApplyViewedProfileCosmetics, wardrobe half).</summary>
        internal void ApplyViewedProfileWardrobe(ProfileCosmetics? cosmetics)
        {
            try
            {
                ApplyProfileWardrobe(ProfileCosmetics.Sanitize(cosmetics, null,
                    new HashSet<string>(Achievement.All.Keys, StringComparer.Ordinal), null,
                    WardrobeCatalog.DecoIds(), WardrobeCatalog.CharmIds()));
            }
            catch (Exception ex) { Log.Debug("ApplyViewedProfileWardrobe: {E}", ex.Message); }
        }

        /// <summary>Applies a sanitized wardrobe loadout - identical for your card and a viewed one.</summary>
        private void ApplyProfileWardrobe(ProfileCosmetics cosmetics)
        {
            ApplyProfileAvatarDecoration(cosmetics.AvatarDeco, cosmetics.DecoTransform);
            ApplyProfileCharms(cosmetics.Charms, cosmetics.CharmTransforms);
        }

        /// <summary>
        /// The decoration worn over the hero avatar. Writing both properties on every apply is what
        /// makes taking a decoration off really take it off.
        /// </summary>
        internal void ApplyProfileAvatarDecoration(string? decorationId, CosmeticTransform? transform)
        {
            try
            {
                var avatar = ProfilePage?.ProfileHeroAvatar;
                if (avatar == null) return;
                avatar.DecorationId = decorationId;
                avatar.DecorationTransform = transform;
            }
            catch (Exception ex) { Log.Debug("ApplyProfileAvatarDecoration: {E}", ex.Message); }
        }

        /// <summary>The two card charms. A charm whose art is missing leaves its slot empty.</summary>
        private void ApplyProfileCharms(List<string>? charmIds, Dictionary<string, CosmeticTransform>? transforms)
        {
            try
            {
                var page = ProfilePage;
                if (page == null) return;

                _appliedCharmIds = charmIds != null ? new List<string>(charmIds) : null;
                _appliedCharmTransforms = transforms;

                if (!_charmResizeHooked)
                {
                    _charmResizeHooked = true;
                    page.ProfileHeroCard.SizeChanged += (_, _) => LayoutProfileCharms();
                }

                var slots = new[] { page.ProfileCharmSlot1, page.ProfileCharmSlot2 };
                var ids = _appliedCharmIds ?? new List<string>();
                for (var i = 0; i < slots.Length; i++)
                {
                    var slot = slots[i];
                    var id = i < ids.Count ? ids[i] : null;
                    var art = Helpers.ModArt.Wardrobe(id);
                    slot.Source = art;
                    // Registry names are plain English, not localized.
                    ToolTip.SetTip(slot, art == null ? null : WardrobeCatalog.Find(id)?.Name);
                    slot.IsVisible = art != null;
                }

                LayoutProfileCharms();
            }
            catch (Exception ex) { Log.Debug("ApplyProfileCharms: {E}", ex.Message); }
        }

        /// <summary>Places the visible charms from their normalized transforms and the card's size.</summary>
        private void LayoutProfileCharms()
        {
            try
            {
                var page = ProfilePage;
                if (page == null) return;
                var width = page.ProfileHeroCard.Bounds.Width;
                var height = page.ProfileHeroCard.Bounds.Height;
                if (width <= 0 || height <= 0) return;

                var slots = new[] { page.ProfileCharmSlot1, page.ProfileCharmSlot2 };
                var ids = _appliedCharmIds ?? new List<string>();
                for (var i = 0; i < slots.Length; i++)
                {
                    var slot = slots[i];
                    if (!slot.IsVisible) continue;

                    var id = i < ids.Count ? ids[i] : null;
                    CosmeticTransform? t = null;
                    if (id != null) _appliedCharmTransforms?.TryGetValue(id, out t);

                    var anchors = WardrobeCatalog.DefaultCharmAnchors;
                    var anchor = i < anchors.Count ? anchors[i] : anchors[0];
                    var (left, top, size) = WardrobeStageGeometry.CharmRect(
                        width, height, t?.X ?? anchor.X, t?.Y ?? anchor.Y, t?.Scale ?? 0.8);

                    slot.Width = size;
                    slot.Height = size;
                    Canvas.SetLeft(slot, left);
                    Canvas.SetTop(slot, top);

                    if (t != null && (t.Flip || Math.Abs(t.Rotation) > 0.05))
                    {
                        var group = new TransformGroup();
                        group.Children.Add(new ScaleTransform(t.Flip ? -1 : 1, 1));
                        group.Children.Add(new RotateTransform(t.Rotation));
                        slot.RenderTransform = group;
                    }
                    else slot.RenderTransform = null;
                }
            }
            catch (Exception ex) { Log.Debug("LayoutProfileCharms: {E}", ex.Message); }
        }

        /// <summary>
        /// WPF OpenProfileCustomizeDialog: edits YOUR loadout only, then <see cref="PersistOwnCosmetics"/>.
        /// </summary>
        internal async void OpenProfileCustomizeDialog()
        {
            try
            {
                var page = ProfilePage;
                var current = SanitizeOwnWardrobe(CoreSettings.Current.ProfileCosmetics);
                // Declaration order, as WPF (Achievement.All.Values filtered by your unlocks).
                var unlockedSet = new HashSet<string>(App.Achievements?.Progress?.UnlockedAchievements ?? new(), StringComparer.Ordinal);
                var unlocked = Achievement.All.Values
                    .Where(a => unlockedSet.Contains(a.Id))
                    .Select(a => (a.Id, CoreMods.MakeModAware(a.TitleName))); // WPF ResolveAchievementTitle
                // The stage shows your avatar only while YOUR card is on screen.
                var avatar = _profileViewingSelf ? page?.ProfileHeroAvatar.AvatarBrush.Source : null;
                var card = page?.ProfileHeroCard.Bounds;

                var dialog = new ProfileCustomizeDialog(current, unlocked, avatar, card?.Width ?? 0, card?.Height ?? 0);
                if (await dialog.ShowDialogSafe<bool>(this) != true) return;

                PersistOwnCosmetics(SanitizeOwnWardrobe(dialog.Result));
            }
            catch (Exception ex) { Log.Error(ex, "OpenProfileCustomizeDialog failed"); }
        }

        /// <summary>WPF PersistOwnCosmetics: saves, repaints your card and pushes the loadout so other people see
        /// it. <paramref name="chosen"/> must already be sanitized. The push carries it until one succeeds (an empty
        /// loadout is WPF's explicit unequip-everything clear); fire-and-forget, as WPF.</summary>
        internal void PersistOwnCosmetics(ProfileCosmetics chosen)
        {
            CoreSettings.Current.ProfileCosmetics = chosen;
            CoreSettings.Save();
            Log.Information(
                "Profile cosmetics saved: banner={Banner}, accent={Accent}, title={Title}, pins={Pins}, deco={Deco}, charms={Charms}",
                chosen.BannerId ?? "none", chosen.Accent ?? "none", chosen.TitleId ?? "none",
                chosen.PinnedAchievements.Count, chosen.AvatarDeco ?? "none", chosen.Charms.Count);
            ApplyOwnProfileWardrobe();
            if (Platform.AccountSeed.Sync is { } sync) _ = sync.PushCosmeticsAsync(chosen);
        }
    }
}
