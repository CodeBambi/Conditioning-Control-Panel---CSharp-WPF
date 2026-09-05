using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// SETTINGS ▸ GENERAL ▸ "Discreet app icon". Swaps the two things a passer-by actually
    /// sees in the taskbar, alt-tab and the tray: the window ICON and the window TITLE.
    ///
    /// <para>Asked for in #general (2026-09-02) - AhrySiss did not want the app recognisable
    /// on screen, Wobberjockey asked for "a generic looking icon".</para>
    ///
    /// <para><b>Scope, on purpose.</b> This only repaints the RUNNING process. The Start-menu
    /// and desktop shortcuts carry the icon Inno Setup baked into them at install time, and
    /// the exe's own <c>ApplicationIcon</c> is a build-time resource - neither can be swapped
    /// from here, so neither is promised.</para>
    ///
    /// <para><b>Off is the default and off must be byte-identical to before.</b> Turning it
    /// off assigns <c>Icon = null</c>, which is WPF's "fall back to the exe icon" - not a
    /// second, remembered brand icon that could drift from the real one.</para>
    /// </summary>
    internal static class DiscreetIcon
    {
        /// <summary>The window title MainWindow.xaml ships with, restored when the toggle is off.</summary>
        internal const string BrandTitle = "Conditioning Control Panel";

        private const string NeutralIconUri = "pack://application:,,,/Resources/app_discreet.ico";

        private static ImageSource? _neutral;

        internal static bool Enabled => App.Settings?.Current?.DiscreetAppIcon == true;

        /// <summary>Title for the current state. Localized only in the discreet direction.</summary>
        internal static string Title =>
            Enabled ? Localization.Loc.Get("title_discreet_window") : BrandTitle;

        /// <summary>Flat grey rounded square with three white bars. Decoded once, then frozen.</summary>
        private static ImageSource? Neutral()
        {
            if (_neutral != null) return _neutral;
            try
            {
                // Decoded through the .ico decoder and loaded EAGERLY: WPF's icon helper looks at
                // the frame's Decoder to pick the right size per surface (16px in the title bar,
                // 32px in alt-tab), so handing it the whole .ico beats handing it one bitmap.
                var frame = BitmapFrame.Create(new Uri(NeutralIconUri, UriKind.Absolute),
                                               BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                if (frame.CanFreeze) frame.Freeze();
                _neutral = frame;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DiscreetIcon: neutral icon unavailable ({E})", ex.Message);
            }
            return _neutral;
        }

        /// <summary>
        /// Applies the current setting to a window and (optionally) the tray icon. Idempotent
        /// and cheap, so it is safe on startup, on toggle, and on any later re-entry.
        /// </summary>
        internal static void Apply(Window? window, TrayIconService? tray = null)
        {
            try
            {
                var on = Enabled;
                if (window != null)
                {
                    // null => WPF falls back to the exe's ApplicationIcon, i.e. the real one.
                    window.Icon = on ? Neutral() : null;
                    window.Title = Title;
                }
                tray?.ApplyDiscreetSkin(on, Title);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DiscreetIcon.Apply: {E}", ex.Message);
            }
        }

        /// <summary>The tray's icon for the current state, or null to keep whatever it has.</summary>
        internal static System.Drawing.Icon? TrayIcon()
        {
            try
            {
                var info = Application.GetResourceStream(new Uri(NeutralIconUri, UriKind.Absolute));
                return info == null ? null : new System.Drawing.Icon(info.Stream);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DiscreetIcon.TrayIcon: {E}", ex.Message);
                return null;
            }
        }
    }
}
