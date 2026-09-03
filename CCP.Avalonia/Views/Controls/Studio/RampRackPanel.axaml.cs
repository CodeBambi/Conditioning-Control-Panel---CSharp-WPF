using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Studio
{
    /// <summary>
    /// Studio rack entry <c>ramp</c>. Hosts the live
    /// <see cref="Features.IntensityRampFeatureControl"/> (which owns every handler and writes
    /// <c>App.Settings.Current</c> on the WPF head) and adds the help "?" that only the dead
    /// ProgressionTab copy carried.
    ///
    /// <para>No service is touched from here on either head. The global ramp runs on MainWindow's
    /// own <c>RampTimer_Tick</c>; the per-session ramp is <c>SessionEngine.UpdateRampingValues</c>.
    /// Both read the settings the hosted control writes.</para>
    ///
    /// <para>PORTED from ConditioningControlPanel/Views/Controls/Studio/RampRackPanel.xaml.cs.
    /// Deviations: <c>ISettingsRebindable</c> / <c>RebindToCurrentSettings</c> are dropped - the
    /// ported <see cref="Features.IntensityRampFeatureControl"/> has no settings hook to rebind
    /// yet - and <c>OnLoaded</c> is gone with them, since its whole body was the help attach.</para>
    /// </summary>
    public partial class RampRackPanel : UserControl
    {
        /// <summary>
        /// Bark key for <c>NotifyFeatureOpened</c>. Deliberately the same <c>"SchedulerRamp"</c>
        /// the popup fired (the popup was one control for both halves) - it is the only
        /// <c>feature_eq</c> value with rules in the mods' bark_rules.json.
        /// See <see cref="SchedulerRackPanel.BarkFeatureKey"/>.
        /// </summary>
        internal const string BarkFeatureKey = "SchedulerRamp";

        public RampRackPanel()
        {
            AvaloniaXamlLoader.Load(this);
            // ponytail: needs Controls.HelpPopover.Attach + Services.HelpContentService
            // .GetContent("IntensityRamp") on HelpBtnStudioRamp, wired when they move to Core.
        }

        /// <summary>The live control this panel hosts, for callers that need the real editor.</summary>
        internal Features.IntensityRampFeatureControl Inner =>
            this.FindControl<Features.IntensityRampFeatureControl>("RampHost")!;
    }
}
