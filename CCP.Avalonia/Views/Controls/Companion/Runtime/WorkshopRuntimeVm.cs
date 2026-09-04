using System;
using System.Collections.Generic;
using System.Windows.Input;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime
{
    /// <summary>
    /// The six re-parented legacy controls, held together so the drawer has one accessor for them.
    ///
    /// <para>PORTED from ConditioningControlPanel/Views/Controls/Companion/Runtime/WorkshopRuntimeVm.cs.
    /// Each cell is a real UserControl containing the actual moved markup — not a copy — so there is
    /// exactly one instance of every control.</para>
    /// </summary>
    public sealed class WorkshopShelfParts
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
    /// a live control through <see cref="IWorkshopCellVm.Content"/>, which is what makes the six
    /// finished cells in this folder reachable: without a viewmodel the accordion draws an empty
    /// drawer and every one of them is an orphan.</para>
    ///
    /// <para><b>Anchor vs heading.</b> <see cref="IWorkshopCellVm.Key"/> is the deep-link identity
    /// (<see cref="CompanionRoomAnchors"/>) and <c>Title</c> is the localizable heading. Keeping
    /// the two apart is what lets a Workshop heading be translated without silently breaking the
    /// hero's Switch chip and Z5's "fine-tuning ↓".</para>
    ///
    /// <para><b>Deviation from WPF.</b> The original routes <c>FocusCellCommand</c> through
    /// <c>CompanionRuntimeContext.Navigator</c>, part of the ICompanionRoomVm layer that stays in
    /// the head. Here the host passes its own reveal action in — the same behaviour, one
    /// indirection shorter.</para>
    /// </summary>
    public sealed class WorkshopRuntimeVm : CompanionObservable, IWorkshopAccordionVm
    {
        private bool _isExpanded;

        public WorkshopRuntimeVm(Action<string?> revealCell)
        {
            Parts = new WorkshopShelfParts();

            Cells = new IWorkshopCellVm[]
            {
                Cell(CompanionRoomAnchors.WorkshopRosterCell, "companion_workshop_cell_roster", Parts.Roster),
                Cell(CompanionRoomAnchors.WorkshopBehaviorCell, "companion_workshop_cell_behavior", Parts.Behavior),
                Cell(CompanionRoomAnchors.WorkshopTriggersCell, "companion_workshop_cell_triggers", Parts.Triggers),
                Cell(CompanionRoomAnchors.WorkshopLibraryCell, "companion_workshop_cell_library", Parts.Library),
                Cell(CompanionRoomAnchors.WorkshopCommunityCell, "companion_workshop_cell_community", Parts.Community),
                Cell(CompanionRoomAnchors.WorkshopAwarenessCell, "companion_workshop_cell_awareness", Parts.Awareness)
            };

            FocusCellCommand = new RevealCommand(revealCell);
        }

        /// <summary>
        /// The one command on this page that needs its CommandParameter — the cell key the heading
        /// button passes. This head's <see cref="CompanionRelayCommand"/> ported only the
        /// no-parameter shape, and widening a primitive every companion zone shares for one caller
        /// is not worth the blast radius; four lines here are.
        /// </summary>
        private sealed class RevealCommand : ICommand
        {
            private readonly Action<string?> _reveal;
            public RevealCommand(Action<string?> reveal) => _reveal = reveal;
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => _reveal(parameter as string);
            public event EventHandler? CanExecuteChanged { add { } remove { } }
        }

        /// <summary>The live controls, for a host that wants to reach one directly.</summary>
        public WorkshopShelfParts Parts { get; }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => Set(ref _isExpanded, value);
        }

        public string DrawerNote => Loc.Get("companion_workshop_drawer_note");
        public IReadOnlyList<IWorkshopCellVm> Cells { get; }
        public ICommand FocusCellCommand { get; }

        private static CompanionWorkshopCell Cell(string key, string titleKey, object content) =>
            new(Loc.Get(titleKey)) { Key = key, Content = content };
    }
}
