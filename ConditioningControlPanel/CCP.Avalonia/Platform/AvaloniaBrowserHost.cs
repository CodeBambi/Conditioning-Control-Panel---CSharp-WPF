using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Browser host that opens the system default browser. Cross-platform fallback.
/// </summary>
public sealed class AvaloniaBrowserHost : IBrowserHost
{
    public Task NavigateAsync(Uri url)
    {
        OpenWithSystemDefault(url.ToString());
        return Task.CompletedTask;
    }

    private static void OpenWithSystemDefault(string url)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // explorer.exe receives the URL as a single argument with no shell involved, so it
            // is the safe primary path even when no default browser is registered
            // (a bare ShellExecute can throw Win32Exception 0x800401F5 in that case).
            if (TryStart("explorer.exe", url)) return;

            // R1: the old cmd.exe fallback built "/c start \"\" \"{url}\"" from an unvalidated URL,
            // letting metacharacters (& | ^ %) inject shell commands. That branch is removed.
            // Only fall back to a shell-interpreted launcher for URLs we have validated as safe.
            if (IsSafeHttpUrl(url) && TryStart("rundll32.exe", $"url.dll,FileProtocolHandler {url}")) return;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (TryStart("xdg-open", url)) return;
            if (TryStart("sensible-browser", url)) return;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (TryStart("open", url)) return;
        }

        // Final platform-agnostic attempt (ShellExecute via the default handler), validated URLs only.
        if (IsSafeHttpUrl(url))
            TryStart(url, useShellExecute: true);
    }

    /// <summary>
    /// R1: gate shell-interpreted launch fallbacks to safe browser URLs only — an absolute
    /// http/https URL that is free of command-injection metacharacters (" &amp; | ^ % CR LF) —
    /// so an untrusted URL can never be smuggled onto a command line.
    /// </summary>
    private static bool IsSafeHttpUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (url.IndexOfAny(new[] { '"', '&', '|', '^', '%', '\r', '\n' }) >= 0)
            return false;

        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static bool TryStart(string fileName, string arguments)
    {
        return TryStart(fileName, arguments, false);
    }

    private static bool TryStart(string fileName, bool useShellExecute)
    {
        return TryStart(fileName, string.Empty, useShellExecute);
    }

    private static bool TryStart(string fileName, string arguments, bool useShellExecute)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = useShellExecute,
                CreateNoWindow = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Task<string> ExecuteScriptAsync(string script)
        => Task.FromResult(string.Empty);

    public event EventHandler<string>? TitleChanged { add { } remove { } }
    public event EventHandler<Uri>? Navigated { add { } remove { } }

    public bool IsFullscreen => false;
    public event EventHandler<bool>? FullscreenChanged { add { } remove { } }

    public Control? CreateBrowserControl() => null;
}
