using System.IO;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Deeper library wave 1: the import path's pure rules. A dropped settings.json
/// used to reach the serializer and produce an "Import failed" modal; a file
/// already in the library used to come back as "Name (2)".
/// </summary>
public class DeeperLibraryWave1Tests
{
    [Theory]
    [InlineData("C:\\x\\foo.ccpenh.json", true)]
    [InlineData("C:\\x\\FOO.CCPENH.JSON", true)]
    [InlineData("C:\\x\\settings.json", true)]
    [InlineData("C:\\x\\foo.mp4", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsImportablePath_IsAnExtensionGateOnly(string? path, bool expected)
        => Assert.Equal(expected, EnhancementImportRules.IsImportablePath(path));

    [Fact]
    public void LooksLikeEnhancementJson_AcceptsTheSchemaTag()
    {
        var head = "{\n  \"$schema\": \"ccp-enhancement/v1\",\n  \"version\": 1,";
        Assert.True(EnhancementImportRules.LooksLikeEnhancementJson(head));
    }

    [Theory]
    [InlineData("{ \"BrowserVideoEngineEnabled\": true, \"DeeperPlayerVolume\": 80 }")]
    [InlineData("{ \"$schema\": \"http://json-schema.org/draft-07/schema#\" }")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("ccp-enhancement/v1")]
    public void LooksLikeEnhancementJson_RejectsOtherJson(string? head)
        => Assert.False(EnhancementImportRules.LooksLikeEnhancementJson(head));

    [Fact]
    public void NormalizedContentHash_IgnoresFormatting()
    {
        var compact = "{\"$schema\":\"ccp-enhancement/v1\",\"version\":1,\"regions\":[{\"id\":\"a\",\"start\":0,\"end\":5}]}";
        var pretty = "{\n  \"$schema\": \"ccp-enhancement/v1\",\r\n  \"version\": 1,\n  \"regions\": [ { \"id\": \"a\", \"start\": 0, \"end\": 5 } ]\n}\n";
        Assert.Equal(EnhancementImportRules.NormalizedContentHash(compact),
                     EnhancementImportRules.NormalizedContentHash(pretty));
    }

    [Fact]
    public void NormalizedContentHash_DiffersOnContent()
    {
        var a = "{\"$schema\":\"ccp-enhancement/v1\",\"version\":1}";
        var b = "{\"$schema\":\"ccp-enhancement/v1\",\"version\":2}";
        Assert.NotEqual(EnhancementImportRules.NormalizedContentHash(a),
                        EnhancementImportRules.NormalizedContentHash(b));
    }

    [Fact]
    public void NormalizedContentHash_FallsBackForNonJson()
    {
        var h = EnhancementImportRules.NormalizedContentHash("not json at all");
        Assert.Equal(64, h.Length);
        Assert.Equal(h, EnhancementImportRules.NormalizedContentHash("not json at all"));
    }

    [Fact]
    public void IsInsideFolder_MatchesDirectChildrenOnly()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ccp-lib-test");
        Assert.True(EnhancementImportRules.IsInsideFolder(Path.Combine(folder, "a.ccpenh.json"), folder));
        Assert.True(EnhancementImportRules.IsInsideFolder(Path.Combine(folder, "a.ccpenh.json"), folder + Path.DirectorySeparatorChar));
        Assert.False(EnhancementImportRules.IsInsideFolder(Path.Combine(folder, "sub", "a.ccpenh.json"), folder));
        Assert.False(EnhancementImportRules.IsInsideFolder(Path.Combine(Path.GetTempPath(), "elsewhere", "a.ccpenh.json"), folder));
        Assert.False(EnhancementImportRules.IsInsideFolder(null, folder));
        Assert.False(EnhancementImportRules.IsInsideFolder("x", null));
    }

    [Fact]
    public void FileLooksLikeEnhancement_ReadsTheHead()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-import-rules-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var enh = Path.Combine(dir, "a.ccpenh.json");
            File.WriteAllText(enh, "{ \"$schema\": \"ccp-enhancement/v1\", \"version\": 1 }");
            var other = Path.Combine(dir, "settings.json");
            File.WriteAllText(other, "{ \"DeeperPlayerVolume\": 80 }");
            Assert.True(EnhancementImportRules.FileLooksLikeEnhancement(enh));
            Assert.False(EnhancementImportRules.FileLooksLikeEnhancement(other));
            Assert.False(EnhancementImportRules.FileLooksLikeEnhancement(Path.Combine(dir, "missing.json")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
