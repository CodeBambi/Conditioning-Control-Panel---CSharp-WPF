// PORTED from ConditioningControlPanel/MainWindow/MainWindow.ProfileCosmetics.cs (532 lines) -
// the Trainer Card's cosmetics painter, sorted member by member. TWO of its twenty members cross,
// and both are rules rather than paint.
//
// ONE SERVICE BLOCKS SEVENTEEN OF THE OTHERS: Services.CosmeticsCatalog
// (ConditioningControlPanel/Services/Profile/CosmeticsCatalog.cs). It owns SanitizeOwn,
// SanitizeViewed, GetAvatarImage, GetBannerImage and TryGetAccentColor - both what a loadout is
// ALLOWED to contain and what it looks like. The sanitize half is portable logic; the four
// image/colour members decode to System.Windows.Media types, so the class cannot move as it
// stands - it splits, or it grows a seam. Until then ApplyOwnProfileCosmetics,
// ApplyViewedProfileCosmetics, ApplyProfileCosmetics, ApplyProfileAvatarPreset,
// ApplyProfileBanner, ApplyProfileAccent, ApplyProfilePins, RefreshShowcasePinArt,
// OpenProfileCustomizeDialog, PersistOwnCosmetics and ToggleOwnAchievementPin are all blocked at
// the door. Models.ProfileCosmetics itself IS in Core, so the model is not what is missing.
//
// THE REST, each with the exact symbol and where it lives today:
//   ApplyProfileTitle          - Models.Achievement.All / .LocalizedName
//   ResolveAchievementTitle      (ConditioningControlPanel/Models/Achievement.cs). CoreMods
//                                .MakeModAware answers the mod-aware half; the roster does not
//                                exist here, so every id would resolve to null.
//   OpenProfileCustomizeDialog - Dialogs/ProfileCustomizeDialog (WPF head) plus
//                                App.Achievements.Progress.UnlockedAchievements.
//   PersistOwnCosmetics        - the settings write IS reachable, but its point is the push that
//                                follows: App.ProfileSync.PendingCosmeticsClear / SyncProfileAsync
//                                (…/Services/Profile/ProfileSyncService.cs). Saving a loadout the
//                                server is never told about is the half that looks like it worked.
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
        // Deliberately empty - see the header. No member of this partial is referenced from
        // MainShellWindow.axaml.
    }
}
