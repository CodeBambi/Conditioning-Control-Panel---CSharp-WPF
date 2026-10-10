// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.Haptics.cs (1,158 lines).
// HapticService is in Core now (CCP.Core/Services/Haptics, seeded as CoreHaptics.Service by App).
// The connection bar and the global dials live on the page itself -
// CCP.Avalonia/Views/Tabs/HapticsTabView.Connection.cs - because that view calls
// InitializeComponent, so its x:Name fields are real (CLAUDE.md trap 7); this partial only opens
// the setup wizard for it.
//
// The provider chips, toy cards, routing rows, live status, DtRH / sync / Phase F / DSP editors and the
// pattern lab are on the page too (HapticsTabView.Dials.cs, Core Views/Controls/HapticUiModels.cs).

using System;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Opens the haptics setup wizard (Avalonia's ShowDialog is awaitable and needs an
        /// owner). The page re-reads its settings afterwards, as WPF BtnHapticsHelp_Click does.</summary>
        internal async System.Threading.Tasks.Task ShowHapticsSetupAsync()
        {
            try { await new HapticsSetupWindow().ShowDialogSafe(this); }
            catch (Exception ex) { Log.Error(ex, "HapticsSetupWindow failed"); }
        }
    }
}
