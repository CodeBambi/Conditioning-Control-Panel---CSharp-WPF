// PORTED from ConditioningControlPanel/MainWindow/MainWindow.ProfileWardrobe.cs (168 lines) -
// Phase 3 of the Profile redesign, sorted member by member. ONE of its four members crosses.
//
// WHAT IS REAL HERE: ApplyProfileAvatarDecoration. It is two property writes onto the ported
// CCP.Avalonia/Views/Controls/AdornedAvatar, whose DecorationId / DecorationTransform are real
// StyledProperties and whose CosmeticTransform is the Core model (CCP.Core/Models/
// ProfileCosmetics.cs). It paints NOTHING today - AdornedAvatar's own GetImage returns null until
// the wardrobe art crosses - and that is exactly the WPF contract for a missing PNG: the plain
// avatar renders. Restoring it now means the hat appears the moment the art does, with no second
// call site to remember.
//
// STILL HEAD-SIDE, each with the exact symbol and where it lives today:
//   ApplyProfileCharms     - Services.WardrobeCatalog.GetImage / .Find
//   (and its two fields)     (ConditioningControlPanel/Services/Profile/WardrobeCatalog.cs). It
//                            reads Resources/cosmetics/registry.json and decodes to
//                            System.Windows.Media.ImageSource, so the class cannot move as it
//                            stands - the registry parse is portable, the BitmapImage is not.
//   LayoutProfileCharms    - Services.WardrobeStageGeometry.CharmRect / DefaultCharmAnchors
//                            (ConditioningControlPanel/Services/Profile/WardrobeStageGeometry.cs).
//                            USEFUL FINDING: that file is pure arithmetic with no WPF type in it
//                            at all, it is already unit-tested (Tests/WardrobeGeometryTests), and
//                            it is the one place wardrobe fractions turn into pixels for BOTH the
//                            card and the editor's stage. It is a clean `git mv` into
//                            CCP.Core/Services/Profile/ - blocked only on this layer not owning
//                            CCP.Core/. Move it and LayoutProfileCharms is a straight port
//                            (Canvas.SetLeft/SetTop and a TransformGroup exist on Avalonia).
//   ApplyProfileWardrobe   - the two-line entry point. Restoring it while ApplyProfileCharms is
//                            missing would be a method that silently does half its name.
//
// The controls themselves ARE on this head - ProfileCharmLayer, ProfileCharmSlot1/2 and
// ProfileHeroCard are all in CCP.Avalonia/Views/Tabs/DiscordTabView.axaml - so the charm half is
// blocked on the catalogue and the geometry, not on anything to draw into.
//
// NO CALLER YET: on WPF the entry is ApplyProfileCosmetics (MainShellWindow.ProfileCosmetics.cs),
// which is blocked on Services.CosmeticsCatalog.

using System;
using Avalonia.Controls;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// The decoration worn over the hero avatar. Placement lives in the art plus the wearer's
        /// optional transform; AdornedAvatar turns both into pixels. Writing both properties on
        /// every apply is what makes taking a decoration off really take it off, rather than
        /// leaving the last card's hat on a stranger's face after a search.
        /// </summary>
        internal void ApplyProfileAvatarDecoration(string? decorationId, CosmeticTransform? transform)
        {
            try
            {
                var avatar = ProfilePage?.FindControl<Controls.AdornedAvatar>("ProfileHeroAvatar");
                if (avatar == null) return;
                avatar.DecorationId = decorationId;
                avatar.DecorationTransform = transform;
            }
            catch (Exception ex) { Log.Debug("ApplyProfileAvatarDecoration: {E}", ex.Message); }
        }
    }
}
