using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// System panel, ported from the WPF head. Five live toggles plus five read-out rows that
    /// LoadFromSettings paints from App.Settings. AppSettings, StartupManager and MainWindow's
    /// navigation (OpenAppSettingsSection / OpenDeviceSettings / RequestPickAssetsFolder) are all
    /// still in the WPF head, so the read-outs show the "nothing set" defaults and the buttons
    /// are inert.
    /// </summary>
    public partial class SystemFeatureControl : UserControl
    {
        public SystemFeatureControl()
        {
            AvaloniaXamlLoader.Load(this);
            LoadFromSettings();
            // ponytail: needs App.Settings + MainWindow navigation, wired when they move to Core.
            // ChkMultiMon / ChkFillAllMon / ChkVideoGpuDecode / ChkVideoBlurBg / ChkBrowserVideoEngine
            // are pure settings writes; BtnOpen* / BtnPickAssets / BtnOpenAssets call MainWindow.
        }

        /// <summary>
        /// Placeholder mirror of the WPF LoadFromSettings: same keys, "nothing configured" values.
        /// TxtNoPanicState / TxtOfflineModeState / TxtStartupVideo keep their markup defaults
        /// (set2_chip_off / label_random), exactly what WPF paints for a fresh settings file.
        /// </summary>
        private void LoadFromSettings()
        {
            this.FindControl<TextBlock>("TxtStartupGroupState")!.Text = Loc.Get("set2_startup_group_none");
            this.FindControl<TextBlock>("TxtPanicKeyState")!.Text = "🔑 —"; // WPF: $"🔑 {s.PanicKey}"
        }
    }
}
