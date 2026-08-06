using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Z3 — the memory diary. See the XAML header for the visual spec.
    ///
    /// <para>Intentionally code-free: filtering, sorting and the hover-reveal action row are all
    /// declarative (viewmodel projection + DataTemplate triggers). The one behaviour that will
    /// arrive with the wiring pass is the diegetic bark on first open per session, which belongs
    /// to whoever owns the bark rules — not to this view.</para>
    /// </summary>
    public partial class MemoryDiaryView : UserControl
    {
        public MemoryDiaryView()
        {
            InitializeComponent();
        }

        /// <summary>Convenience for hosts that hand in a viewmodel rather than setting DataContext.</summary>
        public IMemoryDiaryVm? ViewModel
        {
            get => DataContext as IMemoryDiaryVm;
            set => DataContext = value;
        }
    }
}
