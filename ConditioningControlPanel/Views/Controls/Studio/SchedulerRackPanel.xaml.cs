using System;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls.Studio
{
    /// <summary>
    /// Studio rack entry <c>scheduler</c>. A host, not a rewrite: the settings logic lives in
    /// <see cref="Features.SchedulerFeatureControl"/> (loaded/saved by its own handlers), and
    /// this panel adds the help "?" that only the dead ProgressionTab copy ever had.
    ///
    /// <para><b>Nothing here talks to a service.</b> The scheduler is driven entirely off
    /// <c>App.Settings.Current</c>: <c>MainWindow.StartStop.cs SchedulerTimer_Tick</c> polls
    /// every 30s and starts/stops the engine. The one thing the old Save path did that no live
    /// editor does is call <c>CheckSchedulerAfterSettingsChange()</c> (private on MainWindow) for
    /// an immediate check on enable - so enabling from here arms within one poll tick instead of
    /// instantly. Reported rather than worked around: reaching into a private MainWindow method
    /// from a rack panel would be worse than a 30s delay.</para>
    ///
    /// <para>PHASE 8 CONFIRMS THIS: the ghost tab's read-back is gone, so the rising-edge guard in
    /// <c>MainWindow.SaveSettings</c> can no longer fire at all. If the instant arm is ever wanted
    /// back, the kick belongs in <see cref="Features.SchedulerFeatureControl"/>, not in
    /// SaveSettings.</para>
    /// </summary>
    public partial class SchedulerRackPanel : UserControl
    {
        /// <summary>
        /// Bark key for <c>BarkService.NotifyFeatureOpened</c>. The rack must keep firing the key
        /// the popup path fired, and the popup that hosted Scheduler+Ramp was
        /// <c>SchedulerRampFeatureControl</c> -> <c>"SchedulerRamp"</c>. That string is the
        /// <c>feature_eq</c> value of a live rule in all three built-in mods' bark_rules.json;
        /// deriving a key from THIS type's name ("SchedulerRackPanel") would fire nothing.
        /// The ramp panel returns the same key on purpose - one rule, two doors, cooldown handles
        /// the double-fire.
        /// </summary>
        internal const string BarkFeatureKey = "SchedulerRamp";

        /// <summary>Help topic id, matching the dead ProgressionTab button's Tag.</summary>
        private const string HelpSectionId = "Scheduler";

        public SchedulerRackPanel()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        /// <summary>The live control this panel hosts, for callers that need the real editor.</summary>
        internal global::ConditioningControlPanel.Features.SchedulerFeatureControl Inner => SchedulerHost;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Same two lines MainWindow's SetupHelpButtons used to run for the ProgressionTab twin
            // (deleted in Phase 8, help button included - this is now the only "Scheduler" ? in
            // the app). Attach is idempotent (it clears any previous popover first), so re-running
            // it on a later Loaded - or after a language change - is free.
            try
            {
                global::ConditioningControlPanel.Controls.HelpPopover.Attach(
                    HelpBtnStudioScheduler,
                    global::ConditioningControlPanel.Services.HelpContentService.GetContent(HelpSectionId));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("SchedulerRackPanel help attach failed: {E}", ex.Message);
            }
        }
    }
}
