using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Affirmation modal for catalogue submissions. Shown before a user-
    /// initiated POST to the catalogue API. The user must tick the
    /// affirmation checkbox before Submit is enabled — same gating pattern as
    /// WarningDialog.cs.
    ///
    /// Usage:
    ///   var d = new CatalogueSubmitDialog(enhancementName);
    ///   if (await d.ShowDialog&lt;bool&gt;(owner)) {
    ///       // d.Confirmed == true, proceed with CatalogueService submit
    ///   }
    ///
    /// Body copy is verbatim from the W2 spec — translators can edit
    /// dialog_catalogue_submit_body, but the canonical English text is the
    /// ToS-anchoring reference. The guidelines hyperlink is a separate
    /// localized string (sibling element, not embedded in the body prose) so
    /// each translation is atomic.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/CatalogueSubmitDialog.xaml.cs. Deviations:
    ///  - WPF's <c>DialogResult</c> property becomes <c>Close(bool)</c>; Avalonia carries the
    ///    result through <c>ShowDialog&lt;bool&gt;</c>.
    ///  - <c>LinkGuidelines_RequestNavigate</c> is gone: <c>HyperlinkButton.NavigateUri</c> opens
    ///    the browser itself, so there is no <c>Process.Start</c> to port.
    ///  - Checked/Unchecked collapse into <c>IsCheckedChanged</c>.
    /// </summary>
    public partial class CatalogueSubmitDialog : Window
    {
        public bool Confirmed { get; private set; }

        /// <summary>Render/design constructor: sample data so --render-view can draw the dialog.</summary>
        internal CatalogueSubmitDialog() : this("Deeper Spiral Pack") { }

        public CatalogueSubmitDialog(string enhancementName)
        {
            AvaloniaXamlLoader.Load(this);

            var subtitle = this.FindControl<TextBlock>("TxtSubtitle")!;
            var chkAffirm = this.FindControl<CheckBox>("ChkAffirm")!;
            var btnSubmit = this.FindControl<Button>("BtnSubmit")!;

            subtitle.Text = string.IsNullOrWhiteSpace(enhancementName)
                ? string.Empty
                : Loc.GetF("dialog_catalogue_submit_subtitle_fmt", enhancementName);

            // Checkbox gates the Submit button — same pattern as WarningDialog.
            chkAffirm.IsCheckedChanged += (_, _) => btnSubmit.IsEnabled = chkAffirm.IsChecked == true;

            this.FindControl<Button>("BtnCancel")!.Click += (_, _) =>
            {
                Confirmed = false;
                Close(false);
            };

            btnSubmit.Click += (_, _) =>
            {
                if (chkAffirm.IsChecked != true) return;
                Confirmed = true;
                Close(true);
            };
        }
    }
}
