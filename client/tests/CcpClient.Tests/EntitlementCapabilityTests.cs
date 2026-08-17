using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Entitlement;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-092: the entitlement capability that reads the shipping WPF app's existing login.
///
/// <para>THE FACT THIS FILE EXISTS TO PROVE. "You are not a patron" and "I could not tell" are
/// different answers, and the second must never be reported as the first. A silent downgrade
/// there presents exactly like a legitimate refusal: the user's paid features vanish with no
/// explanation and no way to tell a bug from a policy. The shipping app makes the mistake easy
/// to inherit — its <c>Retrieve()</c> returns a bare null both for "no file" and for "DPAPI
/// refused this blob" (Services/Auth/SecureAuthTokenStore.cs:58-76) — so every undetermined
/// input is tested here by name, and
/// <see cref="EveryUndeterminedInput_IsUnavailableWithItsOwnReason_AndOnlyAnAuthorityRefusalIsNotEntitled"/>
/// closes the set.</para>
/// </summary>
public class EntitlementCapabilityTests
{
    private static HostLoginEntitlement Over(IHostAuthTokenReader reader, IEntitlementTierSource? authority = null,
        Action<string>? log = null) => new(reader, authority, log);

    private static ShippingAppTokenReader ReaderOver(HostFixtureDirectory host, IHostBlobDecryptor decryptor) =>
        new(host.Root, decryptor);

    /// <summary>Asserts the packet's central fact for one input, both halves.</summary>
    private static EntitlementReason AssertCouldNotTell(EntitlementOutcome outcome)
    {
        Assert.IsNotType<EntitlementOutcome.NotEntitled>(outcome);
        var unavailable = Assert.IsType<EntitlementOutcome.Unavailable>(outcome);
        Assert.False(string.IsNullOrWhiteSpace(unavailable.Reason.Code));
        Assert.False(string.IsNullOrWhiteSpace(unavailable.Reason.Detail));
        return unavailable.Reason;
    }

    // ---------------------------------------------------------------- read-side unknowns

    [Fact]
    public async Task NoShippingAppDataDirectory_IsUnavailable_NeverNotEntitled()
    {
        // The bridge's own precondition: a port-only user never installed the WPF app.
        using var host = HostFixtureDirectory.NeverInstalled();
        var outcome = await Over(ReaderOver(host, StubDecryptor.Returning(EntitlementFixtures.TokenUtf8())))
            .ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EntitlementReasonCodes.HostAppDataAbsent, AssertCouldNotTell(outcome).Code);
    }

    [Fact]
    public async Task NoStoredLoginFile_IsUnavailable_NeverNotEntitled()
    {
        // Installed but never logged in — or logged out, since WPF deletes the file on Clear()
        // (SecureAuthTokenStore.cs:98-122). Both are "no login to read", not "no pledge".
        using var host = HostFixtureDirectory.Installed();
        var outcome = await Over(ReaderOver(host, StubDecryptor.Returning(EntitlementFixtures.TokenUtf8())))
            .ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EntitlementReasonCodes.HostTokenAbsent, AssertCouldNotTell(outcome).Code);
    }

    [Fact]
    public async Task DecryptFailure_IsUnavailable_NeverNotEntitled()
    {
        // The packet's named trap: a different Windows account, a corrupt blob, or upstream
        // rotating the entropy literal from _v1 to _v2 (SecureAuthTokenStore.cs:14). WPF itself
        // classifies this as "may be corrupted or from different user" (:71-74) — it is the
        // loudest "I could not tell" in the set and the easiest to mistake for a refusal.
        using var host = HostFixtureDirectory.Installed().WithStoredBlob(EntitlementFixtures.CorruptBlob());
        var outcome = await Over(ReaderOver(host, StubDecryptor.Refusing()))
            .ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EntitlementReasonCodes.HostTokenUndecryptable, AssertCouldNotTell(outcome).Code);
    }

    [Fact]
    public async Task EmptyStoredLogin_IsUnavailable_NeverNotEntitled()
    {
        using var host = HostFixtureDirectory.Installed().WithStoredBlob(EntitlementFixtures.CorruptBlob());
        var outcome = await Over(ReaderOver(host, StubDecryptor.Returning([])))
            .ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EntitlementReasonCodes.HostTokenEmpty, AssertCouldNotTell(outcome).Code);
    }

    [Fact]
    public async Task UnreadableStore_IsUnavailable_NeverNotEntitled()
    {
        using var host = HostFixtureDirectory.Installed().WithStoredBlob(EntitlementFixtures.CorruptBlob());
        var outcome = await Over(ReaderOver(host, new ThrowingDecryptor()))
            .ResolveAsync(TestContext.Current.CancellationToken);

        var reason = AssertCouldNotTell(outcome);
        Assert.Equal(EntitlementReasonCodes.HostReadFailed, reason.Code);
        // Exception TYPE only: a message carries paths, and this string is destined for a log.
        Assert.Contains(nameof(IOException), reason.Detail);
        Assert.DoesNotContain("fixture: the stored login could not be read", reason.Detail);
    }

    [Fact]
    public async Task PlatformWithoutDpapi_IsUnavailable_NeverNotEntitled()
    {
        // The Linux answer. A stub that returned NotEntitled here would tell every Linux user
        // they had stopped paying; the port's ISecretStore already reports absence this way
        // (Persistence/SecretStores.cs:54-58).
        var outcome = await Over(new UnsupportedPlatformTokenReader())
            .ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EntitlementReasonCodes.UnsupportedPlatform, AssertCouldNotTell(outcome).Code);
    }

    // ------------------------------------------------------------ authority-side unknowns

    [Fact]
    public async Task NoAuthorityConfigured_IsUnavailable_NeverNotEntitled()
    {
        // This build's default. The tier is never in the token — it resolves through
        // App.Patreon?.HasLabAccess or the server naming today's free key (TierGate.cs:88-94) —
        // so with no authority the honest answer is "unknown", not "no".
        var outcome = await Over(StubReader.Yielding(EntitlementFixtures.TokenValue))
            .ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EntitlementReasonCodes.TierAuthorityAbsent, AssertCouldNotTell(outcome).Code);
    }

    [Fact]
    public async Task AuthorityUnreachable_IsUnavailable_NeverNotEntitled()
    {
        // The packet's step-7 question: a VALID login the authority cannot be asked about.
        // Offline is not evidence of anything about the pledge.
        var outcome = await Over(
                StubReader.Yielding(EntitlementFixtures.TokenValue),
                new StubTierSource(TierLookup.Unreachable("network")))
            .ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EntitlementReasonCodes.TierAuthorityUnreachable, AssertCouldNotTell(outcome).Code);
    }

    [Fact]
    public async Task AuthorityRejectedTheStoredLogin_IsUnavailable_NeverNotEntitled()
    {
        // A refused bearer (expired or rotated) means the authority never reached the
        // entitlement question. WPF treats the same 401 as recoverable, refreshing before
        // anything is dropped (Services/Account/PatreonService.cs:541-555).
        var outcome = await Over(
                StubReader.Yielding(EntitlementFixtures.TokenValue),
                new StubTierSource(TierLookup.Rejected()))
            .ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EntitlementReasonCodes.TierAuthorityRejected, AssertCouldNotTell(outcome).Code);
    }

    [Fact]
    public async Task AuthorityThrew_IsUnavailable_NeverNotEntitled_AndCarriesNoMessage()
    {
        var outcome = await Over(
                StubReader.Yielding(EntitlementFixtures.TokenValue),
                new ThrowingTierSource(new HttpRequestException("fixture-secret-bearing-message")))
            .ResolveAsync(TestContext.Current.CancellationToken);

        var reason = AssertCouldNotTell(outcome);
        Assert.Equal(EntitlementReasonCodes.TierAuthorityFault, reason.Code);
        Assert.Contains(nameof(HttpRequestException), reason.Detail);
        Assert.DoesNotContain("fixture-secret-bearing-message", reason.Detail);
    }

    [Fact]
    public async Task AuthorityConfirmedAPledgeWithoutNamingATier_IsUnavailable_NeverAnInventedTier()
    {
        // "Do not invent a tier when the server cannot be reached" extends to a server that
        // answered without saying which bar it cleared.
        var outcome = await Over(
                StubReader.Yielding(EntitlementFixtures.TokenValue),
                new StubTierSource(new TierLookup(TierLookupStatus.Entitled, null, "malformed authority answer")))
            .ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EntitlementReasonCodes.TierAuthorityFault, AssertCouldNotTell(outcome).Code);
    }

    // ------------------------------------------------------------------ the answers proper

    [Theory]
    [InlineData(EntitlementTier.Supporter)]
    [InlineData(EntitlementTier.Lab)]
    public async Task AuthorityConfirmsAPledge_IsEntitledAtThatTier(EntitlementTier tier)
    {
        var outcome = await Over(
                StubReader.Yielding(EntitlementFixtures.TokenValue),
                new StubTierSource(TierLookup.Entitled(tier)))
            .ResolveAsync(TestContext.Current.CancellationToken);

        var entitled = Assert.IsType<EntitlementOutcome.Entitled>(outcome);
        Assert.Equal(tier, entitled.Tier);
    }

    [Fact]
    public async Task AuthorityAnswersNoPledge_IsTheOneAndOnlyRefusal()
    {
        // The positive control: without this, every "never NotEntitled" assertion above could
        // be satisfied by a capability that can never say NotEntitled at all.
        var outcome = await Over(
                StubReader.Yielding(EntitlementFixtures.TokenValue),
                new StubTierSource(TierLookup.NoEntitlement()))
            .ResolveAsync(TestContext.Current.CancellationToken);

        Assert.IsType<EntitlementOutcome.NotEntitled>(outcome);
        Assert.IsNotType<EntitlementOutcome.Unavailable>(outcome);
    }

    // ------------------------------------------------------------------------- the closure

    [Fact]
    public async Task EveryUndeterminedInput_IsUnavailableWithItsOwnReason_AndOnlyAnAuthorityRefusalIsNotEntitled()
    {
        // THE PACKET'S CENTRAL FACT, as one table. Each named test above proves one row; this
        // proves the SET: every way this capability can fail to determine the answer lands on
        // Unavailable, each with its OWN reason code (not one "something went wrong" bucket
        // that a caller would be tempted to collapse), and exactly one input in the whole
        // table produces a refusal.
        using var neverInstalled = HostFixtureDirectory.NeverInstalled();
        using var installed = HostFixtureDirectory.Installed();
        using var withBlob = HostFixtureDirectory.Installed().WithStoredBlob(EntitlementFixtures.CorruptBlob());

        (string Case, HostLoginEntitlement Capability, string ExpectedCode)[] undetermined =
        [
            ("shipping app never installed",
                Over(ReaderOver(neverInstalled, StubDecryptor.Returning(EntitlementFixtures.TokenUtf8()))),
                EntitlementReasonCodes.HostAppDataAbsent),
            ("installed, no stored login",
                Over(ReaderOver(installed, StubDecryptor.Returning(EntitlementFixtures.TokenUtf8()))),
                EntitlementReasonCodes.HostTokenAbsent),
            ("stored login decrypts to nothing",
                Over(ReaderOver(withBlob, StubDecryptor.Returning([]))),
                EntitlementReasonCodes.HostTokenEmpty),
            ("DPAPI refused the blob (other user / rotated entropy / corrupt)",
                Over(ReaderOver(withBlob, StubDecryptor.Refusing())),
                EntitlementReasonCodes.HostTokenUndecryptable),
            ("the store could not be read",
                Over(ReaderOver(withBlob, new ThrowingDecryptor())),
                EntitlementReasonCodes.HostReadFailed),
            ("platform has no DPAPI",
                Over(new UnsupportedPlatformTokenReader()),
                EntitlementReasonCodes.UnsupportedPlatform),
            ("no entitlement authority configured",
                Over(StubReader.Yielding(EntitlementFixtures.TokenValue)),
                EntitlementReasonCodes.TierAuthorityAbsent),
            ("authority unreachable",
                Over(StubReader.Yielding(EntitlementFixtures.TokenValue), new StubTierSource(TierLookup.Unreachable("network"))),
                EntitlementReasonCodes.TierAuthorityUnreachable),
            ("authority rejected the login",
                Over(StubReader.Yielding(EntitlementFixtures.TokenValue), new StubTierSource(TierLookup.Rejected())),
                EntitlementReasonCodes.TierAuthorityRejected),
            ("authority lookup threw",
                Over(StubReader.Yielding(EntitlementFixtures.TokenValue), new ThrowingTierSource(new TimeoutException())),
                EntitlementReasonCodes.TierAuthorityFault),
        ];

        var seenCodes = new List<string>();
        foreach (var (label, capability, expectedCode) in undetermined)
        {
            var outcome = await capability.ResolveAsync(TestContext.Current.CancellationToken);

            Assert.IsNotType<EntitlementOutcome.NotEntitled>(outcome);
            var unavailable = Assert.IsType<EntitlementOutcome.Unavailable>(outcome);
            Assert.Equal(expectedCode, unavailable.Reason.Code);
            Assert.False(string.IsNullOrWhiteSpace(label));
            seenCodes.Add(unavailable.Reason.Code);
        }

        // Non-vacuous, and the reasons are separate CAUSES rather than one "something went
        // wrong" bucket: ten inputs, ten distinct codes. A single shared code would be the
        // first step back towards a caller collapsing them all into "locked".
        Assert.Equal(10, seenCodes.Count);
        Assert.Equal(10, seenCodes.Distinct().Count());

        // The single refusal, from the single input entitled to produce one.
        var refusal = await Over(
                StubReader.Yielding(EntitlementFixtures.TokenValue),
                new StubTierSource(TierLookup.NoEntitlement()))
            .ResolveAsync(TestContext.Current.CancellationToken);
        Assert.IsType<EntitlementOutcome.NotEntitled>(refusal);
    }

    // ------------------------------------------------------------------- shape of the type

    [Fact]
    public void TheOutcomeTypeExposesNoBooleanThatCouldCollapseUnavailableIntoARefusal()
    {
        // The conflation's other door: a convenience like `bool IsAllowed` has to map
        // Unavailable onto true or false, and mapping it onto false rebuilds the defect one
        // layer up where no test in this file can see it. Equality members are the only
        // bool-returning members permitted on these types.
        string[] permitted = ["Equals", "op_Equality", "op_Inequality", "GetHashCode", "ToString"];
        Type[] types =
        [
            typeof(EntitlementOutcome),
            typeof(EntitlementOutcome.Entitled),
            typeof(EntitlementOutcome.NotEntitled),
            typeof(EntitlementOutcome.Unavailable),
        ];

        var offenders = new List<string>();
        foreach (var type in types)
        {
            foreach (var method in type.GetMethods())
            {
                if (method.ReturnType == typeof(bool) && !permitted.Contains(method.Name))
                {
                    offenders.Add($"{type.Name}.{method.Name}");
                }
            }

            foreach (var property in type.GetProperties())
            {
                if (property.PropertyType == typeof(bool))
                {
                    offenders.Add($"{type.Name}.{property.Name}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "EntitlementOutcome must expose no boolean collapse: " + string.Join(", ", offenders));
    }

    [Fact]
    public void MatchForcesEveryConsumerToSayWhatItDoesWhenTheAnswerIsUnknown()
    {
        EntitlementOutcome entitled = new EntitlementOutcome.Entitled(EntitlementTier.Lab, "d");
        EntitlementOutcome refused = new EntitlementOutcome.NotEntitled("d");
        EntitlementOutcome unknown = new EntitlementOutcome.Unavailable(
            new EntitlementReason(EntitlementReasonCodes.TierAuthorityUnreachable, "d"));

        static string Render(EntitlementOutcome outcome) => outcome.Match(
            tier => "open:" + tier,
            _ => "refuse-out-loud",
            reason => "explain:" + reason.Code);

        Assert.Equal("open:Lab", Render(entitled));
        Assert.Equal("refuse-out-loud", Render(refused));
        Assert.Equal("explain:" + EntitlementReasonCodes.TierAuthorityUnreachable, Render(unknown));

        // The three renderings are distinct — a consumer cannot accidentally render the
        // unknown case as the refusal.
        Assert.NotEqual(Render(refused), Render(unknown));
    }

    // -------------------------------------------------------------------------- the probe

    [Fact]
    public async Task Probe_ReadableLoginWithNoAuthority_IsDegraded_NeverAvailable()
    {
        var state = await Over(StubReader.Yielding(EntitlementFixtures.TokenValue))
            .ProbeAsync(TestContext.Current.CancellationToken);

        var degraded = Assert.IsType<CapabilityState.Degraded>(state);
        Assert.Equal(EntitlementReasonCodes.TierAuthorityAbsent, degraded.Reason.Code);
        Assert.False(string.IsNullOrWhiteSpace(degraded.SurvivingSemantics));
    }

    [Fact]
    public async Task Probe_ReadableLoginWithAnAuthority_IsAvailable()
    {
        var state = await Over(
                StubReader.Yielding(EntitlementFixtures.TokenValue),
                new StubTierSource(TierLookup.Entitled(EntitlementTier.Lab)))
            .ProbeAsync(TestContext.Current.CancellationToken);

        Assert.IsType<CapabilityState.Available>(state);
    }

    [Fact]
    public async Task Probe_NoShippingApp_IsDependencyMissing_AndNoDpapiPlatformIsUnavailable()
    {
        using var neverInstalled = HostFixtureDirectory.NeverInstalled();
        var missing = await Over(ReaderOver(neverInstalled, StubDecryptor.Refusing()))
            .ProbeAsync(TestContext.Current.CancellationToken);
        var dependency = Assert.IsType<CapabilityState.DependencyMissing>(missing);
        Assert.Equal(EntitlementReasonCodes.HostAppDataAbsent, dependency.Reason.Code);

        var noDpapi = await Over(new UnsupportedPlatformTokenReader())
            .ProbeAsync(TestContext.Current.CancellationToken);
        var unavailable = Assert.IsType<CapabilityState.Unavailable>(noDpapi);
        Assert.Equal(EntitlementReasonCodes.UnsupportedPlatform, unavailable.Reason.Code);
    }
}
