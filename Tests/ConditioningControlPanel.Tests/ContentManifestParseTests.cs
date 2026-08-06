using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// content-manifest.json decides whether ANY pack is reachable, so nothing incidental about the
/// bytes may sink it. The live file is served with a UTF-8 BOM and JToken.Parse rejects a leading
/// U+FEFF outright; today's ReadAsStringAsync strips it, but that must not be the only thing
/// standing between a user and their voice lines.
/// </summary>
public class ContentManifestParseTests
{
    private const string EnvelopeJson =
        "{\"schemaVersion\":1,\"cycle\":\"v6.6.0\",\"packs\":[" +
        "{\"id\":\"audio-base\",\"file\":\"audio-base.zip\",\"sizeBytes\":123,\"contentVersion\":2}]}";

    private const string ArrayJson =
        "[{\"id\":\"mod-bambi\",\"file\":\"mod-bambi.zip\",\"sizeBytes\":456}]";

    [Fact]
    public void EnvelopeFormParses()
    {
        var packs = ReleaseContentService.ParseManifest(EnvelopeJson);
        Assert.NotNull(packs);
        Assert.Equal("audio-base", Assert.Single(packs!).Id);
    }

    [Fact]
    public void BareArrayFormParses()
        => Assert.Equal("mod-bambi", Assert.Single(ReleaseContentService.ParseManifest(ArrayJson)!).Id);

    [Theory]
    [InlineData("\uFEFF")]        // the live manifest is served with a UTF-8 BOM
    [InlineData("\uFEFF\r\n  ")]
    [InlineData("  \t\r\n")]
    [InlineData("\u200B")]
    public void ALeadingBomOrWhitespaceNeverDecidesWhetherPacksResolve(string prefix)
    {
        Assert.Equal("audio-base", Assert.Single(ReleaseContentService.ParseManifest(prefix + EnvelopeJson)!).Id);
        Assert.Equal("mod-bambi", Assert.Single(ReleaseContentService.ParseManifest(prefix + ArrayJson)!).Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\uFEFF")]
    [InlineData("not json at all")]
    public void JunkParsesToNullRatherThanThrowing(string json)
        => Assert.Null(ReleaseContentService.ParseManifest(json));
}
