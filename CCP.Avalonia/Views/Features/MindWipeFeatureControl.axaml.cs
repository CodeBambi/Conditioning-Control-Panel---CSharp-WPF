using System;
using System.ComponentModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Mind Wipe settings panel, ported from the WPF head. Every editor reads and writes
    /// <see cref="CoreSettings.Current"/> and persists through <see cref="CoreSettings.Save"/>,
    /// the one-for-one port of <c>App.Settings.Current</c> / <c>App.Settings.Save()</c>.
    ///
    /// <para>The settings hook is inlined for the reason spelled out in
    /// <see cref="BubbleCountFeatureControl"/>: a cloud restore swaps the settings instance under
    /// a permanently rack-mounted panel.</para>
    ///
    /// <para>Win32's <c>OpenFileDialog</c> becomes Avalonia's <c>StorageProvider</c>, which is
    /// async and needs a TopLevel - so the handler is <c>async void</c> and the title and filter
    /// carry over verbatim. Still head-side: MindWipeService (the audio loop itself) and the
    /// mod-aware feature art.</para>
    /// </summary>
    public partial class MindWipeFeatureControl : UserControl
    {
        private bool _isLoading = true;
        private AppSettings? _hooked;

        public MindWipeFeatureControl()
        {
            InitializeComponent(); // generated: loads the XAML and fills the x:Name fields

            ChkEnable.IsCheckedChanged += ChkEnable_Changed;
            SliderFreq.ValueChanged += SliderFreq_Changed;
            SliderVolume.ValueChanged += SliderVolume_Changed;
            ChkLoop.IsCheckedChanged += ChkLoop_Changed;
            BtnTest.Click += BtnTest_Click;
            BtnSelectAudio.Click += BtnSelectAudio_Click;
            BtnClearAudio.Click += BtnClearAudio_Click;

            Loaded += (_, _) => RebindToCurrentSettings();
            Unloaded += (_, _) => Unhook();

            // ponytail: hero and side plates, same state as BubbleCount's - see the longer note
            // there. CoreModArt.OverridePath("features/Mind_Wipers.png") answers the override half
            // and the built-in ships at avares://CCP.Avalonia/Resources/features/Mind_Wipers.png;
            // MindWipeFeatureControl.axaml just gives neither plate an x:Name to paint into.

            RebindToCurrentSettings();
        }

        /// <summary>Re-points the settings hook at the live instance and repaints from it.</summary>
        public void RebindToCurrentSettings()
        {
            Unhook();
            _hooked = CoreSettings.Current;
            _hooked.PropertyChanged += OnSettingsPropertyChanged;
            LoadFromSettings();
        }

        private void Unhook()
        {
            if (_hooked != null) _hooked.PropertyChanged -= OnSettingsPropertyChanged;
            _hooked = null;
        }

        private void LoadFromSettings()
        {
            var s = CoreSettings.Current;
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

        private void UpdateAudioFileLabel(AppSettings s)
        {
            var path = s.MindWipeAudioPath;
            TxtAudioFile.Text = !string.IsNullOrWhiteSpace(path) && File.Exists(path)
                ? Path.GetFileName(path)
                : "Default (built-in clips)";
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSettings.MindWipeEnabled) ||
                e.PropertyName == nameof(AppSettings.MindWipeFrequency) ||
                e.PropertyName == nameof(AppSettings.MindWipeVolume) ||
                e.PropertyName == nameof(AppSettings.MindWipeLoop) ||
                e.PropertyName == nameof(AppSettings.MindWipeAudioPath))
            {
                Dispatcher.UIThread.Post(LoadFromSettings);
            }
        }

        private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var on = ChkEnable.IsChecked ?? false;
            if (s.MindWipeEnabled == on) return;
            s.MindWipeEnabled = on;
            CoreSettings.Save();
        }

        private void SliderFreq_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var v = (int)e.NewValue;
            TxtFreq.Text = $"{v}/h";
            if (s.MindWipeFrequency == v) return;
            s.MindWipeFrequency = v;
            // ponytail: WPF pushes the pair through App.MindWipe.UpdateSettings(frequency,
            // volume/100) - MindWipeService (ConditioningControlPanel/Services/), still head-side.
            CoreSettings.Save();
        }

        private void SliderVolume_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var v = (int)e.NewValue;
            TxtVolume.Text = $"{v}%";
            if (s.MindWipeVolume == v) return;
            s.MindWipeVolume = v;
            // ponytail: same App.MindWipe.UpdateSettings call as SliderFreq_Changed.
            CoreSettings.Save();
        }

        private void ChkLoop_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var looping = ChkLoop.IsChecked ?? false;
            if (s.MindWipeLoop == looping) return;
            s.MindWipeLoop = looping;
            // ponytail: WPF starts or stops the background loop here -
            // App.MindWipe.StartLoop(volume/100) / StopLoop(), MindWipeService in the WPF head.
            CoreSettings.Save();
        }

        private void BtnTest_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs App.MindWipe.TriggerOnce() - MindWipeService
            // (ConditioningControlPanel/Services/), still in the WPF head.
        }

        /// <summary>
        /// Win32's <c>OpenFileDialog</c> in Avalonia terms. Same title, same extensions, and the
        /// current pick's folder is still where the picker opens.
        /// </summary>
        private async void BtnSelectAudio_Click(object? sender, RoutedEventArgs e)
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage == null) return;
            var s = CoreSettings.Current;

            var options = new FilePickerOpenOptions
            {
                Title = "Select mind-wipe audio (short clip, ~2 sec recommended)",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Audio Files") { Patterns = new[] { "*.mp3", "*.wav", "*.ogg" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } },
                },
            };

            var current = s.MindWipeAudioPath;
            if (!string.IsNullOrWhiteSpace(current) && File.Exists(current) &&
                Path.GetDirectoryName(current) is { Length: > 0 } dir)
            {
                try { options.SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(dir); }
                catch (Exception ex) { Log.Debug("MindWipe audio picker start folder: {E}", ex.Message); }
            }

            var files = await storage.OpenFilePickerAsync(options);
            if (files.Count != 1 || files[0].TryGetLocalPath() is not { } path) return;

            s.MindWipeAudioPath = path;
            CoreSettings.Save();
            ApplyAudioChange(s);
        }

        private void BtnClearAudio_Click(object? sender, RoutedEventArgs e)
        {
            var s = CoreSettings.Current;
            if (string.IsNullOrEmpty(s.MindWipeAudioPath)) return;

            s.MindWipeAudioPath = "";
            CoreSettings.Save();
            ApplyAudioChange(s);
        }

        private void ApplyAudioChange(AppSettings s)
        {
            UpdateAudioFileLabel(s);
            // ponytail: WPF then calls App.MindWipe.ReloadAudioFiles() and, when MindWipeLoop is
            // on and IsLooping, restarts the loop so the new clip takes effect - MindWipeService
            // (ConditioningControlPanel/Services/), still in the WPF head.
        }
    }
}
