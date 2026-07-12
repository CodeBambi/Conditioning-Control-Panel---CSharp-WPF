using System.Runtime.InteropServices;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Windows-only low-level mouse hook (WH_MOUSE_LL) for the Avalonia head.
/// Reports global left/right button down events to the bubble service and other consumers.
/// On non-Windows platforms the events never fire.
///
/// Ownership: this is a DI SINGLETON shared by several consumers (flash clicks, bubble
/// pops, chaos ripple). <see cref="Install"/>/<see cref="Uninstall"/> are therefore
/// REFERENCE-COUNTED: the native hook is set on the 0-&gt;1 install and removed on the
/// 1-&gt;0 uninstall. Consumers must strictly pair their calls; an unpaired Uninstall is a
/// logged no-op instead of tearing the hook away from every other consumer (the lot-3
/// uninstall-collision bug: stopping a non-clickable flash session killed bubble popping).
///
/// Coordinates: <see cref="HookPoint"/> carries MSLLHOOKSTRUCT.pt — PHYSICAL screen
/// pixels. Compositor layers store physical virtual-desktop px (IAvaloniaLayer contract),
/// so consumers hit-test these values directly without DPI conversion.
///
/// Callback discipline: events are raised synchronously inside the hook callback, which
/// serializes system-wide mouse input while it runs. Handlers must be near-free (snapshot
/// reads / Dispatcher.Post only) — heavy work risks Windows silently removing the hook
/// (LowLevelHooksTimeout).
/// </summary>
public sealed class AvaloniaMouseHook : IMouseHook
{
    private readonly ILogger<AvaloniaMouseHook>? _logger;
    private readonly ConditioningControlPanel.Core.Services.Compositor.CaptureMaskState? _captureMaskState;
    private readonly object _sync = new();
    private int _refCount;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    private LowLevelMouseProc? _mouseProc;
    private IntPtr _mouseHook = IntPtr.Zero;

    public AvaloniaMouseHook(
        ILogger<AvaloniaMouseHook>? logger = null,
        ConditioningControlPanel.Core.Services.Compositor.CaptureMaskState? captureMaskState = null)
    {
        _logger = logger;
        _captureMaskState = captureMaskState;
    }

    public event EventHandler<HookPoint>? LeftButtonDown;
    public event EventHandler<HookPoint>? RightButtonDown;
    public event EventHandler<HookPoint>? RightButtonUp;
    public event EventHandler<HookPoint>? LeftButtonUp;

    public void Install()
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger?.LogInformation("Low-level mouse hooks are only supported on Windows in the Avalonia head.");
            return;
        }

        lock (_sync)
        {
            _refCount++;
            if (_mouseHook != IntPtr.Zero)
                return; // already installed for another consumer

            try
            {
                _mouseProc = MouseHookCallback;
                var moduleHandle = GetModuleHandle(null);
                _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
                if (_mouseHook == IntPtr.Zero)
                {
                    var error = Marshal.GetLastWin32Error();
                    _logger?.LogWarning("SetWindowsHookEx(WH_MOUSE_LL) failed with error {Error}", error);
                    _mouseProc = null;
                }
                else
                {
                    _logger?.LogInformation("Low-level mouse hook installed");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to install low-level mouse hook");
                _mouseProc = null;
            }
        }
    }

    public void Uninstall()
    {
        if (!OperatingSystem.IsWindows()) return;

        lock (_sync)
        {
            if (_refCount <= 0)
            {
                // Unpaired uninstall — do NOT remove a hook another consumer still needs.
                _logger?.LogDebug("AvaloniaMouseHook.Uninstall called with no matching Install; ignored");
                return;
            }

            _refCount--;
            if (_refCount > 0)
                return; // other consumers still depend on the hook

            if (_mouseHook != IntPtr.Zero)
            {
                try
                {
                    UnhookWindowsHookEx(_mouseHook);
                    _logger?.LogInformation("Low-level mouse hook uninstalled");
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to uninstall low-level mouse hook");
                }
                _mouseHook = IntPtr.Zero;
            }
            _mouseProc = null;
        }
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // PER-REGION CLICK-THROUGH (team review 2026-07-09, overlay-clickthrough skill). The
        // compositor publishes a per-frame CAPTURE MASK (union of every non-ambient active
        // layer's painted region). A click whose point falls INSIDE the mask is SWALLOWED
        // (return non-zero without calling CallNextHookEx) so it lands on the capturing layer
        // and never reaches the app behind; a click OUTSIDE the mask (ambient-only region or
        // bare desktop) is PASSED via CallNextHookEx. Ambient layers (filter/spiral/blink) never
        // contribute to the mask, so their regions always pass input.
        //
        // Consumer events are raised BEFORE the swallow decision so the bubble-pop / flash-pop
        // handlers still see the click (they marshal to the UI thread and hit-test the layer).
        // The hold-to-defuse exception is enforced at the MASK: a live chaos bubble is NOT in
        // the mask (BubbleLayer excludes HoldToDefuse items), so its click passes through to
        // GetAsyncKeyState exactly as WPF's GlobalMouseHook does (WPF parity).
        bool swallow = false;
        if (nCode >= 0 && lParam != IntPtr.Zero)
        {
            // Filter on wParam BEFORE marshaling the struct (WPF GlobalMouseHook parity):
            // WM_MOUSEMOVE arrives at up to 1000Hz and must not pay the PtrToStructure cost.
            var msg = wParam.ToInt64();
            if (msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP || msg == WM_RBUTTONDOWN || msg == WM_RBUTTONUP)
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                var pt = new HookPoint(info.pt.X, info.pt.Y);

                try
                {
                    switch (msg)
                    {
                        case WM_LBUTTONDOWN:
                            LeftButtonDown?.Invoke(this, pt);
                            break;
                        case WM_RBUTTONDOWN:
                            RightButtonDown?.Invoke(this, pt);
                            break;
                        case WM_RBUTTONUP:
                            RightButtonUp?.Invoke(this, pt);
                            break;
                        case WM_LBUTTONUP:
                            LeftButtonUp?.Invoke(this, pt);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Exception in low-level mouse hook handler");
                }

                // Swallow only button events whose point falls inside the compositor's capture
                // mask. The mask is an immutable snapshot read lock-free; Contains is a cheap
                // linear scan with no allocation, safe under the LowLevelHooksTimeout budget.
                if (_captureMaskState != null && _captureMaskState.CurrentMask.Contains(pt.X, pt.Y))
                {
                    swallow = true;
                    _logger?.LogDebug("MouseHook: SWALLOW {Msg} at ({X},{Y}) — inside capture mask", msg, pt.X, pt.Y);
                }
                else
                {
                    _logger?.LogDebug("MouseHook: PASS {Msg} at ({X},{Y}) — ambient/desktop region", msg, pt.X, pt.Y);
                }
            }
        }

        // Swallow = return non-zero WITHOUT calling CallNextHookEx (WPF GlobalMouseHook parity:
        // return the 1 from the handled-hit callback). This prevents both the rest of the hook
        // chain and the target window from seeing the click. A passed click falls through to
        // CallNextHookEx so it reaches the app behind normally.
        return swallow ? new IntPtr(1) : CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
