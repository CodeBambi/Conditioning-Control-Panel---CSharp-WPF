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

    /// <summary>ContentPackSounds.props: the pack audio (and sissyhypno's portraits) stays out,
    /// while the mod manifests stay in-box - a missing manifest is a hang, not a degrade.</summary>
    [Fact]
    public void ContentPackAudioIsNotShippedInBox()
    {
        var sounds = Path.Combine(AppContext.BaseDirectory, "Resources", "sounds");
        string[] packed = { "flashes_audio", "companion_audio/mods/builtin-bambisleep",
                            "companion_audio/mods/builtin-locked", "companion_audio/mods/builtin-sissyhypno" };
        string[] packExt = { ".mp3", ".wav", ".ogg", ".m4a" };
        var shipped = packed
            .Select(d => Path.Combine(new[] { sounds }.Concat(d.Split('/')).ToArray()))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
            .Where(f => packExt.Contains(Path.GetExtension(f).ToLowerInvariant())
                        || (f.Contains("builtin-sissyhypno") && Path.GetExtension(f).ToLowerInvariant() == ".png"))
            .ToArray();
        Assert.True(shipped.Length == 0, "content-pack files shipped in-box: " + string.Join(", ", shipped));

        foreach (var manifest in new[] { "builtin-bambisleep/bark_rules.json", "builtin-locked/bark_rules.json",
                                         "builtin-sissyhypno/avatar_manifest.json" })
            Assert.True(File.Exists(Path.Combine(new[] { sounds, "companion_audio", "mods" }
                .Concat(manifest.Split('/')).ToArray())), $"mod manifest missing: {manifest}");
    }
}
