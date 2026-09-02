using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// CCBill AI Content Merchant Addendum — 18+ and content-policy acknowledgement gate.
    /// Accept stays disabled until the age checkbox is ticked. ShowDialog&lt;bool&gt; yields true on
    /// accept; the caller records the acknowledgement, exactly as on WPF.
    /// </summary>
    public partial class ExplicitContentAcknowledgementDialog : Window
    {
        public ExplicitContentAcknowledgementDialog()
        {
            AvaloniaXamlLoader.Load(this);

            var chk = this.FindControl<CheckBox>("ChkAgeConfirm")!;
            var accept = this.FindControl<Button>("BtnAccept")!;

            chk.IsCheckedChanged += (_, _) => accept.IsEnabled = chk.IsChecked == true;
            accept.Click += (_, _) =>
            {
                // Defense-in-depth: IsEnabled already prevents this, but a harness may invoke it.
                if (chk.IsChecked != true) return;

                // ponytail: needs AppSettings.CompanionPrompt (ExplicitAcknowledgedAt/Locale audit stamp),
                // wired when AppSettings moves to Core.
                Close(true);
            };
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => Close(false);
        }
    }
}
