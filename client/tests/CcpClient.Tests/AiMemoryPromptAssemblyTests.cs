using System.Net;
using System.Text.Json;
using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-047 memory→prompt assembly proofs (ai-companion-admission.md §4 rule 1; contract §5;
/// board row "Wire companion memory into prompt context"). Proves the falsifiable core: a
/// NEW PAIR ROUND-TRIPS INTO THE NEXT REQUEST'S PROMPT — at the provider seam (captured
/// <see cref="AiRequest.History"/>) AND on the wire (a self-contained loopback listener
/// asserts the JSON payload's messages array carries the persisted pair before the new
/// prompt). Read-gating is the WPF `:113` port (LocalAiService.cs:111-126): consent off ⇒
/// neither read nor written — and ≠ deletion (the file keeps the earlier pair; erasure is
/// the explicit-clear operation). Awareness stays stateless BY CONSTRUCTION (a
/// caller-supplied History is stripped — negative proof that would fail without the strip).
/// Trimming rides the c4 retention mechanism (no assembly-side trim, no new values).
/// </summary>
public class AiMemoryPromptAssemblyTests
{
    private sealed class CapturingProvider : IAiProvider
    {
        private readonly Func<AiRequest, AiReply> _reply;

        public CapturingProvider(Func<AiRequest, AiReply>? reply = null)
        {
            _reply = reply ?? (req => new AiReply.Generated("reply to: " + req.Prompt, AiEndpointClass.Loopback));
        }

        public List<AiRequest> Received { get; } = [];

        public AiProviderDescriptor Descriptor { get; } =
            new(AiProviderId.LocalOllama, AiEndpointClass.Loopback);

        public Func<CancellationToken, Task<CapabilityState>>? Probe { get; } =
            _ => Task.FromResult<CapabilityState>(new CapabilityState.Available("stub-probe"));

        public Task<AiReply> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
        {
            Received.Add(request);
            return Task.FromResult(_reply(request));
        }
    }

    private sealed class Harness : IDisposable
    {
        private readonly TempDir _dir = new();

        public OperationRegistry Registry { get; } = new();
        public CapabilityRegistry Capabilities { get; } = new();
        public CollectingAiDiagnosticsSink Diagnostics { get; } = new();
        public string MemoryPath { get; }
        public AiMemoryStore Memory { get; }
        public AiOperationPipeline Pipeline { get; }
        public CapturingProvider Provider { get; } = new();
        public AiMemoryConsent Consent = AiMemoryConsent.Granted;

        public Harness(Func<AiMemoryConsent>? consent = null, AiMemoryRetention? retention = null, bool defaultConsent = false)
        {
            MemoryPath = _dir.Path(AiMemoryStore.FileName);
            Memory = new AiMemoryStore(
                Registry.OwnerFor("AiMemory"), new ListLogSink(), MemoryPath,
                consent: defaultConsent ? null : (consent ?? (() => Consent)),
                retention: retention);
            Memory.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            Pipeline = new AiOperationPipeline(
                Registry, Capabilities, LoopbackOnlyAdmissionPolicy.Instance, Diagnostics,
                new AiModerationBoundary(), Memory);
        }

        public async Task AdmitProviderAsync()
        {
            Pipeline.RegisterProvider(Provider);
            Pipeline.SelectProvider(AiProviderId.LocalOllama);
            var runner = new CapabilityProbeRunner(Registry.OwnerFor("probes"), Capabilities);
            await runner.RunAllAsync(CancellationToken.None);
        }

        public string? MemoryFileContent() => File.Exists(MemoryPath) ? File.ReadAllText(MemoryPath) : null;

        public void Dispose() => _dir.Dispose();
    }

    [Fact]
    public async Task NewPair_RoundTripsIntoNextRequestsPrompt_InOrder_CurrentPromptExcluded()
    {
        using var h = new Harness();
        await h.AdmitProviderAsync();

        await h.Pipeline.RunInteractiveAsync(new AiRequest("first question"));
        await h.Pipeline.RunInteractiveAsync(new AiRequest("second question"));

        Assert.Equal(2, h.Provider.Received.Count);
        // The FIRST request had nothing to recall.
        Assert.Null(h.Provider.Received[0].History);
        // The SECOND request's prompt carries the persisted pair, oldest first (WPF
        // outgoing-list order, LocalAiService.cs:531-548) — the round-trip INTO the prompt.
        Assert.Equal(
            [new AiMemoryTurn(AiMemoryRole.User, "first question"), new AiMemoryTurn(AiMemoryRole.Assistant, "reply to: first question")],
            h.Provider.Received[1].History);
        // The current prompt is NOT part of the assembled history (it is the final turn).
        Assert.DoesNotContain(h.Provider.Received[1].History!, t => t.Text == "second question");
    }

    [Fact]
    public async Task InteractiveCallerSuppliedHistory_NeverLeaks_AssemblyIsPipelineOwned()
    {
        using var h = new Harness();
        await h.AdmitProviderAsync();

        await h.Pipeline.RunInteractiveAsync(new AiRequest("real question"));
        // A caller smuggling its own History must be overwritten by the store's pairs.
        await h.Pipeline.RunInteractiveAsync(new AiRequest(
            "second question",
            [new AiMemoryTurn(AiMemoryRole.User, "smuggled context")]));

        Assert.Equal(
            [new AiMemoryTurn(AiMemoryRole.User, "real question"), new AiMemoryTurn(AiMemoryRole.Assistant, "reply to: real question")],
            h.Provider.Received[1].History);
        Assert.DoesNotContain(h.Provider.Received[1].History!, t => t.Text == "smuggled context");
    }

    [Fact]
    public async Task ConsentRevoked_NeitherReadNorWritten_FileKeepsPriorPair_ReadGatingIsNotDeletion()
    {
        using var h = new Harness();
        await h.AdmitProviderAsync();

        // Persist a pair under Granted, then revoke (WPF `:113` shape: consent checked FIRST).
        await h.Pipeline.RunInteractiveAsync(new AiRequest("remembered question"));
        Assert.IsType<OperationOutcome.Completed>(await h.Memory.SaveImmediate());
        var fileWithPair = h.MemoryFileContent();
        Assert.NotNull(fileWithPair);

        h.Consent = AiMemoryConsent.Denied;
        await h.Pipeline.RunInteractiveAsync(new AiRequest("after revocation"));

        // NEITHER read (the prompt carries nothing — null, not even an empty list)...
        Assert.Null(h.Provider.Received[1].History);
        // ...NOR written (typed no-op at write admission).
        Assert.Equal(AiMemoryWriteAdmission.ConsentDenied, h.Memory.LastWriteAdmission);
        // Read-gating is NOT deletion: the file still holds the earlier pair byte-identically
        // (erasure is the explicit-clear operation, contract §5 rule 1).
        Assert.Equal(fileWithPair, h.MemoryFileContent());
    }

    [Fact]
    public async Task AwarenessOperation_CallerHistoryStripped_NeverReadsMemory_StatelessByConstruction()
    {
        using var h = new Harness();
        await h.AdmitProviderAsync();

        await h.Pipeline.RunInteractiveAsync(new AiRequest("remembered question")); // memory present, consent Granted

        // An awareness caller passing a NON-empty History makes this proof falsifiable:
        // without the pipeline strip the provider would receive it.
        var result = await h.Pipeline.RunAwarenessAsync(
            new AiRequest("ambient context", [new AiMemoryTurn(AiMemoryRole.User, "smuggled ambient history")]),
            AiAwarenessConsent.Given);

        Assert.IsType<OperationOutcome.Completed>(result.Outcome);
        var awarenessRequest = h.Provider.Received[^1];
        Assert.Null(awarenessRequest.History); // WPF stateless ambient path (LocalAiService.cs:476-502)
    }

    [Fact]
    public async Task AssembledHistory_EqualsPersistedPairs_OnlyUserAssistantRoles_NothingSynthesized()
    {
        using var h = new Harness();
        await h.AdmitProviderAsync();

        await h.Pipeline.RunInteractiveAsync(new AiRequest("question one"));
        await h.Pipeline.RunInteractiveAsync(new AiRequest("question two"));
        await h.Pipeline.RunInteractiveAsync(new AiRequest("question three"));

        var history = h.Provider.Received[^1].History;
        Assert.NotNull(history);
        // Exactly the persisted pairs AT ASSEMBLY TIME (pairs 1-2; op3's own pair appends
        // only after its reply passes moderation) — nothing synthesized. System/enrichment
        // exclusion is by construction (the greenfield request shape carries neither channel;
        // WPF's IsDialogueTurn filter, LocalAiService.cs:166-170, has no greenfield
        // counterpart to port).
        Assert.Equal(
            [
                new AiMemoryTurn(AiMemoryRole.User, "question one"),
                new AiMemoryTurn(AiMemoryRole.Assistant, "reply to: question one"),
                new AiMemoryTurn(AiMemoryRole.User, "question two"),
                new AiMemoryTurn(AiMemoryRole.Assistant, "reply to: question two"),
            ],
            history);
        Assert.All(history!, t => Assert.True(t.Role is AiMemoryRole.User or AiMemoryRole.Assistant));
    }

    [Fact]
    public async Task Trimming_RidesC4RetentionMechanism_AssemblyCarriesOnlyTheCappedWindow()
    {
        // retention = 1 pair ⇒ the store holds at most 2 turns; assembly forwards exactly that.
        using var h = new Harness(retention: new AiMemoryRetention(1));
        await h.AdmitProviderAsync();

        await h.Pipeline.RunInteractiveAsync(new AiRequest("old question"));
        await h.Pipeline.RunInteractiveAsync(new AiRequest("recent question"));
        await h.Pipeline.RunInteractiveAsync(new AiRequest("third question"));

        Assert.Equal(2, h.Memory.ReadRecent(100).Count); // the c4 append-trim did the bounding
        Assert.Equal(
            [new AiMemoryTurn(AiMemoryRole.User, "recent question"), new AiMemoryTurn(AiMemoryRole.Assistant, "reply to: recent question")],
            h.Provider.Received[^1].History);
    }

    [Fact]
    public async Task DefaultConsentStore_DeniesWriteAndRead_PlaceholderDefaultIsExecutable()
    {
        // The placeholder default is Denied (§9.2 #3 owner-pending; WPF baseline FACT: true,
        // CompanionPromptSettings.cs:120 — the tension is recorded in record.md §1 verbatim).
        using var h = new Harness(defaultConsent: true);
        await h.AdmitProviderAsync();

        var result = await h.Pipeline.RunInteractiveAsync(new AiRequest("hello companion"));

        Assert.IsType<AiReply.Generated>(result.Reply); // the operation itself is unaffected
        Assert.Equal(AiMemoryWriteAdmission.ConsentDenied, h.Memory.LastWriteAdmission);
        Assert.Null(h.Provider.Received[0].History);
        Assert.Empty(h.Memory.ReadPromptContext());
    }

    /// <summary>
    /// The WIRE proof (in-scope self-contained listener — `AiProviderLab.cs` is outside this
    /// packet's File Scope): the REAL <see cref="LoopbackOllamaProvider"/> payload's messages
    /// array carries the persisted pair, in order, before the new prompt.
    /// </summary>
    [Fact]
    public async Task WirePayload_MessagesArrayCarriesPersistedPairBeforeNewPrompt()
    {
        using var listener = new WireListener();
        using var dir = new TempDir();
        var registry = new OperationRegistry();
        var capabilities = new CapabilityRegistry();
        var memory = new AiMemoryStore(
            registry.OwnerFor("AiMemory"), new ListLogSink(), dir.Path(AiMemoryStore.FileName),
            consent: () => AiMemoryConsent.Granted);
        await memory.StartAsync(CancellationToken.None);
        var pipeline = new AiOperationPipeline(
            registry, capabilities, LoopbackOnlyAdmissionPolicy.Instance,
            new CollectingAiDiagnosticsSink(), new AiModerationBoundary(), memory);
        pipeline.RegisterProvider(new LoopbackOllamaProvider(
            new LoopbackOllamaProviderOptions { Host = listener.Prefix, RequestTimeout = TimeSpan.FromSeconds(10) }));
        pipeline.SelectProvider(AiProviderId.LocalOllama);
        await new CapabilityProbeRunner(registry.OwnerFor("probes"), capabilities).RunAllAsync(CancellationToken.None);

        await pipeline.RunInteractiveAsync(new AiRequest("wire question one"));
        await pipeline.RunInteractiveAsync(new AiRequest("wire question two"));

        Assert.Equal(2, listener.ChatBodies.Count);
        // The FIRST request's payload is the pre-SP-047 single-message shape (wire-level
        // proof that empty history leaves the payload unchanged).
        using (var firstDoc = JsonDocument.Parse(listener.ChatBodies[0]))
        {
            Assert.Single(firstDoc.RootElement.GetProperty("messages").EnumerateArray());
        }

        using var doc = JsonDocument.Parse(listener.ChatBodies[1]);
        var messages = doc.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal(3, messages.Length);
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        Assert.Equal("wire question one", messages[0].GetProperty("content").GetString());
        Assert.Equal("assistant", messages[1].GetProperty("role").GetString());
        Assert.Equal(WireListener.ReplyText, messages[1].GetProperty("content").GetString());
        Assert.Equal("user", messages[2].GetProperty("role").GetString());
        Assert.Equal("wire question two", messages[2].GetProperty("content").GetString());
        // The pre-SP-047 payload shape is intact (model/stream/think untouched).
        Assert.False(doc.RootElement.GetProperty("stream").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("think").GetBoolean());
    }

    /// <summary>Minimal loopback capture (bind-retry per the AiProviderLab T-15 hardening): GET /api/version → 200 probe; POST /api/chat → valid native reply, body captured.</summary>
    private sealed class WireListener : IDisposable
    {
        public const string ReplyText = "wire lab reply";

        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _serve;

        public WireListener()
        {
            HttpListener? bound = null;
            for (var attempt = 0; attempt < 20 && bound is null; attempt++)
            {
                var port = Random.Shared.Next(49152, 65535);
                var candidate = new HttpListener();
                try
                {
                    candidate.Prefixes.Add($"http://127.0.0.1:{port}/");
                    candidate.Start();
                    bound = candidate;
                    Prefix = new Uri($"http://127.0.0.1:{port}/");
                }
                catch (HttpListenerException)
                {
                    candidate.Close();
                }
            }

            _listener = bound ?? throw new InvalidOperationException("WireListener: no loopback port available");
            _serve = Task.Run(ServeLoop);
        }

        public Uri Prefix { get; }

        /// <summary>Snapshot under the gate (the serve thread records on a listener thread; assertions read a stable copy — pre-completion consult hardening).</summary>
        public IReadOnlyList<string> ChatBodies
        {
            get { lock (_bodies) { return _bodies.ToArray(); } }
        }

        private readonly List<string> _bodies = [];

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Close();
            try { _serve.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
            _cts.Dispose();
        }

        private async Task ServeLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync().WaitAsync(_cts.Token);
                }
                catch (OperationCanceledException) { break; }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; } // teardown, never a product failure

                try
                {
                    var req = ctx.Request;
                    if (req.HttpMethod == "GET" && req.Url!.AbsolutePath == "/api/version")
                    {
                        await Write(ctx.Response, """{"version":"wire-lab"}""");
                        continue;
                    }

                    if (req.HttpMethod == "POST" && req.Url!.AbsolutePath == "/api/chat")
                    {
                        using var ms = new MemoryStream();
                        await req.InputStream.CopyToAsync(ms);
                        lock (_bodies)
                        {
                            _bodies.Add(System.Text.Encoding.UTF8.GetString(ms.ToArray()));
                        }

                        await Write(ctx.Response, $$"""{"model":"wire-lab","message":{"role":"assistant","content":"{{ReplyText}}"},"done":true}""");
                        continue;
                    }

                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
            }
        }

        private static async Task Write(HttpListenerResponse res, string body)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(body);
            res.ContentType = "application/json";
            res.ContentLength64 = bytes.Length;
            await res.OutputStream.WriteAsync(bytes);
            res.Close();
        }
    }

    private sealed class ListLogSink : ILogSink
    {
        public List<string> Messages { get; } = [];

        public void Log(string message) => Messages.Add(message);
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ccp-aimem-asm-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(_root);

        public string Path(string fileName) => System.IO.Path.Combine(_root, fileName);

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; the OS temp reaper owns the residue.
            }
        }
    }
}
