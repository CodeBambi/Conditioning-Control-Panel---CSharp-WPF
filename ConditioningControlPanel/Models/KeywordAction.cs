using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Base class for an individual action that can be attached to a <see cref="KeywordTrigger"/>.
    /// A trigger's <see cref="KeywordTrigger.Actions"/> list is iterated on dispatch, so a single
    /// trigger can fire any combination of audio / visual / haptic / xp / avatar comment / etc.
    ///
    /// Subclasses use the string <see cref="Type"/> field as a JSON discriminator — the
    /// <see cref="KeywordActionConverter"/> registered on this class uses it to deserialize
    /// the correct concrete type.
    /// </summary>
    [JsonConverter(typeof(KeywordActionConverter))]
    public abstract class KeywordAction
    {
        /// <summary>Short unique id for this action (for UI selection, logging).</summary>
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

        /// <summary>Per-action enable toggle. Disabled actions are skipped on dispatch.</summary>
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>JSON discriminator, set in each subclass constructor.</summary>
        [JsonProperty("type")]
        public string Type { get; protected set; } = "";
    }

    // ---- Audio -----------------------------------------------------------------

    public class PlayAudioAction : KeywordAction
    {
        public PlayAudioAction() { Type = "PlayAudio"; }

        [JsonProperty("filePath")]
        public string? FilePath { get; set; }

        [JsonProperty("volume")]
        public int Volume { get; set; } = 80;

        [JsonProperty("playCount")]
        public int PlayCount { get; set; } = 1;

        [JsonProperty("delayBetweenMs")]
        public int DelayBetweenMs { get; set; } = 200;

        [JsonProperty("duckSystemAudio")]
        public bool DuckSystemAudio { get; set; } = true;
    }

    // ---- Visual ----------------------------------------------------------------

    public class VisualEffectAction : KeywordAction
    {
        public VisualEffectAction() { Type = "VisualEffect"; }

        [JsonProperty("effect")]
        [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public KeywordVisualEffect Effect { get; set; } = KeywordVisualEffect.SubliminalFlash;
    }

    /// <summary>
    /// Shows the keyword highlight overlay over matched OCR words.
    /// Respects the global <c>KeywordHighlightEnabled</c> setting at dispatch time.
    /// </summary>
    public class HighlightAction : KeywordAction
    {
        public HighlightAction() { Type = "Highlight"; }
    }

    // ---- Haptic ----------------------------------------------------------------

    public class HapticAction : KeywordAction
    {
        public HapticAction() { Type = "Haptic"; }

        [JsonProperty("intensity")]
        public double Intensity { get; set; } = 0.5;
    }

    // ---- XP --------------------------------------------------------------------

    public class AddXpAction : KeywordAction
    {
        public AddXpAction() { Type = "AddXp"; }

        [JsonProperty("amount")]
        public int Amount { get; set; } = 10;
    }

    // ---- Avatar ----------------------------------------------------------------

    /// <summary>
    /// Fires a companion/avatar line in reaction to the trigger. Goes through the
    /// AI pipeline if available; otherwise can fall back to a canned phrase pool.
    /// </summary>
    public class AvatarCommentAction : KeywordAction
    {
        public AvatarCommentAction() { Type = "AvatarComment"; }

        /// <summary>
        /// Optional user-prompt template for the AI request. <c>{keyword}</c> is
        /// substituted with the matched keyword. Null/empty uses a default prompt.
        /// </summary>
        [JsonProperty("promptTemplate")]
        public string? PromptTemplate { get; set; }

        /// <summary>
        /// Optional phrase pool category to pull from when AI is unavailable
        /// (e.g. "PuppyPraise"). Null means no fallback.
        /// </summary>
        [JsonProperty("fallbackPhraseCategory")]
        public string? FallbackPhraseCategory { get; set; }

        /// <summary>
        /// When true (default), the AI roundtrip is attempted first. When false,
        /// the action always uses the fallback category (pure canned lines).
        /// </summary>
        [JsonProperty("requireAiAvailable")]
        public bool RequireAiAvailable { get; set; } = true;
    }

    // ---- Stubs (declared now, wired later) -------------------------------------

    /// <summary>
    /// STUB: Declared so preset JSONs can reference it. Session extension API
    /// doesn't exist on SessionEngine yet — dispatch logs and no-ops.
    /// </summary>
    public class ExtendSessionAction : KeywordAction
    {
        public ExtendSessionAction() { Type = "ExtendSession"; }

        [JsonProperty("minutes")]
        public int Minutes { get; set; } = 5;
    }

    /// <summary>
    /// STUB: Declared so preset JSONs can reference it. Chaster integration is
    /// pending (see chaster_integration_plan.md) — dispatch logs and no-ops.
    /// </summary>
    public class ChasterAddTimeAction : KeywordAction
    {
        public ChasterAddTimeAction() { Type = "ChasterAddTime"; }

        [JsonProperty("minutes")]
        public int Minutes { get; set; } = 5;
    }

    // ---- Polymorphic JSON converter -------------------------------------------

    /// <summary>
    /// Reads the <c>type</c> discriminator off a JSON object and deserializes into
    /// the correct <see cref="KeywordAction"/> subclass. Writes as normal.
    /// </summary>
    public class KeywordActionConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => typeof(KeywordAction).IsAssignableFrom(objectType);

        // We only customize read. Write uses the default serialization path.
        public override bool CanWrite => false;

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;

            var obj = JObject.Load(reader);
            var typeName = obj["type"]?.ToString();

            KeywordAction action = typeName switch
            {
                "PlayAudio"      => new PlayAudioAction(),
                "VisualEffect"   => new VisualEffectAction(),
                "Highlight"      => new HighlightAction(),
                "Haptic"         => new HapticAction(),
                "AddXp"          => new AddXpAction(),
                "AvatarComment"  => new AvatarCommentAction(),
                "ExtendSession"  => new ExtendSessionAction(),
                "ChasterAddTime" => new ChasterAddTimeAction(),
                _                => null!
            };

            if (action == null)
            {
                // Unknown action type — skip gracefully. Log and return null so the
                // containing list Where-filter can drop it.
                App.Logger?.Warning("KeywordActionConverter: Unknown action type '{Type}'", typeName);
                return null;
            }

            serializer.Populate(obj.CreateReader(), action);
            return action;
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            // Not used — CanWrite == false causes the default writer to be used.
            throw new NotImplementedException();
        }
    }
}
