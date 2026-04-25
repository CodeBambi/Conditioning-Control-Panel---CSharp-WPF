using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Features
{
    public partial class LockCardFeatureControl : UserControl
    {
        private bool _isLoading = true; // Prevent XAML default values from overwriting settings during InitializeComponent

        public LockCardFeatureControl()
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
                ChkEnable.IsChecked = s.LockCardEnabled;
                SliderFreq.Value = s.LockCardFrequency;
                TxtFreq.Text = s.LockCardFrequency.ToString();
                SliderRepeats.Value = s.LockCardRepeats;
                TxtRepeats.Text = $"{s.LockCardRepeats}x";
                ChkStrict.IsChecked = s.LockCardStrict;
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.LockCardEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.LockCardFrequency) ||
                e.PropertyName == nameof(Models.AppSettings.LockCardRepeats) ||
                e.PropertyName == nameof(Models.AppSettings.LockCardStrict))
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
            s.LockCardEnabled = on;
            App.Settings?.Save();

            // Live-apply: start/stop lock card service if engine is running
            if (App.IsEngineRunning)
            {
                if (on)
                    App.LockCard?.Start();
                else
                    App.LockCard?.Stop();
            }
        }

        private void SliderFreq_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtFreq.Text = v.ToString();
            s.LockCardFrequency = v;
            App.Settings?.Save();
        }

        private void SliderRepeats_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtRepeats.Text = $"{v}x";
            s.LockCardRepeats = v;
            App.Settings?.Save();
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
                    "Strict Lock Card",
                    "• You will NOT be able to escape lock cards with ESC\n" +
                    "• You MUST type the phrase the required number of times\n" +
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

            s.LockCardStrict = on;
            App.Settings?.Save();
        }

        private void BtnManagePhrases_Click(object sender, RoutedEventArgs e)
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            var editor = new TextEditorDialog("Lock Card Phrases", s.LockCardPhrases)
            {
                Owner = Window.GetWindow(this) ?? Application.Current.MainWindow
            };
            if (editor.ShowDialog() == true && editor.ResultData != null)
            {
                s.LockCardPhrases = editor.ResultData;
                App.Settings?.Save();
                App.Logger?.Information("Lock card phrases updated: {Count} items", editor.ResultData.Count);
            }
        }

        private void BtnTest_Click(object sender, RoutedEventArgs e)
        {
            var s = App.Settings?.Current;
            if (s == null) return;

            var enabledPhrases = s.LockCardPhrases.Where(p => p.Value).Select(p => p.Key).ToList();
            if (enabledPhrases.Count == 0)
            {
                MessageBox.Show(
                    Loc.Get("msg_no_phrases_enabled_add_some_phrases_first"),
                    "No Phrases",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            App.LockCard?.TestLockCard();
        }

        private void BtnColorSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new LockCardColorDialog
            {
                Owner = Window.GetWindow(this) ?? Application.Current.MainWindow
            };
            dialog.ShowDialog();
        }
    }
}
