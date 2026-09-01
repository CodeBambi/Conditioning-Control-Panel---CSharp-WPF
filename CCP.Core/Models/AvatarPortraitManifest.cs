using System.Collections.Generic;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// POCO for a mod's <c>avatar_manifest.json</c> — the emotive-portrait avatar definition.
    /// One manifest drives every skin (the <see cref="Skins"/> list); each skin is the same
    /// emotion/pose filenames in a different folder. Per-line emotion lives in <see cref="Lines"/>
    /// (keyed by the bark's audio-filename stem). Deserialized by <see cref="Services.AvatarPortraitLoader"/>.
    /// Presence of this manifest for the active mod is what turns the portrait system on; mods
    /// without one keep the legacy 4-pose avatar behavior.
    /// </summary>
    public class AvatarPortraitManifest
    {
        [JsonProperty("character")] public string? Character { get; set; }
        [JsonProperty("version")] public string? Version { get; set; }

        /// <summary>Emotion shown by default before any line plays.</summary>
        [JsonProperty("defaultEmotion")] public string DefaultEmotion { get; set; } = "neutral";

        /// <summary>Emotion the avatar idles on / returns to after a bark dwell expires.</summary>
        [JsonProperty("idleEmotion")] public string IdleEmotion { get; set; } = "neutral";

        /// <summary>Selectable outfit skins (base → l1 → beach → fishnet for Sissy). Mapped to the avatar-set selector.</summary>
        [JsonProperty("skins")] public List<AvatarSkin> Skins { get; set; } = new();

        [JsonProperty("director")] public AvatarDirector Director { get; set; } = new();

        /// <summary>Ambient fx names (e.g. breathing, shimmer/mist) applied to every portrait.</summary>
        [JsonProperty("ambientFx")] public List<string> AmbientFx { get; set; } = new();

        [JsonProperty("fxRules")] public AvatarFxRules? FxRules { get; set; }

        /// <summary>Emotion name → its portrait bucket.</summary>
        [JsonProperty("emotions")] public Dictionary<string, AvatarEmotion> Emotions { get; set; } = new();

        /// <summary>Bark lineId (audio-filename stem) → { text, emotion }.</summary>
        [JsonProperty("lines")] public Dictionary<string, AvatarLine> Lines { get; set; } = new();
    }

    public class AvatarSkin
    {
        [JsonProperty("id")] public string Id { get; set; } = "";
        /// <summary>Folder of this skin's portraits, RELATIVE to the manifest's directory (e.g. "portraits/2_beach").</summary>
        [JsonProperty("dir")] public string Dir { get; set; } = "";
        [JsonProperty("title")] public string? Title { get; set; }
    }

    public class AvatarDirector
    {
        /// <summary>Crossfade length, in ~60fps frames (4 ≈ 150ms).</summary>
        [JsonProperty("crossfadeFrames")] public int CrossfadeFrames { get; set; } = 4;
        /// <summary>When true, idle rotates through the current emotion's poses.</summary>
        [JsonProperty("rotatePerEmotion")] public bool RotatePerEmotion { get; set; } = true;
        /// <summary>How long an event emotion lingers before returning to idle, in ms.</summary>
        [JsonProperty("idleReturnMs")] public int IdleReturnMs { get; set; } = 4000;
    }

    public class AvatarFxRules
    {
        [JsonProperty("blushEmotions")] public List<string> BlushEmotions { get; set; } = new();
        [JsonProperty("heartsEmotions")] public List<string> HeartsEmotions { get; set; } = new();
    }

    public class AvatarEmotion
    {
        [JsonProperty("portraits")] public List<AvatarPortrait> Portraits { get; set; } = new();
        [JsonProperty("fx")] public List<string> Fx { get; set; } = new();
    }

    public class AvatarPortrait
    {
        [JsonProperty("file")] public string File { get; set; } = "";
        [JsonProperty("register")] public string? Register { get; set; }
    }

    public class AvatarLine
    {
        [JsonProperty("text")] public string? Text { get; set; }
        [JsonProperty("emotion")] public string Emotion { get; set; } = "neutral";
    }
}
