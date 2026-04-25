using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ConditioningControlPanel.Models.CommandData;

namespace ConditioningControlPanel.Models
{
    public class AiCommandData
    {
        [JsonPropertyName("command")] public AICommandType Command { get; set; }
        [JsonPropertyName("data")] public IAiCommandData? Data { get; set; }

        public static AiCommandData? ParseCommand(string json)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            options.Converters.Add(new AiCommandConverter());

            try
            {
                return JsonSerializer.Deserialize<AiCommandData>(json, options);
            }
            catch (JsonException ex) when (ex.Message.Contains("Expected depth to be zero") || ex.Message.Contains("open JSON object"))
            {
                try
                {
                    var fixedJson = json.Trim();
                    int openBraces = fixedJson.Count(c => c == '{');
                    int closeBraces = fixedJson.Count(c => c == '}');
                    while (openBraces > closeBraces)
                    {
                        fixedJson += "}";
                        closeBraces++;
                    }
                    return JsonSerializer.Deserialize<AiCommandData>(fixedJson, options);
                }
                catch (Exception inner)
                {
                    App.Logger?.Warning(inner, "AiCommandData: brace-recovery parse failed for: {Json}", json);
                    return null;
                }
            }
            catch (Exception e)
            {
                App.Logger?.Warning(e, "AiCommandData: parse failed for: {Json}", json);
                return null;
            }
        }
    }

    public class AiCommandConverter : JsonConverter<AiCommandData>
    {
        public override AiCommandData? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var stringTypeStr = reader.GetString();
                if (Enum.TryParse<AICommandType>(stringTypeStr, true, out var stringCommandType))
                {
                    return new AiCommandData { Command = stringCommandType };
                }
                return new AiCommandData { Command = AICommandType.none };
            }

            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            if (!root.TryGetProperty("command", out var cmdProp))
                return null;

            var typeStr = cmdProp.GetString();
            if (!Enum.TryParse<AICommandType>(typeStr, true, out var commandType))
                commandType = AICommandType.none;

            IAiCommandData? data = null;

            if (root.TryGetProperty("data", out var dataProp))
            {
                data = commandType switch
                {
                    AICommandType.flash_image => dataProp.Deserialize<FlashImage>(options),
                    AICommandType.bubbles => dataProp.Deserialize<Bubbles>(options),
                    AICommandType.video => dataProp.Deserialize<Media>(options),
                    AICommandType.audio => dataProp.Deserialize<Media>(options),
                    AICommandType.getbacktome => dataProp.Deserialize<GetBackToMe>(options),
                    AICommandType.mantra_lockscreen => dataProp.Deserialize<MantraLockscreen>(options),
                    AICommandType.pink => dataProp.Deserialize<SpiralPinkFiler>(options),
                    AICommandType.spiral => dataProp.Deserialize<SpiralPinkFiler>(options),
                    AICommandType.subliminal => dataProp.Deserialize<Subliminal>(options),
                    AICommandType.bounce => dataProp.Deserialize<Bounce>(options),
                    AICommandType.haptic => dataProp.Deserialize<HapticCommandData>(options),
                    _ => null
                };
            }

            return new AiCommandData
            {
                Command = commandType,
                Data = data
            };
        }

        public override void Write(Utf8JsonWriter writer, AiCommandData value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("command", value.Command.ToString());
            if (value.Data != null)
            {
                writer.WritePropertyName("data");
                JsonSerializer.Serialize(writer, value.Data, value.Data.GetType(), options);
            }
            writer.WriteEndObject();
        }
    }

    public enum AICommandType
    {
        none,
        spiral,
        mantra_lockscreen,
        bubbles,
        video,
        audio,
        pink,
        flash_image,
        subliminal,
        getbacktome,
        bounce,
        haptic
    }
}
