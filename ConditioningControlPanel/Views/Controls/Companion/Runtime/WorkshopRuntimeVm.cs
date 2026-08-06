using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion.Runtime
{
    /// <summary>
    /// The seven re-parented legacy controls, held together so the tab has one accessor for the
    /// x:Names its MainWindow partials still write to.
    ///
    /// <para>Each cell is a real UserControl containing the actual moved markup — not a copy. The
    /// old <c>CompanionTabView.xaml</c> no longer declares any of them, so there is exactly one
    /// instance of every control and exactly one thing MainWindow can be writing to.</para>
    /// </summary>
    internal sealed class WorkshopShelfParts
    {
        public WorkshopRosterCell Roster { get; } = new();
        public WorkshopBehaviorCell Behavior { get; } = new();
        public WorkshopTriggersCell Triggers { get; } = new();
        public WorkshopLibraryCell Library { get; } = new();
        public WorkshopCommunityCell Community { get; } = new();
        public WorkshopAwarenessCell Awareness { get; } = new();
    }

    /// <summary>
    /// Z8 — The Workshop, carrying the real controls.
    ///
    /// <para>"nothing was deleted. it just stopped being the front door." Each cell hands the drawer
    /// a live control through <see cref="IWorkshopCellVm.Content"/>; the scaffold rows the design-time
    /// mock supplies are what the preview harness keeps rendering, and the two never appear
    /// together.</para>
    ///
    /// <para><b>Anchor vs heading.</b> <see cref="IWorkshopCellVm.Key"/> is the deep-link identity
    /// (<see cref="CompanionRoomAnchors"/>) and <c>Title</c> is the localizable heading. They were
    /// one string before this pass, which meant translating a Workshop heading would have silently
    /// broken the hero's Switch chip and Z5's "fine-tuning ↓" — a class of bug that produces no
    /// error, just an affordance that stops working in German.</para>
    /// </summary>
    internal sealed class WorkshopRuntimeVm : CompanionObservable, IWorkshopAccordionVm
    {
        private readonly CompanionRuntimeContext _ctx;
        private bool _isExpanded;

        public WorkshopRuntimeVm(CompanionRuntimeContext ctx, WorkshopShelfParts parts)
        {
            _ctx = ctx;
            Parts = parts;

            Cells = new IWorkshopCellVm[]
            {
                Cell(CompanionRoomAnchors.WorkshopRosterCell, "companion_workshop_cell_roster", parts.Roster),
                Cell(CompanionRoomAnchors.WorkshopBehaviorCell, "companion_workshop_cell_behavior", parts.Behavior),
                Cell(CompanionRoomAnchors.WorkshopTriggersCell, "companion_workshop_cell_triggers", parts.Triggers),
                Cell(CompanionRoomAnchors.WorkshopLibraryCell, "companion_workshop_cell_library", parts.Library),
                Cell(CompanionRoomAnchors.WorkshopCommunityCell, "companion_workshop_cell_community", parts.Community),
                Cell(CompanionRoomAnchors.WorkshopAwarenessCell, "companion_workshop_cell_awareness", parts.Awareness)
            };

            FocusCellCommand = new CompanionRelayCommand(
                p => _ctx.Navigator?.RevealWorkshop(p as string));
        }

        /// <summary>The live controls, so the tab can expose their x:Names.</summary>
        public WorkshopShelfParts Parts { get; }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => Set(ref _isExpanded, value);
        }

        public string DrawerNote => CompanionLocStaging.Resolve("companion_workshop_drawer_note");
        public IReadOnlyList<IWorkshopCellVm> Cells { get; }
        public ICommand FocusCellCommand { get; }

        private static CompanionWorkshopCell Cell(string key, string titleKey, object content) =>
            new(CompanionLocStaging.Resolve(titleKey)) { Key = key, Content = content };
    }
}
