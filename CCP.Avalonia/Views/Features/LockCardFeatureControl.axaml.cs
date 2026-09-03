using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Lock Card settings panel, ported from the WPF head. <see cref="LockCardSettings"/> stands
    /// in for <c>App.Settings.Current</c>. Save, the LockCardService calls, the strict-lock double
    /// warning, MicConsentDialog, the phrase editor write-back, LockCardColorDialog, MessageBox and
    /// the mod-art repaint are stubbed. The voice hint keeps the WPF literals; with no speech
    /// service the "on" branch lands on "No microphone detected", which is the honest answer here.
    /// </summary>
    public partial class LockCardFeatureControl : UserControl
    {
        private bool _isLoading = true;
        private readonly LockCardSettings _s = new();

        public LockCardFeatureControl()
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            // ponytail: needs App.LockCard Start/Stop/TestLockCard, wired when it moves to Core
            ChkEnable.IsCheckedChanged += (_, _) => { if (_isLoading) return; _s.LockCardEnabled = ChkEnable.IsChecked ?? false; Save(); };
            SliderFreq.ValueChanged += (_, e) => { if (_isLoading) return; var v = (int)e.NewValue; TxtFreq.Text = v.ToString(); _s.LockCardFrequency = v; Save(); };
            SliderRepeats.ValueChanged += (_, e) => { if (_isLoading) return; var v = (int)e.NewValue; TxtRepeats.Text = $"{v}x"; _s.LockCardRepeats = v; Save(); };
            // ponytail: needs WarningDialog.ShowDoubleWarning (WPF head); writes through without the confirm
            ChkStrict.IsCheckedChanged += (_, _) => { if (_isLoading) return; _s.LockCardStrict = ChkStrict.IsChecked ?? false; Save(); };
            // ponytail: needs MicConsentDialog (WPF head); writes through without the consent gate
            ChkVoiceMode.IsCheckedChanged += (_, _) => { if (_isLoading) return; _s.LockCardVoiceMode = ChkVoiceMode.IsChecked ?? false; Save(); UpdateVoiceHint(); };
            // ponytail: Views/Dialogs/TextEditorDialog is ported, but the write-back is App.Settings; wired with it
            BtnManagePhrases.Click += (_, _) => { };
            BtnTest.Click += (_, _) => { };
            // ponytail: needs LockCardColorDialog, ported separately
            BtnColorSettings.Click += (_, _) => { };

            LoadFromSettings();
        }

        private void LoadFromSettings()
        {
            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = _s.LockCardEnabled;
                SliderFreq.Value = _s.LockCardFrequency;
                TxtFreq.Text = _s.LockCardFrequency.ToString();
                SliderRepeats.Value = _s.LockCardRepeats;
                TxtRepeats.Text = $"{_s.LockCardRepeats}x";
                ChkStrict.IsChecked = _s.LockCardStrict;
                ChkVoiceMode.IsChecked = _s.LockCardVoiceMode && _s.MicConsentGiven;
                UpdateVoiceHint();
            }
            finally { _isLoading = false; }
        }

        /// <summary>Refresh the grey hint under the voice toggle to reflect mic availability.</summary>
        private void UpdateVoiceHint()
        {
            var on = ChkVoiceMode.IsChecked ?? false;
            if (!on)
            {
                TxtVoiceHint.Text = "Say the phrase out loud instead of typing it (offline mic). Falls back to typing if no mic.";
                return;
            }
            // ponytail: needs App.Speech (IsAvailable / HasCaptureDevice / ModelStatus); no service means no mic
            TxtVoiceHint.Text = "No microphone detected — lock cards will use typing until one is connected.";
        }

        // ponytail: needs App.Settings.Save(), wired when AppSettings moves to Core
        private static void Save() { }
    }

    /// <summary>Placeholder for the AppSettings slice this panel reads. Property names match
    /// Models.AppSettings; values are the WPF XAML defaults.</summary>
    public sealed class LockCardSettings
    {
        public bool LockCardEnabled { get; set; }
        public int LockCardFrequency { get; set; } = 2;
        public int LockCardRepeats { get; set; } = 3;
        public bool LockCardStrict { get; set; }
        public bool LockCardVoiceMode { get; set; }
        public bool MicConsentGiven { get; set; }
    }
}
