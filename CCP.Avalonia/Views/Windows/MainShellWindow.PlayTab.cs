// NOT PORTED from ConditioningControlPanel/MainWindow/MainWindow.PlayTab.cs (331 lines).
//
// Sorted member by member against the fifteen Core seams and the ported views: this file is
// GENUINELY 100% head-side, and it is head-side for one reason rather than fifteen. It is the Play
// wall's ENTITLEMENT painter - every line of it decides "is this card locked, free today, or paid
// for", and not one of those answers exists on this head. There is nothing here that is layout,
// navigation or settings, so there is no half to restore; a token method would be a lie about a
// wall that would then draw every card unlocked.
//
// What each member needs, exactly:
//   RefreshPlayCards        - Services.TierGate.RequiresLab / RequiresPremium for eight verdicts,
//                             which IS available now (CCP.Core/Services/TierGate.cs, over the
//                             CoreEntitlement seam) - the rest of the member is not:
//                             MainWindow.PremiumRail.cs's SetLockband / SetLockbandVisible to paint
//                             one; App.Patreon.HasPremiumAccess / HasLabAccess
//                             (ConditioningControlPanel/Services/Account/PatreonService.cs) for the
//                             two Goon perk lines; and the two static door flags
//                             Services.Arcademy.ArcademyHostService.DoorAvailable and
//                             Services.JustDrop.JustDropService.DoorAvailable, which decide whether
//                             those two cards are on the wall at all.
//   RefreshPlayFreeStamps   - App.DailyFree.IsFreeToday. DailyFreeService itself IS in Core
//   SetFreeStamp              (CCP.Core/Services/DailyFreeService.cs), but no head seeds an
//                             instance and there is no CoreDailyFree seam, so nothing can be asked
//                             which door the wheel opened today. The TierBadge.FreeToday property
//                             it writes is already ported (Views/Controls/TierBadge).
//   RefreshPlayIntakeCard   - App.IntakePass.State + IntakePassService.DaysUntilNextPass
//                             (ConditioningControlPanel/Services/Progression/IntakePassService.cs)
//                             for the card's four pass states.
//   StartMantraSession      - App.Mantra.StartSession
//                             (ConditioningControlPanel/Services/MantraService.cs) and the
//                             MantraWindow it opens (ConditioningControlPanel/Windows/
//                             MantraWindow.xaml.cs), neither of which is ported. Note the WPF file's
//                             own header: this helper has had NO CALLER since the 2026-08-12
//                             relayout took the Mantras card off the page, and it is kept there
//                             only because it is the one place that knows the window needs
//                             StartSession(n) to have run before it loads.
//   GoonPerkLockedOpacity   - the 0.42 dim for an unbought Goon perk. A constant with no reader
//                             until RefreshPlayCards has its two entitlement answers.
//
// One correction for whoever wires this: an earlier revision of this header warned that
// Views/Tabs/PlayTabView loads with AvaloniaXamlLoader.Load and so has null x:Name fields. That was
// fixed at the source - PlayTabView's constructor calls InitializeComponent(), so PlayLockDtrh,
// SlotArcademy, TxtPlayGoonPerkSend and the rest are populated and can be used directly. The hazard
// is still real for THIS window, whose partials must reach controls through Named<T>(name).

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Deliberately empty - see the header. No member of this partial is referenced from
        // MainShellWindow.axaml.
    }
}
