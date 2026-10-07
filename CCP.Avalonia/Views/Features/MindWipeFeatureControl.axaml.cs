using System;
using System.ComponentModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Localization;
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
    /// carry over verbatim.</para>
    ///
    /// <para>Every editor also drives <see cref="CoreMindWipe"/>, the one-for-one port of the WPF
    /// card's <c>App.MindWipe.*</c> calls, seeded by <c>Platform.MindWipePlayer</c> when LibVLC loads.</para>
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

            Helpers.ModArt.BindFeaturePlates(this, "features/Mind_Wipers.png", HeroArt, SideArt);

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
            // #1304: unticking used to leave a running Mind Wipe firing until the engine stopped.
            CoreMindWipe.ApplyRunRule();
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
            CoreMindWipe.UpdateSettings(s.MindWipeFrequency, s.MindWipeVolume / 100.0);
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
            CoreMindWipe.UpdateSettings(s.MindWipeFrequency, s.MindWipeVolume / 100.0);
            CoreSettings.Save();
        }

        private void ChkLoop_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var looping = ChkLoop.IsChecked ?? false;
            if (s.MindWipeLoop == looping) return;
            s.MindWipeLoop = looping;
            // #1304: the loop plays on top of an enabled, running Mind Wipe only (WPF ChkLoop_Changed).
            CoreMindWipe.ApplyRunRule();
            CoreSettings.Save();
        }

        /// <summary>
        /// Plays one clip now. Ungated on purpose: the WPF card has no <c>IsEngineRunning</c>
        /// check here and the service's <c>TriggerOnce</c> is documented as playing with the
        /// service stopped, so gating it would make this button do less than the original.
        /// </summary>
        private async void BtnTest_Click(object? sender, RoutedEventArgs e)
        {
            // WPF MindWipeService.TriggerOnce: no clips is a warning box, not silence.
            if (CoreMindWipe.ClipCount == 0)
            {
                if (TopLevel.GetTopLevel(this) is Window owner)
                    await Dialogs.MessageDialog.ShowAsync(owner, Loc.Get("mindwipe_title"), Loc.Get("mindwipe_no_audio_files"));
                return;
            }
            CoreMindWipe.TriggerOnce();
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
            CoreMindWipe.ReloadClips();
            // Only a loop that is actually playing is restarted - the seam answers "not looping"
            // when no head is playing one, so nothing is started on this head's silence.
            if (s.MindWipeLoop && CoreMindWipe.IsLooping)
            {
                CoreMindWipe.StopLoop();
                CoreMindWipe.StartLoop(s.MindWipeVolume / 100.0);
            }
        }
    }
}
