using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime
{
    /// <summary>
    /// Z8 · AWARENESS FINE-TUNING. See the XAML header. Mostly forwarding, plus the intensity dial's
    /// read-back — the one place in this cell that owns a decision rather than passing one along.
    ///
    /// <para>The WPF code-behind reads App.Settings and forwards to MainWindow. Neither is on this
    /// head, so: the dial's read-back runs against defaults, the chosen stop leaves the cell as
    /// <see cref="IntensityChanged"/>, and the two behaviours that are purely local (the slider
    /// value labels, MainWindow.Patreon.cs:1327/1338; the privacy spoiler, :1308) are wired here.</para>
    /// </summary>
    public partial class WorkshopAwarenessCell : UserControl
    {
        private bool _syncing;

        /// <summary>Raised with the stop's Tag ("Off", "Subtle", "Chatty", "Unhinged") — the same
        /// string MainWindow.SetAwarenessIntensity parses into AwarenessIntensity.</summary>
        public event EventHandler<string>? IntensityChanged;

        public WorkshopAwarenessCell()
        {
            AvaloniaXamlLoader.Load(this);

            foreach (var name in new[] { "RadioIntensityOff", "RadioIntensitySubtle", "RadioIntensityChatty", "RadioIntensityUnhinged" })
                this.FindControl<RadioButton>(name)!.IsCheckedChanged += IntensityRadio_Checked;

            var cooldown = this.FindControl<Slider>("SliderAwarenessCooldown")!;
            var cooldownText = this.FindControl<TextBlock>("TxtAwarenessCooldown")!;
            cooldown.ValueChanged += (_, _) => cooldownText.Text = $"{(int)cooldown.Value}s";

            // 0 (or below the base cooldown) = randomization off; the fixed base cooldown is used.
            var cooldownMax = this.FindControl<Slider>("SliderAwarenessCooldownMax")!;
            var cooldownMaxText = this.FindControl<TextBlock>("TxtAwarenessCooldownMax")!;
            cooldownMax.ValueChanged += (_, _) =>
            {
                int value = (int)cooldownMax.Value;
                cooldownMaxText.Text = value <= 0 ? Loc.Get("label_cooldown_off") : $"{value}s";
            };

            var details = this.FindControl<TextBlock>("TxtPrivacyDetails")!;
            var spoilerText = this.FindControl<TextBlock>("TxtPrivacySpoiler")!;
            this.FindControl<Button>("BtnPrivacySpoiler")!.Click += (_, _) =>
            {
                details.IsVisible = !details.IsVisible;
                spoilerText.Text = Loc.Get(details.IsVisible ? "btn_hide" : "btn_click_to_reveal");
            };

            Loaded += (_, _) => SyncIntensity();
        }

        /// <summary>
        /// Pushes the stored intensity onto the dial and reveals the "her eyes are closed" note.
        /// Writes are suppressed while it runs so restoring the radio cannot round-trip back
        /// through <see cref="IntensityRadio_Checked"/> and re-save.
        /// </summary>
        public void SyncIntensity()
        {
            _syncing = true;
            try
            {
                // ponytail: needs App.Settings (AwarenessIntensity, UseAwarenessV2, AwarenessModeEnabled,
                // AwarenessConsentGiven); wired when settings move to Core. Until then the WPF
                // defaults: Chatty, v2 on, eyes closed.
                const string intensity = "Chatty";
                const bool v2 = true;
                const bool eyesOpen = false;

                foreach (var name in new[] { "RadioIntensityOff", "RadioIntensitySubtle", "RadioIntensityChatty", "RadioIntensityUnhinged" })
                {
                    var radio = this.FindControl<RadioButton>(name)!;
                    radio.IsChecked = string.Equals(radio.Tag as string, intensity, StringComparison.Ordinal);
                }

                // The dial is superseded by the sliders when the v2 kill switch is down; showing both
                // would offer two pacing controls, one of which does nothing.
                this.FindControl<Border>("AwarenessIntensityPanel")!.IsVisible = v2;
                this.FindControl<Border>("AwarenessSettingsPanel")!.IsVisible = !v2;
                this.FindControl<TextBlock>("TxtIntensityEyesClosed")!.IsVisible = v2 && !eyesOpen;

                // The privacy notice describes DATA HANDLING, and the two pipelines handle it
                // differently: the legacy one sends the page title and keeps nothing, v2 keeps local
                // counters and sends no title. One wording cannot be true of both, so the notice
                // follows the pipeline that is actually running.
                this.FindControl<TextBlock>("TxtPrivacyDetails")!.Text = Loc.Get(v2
                    ? "label_awareness_privacy_notice_v2"
                    : "label_this_feature_reads_the_name_of_the_active_win");
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
        private void IntensityRadio_Checked(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_syncing) return;
            if (sender is not RadioButton { IsChecked: true, Tag: string tag }) return;
            IntensityChanged?.Invoke(this, tag);
        }
    }
}
