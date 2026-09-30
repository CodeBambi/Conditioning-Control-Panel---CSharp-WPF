using System;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// WPF RemoteControlService.ExecuteCommand's dispatch table for the features Core drives. Runs on the
    /// UI thread (RemoteRelay marshals). Returns null when the verb ran, else the refusal the controller
    /// sees: a verb with no surface on this head says so rather than reporting "ok" and doing nothing.
    /// ponytail: pink filter, spiral, opacity, HypnoTube, autonomy, mind wipe, wallpaper and the session
    /// verbs have no Core entry point yet; they refuse until their services move.
    /// </summary>
    public static class RemoteCommands
    {
        public const string NotOnThisBuild = "not on this build";

        /// <summary>The command log / notification label key per verb (WPF MainWindow.xaml.cs CommandLabels);
        /// anything else shows as the verb with spaces.</summary>
        public static readonly System.Collections.Generic.Dictionary<string, string> LabelKeys = new()
        {
            ["show_pink_filter"] = "cmd_pink_filter_enabled", ["stop_pink_filter"] = "cmd_pink_filter_disabled",
            ["show_spiral"] = "cmd_spiral_enabled", ["stop_spiral"] = "cmd_spiral_disabled",
            ["start_bubbles"] = "cmd_bubbles_started", ["stop_bubbles"] = "cmd_bubbles_stopped",
            ["trigger_video"] = "cmd_video_triggered", ["trigger_haptic"] = "cmd_haptic_triggered",
            ["trigger_bubble_count"] = "cmd_bubble_count_triggered",
            ["start_autonomy"] = "cmd_autonomy_enabled", ["stop_autonomy"] = "cmd_autonomy_disabled",
            ["start_session"] = "cmd_session_started", ["pause_session"] = "cmd_session_paused",
            ["resume_session"] = "cmd_session_resumed", ["stop_session"] = "cmd_session_stopped",
            ["enable_strict_lock"] = "cmd_strict_lock_enabled", ["disable_strict_lock"] = "cmd_strict_lock_disabled",
            ["disable_panic"] = "cmd_panic_key_disabled", ["enable_panic"] = "cmd_panic_key_enabled",
            ["trigger_panic"] = "cmd_all_effects_stopped",
        };

        /// <summary>Verbs too frequent to log or announce (WPF MainWindow.xaml.cs SuppressedCommands).</summary>
        public static readonly System.Collections.Generic.HashSet<string> Quiet = new()
        { "trigger_flash", "trigger_subliminal", "set_pink_opacity", "set_spiral_opacity", "duck_audio", "unduck_audio" };

        public static string? Execute(string action, JObject? p)
        {
            var s = CoreSettings.Current;
            switch (action)
            {
                case "trigger_flash":
                    if (CoreFlash.ShowProvider is not { } flash) return NotOnThisBuild;
                    flash();
                    return null;
                case "trigger_subliminal":
                case "trigger_custom_subliminal":
                    var text = action == "trigger_subliminal" ? CoreSubliminal.PickPhrase() : p?["text"]?.ToString();
                    if (string.IsNullOrWhiteSpace(text)) return action == "trigger_subliminal" ? "no phrases" : null;   // WPF: blank text is a no-op
                    if (CoreSubliminal.ShowProvider is not { } sub) return NotOnThisBuild;
                    sub(text);
                    return null;
                case "start_flash": s.FlashEnabled = true; CoreFlash.Start(); return null;
                case "stop_flash": CoreFlash.Stop(); return null;
                case "start_subliminal": s.SubliminalEnabled = true; CoreSubliminal.Start(); return null;
                case "stop_subliminal": CoreSubliminal.Stop(); return null;
                case "start_bubbles": CoreBubbles.Start(); return null;
                case "stop_bubbles": CoreBubbles.Stop(); return null;
                case "start_bounce_text": CoreBouncingText.Start(); return null;
                case "stop_bounce_text": CoreBouncingText.Stop(); return null;
                case "trigger_video": return CoreEngine.Video?.Trigger() == true ? null : CoreEngine.Video == null ? NotOnThisBuild : "a video is already playing";
                case "start_video": if (CoreEngine.Video == null) return NotOnThisBuild; CoreEngine.Video.Start(); return null;
                case "stop_video": CoreEngine.Video?.Stop(); return null;
                case "trigger_bubble_count": if (CoreEngine.BubbleCount == null) return NotOnThisBuild; CoreEngine.BubbleCount.Trigger(forceTest: true); return null;
                case "start_lock_card": s.LockCardEnabled = true; LockCardScheduler.Instance.Start(); return null;
                case "stop_lock_card": LockCardScheduler.Instance.Stop(); return null;
                case "trigger_haptic": if (CoreHaptics.Service == null) return NotOnThisBuild; _ = CoreHaptics.Service.TriggerAsync("remote_control", 0.7, 2000); return null;
                case "duck_audio": CoreAudio.Duck(80); return null;
                case "unduck_audio": CoreAudio.Unduck(); return null;
                case "enable_strict_lock": s.StrictLockEnabled = true; CoreSettings.Save(); return null;
                case "disable_strict_lock": s.StrictLockEnabled = false; CoreSettings.Save(); return null;
                case "enable_panic": s.PanicKeyEnabled = true; CoreSettings.Save(); SyncPanicUi(); return null;
                case "trigger_panic": StopEffects(force: true); return null;
                default: return NotOnThisBuild;
            }
        }

        /// <summary>WPF StopAllRemoteEffects: everything a controller could start stops; strict off, panic
        /// key on. Only <paramref name="force"/> (trigger_panic) takes the subject's own engine down (#878).</summary>
        public static void StopEffects(bool force)
        {
            try { CoreHaptics.Service?.PanicStop(); } catch { }
            CoreAudio.Unduck();
            if (force) CoreEngine.Stop();
            else
            {
                CoreFlash.Stop(); CoreSubliminal.Stop(); CoreBubbles.Stop(); CoreBouncingText.Stop();
                CoreEngine.Video?.Stop(); CoreEngine.BubbleCount?.Stop(); LockCardScheduler.Instance.Stop();
            }
            // As WPF, Lockdown or not: this only reduces restraint, and LockdownService.Deactivate restores
            // the pre-lockdown values when the timer ends.
            var s = CoreSettings.Current;
            if (s.StrictLockEnabled || !s.PanicKeyEnabled)
            {
                s.StrictLockEnabled = false;
                s.PanicKeyEnabled = true;
                CoreSettings.Save();
                SyncPanicUi();
            }
        }

        private static void SyncPanicUi() { try { LockdownService.PanicKeyUiSync?.Invoke(); } catch { } }
    }
}
