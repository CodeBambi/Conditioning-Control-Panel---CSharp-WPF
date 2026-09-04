using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using Serilog;
// Aliased for the same reason the WPF original aliases it: the parent namespace has its own
// AwarenessIntensity (Z5's 3-stop dial, AwarenessPrivacyView.axaml.cs) and wins name resolution
// from a child namespace, so the settings enum has to be named explicitly.
using Intensity = ConditioningControlPanel.Services.Awareness.AwarenessIntensity;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime
{
    /// <summary>
    /// Z8 · AWARENESS FINE-TUNING, ported from the WPF head with its settings logic restored
    /// against <see cref="CoreSettings"/>.
    ///
    /// <para>On WPF three of the four handlers hop through MainWindow only to write a setting
    /// (<c>SetAwarenessIntensity</c> in MainWindow.CompanionRoom.cs, the two cooldown sliders in
    /// MainWindow.Patreon.cs:1322/1332); those writes happen here directly, in the same order and
    /// with the same guards. The seed the WPF cell got from MainWindow.Patreon.cs:1048-1052 is
    /// folded into <see cref="SyncIntensity"/>, because this head has no MainWindow to do it.</para>
    ///
    /// <para>The <c>_syncing</c> guard is load-bearing: Avalonia raises IsCheckedChanged and
    /// ValueChanged on a programmatic set exactly as WPF raised Checked, so seeding without it
    /// would save the defaults back over the user's file.</para>
    /// </summary>
    public partial class WorkshopAwarenessCell : UserControl
    {
        private bool _syncing = true;

        public WorkshopAwarenessCell()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns the
            // x:Name fields, and the seed below reads them.
            InitializeComponent();

            foreach (var radio in IntensityStops())
                radio.IsCheckedChanged += IntensityRadio_Checked;

            SliderAwarenessCooldown.ValueChanged += SliderAwarenessCooldown_ValueChanged;
            SliderAwarenessCooldownMax.ValueChanged += SliderAwarenessCooldownMax_ValueChanged;
            BtnPrivacySpoiler.Click += BtnPrivacySpoiler_Click;

            SyncIntensity();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += OnCurrentReplaced;
            SyncIntensity();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnCurrentReplaced;
            base.OnDetachedFromVisualTree(e);
        }

        // A cloud restore or a factory reset swaps the instance; repaint from it, on the UI thread.
        private void OnCurrentReplaced() => Dispatcher.UIThread.Post(SyncIntensity);

        private RadioButton[] IntensityStops() =>
            new[] { RadioIntensityOff, RadioIntensitySubtle, RadioIntensityChatty, RadioIntensityUnhinged };

        /// <summary>
        /// Pushes the stored intensity onto the dial, seeds the legacy cooldown sliders and reveals
        /// the "her eyes are closed" note. Writes are suppressed while it runs so restoring a
        /// control cannot round-trip back through its handler and re-save.
        /// </summary>
        public void SyncIntensity()
        {
            _syncing = true;
            try
            {
                var s = CoreSettings.Current;
                var intensity = s.AwarenessIntensity;

                foreach (var radio in IntensityStops())
                    radio.IsChecked = string.Equals(radio.Tag as string, intensity.ToString(), StringComparison.Ordinal);

                // The dial is superseded by the sliders when the v2 kill switch is down; showing both
                // would offer two pacing controls, one of which does nothing.
                bool v2 = s.UseAwarenessV2;
                AwarenessIntensityPanel.IsVisible = v2;
                AwarenessSettingsPanel.IsVisible = !v2;

                bool eyesOpen = s.AwarenessModeEnabled && s.AwarenessConsentGiven;
                TxtIntensityEyesClosed.IsVisible = v2 && !eyesOpen;

                // Seeded here because this head has no MainWindow.LoadSettings sweep to do it. The
                // label is set explicitly rather than left to ValueChanged, which _syncing blocks.
                SliderAwarenessCooldown.Value = s.AwarenessReactionCooldownSeconds;
                TxtAwarenessCooldown.Text = $"{s.AwarenessReactionCooldownSeconds}s";
                SliderAwarenessCooldownMax.Value = s.AwarenessCooldownMaxSeconds;
                TxtAwarenessCooldownMax.Text = s.AwarenessCooldownMaxSeconds <= 0
                    ? Loc.Get("label_cooldown_off")
                    : $"{s.AwarenessCooldownMaxSeconds}s";

                // The spoiler's own label: chosen in code because it flips between two keys.
                TxtPrivacySpoiler.Text = Loc.Get(TxtPrivacyDetails.IsVisible ? "btn_hide" : "btn_click_to_reveal");

                // The privacy notice describes DATA HANDLING, and the two pipelines handle it
                // differently: the legacy one sends the page title and keeps nothing, v2 keeps local
                // counters and sends no title. One wording cannot be true of both, so the notice
                // follows the pipeline that is actually running.
                TxtPrivacyDetails.Text = Loc.Get(v2
                    ? "label_awareness_privacy_notice_v2"
                    : "label_this_feature_reads_the_name_of_the_active_win");
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "WorkshopAwarenessCell.SyncIntensity failed");
            }
            finally
            {
                _syncing = false;
            }
        }

        /// <summary>
        /// One handler for all four stops; the stop names itself through <c>Tag</c> so reordering the
        /// XAML cannot remap a saved setting onto the wrong intensity. The body is MainWindow's
        /// <c>SetAwarenessIntensity</c> (MainWindow.CompanionRoom.cs:257), which on WPF this cell
        /// only forwarded to.
        /// </summary>
        private void IntensityRadio_Checked(object? sender, RoutedEventArgs e)
        {
            if (_syncing) return;
            if (sender is not RadioButton { IsChecked: true, Tag: string tag }) return;
            if (!Enum.TryParse<Intensity>(tag, ignoreCase: true, out var intensity)) return;

            var s = CoreSettings.Current;
            if (s.AwarenessIntensity == intensity) return;

            s.AwarenessIntensity = intensity;
            // The migration flag is what stops a later start-up from overwriting this choice.
            s.AwarenessIntensityMigrated = true;
            CoreSettings.Save();

            Log.Information("Awareness intensity set to {Intensity}", intensity);
            // ponytail: WPF also re-syncs the room's hero through CompanionRoom.AwarenessVm.Sync()
            // (ConditioningControlPanel/MainWindow/MainWindow.CompanionRoom.cs). The HOST is no
            // longer the blocker - CompanionRoomView composes CompanionHeroCard now - but the pill
            // it would repaint is CompanionHeroCardViewModel.AwarenessPillText, which is init-only
            // and seeded from a viewmodel this head does not build from settings. Give the hero an
            // awareness re-read and this becomes one call.
        }

        private void SliderAwarenessCooldown_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_syncing) return;
            var value = (int)SliderAwarenessCooldown.Value;
            TxtAwarenessCooldown.Text = $"{value}s";
            CoreSettings.Current.AwarenessReactionCooldownSeconds = value;
            CoreSettings.Save();
        }

        private void SliderAwarenessCooldownMax_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_syncing) return;
            // 0 (or below the base cooldown) = randomization off; the fixed base cooldown is used.
            var value = (int)SliderAwarenessCooldownMax.Value;
            TxtAwarenessCooldownMax.Text = value <= 0 ? Loc.Get("label_cooldown_off") : $"{value}s";
            CoreSettings.Current.AwarenessCooldownMaxSeconds = value;
            CoreSettings.Save();
        }

        private void BtnPrivacySpoiler_Click(object? sender, RoutedEventArgs e)
        {
            TxtPrivacyDetails.IsVisible = !TxtPrivacyDetails.IsVisible;
            TxtPrivacySpoiler.Text = Loc.Get(TxtPrivacyDetails.IsVisible ? "btn_hide" : "btn_click_to_reveal");
        }
    }
}
