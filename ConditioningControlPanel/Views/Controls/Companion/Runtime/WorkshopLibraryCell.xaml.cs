using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls.Companion.Runtime
{
    /// <summary>Z8 · HER LIBRARY. See the XAML header. Pure forwarding.</summary>
    public partial class WorkshopLibraryCell : UserControl
    {
        public WorkshopLibraryCell()
        {
            InitializeComponent();
            // The pool is height-capped and scrolls internally; without this a wheel notch over a
            // short list is swallowed and the page under it never moves.
            CompanionWheelRelay.Attach(LinkPoolScroll);
        }

        private void BtnAddVideoLink_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnAddVideoLink_Click(sender, e);
        }
    }
}
