using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Tracks user activity via Win32 GetLastInputInfo to detect idle state.
    /// Used by anti-cheat to suppress passive XP when the user is AFK.
    /// </summary>
    public class ActivityTracker : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        private const int IdleThresholdSeconds = 180; // 3 minutes
        private const int CheckIntervalSeconds = 10;

        private readonly DispatcherTimer _timer;
        private bool _isIdle;
        private bool _disposed;

        /// <summary>
        /// Whether the user is currently idle (no keyboard/mouse input for 3 minutes).
        /// </summary>
        public bool IsIdle => _isIdle;

        /// <summary>
        /// Fired when idle state changes. Argument is true when user becomes idle.
        /// </summary>
        public event EventHandler<bool>? IdleStateChanged;

        public ActivityTracker()
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(CheckIntervalSeconds)
            };
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            var idleSeconds = GetIdleSeconds();
            var nowIdle = idleSeconds >= IdleThresholdSeconds;

            if (nowIdle != _isIdle)
            {
                _isIdle = nowIdle;
                App.Logger?.Debug("ActivityTracker: User is now {State} (idle for {Seconds}s)",
                    nowIdle ? "IDLE" : "ACTIVE", idleSeconds);
                IdleStateChanged?.Invoke(this, nowIdle);
            }
        }

        /// <summary>
        /// Gets the number of seconds since the last keyboard/mouse input.
        /// </summary>
        private static int GetIdleSeconds()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info))
                return 0; // If the call fails, assume active

            var idleMillis = (long)Environment.TickCount - info.dwTime;
            // Handle tick count wrap-around (every ~49 days)
            if (idleMillis < 0)
                idleMillis += (long)uint.MaxValue + 1;

            return (int)(idleMillis / 1000);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
        }
    }
}
