using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Descent vocabulary plumbing ({petname} / {collective}).
///
/// These run with no ModService alive, which is exactly the vanilla case: no active mod means no
/// override, so both tokens must resolve to the defaults in <see cref="VocabTokens"/>. The pass is
/// inert as shipped (no language file uses a token yet), so the thing worth pinning is that it
/// stays inert for everything that ISN'T a token — a substitution pass that "helpfully" matched
/// {0}, {Petname} or { petname } would corrupt strings across nine languages.
/// </summary>
public class VocabTokensTests
{
    [Fact]
    public void VanillaPetName_ReplacesToken()
        => Assert.Equal($"good {VocabTokens.VanillaPetName}", VocabTokens.Apply("good {petname}"));

    [Fact]
    public void VanillaCollective_ReplacesToken()
        => Assert.Equal($"all {VocabTokens.VanillaCollective}", VocabTokens.Apply("all {collective}"));

    [Fact]
    public void BothTokens_AndRepeats_AllReplaced()
    {
        var result = VocabTokens.Apply("{petname}, {petname}, and every {collective}");
        Assert.Equal(
            $"{VocabTokens.VanillaPetName}, {VocabTokens.VanillaPetName}, and every {VocabTokens.VanillaCollective}",
            result);
    }

    [Theory]
    [InlineData("Start Flashes")]                 // the overwhelmingly common case: no braces at all
    [InlineData("You reached level {0}!")]        // string.Format placeholder must survive untouched
    [InlineData("{PetName}")]                     // case-sensitive on purpose
    [InlineData("{ petname }")]                   // exact braces on purpose
    [InlineData("{petnames}")]
    [InlineData("petname")]
    public void NonTokens_PassThroughUnchanged(string input)
        => Assert.Equal(input, VocabTokens.Apply(input));

    [Fact]
    public void NullAndEmpty_AreSafe()
    {
        Assert.Equal(string.Empty, VocabTokens.Apply(null));
        Assert.Equal(string.Empty, VocabTokens.Apply(string.Empty));
    }

    [Fact]
    public void ManifestCarriesTheOverrideFields()
    {
        // The wire shape a mod author writes. Nothing reads these yet beyond
        // ModService.GetPetNameOverride/GetCollectiveOverride, so the deserialization contract is
        // the part worth locking down.
        const string json = """
        { "petName": "bambi", "collective": "bambis" }
        """;
        var identity = JsonConvert.DeserializeObject<ModIdentity>(json)!;
        Assert.Equal("bambi", identity.PetName);
        Assert.Equal("bambis", identity.Collective);
    }

    [Fact]
    public void ManifestWithoutTheFields_LeavesThemNull()
    {
        var identity = JsonConvert.DeserializeObject<ModIdentity>("""{ "userTerm": "Subject" }""")!;
        Assert.Null(identity.PetName);
        Assert.Null(identity.Collective);
    }
}
