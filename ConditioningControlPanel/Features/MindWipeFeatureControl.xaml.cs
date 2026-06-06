using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Features
{
    public partial class MindWipeFeatureControl : UserControl
    {
        private bool _isLoading = true;

        public MindWipeFeatureControl()
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
                ChkEnable.IsChecked = s.MindWipeEnabled;
                SliderFreq.Value = s.MindWipeFrequency;
                TxtFreq.Text = $"{s.MindWipeFrequency}/h";
                SliderVolume.Value = s.MindWipeVolume;
                TxtVolume.Text = $"{s.MindWipeVolume}%";
                ChkLoop.IsChecked = s.MindWipeLoop;
                UpdateAudioFileLabel(s);
            }
            finally { _isLoading = false; }
        }

        private void UpdateAudioFileLabel(Models.AppSettings s)
        {
            var path = s.MindWipeAudioPath;
            TxtAudioFile.Text = !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path)
                ? System.IO.Path.GetFileName(path)
                : "Default (built-in clips)";
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.MindWipeEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.MindWipeFrequency) ||
                e.PropertyName == nameof(Models.AppSettings.MindWipeVolume) ||
                e.PropertyName == nameof(Models.AppSettings.MindWipeLoop) ||
                e.PropertyName == nameof(Models.AppSettings.MindWipeAudioPath))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void ChkEnable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.MindWipeEnabled = ChkEnable.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void SliderFreq_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtFreq.Text = $"{v}/h";
            s.MindWipeFrequency = v;
            try { App.MindWipe?.UpdateSettings(s.MindWipeFrequency, s.MindWipeVolume / 100.0); }
            catch (Exception ex) { App.Logger?.Warning(ex, "MindWipe UpdateSettings failed"); }
            App.Settings?.Save();
        }

        private void SliderVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtVolume.Text = $"{v}%";
            s.MindWipeVolume = v;
            try { App.MindWipe?.UpdateSettings(s.MindWipeFrequency, s.MindWipeVolume / 100.0); }
            catch (Exception ex) { App.Logger?.Warning(ex, "MindWipe UpdateSettings failed"); }
            App.Settings?.Save();
        }

        private void ChkLoop_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var looping = ChkLoop.IsChecked ?? false;
            s.MindWipeLoop = looping;
            try
            {
                if (looping)
                    App.MindWipe?.StartLoop(s.MindWipeVolume / 100.0);
                else
                    App.MindWipe?.StopLoop();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "MindWipe loop toggle failed"); }
            App.Settings?.Save();
        }

        private void BtnTest_Click(object sender, RoutedEventArgs e)
        {
            try { App.MindWipe?.TriggerOnce(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "MindWipe TriggerOnce failed"); }
        }

        private void BtnSelectAudio_Click(object sender, RoutedEventArgs e)
        {
            var s = App.Settings?.Current;
            if (s == null) return;

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select mind-wipe audio (short clip, ~2 sec recommended)",
                Filter = "Audio Files|*.mp3;*.wav;*.ogg|All Files|*.*"
            };

            var current = s.MindWipeAudioPath;
            if (!string.IsNullOrWhiteSpace(current) && System.IO.File.Exists(current))
                dialog.InitialDirectory = System.IO.Path.GetDirectoryName(current);

            if (dialog.ShowDialog() != true) return;

            s.MindWipeAudioPath = dialog.FileName;
            App.Settings?.Save();
            ApplyAudioChange(s);
        }

        private void BtnClearAudio_Click(object sender, RoutedEventArgs e)
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            if (string.IsNullOrEmpty(s.MindWipeAudioPath)) return;

            s.MindWipeAudioPath = "";
            App.Settings?.Save();
            ApplyAudioChange(s);
        }

        private void ApplyAudioChange(Models.AppSettings s)
        {
            UpdateAudioFileLabel(s);
            try
            {
                App.MindWipe?.ReloadAudioFiles();
                // If a background loop is running, restart it so the new clip takes effect.
                if (s.MindWipeLoop && (App.MindWipe?.IsLooping ?? false))
                {
                    App.MindWipe?.StopLoop();
                    App.MindWipe?.StartLoop(s.MindWipeVolume / 100.0);
                }
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "MindWipe audio change failed"); }
        }
    }
}
