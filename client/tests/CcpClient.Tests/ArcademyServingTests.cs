using System.Security.Cryptography;
using CcpClient.Desktop.Features.Arcademy;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Slice 1 of the Arcademy row: the payload really ships, it is served BYTE-IDENTICAL through the
/// §4 loopback class every other web core uses, the DTRH spiral borrow resolves through it, and
/// the door refuses a launch before anything is opened.
///
/// <para><b>What these facts are NOT.</b> None of them loads the page, runs a line of its
/// JavaScript, or opens a window: this assembly has no browser and no display. They pin serving,
/// bytes and refusal — not rendering, not interaction, not a playable class.</para>
/// </summary>
public sealed class ArcademyServingTests : IDisposable
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] UpstreamArcademyParts = ["ConditioningControlPanel", "Resources", "web", "arcademy"];

    private readonly List<string> _log = [];
    private readonly ArcademyParticipant _participant;

    public ArcademyServingTests()
    {
        _participant = new ArcademyParticipant(new SinkAdapter(_log), Path.Combine(Path.GetTempPath(), "ccp-arcademy-" + Guid.NewGuid().ToString("N")));
        _participant.Start();
        LoopbackListenerRegistry.RegisterLoopbackServer(nameof(ArcademyServingTests), _participant.Server);
    }

    public void Dispose()
    {
        try
        {
            LoopbackListenerRegistry.UnregisterLoopbackServer(_participant.Server);
            _participant.Dispose();
        }
        catch (Exception)
        {
            // best-effort teardown
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine([dir.FullName, .. RepoAnchorParts])))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"repo root not found walking up from {AppContext.BaseDirectory} — the arcademy payload guard refuses to skip");
    }

    private static string UpstreamArcademyRoot() => Path.Combine([FindRepoRoot(), .. UpstreamArcademyParts]);

    private static List<string> Relatives(string root) =>
        [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)];

    /// <summary>Tree-existence plumbing, kept OUT of the [Fact] bodies so no fs-predicate shape
    /// lands in a fact (the vacuous-shape detector surface, ProcessEnvCollectionGuardTests.cs:553).</summary>
    private static string RequireTree(string path, string what)
    {
        Assert.True(Directory.Exists(path), $"{what} is missing at {path}");
        return path;
    }

    private static bool IsAbsent(string? path) => path is null || !Directory.Exists(path);

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private async Task<(int Status, string Body, string? ContentType)> Get(string url)
    {
        using var client = new HttpClient();
        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
        return ((int)response.StatusCode,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            response.Content.Headers.ContentType?.MediaType);
    }

    // ==================================================================================
    // 1. The payload ships, unmodified.
    // ==================================================================================

    [Fact]
    public void PayloadGlob_CopiesTheWholeUpstreamArcademyTree_Unmodified()
    {
        var upstream = RequireTree(UpstreamArcademyRoot(), "the upstream arcademy tree");
        var output = RequireTree(
            ArcademyServingRoots.PayloadRoot,
            "payload/arcademy in the build output — the linked glob in CcpClient.Desktop.csproj is "
            + "what puts it there, and without it the host serves nothing");

        var upstreamRelative = Relatives(upstream);
        var outputRelative = Relatives(output);

        // Equality both ways: a NARROWED glob loses files, and an extra file in the output would
        // mean something other than the glob wrote there.
        Assert.Equal(upstreamRelative, outputRelative);
        Assert.Equal(ArcademyServingRoots.FileCountAtBaseline, outputRelative.Count);

        foreach (var relative in upstreamRelative)
        {
            Assert.True(
                Sha256(Path.Combine(upstream, relative)) == Sha256(Path.Combine(output, relative)),
                $"payload/arcademy/{relative} differs from the upstream byte — the glob copies, it never transforms");
        }
    }

    [Fact]
    public void Probe_TypesThePayloadRoot_PresentMissingIncomplete()
    {
        var present = ArcademyServingRoots.Probe();
        Assert.Equal(ArcademyServingRoots.ArcademyPayloadState.Present, present.State);
        Assert.Equal(ArcademyServingRoots.FileCountAtBaseline, present.FileCount);
        Assert.Null(present.MissingFile);

        var missingRoot = Path.Combine(Path.GetTempPath(), "ccp-arcademy-absent-" + Guid.NewGuid().ToString("N"));
        var missing = ArcademyServingRoots.Probe(missingRoot);
        Assert.Equal(ArcademyServingRoots.ArcademyPayloadState.Missing, missing.State);
        Assert.Equal(0, missing.FileCount);

        // Incomplete names the FIRST required file it could not find — a boot transcript that says
        // "the tree is there but bridge.js is not" is a different fact from "there is no tree".
        var partial = Path.Combine(Path.GetTempPath(), "ccp-arcademy-partial-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(partial);
            File.WriteAllText(Path.Combine(partial, "index.html"), "PAGE");
            File.WriteAllText(Path.Combine(partial, "boot.js"), "BOOT");
            var incomplete = ArcademyServingRoots.Probe(partial);
            Assert.Equal(ArcademyServingRoots.ArcademyPayloadState.Incomplete, incomplete.State);
            Assert.Equal("bridge.js", incomplete.MissingFile);
            Assert.Equal(2, incomplete.FileCount);
        }
        finally
        {
            try { Directory.Delete(partial, recursive: true); } catch (Exception) { /* best-effort */ }
        }
    }

    // ==================================================================================
    // 2. It is served — through the SAME server the other three web cores run on.
    // ==================================================================================

    [Fact]
    public async Task ThePage_IsServedFromTheArcademyTree_OnTheReusedLoopbackServer()
    {
        var url = _participant.PageUrl();
        Assert.Contains("/dtrh/arcademy/index.html", url);
        Assert.Contains($"bridge={_participant.BridgeToken}", url);

        var (status, body, contentType) = await Get(url);
        Assert.Equal(200, status);
        Assert.Equal("text/html", contentType);
        // The upstream document, not a substitute: its own boot script tag.
        Assert.Contains("<script type=\"module\" src=\"./boot.js\"></script>", body);

        // The module graph the page cannot boot without.
        Assert.Equal(200, (await Get($"{_participant.Server.PageOrigin}/dtrh/arcademy/boot.js")).Status);
        Assert.Equal(200, (await Get($"{_participant.Server.PageOrigin}/dtrh/arcademy/bridge.js")).Status);
        Assert.Equal(200, (await Get($"{_participant.Server.PageOrigin}/dtrh/arcademy/shell/shell.js")).Status);
    }

    /// <summary>
    /// THE TRAP, discharged. The spiral url is resolved the way a BROWSER resolves it — relative
    /// to the script's own address, using the exact literal from
    /// <c>arcademy/shell/shell.js:76</c> and <c>games/the-deep-end/pressure.js:182</c> — so this
    /// fails the moment the serving root is narrowed to <c>payload/arcademy</c>.
    /// </summary>
    [Fact]
    public async Task TheBorrowedDtrhSpirals_ResolveThroughTheArcademyOrigin()
    {
        var shellScript = new Uri($"{_participant.Server.PageOrigin}/dtrh/arcademy/shell/shell.js");
        var fromShell = new Uri(shellScript, "../../dtrh/assets/bubbles/effects/spirals/sp6.gif");

        var deepEndScript = new Uri($"{_participant.Server.PageOrigin}/dtrh/arcademy/games/the-deep-end/pressure.js");
        var fromDeepEnd = new Uri(deepEndScript, "../../../dtrh/assets/bubbles/effects/spirals/sp6.gif");

        // Both literals must land on the SAME url, or one of the two borrow sites is broken.
        Assert.Equal(fromShell, fromDeepEnd);

        var (status, _, contentType) = await Get(fromShell.ToString());
        Assert.Equal(200, status);
        Assert.Equal("image/gif", contentType);

        // ...and the byte is the upstream DTRH one, not a copy living in the arcademy tree.
        var upstreamSpiral = Path.Combine(
            FindRepoRoot(), "ConditioningControlPanel", "Resources", "web", "dtrh",
            "assets", "bubbles", "effects", "spirals", "sp6.gif");
        var servedSpiral = Path.Combine(
            ArcademyServingRoots.WebRoot, "dtrh", "assets", "bubbles", "effects", "spirals", "sp6.gif");
        Assert.Equal(Sha256(upstreamSpiral), Sha256(servedSpiral));
    }

    /// <summary>
    /// The MIME allowlist change this slice makes, and the property it must not break. The six
    /// placeholder tiles (<c>engine/index.js:184</c>) now serve; the arcademy tree's own
    /// <c>.md</c> file still 415s, which is deny-by-default holding over a REAL file that exists
    /// and is reachable by path.
    /// </summary>
    [Fact]
    public async Task PlaceholderSvgsServe_AndDenyByDefaultStillHolds()
    {
        for (var i = 1; i <= 6; i++)
        {
            var (status, body, contentType) = await Get(
                $"{_participant.Server.PageOrigin}/dtrh/arcademy/provider/assets/ae-ph-{i}.svg");
            Assert.Equal(200, status);
            Assert.Equal("image/svg+xml", contentType);
            Assert.StartsWith("<svg", body, StringComparison.Ordinal);
        }

        // Present on disk, reachable by route, and still refused on extension.
        Assert.Contains("CLAUDE.md", Relatives(ArcademyServingRoots.PayloadRoot));
        Assert.Equal(415, (await Get($"{_participant.Server.PageOrigin}/dtrh/arcademy/CLAUDE.md")).Status);
    }

    [Fact]
    public async Task Section4_Discipline_HoldsOnTheArcademyServer()
    {
        var origin = _participant.Server.PageOrigin;

        using (var client = new HttpClient())
        {
            var response = await client.PostAsync($"{origin}/dtrh/arcademy/index.html", new StringContent("x"), TestContext.Current.CancellationToken);
            Assert.Equal(405, (int)response.StatusCode);
        }

        Assert.Equal(403, (await Get($"{origin}/dtrh/..%2Fsecret.txt")).Status);
        Assert.Equal(404, (await Get($"{origin}/dtrh/arcademy/no/such/file.js")).Status);
        Assert.Equal(404, (await Get($"{origin}/arcademy/index.html")).Status);
        Assert.Equal(200, (await Get($"{origin}/health")).Status);

        // The bridge token is never logged (§4.8), on any line, including the probe transcript.
        Assert.DoesNotContain(_log, line => line.Contains(_participant.BridgeToken, StringComparison.Ordinal));
    }

    // ==================================================================================
    // 3. THE DOOR. Shut, and shut before anything opens.
    // ==================================================================================

    [Fact]
    public void TheDoor_ShipsShut()
    {
        // Upstream withholds this surface (ArcademyHostService.cs:116, PlayTabView.xaml:1312,
        // MainWindow.PlayTab.cs:106-112). Porting it visible would release a feature the upstream
        // product is deliberately not releasing, so the port's flag ships false too.
        Assert.False(ArcademyDoor.Available);
    }

    [Fact]
    public async Task AttendingWithTheDoorShut_RefusesAndOpensNothing()
    {
        var host = new ApplicationHost(new SinkAdapter(_log), [], new StartupTrace());
        var launch = new ArcademyLaunch(host, ArcademyEntryTests.NeverAsked)
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "ccp-arcademy-door-" + Guid.NewGuid().ToString("N")),
        };

        var outcome = await launch.AttendAsync(TestContext.Current.CancellationToken);

        var refused = Assert.IsType<ArcademyLaunch.ArcademyAttendOutcome.Refused>(outcome);
        Assert.Equal(ArcademyDoor.Refusal.Reason, refused.Refusal.Reason);

        // "Opens nothing" is the load-bearing half: no participant exists, so no loopback origin
        // was bound, no port is listening and no payload byte is reachable through this path.
        Assert.Null(launch.Participant);
        Assert.Equal(1, launch.AttendCount);
        Assert.True(IsAbsent(launch.DataDirectory), "the refused attend created a data directory — something ran past the door");

        // A second attempt refuses the same way — a shut door does not open on the second knock.
        Assert.IsType<ArcademyLaunch.ArcademyAttendOutcome.Refused>(
            await launch.AttendAsync(TestContext.Current.CancellationToken));
        Assert.Null(launch.Participant);

        // And the T2 bar BEHIND the door was never reached: the entitlement function this
        // launcher was built with throws if it is ever called (:136-141 then :146).
        Assert.Null(launch.LastDecision);
    }

    private sealed class SinkAdapter(List<string> lines) : ILogSink
    {
        public void Log(string message)
        {
            lock (lines)
            {
                lines.Add(message);
            }
        }
    }
}
