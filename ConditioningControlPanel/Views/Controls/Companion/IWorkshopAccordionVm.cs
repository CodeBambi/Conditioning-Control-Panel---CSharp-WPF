using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Z8 — The Workshop. The second collapsed drawer: roster, behavior sliders, triggers, phrases,
    /// her library, community prompts, and the awareness cooldowns.
    ///
    /// <para>"nothing was deleted. it just stopped being the front door." The interior is today's
    /// accordions almost verbatim — this is a container move, not a rebuild, so the shape here is
    /// deliberately generic: cells of rows, which the wiring pass replaces with the real controls
    /// while preserving every x:Name the MainWindow partials write to.</para>
    /// </summary>
    public interface IWorkshopAccordionVm : INotifyPropertyChanged
    {
        /// <summary>Two-way. The hero's Switch chip opens this straight onto the roster cell.</summary>
        bool IsExpanded { get; set; }

        string DrawerNote { get; }

        /// <summary>The pigeonholes, in display order.</summary>
        IReadOnlyList<IWorkshopCellVm> Cells { get; }

        /// <summary>Scrolls a named cell into view for the hero's Switch chip and Z5's fine-tuning
        /// link. Parameter is the cell title key.</summary>
        ICommand FocusCellCommand { get; }
    }
}
