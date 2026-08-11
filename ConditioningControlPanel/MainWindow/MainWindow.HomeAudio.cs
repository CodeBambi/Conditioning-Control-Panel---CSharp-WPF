using System;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The dashboard's audio card (owner ask, 2026-08-11: audio "was pretty handy to have it on
    /// screen in the dashboard"). Master volume and ducking only - the two dials that get touched
    /// mid-session. Everything else stays in Settings · Audio.
    ///
    /// <para><b>Why this is a mirror and not a second panel.</b> Settings · Audio is not a
    /// self-contained control: <c>AudioSettingsSection</c> re-publishes its children as
    /// <c>AppSettingsTab.&lt;Name&gt;</c>, and MainWindow WRITES them directly - LoadSettings seeds
    /// <c>SliderMaster.Value</c>, and <see cref="ApplySettingsLive"/> READS it back. A second
    /// instance of that control would therefore render whatever its XAML defaulted to and then
    /// push those defaults into settings the first time anyone touched it, which is the
    /// ghost-read data-loss shape already fixed once in this restructure. So the canonical
    /// controls stay the single source of truth and these two are a strict mirror of them - the
    /// same two-surface arrangement <c>SliderAudioSyncLatency</c> has with the Haptics panel.</para>
    ///
    /// <para>Writes go THROUGH the canonical control rather than to settings directly, so the
    /// existing handler keeps doing the live work (volume push to Video/BrainDrain, ForceUnduck)
    /// and there is exactly one code path that can change master volume.</para>
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Guards the mirror against its own echo: writing the canonical control raises
        /// its Changed handler, which mirrors back here, which would write the canonical control
        /// again. One flag, set on whichever side initiated.</summary>
        private bool _homeAudioMirroring;

        internal void HomeSliderMaster_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || _homeAudioMirroring) return;
            var canonical = AppSettingsTab?.SliderMaster;
            if (canonical == null) return;

            try
            {
                _homeAudioMirroring = true;
                if (SettingsTab?.HomeTxtMaster != null)
                    SettingsTab.HomeTxtMaster.Text = $"{(int)e.NewValue}%";
                // The canonical handler does the real work; it is deliberately not duplicated.
                canonical.Value = e.NewValue;
            }
            finally { _homeAudioMirroring = false; }
        }

        internal void HomeChkAudioDuck_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading || _homeAudioMirroring) return;
            var canonical = AppSettingsTab?.ChkAudioDuck;
            if (canonical == null || SettingsTab?.HomeChkAudioDuck == null) return;

            try
            {
                _homeAudioMirroring = true;
                canonical.IsChecked = SettingsTab.HomeChkAudioDuck.IsChecked;
            }
            finally { _homeAudioMirroring = false; }
        }

        internal void HomeSliderVideoVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || _homeAudioMirroring) return;
            var canonical = AppSettingsTab?.SliderVideoVolume;
            if (canonical == null) return;

            try
            {
                _homeAudioMirroring = true;
                if (SettingsTab?.HomeTxtVideoVolume != null)
                    SettingsTab.HomeTxtVideoVolume.Text = $"{(int)e.NewValue}%";
                canonical.Value = e.NewValue;
            }
            finally { _homeAudioMirroring = false; }
        }

        internal void HomeSliderDuck_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || _homeAudioMirroring) return;
            var canonical = AppSettingsTab?.SliderDuck;
            if (canonical == null) return;

            try
            {
                _homeAudioMirroring = true;
                if (SettingsTab?.HomeTxtDuck != null)
                    SettingsTab.HomeTxtDuck.Text = $"{(int)e.NewValue}%";
                canonical.Value = e.NewValue;
            }
            finally { _homeAudioMirroring = false; }
        }

        internal void HomeChkExcludeBambiCloudDucking_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading || _homeAudioMirroring) return;
            var canonical = AppSettingsTab?.ChkExcludeBambiCloudDucking;
            if (canonical == null || SettingsTab?.HomeChkExcludeBambiCloudDucking == null) return;

            try
            {
                _homeAudioMirroring = true;
                canonical.IsChecked = SettingsTab.HomeChkExcludeBambiCloudDucking.IsChecked;
            }
            finally { _homeAudioMirroring = false; }
        }

        internal void HomeCmbAudioOutputDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading || _homeAudioMirroring || _populatingAudioOutputs) return;
            var canonical = AppSettingsTab?.CmbAudioOutputDevice;
            if (canonical == null || SettingsTab?.HomeCmbAudioOutputDevice == null) return;

            try
            {
                _homeAudioMirroring = true;
                canonical.SelectedItem = SettingsTab.HomeCmbAudioOutputDevice.SelectedItem;
            }
            finally { _homeAudioMirroring = false; }
        }

        /// <summary>
        /// Pushes the canonical audio values onto the dashboard card. Called from LoadSettings
        /// (so the card is right on first paint and after a cloud restore swaps the settings
        /// instance) and from the canonical handlers (so changing volume in Settings moves the
        /// dashboard slider too).
        /// </summary>
        internal void MirrorAudioToHome()
        {
            if (_homeAudioMirroring) return;
            try
            {
                _homeAudioMirroring = true;

                var master = AppSettingsTab?.SliderMaster;
                if (master != null && SettingsTab?.HomeSliderMaster != null)
                {
                    SettingsTab.HomeSliderMaster.Value = master.Value;
                    if (SettingsTab.HomeTxtMaster != null)
                        SettingsTab.HomeTxtMaster.Text = $"{(int)master.Value}%";
                }

                var duck = AppSettingsTab?.ChkAudioDuck;
                if (duck != null && SettingsTab?.HomeChkAudioDuck != null)
                    SettingsTab.HomeChkAudioDuck.IsChecked = duck.IsChecked;

                // --- advanced drawer ---
                var video = AppSettingsTab?.SliderVideoVolume;
                if (video != null && SettingsTab?.HomeSliderVideoVolume != null)
                {
                    SettingsTab.HomeSliderVideoVolume.Value = video.Value;
                    if (SettingsTab.HomeTxtVideoVolume != null)
                        SettingsTab.HomeTxtVideoVolume.Text = $"{(int)video.Value}%";
                }

                var duckLevel = AppSettingsTab?.SliderDuck;
                if (duckLevel != null && SettingsTab?.HomeSliderDuck != null)
                {
                    SettingsTab.HomeSliderDuck.Value = duckLevel.Value;
                    if (SettingsTab.HomeTxtDuck != null)
                        SettingsTab.HomeTxtDuck.Text = $"{(int)duckLevel.Value}%";
                }

                var noDuckBrowser = AppSettingsTab?.ChkExcludeBambiCloudDucking;
                if (noDuckBrowser != null && SettingsTab?.HomeChkExcludeBambiCloudDucking != null)
                    SettingsTab.HomeChkExcludeBambiCloudDucking.IsChecked = noDuckBrowser.IsChecked;

                // The device list is shared rather than re-enumerated: two ComboBoxes over one
                // ItemsSource, so a refresh in either place is instantly true in both.
                var devices = AppSettingsTab?.CmbAudioOutputDevice;
                if (devices != null && SettingsTab?.HomeCmbAudioOutputDevice != null)
                {
                    SettingsTab.HomeCmbAudioOutputDevice.DisplayMemberPath = devices.DisplayMemberPath;
                    SettingsTab.HomeCmbAudioOutputDevice.ItemsSource = devices.ItemsSource;
                    SettingsTab.HomeCmbAudioOutputDevice.SelectedItem = devices.SelectedItem;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("MirrorAudioToHome: {E}", ex.Message); }
            finally { _homeAudioMirroring = false; }
        }
    }
}
