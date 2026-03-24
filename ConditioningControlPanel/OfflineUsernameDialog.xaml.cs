using System.Windows;
using System.Windows.Input;
using ConditioningControlPanel.Localization;

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
                TxtCharCount.Text = Loc.GetF("label_char_count_of_max", length, 30);
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
                    Loc.Get("msg_enter_name_min_2_chars"),
                    Loc.Get("title_invalid_name"),
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
