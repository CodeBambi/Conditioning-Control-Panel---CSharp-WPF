using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Affirmation modal for catalogue submissions. Shown before a user-
    /// initiated POST to the catalogue API. The user must tick the
    /// affirmation checkbox before Submit is enabled — same gating pattern as
    /// WarningDialog.cs.
    ///
    /// Usage:
    ///   var d = new CatalogueSubmitDialog(enhancementName) { Owner = this };
    ///   if (d.ShowDialog() == true) {
    ///       // d.Confirmed == true, proceed with CatalogueService submit
    ///   }
    ///
    /// Body copy is verbatim from the W2 spec — translators can edit
    /// dialog_catalogue_submit_body, but the canonical English text is the
    /// ToS-anchoring reference. The guidelines hyperlink is a separate
    /// localized string (sibling element, not embedded in the body prose) so
    /// each translation is atomic.
    /// </summary>
    public partial class CatalogueSubmitDialog : Window
    {
        public bool Confirmed { get; private set; }

        public CatalogueSubmitDialog(string enhancementName)
        {
            InitializeComponent();

            TxtSubtitle.Text = string.IsNullOrWhiteSpace(enhancementName)
                ? string.Empty
                : Loc.GetF("dialog_catalogue_submit_subtitle_fmt", enhancementName);

            // Checkbox gates the Submit button — same pattern as WarningDialog.
            ChkAffirm.Checked += (_, _) => BtnSubmit.IsEnabled = true;
            ChkAffirm.Unchecked += (_, _) => BtnSubmit.IsEnabled = false;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            DialogResult = false;
            Close();
        }

        private void BtnSubmit_Click(object sender, RoutedEventArgs e)
        {
            if (ChkAffirm.IsChecked != true) return;
            Confirmed = true;
            DialogResult = true;
            Close();
        }

        private void LinkGuidelines_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true,
                });
                e.Handled = true;
            }
            catch
            {
                // Best-effort: failure to open the browser shouldn't crash the
                // dialog. The user can still type the URL manually.
            }
        }
    }
}
