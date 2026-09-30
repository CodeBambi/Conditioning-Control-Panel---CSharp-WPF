// PORTED from ConditioningControlPanel/Dialogs/ChasterImportConfirmDialog.xaml.cs.
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>"Allow" keeps a preset's Chaster add-time actions on; "Leave it off" (the default,
    /// also Esc and closing the window) activates everything else with the lock time off.</summary>
    public partial class ChasterImportConfirmDialog : Window
    {
        /// <summary>Render constructor (RenderProof) with sample data; internal so no caller ships it.</summary>
        internal ChasterImportConfirmDialog() : this(new KeywordTriggerChasterImport.Summary(3, 45)) { }

        public ChasterImportConfirmDialog(KeywordTriggerChasterImport.Summary summary)
        {
            InitializeComponent();
            TxtDetail.Text = Loc.GetF("chaster_import_detail", summary.Count, summary.MinutesPerFire);
        }

        /// <summary>WPF Ask: asks when the summary has any Chaster time, and answers false straight
        /// away when it has none (nothing to allow). Always owned: both callers are a shown window.</summary>
        public static async Task<bool> AskAsync(Window owner, KeywordTriggerChasterImport.Summary summary)
        {
            if (!summary.Any) return false;
            return await new ChasterImportConfirmDialog(summary).ShowDialog<bool?>(owner) == true;
        }

        private void BtnSkip_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnAllow_Click(object? sender, RoutedEventArgs e) => Close(true);
    }
}
