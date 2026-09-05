using System;
using System.Diagnostics;
using System.Windows;

using ConditioningControlPanel.Services.Logging;

namespace ConditioningControlPanel.Helpers
{
    /// <summary>
    /// Opens external URLs with graceful fallbacks.
    ///
    /// A bare <c>Process.Start(url, UseShellExecute = true)</c> relies on the OS having a
    /// registered default handler for http(s). On machines with no default browser / a broken
    /// protocol association it throws <c>Win32Exception 0x800401F5</c> ("application not found"),
    /// which silently broke OAuth login for Discord/Patreon/SubscribeStar — see ccp-bugs
    /// #373 / #374 / #378 / #404 (all zh-CN machines with no default browser). We try several
    /// launch strategies and, if all fail, copy the link to the clipboard and tell the user so
    /// they can paste it manually (the OAuth callback listener keeps waiting in the meantime).
    /// </summary>
    public static class BrowserLauncher
    {
        /// <summary>
        /// Attempts to open <paramref name="url"/> in the user's browser. Returns true if any
        /// launch strategy succeeded. Never throws.
        /// </summary>
        public static bool TryOpenUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            // 1. Default shell association — works on the vast majority of machines.
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "ShellExecute failed to open {Host}; trying fallbacks", UrlLog.Host(url));
            }

            // 2. explorer.exe <url> — explorer resolves the default browser itself.
            if (TryStart("explorer.exe", url)) return true;

            // 3. cmd /c start "" "<url>"
            if (TryStart("cmd.exe", $"/c start \"\" \"{url}\"")) return true;

            // 4. rundll32 url.dll,FileProtocolHandler <url>
            if (TryStart("rundll32.exe", $"url.dll,FileProtocolHandler {url}")) return true;

            App.Logger?.Error("All browser-launch strategies failed for {Host}", UrlLog.Host(url));
            return false;
        }

        /// <summary>
        /// Opens <paramref name="url"/>; if every launch strategy fails, copies the link to the
        /// clipboard and shows the user a dialog so they can paste it manually. Returns true if
        /// the browser opened, false if it fell back to the clipboard prompt.
        /// </summary>
        public static bool OpenUrlOrPrompt(string? url, string? purpose = null)
        {
            if (TryOpenUrl(url)) return true;
            if (string.IsNullOrWhiteSpace(url)) return false;

            var dispatcher = Application.Current?.Dispatcher;
            void Prompt()
            {
                try { Clipboard.SetText(url); } catch { /* clipboard may be locked by another app */ }
                var msg = (string.IsNullOrEmpty(purpose)
                        ? "We couldn't open your web browser automatically."
                        : $"We couldn't open your web browser to {purpose}.")
                    + "\n\nThe link has been copied to your clipboard — paste it into any browser to continue:\n\n"
                    + url
                    + "\n\n(This usually means Windows has no default browser set.)";
                MessageBox.Show(msg, "Open this link in your browser",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }

            if (dispatcher == null || dispatcher.CheckAccess()) Prompt();
            else dispatcher.Invoke(Prompt);
            return false;
        }

        private static bool TryStart(string fileName, string arguments)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Fallback browser launcher {File} failed", fileName);
                return false;
            }
        }
    }
}
