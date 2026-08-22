using System.Net;
using System.Text;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Haptics;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The Lovense route's wire, against a REAL loopback HTTP server rather than a mocked
/// <c>HttpClient</c>. The thing this route can get wrong is the shape of a request another program
/// parses, and a fake that returns whatever the sink asked for cannot fail on a wrong shape.
///
/// <para>What is NOT claimed here: that a motor moved. Every fact below observes the SERVER's view
/// of a request. Whether hardware responded is a device gate a human reports, and the row says so.</para>
/// </summary>
public sealed class LovenseHapticSinkTests : IDisposable
{
    private readonly ToyServer _server = new();

    public void Dispose() => _server.Dispose();

    // -----------------------------------------------------------------------------------
    //  Quantization — upstream's map, including the levels it refuses to use
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Levels 1 and 2 are unreachable by design (<c>LovenseProvider.cs:195-204</c>): they are
    /// imperceptible on the hardware, so the map jumps the bottom of the range rather than spending
    /// a quiet setting on something a user would read as broken. A zero stays a true zero — no
    /// floor is ever substituted for silence.
    /// </summary>
    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.05, 0)]      // the off boundary, inclusive
    [InlineData(0.0500001, 3)] // the first audible step skips 1 and 2
    [InlineData(0.5, 11)]
    [InlineData(1.0, 20)]
    public void TheLevelMapIsUpstreams_AndOneAndTwoAreUnreachable(double intensity, int expected) =>
        Assert.Equal(expected, LovenseHapticSink.QuantizeLevel(HapticLevel.Of(intensity)));

    [Fact]
    public void NoIntensityAnywhereInTheRangeEverProducesALevelOfOneOrTwo()
    {
        // Swept first, asserted OUTSIDE the loop. An assertion that only exists inside a loop body
        // passes silently if the loop never runs, and "this range contains no 1 or 2" is exactly the
        // claim that would be worth nothing if the sweep were empty — so the count is asserted too.
        var offenders = new List<string>();
        var swept = 0;
        for (var i = 0; i <= 1000; i++)
        {
            var intensity = i / 1000.0;
            var level = LovenseHapticSink.QuantizeLevel(HapticLevel.Of(intensity));
            swept++;
            if (level is 1 or 2)
            {
                offenders.Add($"{intensity:0.###} -> {level}");
            }
        }

        Assert.Equal(1001, swept);
        Assert.Empty(offenders);
    }

    /// <summary>The strongest actuator wins. This route addresses one toy per command, so a
    /// multi-actuator request is REDUCED rather than truncated to whatever happened to be first.</summary>
    [Fact]
    public void AMultiActuatorRequestSendsTheStrongestLevel_NotTheFirstOne()
    {
        var outputs = new[]
        {
            new HapticOutput(0, HapticLevel.Of(0.10)),
            new HapticOutput(1, HapticLevel.Of(0.90)),
            new HapticOutput(2, HapticLevel.Of(0.20)),
        };

        Assert.Equal(LovenseHapticSink.QuantizeLevel(HapticLevel.Of(0.90)),
            LovenseHapticSink.QuantizeOutputs(outputs));
    }

    // -----------------------------------------------------------------------------------
    //  The two command shapes, read off the server
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task LanModePostsTheFunctionCommandCarryingItsOwnExpiry()
    {
        using var sink = new LovenseHapticSink(_ => { }, _server.BaseUrl, LovenseHapticSink.LovenseMode.Lan);

        var outcome = await sink.SetOutputsAsync("toy-a", [new HapticOutput(0, HapticLevel.Of(1.0))], TestContext.Current.CancellationToken);

        Assert.IsType<CapabilityState.Available>(outcome);
        var request = Assert.Single(_server.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/command", request.Path);

        // The exact payload another program parses. timeSec is the SERVER's expiry, and it carries
        // the whole-second floor the LAN API imposes on itself.
        Assert.Contains("\"command\":\"Function\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"action\":\"Vibrate:20\"", request.Body, StringComparison.Ordinal);
        Assert.Contains($"\"timeSec\":{LovenseHapticSink.LanHoldSeconds}", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"apiVer\":1", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectModeGetsTheVibrateCommand_WithNoExpiryAndTheToyAddressed()
    {
        using var sink = new LovenseHapticSink(_ => { }, _server.BaseUrl, LovenseHapticSink.LovenseMode.Connect);

        await sink.SetOutputsAsync("toy b/1", [new HapticOutput(0, HapticLevel.Of(0.5))], TestContext.Current.CancellationToken);

        var request = Assert.Single(_server.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Contains("command=Vibrate", request.Query, StringComparison.Ordinal);
        Assert.Contains("intensity=11", request.Query, StringComparison.Ordinal);

        // Escaped, because a toy id is server data and a raw one would break the query it lands in.
        Assert.Contains("toy=toy%20b%2F1", request.Query, StringComparison.Ordinal);

        // Connect mode expires nothing; sending an expiry here would hand the device a deadline the
        // caller never asked for.
        Assert.DoesNotContain("timeSec", request.Query, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------------------
    //  The throttle, and the safety hole it deliberately does not reproduce
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// An identical repeat is suppressed, and EVERY change reaches the wire — above all a change to
    /// zero.
    ///
    /// <para>This is the one deliberate divergence from upstream. Upstream rate-limits continuous
    /// sends to one per 200 ms and returns early REGARDLESS OF LEVEL
    /// (<c>LovenseProvider.cs:207-224</c>), so a command that zeroes the toy can be dropped because
    /// an unrelated one went out 190 ms earlier. On the path whose job is making a device stop, that
    /// is not a rate limit, it is a lost stop.</para>
    /// </summary>
    [Fact]
    public async Task AnIdenticalRepeatIsSuppressed_ButAChangeToZeroAlwaysReachesTheWire()
    {
        using var sink = new LovenseHapticSink(_ => { }, _server.BaseUrl, LovenseHapticSink.LovenseMode.Lan);
        var ct = TestContext.Current.CancellationToken;

        await sink.SetOutputsAsync("toy-a", [new HapticOutput(0, HapticLevel.Of(0.8))], ct);
        Assert.Single(_server.Requests);

        // Same level, back to back: nothing new on the wire.
        await sink.SetOutputsAsync("toy-a", [new HapticOutput(0, HapticLevel.Of(0.8))], ct);
        Assert.Single(_server.Requests);

        // A change to zero, immediately after — the case upstream's time-based throttle can eat.
        await sink.SetOutputsAsync("toy-a", [new HapticOutput(0, HapticLevel.Silent)], ct);
        Assert.Equal(2, _server.Requests.Count);
        Assert.Contains("\"action\":\"Vibrate:0\"", _server.Requests[1].Body, StringComparison.Ordinal);
    }

    /// <summary>Suppression is per DEVICE. One toy holding a level must never silence another toy's
    /// identical command.</summary>
    [Fact]
    public async Task SuppressionIsPerDevice_SoOneToyNeverSwallowsAnothersCommand()
    {
        using var sink = new LovenseHapticSink(_ => { }, _server.BaseUrl, LovenseHapticSink.LovenseMode.Lan);
        var ct = TestContext.Current.CancellationToken;
        HapticOutput[] level = [new HapticOutput(0, HapticLevel.Of(0.6))];

        await sink.SetOutputsAsync("toy-a", level, ct);
        await sink.SetOutputsAsync("toy-b", level, ct);

        Assert.Equal(2, _server.Requests.Count);
    }

    // -----------------------------------------------------------------------------------
    //  Stop
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// A stop zeroes every device the sink drove, and it is NOT suppressed by the sink's own memory
    /// that zero already stands. That memory is a record of a command the server accepted, never
    /// evidence that the toy is quiet now, and a teardown is the one moment where being wrong about
    /// that is unacceptable.
    /// </summary>
    [Fact]
    public async Task StopZeroesEveryDeviceItDrove_EvenWhenItBelievesZeroAlreadyStands()
    {
        using var sink = new LovenseHapticSink(_ => { }, _server.BaseUrl, LovenseHapticSink.LovenseMode.Lan);
        var ct = TestContext.Current.CancellationToken;

        await sink.SetOutputsAsync("toy-a", [new HapticOutput(0, HapticLevel.Of(0.7))], ct);
        await sink.SetOutputsAsync("toy-b", [new HapticOutput(0, HapticLevel.Of(0.4))], ct);
        await sink.SetOutputsAsync("toy-a", [new HapticOutput(0, HapticLevel.Silent)], ct);
        var before = _server.Requests.Count;

        var stop = await sink.StopAllAsync();

        Assert.IsType<CapabilityState.Available>(stop);
        var sent = _server.Requests.Skip(before).ToList();
        Assert.Equal(2, sent.Count);
        Assert.All(sent, r => Assert.Contains("\"action\":\"Vibrate:0\"", r.Body, StringComparison.Ordinal));
    }

    /// <summary>Nothing to stop is REPORTED, never claimed as a successful stop: "nothing was
    /// running" and "everything was stopped" are different facts.</summary>
    [Fact]
    public async Task StopWithNothingEverDriven_IsDegradedRatherThanASuccess()
    {
        using var sink = new LovenseHapticSink(_ => { }, _server.BaseUrl, LovenseHapticSink.LovenseMode.Lan);

        var stop = await sink.StopAllAsync();

        var degraded = Assert.IsType<CapabilityState.Degraded>(stop);
        Assert.Equal(HapticReasonCodes.HapticNoDevice, degraded.Reason.Code);
        Assert.Empty(_server.Requests);
    }

    // -----------------------------------------------------------------------------------
    //  Refusals — typed, never thrown
    // -----------------------------------------------------------------------------------

    /// <summary>No server running is the ORDINARY case for a user who owns no Lovense toy. It is a
    /// typed answer naming the separate program, not an exception and not "no device found".</summary>
    [Fact]
    public async Task NoServerAnswering_IsATypedRefusalNamingTheSeparateProgram()
    {
        // A port nothing is listening on. Bound and released, so the refusal is a real connection
        // refusal rather than a guess at an unused number.
        var dead = ToyServer.ReserveAndReleasePort();
        using var sink = new LovenseHapticSink(_ => { }, dead, LovenseHapticSink.LovenseMode.Lan);

        var connect = await sink.ConnectAsync(TestContext.Current.CancellationToken);

        var unavailable = Assert.IsType<CapabilityState.Unavailable>(connect);
        Assert.Equal(HapticReasonCodes.HapticServerUnreachable, unavailable.Reason.Code);
        Assert.Contains("Lovense Connect or Lovense Remote", unavailable.Reason.Detail, StringComparison.Ordinal);
    }

    /// <summary>A server that ANSWERS with no toy is a different fact from a server that is not
    /// running, and collapsing them would tell a user to restart an app that is already up.</summary>
    [Fact]
    public async Task AServerAnsweringWithNoToy_IsDegradedNotUnreachable()
    {
        _server.ToyKeys = [];
        using var sink = new LovenseHapticSink(_ => { }, _server.BaseUrl, LovenseHapticSink.LovenseMode.Lan);

        var connect = await sink.ConnectAsync(TestContext.Current.CancellationToken);

        var degraded = Assert.IsType<CapabilityState.Degraded>(connect);
        Assert.Equal(HapticReasonCodes.HapticNoDevice, degraded.Reason.Code);
    }

    [Fact]
    public async Task AServerAnsweringWithToys_IsAvailableAndEnumeratesThem()
    {
        _server.ToyKeys = ["abc123", "def456"];
        using var sink = new LovenseHapticSink(_ => { }, _server.BaseUrl, LovenseHapticSink.LovenseMode.Lan);

        var observation = await sink.ObserveAsync(TestContext.Current.CancellationToken);

        Assert.True(observation.Confirmed);
        Assert.Equal(2, observation.DeviceCount);
        Assert.Contains("abc123", observation.DeviceKeys);
        Assert.IsType<CapabilityState.Available>(await sink.ConnectAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ADisposedSinkRefusesTyped_RatherThanThrowingOnATeardownRace()
    {
        var sink = new LovenseHapticSink(_ => { }, _server.BaseUrl, LovenseHapticSink.LovenseMode.Lan);
        sink.Dispose();

        var outcome = await sink.SetOutputsAsync(
            "toy-a", [new HapticOutput(0, HapticLevel.Of(0.5))], TestContext.Current.CancellationToken);

        var unavailable = Assert.IsType<CapabilityState.Unavailable>(outcome);
        Assert.Equal(HapticReasonCodes.HapticSinkDisposed, unavailable.Reason.Code);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task AnEmptyOutputListIsACallerError_NeverASilentStop()
    {
        using var sink = new LovenseHapticSink(_ => { }, _server.BaseUrl, LovenseHapticSink.LovenseMode.Lan);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sink.SetOutputsAsync("toy-a", [], TestContext.Current.CancellationToken));
    }

    // -----------------------------------------------------------------------------------

    /// <summary>A real Lovense-shaped server on loopback, recording what actually arrived.</summary>
    private sealed class ToyServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _serve;
        private readonly List<Recorded> _requests = [];

        public ToyServer()
        {
            HttpListener? bound = null;
            string? prefix = null;
            for (var attempt = 0; attempt < 20 && bound is null; attempt++)
            {
                var port = Random.Shared.Next(49152, 65535);
                var candidate = new HttpListener();
                try
                {
                    candidate.Prefixes.Add($"http://127.0.0.1:{port}/");
                    candidate.Start();
                    bound = candidate;
                    prefix = $"http://127.0.0.1:{port}";
                }
                catch (HttpListenerException)
                {
                    candidate.Close();
                }
            }

            _listener = bound ?? throw new InvalidOperationException("ToyServer: no loopback port available");
            BaseUrl = prefix!;
            LoopbackListenerRegistry.Register(nameof(ToyServer), new Uri(BaseUrl).Port, BaseUrl);
            _serve = Task.Run(ServeLoop);
        }

        public string BaseUrl { get; }

        public IReadOnlyList<string> ToyKeys { get; set; } = ["toy-a"];

        public IReadOnlyList<Recorded> Requests
        {
            get { lock (_requests) { return _requests.ToArray(); } }
        }

        /// <summary>Binds a loopback port and lets it go, so a refusal fact refuses against a port
        /// that is genuinely free rather than one guessed to be.</summary>
        public static string ReserveAndReleasePort()
        {
            var listener = new HttpListener();
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var port = Random.Shared.Next(49152, 65535);
                try
                {
                    listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                    listener.Start();
                    listener.Stop();
                    listener.Close();
                    return $"http://127.0.0.1:{port}";
                }
                catch (HttpListenerException)
                {
                    listener.Prefixes.Clear();
                }
            }

            throw new InvalidOperationException("ToyServer: no loopback port available to reserve");
        }

        private async Task ServeLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch { return; }

                var request = context.Request;
                var body = string.Empty;
                if (request.HasEntityBody)
                {
                    using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                    body = await reader.ReadToEndAsync();
                }

                var path = request.Url?.AbsolutePath ?? string.Empty;
                if (!path.EndsWith("/GetToys", StringComparison.OrdinalIgnoreCase))
                {
                    lock (_requests)
                    {
                        _requests.Add(new Recorded(request.HttpMethod, path, request.Url?.Query ?? string.Empty, body));
                    }
                }

                var payload = path.EndsWith("/GetToys", StringComparison.OrdinalIgnoreCase)
                    ? "{" + string.Join(",", ToyKeys.Select(k => $"\"{k}\":{{\"id\":\"{k}\",\"status\":1}}")) + "}"
                    : "{\"code\":200}";

                var bytes = Encoding.UTF8.GetBytes(payload);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();

            // Unregistered ONLY after the listener really closed. The registry's rule is that a
            // failed dispose stays registered, because a leak report that lied would fail the whole
            // assembly loud on a false fact.
            var closed = false;
            try
            {
                _listener.Stop();
                _listener.Close();
                closed = true;
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                // Stays registered on purpose.
            }

            if (closed)
            {
                LoopbackListenerRegistry.Unregister(new Uri(BaseUrl).Port);
            }

            // No join on the serve task, and therefore no wall-clock wait to pin. Closing the
            // listener is what ends the loop: the pending GetContextAsync faults and the loop
            // returns. Waiting on it would add a deadline whose only job is to expire.
            _cts.Dispose();
        }

        internal sealed record Recorded(string Method, string Path, string Query, string Body);
    }
}
