using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-048 (b1 land condition — published-artifact payload location DECIDED: beside the exe
/// via the linked glob, served read-only through AppContext.BaseDirectory). Covers the
/// non-fatal payload-presence probe (Missing/Incomplete/Present), the one-path root
/// resolution shape that makes Debug/Release/published identical, and the participant's
/// startup diagnostic naming the resolved root (falsifiability: a boot transcript proves
/// WHERE the payload is served from).
/// </summary>
public sealed class DtrhPayloadRootTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ccp-sp048-payload-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Probe_MissingDirectory_TypedMissingZeroFiles()
    {
        var probe = DtrhParticipant.ProbePayloadRoot(Path.Combine(_root, "absent"));
        Assert.Equal(DtrhParticipant.DtrhPayloadState.Missing, probe.State);
        Assert.Equal(0, probe.FileCount);
    }

    [Fact]
    public void Probe_DirectoryWithoutIndex_TypedIncompleteWithCount()
    {
        var payload = Path.Combine(_root, "payload");
        Directory.CreateDirectory(Path.Combine(payload, "assets"));
        File.WriteAllText(Path.Combine(payload, "boot.js"), "// boot");
        File.WriteAllText(Path.Combine(payload, "assets", "clip.mp3"), "x");

        var probe = DtrhParticipant.ProbePayloadRoot(payload);
        Assert.Equal(DtrhParticipant.DtrhPayloadState.Incomplete, probe.State);
        Assert.Equal(2, probe.FileCount);
    }

    [Fact]
    public void Probe_DirectoryWithIndex_TypedPresentWithRecursiveCount()
    {
        var payload = Path.Combine(_root, "payload");
        Directory.CreateDirectory(Path.Combine(payload, "engine", "core"));
        File.WriteAllText(Path.Combine(payload, "index.html"), "<html/>");
        File.WriteAllText(Path.Combine(payload, "boot.js"), "// boot");
        File.WriteAllText(Path.Combine(payload, "engine", "core", "mod.js"), "// mod");

        var probe = DtrhParticipant.ProbePayloadRoot(payload);
        Assert.Equal(DtrhParticipant.DtrhPayloadState.Present, probe.State);
        Assert.Equal(3, probe.FileCount);
    }

    [Fact]
    public void Roots_SingleCodePath_BesideBaseDirectoryShape()
    {
        // The decided location is one code path for Debug/Release/published: the payload
        // sits BESIDE the binary (AppContext.BaseDirectory), the overlay is its sibling,
        // and media lives under the payload — the shape the linked glob lays down in all
        // three modes (empirically verified on the published win-x64 artifact, SP-048 record).
        var baseDir = AppContext.BaseDirectory;
        Assert.Equal(Path.Combine(baseDir, "payload", "dtrh"), DtrhParticipant.PayloadRoot);
        Assert.Equal(Path.Combine(baseDir, "payload-overlay"), DtrhParticipant.OverlayRoot);
        Assert.Equal(Path.Combine(DtrhParticipant.PayloadRoot, "assets"), DtrhParticipant.MediaRoot);
    }

    [Fact]
    public async Task ParticipantStart_LogsResolvedRootAndState_NonFatal()
    {
        var log = new CollectingLog();
        var registry = new OperationRegistry();
        var owner = registry.OwnerFor("sp048-payload-probe");
        var participant = new DtrhParticipant(owner, log,
            dataDirectory: Path.Combine(_root, "data"));
        try
        {
            await participant.StartAsync(CancellationToken.None);
            Assert.True(participant.Running); // non-fatal: the participant starts regardless

            var line = log.Lines.FirstOrDefault(l => l.StartsWith("dtrh: payload root '", StringComparison.Ordinal));
            Assert.NotNull(line);
            Assert.Contains(DtrhParticipant.PayloadRoot, line);
            // The state is one of the typed vocabulary (Present under a real build output
            // where the glob copied the payload; Missing/Incomplete are equally honest).
            Assert.True(
                line.Contains("-> Present (", StringComparison.Ordinal)
                || line.Contains("-> Missing (", StringComparison.Ordinal)
                || line.Contains("-> Incomplete (", StringComparison.Ordinal),
                $"unexpected probe line shape: {line}");
        }
        finally
        {
            await participant.StopAsync();
        }
    }

    private sealed class CollectingLog : ILogSink
    {
        private readonly object _gate = new();
        private readonly List<string> _lines = [];

        public IReadOnlyList<string> Lines
        {
            get { lock (_gate) { return _lines.ToArray(); } }
        }

        public void Log(string message)
        {
            lock (_gate) { _lines.Add(message); }
        }
    }
}
