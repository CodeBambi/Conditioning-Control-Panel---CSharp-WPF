using System.Windows;

namespace ConditioningControlPanel
{
    public partial class EasterEggWindow : Window
    {
        public EasterEggWindow(int readerCount = -1)
        {
            InitializeComponent();

            if (readerCount > 0)
            {
                TxtReaderCount.Text = $"This rant has been read {readerCount} times";
                TxtReaderCount.Visibility = Visibility.Visible;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
