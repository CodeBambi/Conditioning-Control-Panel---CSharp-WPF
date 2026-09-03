// NOT PORTED from ConditioningControlPanel/MainWindow/MainWindow.SubscribeStar.cs (121 lines).
//
// All four members are one account provider's presentation layer, and there is no provider on this
// head: App.SubscribeStar is ConditioningControlPanel/Services/Account/SubscribeStarService.cs and
// none of the fifteen seams covers an account, an entitlement or a tier. So this is a REFUSAL on
// the safety line, not a "waiting for a model to move" note.
//
//   InitializeSubscribeStarTab   Subscribes to App.SubscribeStar.TierChanged. No event source.
//   OnSubscribeStarTierChanged   The handler for it. Its WPF body then calls UpdatePatreonUI,
//                                UpdateUnlockablesVisibility, RefreshProgramsUI and
//                                MaybeShowPremiumCelebration - four more shell members that are
//                                themselves stubs on this head. (Its Dispatcher.BeginInvoke maps
//                                to Dispatcher.UIThread.Post, which is the only portable line in
//                                the file.)
//   UpdateSubscribeStarUI        THE ONE THAT MUST NOT BE HALF-PORTED. Its inputs are
//                                svc.IsAuthenticated / CurrentTier / IsWhitelisted /
//                                IsActiveSubscriber; with no service every one of them reads
//                                false-or-None, so restoring it would paint the disconnected
//                                branch unconditionally and forever - a card asserting "not
//                                connected" about an account nobody asked. The card already says
//                                that HONESTLY and statically: AccountSettingsSection.axaml:105-115
//                                binds label_not_connected and
//                                label_login_to_unlock_exclusive_features with {loc:Str}. Writing
//                                .Text over those bindings would also drop them on the next
//                                language change (the standing Avalonia trap), trading a correct
//                                localized card for a stale one.
//   BtnSubscribeStarLogin_Click  A login button. Needs App.SubscribeStar.Logout, App.Patreon /
//                                App.Discord for the "last provider out tears the account down"
//                                test, ClearAccountData, and OpenUnifiedLoginDialog - the unified
//                                login dialog, which is not ported. The handler exists as an empty
//                                stub where the card lives
//                                (Views/Controls/AppSettings/AccountSettingsSection.axaml.cs:27),
//                                so the button is inert rather than wrong.
//
// What would unblock it: an account seam over IsAuthenticated / CurrentTier / IsWhitelisted /
// IsActiveSubscriber / DisplayName plus the TierChanged event, seeded per head. That is a Core
// layer, not this one.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Deliberately empty - see the header. No member of this partial is referenced from
        // MainShellWindow.axaml.
    }
}
