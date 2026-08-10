using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// Settings · AUDIO compat surface.
    ///
    /// <para>These controls used to live in the dashboard's <c>AudioSection</c> and resolved as
    /// <c>SettingsTab.SliderMaster</c> and friends. They now live inside
    /// <see cref="Views.Controls.AppSettingsSections.AudioSettingsSection"/>, so the MainWindow partials that
    /// still drive them (<c>MainWindow.Settings.cs</c> load/save, <c>.StartStop.cs</c> ramping,
    /// <c>.UiUpdates.cs</c> device enumeration, <c>.Patreon.cs</c> voice ducking, <c>.Presets.cs</c>
    /// help wiring) reach them through this passthrough block instead.</para>
    ///
    /// <para>Every one of those call sites dereferences without a null guard — exactly as the old
    /// dashboard code did — so <c>SectionAudio</c> must stay a XAML-instantiated child of this view.
    /// Do not make the sections lazy.</para>
    /// </summary>
    public partial class AppSettingsTabView
    {
        internal Border    AudioSection                => SectionAudio.AudioSection;
        internal Button    HelpBtnAudio                => SectionAudio.HelpBtnAudio;
        internal Slider    SliderMaster                => SectionAudio.SliderMaster;
        internal TextBlock TxtMaster                   => SectionAudio.TxtMaster;
        internal Slider    SliderVideoVolume           => SectionAudio.SliderVideoVolume;
        internal TextBlock TxtVideoVolume              => SectionAudio.TxtVideoVolume;
        internal CheckBox  ChkAudioDuck                => SectionAudio.ChkAudioDuck;
        internal Slider    SliderDuck                  => SectionAudio.SliderDuck;
        internal TextBlock TxtDuck                     => SectionAudio.TxtDuck;
        internal CheckBox  ChkExcludeBambiCloudDucking => SectionAudio.ChkExcludeBambiCloudDucking;
        internal ComboBox  CmbAudioOutputDevice        => SectionAudio.CmbAudioOutputDevice;
        internal Button    BtnAudioOutputRefresh       => SectionAudio.BtnAudioOutputRefresh;
        internal Button    BtnTestAudio                => SectionAudio.BtnTestAudio;
        internal Button    BtnAudioLayers              => SectionAudio.BtnAudioLayers;

        // Phase 3: the audio-sync tuning pair followed the rest of AudioSection out of the
        // dashboard. MainWindow.Haptics.cs (:254 visibility, :941/:955 cross-writes,
        // :959/:974 handlers) and MainWindow.xaml.cs (:2404-2421 load) reached these as
        // SettingsTab.<Name>; they are AppSettingsTab.<Name> now.
        internal Border    AudioSyncLatencyPanel       => SectionAudio.AudioSyncLatencyPanel;
        internal Slider    SliderAudioSyncLatency      => SectionAudio.SliderAudioSyncLatency;
        internal TextBlock TxtAudioSyncLatency         => SectionAudio.TxtAudioSyncLatency;
        internal Slider    SliderAudioSyncIntensity    => SectionAudio.SliderAudioSyncIntensity;
        internal TextBlock TxtAudioSyncIntensity       => SectionAudio.TxtAudioSyncIntensity;
    }
}
