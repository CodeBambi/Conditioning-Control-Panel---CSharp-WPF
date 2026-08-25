using CcpClient.Desktop.Entitlement;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The recorded decision NOT to migrate the shipping WPF product's settings, as the four things
/// the user is actually told (<see cref="SettingsHandoverNotices"/>) and the one fact about this
/// build's composition that makes the telling necessary.
///
/// <para>Pure logic — no Avalonia runtime, and no file system: the notice performs no I/O at all,
/// which is the narrower half of the boundary rule <c>Entitlement/ShippingAppDataLocation.cs</c>
/// already states for that directory. That the System door really shows the line is
/// <c>SystemPageHeadlessTests</c>.</para>
///
/// <para><b>Why the collection attribute is here.</b> The last fact's SUBJECT is the default
/// settings path, so it reads <c>CCP_DATA_ROOT</c> through
/// <see cref="CompositionRoot.DefaultSettingsPath"/> and cannot be given a factory instead —
/// which is the second of the two fixes <c>ProcessEnvCollectionGuardTests</c> names, and it costs
/// this class its parallelism against <c>DataRootOverrideEnvTests</c>. The variable is READ here
/// and never written.</para>
/// </summary>
[Collection(nameof(ProcessEnvCollection))]
public class SettingsHandoverNoticesTests
{
    [Fact]
    public void TheNoticeNamesBOTHFoldersAndSaysTheWindowsAppIsNeitherImportedNorTouched()
    {
        // Two folders a real machine could never produce, so a sentence that hard-coded either
        // location instead of reporting the one it was handed cannot pass this.
        const string Client = @"Q:\ccp-client-root";
        const string Shipping = @"Q:\ccp-shipping-root";

        var line = SettingsHandoverNotices.Describe(Client, Shipping);

        Assert.Contains(Client, line, StringComparison.Ordinal);
        Assert.Contains(Shipping, line, StringComparison.Ordinal);
        Assert.Contains("does not import", line, StringComparison.Ordinal);
        Assert.Contains("never reads or writes", line, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNoticePromisesNOFUTUREIMPORT_BecauseTheDecisionIsADecisionAndNotAPlaceholder()
    {
        // §9 D7's rule applied to a sentence: a screen that says "not yet" is a placeholder with a
        // user reading it. If a later packet really does build an import, this fact is the one it
        // has to change deliberately.
        // Written out rather than looped: an assertion that lives only inside a foreach passes
        // silently over an empty collection, which is the shape VacuousShapeGuardTests refuses.
        var line = SettingsHandoverNotices.Describe(@"Q:\a", @"Q:\b");

        Assert.DoesNotContain("not yet", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("will be", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("coming soon", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("in a future", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("for now", line, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("", @"Q:\b")]
    [InlineData(@"Q:\a", "")]
    [InlineData("   ", @"Q:\b")]
    [InlineData(@"Q:\a", "   ")]
    public void AMissingFolderTHROWSRatherThanPrintingAnEmptyLocation(string client, string shipping)
    {
        // The sentence's whole value is that the user can tell the two stores apart. "This app's
        // settings are in ; the Windows app's are in Q:\b" would be worse than saying nothing, so
        // the caller's bug surfaces at the boundary rather than on the page.
        Assert.Throws<ArgumentException>(() => SettingsHandoverNotices.Describe(client, shipping));
    }

    [Fact]
    public void ThisBuildReallyKEEPSITSSETTINGSSOMEWHEREELSE_WhichIsWhatTheNoticeClaims()
    {
        // NOT a restatement of the sentence: the sentence asserts something about this product's
        // composition, and this is that assertion. The port's data root is
        // CompositionRoot.DefaultSettingsPath()'s directory; the shipping app's is
        // ShippingAppDataLocation.Resolve(). If a later change ever pointed one at the other, the
        // page would be telling the user to look in two places that are one place.
        //
        // The environment is READ, never written: CCP_DATA_ROOT stays exactly as the harness left
        // it, because exporting it here would blind the data-root isolation pin.
        var clientRoot = Path.GetDirectoryName(CompositionRoot.DefaultSettingsPath());
        var shippingRoot = ShippingAppDataLocation.Resolve();

        Assert.False(string.IsNullOrWhiteSpace(clientRoot));
        Assert.NotEqual(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(clientRoot!)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(shippingRoot)),
            StringComparer.OrdinalIgnoreCase);
    }
}
