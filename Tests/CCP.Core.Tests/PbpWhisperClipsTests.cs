using System.Linq;
using ConditioningControlPanel.Services.PieceByPiece;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class PbpWhisperClipsTests
{
    [Fact]
    public void OwnBrainDrainClipsWin()
    {
        var urls = PbpWhisperClips.Build(new[] { "b.mp3", "a one.wav", "notes.txt" }, true,
            new[] { "BAMBI FREEZE.mp3" }, new[] { "drop.mp3" });
        Assert.Equal(new[] { "https://ccp.assets/braindrain/a%20one.wav", "https://ccp.assets/braindrain/b.mp3" }, urls);
    }

    [Fact]
    public void BambiClipsOnlyForModsThatMayUseThem()
    {
        var bambi = PbpWhisperClips.Build(null, true, new[] { "BAMBI FREEZE.mp3" }, new[] { "drop.mp3" });
        Assert.Equal("https://ccp.subaudio/BAMBI%20FREEZE.mp3", Assert.Single(bambi));

        var neutral = PbpWhisperClips.Build(null, false, new[] { "BAMBI FREEZE.mp3" }, new[] { "drop.mp3" });
        Assert.Equal("https://ccp.words/drop.mp3", Assert.Single(neutral));
    }

    [Fact]
    public void NothingAnywhereMeansNoWhispers()
        => Assert.Empty(PbpWhisperClips.Build(new[] { "readme.txt" }, true, null, null));

    [Fact]
    public void ListIsCapped()
    {
        var many = Enumerable.Range(0, 100).Select(i => $"c{i:000}.mp3");
        Assert.Equal(PbpWhisperClips.MaxClips, PbpWhisperClips.Build(many, false, null, null).Count);
    }
}
