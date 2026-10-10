using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Arcademy;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The Arcademy host rules that live in Core: what the rest of the app reads from outside the game
/// (WPF ArcademyHostService.WalletOwnsSku / EquippedEmiOutfit, with the outfit half of WPF
/// EmiLockerOutfitTests), and the local media rules (request readers, the path guard, the sampler,
/// the folder counts, the phrase clip resolver with its mod audio policy).
/// </summary>
[Collection("ArcademyOutsideReads")]   // names the process-wide meta path
public class ArcademyOutsideReadsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-arc-core-" + Guid.NewGuid().ToString("N"));

    public ArcademyOutsideReadsTests()
    {
        Directory.CreateDirectory(_dir);
        ArcademyHostService.LiveMeta = null;
        ArcademyHostService.MetaPathOverride = Path.Combine(_dir, "arcademy_meta.json");
    }

    public void Dispose()
    {
        ArcademyHostService.MetaPathOverride = null;
        ArcademyHostService.LiveMeta = null;
        try { Directory.Delete(_dir, true); } catch { }
    }

    private void WriteMeta(string? outfit, params string[] owned)
    {
        var inv = new JObject();
        foreach (var sku in owned) inv[sku] = new JObject { ["n"] = 1 };
        var blob = new JObject { ["wallet"] = new JObject { ["inv"] = inv } };
        if (outfit != null) blob["lockerOutfit"] = outfit;
        File.WriteAllText(ArcademyHostService.MetaPathOverride!, blob.ToString());
    }

    // ---- wallet + outfit ----------------------------------------------------------------------

    [Fact]
    public void WalletOwnsSku_ReadsTheSavedFile_AndNeverThrows()
    {
        Assert.False(ArcademyHostService.WalletOwnsSku(ArcademyEconomy.SkuTubeMidnight));   // no file
        WriteMeta(null, ArcademyEconomy.SkuTubeMidnight);
        Assert.True(ArcademyHostService.WalletOwnsSku(ArcademyEconomy.SkuTubeMidnight));
        Assert.False(ArcademyHostService.WalletOwnsSku("emi_swim"));
        Assert.False(ArcademyHostService.WalletOwnsSku(""));
        File.WriteAllText(ArcademyHostService.MetaPathOverride!, "{ not json");
        Assert.False(ArcademyHostService.WalletOwnsSku(ArcademyEconomy.SkuTubeMidnight));
    }

    [Fact]
    public void WalletOwnsSku_ARowWithNoCountIsNotHeld()
    {
        File.WriteAllText(ArcademyHostService.MetaPathOverride!,
            "{\"wallet\":{\"inv\":{\"tube_midnight\":{\"n\":0},\"emi_swim\":{\"n\":2}}}}");
        Assert.False(ArcademyHostService.WalletOwnsSku("tube_midnight"));
        Assert.True(ArcademyHostService.WalletOwnsSku("emi_swim"));
    }

    [Fact]
    public void EquippedEmiOutfit_NeedsTheOutfitArmedAndBought()
    {
        Assert.Null(ArcademyHostService.EquippedEmiOutfit());
        WriteMeta("swim");   // armed, not bought
        Assert.Null(ArcademyHostService.EquippedEmiOutfit());
        WriteMeta("swim", "emi_swim");
        Assert.Equal("swim", ArcademyHostService.EquippedEmiOutfit());
        WriteMeta("../../escape", "emi_swim");
        Assert.Null(ArcademyHostService.EquippedEmiOutfit());
        WriteMeta("no-such-outfit", "emi_no-such-outfit");
        Assert.Null(ArcademyHostService.EquippedEmiOutfit());
    }

    [Fact]
    public void EquippedEmiOutfit_ReadsTheLiveStoreWhenTheArcademyIsOpen()
    {
        WriteMeta("labcoat", "emi_labcoat");   // the file says labcoat...
        var live = new ArcademyMetaStore(_ => { }, Path.Combine(_dir, "live.json"));
        ArcademyHostService.LiveMeta = live;
        Assert.Null(ArcademyHostService.EquippedEmiOutfit());   // ...the open campus has nothing armed
        Assert.False(ArcademyHostService.WalletOwnsSku("emi_labcoat"));
        ArcademyHostService.LiveMeta = null;
        Assert.Equal("labcoat", ArcademyHostService.EquippedEmiOutfit());
    }

    /// <summary>WPF EmiLockerOutfitTests.The_desk_and_the_locker_gate_the_same_four_prizes.</summary>
    [Fact]
    public void TheDeskAndTheLockerGateTheSameFourPrizes()
    {
        var locker = ArcademyAnimatedWebpHintTests.ReadWebSource("shell", "locker.js");
        Assert.Equal(new[] { "cheer", "labcoat", "swim", "varsity" }, ArcademyHostService.EmiOutfitSku.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (outfit, sku) in ArcademyHostService.EmiOutfitSku)
        {
            Assert.Equal("emi_" + outfit, sku);
            Assert.Contains(outfit + ": '" + sku + "'", locker);
            Assert.Contains(ArcademyEconomy.Catalog, row => row.Sku == sku);
        }
        Assert.Contains("OUTFIT_KEY = 'lockerOutfit'", locker);
        Assert.Equal("lockerOutfit", ArcademyHostService.EmiOutfitKey);
    }

    [Fact]
    public void EmiOutfitChanged_ReachesItsListeners_AndABadListenerIsSwallowed()
    {
        int hits = 0;
        Action ok = () => hits++;
        Action bad = () => throw new InvalidOperationException("listener");
        ArcademyHostService.EmiOutfitChanged += bad;
        ArcademyHostService.EmiOutfitChanged += ok;
        try { ArcademyHostService.RaiseEmiOutfitChanged(); }
        finally { ArcademyHostService.EmiOutfitChanged -= ok; ArcademyHostService.EmiOutfitChanged -= bad; }
        Assert.True(hits <= 1);   // a throwing listener never escapes the raise
    }

    // ---- request readers ----------------------------------------------------------------------

    [Fact]
    public void ReadRequestSubs_NullMeansAppWide_MalformedMeansRefused()
    {
        Assert.Null(ArcademyLocalMedia.ReadRequestSubs(JObject.Parse("{}")));
        Assert.Null(ArcademyLocalMedia.ReadRequestSubs(JObject.Parse("{\"subs\":null}")));
        Assert.Empty(ArcademyLocalMedia.ReadRequestSubs(JObject.Parse("{\"subs\":\"gifs\"}"))!);
        Assert.Empty(ArcademyLocalMedia.ReadRequestSubs(JObject.Parse("{\"subs\":[\"!!\",\"\"]}"))!);
        var subs = ArcademyLocalMedia.ReadRequestSubs(JObject.Parse("{\"subs\":[\"r/Gifs\",\"gifs\",\"cats\"]}"))!;
        Assert.Equal(2, subs.Count);
    }

    [Fact]
    public void ReadTag_IsSafeAsAKey()
    {
        Assert.Equal("untagged", ArcademyLocalMedia.ReadTag(JObject.Parse("{}")));
        Assert.Equal("target", ArcademyLocalMedia.ReadTag(JObject.Parse("{\"tag\":\" Target \"}")));
        Assert.Equal("ab", ArcademyLocalMedia.ReadTag(JObject.Parse("{\"tag\":\"a|:/b\"}")));
        Assert.Equal(24, ArcademyLocalMedia.ReadTag(JObject.Parse("{\"tag\":\"" + new string('x', 60) + "\"}")).Length);
        Assert.Equal("sort:noise|loop", ArcademyLocalMedia.TaggedBufferKey("noise", "loop"));
    }

    // ---- local media --------------------------------------------------------------------------

    private string Library()
    {
        var root = Path.Combine(_dir, "assets");
        foreach (var rel in new[] { "images/a/one.gif", "images/a/two.png", "images/b/three.jpg", "images/b/deep/four.gif", "videos/five.mp4", "images/a/notes.txt" })
        {
            var p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllBytes(p, new byte[] { 1, 2, 3 });
        }
        return root;
    }

    [Fact]
    public void ResolveAssetsFolder_RefusesAPathOutsideTheRoot()
    {
        var root = Library();
        Assert.NotNull(ArcademyLocalMedia.ResolveAssetsFolder(root, "images/a"));
        Assert.NotNull(ArcademyLocalMedia.ResolveAssetsFolder(root, "\\images\\b\\"));
        Assert.Null(ArcademyLocalMedia.ResolveAssetsFolder(root, "../"));
        Assert.Null(ArcademyLocalMedia.ResolveAssetsFolder(root, "images/../../.."));
        Assert.Null(ArcademyLocalMedia.ResolveAssetsFolder(root, "images/nope"));
        Assert.Null(ArcademyLocalMedia.ResolveAssetsFolder(root, "  "));
    }

    [Fact]
    public void SampleLocalAssets_HonoursFoldersKindAndTheBlacklist_AndIsStablePerRequest()
    {
        var root = Library();
        var none = new List<string>();
        var loops = ArcademyLocalMedia.SampleLocalAssets(root, "r1", 24, "loop", none, "", null, null);
        Assert.Equal(new[] { "images/a/one.gif", "images/b/deep/four.gif", "videos/five.mp4" }, loops.Select(r => r.Rel).OrderBy(r => r, StringComparer.Ordinal));
        Assert.All(loops, r => Assert.Equal("loop", r.Kind));
        Assert.Contains(loops, r => r.Rel == "images/b/deep/four.gif" && r.Src == "images/b/deep");

        var stillsInA = ArcademyLocalMedia.SampleLocalAssets(root, "r2", 24, "still", new List<string> { "images/a" }, "", null, null);
        Assert.Equal("images/a/two.png", Assert.Single(stillsInA).Rel);

        var blocked = ArcademyLocalMedia.SampleLocalAssets(root, "r3", 24, "loop", none, "", new[] { "images\\a\\one.gif" }, null);
        Assert.DoesNotContain(blocked, r => r.Rel == "images/a/one.gif");

        // A preset brings its own blacklist and stamps its own src.
        var preset = new AssetPreset { Id = "p1", Name = "P", DisabledAssetPaths = new HashSet<string> { "videos/five.mp4" } };
        var viaPreset = ArcademyLocalMedia.SampleLocalAssets(root, "r4", 24, "loop", none, "p1", null, new[] { preset });
        Assert.DoesNotContain(viaPreset, r => r.Rel == "videos/five.mp4");
        Assert.All(viaPreset, r => Assert.Equal("preset:p1", r.Src));

        // The same ask deals the same slice; a path that climbs out falls back to the two roots, never outside.
        var a = ArcademyLocalMedia.SampleLocalAssets(root, "same", 2, "loop", none, "", null, null).Select(r => r.Rel);
        var b = ArcademyLocalMedia.SampleLocalAssets(root, "same", 2, "loop", none, "", null, null).Select(r => r.Rel);
        Assert.Equal(a, b);
        var climbed = ArcademyLocalMedia.SampleLocalAssets(root, "r5", 24, "loop", new List<string> { "../.." }, "", null, null);
        Assert.All(climbed, r => Assert.False(r.Rel.StartsWith("..", StringComparison.Ordinal)));
        Assert.Empty(ArcademyLocalMedia.SampleLocalAssets(Path.Combine(_dir, "nope"), "r6", 8, "loop", none, "", null, null));
    }

    [Fact]
    public void SampleLocalAssets_AnAnimatedWebpIsALoop_AStillOneNeverEntersTheLoopLane()
    {
        var root = Path.Combine(_dir, "webp");
        Directory.CreateDirectory(Path.Combine(root, "images"));
        var anim = new byte[32]; var still = new byte[32];
        foreach (var b in new[] { anim, still })
        {
            "RIFF".Select((c, i) => b[i] = (byte)c).ToArray();
            "WEBP".Select((c, i) => b[8 + i] = (byte)c).ToArray();
            "VP8X".Select((c, i) => b[12 + i] = (byte)c).ToArray();
        }
        anim[20] = 0x02;
        File.WriteAllBytes(Path.Combine(root, "images", "moving.webp"), anim);
        File.WriteAllBytes(Path.Combine(root, "images", "quiet.webp"), still);

        var loops = ArcademyLocalMedia.SampleLocalAssets(root, "w1", 8, "loop", new List<string>(), "", null, null);
        var only = Assert.Single(loops);
        Assert.Equal("images/moving.webp", only.Rel);
        Assert.True(only.Animated);

        var stills = ArcademyLocalMedia.SampleLocalAssets(root, "w2", 8, "still", new List<string>(), "", null, null);
        Assert.Contains(stills, r => r.Rel == "images/quiet.webp" && r.Kind == "still" && !r.Animated);
        Assert.Contains(stills, r => r.Rel == "images/moving.webp" && r.Kind == "loop" && r.Animated);

        var (gifs, plain) = ArcademyLocalMedia.BuildLocalAssets(root, null, new Random(1));
        Assert.Contains(gifs, g => g.Rel == "images/moving.webp" && g.Animated);
        Assert.Equal(new[] { "images/quiet.webp" }, plain);
    }

    [Fact]
    public void BuildLocalFolders_CountsRecursively_AndSkipsUncheckedFiles()
    {
        var root = Library();
        var rows = ArcademyLocalMedia.BuildLocalFolders(root, new[] { "images/b/three.jpg" });
        JToken Row(string path) => rows.Single(r => (string?)r["path"] == path);
        Assert.Equal(2, (int)Row("images")["gifs"]!);
        Assert.Equal(1, (int)Row("images")["stills"]!);        // two.png; three.jpg is unchecked
        Assert.Equal(1, (int)Row("images/b")["gifs"]!);        // deep/four.gif credits its parents
        Assert.Equal(1, (int)Row("images/b/deep")["gifs"]!);
        Assert.Equal(1, (int)Row("videos")["videos"]!);
        Assert.Empty(ArcademyLocalMedia.BuildLocalFolders(Path.Combine(_dir, "nope"), null));
    }

    // ---- phrase clips: recorded only, Bambi clips gated to their mods ---------------------------

    [Fact]
    public void TriggerAudio_BambiClipsNeverReachCcpDefaultOrLocked_AndNothingPlaysWhenMuted()
    {
        var shared = Path.Combine(_dir, "sub_audio");
        Directory.CreateDirectory(shared);
        File.WriteAllBytes(Path.Combine(shared, "GOOD GIRL.mp3"), new byte[] { 1 });

        var (_, forDefault) = ArcademyLocalMedia.TriggerAudioDirs(true, BuiltInMods.CCPDefaultId, null, shared);
        Assert.Null(forDefault);
        Assert.Null(ArcademyLocalMedia.ResolveTriggerAudio("good girl", null, forDefault));
        Assert.Null(ArcademyLocalMedia.TriggerAudioDirs(true, BuiltInMods.LockedId, null, shared).SharedDir);
        Assert.Null(ArcademyLocalMedia.TriggerAudioDirs(true, null, null, shared).SharedDir);

        var (_, forBambi) = ArcademyLocalMedia.TriggerAudioDirs(true, BuiltInMods.BambiSleepId, null, shared);
        Assert.Equal(shared, forBambi);
        var hit = ArcademyLocalMedia.ResolveTriggerAudio("good girl", null, forBambi);
        Assert.NotNull(hit);
        Assert.False(hit!.Value.FromMod);
        Assert.Equal("GOOD GIRL.mp3", hit.Value.FileName);

        // The whisper mute closes both folders; a phrase with no recorded clip stays silent (no synthetic speech).
        Assert.Equal((null, null), ArcademyLocalMedia.TriggerAudioDirs(false, BuiltInMods.BambiSleepId, _dir, shared));
        Assert.Null(ArcademyLocalMedia.ResolveTriggerAudio("never recorded", null, forBambi));
        Assert.Null(ArcademyLocalMedia.ResolveTriggerAudio("../GOOD GIRL", null, forBambi));
    }

    [Fact]
    public void TriggerAudio_TheModsOwnClipWinsOverTheBundledOne()
    {
        var shared = Path.Combine(_dir, "sub_audio");
        var mod = Path.Combine(_dir, "mod");
        var modAudio = Path.Combine(mod, "resources", "sounds", "flashes_audio");
        Directory.CreateDirectory(shared);
        Directory.CreateDirectory(modAudio);
        File.WriteAllBytes(Path.Combine(shared, "DROP.mp3"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(modAudio, "drop.ogg"), new byte[] { 1 });

        var (modDir, sharedDir) = ArcademyLocalMedia.TriggerAudioDirs(true, "some-creator-mod", mod, shared);
        Assert.Equal(modAudio, modDir);
        var hit = ArcademyLocalMedia.ResolveTriggerAudio("Drop", modDir, sharedDir);
        Assert.True(hit!.Value.FromMod);
        Assert.Equal("drop.ogg", hit.Value.FileName);
    }
}
