using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Features.Intake;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-055: the ONE active-pool definition (asset deselection honored everywhere —
/// #762/#798/#619 parity). Pins the normalization verbatim (FlashService.GetMediaFiles
/// :2855-2867 / IntakeHostService.cs:776-790 / DtrhAssetManifest.cs:116-165), the
/// UseAssetWhitelist gate (AppSettings.cs:1637 documented contract), the distinct
/// skip-vs-deselect semantics, the both-folders accepted bound, both consumers agreeing
/// on one fixture, and the persisted AssetSelectionDocument on SP-005 machinery.
/// </summary>
public sealed class AssetActivePoolTests : IDisposable
{
    private readonly string _root;
    private readonly string _userMedia;
    private readonly List<string> _log = [];

    public AssetActivePoolTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ccp-sp055-pool-" + Guid.NewGuid().ToString("N"));
        _userMedia = Path.Combine(_root, "assets");
        var images = Path.Combine(_userMedia, "images");
        var videos = Path.Combine(_userMedia, "videos");
        Directory.CreateDirectory(Path.Combine(images, "sub"));
        Directory.CreateDirectory(videos);
        File.WriteAllBytes(Path.Combine(images, "a.jpg"), [1]);
        File.WriteAllBytes(Path.Combine(images, "sub", "b.png"), [1]);
        File.WriteAllBytes(Path.Combine(images, "CASE.JPG"), [1]);
        File.WriteAllBytes(Path.Combine(images, "photo.wmv"), [1]); // media-like, undecodable → counted skipped
        using (var fs = File.Create(Path.Combine(images, "big.gif"))) fs.SetLength(51L * 1024 * 1024); // over cap → skipped
        File.WriteAllBytes(Path.Combine(videos, "v.mp4"), [1]);
        File.WriteAllBytes(Path.Combine(videos, "v2.mkv"), [1]);    // media-like, undecodable → counted skipped
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ } }

    private static HashSet<string> Disabled(params string[] paths) => DtrhUserMedia.BuildDisabledSet(paths);

    // ---------- the one definition: IsAssetActive gate order ----------

    [Fact]
    public void EmptySet_EverythingActive_PreFixBehavior()
    {
        // IntakeHostService.cs:784 — empty set = nothing deselected = everything active.
        var m = DtrhUserMedia.Build(_userMedia, "http://127.0.0.1:9", _log.Add,
            Disabled(), useWhitelist: true);
        Assert.Equal(3, m.Images.Count); // a.jpg, sub/b.png, CASE.JPG (big.gif is over cap)
        Assert.Single(m.Videos);
        Assert.Equal(3, m.Skipped); // photo.wmv + big.gif + v2.mkv — deselection NEVER counted
    }

    [Fact]
    public void WhitelistOff_NonEmptySet_AllActive_AndLogged()
    {
        // AppSettings.cs:1637 documented contract: the flag gates the whole mechanism.
        var m = DtrhUserMedia.Build(_userMedia, "http://127.0.0.1:9", _log.Add,
            Disabled("images/a.jpg"), useWhitelist: false);
        Assert.Equal(3, m.Images.Count);
        Assert.Single(m.Videos);
        // The consult's trap mitigation: present-but-ungated set surfaces as a counts-only line.
        Assert.Contains(_log, l => l.Contains("whitelist gate is OFF") && l.Contains("1 entry"));
        Assert.DoesNotContain(_log, l => l.Contains("a.jpg"));
    }

    [Fact]
    public void UnrelatablePath_NeverSilentlyDropped()
    {
        // IntakeHostService.cs:789 — GetRelativePath throws → TRUE (never drop over a path
        // quirk). Empty root forces the throw; defensive-only in real walks (recorded).
        var disabled = Disabled("images/a.jpg");
        Assert.True(DtrhUserMedia.IsAssetActive(disabled, "", Path.Combine(_userMedia, "images", "a.jpg"), useWhitelist: true));
    }

    [Fact]
    public void IsAssetActive_GateOrder_WhitelistFirst_ThenEmptySet()
    {
        var file = Path.Combine(_userMedia, "images", "a.jpg");
        Assert.True(DtrhUserMedia.IsAssetActive(Disabled("images/a.jpg"), _userMedia, file, useWhitelist: false));
        Assert.True(DtrhUserMedia.IsAssetActive(Disabled(), _userMedia, file, useWhitelist: true));
        Assert.False(DtrhUserMedia.IsAssetActive(Disabled("images/a.jpg"), _userMedia, file, useWhitelist: true));
    }

    // ---------- normalization verbatim (the FlashService.GetMediaFiles match) ----------

    [Fact]
    public void Deselect_ExactMatch_Excluded_SilentlyNotCounted()
    {
        var m = DtrhUserMedia.Build(_userMedia, "http://127.0.0.1:9", _log.Add,
            Disabled("images/a.jpg"), useWhitelist: true);
        Assert.Equal(2, m.Images.Count);
        Assert.DoesNotContain(m.Images, e => e.Name == "a.jpg");
        Assert.Equal(3, m.Skipped); // unchanged — deselected is NOT "skipped" (:148-151)
    }

    [Fact]
    public void Deselect_CaseDifference_Excluded()
    {
        // OrdinalIgnoreCase (Windows is case-insensitive at the FS level, :2855-2867).
        var m = DtrhUserMedia.Build(_userMedia, "http://127.0.0.1:9", _log.Add,
            Disabled("IMAGES/case.jpg"), useWhitelist: true);
        Assert.Equal(2, m.Images.Count);
        Assert.DoesNotContain(m.Images, e => e.Name == "CASE.JPG");
    }

    [Fact]
    public void Deselect_BackslashStoredPath_Excluded()
    {
        // Separator-agnostic: the tree's saved string can differ by separator (:2860-2864).
        var m = DtrhUserMedia.Build(_userMedia, "http://127.0.0.1:9", _log.Add,
            Disabled(@"images\sub\b.png"), useWhitelist: true);
        Assert.Equal(2, m.Images.Count);
        Assert.DoesNotContain(m.Images, e => e.Name == "b.png");
    }

    [Fact]
    public void Deselect_NestedPath_Excluded()
    {
        var m = DtrhUserMedia.Build(_userMedia, "http://127.0.0.1:9", _log.Add,
            Disabled("images/sub/b.png"), useWhitelist: true);
        Assert.Equal(2, m.Images.Count);
        Assert.DoesNotContain(m.Images, e => e.Name == "b.png");
    }

    [Fact]
    public void Deselect_Video_Excluded()
    {
        var m = DtrhUserMedia.Build(_userMedia, "http://127.0.0.1:9", _log.Add,
            Disabled("videos/v.mp4"), useWhitelist: true);
        Assert.Empty(m.Videos);
        Assert.Equal(3, m.Images.Count);
    }

    [Fact]
    public void BuildDisabledSet_NormalizesNullsAndSeparators()
    {
        // IntakeHostService.cs:776-781 verbatim: (p ?? "") with \ → /, OrdinalIgnoreCase.
        var set = DtrhUserMedia.BuildDisabledSet([null!, @"a\b\c.jpg", "A/B/C.JPG", ""]);
        Assert.Contains("a/b/c.jpg", set); // one entry (case + separator collapsed)
        Assert.Equal(2, set.Count);          // "" survives as an entry (upstream verbatim)
    }

    // ---------- skip-vs-deselect: the two meanings stay distinct (:41-45) ----------

    [Fact]
    public void Deselected_OversizeFile_SilentlyDropped_NotCountedSkipped()
    {
        // Upstream Scan order (:134-158): ext filter → deselect → size. A deselected
        // over-cap file hits the deselect check FIRST → silent, Skipped unchanged.
        var m = DtrhUserMedia.Build(_userMedia, "http://127.0.0.1:9", _log.Add,
            Disabled("images/big.gif"), useWhitelist: true);
        Assert.Equal(2, m.Skipped); // photo.wmv + v2.mkv only — big.gif silently gone
        Assert.Equal(3, m.Images.Count);
    }

    [Fact]
    public void Deselected_MediaLikeUnsupported_StillCountedSkipped()
    {
        // The extension filter runs FIRST (:134-143): a deselected .wmv is still counted
        // skipped — "media-looking but not usable", a different meaning from deselection.
        var m = DtrhUserMedia.Build(_userMedia, "http://127.0.0.1:9", _log.Add,
            Disabled("images/photo.wmv", "videos/v2.mkv"), useWhitelist: true);
        Assert.Equal(3, m.Skipped); // wmv + mkv still counted + big.gif over cap
        Assert.Equal(3, m.Images.Count);
        Assert.Single(m.Videos);
    }

    // ---------- the both-folders accepted bound (:128-131) ----------

    [Fact]
    public void WalkBound_SpansBothFolders_ImagesSaturate_VideosNeverWalked()
    {
        // 5 accepted-capable images with bound 5: the second folder's first bound check
        // sees the saturated COMBINED count → zero videos accepted (NOT a per-folder bound,
        // DtrhAssetManifest.cs:128-131). The bound is accepted-only: skipped files don't count.
        var images = Path.Combine(_userMedia, "images");
        File.WriteAllBytes(Path.Combine(images, "e.jpg"), [1]);
        File.WriteAllBytes(Path.Combine(images, "f.jpg"), [1]);
        var m = DtrhUserMedia.Build(_userMedia, "http://127.0.0.1:9", _log.Add, walkBound: 5);
        Assert.Equal(5, m.Images.Count);
        Assert.Empty(m.Videos);
        // Control: the default bound lets the videos folder contribute.
        var control = DtrhUserMedia.Build(_userMedia, "http://127.0.0.1:9", _log.Add);
        Assert.Equal(5, control.Images.Count);
        Assert.Single(control.Videos);
    }

    // ---------- both consumers agree on one fixture (never two scans) ----------

    [Fact]
    public void BothConsumers_SameFixture_SameDeselection()
    {
        var disabled = Disabled("images/CASE.JPG");
        var dtrh = DtrhUserMedia.Build(_userMedia, "http://127.0.0.1:9", _log.Add, disabled, useWhitelist: true);
        var intake = IntakeMediaManifest.Build(_userMedia, "http://127.0.0.1:9", _log.Add, new Random(1),
            disabled, useWhitelist: true);

        Assert.Equal(2, dtrh.Images.Count); // CASE.JPG excluded
        Assert.NotNull(intake);
        // The intake sample rides the SAME manifest: the pool-of-N line proves agreement.
        Assert.Contains(_log, l => l.Contains("intake: media manifest sampled") && l.Contains("pool of 2"));
        // And the deselected URL appears in NEITHER consumer's output.
        var json = System.Text.Json.JsonSerializer.Serialize(intake);
        Assert.DoesNotContain("CASE", json);
    }

    [Fact]
    public void BothConsumers_WhitelistOff_AgreeAllActive()
    {
        var disabled = Disabled("images/a.jpg");
        var dtrh = DtrhUserMedia.Build(_userMedia, "http://127.0.0.1:9", _log.Add, disabled, useWhitelist: false);
        var intake = IntakeMediaManifest.Build(_userMedia, "http://127.0.0.1:9", _log.Add, new Random(1),
            disabled, useWhitelist: false);
        Assert.Equal(3, dtrh.Images.Count);
        Assert.NotNull(intake);
        Assert.Contains(_log, l => l.Contains("pool of 3"));
    }

    // ---------- the persisted document on SP-005 machinery ----------

    private PersistenceStore<AssetSelectionDocument> NewStore(string dir)
    {
        var store = new PersistenceStore<AssetSelectionDocument>(
            new OperationRegistry().OwnerFor("AssetSelectionTests"),
            new SinkAdapter(_log),
            Path.Combine(dir, AssetSelectionStore.FileName),
            AssetSelectionDocument.CurrentSchemaVersion);
        store.StartAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        return store;
    }

    private sealed class SinkAdapter(List<string> lines) : ILogSink
    {
        public void Log(string message) => lines.Add(message);
    }

    [Fact]
    public void Document_Defaults_EmptySet_WhitelistOff_AllActive()
    {
        // The shipped contract: the set stays empty until the Assets-tree row; defaults =
        // everything active (identical to the pre-fix behavior).
        var store = NewStore(Path.Combine(_root, "fresh"));
        Assert.Equal(LoadOutcome.Missing.Instance, store.LastLoadOutcome);
        Assert.Empty(store.Current.DisabledAssetPaths);
        Assert.False(store.Current.UseAssetWhitelist);
    }

    [Fact]
    public async Task Document_RoundTrip_SetAndFlag_Preserved()
    {
        var dir = Path.Combine(_root, "roundtrip");
        var writer = NewStore(dir);
        writer.Mutate(d =>
        {
            d.DisabledAssetPaths.Add(@"images\a.jpg");
            d.UseAssetWhitelist = true;
        });
        await writer.SaveImmediate();

        var store = NewStore(dir);
        Assert.Equal(LoadOutcome.Loaded.Instance, store.LastLoadOutcome);
        var path = Assert.Single(store.Current.DisabledAssetPaths);
        Assert.Equal(@"images\a.jpg", path); // raw on disk; normalization is the seam's job
        Assert.True(store.Current.UseAssetWhitelist);
        // The seam consumes it: the stored backslash form deselects the file.
        var set = DtrhUserMedia.BuildDisabledSet(store.Current.DisabledAssetPaths);
        Assert.False(DtrhUserMedia.IsAssetActive(set, _userMedia,
            Path.Combine(_userMedia, "images", "a.jpg"), store.Current.UseAssetWhitelist));
    }

    [Fact]
    public async Task Document_UnknownMembers_Preserved()
    {
        var dir = Path.Combine(_root, "extdata");
        var writer = NewStore(dir);
        writer.Mutate(d => d.ExtensionData = new Dictionary<string, System.Text.Json.JsonElement>
        {
            ["futureMember"] = System.Text.Json.JsonDocument.Parse("{\"x\":1}").RootElement.Clone(),
        });
        await writer.SaveImmediate();

        var store = NewStore(dir);
        Assert.NotNull(store.Current.ExtensionData);
        Assert.True(store.Current.ExtensionData.ContainsKey("futureMember"));
    }
}
