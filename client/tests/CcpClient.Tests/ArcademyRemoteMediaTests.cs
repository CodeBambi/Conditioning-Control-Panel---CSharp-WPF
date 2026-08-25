using System.Diagnostics.Tracing;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using CcpClient.Desktop.Features.Arcademy;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Slice 7 of the Arcademy row — the remote-media broker — as it was actually resolved: the
/// network is NOT built, and the refusal is.
///
/// <para><b>What upstream's broker is, so the decision can be read rather than trusted.</b>
/// <c>ArcademyHostService.OnAssetsRequest</c> (<c>:1485-1516</c>) has two branches. The closed one
/// (<c>:1501-1505</c>) answers with an empty batch. The open one (<c>:1517-1602</c>) goes through
/// <c>FypOnlineCoordinator.For("arcademy", …)</c> into <c>ScrolllerSource.cs:48</c> —
/// <c>POST https://api.scrolller.com/admin</c>, an unofficial third-party GraphQL API — shaped by
/// the user's own adult-content niche selection and per-channel dwell profile
/// (<c>FypOnlineCoordinator.cs:19-22</c>, <c>:44-62</c>), under a consent union of two one-time
/// cards (<c>Models/AppSettings.cs:3354</c>). This port has neither the dependency nor either
/// card, and <c>client/docs/fyp-census.md</c> §5.1 records the dependency as an owner decision
/// that has not been made. So the closed branch is the whole of the slice here.</para>
///
/// <para><b>The refusal is what these facts pin, and the strong form of it.</b> Not "the gate is
/// shut" — there IS no gate, because there is nothing behind one — but <b>nothing leaves the
/// machine on this path under any setting</b>, evidenced against socket telemetry rather than
/// against a flag.</para>
///
/// <para><b>What they do NOT prove.</b> No page receives any of these frames: there is no browser
/// in this assembly, so the page-side latch these replies clear
/// (<c>arcademy/provider/remote.js:48-55</c>) is read from source, not observed. The socket fact
/// covers the operation it brackets on THIS process and THIS machine; it says nothing about
/// Linux, nothing about any other code path, and nothing about DNS, which it does not watch
/// because a resolution without a connection carries no media. Nothing here was run headed.</para>
/// </summary>
public sealed class ArcademyRemoteMediaTests : IDisposable
{
    private readonly List<string> _log = [];
    private readonly List<object> _posted = [];
    private readonly string _dir;
    private readonly PersistenceStore<ArcademySettingsDocument> _store;

    public ArcademyRemoteMediaTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ccp-arcademy-remote-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _store = new PersistenceStore<ArcademySettingsDocument>(
            new OperationRegistry().OwnerFor("ArcademyRemoteMediaTests"),
            new SinkAdapter(_log),
            Path.Combine(_dir, ArcademySettingsDocument.FileName),
            ArcademySettingsDocument.CurrentSchemaVersion);
        _ = _store.StartAsync(TestContext.Current.CancellationToken);
    }

    public void Dispose()
    {
        try
        {
            _ = _store.StopAsync();
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception)
        {
            // best-effort teardown
        }
    }

    /// <summary>The four settings the page and the host can disagree about, as upstream's gate
    /// reads them (<c>:1501</c>: <c>!RemoteMediaEnabled() || OfflineMode</c>). Every one of them
    /// must produce the same reply here, which is the point.</summary>
    public static TheoryData<bool, bool> EveryGateState => new()
    {
        { false, true },    // this build's shipped facts
        { false, false },
        { true, true },
        { true, false },    // upstream's OPEN gate — the one that fetches there and must not here
    };

    private ArcademySession NewSession(ArcademyAppFacts? facts = null) =>
        new(_store, facts ?? new ArcademyAppFacts(), frame => _posted.Add(frame), new SinkAdapter(_log));

    private JsonElement Frame(int index) =>
        JsonDocument.Parse(ArcademyProtocol.SerializeForPage(_posted[index])).RootElement.Clone();

    /// <summary>Asserts one posted frame is upstream's closed-gate <c>assets</c> reply
    /// (<c>:1502-1504</c>) for <paramref name="reqId"/>: same id back, an EMPTY array, and
    /// <c>done</c> — the three fields the page's channel reads at
    /// <c>arcademy/provider/remote.js:48-55</c>.</summary>
    private void AssertClosedGateReply(int index, string reqId)
    {
        var frame = Frame(index);
        Assert.Equal("assets", frame.GetProperty("type").GetString());
        Assert.Equal(reqId, frame.GetProperty("reqId").GetString());
        var urls = frame.GetProperty("urls");
        Assert.Equal(JsonValueKind.Array, urls.ValueKind);
        Assert.Empty(urls.EnumerateArray());
        Assert.True(frame.GetProperty("done").GetBoolean(), "done:false restarts the exchange (:1598)");
    }

    // ==================================================================================
    // The reply itself.
    // ==================================================================================

    [Fact]
    public void EveryAssetsRequest_IsAnsweredWithATerminatingEmptyBatch_UnderItsOwnReqId()
    {
        var session = NewSession();
        session.Ready();
        _posted.Clear();

        // The full page-shaped ask (arcademy/provider/remote.js:78-80), niches included: the page
        // may name the content categories it wants, and the host neither uses nor forwards them,
        // because it fetches nothing.
        session.Handle("""
            {"type":"assets-request","reqId":"ae-1-4815162","count":8,"kind":"loop","niches":["hypno","bimbo"]}
            """);

        // A missing reqId is upstream's empty string (:1487) and is STILL answered. Silence is the
        // one thing a closed gate may not do — ":1479-1481", verbatim: "A closed gate answers with
        // an empty array rather than silence, because silence is what leaves a page spinning."
        session.Handle("""{"type":"assets-request","kind":"still"}""");

        Assert.Equal(2, _posted.Count);
        AssertClosedGateReply(0, "ae-1-4815162");
        AssertClosedGateReply(1, "");
        Assert.Contains(_log, l => l.Contains("assets-request") && l.Contains("nothing left the machine"));
    }

    [Theory]
    [MemberData(nameof(EveryGateState))]
    public void NeitherRemoteFlagIsLoadBearing_TheReplyIsEmptyEvenWithConsentAndOnlineBothSet(
        bool remoteMediaEnabled, bool offlineMode)
    {
        var facts = new ArcademyAppFacts
        {
            RemoteMediaEnabled = remoteMediaEnabled,
            OfflineMode = offlineMode,
        };
        var session = NewSession(facts);
        session.Ready();

        // The flags are genuinely WIRED — this fact would be vacuous if they never reached the
        // projection at all (:551, :553). They reach it, and change nothing about what is served.
        var init = Frame(0);
        Assert.Equal(remoteMediaEnabled, init.GetProperty("remoteMediaEnabled").GetBoolean());
        Assert.Equal(offlineMode, init.GetProperty("offlineMode").GetBoolean());

        _posted.Clear();
        session.Handle("""{"type":"assets-request","reqId":"gate","count":24,"kind":"loop"}""");

        Assert.Single(_posted);
        AssertClosedGateReply(0, "gate");
    }

    // ==================================================================================
    // The network claim, evidenced against sockets rather than against a flag.
    // ==================================================================================

    [Fact]
    public void HandlingAnAssetsRequest_OpensNoSocket_AndTheObserverIsProvedNotBlind()
    {
        using var observer = new ConnectObserver();
        var session = NewSession(new ArcademyAppFacts { RemoteMediaEnabled = true, OfflineMode = false });
        session.Ready();
        _posted.Clear();

        // The bracketed operation: the real session, the real parser, the real reply path, under
        // the ONE fact combination that makes upstream fetch (:1501).
        session.Handle("""
            {"type":"assets-request","reqId":"wire","count":24,"kind":"loop","niches":["hypno"]}
            """);
        var duringTheOperation = observer.Snapshot();

        // EventListener callbacks run synchronously on the connecting thread, so there is nothing
        // to wait for here and no wall clock is consulted: a connect this operation started has
        // already been recorded by the time Handle returns.
        Assert.Single(_posted);
        AssertClosedGateReply(0, "wire");
        Assert.DoesNotContain(duringTheOperation, e => !IsLoopback(e.Address));

        // NON-BLINDNESS, in the same run and on the same observer: a real outbound connect to a
        // socket this test owns. Without this the fact above passes on a listener that sees
        // nothing, which is the whole failure mode of a proved negative.
        var control = new TcpListener(IPAddress.Loopback, 0);
        control.Start();
        var port = ((IPEndPoint)control.LocalEndpoint).Port;
        using (var client = new TcpClient())
        {
            client.Connect(IPAddress.Loopback, port);
        }

        control.Stop();
        var afterTheControl = observer.Snapshot();
        Assert.Contains(afterTheControl, e => e.Port == port && IsLoopback(e.Address));

        // ...and the claim is scoped honestly: it is "no NON-loopback connect", because this
        // listener is process-wide and sibling tests in this assembly run real loopback servers.
        // A non-loopback connect anywhere in this process during the window is a violation
        // wherever it came from, and no code in this port has one to make.
        Assert.DoesNotContain(afterTheControl, e => !IsLoopback(e.Address));
    }

    // ==================================================================================
    // The boundary itself, as a chokepoint.
    // ==================================================================================

    [Fact]
    public void TheArcademyFeature_CarriesNoOutboundCapableApi_AndTheMatcherIsProvedNotBlind()
    {
        // Both paths are read WITHOUT an Exists check on purpose: a missing feature directory or a
        // missing control file throws out of this fact, which is louder than an assertion and keeps
        // the method free of the filesystem predicate the vacuous-shape guard is right to distrust.
        var root = FindRepoRoot();
        var featureRoot = Path.Combine([root, .. FeatureParts]);

        var offenders = new List<string>();
        var scanned = 0;
        foreach (var file in Directory.EnumerateFiles(featureRoot, "*.cs", SearchOption.AllDirectories))
        {
            scanned++;
            var text = File.ReadAllText(file);
            var hit = OutboundApis.FirstOrDefault(a => text.Contains(a, StringComparison.Ordinal));
            if (hit is not null)
            {
                offenders.Add($"{Path.GetRelativePath(root, file)} → {hit}");
            }
        }

        Assert.True(scanned > 0, $"nothing was scanned under {featureRoot} — the guard refuses to pass vacuously");
        Assert.True(
            offenders.Count == 0,
            "the Arcademy feature must reach no network. Slice 7 of the board row admits outbound traffic ONLY "
            + "through a consent-gated remote-media broker, and that broker was NOT built: its provider is a "
            + "third-party API (ScrolllerSource.cs:48) whose admission is an owner decision recorded as unmade in "
            + "client/docs/fyp-census.md §5.1, and its consent model is a union of two cards this port has no "
            + "surface for. Offenders: " + string.Join(", ", offenders));

        // NON-BLINDNESS: the same matcher over a file that really does hold an outbound client.
        // A typo in the token list would otherwise leave this fact green over anything at all.
        var known = File.ReadAllText(Path.Combine([root, .. KnownOutboundFileParts]));
        Assert.Contains(OutboundApis, a => known.Contains(a, StringComparison.Ordinal));
    }

    /// <summary>The outbound-capable surfaces this feature may not name. LEXICAL, and over the
    /// WHOLE file including its comments — so prose that spells one of these out reds the guard
    /// too. That is deliberate: the alternative is stripping comments, which would let an example
    /// hide in one. It cannot see a client reached through a variable, a factory, an injected
    /// delegate or reflection; it closes one named regression route and claims nothing more.</summary>
    private static readonly string[] OutboundApis =
    [
        "HttpClient", "HttpMessageInvoker", "SocketsHttpHandler", "WebRequest",
        "TcpClient", "Socket(", "ClientWebSocket", "Dns.", "WebClient", "UdpClient",
    ];

    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];

    private static readonly string[] FeatureParts =
        ["client", "src", "CcpClient.Desktop", "Features", "Arcademy"];

    /// <summary>A file in this tree that genuinely holds an outbound-capable client, so the token
    /// list is proved to match something rather than nothing. The Lovense sink talks to a locally
    /// running Intiface/Lovense server over HTTP — admitted, loopback, and unrelated to this
    /// feature.</summary>
    private static readonly string[] KnownOutboundFileParts =
        ["client", "src", "CcpClient.Desktop", "Haptics", "LovenseHapticSink.cs"];

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

        throw new InvalidOperationException("repository root not found from " + AppContext.BaseDirectory);
    }

    private static bool IsLoopback(IPAddress address)
    {
        var flat = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return IPAddress.IsLoopback(flat);
    }

    /// <summary>
    /// Every TCP connect this process starts, from the runtime's own <c>System.Net.Sockets</c>
    /// telemetry — the same source <c>dotnet-counters</c> reads, so it cannot be bypassed by a
    /// handler, a proxy or a hand-built socket the way a mocked <c>HttpClient</c> can.
    ///
    /// <para><c>ConnectStart</c> carries the destination as a <c>SocketAddress</c> rendering,
    /// <c>Family:Size:{bytes}</c>, whose byte list omits the two leading family bytes — hence the
    /// <c>+2</c> when it is rebuilt. Verified against a real connect before this file existed:
    /// a loopback ask renders <c>InterNetworkV6:28:{…,255,255,127,0,0,1,…}</c> and parses back to
    /// <c>::ffff:127.0.0.1</c>.</para>
    /// </summary>
    private sealed class ConnectObserver : EventListener
    {
        private readonly List<IPEndPoint> _connects = [];

        public List<IPEndPoint> Snapshot()
        {
            lock (_connects)
            {
                return [.. _connects];
            }
        }

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "System.Net.Sockets")
            {
                EnableEvents(eventSource, EventLevel.LogAlways, EventKeywords.All);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventName != "ConnectStart" || eventData.Payload is not { Count: > 0 })
            {
                return;
            }

            var endpoint = Parse(eventData.Payload[0]?.ToString());
            if (endpoint is null)
            {
                // An address shape this parser does not know (a unix socket, say) is recorded as
                // an unroutable non-loopback sentinel rather than dropped: an observer that
                // silently ignores what it cannot read is an observer that proves nothing.
                endpoint = new IPEndPoint(IPAddress.Parse("192.0.2.0"), 0);
            }

            lock (_connects)
            {
                _connects.Add(endpoint);
            }
        }

        private static IPEndPoint? Parse(string? rendered)
        {
            if (rendered is null)
            {
                return null;
            }

            var open = rendered.IndexOf('{', StringComparison.Ordinal);
            var close = rendered.IndexOf('}', StringComparison.Ordinal);
            if (open < 0 || close < open
                || !Enum.TryParse<AddressFamily>(rendered.Split(':')[0], out var family)
                || family is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
            {
                return null;
            }

            var bytes = rendered[(open + 1)..close].Split(',');
            var address = new SocketAddress(family, bytes.Length + 2);
            for (var i = 0; i < bytes.Length; i++)
            {
                if (!byte.TryParse(bytes[i], out var value))
                {
                    return null;
                }

                address[i + 2] = value;
            }

            var template = family == AddressFamily.InterNetworkV6
                ? new IPEndPoint(IPAddress.IPv6Any, 0)
                : new IPEndPoint(IPAddress.Any, 0);
            return template.Create(address) as IPEndPoint;
        }
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
