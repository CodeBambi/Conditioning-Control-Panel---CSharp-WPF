using System;
using System.IO;
using System.Linq;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The portable half of <c>MindWipeService</c>: when a clip should fire, how loud, and which
    /// files are candidates. Everything here is arithmetic over settings and a clock, so it runs
    /// unchanged on every head.
    ///
    /// <para>The other half - NAudio's <c>WaveOutEvent</c>, the A/B crossfade pair, the panic
    /// teardown ordering - stays in the head behind <see cref="CoreMindWipe"/>. A dry <c>git mv</c>
    /// of the whole service put the line exactly here: the compiler rejected <c>NAudio.Wave</c>,
    /// <c>WaveOutEvent</c>, <c>AudioFileReader</c>, <c>DispatcherTimer</c> and
    /// <c>System.Windows.Threading</c> in the declarations alone, and behind those sit 48
    /// <c>App.*</c> body references (30 <c>App.Logger</c>, 9 <c>App.Audio</c>, 3
    /// <c>App.DiscordRpc</c>) that the compiler never even reached.</para>
    ///
    /// <para>Stateless on purpose. The tick loop itself stays in the head because WPF's is welded
    /// to <c>_loopMode</c>, the cancellation source, Discord presence and the panic-ordering
    /// comments in <c>Stop</c>; a second timer in Core that no head would start is speculation.
    /// These functions are what that loop asks, and they are the part that had to cross.</para>
    /// </summary>
    public static class MindWipeSchedule
    {
        /// <summary>How often the service asks "fire now?". The per-tick probabilities below are
        /// all expressed against this interval; change one and you must change the other.</summary>
        public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(10);

        private const double TicksPerHour = 360.0;        // 3600s / 10s
        private const double TicksPerSessionBlock = 30.0; // a 5-minute block / 10s

        /// <summary>Plays per hour, clamped to the range the slider offers.</summary>
        public static double ClampFrequency(double perHour) => Math.Clamp(perHour, 1, 180);

        /// <summary>Volume as a 0..1 fraction - the unit every mind-wipe call site uses.</summary>
        public static double ClampVolume(double volume) => Math.Clamp(volume, 0, 1);

        /// <summary>
        /// Chance of firing in one tick at a steady rate. At the 180/hour maximum this is 0.5,
        /// i.e. a coin flip every ten seconds.
        /// </summary>
        public static double Probability(double frequencyPerHour) => frequencyPerHour / TicksPerHour;

        /// <summary>
        /// Session mode escalates: one extra play per five-minute block, capped at 15 plays per
        /// block. Note the cap differs from <see cref="SessionFrequency"/>'s - deliberately, and
        /// exactly as the Windows service has always behaved.
        /// </summary>
        public static double SessionProbability(int baseFrequency, TimeSpan elapsed) =>
            SessionPlaysPerBlock(baseFrequency, elapsed, cap: 15) / TicksPerSessionBlock;

        /// <summary>
        /// The escalated frequency the UI shows during a session. Capped at 30, NOT at the 15 the
        /// probability uses; both numbers are preserved as found.
        /// </summary>
        public static int SessionFrequency(int baseFrequency, TimeSpan elapsed) =>
            SessionPlaysPerBlock(baseFrequency, elapsed, cap: 30);

        private static int SessionPlaysPerBlock(int baseFrequency, TimeSpan elapsed, int cap)
        {
            // No floor on the elapsed time, deliberately: the Windows service has none, and a
            // clock that steps backwards (DST fall-back mid-session) must go quiet here exactly as
            // it always has rather than quietly keeping the base rate.
            var blocks = (int)(elapsed.TotalMinutes / 5);
            return Math.Min(baseFrequency + blocks, cap);
        }

        /// <summary>
        /// THE mind-wipe clip folder the UI advertises: <c>&lt;EffectiveAssets&gt;/mindwipe</c>.
        /// Until v6.8.7 the service read ONLY <see cref="LegacyAudioFolder"/>, so the
        /// "assets/mindwipe/" hint in <c>mindwipe_no_audio_files</c> named a folder that was never
        /// scanned. New files go here; the legacy install folder is still scanned so nobody's old
        /// files go silent, and nothing migrates, moves or deletes.
        /// </summary>
        public static string AudioFolder
        {
            get
            {
                try { return Path.Combine(CorePaths.EffectiveAssets, "mindwipe"); }
                catch { return LegacyAudioFolder; }   // pre-settings startup / test host
            }
        }

        /// <summary>
        /// The ORIGINAL clip folder under the install directory (<c>Resources/sounds/mindwipe</c>),
        /// where the built-in clips ship. Still scanned; never advertised as the place to put new
        /// ones, never written to, never emptied. The base directory is the running head's, which
        /// is the same answer whichever assembly asks for it.
        /// </summary>
        public static string LegacyAudioFolder =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "mindwipe");

        /// <summary>
        /// The candidate clips. A user-chosen custom file that exists wins over both folders (a
        /// short ~2s clip is what the picker recommends); otherwise the advertised folder and the
        /// legacy folder are scanned for .mp3/.wav/.ogg. Never returns null, so a caller with no
        /// clips gets an empty array and stays quiet rather than throwing.
        /// </summary>
        public static string[] DiscoverClips(string? customPath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
                {
                    Log.Information("MindWipe: Using custom audio file {Path}", customPath);
                    return new[] { customPath };
                }

                var folder = AudioFolder;
                Log.Information("MindWipe: Looking for audio files in {Path} (and legacy {Legacy})",
                    folder, LegacyAudioFolder);

                // Create the advertised folder so it exists to be found; the legacy folder is left
                // exactly as the installer shipped it.
                try { Directory.CreateDirectory(folder); }
                catch (Exception ex) { Log.Warning(ex, "MindWipe: could not create {Path}", folder); }

                var files = new[] { folder, LegacyAudioFolder }
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(Directory.Exists)
                    .SelectMany(dir => Directory.GetFiles(dir, "*.*"))
                    .Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (files.Length == 0)
                    Log.Warning("MindWipe: No .mp3/.wav/.ogg files found in {Path}", folder);
                else
                    Log.Information("MindWipe: Loaded {Count} audio files: {Files}",
                        files.Length, string.Join(", ", files.Select(Path.GetFileName)));

                return files;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "MindWipe: Failed to load audio files");
                return Array.Empty<string>();
            }
        }
    }
}
