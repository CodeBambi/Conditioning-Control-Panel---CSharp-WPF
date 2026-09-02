using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// Settings door · DEVICES, ported from the WPF head. Every string is static
    /// ({loc:Str}) or a markup default that MainWindow partials overwrite at runtime on WPF
    /// (TxtPttKey, BtnPanicKey, TxtWebcamDebugStatus, the shortcut labels, the counters); no
    /// <c>Loc.GetF</c> is involved in this view.
    ///
    /// Only the two precision sliders are wired: they touch nothing but their sibling TextBlock.
    /// Everything else in the WPF code-behind is either a <c>Window.GetWindow(this) is MainWindow</c>
    /// hop or reads App.Settings / SpeechService, and stays unwired here - a stub that pretends to
    /// save a device choice or a panic-key toggle would be worse than an inert control.
    ///
    /// The WPF class also implements <c>IAppSettingsSection.OnSectionShown</c> (device list
    /// re-enumeration). That interface lives in the WPF head and is not declared here.
    /// </summary>
    public partial class DevicesSettingsSection : UserControl
    {
        // ponytail: needs App.Settings + SpeechService, wired when they move to Core:
        //   LoadMicSection / PopulateMicDevices / CmbMicDevice_SelectionChanged /
        //   BtnMicRefresh_Click / ChkHeadphones_Changed / OnSectionShown,
        //   and every MainWindow hop (webcam bar, voice modes, panic key, hotkeys).

        public DevicesSettingsSection()
        {
            AvaloniaXamlLoader.Load(this);

            var wake = this.FindControl<Slider>("SliderWakePrecision")!;
            var cmd = this.FindControl<Slider>("SliderCmdPrecision")!;
            var txtWake = this.FindControl<TextBlock>("TxtWakeVal")!;
            var txtCmd = this.FindControl<TextBlock>("TxtCmdVal")!;

            // WPF needed a _loading guard because ValueChanged fired during InitializeComponent;
            // here the handlers attach after Load, so the guard has nothing to guard.
            wake.ValueChanged += (_, e) => txtWake.Text = e.NewValue.ToString("0.00");
            cmd.ValueChanged += (_, e) => txtCmd.Text = e.NewValue.ToString("0.00");
        }
    }
}
