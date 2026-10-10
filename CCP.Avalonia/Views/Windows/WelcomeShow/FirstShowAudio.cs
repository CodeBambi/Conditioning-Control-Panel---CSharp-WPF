using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Services.FirstShow;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Services/FirstShow/FirstShowAudio.cs: EMI's bleepese and the
    /// short show cues. The bleepese is Core <see cref="EmiVox"/> (a rendered blip burst per line, never
    /// speech); the cues are the recorded files under Resources/sounds named by
    /// <see cref="FirstShowScript.CueAsset"/>. Master volume 0 is silence, as on WPF.
    /// ponytail: WPF resolved cues through ModResourceResolver (a mod can re-voice them) and also went
    /// silent while AudioService.IsOutputSuppressed; neither seam exists on this head (see EmiSfx).
    /// </summary>
    internal sealed class FirstShowAudio : IDisposable
    {
        private readonly string _cache = Path.Combine(Path.GetTempPath(), "ccp-first-show-" + Guid.NewGuid().ToString("N"));
        private Action? _stopVoice, _stopCue;
        private bool _disposed;

        /// <summary>Test seam: (path, volume, tag) -> stop.</summary>
        internal Func<string, float, string, Action> Play = (path, volume, tag) => CoreAudio.PlayStoppable(path, volume, tag);

        /// <summary>Test seam: master volume 0..100.</summary>
        internal Func<int> Master = () => CoreSettings.Current?.MasterVolume ?? 0;

        private double Volume => Math.Clamp(Master() / 100.0, 0, 1);

        public void Speak(string text, string mood = "idle")
        {
            if (_disposed || Volume <= 0) return;
            try
            {
                Directory.CreateDirectory(_cache);
                var path = Path.Combine(_cache, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text + mood))) + ".wav");
                if (!File.Exists(path)) File.WriteAllBytes(path, EmiVox.WriteWav(EmiVox.RenderBurst(EmiVox.MakeScore(text, mood))));
                Stop(ref _stopVoice);
                _stopVoice = Play(path, (float)Volume, "first-show-voice");
            }
            catch (Exception ex) { Log.Debug(ex, "First show speech unavailable"); }
        }

        public void Cue(string name)
        {
            if (_disposed || Volume <= 0) return;
            try
            {
                var rel = FirstShowScript.CueAsset(name).Replace('/', Path.DirectorySeparatorChar);
                var path = ContentLocator.Resolve(Path.Combine("Resources", "sounds", rel));
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                Stop(ref _stopCue);
                _stopCue = Play(path, (float)(Volume * FirstShowScript.CueScale), "first-show-cue");
            }
            catch (Exception ex) { Log.Debug(ex, "First show cue unavailable"); }
        }

        private static void Stop(ref Action? stop)
        {
            var s = stop; stop = null;
            try { s?.Invoke(); } catch (Exception ex) { Log.Debug(ex, "First show audio stop failed"); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; Stop(ref _stopVoice); Stop(ref _stopCue);
            try { if (Directory.Exists(_cache)) Directory.Delete(_cache, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
