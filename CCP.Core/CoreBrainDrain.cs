using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The Brain Drain AUDIO half seam (WPF App.BrainDrain = Services/LockCard/BrainDrainService.cs):
    /// started by the engine (StartStop.cs:378) and by sessions, stopped by the engine stop, a pause
    /// and panic. The head owns the player (CCP.Avalonia Platform/BrainDrainPlayer). Unseeded = no-op.
    /// The visual half (blur/melt, WPF BrainDrainLayer + capture pump) is not here.
    /// </summary>
    public static class CoreBrainDrain
    {
        public static volatile Action? StartProvider;
        public static volatile Action? StopProvider;
        public static volatile Func<bool>? IsRunningProvider;
        /// <summary>WPF BrainDrainService.Intensity setter (the session ramp writes it, never the setting).</summary>
        public static volatile Action<double>? IntensityProvider;
        public static volatile Func<int>? ClipCountProvider;
        public static volatile Action? ReloadClipsProvider;

        public static void Start() { try { StartProvider?.Invoke(); } catch { } }
        public static void Stop() { try { StopProvider?.Invoke(); } catch { } }
        public static bool IsRunning { get { try { return IsRunningProvider?.Invoke() ?? false; } catch { return false; } } }
        public static void SetIntensity(double value) { try { IntensityProvider?.Invoke(value); } catch { } }
        public static int ClipCount { get { try { return ClipCountProvider?.Invoke() ?? 0; } catch { return 0; } } }
        public static void ReloadClips() { try { ReloadClipsProvider?.Invoke(); } catch { } }
    }

    /// <summary>The pure numbers of WPF BrainDrainService (same values).</summary>
    public static class BrainDrainSchedule
    {
        /// <summary>WPF UpdateTimerInterval: 500 ms in high refresh, else 5 s.</summary>
        public static TimeSpan TickInterval(bool highRefresh) =>
            highRefresh ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromSeconds(5);

        /// <summary>WPF Intensity setter clamp.</summary>
        public static double ClampIntensity(double v) => Math.Clamp(v, 1, 100);

        /// <summary>WPF Timer_Tick: intensity% spread over the ticks in one minute.</summary>
        public static double Probability(double intensity, TimeSpan interval) =>
            intensity / 100.0 / (60.0 / interval.TotalSeconds);

        /// <summary>WPF EffectiveVolume (#1104): master x Brain Drain volume as a 0-1 gain.</summary>
        public static float EffectiveVolume(int masterVolume, int brainDrainVolume)
            => Math.Clamp(masterVolume, 0, 100) / 100f * (Math.Clamp(brainDrainVolume, 0, 100) / 100f);

        private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".ogg" };

        /// <summary>WPF AudioFolderPath: &lt;EffectiveAssetsPath&gt;/braindrain.</summary>
        public static string AudioFolderPath => Path.Combine(CorePaths.EffectiveAssets, "braindrain");

        /// <summary>WPF LegacyAudioFolderPath: still scanned, never advertised.</summary>
        public static string LegacyAudioFolderPath =>
            Path.Combine(AppContext.BaseDirectory, "Resources", "sounds", "braindrain");

        /// <summary>WPF LoadAudioFiles: both folders, de-duped by file name, the assets copy wins.
        /// Creates the assets folder on the first scan so users can find it.</summary>
        public static string[] DiscoverClips() => DiscoverClips(AudioFolderPath, LegacyAudioFolderPath);

        public static string[] DiscoverClips(string primary, string legacy)
        {
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try { Directory.CreateDirectory(primary); } catch { }
            foreach (var dir in new[] { primary, legacy })
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.GetFiles(dir)
                                 .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())))
                        byName.TryAdd(Path.GetFileName(f), f);
                }
                catch { }
            }
            return byName.Values.ToArray();
        }
    }
}
