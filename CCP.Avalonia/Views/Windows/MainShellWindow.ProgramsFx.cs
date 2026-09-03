// PORTED-AS-A-NOTE from ConditioningControlPanel/MainWindow/MainWindow.ProgramsFx.cs (1,210 lines)
// - the Programs tab's "ignition" rig: the Today band gets warmer, brighter and busier as a run
// gets deeper, and fires one-shot moments on a day completing, a chapter sealing and a graduation.
//
// NOTHING IS RESTORED HERE, and for once the reason is not Avalonia. EVERY effect in this file is
// a FUNCTION OF RUN STATE that does not exist on this head:
//
//     ComputeProgramHeat(ProgramDefinition, ProgramEnrollment, ProgramDay?)
//         -> ProgramHeat.Compute(currentDay, lengthDays, day.Intensity, isBoss)
//         -> _programHeat / _programTier / _programBossToday
//         -> the wash alpha, the sigil breath depth, the border rotation, the rail glow, the comet
//            budget and ProgramHeat.ParticleCount().
//
// Models.ProgramDefinition / ProgramEnrollment / ProgramDay and Services ProgramHeat are all still
// in the WPF head, and MainWindow.ProgramsTab.cs - the only caller of ApplyProgramIgnition,
// NoteProgramDayCompletion, NoteProgramChapterSeal and CelebrateProgramGraduation - is not ported
// either. With no heat there is no tier, and every knob collapses to "cold".
//
// THAT IS WHY THERE IS NO EnsureProgramsFx() HERE, though it looks like the one obvious win.
// ProgramsTabView.axaml carries an empty <Grid x:Name="TodayFxLayer"/> waiting for exactly the
// AmbientFxCanvas that MainShellWindow.EnhancementsFx.cs composes for the skill tree - but WPF
// adds that canvas only from ApplyProgramParticles, and only when
// ProgramHeat.ParticleCount(_programHeat) > 0, i.e. at Charged and above. Composing it
// unconditionally would put dust over a cold band the Windows app leaves clean: a deviation
// dressed as a port, and one a render proof would happily pass. When the program service lands the
// canvas goes in TodayFxLayer, RegisterTabFx("programs", canvas) (idempotent) hooks it into the
// tab-switch park/resume governor, and one `case "programs": EnsureProgramsFx(); break;` goes in
// EnsureTabFx (MainShellWindow.AmbientFx.cs), which this layer does not own.
//
// What each group needs, so the next pass does not have to re-derive it (60 members):
//
//   * the run state, listed above - the eleven _program* run fields, ComputeProgramHeat,
//     ApplyProgramIgnition, BuildProgramIgnitionScenery, NoteProgramDayCompletion,
//     PlayProgramDayCompleteMoment, CelebrateProgramTaskCompletions, NoteProgramChapterSeal,
//     PlayProgramChapterSeal, CelebrateProgramGraduation.
//   * Services/MotionFx.cs + Services/PerformanceProfile.cs (not in Core): the ambient gate and
//     the particle budget - ApplyProgramParticles, EnsureProgramParticleLayer,
//     ApplyProgramRailComet, _programCometAttempts, _programSheenSeconds.
//   * Services/FxTheme.cs's accent (ProgramAccentColor, ProgramRunAccent, ProgramAccentColorNow,
//     _programAccent). CoreMods answers the mod accent (HapticsSetupWindow.AccentFor is the
//     precedent), so this group is cheapest to unblock and least useful alone: an accent with no
//     heat repaints nothing that moves.
//   * WPF drawing blocked only by the two groups above, NOT by Avalonia: the nine cached
//     brush/transform/effect fields (a LinearGradientBrush turned by its own RelativeTransform
//     ports as-is - Avalonia brushes take a Transform; DropShadowEffect exists here), plus
//     ApplyProgramSigilBreath, ApplyProgramBorderRotation, ApplyProgramWashBloom,
//     ApplyProgramRailGlow, ApplyProgramCounterShimmer, ApplyProgramEdgeGlow,
//     ApplyProgramBossFlare, EnsureProgramWindowFxHooked, StopProgramIgnitionLoops and the six
//     constants/colour helpers.
//
// Two rules for whoever restores it: park the loops through the shared gate
// (MainShellWindow.TabFxTakeoverLabStatus.cs:Pr4aAmbientAllowed), not a second Activated
// subscription; and cancel every keyframe Animation through a CTS, as every other loop here does.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Nothing references this partial, and nothing is restored - see the header.
    }
}
