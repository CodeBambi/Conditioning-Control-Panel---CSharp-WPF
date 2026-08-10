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
    public partial class RampRackPanel : UserControl
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

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Mirrors MainWindow.Presets.cs:59 for the ProgressionTab twin. Attach is idempotent.
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
