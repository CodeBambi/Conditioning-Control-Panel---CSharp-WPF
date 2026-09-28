using System;
using System.IO;
using System.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Every sound the Avalonia head's CoreAudio.PlayOneShot callers name, relative to
/// Resources/sounds (the WPF head's logical path). Fails if CCP.Avalonia.csproj stops linking one.</summary>
public sealed class SharedSoundsPackagingTests
{
    [Theory]
    [InlineData("faucet_charge_drop.wav")]                                   // HeadlessSmoke, AudioProbe
    [InlineData("lvup.mp3")]                                                 // SessionCompleteWindow
    [InlineData("chime1.mp3")] [InlineData("chime2.mp3")] [InlineData("chime3.mp3")] // PopQuiz, Quiz, Chaos
    [InlineData("GOOD GIRL.mp3")] [InlineData("result.mp3")]                 // QuizWindow
    [InlineData("giggle5.mp3")] [InlineData("giggle6.wav")] [InlineData("giggle7.mp3")] [InlineData("giggle8.mp3")] // Quiz, AvatarTube
    [InlineData("bubbles/Pop.mp3")] [InlineData("bubbles/Pop2.mp3")] [InlineData("bubbles/Pop3.mp3")] // BubbleCount, Chaos
    [InlineData("chaos/dling.mp3")] [InlineData("chaos/thud.mp3")] [InlineData("chaos/boon_pick.mp3")]
    [InlineData("chaos/cards_in.mp3")] [InlineData("chaos/chip_pop.mp3")] [InlineData("chaos/count_tick.mp3")]
    [InlineData("chaos/pb_fanfare.mp3")] [InlineData("chaos/rank_settle.mp3")] [InlineData("chaos/sin_reveal.mp3")]
    [InlineData("chaos/surface.mp3")] [InlineData("chaos/ui_click.mp3")]    // ChaosOverlayWindow.ChaosSfx
    public void ReferencedSoundIsInOutput(string relative)
    {
        var path = Path.Combine(new[] { AppContext.BaseDirectory, "Resources", "sounds" }
            .Concat(relative.Split('/')).ToArray());
        Assert.True(File.Exists(path), $"missing {path}");
    }

    [Fact]
    public void ContentPackAudioIsNotShippedInBox()
    {
        var flashes = Path.Combine(AppContext.BaseDirectory, "Resources", "sounds", "flashes_audio");
        Assert.False(Directory.Exists(flashes) && Directory.EnumerateFiles(flashes, "*", SearchOption.AllDirectories).Any(),
            "flashes_audio ships as a content pack, not in-box (WPF $(ContentPackSoundsExclude))");
    }
}
