using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Remote
{
    /// <summary>One thing that just happened on the subject's screen, for the controller's live preview.</summary>
    public readonly record struct RemoteScreenEvent(string Kind, long AtUnixMs, string? Src = null, string? Text = null);

    /// <summary>
    /// The last few screen events. Newest last, at most <see cref="MaxEvents"/>, only the last
    /// <see cref="WindowMs"/>. Thread-safe: the effect services raise their events from wherever
    /// they run.
    /// </summary>
    public sealed class RemoteEventRing
    {
        public const int MaxEvents = 12;
        public const long WindowMs = 20_000;

        private readonly object _gate = new();
        private readonly List<RemoteScreenEvent> _events = new();

        public void Add(RemoteScreenEvent e)
        {
            lock (_gate)
            {
                _events.Add(e);
                if (_events.Count > MaxEvents) _events.RemoveRange(0, _events.Count - MaxEvents);
            }
        }

        public IReadOnlyList<RemoteScreenEvent> Recent(long nowUnixMs)
        {
            lock (_gate)
            {
                _events.RemoveAll(e => nowUnixMs - e.AtUnixMs > WindowMs);
                return _events.ToArray();
            }
        }

        public void Clear()
        {
            lock (_gate) _events.Clear();
        }
    }

    /// <summary>Everything the preview is built from, read off the app by the service. Plain data.</summary>
    public sealed class RemoteScreenInputs
    {
        public bool Spiral, Pink, Flash, Subliminal, Bubbles, Bounce, BrainDrain, MindWipe, Duck, Autonomy, LockCards;
        /// <summary>The app's own 0..50 opacity settings.</summary>
        public int SpiralOpacity, PinkOpacity;
        /// <summary>"local" or "web" while a video plays, else null.</summary>
        public string? VideoKind;
        public long VideoElapsedMs;
        public long? VideoDurationMs;
        public string? LockText;
        public int LockPos, LockTypos, LockDone;
        public bool CountActive;
        public int? CountN, CountAnswer;
        public bool? CountRight;
        public int HapticLevel;
        public string? HapticPattern;
        public bool HapticLoop, HapticDevice;
        public double Easy = 1.0;
        /// <summary>Scrolller niche names. Sent only when the subject shares them.</summary>
        public IReadOnlyList<string> OnlineNames = Array.Empty<string>();
        public bool ShareOnlineNames;
        public int Pictures, Videos;
        public int IdleSeconds;
        public IReadOnlyList<RemoteScreenEvent> Events = Array.Empty<RemoteScreenEvent>();
    }

    /// <summary>
    /// Builds the <c>screen</c> object of the v2 status push (wire contract in the v2 brief,
    /// section 4). Pure: no App, no clock. Privacy: carries no file name, path, URL or picture,
    /// and Scrolller niche names only when the subject shares them.
    /// </summary>
    public static class RemoteScreenState
    {
        /// <summary>The server drops a <c>screen</c> over 6 KB; stay well under it.</summary>
        public const int MaxBytes = 5800;
        public const int MaxLockText = 160, MaxNames = 12, MaxNameLength = 40, MaxEventText = 60;

        /// <summary>What this desktop understands, sent as <c>caps</c>.</summary>
        public static readonly string[] Caps = { "haptic_pattern", "haptic_level", "signal", "screen", "brain_drain" };

        public static JObject Build(RemoteScreenInputs i)
        {
            var on = new JObject
            {
                ["spiral"] = i.Spiral, ["pink"] = i.Pink, ["flash"] = i.Flash, ["subliminal"] = i.Subliminal,
                ["bubbles"] = i.Bubbles, ["bounce"] = i.Bounce, ["brain_drain"] = i.BrainDrain,
                ["mind_wipe"] = i.MindWipe, ["duck"] = i.Duck, ["autonomy"] = i.Autonomy, ["lock_cards"] = i.LockCards,
            };

            JToken video = JValue.CreateNull();
            if (i.VideoKind is "local" or "web")
                video = new JObject
                {
                    ["kind"] = i.VideoKind,
                    ["elapsed_ms"] = Math.Max(0, i.VideoElapsedMs),
                    ["dur_ms"] = i.VideoDurationMs is > 0 ? i.VideoDurationMs : null,
                };

            JToken lockCard = JValue.CreateNull();
            if (i.LockText != null)
            {
                var text = Cut(i.LockText, MaxLockText);
                lockCard = new JObject
                {
                    ["text"] = text,
                    ["pos"] = Math.Clamp(i.LockPos, 0, text.Length),
                    ["typos"] = Math.Max(0, i.LockTypos),
                    ["done"] = Math.Max(0, i.LockDone),
                };
            }

            JToken count = JValue.CreateNull();
            if (i.CountActive || i.CountRight != null)
                count = new JObject { ["n"] = i.CountN, ["answer"] = i.CountAnswer, ["right"] = i.CountRight };

            var names = i.ShareOnlineNames
                ? i.OnlineNames.Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => Cut(n.Trim(), MaxNameLength)).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxNames).ToArray()
                : Array.Empty<string>();

            var ev = new JArray();
            foreach (var e in i.Events.Skip(Math.Max(0, i.Events.Count - RemoteEventRing.MaxEvents)))
            {
                var o = new JObject { ["k"] = e.Kind };
                if (e.Kind == "flash") o["src"] = e.Src == "online" ? "online" : "local";
                if (e.Kind == "word") o["text"] = e.Text == null ? null : Cut(e.Text, MaxEventText);
                o["at"] = e.AtUnixMs;
                ev.Add(o);
            }

            var screen = new JObject
            {
                ["v"] = 2,
                ["on"] = on,
                ["str"] = new JObject { ["spiral"] = Strength(i.SpiralOpacity), ["pink"] = Strength(i.PinkOpacity) },
                ["video"] = video,
                ["lock"] = lockCard,
                ["count"] = count,
                ["haptic"] = new JObject
                {
                    ["level"] = Math.Clamp(i.HapticLevel, 0, 100),
                    ["pattern"] = i.HapticPattern,
                    ["loop"] = i.HapticLoop,
                    ["device"] = i.HapticDevice,
                },
                ["easy"] = Math.Round(Math.Clamp(i.Easy, 0, 1), 3),
                ["media"] = new JObject
                {
                    ["online"] = new JArray(names),
                    ["online_count"] = i.OnlineNames.Count,
                    ["pictures"] = Math.Max(0, i.Pictures),
                    ["videos"] = Math.Max(0, i.Videos),
                },
                ["attn"] = new JObject { ["idle_s"] = Math.Max(0, i.IdleSeconds) },
                ["ev"] = ev,
            };

            // Belt and braces: the only unbounded inputs are capped above, but never ship a
            // screen the server would throw away. Shed the optional words first.
            if (Size(screen) > MaxBytes) ((JObject)screen["media"]!)["online"] = new JArray();
            if (Size(screen) > MaxBytes) foreach (var o in ev.OfType<JObject>()) if (o["text"] != null) o["text"] = null;
            if (Size(screen) > MaxBytes && screen["lock"] is JObject l) l["text"] = "";
            return screen;
        }

        /// <summary>
        /// The parts of a screen that mean "something changed", for the push throttle. Leaves out
        /// what moves on its own every second (idle seconds, video position, a pattern's level);
        /// keeps whether the subject is watching (the page's 20 s line).
        /// </summary>
        public static string ChangeKey(JObject screen)
        {
            var copy = (JObject)screen.DeepClone();
            if (copy["attn"] is JObject a) a["idle_s"] = (a["idle_s"]?.Value<int>() ?? 0) < 20 ? 0 : 1;
            if (copy["video"] is JObject v) v.Remove("elapsed_ms");
            if (copy["haptic"] is JObject h) h.Remove("level");
            return copy.ToString(Formatting.None);
        }

        public static int Size(JObject screen) => Encoding.UTF8.GetByteCount(screen.ToString(Formatting.None));

        /// <summary>The app's 0..50 opacity as the wire's 0..100 strength.</summary>
        public static int Strength(int opacity) => Math.Clamp(opacity * 2, 0, 100);

        private static string Cut(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
    }

    /// <summary>The controller's typed name: trimmed, letters/digits/space/<c>._-</c> only,
    /// 1..24 characters, else none (the HUD then says "Your controller").</summary>
    public static class RemoteControllerName
    {
        public const int MaxLength = 24;

        public static string? Sanitize(string? raw)
        {
            if (raw == null) return null;
            var t = raw.Trim();
            if (t.Length == 0 || t.Length > MaxLength) return null;
            foreach (var c in t)
                if (!(char.IsLetterOrDigit(c) || c == ' ' || c == '.' || c == '_' || c == '-')) return null;
            return t;
        }
    }
}
