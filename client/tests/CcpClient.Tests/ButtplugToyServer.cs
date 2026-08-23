using System.Net.Sockets;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CcpClient.Tests;

/// <summary>
/// A real Buttplug server on loopback, speaking the protocol the shipped client actually emits.
///
/// <para><b>Every message shape here was CAPTURED, not read off a specification.</b> A scratch probe
/// pointed the real <c>ButtplugClient</c> at a logging socket and recorded the traffic, which
/// corrected two things a spec page would have got wrong: the library announces
/// <c>ProtocolVersionMajor: 4</c> even though its README says "Version 3 Buttplug Spec", and
/// <c>DeviceList.Devices</c> is a DICTIONARY keyed by device index rather than an array — an array
/// parses to zero devices in silence. Building this from the documentation would have produced a
/// server that answers politely and proves nothing.</para>
///
/// <para>The captured exchange, verbatim:</para>
/// <code>
/// C-&gt;S [{"RequestServerInfo":{"ClientName":"...","ProtocolVersionMajor":4,"ProtocolVersionMinor":0,"Id":1}}]
/// S-&gt;C [{"ServerInfo":{"Id":1,"ServerName":"...","MaxPingTime":0,"ProtocolVersionMajor":4,"ProtocolVersionMinor":0}}]
/// C-&gt;S [{"RequestDeviceList":{"Id":2}}]
/// S-&gt;C [{"DeviceList":{"Id":2,"Devices":{"0":{...}}}}]
/// C-&gt;S [{"StartScanning":{"Id":3}}]            S-&gt;C [{"Ok":{"Id":3}}]
/// C-&gt;S [{"OutputCmd":{"Id":5,"DeviceIndex":0,"FeatureIndex":0,"Command":{"Vibrate":{"Value":15}}}}]
/// C-&gt;S [{"StopCmd":{"DeviceIndex":0,"Id":6}}]
/// </code>
///
/// <para><b>The Value is the library's own quantization.</b> <c>Percent(0.75)</c> against a feature
/// advertising <c>Value:[0,20]</c> arrived as <c>15</c>. That is the evidence that the sink must
/// pass 0..1 through rather than rounding first.</para>
/// </summary>
internal sealed class ButtplugToyServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _serve;
    private readonly List<Recorded> _commands = [];

    public ButtplugToyServer(int vibrateFeatures = 1, bool withDevice = true)
    {
        VibrateFeatures = vibrateFeatures;
        WithDevice = withDevice;

        HttpListener? bound = null;
        string? origin = null;
        for (var attempt = 0; attempt < 20 && bound is null; attempt++)
        {
            var port = Random.Shared.Next(49152, 65535);
            var candidate = new HttpListener();
            try
            {
                candidate.Prefixes.Add($"http://127.0.0.1:{port}/");
                candidate.Start();
                bound = candidate;
                origin = $"ws://127.0.0.1:{port}";
                Port = port;
            }
            catch (HttpListenerException)
            {
                candidate.Close();
            }
        }

        _listener = bound ?? throw new InvalidOperationException("ButtplugToyServer: no loopback port available");
        ServerUrl = origin!;
        LoopbackListenerRegistry.Register(nameof(ButtplugToyServer), Port, ServerUrl);
        _serve = Task.Run(ServeLoop);
    }

    public string ServerUrl { get; }

    public int Port { get; }

    /// <summary>How many vibrate features the single device advertises. Zero models a device that
    /// is present but has nothing that vibrates.</summary>
    public int VibrateFeatures { get; }

    /// <summary>Whether the server reports any device at all.</summary>
    public bool WithDevice { get; }

    /// <summary>The device key the sink will address, which is the device index as a string.</summary>
    public const string DeviceKey = "0";

    public IReadOnlyList<Recorded> Commands
    {
        get { lock (_commands) { return _commands.ToArray(); } }
    }

    /// <summary>Every OutputCmd value the server received, in order.</summary>
    public IReadOnlyList<int> VibrateValues
    {
        get { lock (_commands) { return [.. _commands.Where(c => c.Name == "OutputCmd").Select(c => c.Value)]; } }
    }

    /// <summary>How many StopCmd messages arrived.</summary>
    public int StopCommands
    {
        get { lock (_commands) { return _commands.Count(c => c.Name == "StopCmd"); } }
    }

    private string DeviceListJson()
    {
        if (!WithDevice)
        {
            return "{}";
        }

        var features = string.Join(",", Enumerable.Range(0, VibrateFeatures).Select(i =>
            $"\"{i}\":{{\"FeatureDescription\":\"vibe{i}\",\"FeatureIndex\":{i},"
            + "\"Output\":{\"Vibrate\":{\"Value\":[0,20]}}}"));

        // A device with zero vibrate features still exists — it just advertises nothing to drive.
        return "{\"0\":{\"DeviceName\":\"Test Vibe\",\"DeviceIndex\":0,\"DeviceDisplayName\":\"Test Vibe\","
            + "\"DeviceMessageTimingGap\":0,\"DeviceFeatures\":{" + features + "}}}";
    }

    private async Task ServeLoop()
    {
        HttpListenerContext context;
        try { context = await _listener.GetContextAsync(); }
        catch { return; }

        WebSocket socket;
        try { socket = (await context.AcceptWebSocketAsync(null)).WebSocket; }
        catch { return; }

        var buffer = new byte[32 * 1024];
        while (socket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
        {
            WebSocketReceiveResult received;
            try { received = await socket.ReceiveAsync(buffer, _cts.Token); }
            catch { return; }

            if (received.MessageType == WebSocketMessageType.Close)
            {
                // The close is ECHOED, because a close handshake has two halves and a peer that
                // only ever reads the first one is not a server this port can prove anything
                // against: the client's own disconnect waits for this frame. Returning here without
                // it is what made every fact in this file HANG rather than fail.
                try
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (WebSocketException)
                {
                    // The peer went away mid-close. Nothing left to answer.
                }

                return;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, received.Count);
            string payload;
            try { payload = Answer(text); }
            catch { return; }

            try { await socket.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, _cts.Token); }
            catch { return; }
        }
    }

    private string Answer(string request)
    {
        using var document = JsonDocument.Parse(request);
        var replies = new List<string>();

        foreach (var message in document.RootElement.EnumerateArray())
        {
            var envelope = message.EnumerateObject().First();
            var body = envelope.Value;
            var id = body.TryGetProperty("Id", out var idElement) ? idElement.GetInt32() : 0;

            switch (envelope.Name)
            {
                case "RequestServerInfo":
                    replies.Add($"{{\"ServerInfo\":{{\"Id\":{id},\"ServerName\":\"ccp-test\",\"MaxPingTime\":0,"
                        + "\"ProtocolVersionMajor\":4,\"ProtocolVersionMinor\":0}}");
                    break;

                case "RequestDeviceList":
                    replies.Add($"{{\"DeviceList\":{{\"Id\":{id},\"Devices\":{DeviceListJson()}}}}}");
                    break;

                case "OutputCmd":
                {
                    var value = body.GetProperty("Command").GetProperty("Vibrate").GetProperty("Value").GetInt32();
                    Record(new Recorded("OutputCmd", value));
                    replies.Add($"{{\"Ok\":{{\"Id\":{id}}}}}");
                    break;
                }

                case "StopCmd":
                    Record(new Recorded("StopCmd", 0));
                    replies.Add($"{{\"Ok\":{{\"Id\":{id}}}}}");
                    break;

                default:
                    replies.Add($"{{\"Ok\":{{\"Id\":{id}}}}}");
                    break;
            }
        }

        return "[" + string.Join(",", replies) + "]";
    }

    private void Record(Recorded entry)
    {
        lock (_commands)
        {
            _commands.Add(entry);
        }
    }

    /// <summary>
    /// A loopback port that is HELD, not released — so a refusal fact refuses against a port nothing
    /// can be listening on, and nothing else can take it mid-fact.
    ///
    /// <para><b>This replaces a reserve-then-release that was a real race and was caught being one.</b>
    /// Binding a port, closing it, and then connecting leaves a window in which ANOTHER process can take
    /// that exact port — and two concurrent runs of this suite on one machine do exactly that. Observed:
    /// the no-server refusal fact went Degraded instead of Unavailable, because by the time it connected,
    /// somebody else was listening on the port it had just let go.</para>
    ///
    /// <para>The socket is bound with <c>ExclusiveAddressUse</c> and <b>never listened on</b>. A TCP
    /// connect to a bound-but-not-listening port is REFUSED, which is the condition the fact wants, and
    /// holding the bind is what makes it stay true for the whole fact rather than for an instant.</para>
    /// </summary>
    public sealed class HeldPort : IDisposable
    {
        private readonly Socket _socket;

        internal HeldPort(Socket socket, int port)
        {
            _socket = socket;
            Port = port;
        }

        public int Port { get; }

        public void Dispose() => _socket.Dispose();
    }

    /// <summary>Bind a loopback port and HOLD it, without ever listening on it.</summary>
    public static HeldPort HoldFreePort()
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                socket.ExclusiveAddressUse = true;
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                var port = ((IPEndPoint)socket.LocalEndPoint!).Port;
                return new HeldPort(socket, port);
            }
            catch (SocketException)
            {
                socket.Dispose();
            }
        }

        throw new InvalidOperationException("no loopback port could be bound and held");
    }

    /// <summary>The held port as a ws url, for a fact that wants one.</summary>
    public static string UrlFor(HeldPort held) => $"ws://127.0.0.1:{held.Port}";

    public void Dispose()
    {
        _cts.Cancel();

        // Unregistered only after a SUCCESSFUL close — the registry's rule, because a leak report
        // that lied would fail the whole assembly loud on a false fact.
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
            LoopbackListenerRegistry.Unregister(Port);
        }

        _cts.Dispose();
    }

    internal sealed record Recorded(string Name, int Value);
}
