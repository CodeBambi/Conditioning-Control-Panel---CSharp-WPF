// STILL A STUB from ConditioningControlPanel/MainWindow/MainWindow.HomeAudio.cs (185 lines) -
// RESTORABLE, DEFERRED, not head-side. Nothing in this file touches audio: it is a strict two-way
// mirror between the dashboard's audio card and the canonical Settings/Audio controls, so that
// master volume and ducking have exactly one owner and two surfaces.
//
// It is deferred because BOTH ENDS ARE INERT ON THIS HEAD, and a mirror between two dead controls
// would only move a slider that changes nothing:
//   * the six forwarding handlers on the dashboard card are empty stubs -
//     Views/Tabs/SettingsTabView.axaml.cs:315-320 (HomeSliderMaster_Changed and its five siblings);
//   * the canonical controls' handlers are empty stubs too -
//     Views/Controls/AppSettings/AudioSettingsSection.axaml.cs:29-38 (SliderMaster_Changed, …),
//     because the audio engine and the NAudio device enumeration are still head-only;
//   * AppSettingsTabView does not re-publish SliderMaster / ChkAudioDuck / SliderVideoVolume /
//     SliderDuck / ChkExcludeBambiCloudDucking / CmbAudioOutputDevice the way WPF's AppSettingsTab
//     does, so the mirror would have to reach through the section itself;
//   * _populatingAudioOutputs, one of the three guards, is a dropped member of
//     MainShellWindow.UiUpdates.cs.
// Wire it in the same layer that makes AudioSettingsSection real, so the canonical handler exists to
// do the work the mirror deliberately does not duplicate.
//
// Members still absent (8): _homeAudioMirroring, HomeSliderMaster_Changed,
// HomeChkAudioDuck_Changed, HomeSliderVideoVolume_Changed, HomeSliderDuck_Changed,
// HomeChkExcludeBambiCloudDucking_Changed, HomeCmbAudioOutputDevice_SelectionChanged,
// MirrorAudioToHome.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.
    }
}
