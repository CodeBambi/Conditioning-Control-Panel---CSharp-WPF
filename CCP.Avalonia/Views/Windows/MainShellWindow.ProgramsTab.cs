// NOT PORTED from ConditioningControlPanel/MainWindow/MainWindow.ProgramsTab.cs (2312 lines).
// Sorted member by member against the fifteen Core seams; unlike its neighbours the blanket claim
// survives here, but for a reason the old header did not give and that is worth writing down so
// nobody re-checks it: THE PROGRAM MODEL ITSELF IS STILL IN THE HEAD.
//
//   ProgramDefinition, ProgramDay, ProgramEnrollment   ConditioningControlPanel/Models/Program/
//   ProgramService, ProgramSessionBuilder, ProgramArt  ConditioningControlPanel/Services/Program/
//
// Nothing in CCP.Core names any of them. That is a stronger blocker than a missing seam: not one
// member of this file can be written at all, because there is no type here to write it against.
// Check with `grep -rl "ProgramEnrollment\|ProgramDefinition" CCP.Core` before assuming otherwise -
// when that grep starts hitting, most of this file becomes a straight transcription.
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
//   AnnounceProgramSessionEnded. ProgramService plus SessionEngine. None of these is a lie-risk -
//   they simply have no service to command.
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
//   ProgramDaySettings is ProgramSessionBuilder.
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
