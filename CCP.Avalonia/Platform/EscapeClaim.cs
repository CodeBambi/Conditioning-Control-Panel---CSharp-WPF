// PORTED from ConditioningControlPanel/Services/Safety/EscapeClaim.cs + MainWindow.xaml.cs
// EscapeTakenBySurface (:977), bug hunt 2026-09-29 TAB-8 / DESK-3: an Escape aimed at a CCP surface
// that drops or closes on it (the friends drawer, Circe's Tab price box, the dashboard click-choice
// popup) is that surface's, not a panic press. Same Core rule (PanicPolicy.SurfaceTakesEscape).
// The hook asks from its own thread here, so the "keyboard sits in a marked surface" answer is a
// snapshot the UI thread keeps fresh on every focus change, never a tree walk off-thread.
// ponytail: WPF marks Circe's Tab price box (ChasterTabView.PriceEdit.cs:100) and the dashboard's
// ClickChoiceBody (SettingsTabView.xaml.cs:18). Their owning lanes add `EscapeClaim.Mark(x)` (and
// `EscapeClaim.Taken()` in their Escape handler) when those surfaces are ported; the friends drawer is
// marked by type below. X11 has no claim yet (X11PanicKey raises no per-key events).

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using ConditioningControlPanel.Services.Safety;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

internal static class EscapeClaim
{
    internal static readonly AttachedProperty<bool> DropsOnEscapeProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("DropsOnEscape", typeof(EscapeClaim));

    /// <summary>WPF OnItsWayFor: how long a taken press may take to reach its surface.</summary>
    internal static readonly TimeSpan OnItsWayFor = TimeSpan.FromSeconds(2);

    private static readonly HashSet<Type> MarkedTypes = new();
    private static long _takenAtTicks;          // 0 = none; UTC ticks
    private static volatile bool _keyboardInSurface;
    private static bool _tracking;

    /// <summary>Marks an element whose own Escape handler drops or closes it.</summary>
    internal static void Mark(Control surface) => surface.SetValue(DropsOnEscapeProperty, true);

    /// <summary>Marks every instance of a control type (a surface owned by another lane's file).</summary>
    internal static void MarkType(Type t) { lock (MarkedTypes) MarkedTypes.Add(t); }

    /// <summary>True when <paramref name="node"/> sits in a marked surface (UI thread).</summary>
    internal static bool InASurface(object? node)
    {
        for (var hops = 0; node != null && hops < 256; hops++)
        {
            if (node is AvaloniaObject ao && ao.GetValue(DropsOnEscapeProperty)) return true;
            lock (MarkedTypes) if (MarkedTypes.Contains(node.GetType())) return true;
            object? up = node is Visual v ? v.GetVisualParent() : null;
            up ??= (node as ILogical)?.LogicalParent;
            node = up;
        }
        return false;
    }

    /// <summary>Starts the focus snapshot (UI thread, once, desktop path only).</summary>
    internal static void StartTracking()
    {
        if (_tracking) return;
        _tracking = true;
        // e.Source, not the sender: the class handler runs once per element on the route, and an
        // ancestor above the surface would overwrite the answer.
        InputElement.GotFocusEvent.AddClassHandler<InputElement>((_, e) => _keyboardInSurface = InASurface(e.Source),
            RoutingStrategies.Bubble, handledEventsToo: true);
        InputElement.LostFocusEvent.AddClassHandler<InputElement>((_, e) =>
        {
            var now = TopLevel.GetTopLevel(e.Source as Visual)?.FocusManager?.GetFocusedElement();
            _keyboardInSurface = now != null && InASurface(now);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        // WPF surfaces call Taken() from their own Escape handler; one class handler does it for all.
        InputElement.KeyDownEvent.AddClassHandler<InputElement>((_, e) =>
        {
            if (e.Key == Key.Escape && InASurface(e.Source)) Taken();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>The snapshot the hook thread reads.</summary>
    internal static bool KeyboardInASurface => _keyboardInSurface;

    /// <summary>Test seam for the snapshot.</summary>
    internal static void SetKeyboardInSurfaceForTest(bool v) => _keyboardInSurface = v;

    internal static void Claimed(DateTime nowUtc) => System.Threading.Interlocked.Exchange(ref _takenAtTicks, nowUtc.Ticks);

    /// <summary>A surface's own Escape handler ran: the press it was left arrived.</summary>
    internal static void Taken() => System.Threading.Interlocked.Exchange(ref _takenAtTicks, 0);

    internal static bool OnItsWay(DateTime nowUtc)
    {
        var t = System.Threading.Interlocked.Read(ref _takenAtTicks);
        return t != 0 && nowUtc.Ticks >= t && nowUtc.Ticks - t < OnItsWayFor.Ticks;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    /// <summary>WPF EscapeTakenBySurface, from the hook thread: reads only. True = leave this Escape to
    /// the surface with the keyboard. Any failure answers false: a panic press is never lost to this.</summary>
    internal static bool TakenBySurface(bool panicKeyEnabled, string? panicKey, bool lockCardOpen, DateTime nowUtc, bool? ccpInFront = null)
    {
        try
        {
            bool front = ccpInFront ?? CcpInFront();
            if (!PanicPolicy.SurfaceTakesEscape(panicKeyEnabled, panicKey, lockCardOpen, front,
                    KeyboardInASurface, OnItsWay(nowUtc)))
                return false;
            Claimed(nowUtc);
            Log.Information("Escape left to the surface with the keyboard (it drops an edit or closes a popup) - not a panic press");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Escape claim check failed");
            return false;
        }
    }

    private static bool CcpInFront()
    {
        if (!OperatingSystem.IsWindows()) return false;
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        GetWindowThreadProcessId(fg, out var pid);
        return pid != 0 && pid == (uint)Environment.ProcessId;
    }
}
