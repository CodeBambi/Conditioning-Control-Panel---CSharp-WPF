using System;
using System.Text.Json.Serialization;

namespace ConditioningControlPanel.Models.AiEnrichment
{
    /// <summary>
    /// Lightweight, AI-readable snapshot of the application's current runtime state.
    /// Included in the enrichment context block so the model can tailor replies without
    /// hallucinating what is currently active in the app.
    /// </summary>
    public class AiContextSnapshot
    {
        [JsonPropertyName("session_running")]
        public bool SessionRunning { get; set; }

        [JsonPropertyName("lockdown_active")]
        public bool LockdownActive { get; set; }

        [JsonPropertyName("lockdown_remaining_minutes")]
        public double LockdownRemainingMinutes { get; set; }

        [JsonPropertyName("autonomy_enabled")]
        public bool AutonomyEnabled { get; set; }

        [JsonPropertyName("autonomy_action_in_progress")]
        public bool AutonomyActionInProgress { get; set; }

        [JsonPropertyName("active_mod")]
        public string ActiveMod { get; set; } = string.Empty;

        [JsonPropertyName("persona")]
        public string Persona { get; set; } = string.Empty;

        [JsonPropertyName("overlay_running")]
        public bool OverlayRunning { get; set; }

        [JsonPropertyName("spiral_enabled")]
        public bool SpiralEnabled { get; set; }

        [JsonPropertyName("pink_filter_enabled")]
        public bool PinkFilterEnabled { get; set; }

        [JsonPropertyName("bubbles_running")]
        public bool BubblesRunning { get; set; }

        [JsonPropertyName("bouncing_text_running")]
        public bool BouncingTextRunning { get; set; }

        [JsonPropertyName("subliminal_running")]
        public bool SubliminalRunning { get; set; }

        [JsonPropertyName("flash_running")]
        public bool FlashRunning { get; set; }

        [JsonPropertyName("video_playing")]
        public bool VideoPlaying { get; set; }

        [JsonPropertyName("lock_card_active")]
        public bool LockCardActive { get; set; }

        /// <summary>
        /// Builds a best-effort snapshot from the current <see cref="App"/> state.
        /// Any null service is treated as inactive; this never throws.
        /// </summary>
        public static AiContextSnapshot Build()
        {
            var snapshot = new AiContextSnapshot();
            try
            {
                snapshot.SessionRunning = App.IsSessionRunning;
                snapshot.LockdownActive = App.Lockdown?.IsActive == true;
                snapshot.AutonomyEnabled = App.Autonomy?.IsEnabled == true;
                snapshot.AutonomyActionInProgress = App.Autonomy?.IsActionInProgress == true;
                snapshot.ActiveMod = App.Mods?.ActiveModId ?? string.Empty;
                snapshot.Persona = App.Personality?.GetActivePreset().Name ?? string.Empty;
                snapshot.OverlayRunning = App.Overlay?.IsRunning == true;
                snapshot.SpiralEnabled = App.Settings?.Current?.SpiralEnabled == true;
                snapshot.PinkFilterEnabled = App.Settings?.Current?.PinkFilterEnabled == true;
                snapshot.BubblesRunning = App.Bubbles?.IsRunning == true;
                snapshot.BouncingTextRunning = App.BouncingText?.IsRunning == true;
                snapshot.SubliminalRunning = App.Subliminal?.IsRunning == true;
                snapshot.FlashRunning = App.Flash?.IsRunning == true;
                snapshot.VideoPlaying = App.Video?.IsPlaying == true || App.DualMonitorVideo?.IsPlaying == true;
                snapshot.LockCardActive = App.LockCard?.IsRunning == true;

                if (snapshot.LockdownActive)
                {
                    snapshot.LockdownRemainingMinutes = App.Lockdown?.Remaining.TotalMinutes ?? 0;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AiContextSnapshot: failed to build snapshot");
            }
            return snapshot;
        }
    }
}
