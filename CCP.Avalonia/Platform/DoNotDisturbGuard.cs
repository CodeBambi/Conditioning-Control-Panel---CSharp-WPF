// PORTED from ConditioningControlPanel/Services/UI/DoNotDisturbGuard.cs (same numbers: 1 s foreground
// cache, 60 s log throttle). The list rule lives in Core (Services/UI/DndProcessList).
// Consulted by the scheduled flash (App CoreFlash.ShowProvider -> HoldsScheduledFlash, WPF FlashService.cs:689)
// and the scheduled mandatory video (MandatoryVideoScheduler.ShouldDefer -> HoldsScheduledVideo, WPF
// VideoService.cs:3013); Settings > Performance BtnDndPickApp lists RunningWindowedProcesses.
// ponytail: Linux reads no foreground process
// yet (X11ActiveWindow reads the title only; needs _NET_WM_PID), so it never suppresses there.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

internal static class DoNotDisturbGuard
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private const long CacheMs = 1000;
    private const long LogIntervalMs = 60_000;

    private static long _cacheExpiryTick, _nextLogTick;
    private static string _cachedProcess = "";
    private static uint _cachedPid;
    private static readonly object Gate = new();

    /// <summary>Test seam: the foreground process name (normalised); null = the real read.</summary>
    internal static Func<string>? ForegroundForTest { get; set; }

    /// <summary>The foreground window's process name, normalised; "" when unknown. Cached 1 s.</summary>
    internal static string ForegroundProcessName()
    {
        if (ForegroundForTest is { } f) return f();
        if (!OperatingSystem.IsWindows()) return "";
        lock (Gate)
        {
            var now = Environment.TickCount64;
            if (now < _cacheExpiryTick) return _cachedProcess;
            _cacheExpiryTick = now + CacheMs;
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) { _cachedProcess = ""; return ""; }
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0) { _cachedProcess = ""; return ""; }
                if (pid == _cachedPid && _cachedProcess.Length > 0) return _cachedProcess;
                using var process = Process.GetProcessById((int)pid);
                _cachedPid = pid;
                _cachedProcess = DndProcessList.Normalize(process.ProcessName);
                return _cachedProcess;
            }
            catch
            {
                // An unknown foreground reads as "not privileged": spawns behave as they always did.
                _cachedPid = 0;
                _cachedProcess = "";
                return "";
            }
        }
    }

    /// <summary>True when the foreground app is on the user's do-not-disturb list.</summary>
    internal static bool IsPrivilegedAppForeground()
    {
        try
        {
            var list = CoreSettings.Current.DndProcessList;
            if (list == null || list.Count == 0) return false;
            var fg = ForegroundProcessName();
            if (fg.Length == 0) return false;
            foreach (var entry in list)
                if (string.Equals(entry, fg, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        catch { return false; }
    }

    internal static bool ShouldSuppressVideos()
    {
        try { return CoreSettings.Current.DndSuppressVideos && IsPrivilegedAppForeground(); }
        catch { return false; }
    }

    internal static bool ShouldSuppressFlashes()
    {
        try { return CoreSettings.Current.DndSuppressFlashes && IsPrivilegedAppForeground(); }
        catch { return false; }
    }

    /// <summary>WPF FlashService.TriggerFlash :689: hold this scheduled flash (logged once a minute).</summary>
    internal static bool HoldsScheduledFlash()
    {
        if (!ShouldSuppressFlashes()) return false;
        LogSuppressionThrottled("flash");
        return true;
    }

    /// <summary>WPF VideoService.cs:3013: defer this scheduled video tick (logged once a minute).</summary>
    internal static bool HoldsScheduledVideo()
    {
        if (!ShouldSuppressVideos()) return false;
        LogSuppressionThrottled("scheduled video");
        return true;
    }

    internal static void LogSuppressionThrottled(string what)
    {
        try
        {
            var now = Environment.TickCount64;
            lock (Gate)
            {
                if (now < _nextLogTick) return;
                _nextLogTick = now + LogIntervalMs;
            }
            Log.Information("[DND] {What} suppressed - do-not-disturb app in foreground ({Process})", what, ForegroundProcessName());
        }
        catch { /* logging must never break a spawn path */ }
    }

    /// <summary>The settings picker's list: processes with a main window (roughly the taskbar), minus CCP.</summary>
    internal static List<string> RunningWindowedProcesses()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        string self;
        try { self = DndProcessList.Normalize(Process.GetCurrentProcess().ProcessName); }
        catch { self = ""; }
        Process[] processes;
        try { processes = Process.GetProcesses(); }
        catch (Exception ex) { Log.Debug(ex, "[DND] process enumeration failed"); return new List<string>(); }
        foreach (var p in processes)
        {
            try
            {
                if (p.MainWindowHandle == IntPtr.Zero) continue;
                var name = DndProcessList.Normalize(p.ProcessName);
                if (name.Length == 0 || name == self) continue;
                names.Add(name);
            }
            catch { /* protected or exited */ }
            finally { try { p.Dispose(); } catch { } }
        }
        return new List<string>(names);
    }
}
