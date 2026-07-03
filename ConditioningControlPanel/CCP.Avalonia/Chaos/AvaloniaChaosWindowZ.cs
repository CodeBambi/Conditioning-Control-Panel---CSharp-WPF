using System;
using System.Linq;
using System.Runtime.InteropServices;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.ApplicationLifetimes;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of ChaosWindowZ: shared z-order helper for chaos overlays.
/// Cross-platform topmost re-assert is limited; extended-window-style click-through
/// is platform-specific and stubbed with TODOs.
/// </summary>
internal static class AvaloniaChaosWindowZ
{
    public static bool BornTopmost => AvaloniaChaosMode.BornTopmost;

    /// <summary>Re-assert a window to the top of the topmost band without stealing focus (or, when
    /// the layer isn't pinned, demote it out of the band). Mirrors WPF ChaosWindowZ.RaiseTopmost:
    /// the re-stack is a single SetWindowPos with SWP_NOACTIVATE — no managed <c>Topmost</c> toggle.
    /// The managed flip forces a layered-window ex-style rewrite each call, which risks the
    /// topmost churn/deadlock the overlay-clickthrough contract forbids.</summary>
    public static void RaiseTopmost(global::Avalonia.Controls.Window? w)
    {
        if (w == null) return;
        try
        {
            bool topmost = BornTopmost;
            if (!OperatingSystem.IsWindows() || w.TryGetPlatformHandle() is not { } handle)
            {
                // No native handle to re-stack: fall back to the managed flag on non-Windows heads.
                w.Topmost = topmost;
                return;
            }
            if (!topmost)
            {
                w.Topmost = false;   // demote out of the topmost band (Free Desktop run)
                SetWindowPos(handle.Handle, HWND_NOTOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                return;
            }
            SetWindowPos(handle.Handle, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch { }
    }

    /// <summary>Pin a window to the top of the topmost band UNCONDITIONALLY (ignores
    /// <see cref="BornTopmost"/>). Mirrors WPF ChaosWindowZ.ForceTopmost: for dashboard trigger
    /// overlays that fire outside a chaos run, where a keep-alive singleton may carry a stale
    /// non-topmost state from a prior Free-Desktop run.</summary>
    public static void ForceTopmost(global::Avalonia.Controls.Window? w)
    {
        if (w == null) return;
        try
        {
            w.Topmost = true;
            if (OperatingSystem.IsWindows() && w.TryGetPlatformHandle() is { } handle)
                SetWindowPos(handle.Handle, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch { }
    }

    /// <summary>Re-stack only while a mandatory video is on screen.</summary>
    public static void RaiseAboveVideo(global::Avalonia.Controls.Window? w)
    {
        if (!AvaloniaChaosEnv.VideoIsPlaying) return;
        RaiseTopmost(w);
    }

    /// <summary>
    /// Bounds (DIPs) a full-screen chaos overlay should cover. Single-monitor unless
    /// DualMonitorEnabled is true.
    /// </summary>
    public static (double left, double top, double width, double height) StageBounds(bool forcePrimary = false)
    {
        var screens = GetScreens();
        if (screens == null) return (0, 0, 1920, 1080);

        bool dual = !forcePrimary && (App.Services?.GetService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>()?.Current?.DualMonitorEnabled ?? true);
        if (dual)
        {
            var all = screens.All;
            if (all.Count == 0) return (0, 0, 1920, 1080);
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var s in all)
            {
                var b = s.Bounds;
                minX = Math.Min(minX, b.X);
                minY = Math.Min(minY, b.Y);
                maxX = Math.Max(maxX, b.Right);
                maxY = Math.Max(maxY, b.Bottom);
            }
            return (minX, minY, maxX - minX, maxY - minY);
        }

        var primary = screens.Primary;
        if (primary == null) return (0, 0, 1920, 1080);
        var pb = primary.Bounds;
        return (pb.X, pb.Y, pb.Width, pb.Height);
    }

    /// <summary>
    /// Physical-pixel origin + DIP size a full-screen chaos overlay should use. In Avalonia a
    /// Window's <c>Position</c> is PHYSICAL px but <c>Width</c>/<c>Height</c> are DIPs, so the size
    /// returned by <see cref="StageBounds"/> (physical px from <c>Screens.Bounds</c>) must be
    /// divided by the target screen's scaling before it is assigned to Width/Height. This is the
    /// same convention <c>ChaosBackdropService.Build</c> uses. Assign <c>Position = (left, top)</c>
    /// and <c>Width/Height = (width, height)</c>.
    /// </summary>
    public static (double left, double top, double width, double height) StageBoundsDip(bool forcePrimary = false)
    {
        var (px, py, pw, ph) = StageBounds(forcePrimary);
        double scale = ScalingForOrigin((int)px, (int)py);
        if (scale <= 0) scale = 1.0;
        return (px, py, pw / scale, ph / scale);
    }

    /// <summary>The scaling (DPI/96) of the screen whose bounds contain the given physical-px
    /// point, falling back to the primary screen's scaling. Used to convert a physical-px stage
    /// span into the DIP Width/Height an Avalonia window expects.</summary>
    internal static double ScalingForOrigin(int x, int y)
    {
        var screens = GetScreens();
        if (screens == null) return 1.0;
        foreach (var s in screens.All)
        {
            var b = s.Bounds;
            if (x >= b.X && x < b.Right && y >= b.Y && y < b.Bottom)
                return s.Scaling > 0 ? s.Scaling : 1.0;
        }
        double sc = screens.Primary?.Scaling ?? 1.0;
        return sc > 0 ? sc : 1.0;
    }

    /// <summary>The primary screen's scaling (DPI/96), or 1.0 when unknown.</summary>
    internal static double PrimaryScaling()
    {
        double sc = GetScreens()?.Primary?.Scaling ?? 1.0;
        return sc > 0 ? sc : 1.0;
    }

    internal static global::Avalonia.Controls.Screens? GetScreens()
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } window)
        {
            return window.Screens;
        }
        return null;
    }

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
