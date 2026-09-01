using System.Collections.Generic;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// One spoken-mantra entry for the Takeover "say it for me" mechanic. She voices the whole
    /// <see cref="PromptText"/> (a flavoured delivery line that contains <see cref="Phrase"/>),
    /// the user repeats <see cref="Phrase"/> aloud, and she answers with the bespoke
    /// <see cref="Response"/>. Lives in a per-mod <c>mantras.json</c> alongside the bark audio so
    /// it themes per mod (Bambi / Sissy / Circe) and resolves through the same audio lookup.
    /// </summary>
    public sealed class MantraEntry
    {
        [JsonProperty("id")] public string Id { get; set; } = "";

        /// <summary>The recognition target — what the user must say. A substring of <see cref="PromptText"/>.</summary>
        [JsonProperty("phrase")] public string Phrase { get; set; } = "";

        /// <summary>The full line she speaks/shows ("Hey sweetie~ say this for me: good girls obey").</summary>
        [JsonProperty("promptText")] public string PromptText { get; set; } = "";

        /// <summary>Voiced clip of <see cref="PromptText"/> (filename, resolved per-mod). Null/empty ⇒ text-only.</summary>
        [JsonProperty("promptAudio")] public string? PromptAudio { get; set; }

        /// <summary>The bespoke success line she gives when the user nails the phrase.</summary>
        [JsonProperty("response")] public string Response { get; set; } = "";

        /// <summary>Voiced clip of <see cref="Response"/> (filename, resolved per-mod). Null/empty ⇒ text-only.</summary>
        [JsonProperty("responseAudio")] public string? ResponseAudio { get; set; }

        [JsonProperty("enabled")] public bool Enabled { get; set; } = true;
    }

    /// <summary>A shared reaction line (retry / timeout). Text plus an optional voiced clip.</summary>
    public sealed class MantraLine
    {
        [JsonProperty("text")] public string Text { get; set; } = "";
        [JsonProperty("audio")] public string? Audio { get; set; }
    }

    /// <summary>Top-level shape of a per-mod <c>mantras.json</c>.</summary>
    public sealed class MantraSet
    {
        [JsonProperty("mantras")] public List<MantraEntry> Mantras { get; set; } = new();

        /// <summary>Shared "louder / again" lines played on a miss or too-quiet attempt.</summary>
        [JsonProperty("retry")] public List<MantraLine> Retry { get; set; } = new();

        /// <summary>Shared "too shy?" lines played when no speech is heard at all.</summary>
        [JsonProperty("timeout")] public List<MantraLine> Timeout { get; set; } = new();
    }
}
