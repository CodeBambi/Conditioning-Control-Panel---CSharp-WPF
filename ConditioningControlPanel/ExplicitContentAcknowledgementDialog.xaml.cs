using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// CCBill AI Content Merchant Addendum — 18+ and content-policy acknowledgement gate.
    ///
    /// Shown before activating any preset that <see cref="ExplicitContentGate"/> flags as
    /// requiring acknowledgement. Caller checks <c>DialogResult == true</c> and calls
    /// <see cref="ExplicitContentGate.MarkAcknowledged"/> + SettingsService.Save before
    /// proceeding with the original action.
    /// </summary>
    public partial class ExplicitContentAcknowledgementDialog : Window
    {
        private const string PolicyUrl = "https://cclabs.app/policies/prohibited-content";

        public ExplicitContentAcknowledgementDialog()
        {
            InitializeComponent();
        }

        private void BtnAccept_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void LnkPolicy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(PolicyUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "ExplicitContentAcknowledgementDialog: failed to open policy URL {Url}", PolicyUrl);
            }
        }
    }
}
