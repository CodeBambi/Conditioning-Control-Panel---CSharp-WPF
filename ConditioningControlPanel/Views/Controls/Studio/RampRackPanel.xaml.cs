using System;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls.Studio
{
    /// <summary>
    /// Studio rack entry <c>ramp</c>. Hosts the live
    /// <see cref="Features.IntensityRampFeatureControl"/> (which owns every handler and writes
    /// <c>App.Settings.Current</c> on each edit) and adds the help "?" that only the dead
    /// ProgressionTab copy carried.
    ///
    /// <para>No service is touched from here. The global ramp runs on MainWindow's own
    /// <c>RampTimer_Tick</c>; the per-session ramp is <c>SessionEngine.UpdateRampingValues</c>.
    /// Both read the settings this panel writes.</para>
    /// </summary>
    public partial class RampRackPanel : UserControl, Features.ISettingsRebindable
    {
        /// <summary>
        /// Bark key for <c>NotifyFeatureOpened</c>. Deliberately the same
        /// <c>"SchedulerRamp"</c> the popup fired (the popup was one control for both halves) -
        /// it is the only <c>feature_eq</c> value with rules in the mods' bark_rules.json.
        /// See <see cref="SchedulerRackPanel.BarkFeatureKey"/>.
        /// </summary>
        internal const string BarkFeatureKey = "SchedulerRamp";

        /// <summary>Help topic id, matching the dead ProgressionTab button's Tag.</summary>
        private const string HelpSectionId = "IntensityRamp";

        public RampRackPanel()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        /// <summary>The live control this panel hosts, for callers that need the real editor.</summary>
        internal global::ConditioningControlPanel.Features.IntensityRampFeatureControl Inner => RampHost;

        /// <summary>
        /// Pass-through: this panel is a frame, the hosted control is the editor that holds the
        /// AppSettings hook. Implemented here so the rack's CurrentReplaced fan-out (which walks
        /// the entries' top-level panels) reaches the editor inside the wrapper too.
        /// </summary>
        public void RebindToCurrentSettings() => Inner.RebindToCurrentSettings();

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Mirrors what MainWindow's SetupHelpButtons used to run for the ProgressionTab twin
            // (deleted in Phase 8). Attach is idempotent.
            try
            {
                global::ConditioningControlPanel.Controls.HelpPopover.Attach(
                    HelpBtnStudioRamp,
                    global::ConditioningControlPanel.Services.HelpContentService.GetContent(HelpSectionId));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("RampRackPanel help attach failed: {E}", ex.Message);
            }
        }
    }
}
