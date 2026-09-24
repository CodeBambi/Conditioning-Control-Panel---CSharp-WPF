using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Paints the LibVLC VideoView's letterbox black (#1258).
    ///
    /// LibVLCSharp.WPF 3.8's VideoHwndHost is a plain Win32 "static" control. LibVLC places its own
    /// video child window at the letterboxed destination rect inside it, so whatever the static
    /// shows around that rect is the letterbox. A static control asks its parent for a brush with
    /// WM_CTLCOLORSTATIC, and with nobody answering, Windows hands back the system button face:
    /// near white. That is the white bars (and the "small black box top left" is the video child
    /// before its first resize). The WPF Background on the window and the VideoView never reach
    /// that native surface. Answering the message with the stock black brush does.
    /// </summary>
    internal static class VideoHostBlackFill
    {
        internal const int WM_CTLCOLORSTATIC = 0x0138;
        private const int BLACK_BRUSH = 4;

        [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int fnObject);
        [DllImport("gdi32.dll")] private static extern uint SetBkColor(IntPtr hdc, uint crColor);

        /// <summary>True for the one message this hook answers.</summary>
        internal static bool Handles(int msg) => msg == WM_CTLCOLORSTATIC;

        /// <summary>Hooks <paramref name="win"/> so every static child it hosts paints black.
        /// Safe to call before the window has a handle.</summary>
        public static void Attach(Window win)
        {
            void Wire()
            {
                try
                {
                    var hwnd = new WindowInteropHelper(win).Handle;
                    if (hwnd != IntPtr.Zero) HwndSource.FromHwnd(hwnd)?.AddHook(Hook);
                }
                catch (Exception ex) { App.Logger?.Debug("VideoHostBlackFill: hook failed - {E}", ex.Message); }
            }

            if (new WindowInteropHelper(win).Handle != IntPtr.Zero) Wire();
            else win.SourceInitialized += (_, _) => Wire();
        }

        private static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (!Handles(msg)) return IntPtr.Zero;
            try { SetBkColor(wParam, 0); } catch { /* the brush is what matters */ }
            handled = true;
            return GetStockObject(BLACK_BRUSH);
        }
    }
}
