// NOT PORTED from ConditioningControlPanel/MainWindow/MainWindow.ProgramsTab.cs (2312 lines).
// THE OLD BLOCKER IS GONE. This header used to say the program model itself was still in the WPF
// head; it is not. ProgramDefinition, ProgramDay, ProgramEnrollment, ProgramService,
// ProgramSessionBuilder and the whole BuiltInPrograms library are in CCP.Core (Models/Program/ and
// Services/Program/), and this head references them. Only ProgramArt (WPF ImageSource) and
// ProgramRewardService (App.MainWindowRef, the sessions list) stayed behind.
//
// What blocks this file now is smaller and dumber: NOTHING ON THIS HEAD CONSTRUCTS A ProgramService.
// CCP.Avalonia/App.axaml.cs builds SettingsService and nothing else, so there is no instance to
// read Today off, no events to subscribe to and no ledger to command. Constructing one here before
// the builders below exist would start a day clock and write programs.json for a tab that cannot
// show the run - which is why it is deliberately not done yet. The instance and the builders are
// one job, not two.
//
// What that leaves, grouped so the eventual port can be taken in bites:
//
//   THE BUILDERS - RebuildProgramsTab, BuildProgramBrowseList, BuildProgramRunPanel,
//   BuildProgramDayStrip, BuildProgramTodayPanel, BuildProgramTodayLayers, BuildProgramUpNext,
//   BuildProgramLapsedPanel, BuildProgramGraduatedPanel, RefreshProgramsUI, RefreshProgramTodayCard,
//   UpdateProgramSessionRow, ProgramTodayCard_Loaded, ProgramTodayCard_Click. Code-built panels over
//   ProgramService state; they also want the tab's own ControlThemes, which is a second job.
//
//   THE COMMANDS - BtnProgramEnroll_Click, BtnProgramPauseResume_Click, BtnProgramWithdraw_Click,
//   BtnProgramRestart_Click, BtnProgramDismissGraduated_Click, BtnProgramSubmitRitual_Click,
//   BtnStartTodaySession_Click, StartProgramSession, AnnounceProgramSessionStarted /
//   AnnounceProgramSessionEnded. Every one of these now HAS a service to call - what they lack is
//   an instance and a run panel to switch to. BtnProgramEnroll_Click is the one with a lie-risk:
//   its gate is ProgramService.CanEnroll(def, out reason) BEFORE ProgramEnrollDialog opens, and
//   Enroll(...) only on an awaited true. Enrolling with the run panel still unbuilt would leave the
//   tab on the browse list while a real run ticked underneath it.
//
//   THE SUBSCRIPTIONS - EnsureProgramsSubscribed, EnsureProgramsAppHooks, OnProgramTodayChanged,
//   OnProgramLapsed, OnProgramGraduated, MarshalProgramRefresh, _programsSubscribed,
//   _programsAppHooked. Events on ProgramService; MarshalProgramRefresh's WPF Dispatcher.Invoke is
//   Dispatcher.UIThread.Post here.
//
//   THE PAINT HELPERS - ProgramThemeBrush, ProgramAccentBrush, ProgramContrastForeground,
//   SrgbToLinear, ProgramRailFillBrush, ProgramRadialGlowBrush, ApplyProgramArtMask,
//   ProgramTaskIconPath, ProgramTaskHowTo, ProgramRunKey, FormatProgramClock, ProgramDaySettings,
//   ProgramHasPremium. Two of these - SrgbToLinear (the sRGB->linear curve behind the WCAG
//   luminance pick) and FormatProgramClock - are pure and portable TODAY. They are left out because
//   they are orphans: every caller is a builder above, and a lone contrast helper with nothing to
//   contrast is padding, not a port. ProgramRailFillBrush and ProgramRadialGlowBrush additionally
//   call Freeze(), which Avalonia has no equivalent for; ProgramHasPremium is App.Patreon;
//   ProgramDaySettings is ProgramSessionBuilder, which is in Core now.
//
//   THE FX - EnsureProgramsFxHooked, OnProgramsTabVisibleChanged, StartProgramRunEntrance,
//   AnimateProgramPanelIn, EnsureProgramSessionSheen, StopProgramSessionSheen, PopProgramScale,
//   StartProgramsTabPulse, StopProgramsTabPulse, ResetProgramRunPopsIfRunChanged and their seven
//   state flags. WPF storyboards; the keyframe-Animation recipe in CLAUDE.md covers the shapes, but
//   there is nothing on screen to animate until the builders land.
//
// Checked and NOT the blocker here: CoreProgression and CoreSession. Programs are their own
// enrollment ledger - CoreProgression answers XP and level, CoreSession answers the running
// session, and no member of this file reads either. Do not wire them expecting the tab to fill.
//
// No member of this partial is referenced from MainShellWindow.axaml. The rail's Programs button is
// BtnPrograms_Click, which already ships in MainShellWindow.TabNavigation.cs:205.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
    }
}
