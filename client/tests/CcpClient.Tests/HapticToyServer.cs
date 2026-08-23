using System.Net.Sockets;
using System.Net;
using System.Text;

namespace CcpClient.Tests;

/// <summary>
/// A real Lovense-shaped HTTP server on loopback, recording what actually arrived.
///
/// <para>Shared rather than private to one test class, because two different questions need the same
/// instrument: whether the sink builds the request another program can parse, and whether a teardown
/// really puts a zero on the wire. Both are questions about what a SERVER received, and a fake
/// <c>HttpClient</c> that returns whatever it was handed cannot answer either.</para>
///
/// <para>It registers with <see cref="LoopbackListenerRegistry"/> and unregisters only after a
/// SUCCESSFUL close, which is that registry's rule: a failed dispose must stay registered, because a
/// leak report that lied would fail the assembly loud on a false fact.</para>
/// </summary>
internal sealed class HapticToyServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _serve;
    private readonly List<Recorded> _requests = [];

    public HapticToyServer()
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

        _listener = bound ?? throw new InvalidOperationException("HapticToyServer: no loopback port available");
        BaseUrl = prefix!;
        LoopbackListenerRegistry.Register(nameof(HapticToyServer), new Uri(BaseUrl).Port, BaseUrl);
        _serve = Task.Run(ServeLoop);
    }

    public string BaseUrl { get; }

    /// <summary>The toys this server reports. Settable so a fact can drive "answered with none".</summary>
    public IReadOnlyList<string> ToyKeys { get; set; } = ["toy-a"];

    /// <summary>The first toy, for callers that just need something addressable.</summary>
    public string ToyKey => ToyKeys.Count > 0 ? ToyKeys[0] : "toy-a";

    /// <summary>Every non-enumeration request, in arrival order.</summary>
    public IReadOnlyList<Recorded> Requests
    {
        get { lock (_requests) { return _requests.ToArray(); } }
    }

    /// <summary>Each command as one string — body for a POST, query for a GET — so a fact can ask
    /// "did a Vibrate:0 arrive" without caring which mode produced it.</summary>
    public IReadOnlyList<string> Commands
    {
        get { lock (_requests) { return [.. _requests.Select(r => r.Body + r.Query)]; } }
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

    /// <summary>The held port as a http url, for a fact that wants one.</summary>
    public static string UrlFor(HeldPort held) => $"http://127.0.0.1:{held.Port}";

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
            var isEnumeration = path.EndsWith("/GetToys", StringComparison.OrdinalIgnoreCase);
            if (!isEnumeration)
            {
                lock (_requests)
                {
                    _requests.Add(new Recorded(request.HttpMethod, path, request.Url?.Query ?? string.Empty, body));
                }
            }

            var payload = isEnumeration
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

        // Unregistered ONLY after the listener really closed — the registry's rule, because a leak
        // report that lied would fail the whole assembly loud on a false fact.
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

        // No join on the serve task, and therefore no wall-clock wait to pin. Closing the listener is
        // what ends the loop: the pending GetContextAsync faults and the loop returns.
        _cts.Dispose();
    }

    internal sealed record Recorded(string Method, string Path, string Query, string Body);
}
