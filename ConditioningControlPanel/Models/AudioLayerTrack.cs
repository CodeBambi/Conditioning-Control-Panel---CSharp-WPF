using System.IO;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// A single user-supplied audio track in the layered-audio mixer
    /// (see <see cref="Services.Audio.LayeredAudioService"/>). Each enabled track loops
    /// seamlessly and is mixed with every other enabled track through ONE output device.
    /// Persisted as part of <see cref="AppSettings.AudioLayers"/>.
    /// </summary>
    public class AudioLayerTrack
    {
        /// <summary>Absolute path to the audio file (mp3/wav/etc.).</summary>
        public string Path { get; set; } = "";

        /// <summary>Per-track volume in percent (0-100).</summary>
        public int Volume { get; set; } = 70;

        /// <summary>Whether this track is included when the layered player runs.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Friendly display name derived from the file path (not persisted meaningfully).</summary>
        public string DisplayName =>
            string.IsNullOrEmpty(Path) ? "(no file)" : System.IO.Path.GetFileNameWithoutExtension(Path);
    }
}
