using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ConditioningControlPanel.Services.Haptics.Core;

namespace ConditioningControlPanel.Services.Haptics
{
    /// <summary>
    /// Pure helpers for the Lovense LAN ("Game Mode") JSON protocol used by
    /// <see cref="LovenseProviderV2"/>:
    ///   * capability mapping (GetToys <c>shortFunctionNames</c> -> <see cref="ActuatorType"/>),
    ///   * THE single intensity(0..1) -> native-step quantizer (the legacy provider had two
    ///     disagreeing mappers - never add a second one),
    ///   * request-body builders for Function / Preset / Pattern (v1) / PatternV2.
    ///
    /// Payload shapes are taken verbatim from developer.lovense.com "Standard API"
    /// (see docs/HAPTICS_OVERHAUL_PLAN.md "Lovense LAN API cheat-sheet"). Nothing here
    /// touches the network or any shared state, so every member is safe to call from any thread.
    /// </summary>
    public static class LovensePatterns
    {
        /// <summary>Header shown inside Lovense Remote's "connected app" row - our branding surface.
        /// Must be sent on EVERY request.</summary>
        public const string PlatformHeaderName = "X-platform";
        public const string PlatformHeaderValue = "Conditioning Control Panel";

        /// <summary>Lovense caps a v1 pattern at 50 strength values.</summary>
        public const int MaxPatternStrengths = 50;

        /// <summary>Lovense requires the pattern step interval to be greater than 100 ms.</summary>
        public const int MinPatternIntervalMs = 101;

        /// <summary>PatternV2 keyframe timestamps may not exceed 2 hours.</summary>
        public const int MaxPatternV2TimestampMs = 7_200_000;

        /// <summary>Built-in Lovense Remote presets usable with <c>{"command":"Preset"}</c>.</summary>
        public static readonly IReadOnlyList<string> Presets =
            new[] { "pulse", "wave", "fireworks", "earthquake" };

        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

        // -------------------------------------------------------------------
        // Intensity mapping
        // -------------------------------------------------------------------

        /// <summary>
        /// THE intensity -> native-step mapper. Linear, with a small dead zone so a
        /// mixer floor of exactly 0 stops the motor while any audible request lands on at
        /// least step 1. Perceptual shaping belongs in the mixer, not here.
        /// </summary>
        public static int Quantize(double intensity, int steps)
        {
            if (steps <= 0) steps = 20;
            if (double.IsNaN(intensity) || intensity <= 0d) return 0;
            if (intensity < 0.02d) return 0;            // dead zone: imperceptible, treat as off
            if (intensity >= 1d) return steps;
            var v = (int)Math.Round(intensity * steps, MidpointRounding.AwayFromZero);
            return Math.Clamp(v, 1, steps);
        }

        /// <summary>Native resolution of each actuator kind on Lovense hardware.</summary>
        public static int StepsFor(ActuatorType type) => type switch
        {
            ActuatorType.Pump => 3,
            ActuatorType.Depth => 3,
            ActuatorType.Position => 100,
            ActuatorType.Stroke => 100,
            _ => 20
        };

        // -------------------------------------------------------------------
        // Capability mapping
        // -------------------------------------------------------------------

        /// <summary>
        /// Parses one entry of GetToys' <c>shortFunctionNames</c> (short codes: "v", "v1", "r",
        /// "pos") or <c>fullFunctionNames</c> ("Vibrate", "Vibrate1", "Thrusting"). A trailing
        /// digit is the 1-based motor number on multi-motor toys (Edge = v1/v2, Lapis = v1/v2/v3);
        /// <paramref name="verb"/> is the exact action word to put in a Function action string.
        /// </summary>
        public static bool TryParseFunctionName(string? raw, out ActuatorType type,
                                                out int motorNumber, out string verb)
        {
            type = ActuatorType.Vibrate;
            motorNumber = 0;
            verb = "";
            if (string.IsNullOrWhiteSpace(raw)) return false;

            var s = raw.Trim();

            // split a trailing motor number ("v2" -> "v" + 2, "Vibrate1" -> "Vibrate" + 1)
            var end = s.Length;
            while (end > 0 && char.IsDigit(s[end - 1])) end--;
            if (end < s.Length && end > 0 &&
                int.TryParse(s.AsSpan(end), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            {
                motorNumber = n;
                s = s.Substring(0, end);
            }

            string? baseVerb = s.ToLowerInvariant() switch
            {
                "v" or "vibrate" => "Vibrate",
                "r" or "rotate" => "Rotate",
                "p" or "pump" => "Pump",
                "t" or "thrusting" or "thrust" => "Thrusting",
                "f" or "fingering" or "finger" => "Fingering",
                "s" or "suction" or "suck" => "Suction",
                "o" or "oscillate" => "Oscillate",
                "d" or "depth" => "Depth",
                "pos" or "position" => "Position",
                "stroke" => "Stroke",
                "c" or "constrict" => "Constrict",
                _ => null
            };
            if (baseVerb == null) return false;

            type = baseVerb switch
            {
                "Vibrate" => ActuatorType.Vibrate,
                "Rotate" => ActuatorType.Rotate,
                "Pump" => ActuatorType.Pump,
                "Thrusting" => ActuatorType.Thrust,
                "Fingering" => ActuatorType.Finger,
                "Suction" => ActuatorType.Suction,
                "Oscillate" => ActuatorType.Oscillate,
                "Depth" => ActuatorType.Depth,
                "Position" => ActuatorType.Position,
                "Stroke" => ActuatorType.Stroke,
                _ => ActuatorType.Constrict
            };

            verb = motorNumber > 0
                ? baseVerb + motorNumber.ToString(CultureInfo.InvariantCulture)
                : baseVerb;
            return true;
        }

        /// <summary>Short feature letter used inside a v1 pattern rule (<c>F:v,r</c>).
        /// Returns null for kinds Lovense patterns cannot drive.</summary>
        public static string? PatternFeatureCode(ActuatorType type) => type switch
        {
            ActuatorType.Vibrate => "v",
            ActuatorType.Rotate => "r",
            ActuatorType.Pump => "p",
            ActuatorType.Thrust => "t",
            ActuatorType.Finger => "f",
            ActuatorType.Suction => "s",
            ActuatorType.Oscillate => "o",
            ActuatorType.Depth => "d",
            _ => null
        };

        /// <summary>
        /// Formats one <c>Verb:value</c> fragment of a Function action string, or null when the
        /// fragment must be omitted. Stroke is special: it is a RANGE (<c>Stroke:min-max</c>,
        /// span &gt;= 20) that shapes a Thrusting motion rather than a level, so a zero request
        /// simply drops it (Thrusting:0 is what actually stops the toy).
        /// </summary>
        public static string? FormatActionFragment(string verb, int step)
        {
            if (string.IsNullOrEmpty(verb)) return null;
            if (verb.StartsWith("Constrict", StringComparison.Ordinal)) return null; // not a LAN verb

            if (verb.StartsWith("Stroke", StringComparison.Ordinal))
            {
                if (step <= 0) return null;
                var max = Math.Clamp(step, 20, 100);   // span from 0 is always >= 20
                return verb + ":0-" + max.ToString(CultureInfo.InvariantCulture);
            }

            return verb + ":" + step.ToString(CultureInfo.InvariantCulture);
        }

        // -------------------------------------------------------------------
        // Request bodies
        // -------------------------------------------------------------------

        internal static string Serialize(Dictionary<string, object?> payload)
            => JsonSerializer.Serialize(payload, JsonOpts);

        public static string BuildGetToysPayload()
            => Serialize(new Dictionary<string, object?> { ["command"] = "GetToys" });

        /// <summary>
        /// <c>{"command":"Function","action":"Vibrate:5,Rotate:10","timeSec":0,"toy":"id",
        /// "apiVer":1,"stopPrevious":1}</c>. <paramref name="toyId"/> null = broadcast to all toys.
        /// timeSec 0 runs until stopped - the caller owns the stop.
        /// </summary>
        public static string BuildFunctionPayload(string? toyId, string action, int timeSec = 0,
                                                  bool stopPrevious = true)
        {
            var p = new Dictionary<string, object?>
            {
                ["command"] = "Function",
                ["action"] = action,
                ["timeSec"] = Math.Max(0, timeSec),
                ["apiVer"] = 1,
                ["stopPrevious"] = stopPrevious ? 1 : 0
            };
            if (!string.IsNullOrEmpty(toyId)) p["toy"] = toyId;
            return Serialize(p);
        }

        /// <summary>All-stop for one toy (or every toy when <paramref name="toyId"/> is null).</summary>
        public static string BuildStopPayload(string? toyId)
        {
            var p = new Dictionary<string, object?>
            {
                ["command"] = "Function",
                ["action"] = "Stop",
                ["timeSec"] = 0,
                ["apiVer"] = 1
            };
            if (!string.IsNullOrEmpty(toyId)) p["toy"] = toyId;
            return Serialize(p);
        }

        /// <summary><c>{"command":"Preset","name":"pulse","timeSec":9,"toy":"id","apiVer":1}</c>.</summary>
        public static string BuildPresetPayload(string? toyId, string preset, int timeSec)
        {
            var p = new Dictionary<string, object?>
            {
                ["command"] = "Preset",
                ["name"] = (preset ?? "pulse").Trim().ToLowerInvariant(),
                ["timeSec"] = Math.Max(0, timeSec),
                ["apiVer"] = 1
            };
            if (!string.IsNullOrEmpty(toyId)) p["toy"] = toyId;
            return Serialize(p);
        }

        /// <summary>
        /// v1 pattern: <c>{"command":"Pattern","rule":"V:1;F:v,r;S:1000#","strength":"20;15;10",
        /// "timeSec":9,"toy":"id","apiVer":2}</c>. Strengths are 0..1 and quantized through the
        /// single mapper; the list is truncated to 50 and the interval floored at 101 ms.
        /// </summary>
        public static string BuildPatternV1Payload(string? toyId, IEnumerable<string> featureCodes,
                                                   IEnumerable<double> strengths, int intervalMs, int timeSec)
        {
            var codes = new List<string>();
            foreach (var c in featureCodes)
                if (!string.IsNullOrWhiteSpace(c) && !codes.Contains(c)) codes.Add(c);
            if (codes.Count == 0) codes.Add("v");

            var sb = new StringBuilder();
            var count = 0;
            foreach (var s in strengths)
            {
                if (count >= MaxPatternStrengths) break;
                if (count > 0) sb.Append(';');
                sb.Append(Quantize(s, 20).ToString(CultureInfo.InvariantCulture));
                count++;
            }
            if (count == 0) sb.Append('0');

            var interval = Math.Max(MinPatternIntervalMs, intervalMs);
            var rule = "V:1;F:" + string.Join(",", codes) + ";S:" +
                       interval.ToString(CultureInfo.InvariantCulture) + "#";

            var p = new Dictionary<string, object?>
            {
                ["command"] = "Pattern",
                ["rule"] = rule,
                ["strength"] = sb.ToString(),
                ["timeSec"] = Math.Max(0, timeSec),
                ["apiVer"] = 2
            };
            if (!string.IsNullOrEmpty(toyId)) p["toy"] = toyId;
            return Serialize(p);
        }

        /// <summary>Clamps + orders keyframes for PatternV2 (ts 0..7,200,000 ms; pos 0..100).</summary>
        public static List<Dictionary<string, int>> BuildPatternV2Actions(
            IEnumerable<(int TimestampMs, int Position)> points)
        {
            var list = new List<Dictionary<string, int>>();
            foreach (var (ts, pos) in points)
            {
                list.Add(new Dictionary<string, int>
                {
                    ["ts"] = Math.Clamp(ts, 0, MaxPatternV2TimestampMs),
                    ["pos"] = Math.Clamp(pos, 0, 100)
                });
            }
            list.Sort((a, b) => a["ts"].CompareTo(b["ts"]));
            return list;
        }

        /// <summary><c>{"command":"PatternV2","type":"Setup","actions":[{"ts":0,"pos":10}],"apiVer":1}</c></summary>
        public static string BuildPatternV2SetupPayload(IEnumerable<(int TimestampMs, int Position)> points)
            => Serialize(new Dictionary<string, object?>
            {
                ["command"] = "PatternV2",
                ["type"] = "Setup",
                ["actions"] = BuildPatternV2Actions(points),
                ["apiVer"] = 1
            });

        /// <summary><c>{"command":"PatternV2","type":"Play","toy":"id","startTime":100,"offsetTime":300,"apiVer":1}</c></summary>
        public static string BuildPatternV2PlayPayload(string? toyId, int startTimeMs, int offsetTimeMs)
        {
            var p = new Dictionary<string, object?>
            {
                ["command"] = "PatternV2",
                ["type"] = "Play",
                ["startTime"] = Math.Clamp(startTimeMs, 0, MaxPatternV2TimestampMs),
                ["offsetTime"] = Math.Max(0, offsetTimeMs),
                ["apiVer"] = 1
            };
            if (!string.IsNullOrEmpty(toyId)) p["toy"] = toyId;
            return Serialize(p);
        }

        /// <summary><c>{"command":"PatternV2","type":"InitPlay","actions":[...],"stopPrevious":0,"apiVer":1}</c></summary>
        public static string BuildPatternV2InitPlayPayload(
            IEnumerable<(int TimestampMs, int Position)> points, bool stopPrevious)
            => Serialize(new Dictionary<string, object?>
            {
                ["command"] = "PatternV2",
                ["type"] = "InitPlay",
                ["actions"] = BuildPatternV2Actions(points),
                ["stopPrevious"] = stopPrevious ? 1 : 0,
                ["apiVer"] = 1
            });

        /// <summary><c>{"command":"PatternV2","type":"Stop","toy":"id","apiVer":1}</c></summary>
        public static string BuildPatternV2StopPayload(string? toyId)
        {
            var p = new Dictionary<string, object?>
            {
                ["command"] = "PatternV2",
                ["type"] = "Stop",
                ["apiVer"] = 1
            };
            if (!string.IsNullOrEmpty(toyId)) p["toy"] = toyId;
            return Serialize(p);
        }

        /// <summary><c>{"command":"PatternV2","type":"SyncTime","apiVer":1}</c></summary>
        public static string BuildPatternV2SyncTimePayload()
            => Serialize(new Dictionary<string, object?>
            {
                ["command"] = "PatternV2",
                ["type"] = "SyncTime",
                ["apiVer"] = 1
            });
    }
}
