using System;
using System.IO;

namespace ConditioningControlPanel.Services
{
    /// <summary>The NAudio half of <see cref="MantraVoiceService"/> (which moved to CCP.Core):
    /// an extension, so <c>App.MantraVoice?.GetAudioDuration(path)</c> reads as before.</summary>
    public static class MantraVoiceAudio
    {
        /// <summary>
        /// Best-effort duration of an mp3/wav clip so callers can wait for her to finish speaking
        /// before opening the mic (otherwise the recognizer hears her own delivery). Null if unknown.
        /// </summary>
        public static TimeSpan? GetAudioDuration(this MantraVoiceService _, string? fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath)) return null;
            try
            {
                var ext = Path.GetExtension(fullPath).ToLowerInvariant();
                if (ext == ".wav")
                {
                    using var r = new NAudio.Wave.WaveFileReader(fullPath);
                    return r.TotalTime;
                }
                using var mp3 = new NAudio.Wave.Mp3FileReader(fullPath);
                return mp3.TotalTime;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug(ex, "MantraVoiceService: could not read duration of {Path}", fullPath);
                return null;
            }
        }
    }
}
