using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Features
{
    public partial class LockCardFeatureControl : UserControl, ISettingsRebindable
    {
        private bool _isLoading = true; // Prevent XAML default values from overwriting settings during InitializeComponent

        public LockCardFeatureControl()
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
                ChkEnable.IsChecked = s.LockCardEnabled;
                SliderFreq.Value = s.LockCardFrequency;
                TxtFreq.Text = s.LockCardFrequency.ToString();
                SliderRepeats.Value = s.LockCardRepeats;
                TxtRepeats.Text = $"{s.LockCardRepeats}x";
                ChkStrict.IsChecked = s.LockCardStrict;
                ChkVoiceMode.IsChecked = s.LockCardVoiceMode && s.MicConsentGiven;
                UpdateVoiceHint();
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.LockCardEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.LockCardFrequency) ||
                e.PropertyName == nameof(Models.AppSettings.LockCardRepeats) ||
                e.PropertyName == nameof(Models.AppSettings.LockCardStrict) ||
                e.PropertyName == nameof(Models.AppSettings.LockCardVoiceMode))
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

        private void ChkVoiceMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            var on = ChkVoiceMode.IsChecked ?? false;

            // First time on: require mic consent (shared offline-audio contract). Decline => revert.
            if (on && !s.MicConsentGiven)
            {
                var dlg = new MicConsentDialog { Owner = Window.GetWindow(this) ?? Application.Current.MainWindow };
                var ok = dlg.ShowDialog() == true && dlg.ConsentGiven;
                if (!ok)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _isLoading = true;
                        ChkVoiceMode.IsChecked = false;
                        _isLoading = false;
                    }));
                    return;
                }
            }

            s.LockCardVoiceMode = on;
            App.Settings?.Save();
            UpdateVoiceHint();
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
            if (App.Speech?.IsAvailable == true)
                TxtVoiceHint.Text = "On — speak the phrase to dismiss the card. Typing stays available if the mic can't hear you.";
            else if (App.Speech == null || !Services.Speech.SpeechService.HasCaptureDevice)
                TxtVoiceHint.Text = "No microphone detected — lock cards will use typing until one is connected.";
            else
                TxtVoiceHint.Text = "Speech model not installed yet — lock cards will use typing until it is.";
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
