using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Z8 — the Workshop drawer. See the XAML header for the visual spec.
    /// </summary>
    public partial class WorkshopAccordion : UserControl
    {
        public WorkshopAccordion()
        {
            InitializeComponent();
        }

        /// <summary>Convenience for hosts that hand in a viewmodel rather than setting DataContext.</summary>
        public IWorkshopAccordionVm? ViewModel
        {
            get => DataContext as IWorkshopAccordionVm;
            set => DataContext = value;
        }

        /// <summary>
        /// Opens the drawer and scrolls it into view — what the hero's Switch chip and Z5's
        /// "fine-tuning ↓" link both call.
        ///
        /// <para>Deferred one dispatcher turn at <see cref="DispatcherPriority.Normal"/> so the
        /// body is measured before the scroll, and never at Loaded priority (starved here).</para>
        /// </summary>
        public void ExpandAndReveal() => ExpandAndReveal(null);

        /// <summary>
        /// The same deep link, but landing on a named pigeonhole: the hero's Switch chip asks for
        /// the roster, Z5's fine-tuning link asks for the awareness cell.
        ///
        /// <para><paramref name="cellTitle"/> is matched against <see cref="IWorkshopCellVm.Key"/> —
        /// the anchor, not the heading. Before the wiring pass split the two, a localized Workshop
        /// heading would have quietly broken every deep link on the page. An unknown key is not an
        /// error: the drawer still opens and the caller gets the drawer's own scroll, which is the
        /// useful half of the job.</para>
        /// </summary>
        public void ExpandAndReveal(string? cellTitle)
        {
            var vm = ViewModel;
            if (vm != null) vm.IsExpanded = true;
            else Drawer.IsExpanded = true;

            var dispatcher = Dispatcher;
            if (dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var target = FindCellContainer(cellTitle);
                    if (target != null) target.BringIntoView();
                    else BringIntoView();
                }
                catch (InvalidOperationException) { /* torn down mid-scroll */ }
            }), DispatcherPriority.Normal);
        }

        /// <summary>
        /// Resolves the container for a cell anchor key, or null when there is no match (or the
        /// containers have not been generated because the drawer is still collapsed).
        /// </summary>
        private FrameworkElement? FindCellContainer(string? cellKey)
        {
            if (string.IsNullOrWhiteSpace(cellKey)) return null;

            var cells = ViewModel?.Cells;
            if (cells == null || cells.Count == 0) return null;

            // The Expander only realises its body when expanded; force the pass so the generator
            // has containers to hand back on this same dispatcher turn.
            CellHost.UpdateLayout();

            foreach (var cell in cells)
            {
                if (cell == null) continue;
                if (!string.Equals(cell.Key, cellKey, StringComparison.OrdinalIgnoreCase)) continue;
                return CellHost.ItemContainerGenerator.ContainerFromItem(cell) as FrameworkElement;
            }
            return null;
        }
    }
}
