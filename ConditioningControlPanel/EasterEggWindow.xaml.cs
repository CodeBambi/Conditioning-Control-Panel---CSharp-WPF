using System.Windows;

namespace ConditioningControlPanel
{
    public partial class EasterEggWindow : Window
    {
        public EasterEggWindow()
        {
            InitializeComponent();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
