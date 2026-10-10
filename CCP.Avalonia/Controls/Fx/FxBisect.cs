using System;
using System.Collections.Generic;
using System.IO;

namespace ConditioningControlPanel.Avalonia.Controls.Fx
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Services/Diagnostics/FxBisect.cs (DEBUG perf bisect).
    ///
    /// <para><c>fx-off.txt</c> in the user-data folder lists loop group names, one per line (# for
    /// comments). A loop whose start check asks <see cref="Off"/> with a listed name stays parked,
    /// so a desk build can be measured with one group off at a time. A group "canvas:Name" is also
    /// parked by a bare "canvas" line. Flip the motion level after editing the file so every loop
    /// re-checks (or write <c>motion:N</c> to <c>fx-cmd.txt</c>).</para>
    ///
    /// <para><c>fx-cmd.txt</c>: "motion:N" (0 Full, 1 Reduced, 2 Off) lines, run once and the file
    /// deleted, so a measurement script can drive the app with no input focus. The WPF "goto:key"
    /// command needs the shell's tab router and is not wired here; "effects" (strip every Effect)
    /// runs against the main window. Release builds never read either file.</para>
    /// </summary>
    internal static class FxBisect
    {
#if DEBUG
        private static readonly object Gate = new();
        private static HashSet<string> _off = new(StringComparer.OrdinalIgnoreCase);
        private static long _nextReadTicks;
        private static DateTime _stamp;
        private static global::Avalonia.Threading.DispatcherTimer? _cmd;

        /// <summary>Folder the two files live in; tests point it elsewhere.</summary>
        internal static string Folder = CorePaths.UserData;

        public static bool Off(string group)
        {
            EnsureCommandPump();
            lock (Gate)
            {
                long now = Environment.TickCount64;
                if (now >= _nextReadTicks)
                {
                    _nextReadTicks = now + 500;
                    try
                    {
                        var path = Path.Combine(Folder, "fx-off.txt");
                        if (!File.Exists(path)) { _off.Clear(); _stamp = default; }
                        else
                        {
                            var stamp = File.GetLastWriteTimeUtc(path);
                            if (stamp != _stamp)
                            {
                                _stamp = stamp;
                                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                foreach (var line in File.ReadAllLines(path))
                                {
                                    var t = line.Trim();
                                    if (t.Length > 0 && !t.StartsWith('#')) set.Add(t);
                                }
                                _off = set;
                            }
                        }
                    }
                    catch { }
                }
                if (_off.Count == 0) return false;
                if (_off.Contains(group)) return true;
                int colon = group.IndexOf(':');
                return colon > 0 && _off.Contains(group.Substring(0, colon));
            }
        }

        /// <summary>Force the next <see cref="Off"/> to re-read the file (tests).</summary>
        internal static void Invalidate()
        {
            lock (Gate) { _nextReadTicks = 0; _stamp = default; }
        }

        private static void EnsureCommandPump()
        {
            if (_cmd != null) return;
            try
            {
                if (!global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) return;
                _cmd = new global::Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                _cmd.Tick += (_, _) => { RunCommands(); StripEffects(); };
                _cmd.Start();
            }
            catch { }
        }

        /// <summary>Run and delete fx-cmd.txt.</summary>
        internal static void RunCommands()
        {
            try
            {
                var path = Path.Combine(Folder, "fx-cmd.txt");
                if (!File.Exists(path)) return;
                var lines = File.ReadAllLines(path);
                File.Delete(path);
                foreach (var raw in lines)
                {
                    var line = raw.Trim();
                    if (line.StartsWith("motion:") && int.TryParse(line.Substring(7), out var idx) && idx is >= 0 and <= 2)
                    {
                        CoreSettings.Current.MotionLevel = (global::ConditioningControlPanel.Models.MotionLevel)idx;
                        AmbientFxCanvas.Env.RaiseMotionGateChanged();
                    }
                    else if (line.Length > 0)
                    {
                        Serilog.Log.Information("[FXBISECT] command not wired in this head: {C}", line);
                    }
                }
            }
            catch (Exception ex) { Serilog.Log.Debug("FxBisect command failed: {E}", ex.Message); }
        }

        /// <summary>While "effects" is listed, strips every Effect in the main window (experiment only).</summary>
        private static void StripEffects()
        {
            if (!Off("effects")) return;
            var app = global::Avalonia.Application.Current?.ApplicationLifetime
                as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var window = app?.MainWindow;
            if (window == null) return;
            int n = 0;
            void Walk(global::Avalonia.Visual v)
            {
                if (v.Effect != null) { v.Effect = null; n++; }
                foreach (var c in global::Avalonia.VisualTree.VisualExtensions.GetVisualChildren(v)) Walk(c);
            }
            try { Walk(window); } catch { }
            if (n > 0) Serilog.Log.Information("[FXBISECT] stripped {N} effects", n);
        }
#else
        public static bool Off(string group) => false;
        internal static void RunCommands() { }
#endif
    }
}
