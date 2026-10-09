using System;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// The DtRH page's <c>fire-payload</c> message -> a native effect: the head twin of WPF
    /// <c>DtrhHostService.FirePayload</c> (Services/Chaos/DtrhHostService.cs:572). Since the
    /// 2026-07 cutover the browser game draws every VISUAL effect in-world; only two kinds still
    /// cross the bridge:
    ///   - <c>video</c>: a mandatory video (WPF VideoPayload, silent when the library is empty),
    ///   - <c>audio</c>: one subliminal whisper (WPF AudioPayload = FlashSubliminal).
    /// Any other kind is a page/host version mismatch: logged and ignored, as WPF does.
    ///
    /// <para>The DtRH host calls <see cref="Fire(string)"/> from its web-message switch; a true
    /// <c>audio</c> answer is what WPF counts as "a subliminal heard" for the run.</para>
    ///
    /// <para>ponytail: WPF arms a random 15 s slice of the clip for a run's video
    /// (VideoService.ArmRandomSegment(VideoPayload.SEGMENT_SEC)); MandatoryVideoScheduler has no
    /// segment seam yet, so the whole clip plays.</para>
    /// </summary>
    internal static class DtrhPayloadBridge
    {
        // Seams for tests; the defaults are the real services.
        internal static Func<bool> HasVideos = () => MandatoryVideoScheduler.LocalLibrary().Count > 0
            || (ContentPackStore.Current?.GetAllActivePackVideos().Count ?? 0) > 0;   // WPF: pack clips count
        internal static Func<bool> TriggerVideo = () => MandatoryVideoOverlay.Instance.Scheduler.Trigger();
        internal static Func<bool> Whisper = () =>
        {
            var phrase = CoreSubliminal.PickPhrase();
            var show = CoreSubliminal.ShowProvider;
            if (phrase == null || show == null) return false;
            show(phrase);
            return true;
        };

        /// <summary>The raw message body: <c>{kind, strength?, durationMult?}</c>. Returns the kind
        /// that fired ("video" / "audio"), or null when nothing did.</summary>
        public static string? Fire(string json)
        {
            try
            {
                var o = JObject.Parse(json);
                var kind = (string?)o["kind"];
                return Fire(kind, (int?)o["strength"], (double?)o["durationMult"]) ? kind!.ToLowerInvariant() : null;
            }
            catch (Exception ex)
            {
                Log.Warning("DtrhHost.FirePayload: {E}", ex.Message);
                return null;
            }
        }

        /// <summary>WPF FirePayload's switch. Strength (0..100, default 60) and durationMult
        /// (0.1..10, default 1) are clamped as WPF does; neither native kind reads them.</summary>
        public static bool Fire(string? kind, int? strength = null, double? durationMult = null)
        {
            if (string.IsNullOrWhiteSpace(kind)) return false;
            var s = Math.Clamp(strength ?? 60, 0, 100);
            _ = Math.Clamp(durationMult ?? 1.0, 0.1, 10.0);
            try
            {
                bool fired;
                if (string.Equals(kind, "video", StringComparison.OrdinalIgnoreCase))
                {
                    // WPF TriggerVideo(silentIfEmpty: true): an empty library is no dialog mid-run.
                    if (!HasVideos())
                    {
                        Log.Information("DtrhHost: video payload with an empty library - nothing to fire");
                        return false;
                    }
                    fired = TriggerVideo();
                }
                else if (string.Equals(kind, "audio", StringComparison.OrdinalIgnoreCase))
                    fired = Whisper();
                else
                {
                    Log.Warning("DtrhHost: payload kind '{K}' is in-world since the cutover - ignored", kind);
                    return false;
                }
                if (fired) Log.Information("DtrhHost: fired native payload {K} (strength {S})", kind, s);
                return fired;
            }
            catch (Exception ex)
            {
                Log.Warning("DtrhHost.FirePayload: {E}", ex.Message);
                return false;
            }
        }
    }
}
