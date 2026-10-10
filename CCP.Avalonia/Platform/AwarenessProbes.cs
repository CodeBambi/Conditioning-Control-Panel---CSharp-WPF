// PORTED from ConditioningControlPanel/Services/Awareness/AwarenessProbes.cs (7.1.5): the awareness
// observer's platform probes. Core holds the interfaces (AwarenessSignals.cs) and the observer; this
// file is what WPF's Win32ForegroundProbe / Win32InputProbe / AppStateProbe became on a head that
// runs on Windows and on X11.
//
// One foreground resolver per platform: the title comes from ActiveWindowTitle (user32 / X11ActiveWindow),
// the process name from the do-not-disturb guard's resolver (DoNotDisturbGuard on Windows, X11Windows on
// Linux). Only the fullscreen geometry is read here, because nothing else on the head asks for it.
//
// Privacy: nothing in this file logs a title, a process name or an app id. Samples live in the
// observer's memory and leave it only as a ContextFrame through AwarenessPrivacyRules.
//
// not ported: WasapiMicrophoneProbe (the head has no NAudio session API and no PulseAudio reader yet).
// The observer falls back to "microphone unknown", which WPF itself defines as "not in a meeting":
// the fullscreen, typing-burst and CCP-surface gates still apply.

using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF Win32ForegroundProbe, on both platforms.</summary>
internal sealed class HeadForegroundProbe : IForegroundProbe
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private const uint MonitorDefaultToNearest = 2;

    /// <summary>WPF FullscreenSlackPixels: borderless windows land a pixel or two off the edge.</summary>
    private const int FullscreenSlackPixels = 2;

    /// <summary>WPF ShellClasses: the desktop and the taskbar cover the monitor and are not "fullscreen".</summary>
    private static readonly string[] ShellClasses = { "Progman", "WorkerW", "Shell_TrayWnd", "Windows.UI.Core.CoreWindow" };

    public ForegroundSample? Read()
    {
        try
        {
            return OperatingSystem.IsWindows() ? ReadWindows()
                 : OperatingSystem.IsLinux() ? ReadX11()
                 : null;
        }
        catch (Exception ex)
        {
            // A foreground read that throws is a frame we do not cut. It is never a crash.
            Log.Debug("AwarenessObserver: foreground probe failed - {Error}", ex.Message);
            return null;
        }
    }

    private static ForegroundSample? ReadWindows()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        var title = ActiveWindowTitle.ReadWindows();
        var process = DoNotDisturbGuard.ForegroundProcessName();

        var classBuffer = new StringBuilder(128);
        GetClassName(hwnd, classBuffer, classBuffer.Capacity);
        bool fullscreen = !IsShellClass(classBuffer.ToString()) && CoversMonitor(hwnd);

        return new ForegroundSample(hwnd, title, process, fullscreen);
    }

    private static ForegroundSample? ReadX11()
    {
        var title = X11ActiveWindow.ReadTitle();
        var process = X11Windows.ForegroundProcess();
        // No active window (or no X): WPF's "hwnd == IntPtr.Zero", a frame that is not cut.
        if (title.Length == 0 && process.Length == 0) return null;
        return new ForegroundSample(IntPtr.Zero, title, process, X11ActiveWindow.IsActiveWindowFullscreen());
    }

    private static bool IsShellClass(string className)
    {
        foreach (var known in ShellClasses)
            if (string.Equals(className, known, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
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
/// WPF Win32InputProbe: idle seconds, and a typing-burst guess from "fresh input with a still cursor"
/// sampled four times a second over a four-second window. No hook, no key codes, no key counts.
/// The timer exists only between Start and Stop, which is only while the observer runs.
/// </summary>
internal sealed class HeadInputProbe : IInputProbe
{
    [StructLayout(LayoutKind.Sequential)] private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);

    public static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(250);
    public const int WindowSeconds = 4;
    public const double BurstSamplesPerSecond = 2.0;
    private const int WindowSamples = WindowSeconds * 4;

    private readonly bool[] _samples = new bool[WindowSamples];
    private readonly object _lock = new();

    /// <summary>Test seam: (a tick that changes when input arrives, cursor x, cursor y), or null when unreadable.</summary>
    internal Func<(long InputTick, int X, int Y)?>? SampleSource { get; set; }

    private Timer? _timer;
    private int _cursor;
    private int _typingSamples;
    private long _lastInputTick;
    private int _lastX, _lastY;
    private volatile bool _burst;
    private bool _disposed;

    public int IdleSeconds => ReadIdleSeconds();
    public bool IsTypingBurst => _burst;
    internal bool TimerArmed => _timer != null;

    public void Start()
    {
        if (_disposed) return;

        // Re-arm an existing timer rather than bailing (WPF): Stop() disarms it and leaves it in
        // place, and Stop/Start is a mainline cycle (the privacy card's pause, the awareness dial).
        if (_timer != null)
        {
            try { _timer.Change(SampleInterval, SampleInterval); return; }
            catch (ObjectDisposedException) { _timer = null; }
        }

        _timer = new Timer(_ => Sample(), null, SampleInterval, SampleInterval);
    }

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _timer?.Dispose(); } catch { }
        _timer = null;
        _burst = false;
    }

    internal void Sample()
    {
        if (_disposed) return;

        try
        {
            var read = (SampleSource ?? ReadPlatform)();
            if (read is not { } s) return;

            bool cursorMoved = s.X != _lastX || s.Y != _lastY;
            _lastX = s.X;
            _lastY = s.Y;

            bool freshInput = s.InputTick != _lastInputTick;
            _lastInputTick = s.InputTick;

            Push(freshInput && !cursorMoved);
        }
        catch
        {
            // A failed sample is a sample that says "not typing". Never a crash on a timer.
            Push(false);
        }
    }

    private static (long, int, int)? ReadPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info)) return null;
            return GetCursorPos(out var p) ? (info.dwTime, p.X, p.Y) : (info.dwTime, 0, 0);
        }

        if (OperatingSystem.IsLinux())
        {
            long idle = X11ActiveWindow.IdleMilliseconds();
            if (idle < 0) return null;
            // X reports "idle for", not "last input at". now - idle is the moment of the last input, but
            // it wobbles by the round trip's few milliseconds, so it only counts as NEW input when it has
            // moved by more than that wobble. (Rounding it to sample-sized steps instead flips back and
            // forth whenever the moment sits on a step edge, which read as typing on an idle desk.)
            long lastInputAt = StableInputMoment(Environment.TickCount64 - idle);
            var pointer = X11Pointer.Read();
            return pointer is { } p ? (lastInputAt, p.At.X, p.At.Y) : (lastInputAt, 0, 0);
        }

        return null;
    }

    /// <summary>Milliseconds of disagreement between two reads of the same input moment that are still
    /// the same moment (timer jitter + the X round trip).</summary>
    internal const long InputMomentSlackMs = 100;
    private static long _stableInputMoment = long.MinValue;

    /// <summary>The last-input moment with read wobble removed: the held value moves only when the new
    /// reading differs from it by more than <see cref="InputMomentSlackMs"/>. Pure but for the held value.</summary>
    internal static long StableInputMoment(long reading)
    {
        long held = Interlocked.Read(ref _stableInputMoment);
        if (held != long.MinValue && Math.Abs(reading - held) <= InputMomentSlackMs) return held;
        Interlocked.Exchange(ref _stableInputMoment, reading);
        return reading;
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
            if (OperatingSystem.IsWindows())
            {
                var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
                if (!GetLastInputInfo(ref info)) return 0;

                long millis = (long)Environment.TickCount - info.dwTime;
                if (millis < 0) millis += (long)uint.MaxValue + 1;   // ~49-day tick wrap
                return (int)(millis / 1000);
            }

            if (OperatingSystem.IsLinux())
            {
                long idle = X11ActiveWindow.IdleMilliseconds();
                return idle < 0 ? 0 : (int)(idle / 1000);   // unknown reads as active, as WPF's failed call
            }
        }
        catch { }
        return 0;
    }
}

/// <summary>WPF AppStateProbe: CCP's own state, read at frame-cut time.</summary>
internal sealed class HeadAppStateProbe : IAppStateProbe, IAttachableProbe, IDisposable
{
    /// <summary>WPF RecentAchievementMinutes.</summary>
    public const int RecentAchievementMinutes = 30;

    private readonly object _lock = new();
    private string? _recentAchievementId;
    private DateTime _recentAchievementAt = DateTime.MinValue;
    private bool _attached;
    private bool _disposed;

    public void Attach()
    {
        if (_attached || _disposed) return;
        _attached = true;
        // WPF subscribed to AchievementService.AchievementUnlocked and kept the id. The port's feed
        // carries the display name; the frame only asks "was there one recently", so either serves.
        CoreTubeEvents.AchievementUnlocked += OnAchievementUnlocked;
    }

    private void OnAchievementUnlocked(string name)
    {
        lock (_lock)
        {
            _recentAchievementId = name;
            _recentAchievementAt = DateTime.Now;
        }
    }

    public AppStateSample Read(DateTime at)
    {
        try
        {
            var settings = CoreSettings.HasProvider ? CoreSettings.Current : null;

            string? achievement;
            lock (_lock)
            {
                achievement = _recentAchievementId != null &&
                              (at - _recentAchievementAt).TotalMinutes <= RecentAchievementMinutes
                    ? _recentAchievementId
                    : null;
            }

            return new AppStateSample(
                SessionRunning: CoreSession.IsSessionRunning,
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

    /// <summary>WPF IsBlockingSurfaceActive: a video, a lock card, a Chaos run or a game window.</summary>
    private static bool IsBlockingSurfaceActive()
    {
        try { if (CoreEngine.Video?.IsPlaying == true) return true; } catch { }
        try { if (Views.Windows.LockCardWindow.IsAnyOpen()) return true; } catch { }
        try { if (Views.Games.GameWindow.IsAnyOpen()) return true; } catch { }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_attached) CoreTubeEvents.AchievementUnlocked -= OnAchievementUnlocked;
    }
}

/// <summary>WPF's DispatcherTimer at Normal priority, as the observer's poll timer.</summary>
internal sealed class AwarenessPollTimer : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly EventHandler _handler;

    public AwarenessPollTimer(TimeSpan interval, Action tick)
    {
        _handler = (_, _) => tick();
        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = interval };
        _timer.Tick += _handler;
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= _handler;
    }
}
