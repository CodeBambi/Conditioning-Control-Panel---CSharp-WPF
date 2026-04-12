using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Features
{
    public partial class BubbleCountFeatureControl : UserControl
    {
        private bool _isLoading = true;

        public BubbleCountFeatureControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadFromSettings();
            if (App.Settings?.Current is INotifyPropertyChanged inpc)
                inpc.PropertyChanged += OnSettingsPropertyChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current is INotifyPropertyChanged inpc)
                inpc.PropertyChanged -= OnSettingsPropertyChanged;
        }

        private void LoadFromSettings()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = s.BubbleCountEnabled;
                SliderFreq.Value = s.BubbleCountFrequency;
                TxtFreq.Text = s.BubbleCountFrequency.ToString();
                // Select matching ComboBoxItem by Tag
                foreach (ComboBoxItem item in CmbDifficulty.Items)
                {
                    if (item.Tag is string tag && int.TryParse(tag, out var val) && val == s.BubbleCountDifficulty)
                    {
                        CmbDifficulty.SelectedItem = item;
                        break;
                    }
                }
                ChkStrict.IsChecked = s.BubbleCountStrictLock;
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.BubbleCountEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.BubbleCountFrequency) ||
                e.PropertyName == nameof(Models.AppSettings.BubbleCountDifficulty) ||
                e.PropertyName == nameof(Models.AppSettings.BubbleCountStrictLock))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void ChkEnable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var on = ChkEnable.IsChecked ?? false;
            s.BubbleCountEnabled = on;
            App.Settings?.Save();

            // Live-apply: start/stop bubble count service if engine is running
            if (App.IsSessionRunning)
            {
                if (on)
                    App.BubbleCount?.Start();
                else
                    App.BubbleCount?.Stop();
            }
        }

        private void SliderFreq_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtFreq.Text = v.ToString();
            s.BubbleCountFrequency = v;
            try { App.BubbleCount?.RefreshSchedule(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "BubbleCount RefreshSchedule failed"); }
            App.Settings?.Save();
        }

        private void CmbDifficulty_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (CmbDifficulty.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag && int.TryParse(tag, out var difficulty))
            {
                var s = App.Settings?.Current;
                if (s == null) return;
                s.BubbleCountDifficulty = difficulty;
                App.Settings?.Save();
            }
        }

        private void ChkStrict_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            var on = ChkStrict.IsChecked ?? false;
            if (on)
            {
                var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
                var confirmed = WarningDialog.ShowDoubleWarning(owner,
                    "Strict Bubble Count",
                    "• You will NOT be able to skip the bubble count challenge\n" +
                    "• You MUST answer correctly to dismiss\n" +
                    "• Wrong answers force you to REWATCH the video\n" +
                    "• Mercy system grants escape after 3 retries (if enabled)\n" +
                    "• This can be very restrictive!");

                if (!confirmed)
                {
                    ChkStrict.Checked -= ChkStrict_Changed;
                    ChkStrict.Unchecked -= ChkStrict_Changed;
                    ChkStrict.IsChecked = false;
                    ChkStrict.Checked += ChkStrict_Changed;
                    ChkStrict.Unchecked += ChkStrict_Changed;
                    return;
                }
            }

            s.BubbleCountStrictLock = on;
            App.Settings?.Save();
        }

        private void BtnTest_Click(object sender, RoutedEventArgs e)
        {
            App.BubbleCount?.TriggerGame(forceTest: true);
        }
    }
}
