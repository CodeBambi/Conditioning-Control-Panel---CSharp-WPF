using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// The real foreground-window probe: <c>GetForegroundWindow</c> for the handle,
    /// <c>GetWindowThreadProcessId</c> for the owning process, and a monitor-rect compare for
    /// fullscreen.
    ///
    /// <para>The process name is what fixes doc 02 §1.6's substring lottery. Classifying "on target
    /// for the deadline" by title alone lands on the Shopping dictionary's "target"; classifying the
    /// process ("slack") does not. Titles still matter for browsers, where the site genuinely is the
    /// app, and <see cref="AwarenessObserverPolicy"/> is where that preference is expressed.</para>
    /// </summary>
    public sealed class Win32ForegroundProbe : IForegroundProbe
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private const uint MonitorDefaultToNearest = 2;

        /// <summary>Fullscreen compare slack, in pixels — borderless windows are a pixel or two off.</summary>
        private const int FullscreenSlackPixels = 2;

        /// <summary>
        /// Shell window classes that legitimately cover a whole monitor and are never "fullscreen" in
        /// the sense the DND rule means. Without these, sitting on the desktop reads as a game.
        /// </summary>
        private static readonly string[] ShellClasses = { "Progman", "WorkerW", "Shell_TrayWnd", "Windows.UI.Core.CoreWindow" };

        private uint _cachedPid;
        private string _cachedProcessName = "";

        /// <inheritdoc />
        public ForegroundSample? Read()
        {
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return null;

                var titleBuffer = new StringBuilder(512);
                GetWindowText(hwnd, titleBuffer, titleBuffer.Capacity);
                var title = titleBuffer.ToString();

                var classBuffer = new StringBuilder(128);
                GetClassName(hwnd, classBuffer, classBuffer.Capacity);
                var className = classBuffer.ToString();

                var process = ProcessNameOf(hwnd);
                bool fullscreen = !IsShellClass(className) && CoversMonitor(hwnd);

                return new ForegroundSample(hwnd, title, process, fullscreen);
            }
            catch (Exception ex)
            {
                // A foreground read that throws is a frame we do not cut. It is never a crash.
                App.Logger?.Debug("AwarenessObserver: foreground probe failed - {Error}", ex.Message);
                return null;
            }
        }

        private static bool IsShellClass(string className)
        {
            foreach (var known in ShellClasses)
            {
                if (string.Equals(className, known, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private string ProcessNameOf(IntPtr hwnd)
        {
            try
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0) return "";

                // One process lookup per distinct pid: the foreground rarely changes between polls
                // and Process.GetProcessById is by far the most expensive call on this path.
                if (pid == _cachedPid && _cachedProcessName.Length > 0) return _cachedProcessName;

                using var process = Process.GetProcessById((int)pid);
                var name = (process.ProcessName ?? "").ToLowerInvariant();
                if (name.EndsWith(".exe", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 4);

                _cachedPid = pid;
                _cachedProcessName = name;
                return name;
            }
            catch
            {
                // The process exited between the handle read and the lookup, or it is protected.
                return "";
            }
        }

        private static bool CoversMonitor(IntPtr hwnd)
        {
            if (!GetWindowRect(hwnd, out var window)) return false;

            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero) return false;

            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfoW(monitor, ref info)) return false;

            var screen = info.rcMonitor;
            return window.Left <= screen.Left + FullscreenSlackPixels &&
                   window.Top <= screen.Top + FullscreenSlackPixels &&
                   window.Right >= screen.Right - FullscreenSlackPixels &&
                   window.Bottom >= screen.Bottom - FullscreenSlackPixels;
        }
    }

    /// <summary>
    /// Real input idle plus a typing-burst estimate, both from <c>GetLastInputInfo</c> — the same
    /// source <see cref="ActivityTracker"/> already uses, sampled faster.
    ///
    /// <para><b>No keyboard hook, no key identities, no content.</b> Every
    /// <see cref="SampleInterval"/> the probe asks Windows for the timestamp of the last input event
    /// and for the cursor position. A sample counts as "typing-ish" when the timestamp advanced while
    /// the cursor stood still; the burst flag is on when enough of the trailing
    /// <see cref="WindowSeconds"/> of samples were typing-ish. It therefore measures input events per
    /// second and cannot distinguish one key from another, which is exactly as much as a
    /// do-not-disturb rule needs to know.</para>
    /// </summary>
    public sealed class Win32InputProbe : IInputProbe
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);

        /// <summary>Sampling cadence. Four samples a second is enough to see a burst and costs two syscalls.</summary>
        public static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(250);

        /// <summary>Trailing window the burst is measured over.</summary>
        public const int WindowSeconds = 4;

        /// <summary>Typing-ish samples per second required to call it a burst (doc 02 §4.2: "&gt;2 keys/sec").</summary>
        public const double BurstSamplesPerSecond = 2.0;

        private const int WindowSamples = WindowSeconds * 4;

        private readonly bool[] _samples = new bool[WindowSamples];
        private readonly object _lock = new();

        private Timer? _timer;
        private int _cursor;
        private int _typingSamples;
        private uint _lastInputTick;
        private int _lastX, _lastY;
        private volatile bool _burst;
        private bool _disposed;

        /// <inheritdoc />
        public int IdleSeconds => ReadIdleSeconds();

        /// <inheritdoc />
        public bool IsTypingBurst => _burst;

        /// <inheritdoc />
        public void Start()
        {
            if (_disposed || _timer != null) return;
            _timer = new Timer(_ => Sample(), null, SampleInterval, SampleInterval);
        }

        /// <inheritdoc />
        public void Stop()
        {
            try { _timer?.Change(Timeout.Infinite, Timeout.Infinite); } catch (ObjectDisposedException) { }
            lock (_lock)
            {
                Array.Clear(_samples, 0, _samples.Length);
                _typingSamples = 0;
                _cursor = 0;
            }
            _burst = false;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _timer?.Dispose(); } catch { }
            _timer = null;
            _burst = false;
        }

        private void Sample()
        {
            if (_disposed) return;

            try
            {
                var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
                if (!GetLastInputInfo(ref info)) return;

                bool cursorMoved = false;
                if (GetCursorPos(out var point))
                {
                    cursorMoved = point.X != _lastX || point.Y != _lastY;
                    _lastX = point.X;
                    _lastY = point.Y;
                }

                bool freshInput = info.dwTime != _lastInputTick;
                _lastInputTick = info.dwTime;

                Push(freshInput && !cursorMoved);
            }
            catch
            {
                // A failed sample is a sample that says "not typing". Never a crash on a timer.
                Push(false);
            }
        }

        private void Push(bool typingish)
        {
            lock (_lock)
            {
                if (_samples[_cursor]) _typingSamples--;
                _samples[_cursor] = typingish;
                if (typingish) _typingSamples++;
                _cursor = (_cursor + 1) % WindowSamples;

                _burst = _typingSamples / (double)WindowSeconds >= BurstSamplesPerSecond;
            }
        }

        private static int ReadIdleSeconds()
        {
            try
            {
                var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
                if (!GetLastInputInfo(ref info)) return 0;

                long millis = (long)Environment.TickCount - info.dwTime;
                if (millis < 0) millis += (long)uint.MaxValue + 1;   // ~49-day tick wrap
                return (int)(millis / 1000);
            }
            catch { return 0; }
        }
    }

    /// <summary>
    /// "Is anything using the microphone?", via a WASAPI capture-endpoint session sweep.
    ///
    /// <para>Cached for <see cref="CacheSeconds"/> because the sweep mints a COM wrapper per endpoint,
    /// per session manager and per session, and the observer polls every 1.5 seconds. Every wrapper is
    /// disposed on the way out — the same discipline <c>AudioService.RenderSessionScope</c> exists to
    /// enforce on the ducking path, where leaving them to the finalizer leaked handles (#686).</para>
    /// </summary>
    public sealed class WasapiMicrophoneProbe : IMicrophoneProbe
    {
        /// <summary>How long one sweep's answer is reused.</summary>
        public const int CacheSeconds = 5;

        private readonly object _lock = new();
        private DateTime _checkedAt = DateTime.MinValue;
        private bool _inUse;

        /// <inheritdoc />
        public bool IsInUse(DateTime at)
        {
            lock (_lock)
            {
                if ((at - _checkedAt).TotalSeconds < CacheSeconds) return _inUse;
                _checkedAt = at;
                _inUse = Sweep();
                return _inUse;
            }
        }

        private static bool Sweep()
        {
            // Every wrapper produced here is registered and released on the way out, the same discipline
            // AudioService.RenderSessionScope enforces on the ducking path. Registered as `object` and
            // released through a guarded cast, because which of these NAudio types is IDisposable has
            // changed between versions and a wrapper left to the finalizer leaks a native handle (#686).
            var owned = new List<object?>();
            try
            {
                var enumerator = new MMDeviceEnumerator();
                owned.Add(enumerator);

                var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                owned.Add(endpoints);

                for (int d = 0; d < endpoints.Count; d++)
                {
                    try
                    {
                        var device = endpoints[d];
                        owned.Add(device);

                        var manager = device.AudioSessionManager;
                        owned.Add(manager);

                        var sessions = manager?.Sessions;
                        if (sessions == null) continue;
                        owned.Add(sessions);

                        for (int i = 0; i < sessions.Count; i++)
                        {
                            var session = sessions[i];
                            owned.Add(session);
                            if (session.State == AudioSessionState.AudioSessionStateActive) return true;
                        }
                    }
                    catch { /* endpoint vanished mid-sweep */ }
                }
            }
            catch (Exception ex)
            {
                // No audio stack, no permission, or a device in a bad state. "Mic unknown" must read
                // as "not in a meeting" here: the fullscreen and CCP-surface gates still apply, and
                // silently muting awareness forever because an endpoint threw is the worse failure.
                App.Logger?.Debug("AwarenessObserver: microphone probe failed - {Error}", ex.Message);
            }
            finally
            {
                for (int i = owned.Count - 1; i >= 0; i--)
                {
                    if (owned[i] is IDisposable disposable)
                    {
                        try { disposable.Dispose(); } catch { }
                    }
                }
            }

            return false;
        }
    }

    /// <summary>
    /// CCP's own state, read off the <c>App</c> statics. Defensive throughout: every one of these is
    /// null during early startup and in tests.
    /// </summary>
    public sealed class AppStateProbe : IAppStateProbe, IDisposable
    {
        /// <summary>How long an unlocked achievement stays "recent" (doc 02 §2.3: "last 30 min").</summary>
        public const int RecentAchievementMinutes = 30;

        private readonly object _lock = new();
        private string? _recentAchievementId;
        private DateTime _recentAchievementAt = DateTime.MinValue;
        private AchievementService? _achievements;
        private bool _disposed;

        /// <summary>Subscribes to the achievement feed so "you just unlocked X" can ride the next frame.</summary>
        public void Attach()
        {
            try
            {
                if (_achievements != null) return;
                _achievements = App.Achievements;
                if (_achievements != null) _achievements.AchievementUnlocked += OnAchievementUnlocked;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("AwarenessObserver: could not attach the achievement feed - {Error}", ex.Message);
            }
        }

        private void OnAchievementUnlocked(object? sender, Models.Achievement achievement)
        {
            try
            {
                lock (_lock)
                {
                    _recentAchievementId = achievement?.Id;
                    _recentAchievementAt = DateTime.Now;
                }
            }
            catch { }
        }

        /// <inheritdoc />
        public AppStateSample Read(DateTime at)
        {
            try
            {
                var settings = App.Settings?.Current;

                string? achievement;
                lock (_lock)
                {
                    achievement = _recentAchievementId != null &&
                                  (at - _recentAchievementAt).TotalMinutes <= RecentAchievementMinutes
                        ? _recentAchievementId
                        : null;
                }

                return new AppStateSample(
                    SessionRunning: App.IsSessionRunning,
                    UserLevel: settings?.PlayerLevel ?? 0,
                    LoginStreakDays: settings?.CurrentStreak ?? 0,
                    RecentAchievementId: achievement,
                    BlockingSurfaceActive: IsBlockingSurfaceActive());
            }
            catch
            {
                return AppStateSample.Empty;
            }
        }

        /// <summary>
        /// The CCP surfaces awareness must not talk over: a mandatory video, a lock card, or a live
        /// DtRH run. Each already has authored lines of its own.
        /// </summary>
        private static bool IsBlockingSurfaceActive()
        {
            try { if (App.Video?.IsPlaying == true) return true; } catch { }
            try { if (ConditioningControlPanel.LockCardWindow.IsAnyOpen()) return true; } catch { }
            try { if (Services.Chaos.DtrhHostService.IsActive) return true; } catch { }
            return false;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_achievements != null) _achievements.AchievementUnlocked -= OnAchievementUnlocked;
            }
            catch { }
            _achievements = null;
        }
    }
}
