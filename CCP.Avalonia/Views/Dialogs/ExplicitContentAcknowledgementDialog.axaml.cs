using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Serilog;

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

                // P2 C3: stamp the audit-trail fields BEFORE the caller's MarkAcknowledged call
                // flips ExplicitContentAcknowledged + ExplicitAcknowledgedVersion. These two
                // properties capture WHEN and in WHICH locale the affirmation happened. The
                // dialog never saves — the caller's MarkAcknowledged + Save persists both.
                try
                {
                    var promptSettings = CoreSettings.Current.CompanionPrompt;
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
                    Log.Warning(ex, "ExplicitContentAcknowledgementDialog: failed to capture ack timestamp/locale");
                }

                Close(true);
            };
            this.FindControl<Button>("BtnCancel")!.Click += (_, _) => Close(false);
        }
    }
}
