using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

// NAMESPACE TRAP: see the note in EmiDeskWindow.Fx.cs. Flat ConditioningControlPanel, always.
namespace ConditioningControlPanel;

/// <summary>
/// Keeps her in the topmost band (ccp-bugs #1171). Repairs a lost WS_EX_TOPMOST only; see
/// <see cref="EmiTopmostRule"/> for why a buried-but-still-topmost window is not touched.
/// </summary>
public partial class EmiDeskWindow
{
    private DispatcherTimer? _topmostWatch;

    private void StartTopmostWatch()
    {
        if (_topmostWatch != null) return;
        _topmostWatch = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(EmiTopmostRule.PollMs)
        };
        _topmostWatch.Tick += OnTopmostWatchTick;
        _topmostWatch.Start();
        Closed += (_, _) =>
        {
            try { _topmostWatch?.Stop(); } catch { /* already dead */ }
            _topmostWatch = null;
        };
    }

    private void OnTopmostWatchTick(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            if (!EmiTopmostRule.NeedsRepair(IsVisible, Topmost, ex)) return;
            SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0,
                SwpNoSize | SwpNoMove | SwpNoActivate | SwpNoOwnerZOrder);
            Log.Information("[EmiDesk] lost topmost, put back");
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] topmost watch tick failed"); }
    }

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
