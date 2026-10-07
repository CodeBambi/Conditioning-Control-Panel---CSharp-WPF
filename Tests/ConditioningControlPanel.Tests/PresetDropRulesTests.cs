using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1331: a catalogue preset dragged in from Explorer did nothing. The drop
/// keyed on the exact ".preset.json" suffix, so a browser's second download
/// ("x.preset (1).json") or a renamed "x.json" fell through to the enhancement import.
/// Presets are now recognised by content, with the name as the fast path.
/// </summary>
public class PresetDropRulesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-preset-drop-" + Guid.NewGuid().ToString("N"));

    public PresetDropRulesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static string PresetJson()
        => new PresetFileService().SerializePreset(new Preset { Id = "abc-123", Name = "Morning Drift" });

    // The catalogue stores and serves the asset with its keys sorted (canonicalizeBundle).
    private static string CataloguePresetJson()
    {
        var obj = JsonNode.Parse(PresetJson())!.AsObject();
        var sorted = new JsonObject();
        foreach (var kv in obj.OrderBy(k => k.Key, StringComparer.Ordinal))
            sorted[kv.Key] = kv.Value?.DeepClone();
        return sorted.ToJsonString();
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Theory]
    [InlineData("C:\\x\\Morning Drift.preset.json", true)]
    [InlineData("C:\\x\\Morning Drift.preset (1).json", true)]
    [InlineData("C:\\x\\Morning Drift.preset(12).json", true)]
    [InlineData("C:\\x\\Morning Drift.preset - Copy.json", true)]
    [InlineData("C:\\x\\Morning Drift.preset - Copy (2).json", true)]
    [InlineData("C:\\x\\MORNING.PRESET.JSON", true)]
    [InlineData("C:\\x\\Morning Drift.json", false)]
    [InlineData("C:\\x\\Morning Drift.session.json", false)]
    [InlineData("C:\\x\\preset.json", false)]
    [InlineData("C:\\x\\Morning Drift.preset.json.txt", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsPresetFileName_AcceptsTheCopiesBrowsersMake(string? path, bool expected)
        => Assert.Equal(expected, PresetDropRules.IsPresetFileName(path));

    [Fact]
    public void LooksLikePresetJson_AcceptsAnExportedPreset()
        => Assert.True(PresetDropRules.LooksLikePresetJson(PresetJson()));

    [Fact]
    public void LooksLikePresetJson_AcceptsTheCataloguesSortedCopy()
        => Assert.True(PresetDropRules.LooksLikePresetJson(CataloguePresetJson()));

    [Theory]
    [InlineData("{ \"$schema\": \"ccp-enhancement/v1\", \"id\": \"x\", \"flashEnabled\": true, \"masterVolume\": 3 }")]
    [InlineData("{ \"Id\": \"x\", \"FlashEnabled\": true, \"MasterVolume\": 30 }")] // settings.json shape (PascalCase)
    [InlineData("{ \"id\": \"x\", \"name\": \"y\", \"durationMinutes\": 30, \"settings\": { \"flashEnabled\": true, \"masterVolume\": 3 } }")] // session
    [InlineData("{ \"id\": \"\", \"flashEnabled\": true, \"masterVolume\": 3 }")]
    [InlineData("{ \"id\": \"x\", \"flashEnabled\": true }")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData(null)]
    public void LooksLikePresetJson_RejectsOtherJson(string? json)
        => Assert.False(PresetDropRules.LooksLikePresetJson(json));

    [Fact]
    public void FileIsPreset_RenamedBrowserDownload()
        => Assert.True(PresetDropRules.FileIsPreset(Write("Morning Drift.preset (1).json", CataloguePresetJson())));

    [Fact]
    public void FileIsPreset_PlainJsonPreset()
        => Assert.True(PresetDropRules.FileIsPreset(Write("my preset.json", CataloguePresetJson())));

    [Fact]
    public void FileIsPreset_EnhancementJsonIsNot()
    {
        var enh = "{\n  \"$schema\": \"ccp-enhancement/v1\",\n  \"version\": 1,\n  \"title\": \"x\"\n}";
        Assert.False(PresetDropRules.FileIsPreset(Write("loop.json", enh)));
        Assert.False(PresetDropRules.FileIsPreset(Write("loop.ccpenh.json", enh)));
    }

    [Fact]
    public void FileIsPreset_GarbageJsonIsNot()
    {
        Assert.False(PresetDropRules.FileIsPreset(Write("garbage.json", "{\"hello\": \"world\"}")));
        Assert.False(PresetDropRules.FileIsPreset(Write("broken.json", "this is { not json")));
    }

    [Fact]
    public void FileIsPreset_NonJsonIsNot()
    {
        Assert.False(PresetDropRules.FileIsPreset(Write("preset.txt", CataloguePresetJson())));
        Assert.False(PresetDropRules.FileIsPreset(Write("pic.png", "not a picture")));
    }

    [Fact]
    public void FileIsPreset_MissingFileIsNot()
        => Assert.False(PresetDropRules.FileIsPreset(Path.Combine(_dir, "gone.json")));

    [Fact]
    public void FileIsPreset_SeesAnEditToTheSameFile()
    {
        var path = Write("same.json", "{\"hello\": \"world\"}");
        Assert.False(PresetDropRules.FileIsPreset(path));
        File.WriteAllText(path, CataloguePresetJson());
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        Assert.True(PresetDropRules.FileIsPreset(path));
    }

    [Fact]
    public void ImportPath_AcceptsTheRenamedDownload()
    {
        // The validator used to refuse anything not named exactly *.preset.json.
        var path = Write("Morning Drift.preset (1).json", CataloguePresetJson());
        var svc = new PresetFileService();
        Assert.True(svc.ValidatePresetFile(path, out var error), error);
        Assert.Equal("Morning Drift", svc.ImportPreset(path)!.Name);
    }
}
