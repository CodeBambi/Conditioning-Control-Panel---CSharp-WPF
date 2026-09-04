using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Models;

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
    /// <para><b>What is real here.</b> The mic-sensitivity dial is a stored threshold on
    /// <see cref="AppSettings.SpeechLoudnessThreshold"/> (Core), so its load, its readout and its
    /// save are the WPF round-trip verbatim - see <see cref="SensToThreshold"/>. The two
    /// audio_whispers plates are painted through <see cref="Helpers.ModArt.TryLoad"/> and repainted
    /// on <see cref="CoreMods.ModChanged"/>, which is the WPF ModResourceResolver behaviour split
    /// across the seam.</para>
    ///
    /// <para><b>What is deliberately still a stub.</b> The remaining buttons forward to
    /// ConditioningControlPanel/MainWindow/MainWindow.SheListening.cs, which owns the mic itself
    /// (App.Speech), the wake-word calibration and the premium gate - none of that is on this head.
    /// <c>ChkSL_Mantras</c> is REFUSED rather than stubbed-with-persistence: on WPF
    /// (MainWindow.SheListening.cs:51) turning it on opens MicConsentDialog and reverts the box when
    /// consent is declined. Writing the setting here would record "spoken mantras on" with the
    /// consent gate never shown. <c>BtnSL_MicMaster</c> is refused for the sibling reason - it would
    /// read "Stop listening" over a device nothing has opened.</para>
    /// </summary>
    public partial class SheListeningTabView : UserControl
    {
        // MainWindow.SheListening.cs:406 - the slider's two ends, copied so the stored threshold
        // means the same thing on both heads.
        private const double LoudThrAtMinSens = 0.045; // slider 0%
        private const double LoudThrAtMaxSens = 0.004; // slider 100%

        private static double SensToThreshold(double sens)
            => LoudThrAtMinSens - (LoudThrAtMinSens - LoudThrAtMaxSens) * (Math.Clamp(sens, 0, 100) / 100.0);
        private static double ThresholdToSens(double thr)
            => Math.Clamp((LoudThrAtMinSens - thr) / (LoudThrAtMinSens - LoudThrAtMaxSens) * 100.0, 0, 100);

        private bool _isLoading;

        public SheListeningTabView()
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            // Refused, not missing - see the class note. Each would need
            // ConditioningControlPanel/MainWindow/MainWindow.SheListening.cs (App.Speech, the wake
            // calibration, the premium gate, MicConsentDialog).
            BtnSL_MicMaster.Click += (_, _) => { };          // mw.ToggleVoiceMic()
            BtnSL_OpenDeviceSettings.Click += (_, _) => { }; // mw.OpenDeviceSettings()
            BtnSL_Calibrate.Click += (_, _) => { };          // mw.SL_Calibrate_Click(...)
            BtnSL_TestMantra.Click += (_, _) => { };         // mw.BtnTestVoice_Click(...)
            BtnSL_RevokeConsent.Click += (_, _) => { };      // mw.SL_RevokeMicConsent_Click(...)
            BtnSL_GateUnlock.Click += (_, _) => { };         // mw.BtnGateUnlock_Click(...)
            ChkSL_Mantras.IsCheckedChanged += (_, _) => { }; // mw.SL_Mantras_Changed(...) - consent gate

            // MainWindow.SheListening.cs:419, both halves. The readout uses Math.Round, not a
            // truncating cast, so 49.6 reads 50 the way it does on WPF.
            SldSL_MicSensitivity.ValueChanged += (_, e) =>
            {
                TxtSL_MicSensitivity.Text = $"{(int)Math.Round(e.NewValue)}%";
                if (_isLoading) return;
                CoreSettings.Current.SpeechLoudnessThreshold = SensToThreshold(e.NewValue);
                CoreSettings.Save();
            };

            _isLoading = true;
            SldSL_MicSensitivity.Value = ThresholdToSens(CoreSettings.Current.SpeechLoudnessThreshold);
            _isLoading = false;

            ApplyFeatureArt();
        }

        protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            CoreMods.ModChanged += OnModChanged;
            ApplyFeatureArt();
        }

        protected override void OnDetachedFromVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
        {
            CoreMods.ModChanged -= OnModChanged;
            base.OnDetachedFromVisualTree(e);
        }

        /// <summary>ModChanged can be raised off the UI thread, so the repaint is marshalled.</summary>
        private void OnModChanged(object? sender, ModPackage mod) =>
            Dispatcher.UIThread.Post(ApplyFeatureArt);

        /// <summary>
        /// The WPF ImageBrush pair, resolved mod-first. A null answer means neither the mod nor this
        /// head has the picture, and the authored surface - a bare hero, the wash on the side card -
        /// stands, which is what WPF's resolver falls back to as well.
        /// </summary>
        private void ApplyFeatureArt()
        {
            var art = Helpers.ModArt.TryLoad("features/audio_whispers.png");
            if (art == null) return;

            SheListeningHeroArt.Background = new ImageBrush(art)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Right,
            };
            SheListeningSideArt.Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill };
        }
    }
}
