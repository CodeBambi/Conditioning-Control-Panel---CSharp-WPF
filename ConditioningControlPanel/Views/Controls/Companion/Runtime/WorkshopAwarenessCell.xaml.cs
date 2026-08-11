using System;
using System.Windows;
using System.Windows.Controls;
// Aliased: the parent namespace has its own AwarenessIntensity (Z5's 3-stop dial) and wins
// name resolution here, so the settings enum has to be named explicitly.
using Intensity = ConditioningControlPanel.Services.Awareness.AwarenessIntensity;

namespace ConditioningControlPanel.Views.Controls.Companion.Runtime
{
    /// <summary>
    /// Z8 · AWARENESS FINE-TUNING. See the XAML header. Mostly forwarding, plus the intensity dial's
    /// read-back — the one place in this cell that owns a decision rather than passing one along.
    /// </summary>
    public partial class WorkshopAwarenessCell : UserControl
    {
        private bool _syncing;

        public WorkshopAwarenessCell()
        {
            InitializeComponent();
            Loaded += (_, _) => SyncIntensity();
        }

        /// <summary>
        /// Pushes the stored intensity onto the dial and reveals the "her eyes are closed" note.
        /// Called from the page's sync and on load; writes are suppressed while it runs so restoring
        /// the radio cannot round-trip back through <see cref="IntensityRadio_Checked"/> and re-save.
        /// </summary>
        public void SyncIntensity()
        {
            _syncing = true;
            try
            {
                var settings = App.Settings?.Current;
                var intensity = settings?.AwarenessIntensity ?? Intensity.Chatty;

                RadioIntensityOff.IsChecked = intensity == Intensity.Off;
                RadioIntensitySubtle.IsChecked = intensity == Intensity.Subtle;
                RadioIntensityChatty.IsChecked = intensity == Intensity.Chatty;
                RadioIntensityUnhinged.IsChecked = intensity == Intensity.Unhinged;

                // The dial is superseded by the sliders when the v2 kill switch is down; showing both
                // would offer two pacing controls, one of which does nothing.
                bool v2 = settings?.UseAwarenessV2 ?? true;
                AwarenessIntensityPanel.Visibility = v2 ? Visibility.Visible : Visibility.Collapsed;

                bool eyesOpen = settings?.AwarenessModeEnabled == true && settings?.AwarenessConsentGiven == true;
                TxtIntensityEyesClosed.Visibility = v2 && !eyesOpen ? Visibility.Visible : Visibility.Collapsed;

                // The privacy notice describes DATA HANDLING, and the two pipelines handle it
                // differently: the legacy one sends the page title and keeps nothing, v2 keeps local
                // counters and sends no title. One wording cannot be true of both, so the notice
                // follows the pipeline that is actually running.
                TxtPrivacyDetails.Text = Localization.Loc.Get(v2
                    ? "label_awareness_privacy_notice_v2"
                    : "label_this_feature_reads_the_name_of_the_active_win");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("WorkshopAwarenessCell.SyncIntensity failed: {E}", ex.Message);
            }
            finally
            {
                _syncing = false;
            }
        }

        /// <summary>
        /// One handler for all four stops; the stop names itself through <c>Tag</c> so reordering the
        /// XAML cannot remap a saved setting onto the wrong intensity.
        /// </summary>
        private void IntensityRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;
            if (sender is not RadioButton radio || radio.Tag is not string tag) return;
            if (!Enum.TryParse<Intensity>(tag, ignoreCase: true, out var intensity)) return;

            if (Window.GetWindow(this) is MainWindow mw) mw.SetAwarenessIntensity(intensity);
        }

        private void SliderAwarenessCooldown_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SliderAwarenessCooldown_ValueChanged(sender, e);
        }

        private void SliderAwarenessCooldownMax_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SliderAwarenessCooldownMax_ValueChanged(sender, e);
        }

        private void BtnPrivacySpoiler_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnPrivacySpoiler_Click(sender, e);
        }
    }
}
