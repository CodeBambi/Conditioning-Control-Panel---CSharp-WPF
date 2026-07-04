using System;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Core.Services.Auth;

/// <summary>
/// Opens external URLs with graceful cross-platform fallbacks.
/// </summary>
/// <remarks>
/// A bare shell-execute can fail on machines with no default browser/protocol association
/// (Win32Exception 0x800401F5 on Windows, or no xdg-open/open on Linux/macOS), silently
/// breaking OAuth login. This helper delegates the actual launch to <see cref="IBrowserHost"/>
/// and, if that fails, copies the link to the clipboard and tells the user so they can paste
/// it manually while the OAuth callback listener keeps waiting.
/// </remarks>
public static class BrowserLauncher
{
    /// <summary>
    /// Attempts to open <paramref name="url"/> via <paramref name="browserHost"/>.
    /// Returns true if the launch was requested without throwing; never throws.
    /// </summary>
    public static async Task<bool> TryOpenUrlAsync(IBrowserHost browserHost, string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (browserHost is null) return false;

        try
        {
            // OAuth and external links must open in the OS/system browser, NEVER the
            // invisible embedded WebView2 (embedded hosts override OpenExternalAsync to
            // shell-launch; system-browser hosts inherit the NavigateAsync default).
            await browserHost.OpenExternalAsync(new Uri(url));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Opens <paramref name="url"/>; if the browser host fails, copies the link to the
    /// clipboard and shows the user a dialog so they can paste it manually. Returns true if
    /// the browser opened, false if it fell back to the clipboard prompt.
    /// </summary>
    public static async Task<bool> OpenUrlOrPromptAsync(
        IBrowserHost browserHost,
        IDialogService dialogService,
        Func<string, Task>? setClipboardAsync,
        string? url,
        string? purpose = null)
    {
        if (await TryOpenUrlAsync(browserHost, url)) return true;
        if (string.IsNullOrWhiteSpace(url)) return false;

        try
        {
            if (setClipboardAsync != null)
                await setClipboardAsync(url);
        }
        catch
        {
            // Clipboard may be locked by another app; the dialog still shows the URL.
        }

        var message = (string.IsNullOrEmpty(purpose)
                ? "We couldn't open your web browser automatically."
                : $"We couldn't open your web browser to {purpose}.")
            + "\n\nThe link has been copied to your clipboard — paste it into any browser to continue:\n\n"
            + url
            + "\n\n(This usually means no default browser is set.)";

        await dialogService.ShowMessageAsync("Open this link in your browser", message, DialogSeverity.Info);
        return false;
    }
}
