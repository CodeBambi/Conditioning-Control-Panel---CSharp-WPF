// "Is anyone at the desk": seconds since the last keyboard or pointer input, or -1 when nobody can
// tell. Windows answers through ActivityIdle.IdleSecondsProvider (GetLastInputInfo, seeded by
// ProgressionHead). Linux had no answer at all, so Natasha's red flash (dealt only to a player who is
// provably at the desk) was never dealt there (owner, 10 Oct 2026: "Add an X11 idle probe").
//
// Linux: the XScreenSaver extension (libXss, XScreenSaverQueryInfo) through X11ActiveWindow, which
// loads it defensively. A missing library, no display, or a Wayland session without XWayland reads as
// UNKNOWN, and unknown keeps the old behaviour: never deal. Under XWayland the counter only sees input
// that went to X windows, so it can read MORE idle than the truth; that errs toward not dealing.
//
// Deliberately not wired into ActivityIdle (the passive XP idle gate): an idle counter that over-reads
// on a Wayland session would withhold XP from a player who is at the desk. That stays a separate call.

using System;

namespace ConditioningControlPanel.Avalonia.Platform;

internal static class InputIdleProbe
{
    /// <summary>The raw X11 read in milliseconds, -1 = unknown. Tests put a fake here; nothing in a
    /// test may reach the native library.</summary>
    internal static Func<long> X11IdleMilliseconds = X11ActiveWindow.IdleMilliseconds;

    /// <summary>Which desk this is. Tests name one.</summary>
    internal static Func<bool> IsLinux = OperatingSystem.IsLinux;

    /// <summary>Milliseconds from the X server to whole seconds; anything unreadable is -1.</summary>
    internal static int SecondsFrom(long milliseconds) =>
        milliseconds < 0 ? -1 : (int)Math.Min(int.MaxValue, milliseconds / 1000);

    /// <summary>Seconds since the last input on an X11 desk, or -1 (unknown). Never throws.</summary>
    internal static int X11Seconds()
    {
        try { return SecondsFrom(X11IdleMilliseconds()); }
        catch { return -1; }
    }

    /// <summary>Seconds since the last input on this desk, or -1 when it cannot be told: the seeded
    /// provider where there is one (Windows), the X11 probe on Linux, unknown anywhere else. A
    /// provider that throws, or answers below zero, is unknown.</summary>
    internal static int DeskIdleSeconds()
    {
        try
        {
            if (ActivityIdle.IdleSecondsProvider is { } seeded)
            {
                int s = seeded();
                return s < 0 ? -1 : s;
            }
            return IsLinux() ? X11Seconds() : -1;
        }
        catch { return -1; }
    }
}
