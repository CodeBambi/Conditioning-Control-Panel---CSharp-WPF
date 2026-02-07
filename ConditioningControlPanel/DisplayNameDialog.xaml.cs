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
            SetupTextChanged();

            Loaded += (s, e) =>
            {
                TxtDisplayName.Focus();
            };
        }

        public DisplayNameDialog(bool isChangeName, string? currentName) : this()
        {
            if (isChangeName)
            {
                TxtTitle.Text = "Change Your Display Name";
                WarningPanel.Visibility = Visibility.Collapsed;

                if (!string.IsNullOrEmpty(currentName))
                {
                    TxtDisplayName.Text = currentName;
                    TxtDisplayName.SelectAll();
                }
            }
        }

        private bool _isDeleteMode;

        public DisplayNameDialog(string confirmationMode) : this()
        {
            if (confirmationMode == "delete")
            {
                _isDeleteMode = true;
                TxtTitle.Text = "Delete Your Profile";
                TxtTitle.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF4444"));

                // Red-tinted warning
                WarningPanel.Visibility = Visibility.Visible;
                var warningStack = (System.Windows.Controls.StackPanel)WarningPanel.Child;
                var warningLabel = (System.Windows.Controls.TextBlock)warningStack.Children[0];
                var warningText = (System.Windows.Controls.TextBlock)warningStack.Children[1];
                warningLabel.Text = "WARNING:";
                warningLabel.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF4444"));
                warningText.Text = " This will permanently delete all your data and stats!";
                warningText.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF4444"));

                // Update prompt and button
                var border = (System.Windows.Controls.Border)Content;
                var grid = (System.Windows.Controls.Grid)border.Child;
                var promptBlock = (System.Windows.Controls.TextBlock)grid.Children[2];
                promptBlock.Text = "Type DELETE to confirm:";
                BtnConfirm.Content = "Delete";
                BtnConfirm.Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF4444"));

                TxtDisplayName.MaxLength = 6;
                TxtDisplayName.Text = "";
                TxtCharCount.Visibility = Visibility.Collapsed;
            }
        }

        private void SetupTextChanged()
        {
            TxtDisplayName.TextChanged += (s, e) =>
            {
                if (_isDeleteMode)
                {
                    BtnConfirm.IsEnabled = TxtDisplayName.Text.Trim() == "DELETE";
                }
                else
                {
                    var length = TxtDisplayName.Text.Trim().Length;
                    TxtCharCount.Text = $"{length}/20 characters";
                    BtnConfirm.IsEnabled = length >= 2 && length <= 20;
                }
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

            if (_isDeleteMode)
            {
                if (name != "DELETE") return;
                DisplayName = name;
                DialogResult = true;
                Close();
                return;
            }

            if (name.Length < 2 || name.Length > 20)
            {
                MessageBox.Show(
                    "Please enter a name with 2-20 characters.",
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
