using System.Text;
using CcpClient.Desktop.Entitlement;

namespace CcpClient.Tests;

// SP-092 shared fixtures and seams for the entitlement tests.
//
// PRIVACY RULE, MECHANICAL RATHER THAN ASSUMED: no test in this suite contains a real token
// and no test reads the developer's real %LOCALAPPDATA%/ConditioningControlPanel/auth_token.dat.
// Every value here is the self-describing literal below, every store is a temp directory this
// file creates and deletes, and the decrypt step is an injected seam.

internal static class EntitlementFixtures
{
    /// <summary>The only "credential" any entitlement test ever handles. It says what it is.</summary>
    public const string TokenValue = "SP092-FIXTURE-NOT-A-REAL-TOKEN-4c1f9e";

    public static byte[] TokenUtf8() => Encoding.UTF8.GetBytes(TokenValue);

    /// <summary>Bytes that are not a DPAPI blob at all — the "corrupt store" fixture.</summary>
    public static byte[] CorruptBlob() => Encoding.UTF8.GetBytes("not-a-dpapi-blob-SP092");
}

/// <summary>A temp stand-in for the shipping app's data directory. Never the real one.</summary>
internal sealed class HostFixtureDirectory : IDisposable
{
    private HostFixtureDirectory(string root, bool create)
    {
        Root = root;
        if (create)
        {
            Directory.CreateDirectory(root);
        }
    }

    public string Root { get; }

    /// <summary>The directory exists (the shipping app is "installed").</summary>
    public static HostFixtureDirectory Installed() => new(NewRoot(), create: true);

    /// <summary>The directory does not exist (the shipping app was never installed here).</summary>
    public static HostFixtureDirectory NeverInstalled() => new(NewRoot(), create: false);

    public HostFixtureDirectory WithStoredBlob(byte[] blob)
    {
        File.WriteAllBytes(Path.Combine(Root, ShippingAppTokenReader.TokenFileName), blob);
        return this;
    }

    public string TokenFilePath => Path.Combine(Root, ShippingAppTokenReader.TokenFileName);

    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "ccp-sp092-host-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

/// <summary>Decrypt seam: hands back a fixed plaintext, or refuses like DPAPI does.</summary>
internal sealed class StubDecryptor(byte[]? plaintext) : IHostBlobDecryptor
{
    /// <summary>Refuses every blob — corrupt, foreign user, or rotated entropy.</summary>
    public static StubDecryptor Refusing() => new(null);

    public static StubDecryptor Returning(byte[] plaintext) => new(plaintext);

    public int Calls { get; private set; }

    public byte[]? LastEntropy { get; private set; }

    public bool TryDecrypt(byte[] blob, byte[] entropy, out byte[] plain)
    {
        Calls++;
        LastEntropy = entropy;
        if (plaintext is null)
        {
            plain = [];
            return false;
        }

        plain = (byte[])plaintext.Clone();
        return true;
    }
}

/// <summary>A store that cannot be read at all (I/O, permission, sharing).</summary>
internal sealed class ThrowingDecryptor : IHostBlobDecryptor
{
    public bool TryDecrypt(byte[] blob, byte[] entropy, out byte[] plain) =>
        throw new IOException("fixture: the stored login could not be read");
}

/// <summary>Read seam under full test control.</summary>
internal sealed class StubReader(Func<HostTokenRead> read) : IHostAuthTokenReader
{
    public static StubReader Yielding(string tokenValue) =>
        new(() => HostTokenRead.Found(new HostAuthToken(tokenValue)));

    public int Calls { get; private set; }

    public HostTokenRead Read()
    {
        Calls++;
        return read();
    }
}

/// <summary>An authority with a scripted answer, which also records how it was called.</summary>
internal sealed class StubTierSource(TierLookup lookup) : IEntitlementTierSource
{
    /// <summary>The handle the capability lent us — kept to prove it is dropped afterwards.</summary>
    public HostAuthToken? BorrowedToken { get; private set; }

    /// <summary>What the seam could read while it held the loan (fixture value only).</summary>
    public string? ReadDuringLoan { get; private set; }

    public int Calls { get; private set; }

    public Task<TierLookup> LookupAsync(HostAuthToken token, CancellationToken cancellationToken)
    {
        Calls++;
        BorrowedToken = token;
        ReadDuringLoan = new string(token.Reveal());
        return Task.FromResult(lookup);
    }
}

/// <summary>An authority whose lookup throws.</summary>
internal sealed class ThrowingTierSource(Exception failure) : IEntitlementTierSource
{
    public Task<TierLookup> LookupAsync(HostAuthToken token, CancellationToken cancellationToken) =>
        throw failure;
}
