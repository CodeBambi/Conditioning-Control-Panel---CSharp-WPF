using System;
using ConditioningControlPanel.Services.GoonGame;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The Goon Game's share card on the host side: only real PNG bytes, a known action and a
/// tame file name ever get as far as the clipboard or the save dialog.</summary>
public class GoonShareCardTests
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static JObject Frame(string action = "copy", string id = "sc1", string? png = null, string? name = null)
        => new() { ["type"] = "share-card", ["id"] = id, ["action"] = action, ["png"] = png ?? Convert.ToBase64String(TinyPng), ["name"] = name };

    [Fact]
    public void AGoodCopyFrameParses()
    {
        var r = GoonShareCard.Parse(Frame(), out var err);
        Assert.NotNull(r);
        Assert.Equal("", err);
        Assert.Equal("copy", r!.Action);
        Assert.Equal(TinyPng.Length, r.Png.Length);
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("")]
    [InlineData("open")]
    public void UnknownActionsAreRefused(string action)
    {
        Assert.Null(GoonShareCard.Parse(Frame(action), out var err));
        Assert.Equal("bad-action", err);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../x")]
    [InlineData("a b")]
    [InlineData("0123456789012345678901234")]
    public void BadIdsAreRefused(string id)
    {
        Assert.Null(GoonShareCard.Parse(Frame(id: id), out var err));
        Assert.Equal("bad-id", err);
    }

    [Fact]
    public void NonPngBytesAreRefused()
    {
        Assert.Null(GoonShareCard.Parse(Frame(png: Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5 })), out var e1));
        Assert.Equal("bad-format", e1);
        Assert.Null(GoonShareCard.Parse(Frame(png: "not base64 at all!"), out var e2));
        Assert.Equal("bad-format", e2);
    }

    [Fact]
    public void OversizedPayloadIsRefusedBeforeDecoding()
    {
        Assert.Null(GoonShareCard.Parse(Frame(png: new string('A', GoonShareCard.MaxBase64Chars + 4)), out var err));
        Assert.Equal("too-big", err);
    }

    [Theory]
    [InlineData("goon-game-2026-09-24.png", "goon-game-2026-09-24.png")]
    [InlineData(@"..\..\Windows\evil.exe", "Windows-evil.exe.png")]
    [InlineData("C:/x/y", "C-x-y.png")]
    [InlineData("", "goon-game.png")]
    [InlineData(null, "goon-game.png")]
    [InlineData("card.PNG", "card.png")]
    public void FileNamesAreTamed(string? given, string expected)
        => Assert.Equal(expected, GoonShareCard.SafeFileName(given));
}
