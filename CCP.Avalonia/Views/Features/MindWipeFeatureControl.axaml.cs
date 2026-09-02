using System.IO;
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Mind Wipe settings panel, ported from the WPF head. <see cref="MindWipeSettings"/> stands
    /// in for <c>App.Settings.Current</c>. Save, the MindWipeService calls, the Win32
    /// OpenFileDialog and the mod-art repaint are stubbed.
    /// </summary>
    public partial class MindWipeFeatureControl : UserControl
    {
        private bool _isLoading = true;
        private readonly MindWipeSettings _s = new();

        public MindWipeFeatureControl()
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            // ponytail: needs App.MindWipe UpdateSettings/StartLoop/StopLoop/TriggerOnce/ReloadAudioFiles, wired when it moves to Core
            ChkEnable.IsCheckedChanged += (_, _) => { if (_isLoading) return; _s.MindWipeEnabled = ChkEnable.IsChecked ?? false; Save(); };
            SliderFreq.ValueChanged += (_, e) => { if (_isLoading) return; var v = (int)e.NewValue; TxtFreq.Text = $"{v}/h"; _s.MindWipeFrequency = v; Save(); };
            SliderVolume.ValueChanged += (_, e) => { if (_isLoading) return; var v = (int)e.NewValue; TxtVolume.Text = $"{v}%"; _s.MindWipeVolume = v; Save(); };
            ChkLoop.IsCheckedChanged += (_, _) => { if (_isLoading) return; _s.MindWipeLoop = ChkLoop.IsChecked ?? false; Save(); };
            BtnTest.Click += (_, _) => { };
            // ponytail: needs a file picker (Win32 OpenFileDialog on WPF); StorageProvider needs a TopLevel, wired with the host
            BtnSelectAudio.Click += (_, _) => { };
            BtnClearAudio.Click += (_, _) =>
            {
                if (string.IsNullOrEmpty(_s.MindWipeAudioPath)) return;
                _s.MindWipeAudioPath = "";
                Save();
                UpdateAudioFileLabel();
            };

            LoadFromSettings();
        }

        private void LoadFromSettings()
        {
            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = _s.MindWipeEnabled;
                SliderFreq.Value = _s.MindWipeFrequency;
                TxtFreq.Text = $"{_s.MindWipeFrequency}/h";
                SliderVolume.Value = _s.MindWipeVolume;
                TxtVolume.Text = $"{_s.MindWipeVolume}%";
                ChkLoop.IsChecked = _s.MindWipeLoop;
                UpdateAudioFileLabel();
            }
            finally { _isLoading = false; }
        }

        private void UpdateAudioFileLabel()
        {
            var path = _s.MindWipeAudioPath;
            TxtAudioFile.Text = !string.IsNullOrWhiteSpace(path) && File.Exists(path)
                ? Path.GetFileName(path)
                : "Default (built-in clips)";
        }

        // ponytail: needs App.Settings.Save(), wired when AppSettings moves to Core
        private static void Save() { }
    }

    /// <summary>Placeholder for the AppSettings slice this panel reads. Property names match
    /// Models.AppSettings; values are the WPF XAML defaults.</summary>
    public sealed class MindWipeSettings
    {
        public bool MindWipeEnabled { get; set; }
        public int MindWipeFrequency { get; set; } = 6;
        public int MindWipeVolume { get; set; } = 50;
        public bool MindWipeLoop { get; set; }
        public string MindWipeAudioPath { get; set; } = "";
    }
}
