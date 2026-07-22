using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Persistence;

// ISecretStore platform implementations (SP-033; contract §10; admission §6).
// Settings documents carry opaque secret NAMES, never values (persistence-migration-contract
// §9). There is NEVER a plaintext fallback: where no platform secret service is reachable
// the probe reports a typed SP-006 Unavailable and store operations throw the typed
// SecretStoreUnavailableException.

/// <summary>Typed failure for secret-store operations when the platform backend is unavailable or rejects the operation. <see cref="Exception.Message"/> carries stable codes only — never secret values, never names with user data.</summary>
public sealed class SecretStoreUnavailableException : Exception
{
    public SecretStoreUnavailableException(string stableCode)
        : base($"secret-store: {stableCode}") => StableCode = stableCode;

    /// <summary>A stable machine token (e.g. "secret-service-unreachable", "unsupported-platform").</summary>
    public string StableCode { get; }
}

/// <summary>
/// Platform admission (admission §6 rule 2): the store for the current OS plus its SP-006
/// probe. Windows = DPAPI (CurrentUser scope — WPF discipline, SecureAuthTokenStore.cs:40,66)
/// via crypt32 P/Invoke (ProtectedData is not inbox on a plain net10.0 TFM and the project
/// file is outside this packet's File Scope — zero new packages). Linux = freedesktop
/// Secret Service through the platform's secret-tool CLI (libsecret; no new dependency).
/// Anything else = typed unsupported.
/// </summary>
public static class PlatformSecretStore
{
    /// <summary>The capability name under which the secret store's readiness is registered.</summary>
    public const string CapabilityName = "secret-store";

    /// <summary>Creates the platform store rooted at <paramref name="rootDirectory"/> (Windows file-backed DPAPI) and its probe.</summary>
    public static (ISecretStore Store, Func<CancellationToken, Task<CapabilityState>> Probe) ForCurrentPlatform(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        if (OperatingSystem.IsWindows())
        {
            var store = new WindowsDpapiSecretStore(rootDirectory);
            return (store, _ => Task.FromResult<CapabilityState>(
                new CapabilityState.Available("dpapi-currentuser")));
        }

        if (OperatingSystem.IsLinux())
        {
            var store = new SecretToolSecretStore();
            return (store, store.ProbeAsync);
        }

        var unsupported = new UnsupportedSecretStore();
        return (unsupported, _ => Task.FromResult<CapabilityState>(
            new CapabilityState.Unavailable(new CapabilityReason(
                CapabilityReasonCodes.UnsupportedPlatform, "no secret-store backend for this OS"))));
    }
}

/// <summary>
/// Windows DPAPI per-name file store. Each secret is ProtectedData-equivalent
/// (CryptProtectData, CurrentUser scope, UI forbidden) bytes in one file under the root —
/// the WPF discipline (SecureAuthTokenStore.cs:40,66; DiscordTokenStorage.cs:52-55).
/// A corrupt/foreign-machine blob is deleted and read as absent (WPF precedent: Clear()).
/// </summary>
public sealed class WindowsDpapiSecretStore : ISecretStore
{
    private readonly string _rootDirectory;

    public WindowsDpapiSecretStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        if (!OperatingSystem.IsWindows())
        {
            throw new SecretStoreUnavailableException(CapabilityReasonCodes.UnsupportedPlatform);
        }

        _rootDirectory = rootDirectory;
    }

    public string? Get(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path))
        {
            return null;
        }

        var blob = File.ReadAllBytes(path);
        if (!DpapiNative.TryUnprotect(blob, out var plain))
        {
            // Corrupt or encrypted for a different user/machine — never surfaceable (WPF precedent: Clear()).
            File.Delete(path);
            return null;
        }

        return Encoding.UTF8.GetString(plain);
    }

    public void Set(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Directory.CreateDirectory(_rootDirectory);
        var blob = DpapiNative.Protect(Encoding.UTF8.GetBytes(value));
        File.WriteAllBytes(PathFor(name), blob);
    }

    public void Delete(string name)
    {
        var path = PathFor(name);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string PathFor(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        // Names are opaque routing tokens; force them filesystem-safe (never values).
        var safe = string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));
        return Path.Combine(_rootDirectory, safe + ".dpapi");
    }
}

/// <summary>
/// Linux freedesktop Secret Service via the platform's secret-tool CLI (libsecret) — the
/// candidate backend (admission §6 rule 2). Where no secret service is reachable (EXPECTED
/// on WSL2: no session D-Bus daemon) the probe reports typed Unavailable and operations
/// throw typed SecretStoreUnavailableException — never a silent plaintext fallback. A
/// working-daemon proof needs a desktop-session box (named limit, never faked).
/// </summary>
public sealed class SecretToolSecretStore : ISecretStore
{
    private const string Tool = "secret-tool";

    // Attribute schema for every entry this store writes (service + opaque name).
    private static readonly string[] LookupArgsPrefix = ["service", "ccp-client", "name"];

    public string? Get(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var result = Run(["lookup", .. LookupArgsPrefix, name], stdin: null, CancellationToken.None)
            .GetAwaiter().GetResult();
        ThrowIfUnavailable(result);
        // exit 1 with empty stderr = "no such secret" (tool + daemon working); exit 0 = found.
        return result.ExitCode == 0 ? result.StdOut.TrimEnd('\n') : null;
    }

    public void Set(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        // The secret value travels ONLY on the tool's stdin — never on the command line
        // (argv is world-readable via /proc) and never in logs.
        var result = Run(["store", "--label=ccp-client", .. LookupArgsPrefix, name], value, CancellationToken.None)
            .GetAwaiter().GetResult();
        ThrowIfUnavailable(result);
        if (result.ExitCode != 0)
        {
            throw new SecretStoreUnavailableException("store-failed");
        }
    }

    public void Delete(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var result = Run(["clear", .. LookupArgsPrefix, name], stdin: null, CancellationToken.None)
            .GetAwaiter().GetResult();
        ThrowIfUnavailable(result);
    }

    /// <summary>
    /// The SP-006 probe: binary on PATH + a real lookup round-trip. A lookup that fails
    /// with an empty/clean "not found" exit proves the daemon is reachable; an error on
    /// stderr (e.g. "Cannot autolaunch D-Bus without X11 $DISPLAY") proves it is not.
    /// </summary>
    public async Task<CapabilityState> ProbeAsync(CancellationToken cancellationToken)
    {
        if (FindOnPath(Tool) is null)
        {
            return new CapabilityState.DependencyMissing("secret-tool", new CapabilityReason(
                CapabilityReasonCodes.SecretServiceUnreachable,
                "secret-tool (libsecret) not on PATH"));
        }

        try
        {
            var result = await Run(
                ["lookup", .. LookupArgsPrefix, "ccp-probe"], stdin: null, cancellationToken).ConfigureAwait(false);
            if (result.ToolMissing)
            {
                return new CapabilityState.DependencyMissing("secret-tool", new CapabilityReason(
                    CapabilityReasonCodes.SecretServiceUnreachable,
                    "secret-tool (libsecret) not on PATH"));
            }

            if (result.TimedOut)
            {
                return new CapabilityState.Unavailable(new CapabilityReason(
                    CapabilityReasonCodes.SecretServiceUnreachable, "secret-tool probe timed out"));
            }

            if (result.StdErr.Length > 0)
            {
                // Daemon unreachable (expected on WSL2 — no session bus). The detail is a
                // stable classification, never the raw stderr (which can carry session paths).
                return new CapabilityState.Unavailable(new CapabilityReason(
                    CapabilityReasonCodes.SecretServiceUnreachable,
                    "secret service unreachable (no session daemon)"));
            }

            return new CapabilityState.Available("secret-service via secret-tool");
        }
        catch (Exception ex)
        {
            return new CapabilityState.Faulted(new CapabilityReason(
                CapabilityReasonCodes.ProbeFault, ex.GetType().Name));
        }
    }

    private static void ThrowIfUnavailable(ToolResult result)
    {
        if (result.TimedOut || result.ToolMissing || result.StdErr.Length > 0)
        {
            throw new SecretStoreUnavailableException(CapabilityReasonCodes.SecretServiceUnreachable);
        }
    }

    private sealed record ToolResult(int ExitCode, string StdOut, string StdErr, bool TimedOut, bool ToolMissing = false);

    private static async Task<ToolResult> Run(string[] args, string? stdin, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Tool,
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        Process process;
        try
        {
            process = Process.Start(psi)!;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Tool absent from PATH — typed unavailability, never an unclassified fault.
            return new ToolResult(-1, "", "", false, ToolMissing: true);
        }

        using (process)
        {
            if (stdin is not null)
            {
                await process.StandardInput.WriteAsync(stdin).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                return new ToolResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false), false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                if (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    return new ToolResult(-1, "", "", true);
                }

                throw;
            }
        }
    }

    private static string? FindOnPath(string binary)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (path is null)
        {
            return null;
        }

        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (dir.Length == 0)
            {
                continue;
            }

            var candidate = Path.Combine(dir, binary);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

/// <summary>Typed unsupported-OS store: every operation fails typed; never plaintext.</summary>
public sealed class UnsupportedSecretStore : ISecretStore
{
    public string? Get(string name) => throw new SecretStoreUnavailableException(CapabilityReasonCodes.UnsupportedPlatform);

    public void Set(string name, string value) => throw new SecretStoreUnavailableException(CapabilityReasonCodes.UnsupportedPlatform);

    public void Delete(string name) => throw new SecretStoreUnavailableException(CapabilityReasonCodes.UnsupportedPlatform);
}

/// <summary>
/// Minimal crypt32 DPAPI binding (CurrentUser scope, UI forbidden) — the same protection
/// WPF gets from ProtectedData (SecureAuthTokenStore.cs:40,66). ProtectedData is not inbox
/// on the plain net10.0 TFM (verified empirically, record.md §7) and adding a package is
/// outside this packet's File Scope; this is the zero-dependency equivalent.
/// </summary>
internal static class DpapiNative
{
    private const int CryptprotectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int CbData;
        public IntPtr PbData;
    }

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn,
        [MarshalAs(UnmanagedType.LPWStr)] string szDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn,
        IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DataBlob pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static byte[] Protect(byte[] plain)
    {
        var input = new DataBlob { CbData = plain.Length, PbData = Marshal.AllocHGlobal(plain.Length) };
        try
        {
            Marshal.Copy(plain, 0, input.PbData, plain.Length);
            if (!CryptProtectData(ref input, "ccp-client", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, out var output))
            {
                throw new SecretStoreUnavailableException("dpapi-protect-failed");
            }

            try
            {
                var blob = new byte[output.CbData];
                Marshal.Copy(output.PbData, blob, 0, output.CbData);
                return blob;
            }
            finally
            {
                LocalFree(output.PbData);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(input.PbData);
        }
    }

    public static bool TryUnprotect(byte[] blob, out byte[] plain)
    {
        plain = [];
        var input = new DataBlob { CbData = blob.Length, PbData = Marshal.AllocHGlobal(blob.Length) };
        try
        {
            Marshal.Copy(blob, 0, input.PbData, blob.Length);
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, out var output))
            {
                return false;
            }

            try
            {
                plain = new byte[output.CbData];
                Marshal.Copy(output.PbData, plain, 0, output.CbData);
                return true;
            }
            finally
            {
                LocalFree(output.PbData);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(input.PbData);
        }
    }
}
