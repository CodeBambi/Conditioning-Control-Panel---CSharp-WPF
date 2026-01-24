using System.Windows;

namespace ConditioningControlPanel
{
    public partial class CompanionTutorialWindow : Window
    {
        public CompanionTutorialWindow()
        {
            InitializeComponent();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
