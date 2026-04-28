using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Audio cues for the webcam calibration flow (full 25-point + 1-dot quick recal).
    /// Each public method maps to one calibration moment and pins the asset + master-
    /// volume multiplier. Playback is fire-and-forget; clips are ≤1.6 s and self-dispose,
    /// so cancelling the calibration mid-flight just lets the in-flight clip finish in
    /// the background.
    /// </summary>
    public static class CalibrationSoundService
    {
        public static void DotSampleStart()      => Play("lvup.mp3", 0.25f);
        public static void RingFull()            => Play("bubbles/Pop.mp3", 0.35f);
        public static void AllDotsCollected()    => Play("chime2.mp3", 0.5f);
        public static void ValidationStepPass()  => Play("chime1.mp3", 0.45f);
        public static void CalibrationVerified() => Play("result.mp3", 0.6f);
        public static void QuickRecalComplete()  => Play("chime3.mp3", 0.55f);

        private static void Play(string filename, float multiplier)
        {
            try
            {
                var settings = App.Settings?.Current;
                if (settings == null) return;

                float master = settings.MasterVolume / 100f;
                float linear = master * multiplier;
                float curved = (float)Math.Pow(linear, 1.5);
                if (curved <= 0.001f) return;

                string path = ModResourceResolver.ResolveAudioPath(filename);
                if (!File.Exists(path))
                {
                    App.Logger?.Warning("CalibrationSoundService: asset not found at {Path}", path);
                    return;
                }

                Task.Run(() =>
                {
                    WaveOutEvent? device = null;
                    AudioFileReader? reader = null;
                    try
                    {
                        reader = new AudioFileReader(path) { Volume = curved };
                        device = new WaveOutEvent();
                        device.Init(reader);
                        device.Play();
                        while (device.PlaybackState == PlaybackState.Playing)
                            Thread.Sleep(50);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning("CalibrationSoundService playback failed: {Error}", ex.Message);
                    }
                    finally
                    {
                        reader?.Dispose();
                        if (device != null)
                        {
                            try { device.Stop(); } catch { }
                            device.Dispose();
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("CalibrationSoundService.Play threw: {Error}", ex.Message);
            }
        }
    }
}
