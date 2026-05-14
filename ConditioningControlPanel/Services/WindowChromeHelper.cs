using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ConditioningControlPanel.Services
{
    // Tints the OS title bar (non-client area) to match the app's dark theme.
    // Without this, WPF windows render the default Windows title bar — white
    // on a light-theme system — which clashes with our dark window body.
    // Requires Windows 10 1809+ for dark mode and Windows 11 22000+ for the
    // explicit color attributes; older systems silently ignore the calls.
    internal static class WindowChromeHelper
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_BORDER_COLOR  = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR    = 36;

        // COLORREF is 0x00BBGGRR. Mirrors Resources/Theme/Colors.xaml:
        //   DarkerBg     #121220 → 0x00201212 (matches window body bg)
        //   DeeperAccent #7B5CFF → 0x00FF5C7B (violet edge)
        //   TextLight    #F0F0F5 → 0x00F5F0F0
        private const int CaptionColor = 0x00201212;
        private const int BorderColor  = 0x00FF5C7B;
        private const int TextColor    = 0x00F5F0F0;

        // Apply on `window`. Safe to call before the hwnd exists — the helper
        // hooks SourceInitialized and retries. Idempotent.
        public static void ApplyDarkTitleBar(Window window)
        {
            if (window == null) return;
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                window.SourceInitialized += OnSourceInitialized;
                return;
            }
            Apply(hwnd);
        }

        private static void OnSourceInitialized(object? sender, EventArgs e)
        {
            if (sender is Window w)
            {
                w.SourceInitialized -= OnSourceInitialized;
                var hwnd = new WindowInteropHelper(w).Handle;
                if (hwnd != IntPtr.Zero) Apply(hwnd);
            }
        }

        private static void Apply(IntPtr hwnd)
        {
            try
            {
                int dark = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

                int caption = CaptionColor;
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));

                int text = TextColor;
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));

                int border = BorderColor;
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
            }
            catch
            {
                // Older Windows silently ignore unknown DWM attributes; only
                // genuine HRESULT failures land here and they're cosmetic.
            }
        }

        // Re-activates the Owner on Closed. WPF normally does this for you,
        // but windows hosting native HWNDs (WebView2, LibVLC VideoView) race
        // the owner-restore: when the native HWND is destroyed mid-close,
        // the OS reactivates whichever window was foreground BEFORE this
        // owned window opened — often another app, not the MainWindow.
        // We defer to dispatcher-idle so the call lands after the native
        // teardown is fully done; firing during the synchronous Closed
        // callback gets overwritten by the OS.
        public static void RestoreOwnerOnClose(Window window)
        {
            if (window == null) return;
            window.Closed += OnClosedRestoreOwner;
        }

        private static void OnClosedRestoreOwner(object? sender, EventArgs e)
        {
            if (sender is not Window w) return;
            w.Closed -= OnClosedRestoreOwner;
            var owner = w.Owner;
            if (owner == null) return;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (owner.WindowState == WindowState.Minimized) return;
                    if (!owner.IsLoaded) return;
                    owner.Activate();
                }
                catch { }
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }
}
