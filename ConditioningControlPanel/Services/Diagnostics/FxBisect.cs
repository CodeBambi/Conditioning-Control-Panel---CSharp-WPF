using System;
using System.Collections.Generic;
using System.IO;

namespace ConditioningControlPanel.Services.Diagnostics
{
    /// <summary>
    /// DEBUG perf bisect: fx-off.txt in the user-data folder lists loop group names, one per line.
    /// A loop whose start check asks <see cref="Off"/> with a listed name stays parked, so a desk
    /// build can be measured with one group off at a time (flip the motion level after editing the
    /// file so every loop re-checks). Release builds never read the file.
    /// </summary>
    internal static class FxBisect
    {
#if DEBUG
        private static readonly object Gate = new();
        private static HashSet<string> _off = new(StringComparer.OrdinalIgnoreCase);
        private static long _nextReadTicks;
        private static DateTime _stamp;

        public static bool Off(string group)
        {
            lock (Gate)
            {
                long now = Environment.TickCount64;
                if (now >= _nextReadTicks)
                {
                    _nextReadTicks = now + 500;
                    try
                    {
                        var path = Path.Combine(App.UserDataPath, "fx-off.txt");
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
        /// <summary>Every 2 s, while "effects" is listed, strips every Effect in the window (experiment only).</summary>
        public static void Watch(System.Windows.Window window)
        {
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            t.Tick += (_, _) =>
            {
                RunCommands(window);
                if (!Off("effects")) return;
                int n = 0;
                void Walk(System.Windows.DependencyObject d)
                {
                    if (d is System.Windows.UIElement u && u.Effect != null) { u.Effect = null; n++; }
                    int c = System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);
                    for (int i = 0; i < c; i++) Walk(System.Windows.Media.VisualTreeHelper.GetChild(d, i));
                }
                try { Walk(window); } catch { }
                if (n > 0) App.Logger?.Information("[FXBISECT] stripped {N} effects", n);
            };
            t.Start();
        }
        /// <summary>fx-cmd.txt: "motion:N" (0 Full, 1 Reduced, 2 Off) and "goto:key" lines, run once
        /// then the file is deleted. Lets a measurement script drive the app with no input focus.</summary>
        private static void RunCommands(System.Windows.Window window)
        {
            try
            {
                var path = Path.Combine(App.UserDataPath, "fx-cmd.txt");
                if (!File.Exists(path)) return;
                var lines = File.ReadAllLines(path);
                File.Delete(path);
                if (window is not MainWindow mw) return;
                foreach (var raw in lines)
                {
                    var line = raw.Trim();
                    if (line.StartsWith("motion:") && int.TryParse(line.Substring(7), out var idx))
                    {
                        var cmb = mw.AppSettingsTab?.CmbMotionLevel;
                        if (cmb != null) cmb.SelectedIndex = idx;
                    }
                    else if (line.StartsWith("goto:")) mw.ShowTab(line.Substring(5));
                }
            }
            catch (Exception ex) { App.Logger?.Debug("FxBisect command failed: {E}", ex.Message); }
        }
#else
        public static bool Off(string group) => false;
        public static void Watch(System.Windows.Window window) { }
#endif
    }
}
