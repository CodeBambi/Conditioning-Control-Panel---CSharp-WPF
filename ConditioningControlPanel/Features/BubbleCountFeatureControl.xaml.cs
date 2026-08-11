using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Features
{
    public partial class BubbleCountFeatureControl : UserControl, ISettingsRebindable
    {
        private bool _isLoading = true;

        public BubbleCountFeatureControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        // Tracks WHICH AppSettings instance the hook is attached to, so a cloud restore - which
        // SWAPS the instance - can be followed instead of leaving this permanently-mounted rack
        // panel listening to, and displaying, the discarded object. See ISettingsRebindable.
        private SettingsHook? _settingsHook;

        private void OnLoaded(object sender, RoutedEventArgs e) => RebindToCurrentSettings();

        private void OnUnloaded(object sender, RoutedEventArgs e) => _settingsHook?.Unhook();

        /// <inheritdoc/>
        public void RebindToCurrentSettings()
        {
            (_settingsHook ??= new SettingsHook(OnSettingsPropertyChanged)).Rebind();
            LoadFromSettings();
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
            if (App.IsEngineRunning)
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
                var owner = Application.Current.MainWindow;
                var confirmed = WarningDialog.ShowDoubleWarning(owner,
                    "Strict Bubble Count",
                    "• You will NOT be able to skip the bubble count challenge\n" +
                    "• You MUST answer correctly to dismiss\n" +
                    "• Wrong answers force you to REWATCH the video\n" +
                    "• Mercy system grants escape after 3 retries (if enabled)\n" +
                    "• This can be very restrictive!");

                if (!confirmed)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _isLoading = true;
                        ChkStrict.IsChecked = false;
                        _isLoading = false;
                    }));
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
