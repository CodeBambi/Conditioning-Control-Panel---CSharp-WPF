using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Bubble Pop panel, ported from the WPF head. The slider read-outs, the trigger-options
    /// reveal and the two formatted lines are real; the settings writes, the BubbleService
    /// start/stop and the persona lookup (App.Mods.ActiveModId) are WPF-head services.
    /// </summary>
    public partial class BubblePopFeatureControl : UserControl
    {
        public BubblePopFeatureControl()
        {
            AvaloniaXamlLoader.Load(this);

            SliderLabel.Wire(this, "SliderFreq", "TxtFreq", v => ((int)v).ToString());
            SliderLabel.Wire(this, "SliderVolume", "TxtVolume", v => $"{(int)v}%");
            SliderLabel.Wire(this, "SliderSize", "TxtSize", v => $"{(int)v}%");
            SliderLabel.Wire(this, "SliderSpeed", "TxtSpeed", v => $"+{(int)v}%");
            SliderLabel.Wire(this, "SliderTriggerChance", "TxtTriggerChance", v => $"{(int)v}%");

            var triggers = this.FindControl<CheckBox>("ChkTriggers")!;
            var options = this.FindControl<StackPanel>("TriggerOptionsPanel")!;
            triggers.IsCheckedChanged += (_, _) => options.IsVisible = triggers.IsChecked ?? false;

            // WPF: persona from App.Mods.ActiveModId; "your companion" is its fallback arm.
            this.FindControl<TextBlock>("TxtTriggerEggHint")!.Text = "careful — your companion loves these…";
            // WPF: BubbleService.AmbientBubbleXpPaidToday() / AmbientBubbleDailyXpCap (300).
            this.FindControl<TextBlock>("TxtAmbientXpBudget")!.Text = Loc.GetF("label_ambient_bubble_xp_budget", 0, 300);

            // ponytail: needs App.Settings / App.Bubbles / App.Mods, wired when they move to Core.
            // ChkEnable, ChkSolidMode and the ChkType* effect checkboxes are settings writes only.
        }
    }
}
