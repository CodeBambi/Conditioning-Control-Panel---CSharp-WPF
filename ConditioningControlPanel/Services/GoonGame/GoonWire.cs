using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>
    /// The one serializer every Goon Game transport shares (Phase A).
    ///
    /// Wire format: Newtonsoft JSON, one compact object per message, snake_case member names
    /// coming straight off the <c>[JsonProperty]</c> attributes in GoonContracts.cs. Routing is
    /// by the <c>"t"</c> discriminator into the concrete GoonMessage subtype.
    ///
    /// PLATFORM-NEUTRAL BY MANDATE. Mobile (Expo/RN) and web clients are planned, so nothing here
    /// may lean on a .NET serialization behaviour a TypeScript client can't reproduce with plain
    /// JSON.parse/stringify. Concretely:
    ///  - <b>Enums are integers</b>, using the frozen codes in GoonContracts.cs. Never
    ///    TypeNameHandling, never $type, never a name a foreign client would have to map.
    ///  - <b>Every time is an integer count of milliseconds.</b> No ticks, no DateTime, no ISO
    ///    strings anywhere on the P2P wire.
    ///  - <b>64-bit seeds travel as decimal STRINGS</b> — see <see cref="UInt64AsStringConverter"/>.
    ///    This is the one place a bare JSON number would have quietly broken every JS client.
    ///  - <b>Nulls are dropped</b> on the way out. PayloadMsg carries a lot of optional fields and
    ///    the relay path pays per byte; a foreign client reads a missing member as its default.
    ///
    /// The wire is UNTRUSTED. Deserialize never throws: it returns null and logs. Oversize frames
    /// are rejected before parsing (<see cref="MaxWireBytes"/>) so a hostile peer can't make us
    /// allocate. Note that an integer enum from a NEWER peer deserializes to an out-of-range value
    /// rather than throwing — that is deliberate forward-compatibility, and it means the receiver
    /// side (Phase C) must treat an unrecognised kind as rejected_filtered rather than assuming the
    /// value is one of the ones it knows.
    ///
    /// Semantic validation (rate limits, text caps, capability filtering, banned kinds) is NOT done
    /// here — it belongs to the receiver-side executor, per the plan's safety rails.
    /// </summary>
    public static class GoonWire
    {
        /// <summary>
        /// Hard ceiling on one frame. Real messages are 100-400 bytes; the biggest legitimate one
        /// (a PayloadMsg with tags + a 200-char text) is well under 1 KB. 16 KB leaves absurd
        /// headroom while keeping a malicious peer from handing us a 100 MB string to parse.
        /// </summary>
        public const int MaxWireBytes = 16 * 1024;

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Include,
            Formatting = Formatting.None,
            // A malformed member must not poison the whole frame — we'd rather route a message
            // with one field at its default than drop a mercy or a result.
            Error = (_, e) => e.ErrorContext.Handled = true,
            Converters = { new UInt64AsStringConverter() },
        };

        /// <summary>"t" discriminator -> concrete type. Explicit, not reflected: the map is the spec.</summary>
        private static readonly Dictionary<string, Type> TypeMap = new(StringComparer.Ordinal)
        {
            ["clock_ping"] = typeof(ClockPingMsg),
            ["clock_pong"] = typeof(ClockPongMsg),
            ["hello"] = typeof(HelloMsg),
            ["consent"] = typeof(ConsentSheetMsg),
            ["draft"] = typeof(DraftMsg),
            ["match_start"] = typeof(MatchStartMsg),
            ["tick"] = typeof(StateTickMsg),
            ["payload"] = typeof(PayloadMsg),
            ["payload_receipt"] = typeof(PayloadReceiptMsg),
            ["round"] = typeof(RoundScheduleMsg),
            ["round_result"] = typeof(RoundResultMsg),
            ["mercy"] = typeof(MercyMsg),
            ["emote"] = typeof(EmoteMsg),
            ["result"] = typeof(ResultMsg),
        };

        public static string Serialize(GoonMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            return JsonConvert.SerializeObject(message, Settings);
        }

        /// <summary>
        /// Parse one inbound frame. Returns null for anything we can't route — oversize, empty,
        /// not an object, missing/unknown "t", or a body that doesn't fit its declared type.
        /// Callers drop nulls; they never disconnect over one, because a single bad frame from a
        /// mismatched build shouldn't end a match.
        /// </summary>
        public static GoonMessage? Deserialize(string? json, string logTag = "GoonWire")
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            if (Encoding.UTF8.GetByteCount(json) > MaxWireBytes)
            {
                App.Logger?.Warning("[{Tag}] Dropping oversize frame ({Chars} chars, cap {Cap} bytes)",
                    logTag, json.Length, MaxWireBytes);
                return null;
            }

            try
            {
                var obj = JObject.Parse(json);
                var t = obj["t"]?.Value<string>();
                if (string.IsNullOrEmpty(t))
                {
                    App.Logger?.Warning("[{Tag}] Frame has no \"t\" discriminator", logTag);
                    return null;
                }

                if (!TypeMap.TryGetValue(t!, out var clrType))
                {
                    // A newer peer sending a message kind we don't know about. Forward-compatible
                    // by design: ignore it and keep the channel alive.
                    App.Logger?.Information("[{Tag}] Ignoring unknown message type '{T}'", logTag, t);
                    return null;
                }

                var v = obj["v"]?.Value<int?>() ?? GoonConsts.ProtocolVersion;
                if (v > GoonConsts.ProtocolVersion)
                {
                    App.Logger?.Information("[{Tag}] Peer speaks protocol v{Peer} (we are v{Ours}) — parsing anyway",
                        logTag, v, GoonConsts.ProtocolVersion);
                }

                return obj.ToObject(clrType, JsonSerializer.Create(Settings)) as GoonMessage;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[{Tag}] Unparseable frame ({Chars} chars)", logTag, json.Length);
                return null;
            }
        }

        /// <summary>True if this type is one the clock owns and no one else should ever see.</summary>
        public static bool IsClockMessage(GoonMessage message) =>
            message is ClockPingMsg || message is ClockPongMsg;
    }

    /// <summary>
    /// Writes every <c>ulong</c> on the wire as a decimal STRING, and reads back either a string or
    /// a number.
    ///
    /// This exists for exactly one reason and it is not cosmetic. The only ulongs in the protocol
    /// are the round seed contributions, and a seed is the thing that guarantees both players see
    /// the same sudden-death round. JavaScript's Number is an IEEE double: any seed above 2^53
    /// loses its low bits the instant a browser or React Native client calls JSON.parse. That
    /// wouldn't throw — it would silently hand the web player a different bubble layout and a
    /// legitimate reason to dispute the result. Roughly 99.9% of random 64-bit seeds are above
    /// 2^53, so this is the normal case, not an edge case.
    ///
    /// A decimal string survives every client: .NET parses it here, TypeScript reads it with
    /// BigInt(s), Kotlin/Swift with their own 64-bit parsers. Reading numbers too keeps us
    /// compatible with any peer that emits the naive encoding.
    /// </summary>
    internal sealed class UInt64AsStringConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) =>
            objectType == typeof(ulong) || objectType == typeof(ulong?);

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value == null) { writer.WriteNull(); return; }
            writer.WriteValue(((ulong)value).ToString(CultureInfo.InvariantCulture));
        }

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            var nullable = objectType == typeof(ulong?);

            switch (reader.TokenType)
            {
                case JsonToken.Null:
                    return nullable ? null : (object)0UL;

                case JsonToken.String:
                    return ulong.TryParse((string?)reader.Value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var parsed) ? parsed : 0UL;

                case JsonToken.Integer:
                    // Anything above long.MaxValue arrives as a BigInteger, which Convert can't take.
                    if (reader.Value is BigInteger big)
                        return big >= 0 && big <= ulong.MaxValue ? (ulong)big : 0UL;
                    return Convert.ToUInt64(reader.Value, CultureInfo.InvariantCulture);

                case JsonToken.Float:
                    // A JS client that emitted a bare number and lost precision. We can't recover
                    // the true seed, but we can avoid throwing; the round will be flagged by the
                    // mismatched results rather than by a crash.
                    var d = Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture);
                    return d >= 0 && d <= ulong.MaxValue ? (ulong)d : 0UL;

                default:
                    return 0UL;
            }
        }
    }
}
