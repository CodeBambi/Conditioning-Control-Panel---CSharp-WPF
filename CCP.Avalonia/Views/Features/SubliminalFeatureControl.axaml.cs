using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Subliminal settings panel, ported from the WPF head. <see cref="SubliminalSettings"/>
    /// stands in for <c>App.Settings.Current</c>. Save, SubliminalService.SetEnabled, the pool
    /// editor write-back (with its user-added / removed-default bookkeeping), ColorEditorDialog,
    /// FontPickerHelper and the mod-art repaint are stubbed; the font combo carries two
    /// placeholder families.
    /// </summary>
    public partial class SubliminalFeatureControl : UserControl
    {
        private bool _isLoading = true;
        private readonly SubliminalSettings _s = new();

        public SubliminalFeatureControl()
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            // ponytail: needs App.Subliminal.SetEnabled (the single authority on WPF), wired when it moves to Core
            ChkEnable.IsCheckedChanged += (_, _) => { if (_isLoading) return; _s.SubliminalEnabled = ChkEnable.IsChecked ?? false; Save(); };
            SliderPerMin.ValueChanged += (_, e) => { if (_isLoading) return; var v = (int)e.NewValue; TxtPerMin.Text = v.ToString(); _s.SubliminalFrequency = v; Save(); };
            SliderFrames.ValueChanged += (_, e) => { if (_isLoading) return; var v = (int)e.NewValue; TxtFrames.Text = v.ToString(); _s.SubliminalDuration = v; Save(); };
            SliderOpacity.ValueChanged += (_, e) => { if (_isLoading) return; var v = (int)e.NewValue; TxtOpacity.Text = $"{v}%"; _s.SubliminalOpacity = v; Save(); };
            ChkWhispers.IsCheckedChanged += (_, _) => { if (_isLoading) return; _s.SubAudioEnabled = ChkWhispers.IsChecked ?? false; Save(); };
            SliderWhisperVol.ValueChanged += (_, e) => { if (_isLoading) return; var v = (int)e.NewValue; TxtWhisperVol.Text = $"{v}%"; _s.SubAudioVolume = v; Save(); };
            ChkSolidMode.IsCheckedChanged += (_, _) => { if (_isLoading) return; _s.SubliminalSolidMode = ChkSolidMode.IsChecked ?? false; Save(); };
            CmbFont.SelectionChanged += (_, _) =>
            {
                if (_isLoading) return;
                var name = CmbFont.SelectedItem as string;
                if (string.IsNullOrWhiteSpace(name) || _s.SubliminalFont == name) return;
                _s.SubliminalFont = name;
                Save();
            };
            // ponytail: Views/Dialogs/TextEditorDialog is ported, but the pool bookkeeping is App.Settings/App.Mods; wired with them
            BtnManageMessages.Click += (_, _) => { };
            // ponytail: needs ColorEditorDialog, ported separately
            BtnAdvanced.Click += (_, _) => { };

            LoadFromSettings();
        }

        private void LoadFromSettings()
        {
            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = _s.SubliminalEnabled;
                SliderPerMin.Value = _s.SubliminalFrequency;
                TxtPerMin.Text = _s.SubliminalFrequency.ToString();
                SliderFrames.Value = _s.SubliminalDuration;
                TxtFrames.Text = _s.SubliminalDuration.ToString();
                SliderOpacity.Value = _s.SubliminalOpacity;
                TxtOpacity.Text = $"{_s.SubliminalOpacity}%";
                ChkWhispers.IsChecked = _s.SubAudioEnabled;
                SliderWhisperVol.Value = _s.SubAudioVolume;
                TxtWhisperVol.Text = $"{_s.SubAudioVolume}%";
                ChkSolidMode.IsChecked = _s.SubliminalSolidMode;
                // ponytail: needs Helpers.FontPickerHelper (system font enumeration); two placeholders, selection by name as on WPF
                CmbFont.ItemsSource = new[] { "Arial", "Fredoka" };
                CmbFont.SelectedItem = _s.SubliminalFont;
            }
            finally { _isLoading = false; }
        }

        // ponytail: needs App.Settings.Save(), wired when AppSettings moves to Core
        private static void Save() { }
    }

    /// <summary>Placeholder for the AppSettings slice this panel reads. Property names match
    /// Models.AppSettings; values are the WPF XAML defaults.</summary>
    public sealed class SubliminalSettings
    {
        public bool SubliminalEnabled { get; set; }
        public int SubliminalFrequency { get; set; } = 5;
        public int SubliminalDuration { get; set; } = 2;
        public int SubliminalOpacity { get; set; } = 80;
        public bool SubAudioEnabled { get; set; }
        public int SubAudioVolume { get; set; } = 50;
        public bool SubliminalSolidMode { get; set; }
        public string SubliminalFont { get; set; } = "Arial";
    }
}
