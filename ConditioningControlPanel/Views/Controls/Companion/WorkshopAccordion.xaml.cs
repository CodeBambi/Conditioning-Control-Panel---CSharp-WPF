using System;
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
        public void ExpandAndReveal()
        {
            var vm = ViewModel;
            if (vm != null) vm.IsExpanded = true;
            else Drawer.IsExpanded = true;

            var dispatcher = Dispatcher;
            if (dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                try { BringIntoView(); }
                catch (InvalidOperationException) { /* torn down mid-scroll */ }
            }), DispatcherPriority.Normal);
        }
    }
}
