using System.Text.Json;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Features.Intake;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-054: the host support pieces — subject id (local fiction), the save-image sink
/// (magic/ceiling/clamp/atomic), the drafting sink (sanitize + collision suffixes +
/// never-runnable round-trip), the serving-root probe (SP-048 discipline), the niche
/// bank clamp, the media manifest sampling, and the borrowed-constant drift net
/// (consult 7c — a DTRH retune must not silently retune intake).
/// </summary>
public sealed class IntakeHostSupportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-sp054-support-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = [];

    public IntakeHostSupportTests() => Directory.CreateDirectory(_root);

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ } }

    // ---------- subject id ----------

    [Fact]
    public void SubjectId_Mints_Persists_Reuses_And_ReMints_On_Corruption()
    {
        var path = Path.Combine(_root, "sub", "intake_subject.txt");
        var id = IntakeSubjectId.LoadOrMint(path, _log.Add, new Random(42));
        Assert.Matches("^[0-9]{4}$", id);
        Assert.True(int.Parse(id) is >= 1 and <= 9999);
        Assert.Equal(id, File.ReadAllText(path));
        // Reuse: the persisted id stands.
        Assert.Equal(id, IntakeSubjectId.LoadOrMint(path, _log.Add, new Random(7)));
        // Corruption re-mints (the id is a greeting, not an identity).
        File.WriteAllText(path, "not-a-number");
        var reminted = IntakeSubjectId.LoadOrMint(path, _log.Add, new Random(7));
        Assert.Matches("^[0-9]{4}$", reminted);
        Assert.Contains(_log, l => l.Contains("re-minting"));
    }

    // ---------- save-image sink ----------

    private static byte[] TinyPng() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    [Fact]
    public void SaveImage_Writes_Host_Built_Name_With_Index_Clamp()
    {
        var now = new DateTimeOffset(2026, 8, 11, 12, 34, 56, TimeSpan.Zero);
        var sink = new IntakeSaveImageSink(Path.Combine(_root, "intake_spirals"), () => now, _log.Add);
        var result = sink.Save(Convert.ToBase64String(TinyPng()), 0); // clamped 1..99
        Assert.True(result.Ok);
        Assert.EndsWith("intake-spiral-20260811-123456-01.png", result.Path);
        Assert.True(File.Exists(result.Path));

        var high = sink.Save(Convert.ToBase64String(TinyPng()), 123); // clamped to 99
        Assert.EndsWith("-99.png", high.Path);
    }

    [Fact]
    public void SaveImage_Refusals_Are_The_Reply_Vocabulary()
    {
        var sink = new IntakeSaveImageSink(Path.Combine(_root, "k2"), null, _log.Add);
        Assert.Equal("too-big", sink.Save(null, 1).Error);
        Assert.Equal("bad-image", sink.Save("!!!not-base64!!!", 1).Error);
        Assert.Equal("bad-image", sink.Save(Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }), 1).Error);
    }

    // ---------- drafting sink ----------

    private static IntakeDraft.IntakeDraftDocument Draft(string name) =>
        IntakeDraft.Generate(new IntakeQuizRun { Niche = "bambi", PeakDepth = 0.5, Route = new IntakeQuizRunRoute() },
            new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero),
            new IntakeDraft.IntakeDraftText { Name = name });

    [Fact]
    public void DraftSink_Sanitizes_Collides_And_Round_Trips_Never_Runnable()
    {
        var sink = new IntakeDraftSink(Path.Combine(_root, "drafted_sessions"), _log.Add);
        Assert.Equal("a_b_c", IntakeDraftSink.SanitizeFileName("a/b\\c"));
        Assert.Equal("intake-draft", IntakeDraftSink.SanitizeFileName("   "));

        var first = sink.Write(Draft("Deep Bambi Intake"));
        var second = sink.Write(Draft("Deep Bambi Intake"));
        Assert.EndsWith("Deep Bambi Intake.session.json", first);
        Assert.EndsWith("Deep Bambi Intake-2.session.json", second);

        using var doc = JsonDocument.Parse(File.ReadAllText(first));
        Assert.False(doc.RootElement.GetProperty("runnable").GetBoolean());
        Assert.Equal(IntakeDraft.NeverRunnableReason, doc.RootElement.GetProperty("runnableReason").GetString());
        Assert.Equal("Hard", doc.RootElement.GetProperty("difficulty").GetString());
    }

    // ---------- serving-root probe ----------

    [Fact]
    public void Serving_Probe_States()
    {
        var missing = IntakeServingRoots.Probe(Path.Combine(_root, "nope"));
        Assert.Equal(IntakeServingRoots.IntakePayloadState.Missing, missing.State);

        var incompleteDir = Path.Combine(_root, "incomplete");
        Directory.CreateDirectory(incompleteDir);
        File.WriteAllText(Path.Combine(incompleteDir, "boot.js"), "//");
        var incomplete = IntakeServingRoots.Probe(incompleteDir);
        Assert.Equal(IntakeServingRoots.IntakePayloadState.Incomplete, incomplete.State);
        Assert.Equal(1, incomplete.FileCount);

        File.WriteAllText(Path.Combine(incompleteDir, "index.html"), "<html/>");
        var present = IntakeServingRoots.Probe(incompleteDir);
        Assert.Equal(IntakeServingRoots.IntakePayloadState.Present, present.State);
    }

    // ---------- niche bank clamp ----------

    [Fact]
    public void Niche_Clamp_And_Seam_Default()
    {
        var tree = Path.Combine(_root, "intake-tree");
        Directory.CreateDirectory(Path.Combine(tree, "banks"));
        File.WriteAllText(Path.Combine(tree, "banks", "bambi.json"), "{}");

        Assert.Equal("bambi", IntakeNiche.CurrentFromSeam()); // typed seam default (no mod system)
        Assert.Equal("bambi", IntakeNiche.ClampToOnDiskBanks("bambi", tree));
        Assert.Equal("bambi", IntakeNiche.ClampToOnDiskBanks("sissy", tree));    // bank missing → clamp
        Assert.Equal("bambi", IntakeNiche.ClampToOnDiskBanks("nonsense", tree)); // unknown → clamp
        Assert.Equal("bambi", IntakeNiche.ClampToOnDiskBanks(null, tree));
        File.WriteAllText(Path.Combine(tree, "banks", "circe.json"), "{}");
        Assert.Equal("circe", IntakeNiche.ClampToOnDiskBanks("CIRCE", tree));
    }

    // ---------- media manifest ----------

    [Fact]
    public void Media_Manifest_Empty_Pool_Is_Null_And_Sample_Caps_At_18()
    {
        var empty = Path.Combine(_root, "um-empty");
        Assert.Null(IntakeMediaManifest.Build(empty, "http://127.0.0.1:9", _log.Add, new Random(1)));

        var pool = Path.Combine(_root, "um", "images");
        Directory.CreateDirectory(pool);
        for (var i = 0; i < 25; i++)
        {
            File.WriteAllBytes(Path.Combine(pool, $"g{i}.gif"), [1]);
            File.WriteAllBytes(Path.Combine(pool, $"s{i}.png"), [1]);
        }

        var manifest = IntakeMediaManifest.Build(Path.Combine(_root, "um"), "http://127.0.0.1:9", _log.Add, new Random(2));
        Assert.NotNull(manifest);
        var json = JsonSerializer.Serialize(manifest);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(18, doc.RootElement.GetProperty("gifs").GetArrayLength());
        Assert.Equal(18, doc.RootElement.GetProperty("images").GetArrayLength());
        Assert.Contains("sampled", string.Join('\n', _log)); // presence+shape — never a URL
        Assert.DoesNotContain(_log, l => l.Contains(".gif") || l.Contains(".png"));
    }

    // ---------- the borrowed-constant drift net (consult 7c) ----------

    [Fact]
    public void Watchdog_Contract_Pins()
    {
        // WPF IntakeHostService.cs:993-1004 — 5s cadence, single 20s silence limit.
        Assert.Equal(TimeSpan.FromSeconds(5), DtrhWatchdog.WatchInterval);
        Assert.Equal(TimeSpan.FromSeconds(20), DtrhWatchdog.HubSilenceLimit);
        // WPF :1045-1051 — the 1200ms exit watchdog.
        Assert.Equal(TimeSpan.FromMilliseconds(1200), DtrhWatchdog.ExitDoneWait);

        // Intake ticks with runActive:FALSE at its single call site (no 10s mid-run tier):
        // 15s silent is healthy; 21s silent demands the relaunch; the second failure exhausts.
        var dog = new DtrhWatchdog();
        var t0 = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
        dog.MarkLive(t0);
        Assert.Null(dog.Tick(t0.AddSeconds(15), runActive: false));
        var relaunch = Assert.IsType<DtrhWatchdog.DtrhRecoveryOutcome.Relaunch>(dog.Tick(t0.AddSeconds(21), runActive: false));
        Assert.Equal(1, relaunch.Generation);
        dog.MarkLive(t0.AddSeconds(22));
        Assert.IsType<DtrhWatchdog.DtrhRecoveryOutcome.Exhausted>(dog.Tick(t0.AddSeconds(50), runActive: false));
    }
}
