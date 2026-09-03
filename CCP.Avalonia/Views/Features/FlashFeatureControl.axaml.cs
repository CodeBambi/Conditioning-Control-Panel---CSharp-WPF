using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Flash Images panel, ported from the WPF head. Slider read-outs are real; every toggle and
    /// slider also writes App.Settings on WPF (and ChkEnable / SliderFrequency poke FlashService),
    /// which is not portable yet.
    /// </summary>
    public partial class FlashFeatureControl : UserControl
    {
        public FlashFeatureControl()
        {
            AvaloniaXamlLoader.Load(this);

            SliderLabel.Wire(this, "SliderFrequency", "TxtFrequency", v => ((int)v).ToString());
            SliderLabel.Wire(this, "SliderImages", "TxtImages", v => ((int)v).ToString());
            SliderLabel.Wire(this, "SliderMaxOnScreen", "TxtMaxOnScreen", v => ((int)v).ToString());
            SliderLabel.Wire(this, "SliderCenterExclusion", "TxtCenterExclusion", v => $"{(int)v}%");
            SliderLabel.Wire(this, "SliderFlashLingerMs", "TxtFlashLingerMs", v => $"{(int)v} ms");

            // ponytail: needs App.Settings / App.Flash, wired when they move to Core. The ten Chk*
            // toggles are settings writes only.
        }
    }
}
