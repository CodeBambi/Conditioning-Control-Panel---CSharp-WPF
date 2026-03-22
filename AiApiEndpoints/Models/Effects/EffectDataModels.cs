using System.Text.Json.Serialization;

namespace AiApiEndpoints.Models.Effects;

public class VideoAudioData
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
    
    [JsonPropertyName("random")]
    public bool Random { get; set; } = false;
}

public class SubliminalData
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("opacity")]
    public int Opacity { get; set; }
}

public class FlashImageData
{
    [JsonPropertyName("amount")]
    public int Amount { get; set; }

    [JsonPropertyName("durarion")]
    public int Duration { get; set; }

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("opacity")]
    public int Opacity { get; set; }
}

public class MantraData
{
    [JsonPropertyName("mantra")]
    public string Mantra { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public int Amount { get; set; }
}

public class SpiralPinkData
{
    [JsonPropertyName("On")]
    public bool On { get; set; }

    [JsonPropertyName("intensity")]
    public int Intensity { get; set; }
}

public class BounceData
{
    [JsonPropertyName("Words")]
    public string[] Words { get; set; } = Array.Empty<string>();

    [JsonPropertyName("On")]
    public bool On { get; set; }
}

public class BubblesData
{
    [JsonPropertyName("On")]
    public bool On { get; set; }

    [JsonPropertyName("feq")]
    public int Frequency { get; set; }
}

public class HapticData
{
    [JsonPropertyName("Intensity")]
    public double Intensity { get; set; }

    [JsonPropertyName("Duration")]
    public int Duration { get; set; }
}
