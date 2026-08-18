using System.Text;

namespace CcpClient.Desktop.Entitlement;

/// <summary>
/// What a read of the shipping app's stored login found. Every failure is its own member:
/// the shipping app's own <c>Retrieve()</c> returns a bare null for "no file" AND for "DPAPI
/// refused the blob" (Services/Auth/SecureAuthTokenStore.cs:58-76), and a port that inherited
/// that single null would have no way to tell "never logged in" from "cannot read this login".
/// </summary>
public enum HostTokenReadStatus
{
    /// <summary>A login was read and can be presented to an entitlement authority.</summary>
    Found,

    /// <summary>The shipping app's data directory does not exist — never installed for this user.</summary>
    NoDataDirectory,

    /// <summary>The directory exists; there is no token file (never logged in, or logged out).</summary>
    NoTokenFile,

    /// <summary>The blob decrypted to nothing.</summary>
    EmptyToken,

    /// <summary>DPAPI refused: corrupt, a different Windows user, or rotated entropy.</summary>
    DecryptFailed,

    /// <summary>Reading the file failed (I/O, permission, sharing).</summary>
    ReadFailed,

    /// <summary>This OS has no DPAPI; the shipping app's login cannot be read here at all.</summary>
    UnsupportedPlatform,
}

/// <summary>
/// The typed read result. Owns the token handle when there is one, so a caller's
/// <c>using</c> drops the credential on every path including the failure paths.
/// </summary>
public sealed class HostTokenRead : IDisposable
{
    private HostTokenRead(HostTokenReadStatus status, HostAuthToken? token, string detail)
    {
        Status = status;
        Token = token;
        Detail = detail;
    }

    public HostTokenReadStatus Status { get; }

    /// <summary>Non-null only when <see cref="Status"/> is <see cref="HostTokenReadStatus.Found"/>.</summary>
    public HostAuthToken? Token { get; }

    /// <summary>Authored classification text. Never a path, a username, or any part of a token.</summary>
    public string Detail { get; }

    public static HostTokenRead Found(HostAuthToken token) =>
        new(HostTokenReadStatus.Found, token, "host login read from the shipping app's store");

    public static HostTokenRead NoDataDirectory() =>
        new(HostTokenReadStatus.NoDataDirectory, null, "the shipping app's data directory does not exist");

    public static HostTokenRead NoTokenFile() =>
        new(HostTokenReadStatus.NoTokenFile, null, "the shipping app is installed but has stored no login");

    public static HostTokenRead EmptyToken() =>
        new(HostTokenReadStatus.EmptyToken, null, "the stored login decrypted to an empty value");

    public static HostTokenRead DecryptFailed() =>
        new(HostTokenReadStatus.DecryptFailed, null,
            "the stored login could not be decrypted (corrupt, a different Windows user, or rotated entropy)");

    /// <param name="failureClass">An exception TYPE name. Never a message: messages carry paths.</param>
    public static HostTokenRead ReadFailed(string failureClass) =>
        new(HostTokenReadStatus.ReadFailed, null, "reading the stored login failed: " + failureClass);

    public static HostTokenRead UnsupportedPlatform() =>
        new(HostTokenReadStatus.UnsupportedPlatform, null, "this platform has no DPAPI, so the shipping app's login cannot be read");

    /// <summary>Class only — a read result never renders its token.</summary>
    public override string ToString() => "host-token-read(" + Status + ")";

    public void Dispose() => Token?.Dispose();
}

/// <summary>The read seam. Injected everywhere, so no test ever touches a real store.</summary>
public interface IHostAuthTokenReader
{
    HostTokenRead Read();
}

/// <summary>
/// Reads the shipping WPF app's DPAPI-protected bearer.
///
/// <para>Mechanism, all four facts confirmed in the read-only tree at authoring:
/// the file is <c>auth_token.dat</c> under the shipping app's data directory
/// (Services/Auth/SecureAuthTokenStore.cs:15-17), the entropy is the compile-time literal
/// <c>ConditioningControlPanel_AuthToken_v1</c> (:14), and protect/unprotect use
/// <c>DataProtectionScope.CurrentUser</c> (:39,65) — which is what lets a second process
/// running as the same Windows user read the existing login instead of starting another
/// OAuth flow.</para>
///
/// <para>The tier is NOT in this file. It resolves server-side through
/// <c>App.Patreon?.HasLabAccess</c> or the server naming today's free key
/// (Services/TierGate.cs:88-94) — the token is a bearer, the entitlement is an answer. This
/// class therefore reports only whether a login could be read, never whether it pays.</para>
/// </summary>
public sealed class ShippingAppTokenReader : IHostAuthTokenReader
{
    /// <summary>SecureAuthTokenStore.cs:17.</summary>
    public const string TokenFileName = "auth_token.dat";

    /// <summary>
    /// SecureAuthTokenStore.cs:14. A constant in shipped source: namespacing for the DPAPI
    /// blob, not a secret. If upstream ever rotates it to <c>_v2</c>, decryption starts
    /// failing here — and that failure surfaces as <c>Unavailable(host-token-undecryptable)</c>,
    /// never as "not a patron". That is the whole point of the typed outcome.
    /// </summary>
    public const string EntropyLiteral = "ConditioningControlPanel_AuthToken_v1";

    private readonly string _dataDirectory;
    private readonly IHostBlobDecryptor _decryptor;

    public ShippingAppTokenReader(string dataDirectory, IHostBlobDecryptor decryptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(decryptor);
        _dataDirectory = dataDirectory;
        _decryptor = decryptor;
    }

    public HostTokenRead Read()
    {
        try
        {
            if (!Directory.Exists(_dataDirectory))
            {
                return HostTokenRead.NoDataDirectory();
            }

            var path = Path.Combine(_dataDirectory, TokenFileName);
            if (!File.Exists(path))
            {
                return HostTokenRead.NoTokenFile();
            }

            var blob = File.ReadAllBytes(path);
            if (!_decryptor.TryDecrypt(blob, Encoding.UTF8.GetBytes(EntropyLiteral), out var plain))
            {
                return HostTokenRead.DecryptFailed();
            }

            var token = HostAuthToken.FromDecryptedUtf8(plain);
            if (token.IsEmpty)
            {
                token.Dispose();
                return HostTokenRead.EmptyToken();
            }

            return HostTokenRead.Found(token);
        }
        catch (Exception ex)
        {
            // Type name only. WPF logs the exception object here (SecureAuthTokenStore.cs:79);
            // the port cannot, because its diagnostic strings must stay value-free.
            return HostTokenRead.ReadFailed(ex.GetType().Name);
        }
    }
}

/// <summary>
/// The honest answer on a platform with no DPAPI. NOT a stub that says "not entitled" — that
/// shape would tell every Linux user they had stopped paying. It mirrors how the port's
/// <c>ISecretStore</c> already reports an absent platform backend
/// (Persistence/SecretStores.cs:54-58): a typed unavailability, never a silent downgrade.
/// </summary>
public sealed class UnsupportedPlatformTokenReader : IHostAuthTokenReader
{
    public HostTokenRead Read() => HostTokenRead.UnsupportedPlatform();
}

/// <summary>Platform admission: the reader for the OS this process is running on.</summary>
public static class HostTokenReaders
{
    /// <param name="dataDirectory">
    /// Where the shipping app keeps its data; defaults to <see cref="ShippingAppDataLocation.Resolve"/>.
    /// Injected by tests so no test ever reads the developer's real store.
    /// </param>
    public static IHostAuthTokenReader ForCurrentPlatform(string? dataDirectory = null) =>
        OperatingSystem.IsWindows()
            ? new ShippingAppTokenReader(dataDirectory ?? ShippingAppDataLocation.Resolve(), new WindowsDpapiBlobDecryptor())
            : new UnsupportedPlatformTokenReader();
}
