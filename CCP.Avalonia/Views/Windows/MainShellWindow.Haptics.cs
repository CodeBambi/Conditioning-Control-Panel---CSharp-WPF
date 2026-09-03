// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.Haptics.cs (1,158 lines) -
// the Haptics page's entire brain on WPF: 34 forwarding handlers from HapticsTabView land here.
// ONE member is restored, and it is the only one in the file that does not need a control:
// BtnHapticsHelp_Click, which opens the setup wizard. CCP.Avalonia/Views/Windows/
// HapticsSetupWindow.axaml.cs is a real port - three pages, the provider guides, the mod accent,
// and the Lovense address read from and written back to CoreSettings - and NOTHING ON THIS HEAD
// OPENED IT. Now something can. (Uncalled: see the caller note below.)
//
// THE REST IS BLOCKED ON THE PAGE, NOT ON THE PORT - the correction this file needed. The old
// header said "wholesale stub, every member reaches App.* / a device", which sent the reader
// looking for HapticService. The nearer truth: CCP.Avalonia/Views/Tabs/HapticsTabView.axaml IS
// PORTED - the whole page, all four DataTemplates, its ControlThemes and its HapticStatusDot. It
// is simply NOT HOSTED: CCP.Avalonia/Views/Tabs/StudioTabView.axaml:249 still carries a Border
// placard named PanelHaptics, whose own comment ("HapticsTabView is not ported to this head yet")
// is now stale. Until that Border becomes <tabs:HapticsTabView x:Name="PanelHaptics"/>, every
// control lookup from this partial resolves to null and every member restored here would be a
// silent no-op - the x:Name hazard one level further out.
// MainShellWindow.TabFxTakeoverLabStatus.cs:296 reaches through the same two hops and says so too.
//
// AND DO NOT CALL SetHapticsStatusPulse FROM HERE. The entry point exists and names this file as
// the caller it waits for, but what it reports is `haptics.IsConnected` and there is no haptic
// service here to be connected. A dot breathing "device connected" over no device is the failure
// this port refuses on principle, and on a page whose subject is hardware that touches the user it
// is the worst place to make it. Wire it from RefreshHapticConnectionUi when that is real.
//
// A SECOND TRAP FOR THE SETTINGS HALF. HapticSettings and HapticSettingsV2 ARE in Core
// (CCP.Core/Models/HapticSettings.cs), so the ~30 editors below look free. They are not: the page
// has no LoadHapticsSettingsToUi pass here, so its controls start on MARKUP defaults, and Avalonia
// raises IsCheckedChanged/ValueChanged on a PROGRAMMATIC set as well as a user one - restore an
// editor first and the tab's first layout writes the markup default over the user's saved value.
// WPF is safe only because MainWindow loads settings in first, guarded by _isLoading. Restore the
// load pass (with that guard and compare-before-write) FIRST, then the editors.
//
// Blocked, grouped (77 members):
//   * the device transport and the mixer - App.Haptics (Services/Haptics/HapticService.cs),
//     HapticDeviceManager, HapticMixer, DtrhHapticDirector, MockHapticProvider, none with a Core
//     seam: OnHapticDevicesChanged, OnHapticConnectionChanged, OnHapticActivity,
//     RefreshHapticConnectionUi, RefreshHapticLiveStatus, Start/StopHapticLiveStatusTimer,
//     _hapticLiveStatusTimer, BtnHapticConnect_Click, BtnHapticPanic_Click, BtnHapticTest_Click,
//     BtnHapticToyTest_Click, BtnPatternPlay_Click, RefreshPatternToyPicker, RefreshHapticToys,
//     BuildHapticToyShapeSignature, _hapticToyCardsShape.
//   * the row view models - Views/Controls/HapticUiModels.cs's four types expose
//     System.Windows.Visibility, so they cannot move as-is (HapticsTabView seeds its own *Sample
//     twins): the three ObservableCollections, _hapticRowScope, OnHapticRoutingRowChanged,
//     RefreshHapticRoutingRows.
//   * the page host (see above) - InitializeHapticsTab, _hapticsTabBuilt,
//     OnHapticsTabVisibilityChanged, RefreshAudioSyncCardVisibility, HapticsRunOnUi (=
//     Dispatcher.UIThread.Post), SafeRun, SetSliderEnabled, HapticCfg, _hapticSliderDebounce.
//   * the settings editors, blocked on the load pass above and not on a service (~35): every
//     Chk*_Changed, Txt*_TextChanged, Slider*_Changed/_ValueChanged, Cmb*_SelectionChanged,
//     RbHapticTemperament_Checked, HapticTemperamentChips, the four Load*ToUi helpers,
//     ApplyHapticToyInputEnabledState, SetDspSlider, FormatDsp, BtnDspReset_Click,
//     OnDspSliderChanged, SelectedPatternMode, SelectedPatternIntensity.
//   * the pattern preview - UpdateHapticPatternPreview and PatternPreviewCanvas_SizeChanged draw
//     Services/Haptics/Core/HapticPatterns.cs curves onto a Canvas: the drawing is portable, the
//     curve source is not.
//
// CALLER STILL MISSING for the one restored member: HapticsTabView.axaml.cs carries the WPF
// forward as a stub. `(TopLevel.GetTopLevel(this) as MainShellWindow)?.BtnHapticsHelp_Click(sender,
// e)` is the whole of it - reachable once StudioTabView hosts the page. Neither file is this
// layer's.

using System;
using Avalonia.Interactivity;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// Opens the haptics setup wizard. async void and awaited rather than blocking: Avalonia's
        /// ShowDialog is awaitable where WPF's blocks, and it needs an explicit owner.
        ///
        /// <para>WPF follows the dialog with LoadHapticsSettingsToUi + RefreshHapticToys(force),
        /// because the wizard rewrites provider flags and the Lovense address behind the page. Both
        /// are omitted here rather than approximated: neither exists on this head (see the header),
        /// and the wizard's writes go to CoreSettings, so nothing is lost - the page will read them
        /// whenever it gains a load pass.</para>
        /// </summary>
        internal async void BtnHapticsHelp_Click(object? sender, RoutedEventArgs e)
        {
            try { await new HapticsSetupWindow().ShowDialog(this); }
            catch (Exception ex) { Log.Error(ex, "HapticsSetupWindow failed"); }
        }
    }
}
