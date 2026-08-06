using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// Z6 — the attention meter. See the XAML header for the visual spec.
    ///
    /// <para>Code-free by design: the copy ladder is a pure function of the remaining fraction
    /// (<see cref="AttentionCopy"/>), the bar is a star-width column, and detail-on-demand is a
    /// command on the viewmodel. Nothing here animates.</para>
    /// </summary>
    public partial class AttentionGaugeView : UserControl
    {
        public AttentionGaugeView()
        {
            InitializeComponent();
        }

        /// <summary>Convenience for hosts that hand in a viewmodel rather than setting DataContext.</summary>
        public IAttentionGaugeVm? ViewModel
        {
            get => DataContext as IAttentionGaugeVm;
            set => DataContext = value;
        }
    }
}
