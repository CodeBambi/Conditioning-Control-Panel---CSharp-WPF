using System.Text;
using CcpClient.Desktop.Entitlement;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-092, the other half: the borrowed credential is read, used and dropped.
///
/// <para>The rules are not style preferences. The token is another application's live bearer
/// held only because DPAPI's <c>CurrentUser</c> scope lets this process decrypt it
/// (Services/Auth/SecureAuthTokenStore.cs:39,65); it may not be logged, persisted, copied into
/// the port's own store, or placed in any message or exception. These tests drive the injected
/// seams over fixture data — no test here contains a real token or reads the developer's real
/// <c>%LOCALAPPDATA%/ConditioningControlPanel/auth_token.dat</c>.</para>
/// </summary>
public class EntitlementPrivacyTests
{
    [Fact]
    public async Task TheTokenValueAppearsInNoDiagnosticSurfaceThisCapabilityProduces()
    {
        var log = new List<string>();
        var authority = new StubTierSource(TierLookup.Entitled(EntitlementTier.Lab));
        var token = new HostAuthToken(EntitlementFixtures.TokenValue);
        var read = HostTokenRead.Found(token);

        // Everything the capability can hand a caller or a log, in one bag.
        var outcome = await new HostLoginEntitlement(new StubReader(() => read), authority, log.Add)
            .ResolveAsync(TestContext.Current.CancellationToken);

        var surfaces = new List<string>
        {
            token.ToString(),
            read.ToString(),
            read.Detail,
            outcome.ToString(),
            outcome.Describe(),
            string.Join("\n", log),
        };
        surfaces.AddRange(log);

        foreach (var surface in surfaces)
        {
            Assert.DoesNotContain(EntitlementFixtures.TokenValue, surface, StringComparison.Ordinal);
        }

        // Non-vacuous on two counts: the seam really did see the value (so the value really was
        // in play), and the log really did record something (so "no leak" is not "no output").
        Assert.Equal(EntitlementFixtures.TokenValue, authority.ReadDuringLoan);
        Assert.Equal(HostAuthToken.RedactedText, token.ToString());
        Assert.Single(log);
        Assert.Equal("entitlement: entitled(lab)", log[0]);
    }

    [Fact]
    public async Task TheOutcomeCarriesNoTokenAndTheHandleIsDroppedAfterResolve()
    {
        var authority = new StubTierSource(TierLookup.Entitled(EntitlementTier.Supporter));
        var token = new HostAuthToken(EntitlementFixtures.TokenValue);

        var outcome = await new HostLoginEntitlement(new StubReader(() => HostTokenRead.Found(token)), authority)
            .ResolveAsync(TestContext.Current.CancellationToken);

        Assert.IsType<EntitlementOutcome.Entitled>(outcome);
        Assert.Same(token, authority.BorrowedToken);

        // Read, used, DROPPED: the buffer is zeroed and a later read throws rather than
        // returning an empty value that would read downstream as "no login".
        Assert.True(token.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => _ = token.Reveal().Length);
    }

    [Fact]
    public async Task ResolvingWritesNothing_NotIntoTheHostStoreAndNotBesideIt()
    {
        using var host = HostFixtureDirectory.Installed().WithStoredBlob(EntitlementFixtures.CorruptBlob());
        var before = File.ReadAllBytes(host.TokenFilePath);
        var stampBefore = File.GetLastWriteTimeUtc(host.TokenFilePath);

        var capability = new HostLoginEntitlement(
            new ShippingAppTokenReader(host.Root, StubDecryptor.Returning(EntitlementFixtures.TokenUtf8())),
            new StubTierSource(TierLookup.Entitled(EntitlementTier.Lab)));
        await capability.ResolveAsync(TestContext.Current.CancellationToken);
        await capability.ProbeAsync(TestContext.Current.CancellationToken);

        // The shipping app's store is untouched — the port reads it and never writes it, so a
        // failed decrypt can never "heal" the user's login away (WPF's own Clear-on-failure,
        // SecureAuthTokenStore.cs:75, is deliberately NOT ported: this is someone else's file).
        Assert.Equal([ShippingAppTokenReader.TokenFileName],
            Directory.EnumerateFiles(host.Root).Select(f => Path.GetFileName(f)).ToArray());
        Assert.Equal(before, File.ReadAllBytes(host.TokenFilePath));
        Assert.Equal(stampBefore, File.GetLastWriteTimeUtc(host.TokenFilePath));
    }

    [Fact]
    public void NothingUnderEntitlementCanWriteACredentialAnywhere()
    {
        // A lexical guard over this packet's own source. The privacy rule says the token is
        // never persisted or copied into the port's own store; the cheapest way to keep that
        // true is for the namespace to contain no write API at all — no file write, no DPAPI
        // protect, no secret-store call. Honest limit: this is a token scan, so a write reached
        // through an indirection it does not name is invisible to it.
        // No existence PREDICATE anywhere in this body on purpose: the enumeration below
        // throws when the directory is missing, so a guard pointed at nothing fails loudly
        // instead of quietly scanning zero files.
        var entitlementDir = Path.Combine(FindRepoRoot(), "client", "src", "CcpClient.Desktop", "Entitlement");

        string[] forbidden =
        [
            "File.WriteAllBytes", "File.WriteAllText", "File.AppendAll", "File.Create",
            "FileStream", "StreamWriter", "CryptProtectData", "ProtectedData.Protect",
            "ISecretStore", "SecretStore",
        ];

        var violations = new List<string>();
        var scanned = 0;
        foreach (var file in Directory.EnumerateFiles(entitlementDir, "*.cs", SearchOption.AllDirectories))
        {
            scanned++;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal) ||
                    line.TrimStart().StartsWith("///", StringComparison.Ordinal))
                {
                    continue; // prose may name what the code must not do
                }

                foreach (var token in forbidden)
                {
                    if (line.Contains(token, StringComparison.Ordinal))
                    {
                        violations.Add($"{file.Replace('\\', '/')}:{i + 1}: {token}");
                    }
                }
            }
        }

        Assert.True(scanned > 0, "no Entitlement source files were scanned — the guard refuses to go blind");
        Assert.True(violations.Count == 0,
            "the entitlement namespace must contain no write path for a borrowed credential:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public async Task TheShippingAppsOwnConstantsAreWhatTheReaderUses()
    {
        // Citation check against the read-only WPF tree: if upstream rotates the entropy to
        // _v2 or renames the file, this reds HERE with the drift named, instead of the port
        // silently reporting every patron as undecryptable. (Which would still be an honest
        // Unavailable — but a knowable break should be known.)
        // Read it directly rather than probing for it: a missing evidence file throws here,
        // which is the loud failure this check wants, not a predicate that could go quiet.
        var upstream = Path.Combine(FindRepoRoot(), "ConditioningControlPanel", "Services", "Auth", "SecureAuthTokenStore.cs");
        var text = await File.ReadAllTextAsync(upstream, TestContext.Current.CancellationToken);

        Assert.Contains($"\"{ShippingAppTokenReader.EntropyLiteral}\"", text, StringComparison.Ordinal);
        Assert.Contains($"\"{ShippingAppTokenReader.TokenFileName}\"", text, StringComparison.Ordinal);
        Assert.Contains("DataProtectionScope.CurrentUser", text, StringComparison.Ordinal);

        // And the reader really does present that entropy to the decrypt seam.
        using var host = HostFixtureDirectory.Installed().WithStoredBlob(EntitlementFixtures.CorruptBlob());
        var decryptor = StubDecryptor.Returning(EntitlementFixtures.TokenUtf8());
        using var read = new ShippingAppTokenReader(host.Root, decryptor).Read();
        Assert.Equal(HostTokenReadStatus.Found, read.Status);
        Assert.Equal(Encoding.UTF8.GetBytes(ShippingAppTokenReader.EntropyLiteral), decryptor.LastEntropy);
    }

    [Fact]
    public async Task TheRealPlatformCapability_OverBytesItCannotRead_IsNeverARefusal()
    {
        // The REAL platform path — no injected decryptor, no branch, and no OS predicate, so
        // this asserts something true and non-vacuous wherever it runs. On Windows it drives
        // real crypt32 (WindowsDpapiBlobDecryptor) over bytes that are not a DPAPI blob and
        // proves the refusal surfaces as host-token-undecryptable; on a platform without DPAPI
        // it proves the same call lands unsupported-platform. Both are "I could not tell", and
        // the assertion that matters is identical on both: NEVER NotEntitled.
        using var host = HostFixtureDirectory.Installed().WithStoredBlob(EntitlementFixtures.CorruptBlob());
        var outcome = await HostLoginEntitlement
            .ForCurrentPlatform(new StubTierSource(TierLookup.Entitled(EntitlementTier.Lab)),
                shippingAppDataDirectory: host.Root)
            .ResolveAsync(TestContext.Current.CancellationToken);

        Assert.IsNotType<EntitlementOutcome.NotEntitled>(outcome);
        var reason = Assert.IsType<EntitlementOutcome.Unavailable>(outcome).Reason;
        Assert.Contains(reason.Code,
            new[] { EntitlementReasonCodes.HostTokenUndecryptable, EntitlementReasonCodes.UnsupportedPlatform });

        // NAMED LIMIT, and it is a real one: the POSITIVE half — a blob protected with the
        // shipping app's entropy decrypting back through this same real path — is inherently
        // Windows-only, and an OS-gated fact needs either an allowedSkips name in
        // client/tests/floor/floor.json or a disposition in
        // client/tests/floor/vacuous-shape-ledger.json. The positive Windows-only leg is
        // manual verification; this portable test never claims to exercise it.
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "client", "CcpClient.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"repo root not found walking up from {AppContext.BaseDirectory} — the entitlement guards refuse to skip");
    }
}
