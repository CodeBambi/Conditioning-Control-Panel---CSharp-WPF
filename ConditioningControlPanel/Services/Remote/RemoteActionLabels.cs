using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Remote
{
    /// <summary>
    /// Short human words for a remote command, for the HUD's "Last: ..." line. English for now
    /// (translation owed with the rest of the v2 copy). An unknown action reads as its own name
    /// with the underscores taken out, never as nothing.
    /// </summary>
    public static class RemoteActionLabels
    {
        private static readonly Dictionary<string, string> Labels = new()
        {
            ["trigger_flash"] = "Flash",
            ["start_flash"] = "Flashes on",
            ["stop_flash"] = "Flashes off",
            ["trigger_subliminal"] = "Word",
            ["trigger_custom_subliminal"] = "Their words",
            ["start_subliminal"] = "Words on",
            ["stop_subliminal"] = "Words off",
            ["show_pink_filter"] = "Pink",
            ["stop_pink_filter"] = "Pink off",
            ["set_pink_opacity"] = "Pink strength",
            ["show_spiral"] = "Spiral",
            ["stop_spiral"] = "Spiral off",
            ["set_spiral_opacity"] = "Spiral strength",
            ["start_bubbles"] = "Bubbles",
            ["stop_bubbles"] = "Bubbles off",
            ["trigger_video"] = "Video",
            ["start_video"] = "Videos on",
            ["stop_video"] = "Video off",
            ["play_hypnotube"] = "Video link",
            ["trigger_haptic"] = "Toy buzz",
            ["haptic_pattern"] = "Toy pattern",
            ["haptic_level"] = "Toy buzz",
            ["haptic_stop"] = "Toy off",
            ["start_brain_drain"] = "Melt",
            ["stop_brain_drain"] = "Melt off",
            ["duck_audio"] = "Music down",
            ["unduck_audio"] = "Music up",
            ["start_autonomy"] = "Autonomy",
            ["stop_autonomy"] = "Autonomy off",
            ["trigger_bubble_count"] = "Bubble count",
            ["trigger_lock_card"] = "Lock card",
            ["start_lock_card"] = "Lock cards on",
            ["stop_lock_card"] = "Lock cards off",
            ["trigger_mind_wipe"] = "Whisper",
            ["start_mind_wipe"] = "Whispers",
            ["stop_mind_wipe"] = "Whispers off",
            ["start_bounce_text"] = "Bouncing words",
            ["stop_bounce_text"] = "Bouncing off",
            ["start_session"] = "Session",
            ["pause_session"] = "Session paused",
            ["resume_session"] = "Session resumed",
            ["stop_session"] = "Session stopped",
            ["disable_strict_lock"] = "Strict lock off",
            ["enable_panic"] = "Panic key on",
            ["trigger_wallpaper"] = "Wallpaper",
            ["stop_wallpaper"] = "Wallpaper off",
            ["trigger_panic"] = "Stopped everything",
        };

        public static string For(string? action)
        {
            if (string.IsNullOrWhiteSpace(action)) return "";
            return Labels.TryGetValue(action, out var label) ? label : action.Replace('_', ' ');
        }
    }
}
