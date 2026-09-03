// NOT PORTED from ConditioningControlPanel/MainWindow/MainWindow.ProfileFx.cs (239 lines).
//
// Sorted member by member against the fifteen Core seams: GENUINELY 100% head-side. Every one of
// its fourteen members is either a WPF animation clock or a gate on two static services that have
// not moved, and the file has no third concern hiding in it. Recorded as a finding rather than
// padded with token methods.
//
// THE TWO SERVICES, and they are what block eleven of the fourteen:
//   Services.MotionFx  (ConditioningControlPanel/Services/MotionFx.cs) - AllowAmbientLoops,
//        AllowTransitions and StaggerIn, i.e. the reduced-motion and performance-tier gate plus
//        the entrance choreography itself. This head has a partial stand-in, the private `Env`
//        class inside CCP.Avalonia/Controls/AmbientFxCanvas.cs, which carries its own ponytail
//        note saying it IS the missing MotionFx with the reduced-motion half absent. Reaching
//        into it from here would spread a documented placeholder to a second consumer.
//   Services.FxTheme   (ConditioningControlPanel/Services/FxTheme.cs) - GlowColor, the mod's
//        accent that the search-box focus glow animates to. CoreMods.AccentColorHex answers a
//        neighbouring question, not this one: FxTheme resolves a GLOW slot with its own fallback.
//
// THE ANIMATION CLOCKS, which have no direct Avalonia twin:
//   ApplyOgBorderLoop      - gates Begin/Stop on a Storyboard read out of
//                            OgBorderContainer.Resources by key ("OgBorderAnimation"). That
//                            resource does not exist here and cannot: the WPF storyboard spins a
//                            GradientBrush's RelativeTransform, which Avalonia's GradientBrush has
//                            no equivalent of. DiscordTabView.axaml:145 carries the matching note
//                            and draws the static gold frame; a restore is a DispatcherTimer
//                            rotating the stops - a decision, not a copy.
//   ApplyProfileSearchGlow - BeginAnimation(SolidColorBrush.ColorProperty) on a brush the window
//   EnsureProfileSearchBrush owns. Avalonia has no per-object BeginAnimation; the twin is a
//   ProfileSearch_GotFocus   BrushTransition. Cheap to write - but the colour it animates TO is
//   ProfileSearch_LostFocus  FxTheme.GlowColor, so it would be motion toward an invented tint.
//   StaggerProfileCards    - MotionFx.StaggerIn plus EnsureCardTransforms
//                            (MainShellWindow.Animations.cs, still a stub). The three tuning
//                            constants and three state fields exist only to serve these.
//
// THE LIFECYCLE MEMBERS, blocked on their own callees rather than on FX:
//   OnProfileTabVisibilityChanged - UpdateProfileSharingSummary and RefreshProfileShareButton
//                            (MainShellWindow.ProfileBubble.cs, a stub), EnsureProfileMeFirst
//                            (named as head-side in MainShellWindow.ProfileCard.cs's own header),
//                            IsIncomingTab (MainShellWindow.Animations.cs) and
//                            OnProfileVatVisibilityChanged (MainShellWindow.ProfileVat.cs, whose
//                            body needs App.Descent). Five callees, none of them here.
//   InitializeProfileFx / OnProfileFxWindowStateish - Activated/Deactivated/StateChanged hooks
//                            whose only purpose is to re-run ApplyOgBorderLoop and EvaluateVatPoll.
//                            Both callees are blocked, so the hooks would fire into nothing.
//
// The controls this file drives ARE all on this head - ProfileColumnStack, OgBorderContainer,
// ProfileSearchBox and TxtProfileSearch are in CCP.Avalonia/Views/Tabs/DiscordTabView.axaml - so
// what is missing is the motion policy and the two clocks, never a surface to animate.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Deliberately empty - see the header. No member of this partial is referenced from
        // MainShellWindow.axaml.
    }
}
