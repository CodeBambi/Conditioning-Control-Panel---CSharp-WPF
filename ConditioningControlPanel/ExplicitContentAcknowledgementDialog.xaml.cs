using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Navigation;
using ConditioningControlPanel.Models;
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
    ///
    /// P2 C3 hardening: the user must tick a required age-confirmation checkbox before
    /// the Accept button enables. On accept, we additionally stamp the UTC timestamp and
    /// current locale onto the CompanionPrompt settings for the CCBill audit trail.
    /// </summary>
    public partial class ExplicitContentAcknowledgementDialog : Window
    {
        private const string PolicyUrl = "https://app.cclabs.app/policies/prohibited-content";

        public ExplicitContentAcknowledgementDialog()
        {
            InitializeComponent();
        }

        private void ChkAgeConfirm_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // Accept stays disabled until the user affirms the 18+ checkbox.
            if (BtnAccept != null)
            {
                BtnAccept.IsEnabled = ChkAgeConfirm?.IsChecked == true;
            }
        }

        private void BtnAccept_Click(object sender, RoutedEventArgs e)
        {
            // Defense-in-depth: the IsEnabled binding should already prevent this click,
            // but guard anyway in case a hosting harness invokes the handler programmatically.
            if (ChkAgeConfirm?.IsChecked != true)
            {
                return;
            }

            // P2 C3: stamp the audit-trail fields BEFORE the caller's MarkAcknowledged call
            // flips ExplicitContentAcknowledged + ExplicitAcknowledgedVersion. These two
            // properties capture WHEN and in WHICH locale the affirmation happened.
            try
            {
                CompanionPromptSettings? promptSettings = App.Settings?.Current?.CompanionPrompt;
                if (promptSettings != null)
                {
                    promptSettings.ExplicitAcknowledgedAt =
                        DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                    promptSettings.ExplicitAcknowledgedLocale = CultureInfo.CurrentCulture.Name;
                }
            }
            catch (Exception ex)
            {
                // Best-effort capture; the gate itself still functions if this fails.
                App.Logger?.Warning(ex, "ExplicitContentAcknowledgementDialog: failed to capture ack timestamp/locale");
            }

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
