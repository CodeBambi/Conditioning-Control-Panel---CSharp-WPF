using System;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The audio seam: the four things ported views ask of the audio service. The service itself
    /// is NAudio and LibVLC on Windows and stays in the head; a Linux backend is a later layer.
    ///
    /// <para>Unseeded means "no audio on this head": <see cref="PlayOneShot"/> fires the finished
    /// callback at once and returns, which is exactly what the service does when disposed, so a
    /// view that sequences on the sound still proceeds; ducking is a no-op and the generation
    /// never advances.</para>
    ///
    /// <para><c>ApplyPreferredDevice</c> is deliberately not here: it takes an NAudio or LibVLC
    /// player object, which is the head's business. A view that needs it keeps its note.</para>
    /// </summary>
    public static class CoreAudio
    {
        public static volatile Action<string, float, string, Action<TimeSpan>?, Action?>? PlayOneShotProvider;
        public static volatile Action<int>? DuckProvider;
        public static volatile Action<long>? UnduckProvider;
        public static volatile Func<long>? DuckGenerationProvider;

        public static void PlayOneShot(string path, float volume, string tag = "audio",
                                       Action<TimeSpan>? onStarted = null, Action? onFinished = null)
        {
            var p = PlayOneShotProvider;
            if (p is null) { try { onFinished?.Invoke(); } catch { } return; }
            try { p(path, volume, tag, onStarted, onFinished); }
            catch { try { onFinished?.Invoke(); } catch { } }
        }

        public static void Duck(int strength = 80) { try { DuckProvider?.Invoke(strength); } catch { } }
        public static void Unduck(long generation = -1) { try { UnduckProvider?.Invoke(generation); } catch { } }
        public static long DuckGeneration { get { try { return DuckGenerationProvider?.Invoke() ?? 0; } catch { return 0; } } }
    }
}
