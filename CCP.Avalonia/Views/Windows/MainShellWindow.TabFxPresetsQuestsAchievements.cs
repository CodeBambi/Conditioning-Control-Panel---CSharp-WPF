// PORTED-AS-A-NOTE from ConditioningControlPanel/MainWindow/MainWindow.TabFxPresetsQuestsAchievements.cs
// (834 lines) - PR-3a of the FX overhaul: one focal pass each for Presets, Quests and Achievements.
//
// NOTHING IS RESTORED HERE. The blanket "every member reaches App.*, a service, a device, a
// WebView2 or Win32" header that used to sit here was untrue of this file - almost none of it
// touches a service - so it is replaced with what actually blocks each group. The honest summary:
// this file is nearly all PER-ITEM decoration attached by whoever BUILDS the item, and none of
// those builders exist here yet. Restoring the attach methods now would give six public entry
// points no code path can reach, on three tabs, with no render able to say so.
//
// Two corrections to earlier notes, both found by reading rather than by grepping for App.:
//   * ADORNERS ARE NOT THE BLOCKER. Avalonia has AdornerLayer with the same
//     GetAdornerLayer/SetAdornedElement shape and this head already uses it
//     (CCP.Avalonia/Controls/TierFxBorder.cs:192-226 attaches and removes one exactly as
//     CardSheenAdorner and RowSweepAdorner would). Missing are those two WPF controls themselves,
//     plus their hosts.
//   * PresetsTabView and QuestsTabView now call InitializeComponent(), not
//     AvaloniaXamlLoader.Load - MainShellWindow.TabNavigation.cs's header still lists them among
//     the views whose x:Name fields are permanently null, and that half of it is stale.
//     AchievementsTabView still loads with AvaloniaXamlLoader, so ITS named controls must be
//     reached with FindControl.
//
// Group by group (62 members):
//
//   * the three tab entry points - On{Presets,Quests,Achievements}TabVisibilityChanged,
//     IsIncomingTab, HookTabFxWindowEvents, TabFx_WindowStateish, TabFx_ModChanged,
//     TabFxAmbientAllowed and the five guard flags. WPF's caller is each tab's IsVisibleChanged;
//     here the equivalent is one line per tab in EnsureTabFx (MainShellWindow.AmbientFx.cs) or the
//     PropertyChanged-on-IsVisibleProperty filter .TabFxTakeoverLabStatus.cs already uses -
//     neither file is this layer's. TabFxAmbientAllowed is that partial's Pr4aAmbientAllowed;
//     reuse it, do not define a second gate.
//   * Presets, the tab's only ambient clock - the three _cardSheen* fields, RefreshCardSheen,
//     DetachCardSheen, FindSelectedCard, InitializePresetsFx, StaggerPresetCards. Needs
//     Controls/CardSheenAdorner.cs ported, and needs to know which preset card is selected -
//     MainWindow.Presets.cs's list, and MainShellWindow.Presets.cs is still a stub.
//   * the session rack rows - the three _rowSweep* fields, PrepareSessionRowFx,
//     SessionRow_MouseEnter/MouseLeave, Attach/Release/DetachRowSweep, LiftSessionRow,
//     SessionRowLiftPx, SessionCardCornerRadius. Attached by
//     MainWindow.SessionIO.BuildSessionRackRow as each row is built; nothing builds rack rows
//     here. Needs Controls/RowSweepAdorner.cs too.
//   * per-card / per-tile hover - PreparePresetCardFx, PresetCard_MouseEnter/MouseLeave,
//     PrepareAchievementTileFx, SetAchievementTileUnlocked, TiltTargetFor,
//     AchievementTile_MouseEnter/MouseLeave, TiltAchievementTile, StaggerAchievementTiles, the
//     three achievement dictionaries and four tuning constants. Same shape: called by the builder,
//     and the builders are MainShellWindow.Presets.cs / .AchievementsTab.cs. In Avalonia these are
//     pointer-over transitions on the item's own ControlTheme far more cheaply than as C# per-item
//     hooks - prefer that when the builders land, over a literal transcription.
//   * the button press squish - WireButtonPress, FxButton_PressDown/PressUp, PressFx,
//     EnsureCardTransforms, FindTransform<T>. WPF reaches it through Behaviors/MotionFx.cs; this
//     head has none, and a :pressed setter in Theme/Styles.xaml is the Avalonia answer (off-limits
//     to this layer, as it was to the WPF PR).
//   * Quests' weekly bar - _weeklyQuestFraction, _questTracksHooked, InitializeQuestsFx,
//     QuestTrack_SizeChanged, SetQuestProgress, ApplyQuestProgressBars. THE CONTROLS EXIST
//     (QuestsTabView.axaml:410 names WeeklyProgressTrack and WeeklyProgressFill) and the body is a
//     width tween with a no-tween re-seat on resize, which ports directly. What is missing is the
//     FRACTION: SetQuestProgress is called from RefreshQuestUI, and MainShellWindow.Quests.cs is a
//     stub blocked on the quest service. A bar seeded to any fraction we can compute here would
//     report progress nobody measured, so it stays at its authored width.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Nothing references this partial, and nothing is restored - see the header.
    }
}
