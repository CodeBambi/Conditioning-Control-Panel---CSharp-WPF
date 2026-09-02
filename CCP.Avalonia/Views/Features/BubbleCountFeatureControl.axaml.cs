using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Bubble Count settings panel, ported from the WPF head. <see cref="BubbleCountSettings"/>
    /// stands in for <c>App.Settings.Current</c>. Save, SettingsHook, the mod-art repaint and the
    /// BubbleCountService calls are stubbed; the strict-lock double warning writes through.
    /// </summary>
    public partial class BubbleCountFeatureControl : UserControl
    {
        private bool _isLoading = true;
        private readonly BubbleCountSettings _s = new();

        public BubbleCountFeatureControl()
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            // ponytail: needs App.BubbleCount Start/Stop/RefreshSchedule/TriggerGame, wired when it moves to Core
            ChkEnable.IsCheckedChanged += (_, _) => { if (_isLoading) return; _s.BubbleCountEnabled = ChkEnable.IsChecked ?? false; Save(); };
            SliderFreq.ValueChanged += (_, e) => { if (_isLoading) return; var v = (int)e.NewValue; TxtFreq.Text = v.ToString(); _s.BubbleCountFrequency = v; Save(); };
            CmbDifficulty.SelectionChanged += (_, _) =>
            {
                if (_isLoading) return;
                if (CmbDifficulty.SelectedItem is ComboBoxItem item && item.Tag is string tag && int.TryParse(tag, out var difficulty))
                {
                    _s.BubbleCountDifficulty = difficulty;
                    Save();
                }
            };
            // ponytail: needs WarningDialog.ShowDoubleWarning (WPF head); writes through without the confirm
            ChkStrict.IsCheckedChanged += (_, _) => { if (_isLoading) return; _s.BubbleCountStrictLock = ChkStrict.IsChecked ?? false; Save(); };
            BtnTest.Click += (_, _) => { };

            LoadFromSettings();
        }

        private void LoadFromSettings()
        {
            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = _s.BubbleCountEnabled;
                SliderFreq.Value = _s.BubbleCountFrequency;
                TxtFreq.Text = _s.BubbleCountFrequency.ToString();
                // Select matching ComboBoxItem by Tag
                foreach (var obj in CmbDifficulty.Items)
                {
                    if (obj is ComboBoxItem item && item.Tag is string tag && int.TryParse(tag, out var val) && val == _s.BubbleCountDifficulty)
                    {
                        CmbDifficulty.SelectedItem = item;
                        break;
                    }
                }
                ChkStrict.IsChecked = _s.BubbleCountStrictLock;
            }
            finally { _isLoading = false; }
        }

        // ponytail: needs App.Settings.Save(), wired when AppSettings moves to Core
        private static void Save() { }
    }

    /// <summary>Placeholder for the AppSettings slice this panel reads. Property names match
    /// Models.AppSettings; values are the WPF XAML defaults.</summary>
    public sealed class BubbleCountSettings
    {
        public bool BubbleCountEnabled { get; set; }
        public int BubbleCountFrequency { get; set; } = 2;
        public int BubbleCountDifficulty { get; set; } = 1;
        public bool BubbleCountStrictLock { get; set; }
    }
}
