using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Features.Intake;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-054: the serving contract — the intake tree served under the page origin through
/// the SAME §4 LoopbackServer class as DTRH (GET-only, overlay-first, MIME allowlist +
/// 415, traversal refusal), with the intake tree as overlayRoot and the dtrh tree as
/// payloadRoot so the OVERLAY-FIRST BORROW is proven: the page's own files shadow, and
/// the borrowed dtrh assets (vendor/three, the chime mp3s) fall through. Payload READ-ONLY.
/// </summary>
public sealed class IntakeServingTests : IDisposable
{
    private readonly string _root;
    private readonly string _dtrhTree;
    private readonly string _intakeTree;
    private readonly LoopbackServer _server;

    public IntakeServingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ccp-sp054-serve-" + Guid.NewGuid().ToString("N"));
        _dtrhTree = Path.Combine(_root, "dtrh");
        _intakeTree = Path.Combine(_root, "intake");
        Directory.CreateDirectory(Path.Combine(_dtrhTree, "vendor", "three"));
        Directory.CreateDirectory(Path.Combine(_dtrhTree, "assets", "bubbles", "sfx"));
        Directory.CreateDirectory(Path.Combine(_intakeTree, "render"));
        Directory.CreateDirectory(Path.Combine(_intakeTree, "banks"));
        Directory.CreateDirectory(Path.Combine(_intakeTree, "assets", "vo"));
        File.WriteAllText(Path.Combine(_dtrhTree, "index.html"), "DTRH PAGE");
        File.WriteAllText(Path.Combine(_dtrhTree, "vendor", "three", "three.module.min.js"), "THREE");
        File.WriteAllText(Path.Combine(_dtrhTree, "assets", "bubbles", "sfx", "chime1.mp3"), "CHIME");
        File.WriteAllText(Path.Combine(_intakeTree, "index.html"), "INTAKE PAGE");
        File.WriteAllText(Path.Combine(_intakeTree, "render", "audio.js"), "INTAKE AUDIO");
        File.WriteAllText(Path.Combine(_intakeTree, "banks", "bambi.json"), "{}");
        File.WriteAllText(Path.Combine(_intakeTree, "assets", "vo", "vo_manifest.json"), "INTAKE VO");
        File.WriteAllText(Path.Combine(_intakeTree, "styles.svg"), "<svg/>"); // outside the §4.4 allowlist

        // The exact construction IntakeParticipant uses: payload fallback = the dtrh tree,
        // overlay-first = the intake tree.
        _server = new LoopbackServer(_dtrhTree, _intakeTree, Path.Combine(_dtrhTree, "assets"),
            new Inbox(), "tok", new CollectingLog(), TimeSpan.FromMilliseconds(100));
        _server.Start();
    }

    public void Dispose() { try { _server.Dispose(); Directory.Delete(_root, recursive: true); } catch { /* best-effort */ } }

    private sealed class CollectingLog : ILogSink
    {
        public void Log(string message) { }
    }

    private async Task<(int Status, string Body)> Get(string path)
    {
        using var client = new HttpClient();
        var response = await client.GetAsync($"{_server.PageOrigin}{path}", TestContext.Current.CancellationToken);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Intake_Page_Serves_From_The_Intake_Tree()
    {
        var (status, body) = await Get("/dtrh/index.html");
        Assert.Equal(200, status);
        Assert.Equal("INTAKE PAGE", body);
    }

    [Fact]
    public async Task Intake_Modules_And_Banks_Shadow()
    {
        Assert.Equal("INTAKE AUDIO", (await Get("/dtrh/render/audio.js")).Body);
        Assert.Equal("{}", (await Get("/dtrh/banks/bambi.json")).Body);
        // Intake's OWN assets shadow the dtrh tree at the same route path.
        Assert.Equal("INTAKE VO", (await Get("/dtrh/assets/vo/vo_manifest.json")).Body);
    }

    [Fact]
    public async Task The_Borrow_Falls_Through_To_The_Dtrh_Tree()
    {
        // THE chime-borrow proof (consult evidence obligation (a)): 200 from the dtrh tree.
        var chime = await Get("/dtrh/assets/bubbles/sfx/chime1.mp3");
        Assert.Equal(200, chime.Status);
        Assert.Equal("CHIME", chime.Body);
        // The shared vendor importmap target (index.html:17-22).
        Assert.Equal("THREE", (await Get("/dtrh/vendor/three/three.module.min.js")).Body);
    }

    [Fact]
    public async Task The_Dtrh_Page_Itself_Is_Shadowed_On_This_Server()
    {
        // Recorded, intentional: nothing on the intake server serves the DTRH page (the
        // DTRH host runs its own server) — the route path collides and the overlay wins.
        var (status, body) = await Get("/dtrh/index.html");
        Assert.Equal(200, status);
        Assert.NotEqual("DTRH PAGE", body);
    }

    [Fact]
    public async Task Section4_Discipline_Holds_On_The_Intake_Server()
    {
        // GET-only.
        using (var client = new HttpClient())
        {
            var response = await client.PostAsync($"{_server.PageOrigin}/dtrh/index.html", new StringContent("x"), TestContext.Current.CancellationToken);
            Assert.Equal(405, (int)response.StatusCode);
        }

        // MIME allowlist deny-by-default: .svg 415s even though the file exists.
        Assert.Equal(415, (await Get("/dtrh/styles.svg")).Status);
        // Traversal refusal.
        Assert.Equal(403, (await Get("/dtrh/..%2Fsecret.txt")).Status);
        // Unknown route → 404.
        Assert.Equal(404, (await Get("/dtrh/no/such/file.js")).Status);
        // Outside the /dtrh/ route → 404.
        Assert.Equal(404, (await Get("/intake/index.html")).Status);
        // Health lives.
        Assert.Equal(200, (await Get("/health")).Status);
    }
}
