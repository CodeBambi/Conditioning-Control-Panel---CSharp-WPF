using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// The only way this head opens anything outside the app: a web link, a file or a folder. Every launch
    /// asks <see cref="SandboxNet.Allows"/> first, so a CCP_USERDATA_DIR sandbox (tests, kc, render-all)
    /// never opens a real site; a refused launch is logged. <c>SandboxNetTests.EveryLaunchGoesThroughExternalOpener</c>
    /// fails when a new Process.Start(UseShellExecute) / Launcher / NavigateUri call bypasses it.
    /// </summary>
    internal static class ExternalOpener
    {
        /// <summary>Test seam for the shell half (Process.Start with UseShellExecute: xdg-open on Linux).</summary>
        internal static Func<string, bool> Shell = target =>
        {
            using var p = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        };

        /// <summary>Local paths are always allowed; anything else only by <see cref="SandboxNet.Allows"/>.</summary>
        internal static bool Allowed(string? target)
        {
            if (string.IsNullOrWhiteSpace(target)) return false;
            if (Path.IsPathRooted(target) && !target.Contains("://")) return true;
            if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && SandboxNet.Allows(uri)) return true;
            // Host only: a signed URL's query string must not reach the log.
            Log.Warning("Sandbox: refused to open {Host}", Uri.TryCreate(target, UriKind.Absolute, out var u) ? u.Host : "(not a URL)");
            return false;
        }

        /// <summary>Shell-open <paramref name="target"/> (WPF Process.Start with UseShellExecute). False when refused or failed.</summary>
        public static bool Open(string? target)
        {
            if (!Allowed(target)) return false;
            try { return Shell(target!); }
            catch (Exception ex) { Log.Warning("Could not open {Target}: {Error}", Path.IsPathRooted(target!) ? target : "link", ex.Message); return false; }
        }

        /// <summary>Open through the window's platform launcher (portal-aware), else the shell. False when refused or failed.</summary>
        public static async Task<bool> OpenAsync(TopLevel? top, string? target)
        {
            if (!Allowed(target)) return false;
            try
            {
                if (top?.Launcher is { } l && (Directory.Exists(target)
                        ? await l.LaunchDirectoryInfoAsync(new DirectoryInfo(target!))
                        : await l.LaunchUriAsync(Path.IsPathRooted(target!) ? new Uri(Path.GetFullPath(target!)) : new Uri(target!))))
                    return true;
            }
            catch (Exception ex) { Log.Debug("Launcher failed, trying the shell: {Error}", ex.Message); }
            return Open(target);
        }
    }
}
