using System;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls.AppSettingsSections
{
    /// <summary>
    /// Settings door · AUDIO. See the XAML header.
    ///
    /// Pure forwarding, like every re-parented cell (Companion Workshop precedent): the
    /// handlers hop to <see cref="MainWindow"/> with the same
    /// <c>Window.GetWindow(this) is MainWindow</c> idiom the tab views use, and every
    /// control is exposed as an <c>internal</c> field via <c>x:FieldModifier</c> so the
    /// host view can re-publish it as <c>AppSettingsTab.&lt;Name&gt;</c>.
    ///
    /// The only self-contained handler is <see cref="BtnAudioLayers_Click"/> — it was
    /// self-contained on the dashboard too (it just opens a window and needs no MainWindow
    /// state), so it moved verbatim rather than growing a hop.
    /// </summary>
    public partial class AudioSettingsSection : UserControl
    {
        public AudioSettingsSection()
        {
            InitializeComponent();
        }

        private void SliderMaster_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SliderMaster_Changed(sender, e);
        }

        private void SliderVideoVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SliderVideoVolume_Changed(sender, e);
        }

        private void SliderDuck_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SliderDuck_Changed(sender, e);
        }

        private void ChkAudioDuck_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ChkAudioDuck_Changed(sender, e);
        }

        private void ChkExcludeBambiCloudDucking_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ChkExcludeBambiCloudDucking_Changed(sender, e);
        }

        private void CmbAudioOutputDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.CmbAudioOutputDevice_SelectionChanged(sender, e);
        }

        private void BtnAudioOutputRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnAudioOutputRefresh_Click(sender, e);
        }

        private void BtnTestAudio_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnTestAudio_Click(sender, e);
        }

        // Audio-sync tuning pair, moved here from the dashboard's browser card in Phase 3.
        // Same MainWindow handlers, same names — MainWindow.Haptics.cs writes these sliders'
        // Values from the Haptics panel's own delay/power pair and vice versa, so the hop must
        // stay a pure forward or the two surfaces will fight.
        private void SliderAudioSyncLatency_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SliderAudioSyncLatency_Changed(sender, e);
        }

        private void SliderAudioSyncIntensity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SliderAudioSyncIntensity_Changed(sender, e);
        }

        // Suggestion #659 — open the Audio Layers config window (self-contained, no MainWindow dep).
        private void BtnAudioLayers_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new LayeredAudioWindow { Owner = Window.GetWindow(this) };
                win.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Settings/Audio: Audio Layers window launch failed");
            }
        }
    }
}
