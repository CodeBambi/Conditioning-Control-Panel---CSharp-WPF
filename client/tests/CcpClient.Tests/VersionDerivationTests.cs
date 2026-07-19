using System.Reflection;
using CcpClient.Desktop;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Version-derivation tests (client/docs/release-publish-gates.md §2/§4). Assert the
/// DERIVATION RULES between the assembly attributes — never blind full-string equality
/// (InformationalVersion may carry a +&lt;SourceRevisionId&gt; suffix) and never a
/// hardcoded version value. The artifact-name ↔ authority agreement is verified against
/// the real published artifact by the matrix scripts (gate 3), not here.
/// </summary>
public class VersionDerivationTests
{
    private static readonly Assembly EntryAssembly = typeof(Program).Assembly;

    [Fact]
    public void InformationalVersion_Exists_Parses_AndPrefixDerivesFromAssemblyVersion()
    {
        var info = VersionSelfCheck.GetInformationalVersion(EntryAssembly);
        Assert.False(string.IsNullOrWhiteSpace(info), "AssemblyInformationalVersionAttribute missing on the entry assembly");

        // Canonical display = InformationalVersion; strip any +<SourceRevisionId> suffix.
        var prefix = info!.Split('+')[0];
        Assert.True(Version.TryParse(prefix, out var infoVersion), $"InformationalVersion prefix '{prefix}' must parse as a version");

        var asmVersion = EntryAssembly.GetName().Version;
        Assert.NotNull(asmVersion);

        // Derivation rule: AssemblyVersion = Version numeric padded to four parts —
        // every component of the informational prefix must match the assembly version.
        Assert.Equal(asmVersion!.Major, infoVersion!.Major);
        Assert.Equal(asmVersion.Minor, infoVersion.Minor);
        Assert.Equal(asmVersion.Build, infoVersion.Build < 0 ? 0 : infoVersion.Build);
    }

    [Fact]
    public void AssemblyVersion_And_FileVersion_Agree_Numerically()
    {
        var asmVersion = EntryAssembly.GetName().Version;
        var fileAttr = EntryAssembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
        Assert.NotNull(asmVersion);
        Assert.NotNull(fileAttr);

        Assert.True(Version.TryParse(fileAttr!.Version, out var fileVersion),
            $"FileVersion '{fileAttr.Version}' must parse as a version");
        Assert.Equal(asmVersion, fileVersion);
    }

    [Fact]
    public void VersionSelfCheck_Prints_InformationalVersion_AndExitsZero()
    {
        using var writer = new StringWriter();
        var exit = VersionSelfCheck.Run(EntryAssembly, writer);
        Assert.Equal(0, exit);

        var info = VersionSelfCheck.GetInformationalVersion(EntryAssembly);
        Assert.Equal($"version: {info}", writer.ToString().Trim());
    }
}
