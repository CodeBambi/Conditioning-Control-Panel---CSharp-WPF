using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace ConditioningControlPanel.Avalonia.Controls.Fx
{
    /// <summary>
    /// The desktop's "reduce animations" preference, the twin of WPF's
    /// <c>SystemParameters.ClientAreaAnimation</c> read by MotionFx.Level. It can only ever REMOVE
    /// motion: when the OS says animations are off, the user's Full is capped to Reduced.
    ///
    /// <list type="bullet">
    /// <item>Windows: <c>SystemParametersInfo(SPI_GETCLIENTAREAANIMATION)</c>, the same flag WPF
    /// reads (Settings, Accessibility, Visual effects, Animation effects).</item>
    /// <item>GNOME (and anything on gsettings): <c>org.gnome.desktop.interface enable-animations</c>.</item>
    /// <item>KDE Plasma: <c>AnimationDurationFactor</c> in <c>[KDE]</c> of <c>kdeglobals</c>; 0 is
    /// "instant", Plasma's own off switch.</item>
    /// </list>
    ///
    /// <para>The read is cached and refreshed on a background timer (the Linux probes spawn a
    /// process or read a file, never on the UI thread per frame). A change raises <see cref="Changed"/>
    /// once, which the head's motion gate forwards to every running loop. Anything unknown (no
    /// gsettings, no kdeglobals, a probe that throws) answers "animations on", so a missing desktop
    /// never takes motion away.</para>
    /// </summary>
    public static class OsReducedMotion
    {
        /// <summary>How often the cached answer is refreshed.</summary>
        public static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(15);

        private const uint SpiGetClientAreaAnimation = 0x1042;

        private static volatile bool _enabled = true;
        private static int _started;
        private static Timer? _timer;

        /// <summary>Tests pin the answer here; null = probe the desktop.</summary>
        internal static bool? TestOverride;

        /// <summary>Raised (on a background thread) when the OS answer flips.</summary>
        public static event Action? Changed;

        /// <summary>True unless the desktop asks for reduced animations.</summary>
        public static bool AnimationsEnabled
        {
            get
            {
                if (TestOverride is { } o) return o;
                EnsureStarted();
                return _enabled;
            }
        }

        private static void EnsureStarted()
        {
            if (Interlocked.Exchange(ref _started, 1) == 1) return;
            // Windows answers in microseconds, so the first read is exact; Linux probes on the pool.
            if (OperatingSystem.IsWindows()) _enabled = ProbeWindows() ?? true;
            _timer = new Timer(_ => Refresh(), null, OperatingSystem.IsWindows() ? RefreshEvery : TimeSpan.Zero, RefreshEvery);
        }

        /// <summary>Probe now and raise <see cref="Changed"/> if the answer moved.</summary>
        internal static void Refresh()
        {
            bool now;
            try { now = Probe(); }
            catch { now = true; }
            if (now == _enabled) return;
            _enabled = now;
            try { Changed?.Invoke(); }
            catch (Exception ex) { Serilog.Log.Debug("OsReducedMotion.Changed: {E}", ex.Message); }
        }

        private static bool Probe()
        {
            if (OperatingSystem.IsWindows()) return ProbeWindows() ?? true;
            if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
            {
                // Either desktop saying "off" is enough: the preference can only remove motion.
                if (ProbeKde() == false) return false;
                if (ProbeGnome() == false) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ Windows

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);

        private static bool? ProbeWindows()
        {
            try
            {
                int v = 1;
                return SystemParametersInfo(SpiGetClientAreaAnimation, 0, ref v, 0) ? v != 0 : null;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------ GNOME

        private static bool? ProbeGnome()
        {
            try
            {
                var psi = new ProcessStartInfo("gsettings", "get org.gnome.desktop.interface enable-animations")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null) return null;
                var read = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(1500)) { try { p.Kill(); } catch { } return null; }
                return p.ExitCode == 0 ? ParseGsettings(read.GetAwaiter().GetResult()) : null;
            }
            catch { return null; }   // no gsettings on this desktop
        }

        /// <summary>gsettings prints <c>true</c> or <c>false</c>; anything else is unknown.</summary>
        public static bool? ParseGsettings(string? output)
        {
            var t = output?.Trim().Trim('\'').ToLowerInvariant();
            return t switch
            {
                "true" => true,
                "false" => false,
                _ => null,
            };
        }

        // ------------------------------------------------------------------ KDE

        private static bool? ProbeKde()
        {
            try
            {
                var cfg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (string.IsNullOrWhiteSpace(cfg))
                    cfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
                var path = Path.Combine(cfg, "kdeglobals");
                return File.Exists(path) ? ParseKdeGlobals(File.ReadAllText(path)) : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// <c>[KDE] AnimationDurationFactor</c>: 0 (or less) = animations off, any positive factor =
        /// on, missing = unknown. Group and key names are matched the way KConfig does (exact).
        /// </summary>
        public static bool? ParseKdeGlobals(string? text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            bool inKde = false;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                if (line[0] == '[')
                {
                    inKde = line == "[KDE]";
                    continue;
                }
                if (!inKde) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line.Substring(0, eq).Trim();
                // KConfig allows a locale or $-flag suffix on a key: AnimationDurationFactor[$e].
                int br = key.IndexOf('[');
                if (br > 0) key = key.Substring(0, br);
                if (key != "AnimationDurationFactor") continue;
                var val = line.Substring(eq + 1).Trim();
                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                    return f > 0;
                return null;
            }
            return null;
        }
    }
}
