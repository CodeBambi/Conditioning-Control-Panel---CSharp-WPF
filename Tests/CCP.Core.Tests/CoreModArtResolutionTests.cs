using System;
using System.IO;
using ConditioningControlPanel;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The file-path half of WPF's ModResourceResolver, now in CoreModArt.</summary>
public sealed class CoreModArtResolutionTests : IDisposable
{
    private readonly string _mod = Directory.CreateTempSubdirectory("ccp-modart-").FullName;

    public void Dispose() => Directory.Delete(_mod, recursive: true);

    private string Put(string rel)
    {
        var full = Path.Combine(_mod, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
        return full;
    }

    [Fact]
    public void ModOverride_ResolvesFromResources_AndMissIsNull()
    {
        var hit = Put("resources/features/flash.png");
        Assert.Equal(hit, CoreModArt.ResolveOverride("features/flash.png", null, _mod));
        Assert.Equal(hit, CoreModArt.ResolveOverride(@"features\flash.png", null, _mod));
        Assert.Null(CoreModArt.ResolveOverride("features/other.png", null, _mod));
        Assert.Null(CoreModArt.ResolveOverride("features/flash.png", null, null));
    }

    [Fact]
    public void EventSkin_OutranksMod_AndUnsafeIdIsIgnored()
    {
        var modHit = Put("resources/bubble.png");
        var skinId = "ccp-test-" + Guid.NewGuid().ToString("N");
        var skinDir = Path.Combine(CorePaths.UserData, CoreModArt.EventSkinRoot, skinId);
        try
        {
            Directory.CreateDirectory(skinDir);
            var skinHit = Path.Combine(skinDir, "bubble.png");
            File.WriteAllText(skinHit, "x");

            Assert.Equal(skinHit, CoreModArt.ResolveOverride("bubble.png", skinId, _mod));
            Assert.Equal(modHit, CoreModArt.ResolveOverride("bubble.png", "../" + skinId, _mod));
            Assert.Equal(modHit, CoreModArt.ResolveOverride("bubble.png", null, _mod));
        }
        finally { Directory.Delete(skinDir, recursive: true); }
    }

    [Theory]
    [InlineData("../resources/bubble.png")]
    [InlineData(@"..\resources\bubble.png")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    public void Traversal_IsRejected(string path)
    {
        Put("resources/bubble.png");
        Assert.Null(CoreModArt.ModFile(path, Path.Combine(_mod, "resources", "x")));
        Assert.Null(CoreModArt.ResolveOverride(path, null, _mod));
        Assert.Null(CoreModArt.ModAudioFile(path, _mod));
    }

    [Fact]
    public void Audio_ProbesSounds_ThenTheWavMp3Twin()
    {
        var wav = Put("resources/sounds/bubbles/Pop.wav");
        Assert.Equal(wav, CoreModArt.ModAudioFile("bubbles/Pop.mp3", _mod));
        var mp3 = Put("resources/sounds/bubbles/Pop.mp3");
        Assert.Equal(mp3, CoreModArt.ModAudioFile("bubbles/Pop.mp3", _mod));
        Assert.Null(CoreModArt.ModAudioFile("giggle5.mp3", _mod));
    }

    [Fact]
    public void Linux_ProbesCaseInsensitively()
    {
        if (OperatingSystem.IsWindows()) return; // exact probe on Windows, by design
        var hit = Put("Resources/Cards/Lv_10.PNG");
        Assert.Equal(hit, CoreModArt.ResolveOverride("cards/lv_10.png", null, _mod));
    }

    [Fact]
    public void Linux_FindsBackslashNamedFileFromCcpmodExtraction()
    {
        if (OperatingSystem.IsWindows()) return; // a '\' file name cannot exist there
        // ZipFile.ExtractToDirectory on Unix writes a "resources\cards\x.png" entry as ONE file.
        var flat = Path.Combine(_mod, @"resources\Cards\x.png");
        File.WriteAllText(flat, "x");
        Assert.Equal(flat, CoreModArt.ResolveOverride("cards/x.png", null, _mod));
    }
}
