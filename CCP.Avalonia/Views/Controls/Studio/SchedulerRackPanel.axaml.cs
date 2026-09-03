using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Studio
{
    /// <summary>
    /// Studio rack entry <c>scheduler</c>. A host, not a rewrite: the settings logic lives in
    /// <see cref="Features.SchedulerFeatureControl"/>, and this panel adds the help "?" that only
    /// the dead ProgressionTab copy ever had.
    ///
    /// <para><b>Nothing here talks to a service.</b> On WPF the scheduler is driven entirely off
    /// <c>App.Settings.Current</c>: <c>MainWindow.StartStop.cs SchedulerTimer_Tick</c> polls every
    /// 30s and starts/stops the engine.</para>
    ///
    /// <para>PORTED from ConditioningControlPanel/Views/Controls/Studio/SchedulerRackPanel.xaml.cs.
    /// Deviations: <c>ISettingsRebindable</c> / <c>RebindToCurrentSettings</c> are dropped - the
    /// ported <see cref="Features.SchedulerFeatureControl"/> has no settings hook to rebind yet -
    /// and <c>OnLoaded</c> is gone with them, since its whole body was the help attach.</para>
    /// </summary>
    public partial class SchedulerRackPanel : UserControl
    {
        /// <summary>
        /// Bark key for <c>BarkService.NotifyFeatureOpened</c>. The rack must keep firing the key
        /// the popup path fired, and the popup that hosted Scheduler+Ramp was
        /// <c>SchedulerRampFeatureControl</c> -> <c>"SchedulerRamp"</c>. That string is the
        /// <c>feature_eq</c> value of a live rule in all three built-in mods' bark_rules.json;
        /// deriving a key from THIS type's name would fire nothing. The ramp panel returns the
        /// same key on purpose - one rule, two doors, cooldown handles the double-fire.
        /// </summary>
        internal const string BarkFeatureKey = "SchedulerRamp";

        public SchedulerRackPanel()
        {
            AvaloniaXamlLoader.Load(this);
            // ponytail: needs Controls.HelpPopover.Attach + Services.HelpContentService
            // .GetContent("Scheduler") on HelpBtnStudioScheduler, wired when they move to Core.
        }

        /// <summary>The live control this panel hosts, for callers that need the real editor.</summary>
        internal Features.SchedulerFeatureControl Inner =>
            this.FindControl<Features.SchedulerFeatureControl>("SchedulerHost")!;
    }
}
