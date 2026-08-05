using System.Diagnostics;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// SP-027 slice b5: the `0x800700AA`-class stale-profile-lock recovery (routed from
/// SP-023 record surprise #7: back-to-back runs where a prior process was killed leave
/// the WebView2 profile locked briefly → init failure; recovery = kill the stale
/// msedgewebview2 children holding OUR UserDataFolder). 0x800700AA = HRESULT from
/// ERROR_BUSY (170, "The resource is in use") — WebView2's environment creation fails
/// with it when a zombie browser process still holds the profile directory.
///
/// This is the deterministic case on the RELAUNCH path (consult (b)2): the watchdog
/// relaunch after a renderer kill is exactly SP-023's back-to-back shape, so the
/// coordinator runs <see cref="TryRecover"/> before recreating the window.
///
/// Honesty: every outcome is typed + logged by the caller — never silent, never a crash
/// loop (the window retries navigation once, then the failure stands typed). The process
/// enumeration runs through PowerShell CIM (the SP-011 kill-renderer.ps1 technique —
/// the only package-free command-line source on Windows; System.Management is NOT
/// admitted). Windows-only; Linux returns a typed unsupported outcome.
/// </summary>
public static class DtrhProfileLock
{
    /// <summary>HRESULT 0x800700AA (ERROR_BUSY as a failure HRESULT).</summary>
    public const int StaleProfileLockHResult = unchecked((int)0x800700AA);

    /// <summary>The DTRH data root next to settings.json (single source — the host
    /// window's environment setup and the coordinator's relaunch-path recovery share
    /// it).</summary>
    public static string DtrhDataRoot() =>
        Path.Combine(Path.GetDirectoryName(CompositionRoot.DefaultSettingsPath())!, "dtrh");

    /// <summary>The WebView2 UserDataFolder (DtrhHostService.cs:111 browser_data_dtrh
    /// parity — a dedicated profile per surface).</summary>
    public static string WebView2ProfileDir() => Path.Combine(DtrhDataRoot(), "wv2-profile");

    /// <summary>SP-049: a NAMED per-surface profile (WPF parity: the game uses
    /// browser_data_dtrh, the studio window browser_data_loom — LoomHostService.cs:64
    /// "Own browser profile: the game's WebView2 state stays untouched"). Two surfaces on
    /// ONE profile would also re-arm the b5 stale-profile-lock class.</summary>
    public static string WebView2ProfileDir(string surface) => Path.Combine(DtrhDataRoot(), $"wv2-profile-{surface}");

    /// <summary>Typed recovery outcome — never silent.</summary>
    public abstract record DtrhProfileRecovery
    {
        /// <summary>Stale children holding our profile were found and killed.</summary>
        public sealed record Recovered(int KilledCount) : DtrhProfileRecovery;

        /// <summary>No msedgewebview2 process references our UserDataFolder (nothing to
        /// recover — the lock, if any, is not a stale-child lock).</summary>
        public sealed record NothingToKill : DtrhProfileRecovery;

        /// <summary>The recovery mechanism itself is unavailable here.</summary>
        public sealed record Unsupported(string Detail) : DtrhProfileRecovery;

        /// <summary>The recovery attempted and failed (enumeration/kill error).</summary>
        public sealed record Failed(string Detail) : DtrhProfileRecovery;
    }

    /// <summary>The 0x800700AA class, matched on HResult (COMException) or on the
    /// rendered hex in a message (the package may wrap the HRESULT in a generic
    /// exception's text). Walks inner exceptions.</summary>
    public static bool IsStaleProfileLock(Exception? ex)
    {
        while (ex is not null)
        {
            if (ex.HResult == StaleProfileLockHResult
                || ex.Message.Contains("0x800700AA", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            ex = ex.InnerException;
        }

        return false;
    }

    /// <summary>Kill every msedgewebview2 process whose command line carries our
    /// UserDataFolder (the SP-011 kill-renderer.ps1 selector, run in-product). Never
    /// throws; the outcome says exactly what happened.</summary>
    public static DtrhProfileRecovery TryRecover(string userDataFolder)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new DtrhProfileRecovery.Unsupported(
                "stale-profile-lock recovery is a WebView2 (Windows) class; the Linux WebKitGTK dialog "
                + "path has no msedgewebview2 children and no 0x800700AA surface (named limit)");
        }

        try
        {
            // The spike's proven selector (kill-renderer.ps1), package-free. -NoProfile
            // keeps it fast and hermetic; the marker match is case-insensitive like the
            // spike's -like. -EncodedCommand (Base64 UTF-16LE) is quoting-immune — a
            // quoted -Command string with inner quotes misparses (observed in the b5
            // test run).
            var escaped = userDataFolder.Replace("'", "''");
            var script = "$m='" + escaped + "';"
                + "$p=Get-CimInstance Win32_Process -Filter \"Name='msedgewebview2.exe'\" | Where-Object { $_.CommandLine -like \"*$m*\" };"
                + "if (-not $p) { 'NONE' } else { $p | ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop; $_.ProcessId } catch {} } }";
            var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
            using var ps = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (ps is null)
            {
                return new DtrhProfileRecovery.Failed("powershell.exe did not start");
            }

            // Bounded wait: CIM enumeration is seconds at worst; a wedged WMI service
            // must never hang the host (typed failure instead).
            if (!ps.WaitForExit(15000))
            {
                try { ps.Kill(); } catch { /* best-effort */ }
                return new DtrhProfileRecovery.Failed("CIM enumeration timed out (15s)");
            }

            var output = ps.StandardOutput.ReadToEnd();
            if (ps.ExitCode != 0)
            {
                var err = ps.StandardError.ReadToEnd();
                return new DtrhProfileRecovery.Failed($"powershell exit {ps.ExitCode}: {Trim(err)}");
            }

            var killed = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Count(line => int.TryParse(line, out _));
            return killed > 0
                ? new DtrhProfileRecovery.Recovered(killed)
                : new DtrhProfileRecovery.NothingToKill();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new DtrhProfileRecovery.Failed($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string Trim(string s) =>
        s.Length <= 200 ? s.Trim() : s[..200].Trim() + "…";
}
