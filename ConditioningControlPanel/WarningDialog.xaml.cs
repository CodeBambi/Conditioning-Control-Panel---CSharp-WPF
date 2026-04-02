using System.Windows;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    public partial class WarningDialog : Window
    {
        public bool Confirmed { get; private set; }

        public WarningDialog(string title, string message, string confirmText = "I understand the risks")
        {
            InitializeComponent();
            
            TxtTitle.Text = title;
            TxtMessage.Text = message;
            TxtConfirmLabel.Text = confirmText;
            
            ChkConfirm.Checked += (s, e) => BtnConfirm.IsEnabled = true;
            ChkConfirm.Unchecked += (s, e) => BtnConfirm.IsEnabled = false;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            DialogResult = false;
            Close();
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (ChkConfirm.IsChecked == true)
            {
                Confirmed = true;
                DialogResult = true;
                Close();
            }
        }

        /// <summary>
        /// Shows a double warning dialog for dangerous features
        /// </summary>
        public static bool ShowDoubleWarning(Window owner, string feature, string consequences)
        {
            var title = Loc.GetF("warning_enable_feature_title", feature);
            var message = Loc.GetF("warning_enable_feature_body", feature, consequences);

            var dialog = new WarningDialog(title, message, Loc.GetF("warning_enable_feature_confirm", feature))
            {
                Owner = owner
            };
            
            return dialog.ShowDialog() == true && dialog.Confirmed;
        }
    }
}
