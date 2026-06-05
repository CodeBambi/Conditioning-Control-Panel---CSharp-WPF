using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Bark
{
    /// <summary>
    /// One line in a bark rule's <c>variant_pool</c>. Carries the on-screen text plus an optional
    /// voiceline audio filename (resolved against the active mod's companion_audio folder at speak
    /// time). Deserializes from EITHER a bare JSON string (text-only) or an object
    /// <c>{ "text": "...", "audio": "file.mp3" }</c> — so simple/sample manifests and the generated
    /// voiced manifests both parse. <c>display</c>/<c>file</c> are accepted as aliases for the
    /// content-side manifest shape.
    /// </summary>
    [JsonConverter(typeof(BarkVariantJsonConverter))]
    public class BarkVariant
    {
        public string Text { get; set; } = "";
        public string? Audio { get; set; }

        public BarkVariant() { }
        public BarkVariant(string text, string? audio = null) { Text = text; Audio = audio; }

        public bool HasText => !string.IsNullOrWhiteSpace(Text);
    }

    /// <summary>Reads a <see cref="BarkVariant"/> from a bare string or a {text/display, audio/file} object.</summary>
    public class BarkVariantJsonConverter : JsonConverter<BarkVariant>
    {
        public override BarkVariant ReadJson(JsonReader reader, Type objectType, BarkVariant? existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            switch (reader.TokenType)
            {
                case JsonToken.String:
                    return new BarkVariant((string)reader.Value! );
                case JsonToken.StartObject:
                {
                    var o = JObject.Load(reader);
                    var text = (string?)(o["text"] ?? o["display"]) ?? "";
                    var audio = (string?)(o["audio"] ?? o["file"]);
                    return new BarkVariant(text, string.IsNullOrWhiteSpace(audio) ? null : audio);
                }
                default:
                    reader.Skip();
                    return new BarkVariant();
            }
        }

        public override void WriteJson(JsonWriter writer, BarkVariant? value, JsonSerializer serializer) =>
            throw new NotSupportedException("BarkVariant is read-only from manifests.");

        public override bool CanWrite => false;
    }
}
