using System;
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// "She's Listening" - the voice-control Exclusive, PORTED from
    /// ConditioningControlPanel/Views/Tabs/SheListeningTabView.xaml.cs. A purpose-built surface for
    /// the offline mic features (spoken mantras + the "Hey Bambi" voice commands), with a command
    /// cheat-sheet. The microphone hardware and the voice input modes (device, wake word,
    /// push-to-talk, headphone barge-in) are owned by Settings &gt; Devices since Phase 2 of the UX
    /// restructure and appear here only as read-only chips.
    ///
    /// <para><b>Every handler on the WPF head is a one-line shim</b> - it looks up the hosting
    /// MainWindow and forwards to a MainWindow partial (SL_Mantras_Changed, SL_Calibrate_Click,
    /// OpenDeviceSettings, ToggleVoiceMic, SL_MicSensitivity_Changed, BtnTestVoice_Click,
    /// BtnGateUnlock_Click, SL_RevokeMicConsent_Click). None of those partials is on this head, so
    /// each one is a stub here; there is no view-only handler to port. The sensitivity readout is
    /// the exception: painting a percentage next to the slider is pure view state, so it is real.</para>
    ///
    /// <para>The mod-aware feature art (ModService.ModChanged -&gt; ModResourceResolver repainting
    /// the two audio_whispers.png plates) is dropped with the plates themselves - see the XAML
    /// header. ponytail: restore both together when Resources/features ships on this head.</para>
    /// </summary>
    public partial class SheListeningTabView : UserControl
    {
        public SheListeningTabView()
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            // ponytail: each of these needs the MainWindow.SheListening partial (mic service, wake
            // calibration, premium gate, consent store); wired when they move to Core.
            BtnSL_MicMaster.Click += (_, _) => { };          // mw.ToggleVoiceMic()
            BtnSL_OpenDeviceSettings.Click += (_, _) => { }; // mw.OpenDeviceSettings()
            BtnSL_Calibrate.Click += (_, _) => { };          // mw.SL_Calibrate_Click(...)
            BtnSL_TestMantra.Click += (_, _) => { };         // mw.BtnTestVoice_Click(...)
            BtnSL_RevokeConsent.Click += (_, _) => { };      // mw.SL_RevokeMicConsent_Click(...)
            BtnSL_GateUnlock.Click += (_, _) => { };         // mw.BtnGateUnlock_Click(...)
            ChkSL_Mantras.IsCheckedChanged += (_, _) => { }; // mw.SL_Mantras_Changed(...)

            // View-only half of SL_MicSensitivity_Changed: the readout beside the slider. Format
            // copied verbatim from MainWindow.SheListening.cs:427 - Math.Round, not a truncating
            // cast, so 49.6 reads 50 the way it does on WPF. The settings write that handler also
            // does is the stubbed half.
            SldSL_MicSensitivity.ValueChanged += (_, e) => TxtSL_MicSensitivity.Text = $"{(int)Math.Round(e.NewValue)}%";

            // Placeholder start value; the real one comes from settings when the mic service lands.
            SldSL_MicSensitivity.Value = 50;
        }
    }
}
