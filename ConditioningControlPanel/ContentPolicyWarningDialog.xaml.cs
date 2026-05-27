using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    /// <summary>
    /// CCBill compliance — moderation escalation warning modal.
    ///
    /// Shown by <see cref="Services.Moderation.IModerationCounter"/> when the user
    /// hits <see cref="Services.Moderation.ModerationCounter.WarningThreshold"/>
    /// moderation events within the sliding window. One-shot per threshold-cross:
    /// the counter does not re-raise this dialog on each additional hit.
    /// </summary>
    public partial class ContentPolicyWarningDialog : Window
    {
        private const string PolicyUrl = "https://cclabs.app/policies/prohibited-content";

        public ContentPolicyWarningDialog(int hitCount)
        {
            InitializeComponent();
            TxtBodyCount.Text = string.Format(
                CultureInfo.CurrentCulture,
                Loc.Get("policy_warning_body_count"),
                hitCount);
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
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
                App.Logger?.Warning(ex, "ContentPolicyWarningDialog: failed to open policy URL {Url}", PolicyUrl);
            }
        }
    }
}
