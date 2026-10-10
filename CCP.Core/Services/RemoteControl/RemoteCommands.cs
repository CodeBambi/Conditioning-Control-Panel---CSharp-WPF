using System;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// WPF RemoteControlService.ExecuteCommand's dispatch table for the features Core drives. Runs on the
    /// UI thread (RemoteRelay marshals). Returns null when the verb ran, else the refusal the controller
    /// sees: a verb with no surface on this head says so rather than reporting "ok" and doing nothing.
    /// Verbs that need a window (pink filter, spiral, the Melt haze, a lock card now, Takeover, the session
    /// verbs) go through <see cref="Head"/>; with no head they refuse. No tier or waiver check here: WPF
    /// has none on the receiving side either (the relay only hands a session the verbs of its tier).
    /// ponytail: play_hypnotube (the controller supplies a url: owner call) still refuses.
    /// </summary>
    public static class RemoteCommands
    {
        public const string NotOnThisBuild = "not on this build";
        public const string NoOverlayHere = "no overlay on this display";

        /// <summary>What the desktop head does for the verbs Core cannot (MainShellWindow.RemoteVerbs.cs).
        /// Every member runs on the UI thread. A string result is the refusal the controller sees, null = done.</summary>
        public interface IRemoteHead
        {
            /// <summary>"pink" or "spiral": bring it in line with the settings and <see cref="OverlayHold"/>.
            /// True when it is on screen (or still loading) afterwards.</summary>
            bool RefreshOverlay(string which);
            /// <summary>Bring the Melt haze in line; a reason when this system cannot draw it.</summary>
            string? RefreshBrainDrain();
            string? ShowLockCard();
            /// <summary>WPF LockCardWindow.ForceCloseAll + BubbleCountWindow.ForceCloseAll.</summary>
            void CloseCards();
            string? SetAutonomy(bool on);
            /// <summary>WPF CancelActivePulses; <paramref name="restart"/> = Stop, then Start again if the
            /// subject's own switch is on (a controller never switches Takeover off for good, #299).</summary>
            void CancelAutonomyPulses(bool restart);
            /// <summary>start_session / pause_session / resume_session / stop_session.</summary>
            string? Session(string verb, JObject? p);
            /// <summary>WPF IsSessionRemoteStarted: the run on screen is the one a controller started.</summary>
            bool SessionIsRemoteStarted { get; }
            /// <summary>WPF RestoreFromTrayForRemote + ShowAvatarTube: after a remote stop the panel comes
            /// back from the tray. Default: nothing (a head without a tray).</summary>
            void RestoreWindow() { }
            /// <summary>trigger_wallpaper (true: a picture from the SUBJECT's own wallpaper folder; the
            /// controller never supplies one) / stop_wallpaper and every stop path (false: the desktop the
            /// subject had comes back). A reason when this system cannot change the wallpaper.</summary>
            string? Wallpaper(bool on) => NotOnThisBuild;
        }

        public static volatile IRemoteHead? Head;

        /// <summary>WPF EnsureOverlayRunning (OverlayService.BypassLevelCheck + Start): while it holds, the
        /// pink filter, the spiral and the Melt haze show with the engine off. Set by the controller's
        /// overlay verbs; dropped by every stop path, the controller leaving and panic.</summary>
        public static bool OverlayHold { get; private set; }

        // What the controller switched on that was off before: handed back when it leaves.
        private static bool _remotePink, _remoteSpiral, _remoteBrainDrain;

        private static string? Overlay(string which, bool on)
        {
            if (Head is not { } head) return NotOnThisBuild;
            var s = CoreSettings.Current;
            var pink = which == "pink";
            var was = pink ? s.PinkFilterEnabled : s.SpiralEnabled;
            var holdWas = OverlayHold;
            if (pink) s.PinkFilterEnabled = on; else s.SpiralEnabled = on;
            if (on) OverlayHold = true;
            var showing = head.RefreshOverlay(which);
            if (on && !showing)
            {
                // Nothing reached the screen: the subject's settings stay as they were, and the controller is told.
                if (pink) s.PinkFilterEnabled = was; else s.SpiralEnabled = was;
                OverlayHold = holdWas;
                head.RefreshOverlay(which);
                return NoOverlayHere;
            }
            if (pink) _remotePink = on && (!was || _remotePink); else _remoteSpiral = on && (!was || _remoteSpiral);
            CoreSettings.Save();
            return null;
        }

        private static string? OverlayOpacity(string which, JObject? p)
        {
            if (Head is not { } head) return NotOnThisBuild;
            if (p == null) return null;                      // WPF: no params is a no-op
            var s = CoreSettings.Current;
            int value;
            try { value = p["value"]?.Value<int>() ?? 25; } catch { return "bad params"; }
            // WPF EasedOpacity.Ask: clamp to 0..max (pink 50, spiral 100), then the subject's Easy factor.
            if (which == "pink") s.PinkFilterOpacity = _easedPink.Ask(value, 50, EasyFactor);
            else s.SpiralOpacity = _easedSpiral.Ask(value, 100, EasyFactor);
            OverlayHold = true;
            head.RefreshOverlay(which);
            CoreSettings.Save();
            return null;
        }

        private static string? BrainDrain(bool on)
        {
            if (Head is not { } head) return NotOnThisBuild;
            var s = CoreSettings.Current;
            if (!on)
            {
                _remoteBrainDrain = false;
                s.BrainDrainEnabled = false;
                CoreBrainDrain.Stop();
                head.RefreshBrainDrain();
                CoreSettings.Save();
                return null;
            }
            var (was, holdWas) = (s.BrainDrainEnabled, OverlayHold);
            s.BrainDrainEnabled = true;
            OverlayHold = true;
            if (head.RefreshBrainDrain() is { } why)
            {
                (s.BrainDrainEnabled, OverlayHold) = (was, holdWas);
                head.RefreshBrainDrain();
                return why;
            }
            if (!was) _remoteBrainDrain = true;
            CoreBrainDrain.Start();
            CoreSettings.Save();
            return null;
        }

        /// <summary>The hold drops and all three overlays re-read the settings. UI thread.</summary>
        private static void DropOverlayHold()
        {
            OverlayHold = false;
            if (Head is not { } head) return;
            try { head.RefreshOverlay("pink"); head.RefreshOverlay("spiral"); head.RefreshBrainDrain(); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "[RemoteControl] overlay refresh failed"); }
        }

        /// <summary>The controller left or idled out: what it put on the screen comes down, whatever
        /// StopEffectsOnRemoteDisconnect says (that switch is about the loops the subject can stop from the
        /// panel; a full-screen overlay with the engine off and nobody driving is not left up). An overlay
        /// the subject had on themselves keeps its setting and follows their engine again. UI thread.</summary>
        public static void ControllerLeft()
        {
            var s = CoreSettings.Current;
            var changed = _remotePink || _remoteSpiral || _remoteBrainDrain;
            if (_remotePink) s.PinkFilterEnabled = false;
            if (_remoteSpiral) s.SpiralEnabled = false;
            if (_remoteBrainDrain) { s.BrainDrainEnabled = false; CoreBrainDrain.Stop(); }
            (_remotePink, _remoteSpiral, _remoteBrainDrain) = (false, false, false);
            DropOverlayHold();
            if (changed) CoreSettings.Save();
        }

        /// <summary>Panic, on the UI thread (PanicSurfaces "remote-overlays"): the hold drops, so nothing a
        /// controller put up outlives the press; the settings stay for the session-end path to hand back.</summary>
        public static void PanicDropOverlays() => DropOverlayHold();

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
        { "trigger_flash", "trigger_subliminal", "set_pink_opacity", "set_spiral_opacity", "duck_audio", "unduck_audio",
          // v2: hold-to-buzz sends one every second while held (main 719ed9ca5)
          "haptic_level", "haptic_stop" };

        /// <summary>The remote haptic player (WPF RemoteControlService.RemoteHaptics), scaled by Easy.</summary>
        internal static Remote.CoreRemoteHapticDriver RemoteHaptics = new(() => EasyFactor);   // tests swap in a stepped clock

        // ------------------------------------------------------------------ Easy (WPF RemoteControlService.V2.cs)

        private static readonly Remote.EasedOpacity _easedSpiral = new(), _easedPink = new();

        /// <summary>Scales every remote haptic and remote-set spiral / pink opacity. 1 at the start of a
        /// session, halved by each Easy press (floor 0.25), back to 1 when the session ends.</summary>
        public static double EasyFactor { get; private set; } = 1.0;

        /// <summary>Raised when <see cref="EasyFactor"/> changes.</summary>
        public static event EventHandler? EasyChanged;

        /// <summary>WPF ApplyEasy: halves the remote's strength for the rest of the session (floor 0.25): the
        /// haptic replays at the new level, a controller-set (or showing) spiral and pink fade. UI thread.</summary>
        public static void ApplyEasy()
        {
            var next = Remote.RemoteEasy.Next(EasyFactor);
            if (next == EasyFactor) return;
            EasyFactor = next;
            Serilog.Log.Information("[RemoteControl] Easy: remote strength now x{Factor}", next);
            var s = CoreSettings.Current;
            var changed = false;
            if (_easedSpiral.Rescale(s.SpiralOpacity, s.SpiralEnabled, next) is int sv) { s.SpiralOpacity = sv; changed = true; }
            if (_easedPink.Rescale(s.PinkFilterOpacity, s.PinkFilterEnabled, next) is int pv) { s.PinkFilterOpacity = pv; changed = true; }
            if (changed)
            {
                try { Head?.RefreshOverlay("spiral"); Head?.RefreshOverlay("pink"); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "[RemoteControl] overlay refresh failed"); }
                CoreSettings.Save();
            }
            try { RemoteHaptics.Rescale(); } catch (Exception ex) { Serilog.Log.Warning(ex, "[RemoteControl] haptic rescale failed"); }
            EasyChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>WPF RestoreEasedOpacities + the Easy half of ResetV2SessionState: the session ended, so
        /// where Easy faded the subject's own opacity it is handed back and the factor returns to 1.</summary>
        public static void ResetEasy()
        {
            var s = CoreSettings.Current;
            var changed = false;
            if (_easedSpiral.SubjectOriginal is int so) { s.SpiralOpacity = so; changed = true; }
            if (_easedPink.SubjectOriginal is int po) { s.PinkFilterOpacity = po; changed = true; }
            _easedSpiral.Reset();
            _easedPink.Reset();
            if (changed) CoreSettings.Save();
            if (EasyFactor != 1.0)
            {
                EasyFactor = 1.0;
                EasyChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        private static int _panicGeneration;
        /// <summary>Moves on every panic: a command fetched before it never runs after it (RemoteRelay).</summary>
        public static int PanicGeneration => Volatile.Read(ref _panicGeneration);

        /// <summary>Panic stops the remote haptic loop (decisions 2026-10-08). HapticMixer.PanicStop only
        /// silences for 400 ms; without this the driver's next tick restarts the loop. Every panic path
        /// on every head calls it, any thread.</summary>
        public static void StopHaptics()
        {
            Interlocked.Increment(ref _panicGeneration);
            try { RemoteHaptics.Stop(); } catch (Exception ex) { Serilog.Log.Warning(ex, "[RemoteControl] remote haptic stop failed"); }
        }

        /// <summary>WPF App.Chaster?.Note(id) (RemoteControlService.cs:1245, :1358). The head seeds it.</summary>
        public static volatile Action<string>? ChasterNote;

        private static void Chaster(string id)
        {
            try { ChasterNote?.Invoke(id); } catch (Exception ex) { Serilog.Log.Debug(ex, "[RemoteControl] chaster remote hook"); }
        }

        public static string? Execute(string action, JObject? p)
        {
            var s = CoreSettings.Current;
            switch (action)
            {
                case "trigger_flash":
                    if (CoreFlash.ShowProvider is not { } flash) return NotOnThisBuild;
                    flash();
                    Chaster("remote_media");   // Circe's tab: what the controller sends lands on the wearer's tab
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
                case "trigger_video":
                    if (CoreEngine.Video is not { } video) return NotOnThisBuild;
                    var played = video.Trigger();
                    Chaster("remote_video");   // WPF :1357-1358 books the send whether or not a video was already up
                    return played ? null : "a video is already playing";
                case "start_video": if (CoreEngine.Video == null) return NotOnThisBuild; CoreEngine.Video.Start(); return null;
                case "stop_video": CoreEngine.Video?.Stop(); return null;
                case "show_pink_filter": return Overlay("pink", true);
                case "stop_pink_filter": return Overlay("pink", false);
                case "show_spiral": return Overlay("spiral", true);
                case "stop_spiral": return Overlay("spiral", false);
                case "set_pink_opacity": return OverlayOpacity("pink", p);
                case "set_spiral_opacity": return OverlayOpacity("spiral", p);
                case "start_brain_drain": return BrainDrain(true);
                case "stop_brain_drain": return BrainDrain(false);
                case "start_autonomy": return Head is { } ha ? ha.SetAutonomy(true) : NotOnThisBuild;
                case "stop_autonomy": return Head is { } hb ? hb.SetAutonomy(false) : NotOnThisBuild;
                case "trigger_lock_card": return Head is { } hc ? hc.ShowLockCard() : NotOnThisBuild;
                case "start_session":
                case "pause_session":
                case "resume_session":
                case "stop_session":
                    return Head is { } hs ? hs.Session(action, p) : NotOnThisBuild;
                case "trigger_bubble_count": if (CoreEngine.BubbleCount == null) return NotOnThisBuild; CoreEngine.BubbleCount.Trigger(forceTest: true); return null;
                case "start_lock_card": s.LockCardEnabled = true; LockCardScheduler.Instance.Start(); return null;
                case "stop_lock_card": LockCardScheduler.Instance.Stop(); return null;
                case "trigger_haptic":
                    if (CoreHaptics.Service?.IsConnected != true) return "no_device";   // WPF 7b22ece8c (ccp-bugs #1065): never a silent "ok"
                    _ = CoreHaptics.Service!.TriggerAsync("remote_control", 0.7 * EasyFactor, 2000); return null;
                // Remote Control v2 (main 719ed9ca5): a pattern or hold-to-buzz level replaces whatever remote haptic plays.
                case "haptic_pattern":
                case "haptic_level":
                    var plan = action == "haptic_level" ? Remote.RemoteHapticPlan.FromLevel(p, out var why) : Remote.RemoteHapticPlan.FromPattern(p, out why);
                    if (plan == null) return why ?? "bad params";
                    RemoteHaptics.Play(plan);
                    return null;
                case "haptic_stop": RemoteHaptics.Stop(); return null;
                case "duck_audio": CoreAudio.Duck(80); return null;
                case "unduck_audio": CoreAudio.Unduck(); return null;
                // WPF RemoteControlService.cs:1457-1470. No clips: a silent no-op there, told to the controller here.
                case "trigger_mind_wipe":
                    if (CoreMindWipe.TriggerOnceProvider == null) return NotOnThisBuild;
                    if (CoreMindWipe.ClipCount <= 0) return "no clips";
                    CoreMindWipe.TriggerOnce();
                    return null;
                case "start_mind_wipe":
                    if (CoreMindWipe.StartProvider == null) return NotOnThisBuild;
                    CoreMindWipe.Start(s.MindWipeFrequency, s.MindWipeVolume / 100.0);
                    return null;
                case "stop_mind_wipe": CoreMindWipe.Stop(); return null;
                case "enable_strict_lock": s.StrictLockEnabled = true; CoreSettings.Save(); return null;
                case "disable_strict_lock": s.StrictLockEnabled = false; CoreSettings.Save(); return null;
                case "enable_panic": s.PanicKeyEnabled = true; CoreSettings.Save(); SyncPanicUi(); return null;
                case "trigger_panic": StopEffects(force: true); return null;
                // WPF: App.Wallpaper Shuffle / Activate, and Deactivate.
                case "trigger_wallpaper": return Head is { } hw ? hw.Wallpaper(true) : NotOnThisBuild;
                case "stop_wallpaper": return Head is { } hx ? hx.Wallpaper(false) : NotOnThisBuild;
                default: return NotOnThisBuild;
            }
        }

        /// <summary>WPF StopAllRemoteEffects: everything a controller could start stops; strict off, panic
        /// key on. Only <paramref name="force"/> (trigger_panic) takes the subject's own engine down (#878).</summary>
        public static void StopEffects(bool force)
        {
            if (force) Commands.AiCommandService.CancelAll();   // a remote panic cancels pending AI follow-ups too
            if (force) StopHaptics(); else RemoteHaptics.Stop();
            try { CoreHaptics.Service?.PanicStop(); } catch { }
            CoreAudio.Unduck();
            CoreMindWipe.Stop();   // WPF App.MindWipe?.Stop() on both stop paths
            CoreBrainDrain.Stop(); // WPF App.BrainDrain?.Stop() on both stop paths
            var head = Head;
            try { head?.CancelAutonomyPulses(restart: force); } catch (Exception ex) { Serilog.Log.Warning(ex, "[RemoteControl] takeover stop failed"); }
            // WPF: the session and engine go only on a panic or when the controller started this run (#878).
            var remoteRun = false;
            try { remoteRun = head?.SessionIsRemoteStarted == true; } catch { }
            if (force || remoteRun) { try { head?.Session("stop_session", null); } catch (Exception ex) { Serilog.Log.Warning(ex, "[RemoteControl] session stop failed"); } }
            if (force) CoreEngine.Stop();
            else
            {
                CoreFlash.Stop(); CoreSubliminal.Stop(); CoreBubbles.Stop(); CoreBouncingText.Stop();
                CoreEngine.Video?.Stop(); CoreEngine.BubbleCount?.Stop(); LockCardScheduler.Instance.Stop();
            }
            try { head?.CloseCards(); } catch (Exception ex) { Serilog.Log.Warning(ex, "[RemoteControl] card close failed"); }
            // WPF App.Wallpaper?.Deactivate() on both stop paths: the subject's own desktop comes back.
            try { head?.Wallpaper(false); } catch (Exception ex) { Serilog.Log.Warning(ex, "[RemoteControl] wallpaper restore failed"); }
            // As WPF, Lockdown or not: this only reduces restraint, and LockdownService.Deactivate restores
            // the pre-lockdown values when the timer ends.
            var s = CoreSettings.Current;
            // WPF "turn off overlays": pink and spiral off, Melt handed back if the controller switched it on.
            var overlays = s.PinkFilterEnabled || s.SpiralEnabled || _remoteBrainDrain;
            s.PinkFilterEnabled = false;
            s.SpiralEnabled = false;
            if (_remoteBrainDrain) s.BrainDrainEnabled = false;
            (_remotePink, _remoteSpiral, _remoteBrainDrain) = (false, false, false);
            DropOverlayHold();
            if (s.StrictLockEnabled || !s.PanicKeyEnabled)
            {
                s.StrictLockEnabled = false;
                s.PanicKeyEnabled = true;
                CoreSettings.Save();
                SyncPanicUi();
            }
            else if (overlays) CoreSettings.Save();
            // WPF, both stop paths: "restore window visibility" (RestoreFromTrayForRemote + ShowAvatarTube).
            try { head?.RestoreWindow(); } catch (Exception ex) { Serilog.Log.Warning(ex, "[RemoteControl] window restore failed"); }
        }

        private static void SyncPanicUi() { try { LockdownService.PanicKeyUiSync?.Invoke(); } catch { } }
    }
}
