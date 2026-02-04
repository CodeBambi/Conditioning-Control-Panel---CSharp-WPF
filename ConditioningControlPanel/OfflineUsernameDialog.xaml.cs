using System.Windows;
using System.Windows.Input;

namespace ConditioningControlPanel
{
    public partial class OfflineUsernameDialog : Window
    {
        public string Username { get; private set; } = "";

        public OfflineUsernameDialog()
        {
            InitializeComponent();

            TxtUsername.TextChanged += (s, e) =>
            {
                var length = TxtUsername.Text.Trim().Length;
                TxtCharCount.Text = $"{length}/30 characters";
                BtnConfirm.IsEnabled = length >= 2;
            };

            Loaded += (s, e) =>
            {
                TxtUsername.Focus();
            };
        }

        private void TxtUsername_KeyDown(object sender, KeyEventArgs e)
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
            var name = TxtUsername.Text.Trim();
            if (name.Length < 2)
            {
                MessageBox.Show(
                    "Please enter a name with at least 2 characters.",
                    "Invalid Name",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            Username = name;
            DialogResult = true;
            Close();
        }
    }
}
