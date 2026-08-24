using System.Diagnostics;
using System.Text.Json;
using CcpClient.Desktop.Features.Arcademy;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Slice 1's other half: the boot handshake as BEHAVIOUR. The Arcademy row has carried the same
/// admission since slices 1-2 landed — "no browser was started, no page loaded, not one line of
/// the payload's JavaScript ran … the handshake and echo loop are proved as FRAMES AND STORE
/// EFFECTS, never as a page that received them". These two facts close exactly that, and nothing
/// wider.
///
/// <para><b>Why the existing facts could not.</b> <see cref="ArcademyProtocolTests"/> pins what
/// C# emits; <see cref="ArcademyServingTests"/> pins that the bytes are served. Both halves can
/// be green while the two sides disagree — a protocol number, a frame name, or the ORDER a frame
/// is allowed to arrive in. Only running the page's own code can tell them apart, and the page
/// really does refuse a host it disagrees with (<c>arcademy/boot.js:172-178</c> treats a protocol
/// mismatch as fatal and posts <c>boot-error</c> instead of booting).</para>
///
/// <para><b>What is real here, and it is the whole basis of the claim.</b> The JavaScript is the
/// SERVED payload — <see cref="ArcademyServingRoots.PayloadRoot"/>, the subtree
/// <see cref="Dtrh.LoopbackServer"/> publishes at <c>/dtrh/arcademy/*</c>, unmodified and
/// unshimmed. The host is a real <see cref="ArcademySession"/> answering live, not a script of
/// canned replies: the page's <c>ready</c> is fed to <see cref="ArcademySession.Handle"/> and
/// whatever that posts is what crosses back. The transport is the port's own — page→host
/// <c>postMessage</c> and host→page a <c>MessageEvent</c> on <c>window.chrome.webview</c>, the
/// pair <c>GoonHostWindow.axaml.cs:270</c> and <c>:629</c> already use against a real WebView2.
/// </para>
///
/// <para><b>The third fact is slice 5's, and it is here rather than beside the rest of slice 5
/// for one reason: upstream posts the native-state seed INSIDE the handshake</b>
/// (<c>SeedNativeState</c> is called from <c>OnPageReady</c>, <c>ArcademyHostService.cs:402</c>),
/// and the page's own buffer-and-replay for it (<c>arcademy/boot.js:195-205</c>,
/// <c>:148-153</c>) only exists because the frame lands mid-boot. It is the same live loop, one
/// boot, and it needs the page to keep talking after <c>shell live</c> — which is the only reason
/// <see cref="BootAsync"/> grew its two optional parameters.</para>
///
/// <para><b>WHAT THIS DOES NOT PROVE, stated first-class because the gap is the point of the
/// file.</b> The DOM is a double (see the harness header): there is no layout, no paint, no
/// compositor, no window and no user input. So a shell that reports itself LIVE here is a shell
/// whose boot logic completed — never a shell that was DRAWN, was legible, or could be clicked.
/// No pixel is claimed, no rendering, no interaction, no audio, no focus. That evidence needs a
/// headed capture, and the Arcademy door ships shut (<see cref="ArcademyDoor.Available"/>), so
/// for this surface it remains UNPROVEN rather than pending. No Linux claim is made either: this
/// ran on Windows only.</para>
/// </summary>
public sealed class ArcademyBootHandshakeTests : IDisposable
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] HarnessParts = ["client", "tests", "CcpClient.Tests", "arcademy-boot-harness.mjs"];

    /// <summary>What <c>boot.js</c> appends to a suspend it could not apply yet because the shell
    /// did not exist (<c>arcademy/boot.js:204-205</c>).</summary>
    private const string BufferedMarker = " [buffered until the shell exists]";

    /// <summary>bridge.js's boot allowlist verbatim (<c>arcademy/bridge.js:47</c>) — the only
    /// frame types permitted to leave the page before <c>init</c> has landed.</summary>
    private static readonly HashSet<string> BootLane =
        ["ready", "log", "heartbeat", "boot-error", "exit"];

    private readonly List<string> _log = [];
    private readonly string _dir;
    private readonly PersistenceStore<ArcademySettingsDocument> _store;

    public ArcademyBootHandshakeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ccp-arcademy-boot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _store = new PersistenceStore<ArcademySettingsDocument>(
            new OperationRegistry().OwnerFor(nameof(ArcademyBootHandshakeTests)),
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

    // ==================================================================================
    // The two facts.
    // ==================================================================================

    /// <summary>
    /// The handshake, end to end, with a real page on one side and a real host on the other:
    /// the page announces <c>ready</c>, the host answers with exactly one <c>init</c> and then
    /// <c>fullscreen</c> (<c>ArcademyHostService.cs:396-401</c>), the page ACCEPTS that init and
    /// the shell reports itself live (<c>arcademy/boot.js:144</c>, written only after
    /// <c>createShell</c> returned and <c>bootSettled</c> was set).
    /// </summary>
    [Fact]
    public async Task ThePageBoots_AndTheRealHostsInitTakesTheShellLive()
    {
        var run = await BootAsync();

        // The harness is not allowed to fail quietly and call it a boot.
        Assert.Empty(run.HarnessErrors);

        // 1. The page announced itself, on the boot lane, at the protocol C# speaks. This is the
        //    number that has never been checked from the page's side before: arcademy/bridge.js:29 says
        //    1 and ArcademyProtocol.Version says 1, and until now nothing made them agree.
        Assert.Equal(1, run.Frame("ready").GetProperty("protocol").GetInt32());

        // 2. The REAL host answered — exactly one init, then fullscreen, in upstream's order.
        Assert.Equal(new[] { "init", "fullscreen" }, run.HostTypes);
        Assert.Equal(ArcademyProtocol.Version, run.HostFrame(0).GetProperty("protocol").GetInt32());

        // 3. The page ACCEPTED it and booted the shell out of the served payload. Everything in
        //    this list is the payload's own voice over the bridge, not this test's narration.
        Assert.Contains("shell live", run.PageLogs);
        Assert.Contains("registry: 5/5 classes live", run.PageLogs);

        // 4. And it never failed the boot. boot.js posts `boot-error` on a protocol mismatch, a
        //    shell throw or its 45s deadline (:104, :114, :156), so its ABSENCE alongside a live
        //    shell is the difference between "booted" and "gave up honestly".
        Assert.DoesNotContain("boot-error", run.PageTypes);
        Assert.False(run.BootFailed);
    }

    /// <summary>
    /// The half of the handshake that makes it safe rather than merely complete: nothing
    /// gameplay-shaped may reach the host before the settings projection has (bridge.js's header,
    /// "nothing gameplay-shaped can race the settings projection"). The harness posts a real
    /// <c>meta-command</c> BEFORE boot.js is even imported, so it is queued before <c>ready</c>
    /// exists; bridge.js holds everything outside its boot allowlist (<c>:47</c>) until
    /// <c>markInitialized</c> flushes it (<c>:114-118</c>, <c>:125-129</c>).
    /// </summary>
    [Fact]
    public async Task NothingGameplayShaped_ReachesTheHostBeforeInit()
    {
        var run = await BootAsync();
        var probe = run.IndexOfType("meta-command");
        var ready = run.IndexOfType("ready");

        // It did arrive — a frame silently dropped would pass an ordering check vacuously.
        Assert.True(probe >= 0, "the pre-init meta-command never reached the host at all: " + run.Transcript);

        // It was posted first and arrived last: the queue held it across the whole boot lane.
        Assert.True(probe > ready, "the meta-command overtook `ready`: " + run.Transcript);

        // The real claim, and the general form of it: every frame ahead of the flush is on
        // bridge.js's boot allowlist. A leak of any other type lands in this list, by name.
        var leaked = run.PageTypes.Take(probe).Where(type => !BootLane.Contains(type)).ToList();
        Assert.True(
            leaked.Count == 0,
            $"frames left the page before init that are NOT on bridge.js's boot allowlist: " +
            $"[{string.Join(", ", leaked)}] — {run.Transcript}");

        // And the host recognized what finally arrived as real vocabulary rather than filing it
        // under `unhandled message` — the queue must not smuggle a frame past the router.
        Assert.DoesNotContain(run.HostLog, line => line.Contains("unhandled message 'meta-command'", StringComparison.Ordinal));
    }

    /// <summary>
    /// <b>SLICE 5, PAGE-SIDE: a covering native video really does freeze the real shell, and the
    /// class really does come back when the video ends.</b> Every other suspension fact in this
    /// suite pins the FRAME the host emits; this one runs the payload's own freeze path
    /// (<c>arcademy/shell/shell.js:1250-1272</c> <c>applySuspend</c>, reached through
    /// <c>:1344</c> <c>onSuspend</c>) on the real bytes and reads what the page says about it.
    ///
    /// <para><b>Why the page's own words are evidence rather than narration.</b> Each of the three
    /// lines asserted below is written by <c>boot.js</c> AFTER the shell's suspend path returned,
    /// and never if it threw: the replay line is inside the try whose catch writes "buffered
    /// suspend replay threw" instead (<c>:148-153</c>), and the live-shell line sits after
    /// <c>shell.onSuspend(m)</c> inside a <c>guard()</c> whose catch writes "suspend handler
    /// threw" and skips it (<c>:190-205</c>). So their presence means the real
    /// <c>applySuspend</c> ran end to end — it set the global freeze, re-rendered the topbar with
    /// the <c>class_suspended</c> chip and re-rendered the board with every class button disabled
    /// (<c>shell.js:343</c>, <c>:383</c>).</para>
    ///
    /// <para><b>WHICH of the page's two seed paths runs is a RACE, and this fact refuses to pin
    /// it.</b> The seed is posted at the tail of the handshake, and the shell is built behind two
    /// dynamic imports (<c>boot.js:129-135</c>), so the frame may find the shell already there or
    /// still missing — measured BOTH ways on this machine. The page handles each: it applies the
    /// suspend directly, or BUFFERS it and replays it once the shell exists (<c>:198-203</c>,
    /// <c>:148-153</c>). So the assertion is the invariant that holds under either — <b>every
    /// buffered suspend is replayed</b> — and never the ordering that happened to occur.</para>
    ///
    /// <para><b>What it still does not prove:</b> nothing was DRAWN. The DOM is a double, so a
    /// class that reports itself frozen is one whose freeze LOGIC ran — never one a user could
    /// see was frozen. No pixel, no layout, no input, no audio, no window. Windows only.</para>
    /// </summary>
    [Fact]
    public async Task ACoveringNativeVideo_FreezesTheRealShell_AndItsEndLiftsTheFreeze()
    {
        var run = await BootAsync(
            // A mandatory video is ALREADY covering the screen when the page opens — upstream's
            // own reason for seeding at all (ArcademyHostService.cs:410-413).
            configure: session => session.NativeStateOwnsScreen = () => true,
            afterShellLive: async driver =>
            {
                // The video ends. This is the exact call ArcademyNativeSuspension makes when the
                // port's mandatory video takes its picture down.
                driver.Session.NativeVideoChanged(playing: false);
                await driver.PageSaid("suspend off (video)");
            });

        Assert.Empty(run.HarnessErrors);
        Assert.DoesNotContain("boot-error", run.PageTypes);

        // The host's side: the handshake, then the seed, then the lift. Nothing else.
        Assert.Equal(new[] { "init", "fullscreen", "suspend", "suspend" }, run.HostTypes);
        Assert.True(run.HostFrame(2).GetProperty("on").GetBoolean());
        Assert.Equal("video", run.HostFrame(2).GetProperty("reason").GetString());
        Assert.False(run.HostFrame(3).GetProperty("on").GetBoolean());

        // The page's side, in the payload's own voice. The buffered marker is normalized away
        // because which path the seed took is a race (see the remarks); the invariant that a
        // buffered suspend is REPLAYED rather than dropped is asserted on its own below.
        var logs = run.PageLogs;
        var narration = logs.Select(l => l.Replace(BufferedMarker, "", StringComparison.Ordinal)).ToList();
        var froze = narration.IndexOf("suspend ON (video)");
        var live = narration.IndexOf("shell live");
        var lifted = narration.IndexOf("suspend off (video)");
        Assert.True(
            froze >= 0 && live >= 0 && lifted > froze,
            "the page did not narrate the seed and then the lift: " + string.Join(" | ", logs));

        // Nothing was dropped on the way: every suspend that arrived before the shell existed was
        // replayed into it. Zero and zero on a run where the shell won the race, one and one on a
        // run where it did not — and 1/0 is the state that would leave a class frozen for good.
        Assert.Equal(
            logs.Count(l => l.Contains(BufferedMarker, StringComparison.Ordinal)),
            logs.Count(l => l == "replayed buffered suspend (video)"));

        // And the shell never fell over on either edge — the two lines that would replace them.
        Assert.DoesNotContain(logs, l => l.Contains("suspend handler threw", StringComparison.Ordinal));
        Assert.DoesNotContain(logs, l => l.Contains("buffered suspend replay threw", StringComparison.Ordinal));
    }

    // ==================================================================================
    // The live loop. Kept out of the [Fact] bodies: filesystem and process plumbing in a
    // fact body is a silencing shape (VacuousShapeDetector), and the facts above are meant
    // to read as the transcript they assert over.
    // ==================================================================================

    /// <summary>
    /// Runs one whole boot: spawn the harness on the SERVED payload, wire a real
    /// <see cref="ArcademySession"/> to its stdio, and return the transcript both sides produced.
    /// The wait is the approved bounded helper and the terminal condition is a deterministic
    /// signal — the page's own `shell live` or `boot-error` — never an elapsed guess.
    /// </summary>
    /// <param name="configure">Applied to the live session BEFORE the page can say anything, which
    /// is the only place a native state that is already true at boot can be set: upstream hooks
    /// its own producers before the window is shown for exactly this reason
    /// (<c>ArcademyHostService.cs:205-212</c>).</param>
    /// <param name="afterShellLive">Runs once the page has settled its boot and BEFORE stdin is
    /// closed, so a fact can keep the conversation going. Its waits are the page's own words
    /// (<see cref="BootDriver.PageSaid"/>) — never an elapsed guess, and never a poll.</param>
    private async Task<BootRun> BootAsync(
        Action<ArcademySession>? configure = null,
        Func<BootDriver, Task>? afterShellLive = null)
    {
        var harness = Path.Combine([FindRepoRoot(), .. HarnessParts]);
        Assert.True(File.Exists(harness), $"the arcademy boot harness is missing at {harness}");
        var payload = ArcademyServingRoots.PayloadRoot;
        Assert.True(Directory.Exists(payload), $"the served arcademy payload is missing at {payload}");

        var start = new ProcessStartInfo("node")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = FindRepoRoot(),
        };
        start.ArgumentList.Add(harness);
        start.ArgumentList.Add(payload);

        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // A FAILURE, never a skip — the precedent is ExecutionCensusTests.cs:641 and the
            // reason is the same: both tier-1 gates of this tree are node scripts, so a machine
            // that cannot run node cannot run its own gates.
            throw new InvalidOperationException(
                "could not start `node` to boot the Arcademy payload — node is a hard requirement " +
                "of this tree (check-warnings.mjs, check-floor.mjs) and this fact refuses to skip", ex);
        }

        using (process)
        {
            process.StandardInput.AutoFlush = true;

            var gate = new object();
            var pageFrames = new List<string>();
            var hostFrames = new List<string>();
            var harnessErrors = new List<string>();
            var pageLogs = new List<string>();
            var waiters = new List<(string Message, TaskCompletionSource Landed)>();
            var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var session = new ArcademySession(
                _store,
                new ArcademyAppFacts(),
                frame =>
                {
                    var json = ArcademyProtocol.SerializeForPage(frame);
                    lock (gate)
                    {
                        hostFrames.Add(json);
                    }

                    try
                    {
                        process.StandardInput.WriteLine(json);
                    }
                    catch (IOException)
                    {
                        // The harness exited first. The transcript still records what the host
                        // tried to send, which is what a failure needs to be diagnosable.
                    }
                },
                new SinkAdapter(_log));

            // Before a single byte can arrive: the native state a page opening RIGHT NOW would
            // meet is not something a fact can set once the handshake is under way.
            configure?.Invoke(session);

            var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            var reader = Task.Run(async () =>
            {
                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken)) is not null)
                {
                    if (line.StartsWith("X ", StringComparison.Ordinal))
                    {
                        lock (gate)
                        {
                            harnessErrors.Add(line[2..]);
                        }

                        settled.TrySetResult();
                        continue;
                    }

                    if (!line.StartsWith("F ", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var json = line[2..];
                    List<TaskCompletionSource> landed = [];
                    lock (gate)
                    {
                        pageFrames.Add(json);
                        if (PageLogOf(json) is { } said)
                        {
                            pageLogs.Add(said);
                            // Wake anything waiting on this exact sentence, and stop waiting on it.
                            landed = [.. waiters.Where(w => w.Message == said).Select(w => w.Landed)];
                            waiters.RemoveAll(w => w.Message == said);
                        }
                    }

                    foreach (var wake in landed)
                    {
                        wake.TrySetResult();
                    }

                    // THE LIVE HALF: the page's frame goes straight into the real router, and
                    // anything the session posts in response is written back over the bridge by
                    // the sink above. Nothing here decides what the answer should be.
                    session.Handle(json);

                    if (IsTerminal(json))
                    {
                        settled.TrySetResult();
                    }
                }
            }, TestContext.Current.CancellationToken);

            // A DETERMINISTIC wait on the page's own voice. The sentence has either already
            // arrived or a waiter is armed for it before this returns, so there is no window in
            // which it can be missed — and no poll, no sleep and no elapsed guess anywhere in it.
            Task PageSaid(string message)
            {
                var landed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (gate)
                {
                    if (pageLogs.Contains(message))
                    {
                        landed.TrySetResult();
                    }
                    else
                    {
                        waiters.Add((message, landed));
                    }
                }

                return TestWait.Until(
                    landed.Task,
                    $"the Arcademy page to say `{message}`",
                    () =>
                    {
                        lock (gate)
                        {
                            return $"page said: {string.Join(" | ", pageLogs)}";
                        }
                    });
            }

            try
            {
                await TestWait.Until(
                    settled.Task,
                    "the Arcademy page to settle its boot (`shell live` or `boot-error`)",
                    () =>
                    {
                        lock (gate)
                        {
                            return $"page frames={pageFrames.Count}, host frames={hostFrames.Count}";
                        }
                    });

                if (afterShellLive is not null)
                {
                    // The page is up and still listening. Whatever this posts crosses the same
                    // bridge, and every wait inside it is the page's own answer coming back.
                    await afterShellLive(new BootDriver(session, PageSaid));
                }

                // Closing stdin is how the harness is asked to leave (it has no timer of its own).
                process.StandardInput.Close();
                await TestWait.Until(process.WaitForExitAsync(TestContext.Current.CancellationToken), "the boot harness to exit");
                await TestWait.Until(reader, "the harness transcript to drain");
            }
            catch
            {
                // A wedged node must not outlive the fact that started it: the window expiring is
                // already the failure and stays one, this only stops it leaving a process behind.
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // It exited between the window expiring and the kill.
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // The OS refused the kill; reporting that would bury the real failure.
                }

                throw;
            }

            var standardError = await stderr;
            lock (gate)
            {
                return new BootRun([.. pageFrames], [.. hostFrames], [.. _log], [.. harnessErrors],
                    session.BootFailed, standardError);
            }
        }
    }

    /// <summary>The message of a page <c>log</c> frame, or null for any other frame. The payload's
    /// diagnostics channel is the only voice it has without devtools
    /// (<c>arcademy/boot.js:57-59</c>).</summary>
    private static string? PageLogOf(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("type", out var type) || type.GetString() != "log")
        {
            return null;
        }

        return root.TryGetProperty("msg", out var msg) ? msg.GetString() : null;
    }

    /// <summary>Everything a fact is allowed to do to a page that is already up: post through the
    /// REAL host, and wait on the page's own answer.</summary>
    /// <param name="Session">The live host. Nothing here decides what the page will say.</param>
    /// <param name="Await">Waits for one exact sentence from the payload's diagnostics channel.</param>
    private sealed record BootDriver(ArcademySession Session, Func<string, Task> Await)
    {
        /// <summary>Wait until the page has said exactly this. A deterministic signal from the
        /// payload itself — never an elapsed guess.</summary>
        public Task PageSaid(string message) => Await(message);
    }

    /// <summary>The page's own end-of-boot signals — the only two ways boot.js settles
    /// (<c>:141</c> sets bootSettled after the shell is built; <c>:110-114</c> after a failure).</summary>
    private static bool IsTerminal(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (type == "boot-error")
        {
            return true;
        }

        return type == "log"
            && root.TryGetProperty("msg", out var msg)
            && msg.GetString() == "shell live";
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
            $"repo root not found walking up from {AppContext.BaseDirectory} — the arcademy boot fact refuses to skip");
    }

    /// <summary>Both sides of one boot, in arrival order.</summary>
    private sealed record BootRun(
        IReadOnlyList<string> PageJson,
        IReadOnlyList<string> HostJson,
        IReadOnlyList<string> HostLog,
        IReadOnlyList<string> HarnessErrors,
        bool BootFailed,
        string StandardError)
    {
        public IReadOnlyList<string> PageTypes => [.. PageJson.Select(TypeOf)];

        public IReadOnlyList<string> HostTypes => [.. HostJson.Select(TypeOf)];

        /// <summary>Every <c>log</c> frame's message — the payload narrating its own boot.</summary>
        public IReadOnlyList<string> PageLogs =>
        [
            .. PageJson
                .Select(json => JsonDocument.Parse(json).RootElement)
                .Where(root => TypeOfElement(root) == "log")
                .Select(root => root.TryGetProperty("msg", out var m) ? m.GetString() ?? "" : "")
        ];

        public int IndexOfType(string type) => PageTypes.ToList().IndexOf(type);

        public JsonElement Frame(string type) =>
            JsonDocument.Parse(PageJson[IndexOfType(type)]).RootElement.Clone();

        public JsonElement HostFrame(int index) =>
            JsonDocument.Parse(HostJson[index]).RootElement.Clone();

        /// <summary>The whole conversation, for a failure message — an ordering assertion that
        /// fails without showing the order it saw is one nobody can act on.</summary>
        public string Transcript =>
            $"page=[{string.Join(", ", PageTypes)}] host=[{string.Join(", ", HostTypes)}]" +
            (StandardError.Length > 0 ? " stderr=" + StandardError : "");

        private static string TypeOf(string json) => TypeOfElement(JsonDocument.Parse(json).RootElement);

        private static string TypeOfElement(JsonElement root) =>
            root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
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
