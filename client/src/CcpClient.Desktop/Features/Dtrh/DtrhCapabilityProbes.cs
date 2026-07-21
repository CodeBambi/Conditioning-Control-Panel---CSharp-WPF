using System.Runtime.InteropServices;
using CcpClient.Desktop.Capabilities;
using Microsoft.Win32;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// The DTRH host's capability probes (runtime-capability-contract; dtrh-admission.md §5).
/// Mechanism verified against the Avalonia.Controls.WebView 12.0.1 binary itself
/// (spine-tasks/SP-023 record Step 2): on Windows the package locates the WebView2 runtime
/// via the EdgeUpdate ClientState registry key, then loads the runtime's
/// EmbeddedBrowserWebView.dll and resolves its CreateWebViewEnvironmentWithOptionsInternal
/// export; on Linux it dlopens libwebkit2gtk-4.1 + libgtk-3. The probes exercise exactly
/// those dependency paths — an Available means the dependency loads for real; RENDERING is
/// claimed only by the headed boot matrix, never by these probes (detail strings say so).
/// No P/Invoke on WebView2Loader.dll: the package does not use it (pre-approach consult's
/// false-negative warning, confirmed by the binary's own DllImport table).
/// </summary>
public static class DtrhCapabilityProbes
{
    /// <summary>Embedded WebView surface (Windows = WebView2 NativeWebView, §5).</summary>
    public const string EmbeddedCapability = "dtrh-webview-embedded";

    /// <summary>NativeWebDialog surface (Linux X11 = WebKitGTK 4.1 dedicated toplevel, §5).</summary>
    public const string DialogCapability = "dtrh-web-dialog";

    // The package's own discovery key (verified in the 12.0.1 binary's string table):
    // Software\Microsoft\EdgeUpdate\ClientState\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}.
    private const string WebView2ClientStateKey =
        @"Software\Microsoft\EdgeUpdate\ClientState\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    public static CapabilityState ProbeEmbedded()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Verified-absent evidence, not an OS guess: SP-011 L4 (embedded loads but
            // never presents on WSLg/X11; adapter declares NativeDialog-only scenarios)
            // + admission §2 (WPE unpackaged on Ubuntu 26.04).
            return new CapabilityState.Unavailable(new CapabilityReason(
                "unsupported-platform",
                "embedded WebView is the Windows (WebView2) shape per admission §5; on Linux the embedded "
                + "adapter never presents on X11/WSLg (SP-011 L4) and WPE is unpackaged (admission §2)"));
        }

        try
        {
            var runtimeDir = FindWebView2Runtime();
            if (runtimeDir is null)
            {
                return new CapabilityState.DependencyMissing("Microsoft Edge WebView2 Runtime",
                    new CapabilityReason("dependency-missing",
                        "no WebView2 runtime registered under EdgeUpdate ClientState (HKLM/HKCU); "
                        + "installer: https://developer.microsoft.com/en-us/microsoft-edge/webview2/"));
            }

            // Runtime layout (observed 2026-07-21, runtime 150.0.4078.83): the loader dll
            // lives in the EBWebView/<arch> subdir, not at the version root.
            var arch = Environment.Is64BitProcess ? "x64" : "x86";
            var dll = Path.Combine(runtimeDir, "EBWebView", arch, "EmbeddedBrowserWebView.dll");
            if (!File.Exists(dll))
            {
                return new CapabilityState.DependencyMissing("Microsoft Edge WebView2 Runtime",
                    new CapabilityReason("dependency-missing", $"EmbeddedBrowserWebView.dll absent at {dll}"));
            }

            // Exercise the exact load the package performs (12.0.1 binary evidence).
            var handle = NativeLibrary.Load(dll);
            try
            {
                if (!NativeLibrary.TryGetExport(handle, "CreateWebViewEnvironmentWithOptionsInternal", out _))
                {
                    return new CapabilityState.Unavailable(new CapabilityReason("dependency-incompatible",
                        "EmbeddedBrowserWebView.dll loaded but CreateWebViewEnvironmentWithOptionsInternal is not exported"));
                }
            }
            finally
            {
                NativeLibrary.Free(handle);
            }

            return new CapabilityState.Available(
                "WebView2 runtime loads and exports the environment factory (the exact load "
                + "Avalonia.Controls.WebView 12.0.1 performs); rendering claimed only by the headed boot matrix");
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or IOException)
        {
            return new CapabilityState.Unavailable(new CapabilityReason(
                "io-failure", $"WebView2 runtime load failed: {ex.GetType().Name}: {ex.Message}"));
        }
    }

    public static CapabilityState ProbeDialog()
    {
        if (!OperatingSystem.IsLinux())
        {
            return new CapabilityState.Unavailable(new CapabilityReason(
                "unsupported-platform",
                "NativeWebDialog (WebKitGTK) is the Linux X11 shape per admission §5; the admitted "
                + "Windows shape is the embedded WebView2 surface"));
        }

        try
        {
            // The package's own dlopen list (12.0.1 binary string table): webkit2gtk 4.1
            // then 4.0; gtk-3. §2 pins the 4.1 stack on Ubuntu 26.04.
            if (!TryLoadAny(out var webkitName, "libwebkit2gtk-4.1.so.0", "libwebkit2gtk-4.1.so",
                    "libwebkit2gtk-4.0.so.37", "libwebkit2gtk-4.0.so"))
            {
                return new CapabilityState.DependencyMissing("libwebkit2gtk-4.1-0",
                    new CapabilityReason("dependency-missing",
                        "WebKitGTK not loadable; install: apt install libwebkit2gtk-4.1-0 libgtk-3-0t64 (admission §2)"));
            }

            if (!TryLoadAny(out var gtkName, "libgtk-3.so.0", "libgtk-3.so"))
            {
                return new CapabilityState.DependencyMissing("libgtk-3-0t64",
                    new CapabilityReason("dependency-missing",
                        "GTK 3 not loadable; install: apt install libwebkit2gtk-4.1-0 libgtk-3-0t64 (admission §2)"));
            }

            return new CapabilityState.Available(
                $"{webkitName} + {gtkName} dlopen successfully (the exact sonames Avalonia.Controls.WebView "
                + "12.0.1 loads); dialog rendering claimed only by headed session facts");
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or IOException)
        {
            return new CapabilityState.Unavailable(new CapabilityReason(
                "io-failure", $"WebKitGTK load failed: {ex.GetType().Name}: {ex.Message}"));
        }
    }

    private static bool TryLoadAny(out string loadedName, params string[] names)
    {
        foreach (var name in names)
        {
            if (NativeLibrary.TryLoad(name, out var handle))
            {
                NativeLibrary.Free(handle);
                loadedName = name;
                return true;
            }
        }

        loadedName = names[0];
        return false;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? FindWebView2Runtime()
    {
        // Same discovery the package performs: ClientState "pv" value presence means an
        // installed runtime; the loader resolves the runtime's install dir. The registry
        // read only LOCATES the dependency — Available comes from loading the dll above,
        // never from the key's presence (probe rule §2).
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(WebView2ClientStateKey);
                var version = key?.GetValue("pv") as string;
                if (string.IsNullOrEmpty(version))
                {
                    continue;
                }

                // The evergreen runtime's install roots (Microsoft-documented): machine-wide
                // under ProgramFilesX86; per-user under LocalAppData (SP-011 observed a
                // user-level install, WebView2 150.0.4078.83).
                foreach (var root in new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                })
                {
                    var candidate = Path.Combine(root, "Microsoft", "EdgeWebView", "Application", version);
                    if (Directory.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }
}
