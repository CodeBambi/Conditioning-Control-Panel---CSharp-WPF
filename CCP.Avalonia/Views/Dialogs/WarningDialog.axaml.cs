using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Dialogs/WarningDialog.xaml.cs. Deviations:
    ///  - <c>DialogResult</c> becomes <c>Close(bool)</c>; Avalonia carries the result through
    ///    <c>ShowDialog&lt;bool?&gt;</c>. <c>Confirmed</c> is kept, so callers that read it after
    ///    the await still work.
    ///  - <c>Checked</c>/<c>Unchecked</c> become the single
    ///    <c>ToggleButton.IsCheckedChanged</c> event, wired here rather than in markup.
    ///  - <c>ShowDoubleWarning</c> becomes <c>ShowDoubleWarningAsync</c>: Avalonia's
    ///    <c>ShowDialog</c> is awaitable and has no blocking form, so the call site awaits. The
    ///    loc keys and their argument order are copied from the WPF original unchanged.
    ///  - The three design-time <c>{loc:Str}</c> defaults are gone from the markup and live in the
    ///    render constructor instead; see the header comment in the .axaml for why.
    /// </summary>
    public partial class WarningDialog : Window
    {
        private readonly CheckBox _chkConfirm;

        public bool Confirmed { get; private set; }

        /// <summary>Render/design constructor: sample data so --render-view can draw the dialog.
        /// Built through the same Loc.GetF calls ShowDoubleWarningAsync uses, so the render shows
        /// the strings a real caller gets. The box is pre-ticked so the render proves the checked
        /// half of ConfirmCheckBox's template and the enabled Confirm button.</summary>
        internal WarningDialog() : this(
            Loc.GetF("warning_enable_feature_title", "Lockdown Mode"),
            Loc.GetF("warning_enable_feature_body", "Lockdown Mode",
                     "• The app cannot be closed until the timer runs out.\n" +
                     "• Settings are frozen for the whole session.\n" +
                     "• There is no emergency exit once it starts."),
            Loc.GetF("warning_enable_feature_confirm", "Lockdown Mode"))
        {
            _chkConfirm.IsChecked = true;
        }

        public WarningDialog(string title, string message, string confirmText = "I understand the risks")
        {
            AvaloniaXamlLoader.Load(this);

            this.FindControl<TextBlock>("TxtTitle")!.Text = title;
            this.FindControl<TextBlock>("TxtMessage")!.Text = message;
            this.FindControl<TextBlock>("TxtConfirmLabel")!.Text = confirmText;

            _chkConfirm = this.FindControl<CheckBox>("ChkConfirm")!;
            var btnConfirm = this.FindControl<Button>("BtnConfirm")!;

            _chkConfirm.IsCheckedChanged += (_, _) => btnConfirm.IsEnabled = _chkConfirm.IsChecked == true;

            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => BtnCancel_Click();
            btnConfirm.Click += (_, _) => BtnConfirm_Click();
        }

        private void BtnCancel_Click()
        {
            Confirmed = false;
            Close(false);
        }

        private void BtnConfirm_Click()
        {
            if (_chkConfirm.IsChecked == true)
            {
                Confirmed = true;
                Close(true);
            }
        }

        /// <summary>
        /// Shows a double warning dialog for dangerous features
        /// </summary>
        public static async Task<bool> ShowDoubleWarningAsync(Window owner, string feature, string consequences)
        {
            var title = Loc.GetF("warning_enable_feature_title", feature);
            var message = Loc.GetF("warning_enable_feature_body", feature, consequences);

            var dialog = new WarningDialog(title, message, Loc.GetF("warning_enable_feature_confirm", feature))
            {
                Topmost = true
            };

            return await dialog.ShowDialog<bool?>(owner) == true && dialog.Confirmed;
        }
    }
}
