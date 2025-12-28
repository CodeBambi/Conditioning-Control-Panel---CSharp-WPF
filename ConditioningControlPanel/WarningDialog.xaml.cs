using System.Windows;

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
            var title = $"⚠️ Enable {feature}?";
            var message = $"You are about to enable {feature}.\n\n" +
                         $"⚠️ CONSEQUENCES:\n{consequences}\n\n" +
                         "This is a DANGEROUS setting. Are you absolutely sure?";
            
            var dialog = new WarningDialog(title, message, $"I understand enabling {feature} is dangerous")
            {
                Owner = owner
            };
            
            return dialog.ShowDialog() == true && dialog.Confirmed;
        }
    }
}
