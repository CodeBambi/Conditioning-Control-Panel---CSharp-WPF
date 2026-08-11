using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls.Companion.Runtime
{
    /// <summary>Z8 · COMMUNITY. See the XAML header. Pure forwarding.</summary>
    public partial class WorkshopCommunityCell : UserControl
    {
        public WorkshopCommunityCell()
        {
            InitializeComponent();
            CompanionWheelRelay.Attach(InstalledPromptsScroll);
        }

        private void BtnBrowsePrompts_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnBrowsePrompts_Click(sender, e);
        }

        private void BtnImportPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnImportPrompt_Click(sender, e);
        }

        private void BtnExportPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnExportPrompt_Click(sender, e);
        }

        private void BtnRefreshPrompts_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnRefreshPrompts_Click(sender, e);
        }
    }
}
