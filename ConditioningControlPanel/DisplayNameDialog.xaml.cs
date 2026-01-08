using System.Windows;
using System.Windows.Input;

namespace ConditioningControlPanel
{
    public partial class DisplayNameDialog : Window
    {
        public string DisplayName { get; private set; } = "";

        public DisplayNameDialog()
        {
            InitializeComponent();

            TxtDisplayName.TextChanged += (s, e) =>
            {
                var length = TxtDisplayName.Text.Trim().Length;
                TxtCharCount.Text = $"{length}/30 characters";
                BtnConfirm.IsEnabled = length >= 2;
            };

            Loaded += (s, e) =>
            {
                TxtDisplayName.Focus();
            };
        }

        private void TxtDisplayName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && BtnConfirm.IsEnabled)
            {
                Accept();
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            Accept();
        }

        private void Accept()
        {
            var name = TxtDisplayName.Text.Trim();
            if (name.Length < 2)
            {
                MessageBox.Show(
                    "Please enter a name with at least 2 characters.",
                    "Invalid Name",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DisplayName = name;
            DialogResult = true;
            Close();
        }
    }
}
