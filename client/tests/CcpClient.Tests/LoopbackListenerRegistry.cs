using System.Collections.Concurrent;
using CcpClient.Desktop.Features.Dtrh;

// T-15 leaked-listener self-check, generalized (SP-059): after ALL collections finish, any
// undisposed loopback listener fails the run LOUD.
[assembly: Xunit.AssemblyFixture(typeof(CcpClient.Tests.LoopbackListenerLeakSelfCheck))]

namespace CcpClient.Tests;

/// <summary>
/// Live loopback-listener registry (T-15 self-check, generalized by SP-059 from AiProviderLab-
/// only to EVERY loopback <c>HttpListener</c> the suite binds: the AI lab, §4 LoopbackServer
/// fixtures, and private harnesses). A leaked entry at assembly teardown = a leaked listener
/// holding a loopback port; the assembly fixture fails the run LOUD naming owner + port.
/// Register after the listener is bound; unregister only after a SUCCESSFUL dispose — a failed
/// dispose must stay registered (a false leak report would fail the suite loud on a lie).
/// </summary>
public static class LoopbackListenerRegistry
{
    private static readonly ConcurrentDictionary<int, string> Live = new();

    public static void Register(string owner, int port, string prefix) =>
        Live[port] = $"{owner}: {prefix}";

    public static void Unregister(int port) => Live.TryRemove(port, out _);

    /// <summary>Registers BOTH origins (page + media) of a started §4 LoopbackServer.</summary>
    public static void RegisterLoopbackServer(string owner, LoopbackServer server)
    {
        Register(owner, server.PagePort, server.PageOrigin);
        Register(owner, server.MediaPort, server.MediaOrigin);
    }

    public static void UnregisterLoopbackServer(LoopbackServer server)
    {
        Unregister(server.PagePort);
        Unregister(server.MediaPort);
    }

    /// <summary>Called by the assembly fixture at test-assembly teardown. Throws LOUD naming
    /// every leaked owner/port. Never called from any Dispose — a Dispose throw during
    /// test-failure unwinding would mask the real failure.</summary>
    public static void AssertNoLeakedListeners()
    {
        if (!Live.IsEmpty)
        {
            throw new InvalidOperationException(
                "leaked loopback listener(s) holding port(s): " +
                string.Join(", ", Live.Select(kv => $"{kv.Value} (port {kv.Key})")));
        }
    }
}

/// <summary>Assembly-teardown self-check (T-15, generalized SP-059): runs AFTER all
/// collections (no parallel-lab race), adds zero test cases. Any listener still registered
/// leaked.</summary>
public sealed class LoopbackListenerLeakSelfCheck : IDisposable
{
    public void Dispose() => LoopbackListenerRegistry.AssertNoLeakedListeners();
}
