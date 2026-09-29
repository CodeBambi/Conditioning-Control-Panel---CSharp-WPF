using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CCP.Tests.Shared;

// Loopback stand-in for the GitHub release assets, shared by the Core content tests and the
// Avalonia Mod Manager tests. Linked into each project; nothing here may reach a real host.

/// <summary>Counts requests and refuses (and records) any that is not for a loopback host.</summary>
internal sealed class LoopbackOnlyHandler : DelegatingHandler
{
    public int Count;
    public readonly ConcurrentQueue<Uri> Violations = new();
    public LoopbackOnlyHandler() : base(new SocketsHttpHandler()) { }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri is not { IsLoopback: true })
        {
            Violations.Enqueue(request.RequestUri!);
            throw new InvalidOperationException("non-loopback request: " + request.RequestUri);
        }
        Interlocked.Increment(ref Count);
        return base.SendAsync(request, ct);
    }
}

/// <summary>HttpListener on 127.0.0.1 standing in for the GitHub release assets.</summary>
internal sealed class FakeServer : IDisposable
{
    private readonly HttpListener _listener = new();
    public readonly string Prefix;
    public readonly ConcurrentQueue<(string Path, string? Range)> Requests = new();
    public Func<HttpListenerContext, Task> Handle = c => Send(c, Array.Empty<byte>(), 404);

    public FakeServer()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Prefix = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(Prefix);
        _listener.Start();
        _ = Task.Run(Loop);
    }

    private async Task Loop()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext c;
            try { c = await _listener.GetContextAsync(); } catch { return; }
            Requests.Enqueue((c.Request.Url!.AbsolutePath, c.Request.Headers["Range"]));
            try { await Handle(c); } catch { try { c.Response.Abort(); } catch { } }
        }
    }

    public static async Task Send(HttpListenerContext c, byte[] body, int status = 200)
    {
        c.Response.StatusCode = status;
        c.Response.ContentLength64 = body.Length;
        await c.Response.OutputStream.WriteAsync(body);
        c.Response.Close();
    }

    public int PackGets => Requests.Count(r => r.Path.EndsWith(".zip", StringComparison.Ordinal));

    public void Dispose() { try { _listener.Stop(); _listener.Close(); } catch { } }
}
