using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// SETTINGS · AUDIO, ported from the WPF head.
    ///
    /// On WPF every handler is a one-line hop to the identically named <c>MainWindow</c> method
    /// (audio engine, NAudio device enumeration, ducking) and <see cref="BtnAudioLayers_Click"/>
    /// opens <c>LayeredAudioWindow</c>. None of that is on this head, so the handlers are stubs.
    /// The value labels beside each slider keep their markup defaults; on WPF the host repaints
    /// them from settings.
    /// </summary>
    public partial class AudioSettingsSection : UserControl
    {
        public AudioSettingsSection()
        {
            AvaloniaXamlLoader.Load(this);
            // ponytail: placeholder so the combo is not an empty pill; real list comes from the audio device service
            var cmb = this.FindControl<ComboBox>("CmbAudioOutputDevice")!;
            cmb.ItemsSource = new[] { "Default" };
            cmb.SelectedIndex = 0;
        }

        // ponytail: needs MainWindow's audio handlers (AudioService, NAudio devices), wired when they move to Core
        private void SliderMaster_Changed(object? sender, RangeBaseValueChangedEventArgs e) { }
        private void SliderVideoVolume_Changed(object? sender, RangeBaseValueChangedEventArgs e) { }
        private void SliderDuck_Changed(object? sender, RangeBaseValueChangedEventArgs e) { }
        private void ChkAudioDuck_Changed(object? sender, RoutedEventArgs e) { }
        private void ChkExcludeBambiCloudDucking_Changed(object? sender, RoutedEventArgs e) { }
        private void CmbAudioOutputDevice_SelectionChanged(object? sender, SelectionChangedEventArgs e) { }
        private void BtnAudioOutputRefresh_Click(object? sender, RoutedEventArgs e) { }
        private void BtnTestAudio_Click(object? sender, RoutedEventArgs e) { }
        private void SliderAudioSyncLatency_Changed(object? sender, RangeBaseValueChangedEventArgs e) { }
        private void SliderAudioSyncIntensity_Changed(object? sender, RangeBaseValueChangedEventArgs e) { }

        // ponytail: needs LayeredAudioWindow, which is not ported
        private void BtnAudioLayers_Click(object? sender, RoutedEventArgs e) { }
    }
}
