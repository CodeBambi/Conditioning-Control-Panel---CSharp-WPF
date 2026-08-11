using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Phase 3 of the Profile redesign ("Wardrobe"): paints the equipped avatar decoration and the
    /// two card charms onto the Trainer Card, honouring the wearer's saved editor transforms
    /// (drag / resize / rotate / flip).
    ///
    /// Same contract as the rest of the cosmetics code - a missing PNG or an unknown id renders the
    /// plain avatar, and every apply writes BOTH states, so taking a decoration off really takes it
    /// off (rather than leaving the last card's hat on a stranger's face after a search).
    ///
    /// Charms live on <c>ProfileCharmLayer</c>, a Canvas overlay: their placement is normalized
    /// card fractions, so they are re-laid out here on every apply AND on card resize, and they can
    /// never contribute to the hero's measured height (the pre-overlay layout let stacked charm
    /// slots grow the card past 380px).
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>The charm loadout currently painted, kept for re-layout on card resize.</summary>
        private List<string>? _appliedCharmIds;
        private Dictionary<string, CosmeticTransform>? _appliedCharmTransforms;
        private bool _charmResizeHooked;

        /// <summary>
        /// Applies a sanitized wardrobe loadout. Called from ApplyProfileCosmetics for both your
        /// own card and a viewed one - the render is identical, only the sanitize rules differ.
        /// </summary>
        private void ApplyProfileWardrobe(ProfileCosmetics cosmetics)
        {
            ApplyProfileAvatarDecoration(cosmetics.AvatarDeco, cosmetics.DecoTransform);
            ApplyProfileCharms(cosmetics.Charms, cosmetics.CharmTransforms);
        }

        /// <summary>
        /// The decoration worn over the 104px hero avatar. Placement lives in the art plus the
        /// wearer's optional transform; AdornedAvatar turns both into pixels.
        /// </summary>
        private void ApplyProfileAvatarDecoration(string? decorationId, CosmeticTransform? transform)
        {
            try
            {
                var avatar = DiscordTab?.ProfileHeroAvatar;
                if (avatar == null) return;
                avatar.DecorationId = decorationId;
                avatar.DecorationTransform = transform;
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyProfileAvatarDecoration: {E}", ex.Message); }
        }

        /// <summary>
        /// The two card charms. A charm whose art is missing simply leaves its slot empty. Sources
        /// and transforms are stored, then placement is computed from the card's current size -
        /// and recomputed whenever the card resizes.
        /// </summary>
        private void ApplyProfileCharms(List<string>? charmIds, Dictionary<string, CosmeticTransform>? transforms)
        {
            try
            {
                if (DiscordTab == null) return;

                _appliedCharmIds = charmIds != null ? new List<string>(charmIds) : null;
                _appliedCharmTransforms = transforms;

                if (!_charmResizeHooked && DiscordTab.ProfileHeroCard != null)
                {
                    _charmResizeHooked = true;
                    DiscordTab.ProfileHeroCard.SizeChanged += (_, _) => LayoutProfileCharms();
                }

                var slots = new[] { DiscordTab.ProfileCharmSlot1, DiscordTab.ProfileCharmSlot2 };
                var ids = _appliedCharmIds ?? new List<string>();

                for (var i = 0; i < slots.Length; i++)
                {
                    var slot = slots[i];
                    if (slot == null) continue;

                    var id = i < ids.Count ? ids[i] : null;
                    var art = WardrobeCatalog.GetImage(id);
                    var item = WardrobeCatalog.Find(id);

                    if (art == null)
                    {
                        slot.Source = null;
                        slot.ToolTip = null;
                        slot.Visibility = Visibility.Collapsed;
                        continue;
                    }

                    slot.Source = art;
                    slot.ToolTip = item?.Name;   // registry names are plain English, not localized
                    slot.Visibility = Visibility.Visible;
                }

                LayoutProfileCharms();
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyProfileCharms: {E}", ex.Message); }
        }

        /// <summary>
        /// Places the visible charm sprites from their normalized transforms and the card's actual
        /// size. Pure geometry - safe to run on every SizeChanged.
        /// </summary>
        private void LayoutProfileCharms()
        {
            try
            {
                var card = DiscordTab?.ProfileHeroCard;
                var layer = DiscordTab?.ProfileCharmLayer;
                if (card == null || layer == null) return;

                var width = card.ActualWidth;
                var height = card.ActualHeight;
                if (width <= 0 || height <= 0) return;

                var slots = new[] { DiscordTab!.ProfileCharmSlot1, DiscordTab.ProfileCharmSlot2 };
                var ids = _appliedCharmIds ?? new List<string>();

                for (var i = 0; i < slots.Length; i++)
                {
                    var slot = slots[i];
                    if (slot == null || slot.Visibility != Visibility.Visible) continue;

                    var id = i < ids.Count ? ids[i] : null;
                    CosmeticTransform? t = null;
                    if (id != null && _appliedCharmTransforms != null)
                        _appliedCharmTransforms.TryGetValue(id, out t);

                    var anchors = WardrobeCatalog.DefaultCharmAnchors;
                    var anchor = i < anchors.Count ? anchors[i] : anchors[0];

                    // Shared with the wardrobe editor's stage (WardrobeStageGeometry.CharmRect),
                    // so the preview and the card cannot drift apart again.
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
                        if (group.CanFreeze) group.Freeze();
                        slot.RenderTransform = group;
                    }
                    else
                    {
                        slot.RenderTransform = null;
                    }
                }
            }
            catch (Exception ex) { App.Logger?.Debug("LayoutProfileCharms: {E}", ex.Message); }
        }
    }
}
