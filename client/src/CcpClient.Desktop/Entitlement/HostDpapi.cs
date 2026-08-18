using System.Runtime.InteropServices;

namespace CcpClient.Desktop.Entitlement;

/// <summary>
/// The seam every host-token read goes through, so the tests never need real DPAPI, a real
/// Windows profile, or the developer's real <c>auth_token.dat</c>.
/// </summary>
public interface IHostBlobDecryptor
{
    /// <summary>
    /// Attempts to decrypt <paramref name="blob"/> with <paramref name="entropy"/>. Returns
    /// false — never throws, never reports WHY beyond false — when the blob is corrupt, was
    /// written by a different user, or was protected with different entropy. The caller turns
    /// that false into a typed "could not tell", never into a refusal.
    /// </summary>
    bool TryDecrypt(byte[] blob, byte[] entropy, out byte[] plain);
}

/// <summary>
/// Windows DPAPI decrypt, <c>CurrentUser</c> scope, UI forbidden.
///
/// <para>This is the whole reason the bridge works: the shipping app protects its bearer with
/// <c>ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser)</c>
/// (Services/Auth/SecureAuthTokenStore.cs:39) and unprotects it the same way (:65), so ANY
/// process running as the same Windows user can decrypt it — no second OAuth flow, no second
/// credential store. The entropy is a compile-time literal in shipped source
/// (SecureAuthTokenStore.cs:14), i.e. a namespacing constant, not a secret.</para>
///
/// <para>DECRYPT ONLY, BY DESIGN. There is no Protect here and there must never be one: the
/// port reads the shipping app's login and never writes it — not back to that file, not into
/// the port's own secret store, not anywhere. A missing capability is easier to keep than a
/// discipline.</para>
///
/// <para>Own crypt32 binding rather than the one in <c>Persistence/SecretStores.cs</c> because
/// that one deliberately passes no entropy blob (it protects the port's OWN secrets) and this
/// one must pass the shipping app's; that file is also outside this packet's File Scope.
/// <c>System.Security.Cryptography.ProtectedData</c> is not inbox on the plain <c>net10.0</c>
/// TFM (recorded empirically at Persistence/SecretStores.cs:327-330) and adding a package would
/// mean editing a csproj outside scope — so, zero new dependencies.</para>
/// </summary>
public sealed class WindowsDpapiBlobDecryptor : IHostBlobDecryptor
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
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn,
        IntPtr ppszDataDescr,
        ref DataBlob pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DataBlob pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public bool TryDecrypt(byte[] blob, byte[] entropy, out byte[] plain)
    {
        ArgumentNullException.ThrowIfNull(blob);
        ArgumentNullException.ThrowIfNull(entropy);
        plain = [];
        if (!OperatingSystem.IsWindows())
        {
            // Never reachable through the platform factory (which hands back the unsupported
            // reader off Windows); false here keeps the type honest if it is constructed directly.
            return false;
        }

        var input = new DataBlob { CbData = blob.Length, PbData = Marshal.AllocHGlobal(blob.Length) };
        var entropyBlob = new DataBlob { CbData = entropy.Length, PbData = Marshal.AllocHGlobal(Math.Max(entropy.Length, 1)) };
        try
        {
            Marshal.Copy(blob, 0, input.PbData, blob.Length);
            if (entropy.Length > 0)
            {
                Marshal.Copy(entropy, 0, entropyBlob.PbData, entropy.Length);
            }

            if (!CryptUnprotectData(ref input, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero,
                    CryptprotectUiForbidden, out var output))
            {
                // Corrupt, foreign user/machine, or rotated entropy. The Win32 error is
                // deliberately not surfaced: the caller must not be able to turn this into a
                // refusal, and a diagnostic string here is one interpolation away from a leak.
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
            Marshal.FreeHGlobal(entropyBlob.PbData);
        }
    }
}
