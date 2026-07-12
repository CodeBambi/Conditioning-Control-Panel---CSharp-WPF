using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Desktop.macOS.Platform;

/// <summary>
/// macOS stub browser host. Visual WebKit embedding is not implemented yet;
/// links open in the system default browser.
/// </summary>
public sealed class WebKitBrowserHost : IBrowserHost
{
    public Task NavigateAsync(Uri url)
    {
        var urlString = url.ToString();
        if (!TryStart("open", urlString))
            Process.Start(new ProcessStartInfo { FileName = urlString, UseShellExecute = true });
        return Task.CompletedTask;
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
        catch
        {
            return false;
        }
    }

    public Task<string> ExecuteScriptAsync(string script) => Task.FromResult(string.Empty);

    public event EventHandler<string>? TitleChanged { add { } remove { } }
    public event EventHandler<Uri>? Navigated { add { } remove { } }

    public bool IsFullscreen => false;
    public event EventHandler<bool>? FullscreenChanged { add { } remove { } }

    // object? (not Control?): a covariant return does not implement the interface member and
    // interface dispatch would silently use the IBrowserHost default (always null).
    public object? CreateBrowserControl() => null;
}
