using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Visuals settings panel, ported from the WPF head. Same load/handler shape as the original;
    /// <see cref="VisualsSettings"/> stands in for <c>App.Settings.Current</c> until AppSettings
    /// reaches Core. Save, SettingsHook and ISettingsRebindable are stubbed.
    /// </summary>
    public partial class VisualsFeatureControl : UserControl
    {
        private bool _isLoading = true;
        private readonly VisualsSettings _s = new();

        public VisualsFeatureControl()
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            SliderSize.ValueChanged += (_, e) => { if (_isLoading) return; var v = (int)e.NewValue; TxtSize.Text = $"{v}%"; _s.ImageScale = v; Save(); };
            SliderOpacity.ValueChanged += (_, e) => { if (_isLoading) return; var v = (int)e.NewValue; TxtOpacity.Text = $"{v}%"; _s.FlashOpacity = v; Save(); };
            SliderFade.ValueChanged += (_, e) => { if (_isLoading) return; var v = (int)e.NewValue; TxtFade.Text = $"{v}%"; _s.FadeDuration = v; Save(); };
            SliderDuration.ValueChanged += (_, e) => { if (_isLoading) return; var v = (int)e.NewValue; TxtDuration.Text = $"{v}s"; _s.FlashDuration = v; Save(); };
            ChkAudio.IsCheckedChanged += (_, _) => { if (_isLoading) return; _s.FlashAudioEnabled = ChkAudio.IsChecked ?? false; Save(); };

            LoadFromSettings();
        }

        private void LoadFromSettings()
        {
            _isLoading = true;
            try
            {
                SliderSize.Value = _s.ImageScale;
                TxtSize.Text = $"{_s.ImageScale}%";
                SliderOpacity.Value = _s.FlashOpacity;
                TxtOpacity.Text = $"{_s.FlashOpacity}%";
                SliderFade.Value = _s.FadeDuration;
                TxtFade.Text = $"{_s.FadeDuration}%";
                SliderDuration.Value = _s.FlashDuration;
                TxtDuration.Text = $"{_s.FlashDuration}s";
                ChkAudio.IsChecked = _s.FlashAudioEnabled;
            }
            finally { _isLoading = false; }
        }

        // ponytail: needs App.Settings.Save(), wired when AppSettings moves to Core
        private static void Save() { }
    }

    /// <summary>Placeholder for the AppSettings slice this panel reads. Property names match
    /// Models.AppSettings; values are the WPF XAML defaults.</summary>
    public sealed class VisualsSettings
    {
        public int ImageScale { get; set; } = 100;
        public int FlashOpacity { get; set; } = 100;
        public int FadeDuration { get; set; } = 50;
        public int FlashDuration { get; set; } = 5;
        public bool FlashAudioEnabled { get; set; }
    }
}
