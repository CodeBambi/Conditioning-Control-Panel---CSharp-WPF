using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Features
{
    public partial class SubliminalFeatureControl : UserControl, ISettingsRebindable
    {
        private bool _isLoading = true;

        public SubliminalFeatureControl()
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
                ChkEnable.IsChecked = s.SubliminalEnabled;
                SliderPerMin.Value = s.SubliminalFrequency;
                TxtPerMin.Text = s.SubliminalFrequency.ToString();
                SliderFrames.Value = s.SubliminalDuration;
                TxtFrames.Text = s.SubliminalDuration.ToString();
                SliderOpacity.Value = s.SubliminalOpacity;
                TxtOpacity.Text = $"{s.SubliminalOpacity}%";
                ChkWhispers.IsChecked = s.SubAudioEnabled;
                SliderWhisperVol.Value = s.SubAudioVolume;
                TxtWhisperVol.Text = $"{s.SubAudioVolume}%";
                ChkSolidMode.IsChecked = s.SubliminalSolidMode;
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.SubliminalEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.SubliminalFrequency) ||
                e.PropertyName == nameof(Models.AppSettings.SubliminalDuration) ||
                e.PropertyName == nameof(Models.AppSettings.SubliminalOpacity) ||
                e.PropertyName == nameof(Models.AppSettings.SubAudioEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.SubAudioVolume) ||
                e.PropertyName == nameof(Models.AppSettings.SubliminalSolidMode))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void ChkEnable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            // Single authority: persists the flag and live-applies start/stop (idempotently).
            App.Subliminal?.SetEnabled(ChkEnable.IsChecked ?? false);
        }

        private void SliderPerMin_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtPerMin.Text = v.ToString();
            s.SubliminalFrequency = v;
            App.Settings?.Save();
        }

        private void SliderFrames_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtFrames.Text = v.ToString();
            s.SubliminalDuration = v;
            App.Settings?.Save();
        }

        private void SliderOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtOpacity.Text = $"{v}%";
            s.SubliminalOpacity = v;
            App.Settings?.Save();
        }

        private void ChkWhispers_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.SubAudioEnabled = ChkWhispers.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void SliderWhisperVol_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtWhisperVol.Text = $"{v}%";
            s.SubAudioVolume = v;
            App.Settings?.Save();
        }

        private void ChkSolidMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.SubliminalSolidMode = ChkSolidMode.IsChecked ?? false;
            App.Settings?.Save();
            // No service bounce needed: each show reads the setting, so the next subliminal
            // uses the new renderer. An in-flight card finishes out on whichever spawned it.
        }

        private void BtnManageMessages_Click(object sender, RoutedEventArgs e)
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            var oldKeys = new HashSet<string>(s.SubliminalPool.Keys);
            var dialog = new TextEditorDialog("Subliminal Messages", s.SubliminalPool)
            {
                Owner = Window.GetWindow(this) ?? Application.Current.MainWindow
            };
            if (dialog.ShowDialog() == true && dialog.ResultData != null)
            {
                // Remember hand-added phrases (and forget removed ones) so the cross-mod prune
                // never silently deletes a custom phrase that collides with another mod's default.
                var newKeys = new HashSet<string>(dialog.ResultData.Keys);
                foreach (var key in newKeys)
                    if (!oldKeys.Contains(key)) s.UserAddedSubliminals.Add(key);
                foreach (var key in oldKeys)
                    if (!newKeys.Contains(key)) s.UserAddedSubliminals.Remove(key);

                s.SubliminalPool = dialog.ResultData;
                App.Settings?.Save();
                App.Logger?.Information("Subliminal pool updated: {Count} items", dialog.ResultData.Count);
            }
        }

        private void BtnAdvanced_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ColorEditorDialog
            {
                Owner = Window.GetWindow(this) ?? Application.Current.MainWindow
            };
            dialog.ShowDialog();
        }
    }
}
