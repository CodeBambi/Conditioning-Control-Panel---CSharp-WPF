using System.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel;

/// <summary>
/// Asked before a preset that carries Chaster add-time actions goes live. "Allow" keeps those
/// actions on; "Leave it off" (the default, also Esc and closing the window) activates everything
/// else with the lock time switched off.
/// </summary>
public partial class ChasterImportConfirmDialog : Window
{
    public bool Allowed { get; private set; }

    public ChasterImportConfirmDialog(KeywordTriggerChasterImport.Summary summary)
    {
        InitializeComponent();
        TxtDetail.Text = Loc.GetF("chaster_import_detail", summary.Count, summary.MinutesPerFire);
    }

    /// <summary>Asks when the summary has any Chaster time, and answers false straight away when
    /// it has none (nothing to allow).</summary>
    public static bool Ask(Window? owner, KeywordTriggerChasterImport.Summary summary)
    {
        if (!summary.Any) return false;
        var dlg = new ChasterImportConfirmDialog(summary);
        if (owner != null && owner.IsVisible) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();
        return dlg.Allowed;
    }

    private void BtnSkip_Click(object sender, RoutedEventArgs e)
    {
        Allowed = false;
        DialogResult = false;
    }

    private void BtnAllow_Click(object sender, RoutedEventArgs e)
    {
        Allowed = true;
        DialogResult = true;
    }
}
