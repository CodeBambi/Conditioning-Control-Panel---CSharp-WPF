// NOT PORTED from ConditioningControlPanel/MainWindow/MainWindow.ProgramBanner.cs (495 lines) -
// the dashboard Today card's decoration: banner art, accent-derived fog and sheen, sparkles, an
// ambient loop and a hover parallax. Two blockers, only one of them a missing service.
//
// 1. THE ENTRY POINT NAMES TYPES THIS HEAD DOES NOT HAVE.
//    ApplyProgramBannerArt(ProgramDefinition?, Brush?) is the only door in, and ProgramDefinition
//    is ConditioningControlPanel/Models/Program/ProgramDefinition.cs - it did NOT move to Core. So
//    did its art resolver: ResolveProgramBannerArt is one line, `ProgramArt.Banner(program)`, and
//    ProgramArt (ConditioningControlPanel/Services/Program/ProgramArt.cs) returns a WPF ImageSource
//    from a pack:// URI. There is no caller here either - the card is refreshed by
//    MainWindow.ProgramsTab / .DashboardFx, also stubs. ProgramBannerAccentColor's fallback,
//    FxTheme.GlowColor (ConditioningControlPanel/Services/FxTheme.cs), has an equivalent here in
//    the FxGlowColor resource of CCP.Avalonia/Theme/Colors.xaml.
//
// 2. THE REST IS WPF STORYBOARD MOTION WITH NO EQUIVALENT WRITTEN HERE. A still card that reads
//    as finished beats a moving one that is wrong, so these are named rather than faked:
//      StartProgramBannerFx / StopProgramBannerFx - two looping DoubleAnimations (fog drift) and a
//        repeating sheen sweep, started and stopped by hand. Avalonia's twin is a keyframe
//        Animation with IterationCount.Infinite: a real port, not a mapping.
//      BuildProgramBannerSparkles / PositionProgramBannerSparkles / StartProgramBannerSparkles -
//        ellipses added to the ProgramTodaySparkles Canvas at fractions of the card width, each on
//        its own staggered twinkle clock.
//      ProgramBannerCard_MouseEnter / _MouseLeave / NudgeProgramBannerArt - the hover parallax. The
//        art overhangs the card by 14px each side (that negative margin is
//        CCP.Avalonia/Views/Tabs/SettingsTabView.axaml:533) and slides within it. Avalonia's events
//        are PointerEntered/PointerExited; the slide is the storyboard part.
//      const ProgramBannerParallax / ProgramBannerArtOpacity, _programBannerFxRunning,
//      _programBannerSparklesBuilt.
//
// NOT A BLOCKER, so nobody re-derives it: the MARKUP is all here and correct. SettingsTabView.axaml
// carries ProgramTodayDecor, ProgramTodayArt, ProgramTodayFog, ProgramTodaySheen and the
// ProgramTodaySparkles Canvas, each collapsed until this file paints it - which is why the card
// renders today as a finished, undecorated card rather than a hole. The brush builders
// (ProgramBannerFogBrush, ProgramBannerSheenBrush) and the colour helpers (WithAlpha, TowardWhite)
// are pure gradient/colour arithmetic and would port as-is; they are just not worth restoring with
// nothing to hand them to.
//
// Remaining members: const ProgramBannerFolder (Resources/programs/<id>/banner.png, resolved by
// ProgramArt so it goes with it), _programBannerHooked, EnsureProgramBannerHooks (one-shot hookup
// of the card's four events), ProgramBannerCard_IsVisibleChanged (starts/stops the loop with the
// card), ProgramBannerDecor_SizeChanged / LayoutProgramBannerDecor (sizes fog and sheen to the card).

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Nothing here on purpose. The decoration markup is in
        // CCP.Avalonia/Views/Tabs/SettingsTabView.axaml and stays collapsed until a
        // ProgramDefinition-shaped seam and an Avalonia animation port exist.
    }
}
