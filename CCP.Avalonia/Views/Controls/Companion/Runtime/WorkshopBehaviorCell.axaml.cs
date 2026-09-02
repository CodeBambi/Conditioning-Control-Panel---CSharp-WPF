using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime
{
    /// <summary>Z8 · BEHAVIOR. See the XAML header. Pure forwarding, like every re-parented cell.
    ///
    /// <para>The WPF code-behind forwards every handler to MainWindow, which writes App.Settings.
    /// Neither is on this head, so the host-owned actions leave the cell as events (the same
    /// contract WorkshopCommunityCell chose) and only the two behaviours local to the cell are
    /// wired: the slider value labels (MainWindow.Patreon.cs:1182/1194).</para>
    /// </summary>
    public partial class WorkshopBehaviorCell : UserControl
    {
        public event EventHandler? ChatShortcutRequested;
        public event EventHandler? CameraShortcutRequested;
        /// <summary>Raised with the new IsChecked of the switch that changed.</summary>
        public event EventHandler<bool>? MuteWhispersChanged;
        public event EventHandler<bool>? PauseBrowserChanged;
        public event EventHandler<bool>? VoiceLinesChanged;
        public event EventHandler<bool>? TubeMidnightGlassChanged;

        public WorkshopBehaviorCell()
        {
            AvaloniaXamlLoader.Load(this);

            var idle = this.FindControl<Slider>("SliderIdleIntervalCompanion")!;
            var idleText = this.FindControl<TextBlock>("TxtIdleIntervalCompanion")!;
            idle.ValueChanged += (_, _) => idleText.Text = $"{(int)idle.Value}s";

            var bubble = this.FindControl<Slider>("SliderBubbleDurationCompanion")!;
            var bubbleText = this.FindControl<TextBlock>("TxtBubbleDurationCompanion")!;
            bubble.ValueChanged += (_, _) => bubbleText.Text = $"{(int)bubble.Value}s";

            this.FindControl<Button>("BtnChatShortcut")!.Click += (_, _) => ChatShortcutRequested?.Invoke(this, EventArgs.Empty);
            this.FindControl<Button>("BtnCameraShortcut")!.Click += (_, _) => CameraShortcutRequested?.Invoke(this, EventArgs.Empty);

            Wire("ChkMuteWhispersCompanion", v => MuteWhispersChanged?.Invoke(this, v));
            Wire("ChkPauseBrowserCompanion", v => PauseBrowserChanged?.Invoke(this, v));
            Wire("ChkVoiceLinesCompanion", v => VoiceLinesChanged?.Invoke(this, v));
            Wire("ChkTubeMidnightGlass", v => TubeMidnightGlassChanged?.Invoke(this, v));

            // ponytail: needs ArcademyHostService.WalletOwnsSku(SkuTubeMidnight) + App.Settings;
            // wired when they move to Core. Until then the honest state for a prize we cannot
            // prove was sold: greyed out, unchecked.
            this.FindControl<CheckBox>("ChkTubeMidnightGlass")!.IsEnabled = false;
        }

        // WPF wired Checked/Unchecked separately; Avalonia has one event for both.
        private void Wire(string name, Action<bool> onChanged)
        {
            var box = this.FindControl<CheckBox>(name)!;
            box.IsCheckedChanged += (_, _) => onChanged(box.IsChecked == true);
        }
    }
}
