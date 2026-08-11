using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// Performance-section passthroughs (UX restructure, Phase 3 hardening). SaveSettings reads
    /// these five so the round-trip goes through the LIVE editors in Settings ▸ Performance
    /// instead of the dead LegacyDashboardHost twins — the twins were only ever safe because
    /// SaveSettings re-seeds them via LoadSettings first, an invariant three separate reviews
    /// have now had to re-prove. Reading the live controls removes the trap outright.
    /// </summary>
    public partial class AppSettingsTabView : UserControl
    {
        internal CheckBox ChkPerformanceMode => SectionPerformance.ChkPerformanceMode;
        internal CheckBox ChkAutoPerformance => SectionPerformance.ChkAutoPerformance;
        internal CheckBox ChkUnifiedOverlay => SectionPerformance.ChkUnifiedOverlay;
        internal CheckBox ChkVideoHwDecode => SectionPerformance.ChkVideoHwDecode;
        internal ComboBox CmbMotionLevel => SectionPerformance.CmbMotionLevel;
    }
}
