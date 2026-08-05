using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Phase 3 of the Profile redesign ("Wardrobe"): paints the equipped avatar decoration and the
    /// two card charms onto the Trainer Card.
    ///
    /// Same contract as the rest of the cosmetics code - a missing PNG or an unknown id renders the
    /// plain avatar, and every apply writes BOTH states, so taking a decoration off really takes it
    /// off (rather than leaving the last card's hat on a stranger's face after a search).
    ///
    /// Charms are deliberately full-card only (DESIGN.md): they are free-floating props with no
    /// avatar-circle constraint, so they have no meaning on a 28px row.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// Applies a sanitized wardrobe loadout. Called from ApplyProfileCosmetics for both your
        /// own card and a viewed one - the render is identical, only the sanitize rules differ.
        /// </summary>
        private void ApplyProfileWardrobe(string? decorationId, List<string>? charmIds)
        {
            ApplyProfileAvatarDecoration(decorationId);
            ApplyProfileCharms(charmIds);
        }

        /// <summary>
        /// The decoration worn over the 104px hero avatar. All of the placement lives in the art
        /// and in AdornedAvatar; here it is just an id.
        /// </summary>
        private void ApplyProfileAvatarDecoration(string? decorationId)
        {
            try
            {
                var avatar = DiscordTab?.ProfileHeroAvatar;
                if (avatar == null) return;
                avatar.DecorationId = decorationId;
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyProfileAvatarDecoration: {E}", ex.Message); }
        }

        /// <summary>
        /// The two corner charms. A charm whose art is missing simply leaves its slot empty, and
        /// the whole row hides when neither slot got anything - an empty 46px gap under the plates
        /// reads as a broken card.
        /// </summary>
        private void ApplyProfileCharms(List<string>? charmIds)
        {
            try
            {
                if (DiscordTab == null) return;

                var slots = new[] { DiscordTab.ProfileCharmSlot1, DiscordTab.ProfileCharmSlot2 };
                var ids = charmIds ?? new List<string>();
                var shown = 0;

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
                    shown++;
                }

                if (DiscordTab.ProfileCharmSlots != null)
                {
                    DiscordTab.ProfileCharmSlots.Visibility =
                        shown > 0 ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyProfileCharms: {E}", ex.Message); }
        }
    }
}
