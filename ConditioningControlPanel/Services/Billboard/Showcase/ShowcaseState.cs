using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace ConditioningControlPanel.Services.Billboard.Showcase
{
    /// <summary>
    /// The showcase's own small state, kept beside its cache (machine-local on purpose: the
    /// order is per install, so it never rides the cloud profile).
    /// </summary>
    public sealed class ShowcaseState
    {
        /// <summary>Seeds this install's clip order. Made once, never changed.</summary>
        public int Seed { get; set; }

        /// <summary>The last clip a card actually played. The next cycle starts after it.</summary>
        public string? LastShown { get; set; }

        /// <summary>When the manifest last came down from the network.</summary>
        public DateTime? ManifestFetchedUtc { get; set; }

        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

        public static ShowcaseState Load(string path)
        {
            ShowcaseState? state = null;
            try
            {
                if (File.Exists(path)) state = JsonSerializer.Deserialize<ShowcaseState>(File.ReadAllText(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }

            state ??= new ShowcaseState();
            if (state.Seed == 0)
            {
                state.Seed = RandomNumberGenerator.GetInt32(1, int.MaxValue);
                state.Save(path);
            }
            return state;
        }

        public void Save(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var part = path + ".part";
                File.WriteAllText(part, JsonSerializer.Serialize(this, Json));
                File.Move(part, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
