using Buttplug.Client;
using Buttplug.Core.Messages;

namespace CcpClient.Desktop.Haptics;

/// <summary>
/// The real session: a <see cref="ButtplugClient"/> over a WebSocket to Intiface.
///
/// <para><b>Pure delegation, on purpose.</b> Every method here is the smallest possible translation
/// between this port's vocabulary and the library's. There is no policy, no retry, no fallback and
/// no interpretation — all of that lives in <see cref="ButtplugHapticSink"/>, where the sink's own
/// facts can see it. A seam that grows logic becomes a place bugs hide from both sides.</para>
///
/// <para><b>The one exception is threading, and it is not a preference.</b> This library hands
/// control of its own reader thread to whoever awaits it, so the two rules below —
/// <see cref="DetachedAsync"/> and the pool-thread connector — are the seam's real work. Both are
/// stated where they are applied, with the decompiled evidence, because either one read as a style
/// choice would be deleted by the next person to tidy this file.</para>
///
/// <para><b>No fixed scan wait.</b> The shipping provider sleeps two seconds after
/// <c>StartScanningAsync</c> hoping devices appear (<c>ButtplugProvider.cs:89-95</c>). Devices
/// already paired arrive in the server's opening device list, and later ones arrive on the client's
/// own device-added notification, so a timer would make every connect two seconds slower and still
/// be a guess about another program's discovery latency.</para>
/// </summary>
internal sealed class RealButtplugSession : IButtplugSession
{
    /// <summary>
    /// The teardown budget for the library's disconnect, after which the socket is abandoned to the
    /// OS rather than held onto.
    ///
    /// <para>It exists because nothing above this bounds it: <c>ApplicationHost.ShutdownAsync</c>
    /// awaits each participant's <c>StopAsync</c> with no budget of its own
    /// (<c>Lifecycle/ApplicationHost.cs:162</c>), and that stop is where the sink is released. An
    /// Intiface that stopped answering mid-close would otherwise hang the process on exit — and by
    /// this point the all-stop has already been delivered, so what is being waited on is hygiene,
    /// not the outcome a user feels.</para>
    ///
    /// <para><b>It is a safety net and must not be load-bearing.</b> A disconnect that routinely
    /// spends this budget is a defect somewhere else, not a slow peer: that is exactly how the
    /// reader-thread deadlock below first showed itself, as every fact in this route's suite taking
    /// almost precisely this long.</para>
    /// </summary>
    internal static readonly TimeSpan DisconnectBudget = TimeSpan.FromSeconds(2);

    private readonly string _serverUrl;
    private readonly Action<string> _log;
    private readonly ButtplugClient _client = new(ButtplugHapticSink.ClientName);
    private bool _disposed;

    internal RealButtplugSession(string serverUrl, Action<string> log)
    {
        _serverUrl = serverUrl;
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public bool Connected => !_disposed && _client.Connected;

    /// <summary>
    /// Awaits a library call and then LEAVES THE LIBRARY'S THREAD before the caller resumes.
    ///
    /// <para><b>Every await of this library goes through here, and that is the whole point of the
    /// seam.</b> A Buttplug reply is delivered by completing a promise from inside the connector's
    /// read loop, and the completion source is created WITHOUT
    /// <c>TaskCreationOptions.RunContinuationsAsynchronously</c>
    /// (<c>ButtplugConnectorMessageSorter.CheckMessage</c> → <c>TaskCompletionSource.SetResult</c>,
    /// Buttplug 5.0.1, lib/netstandard2.1). So everything awaiting a Buttplug call resumes INLINE on
    /// the connector's single reader thread — not just the next line, but the caller's caller, and
    /// its caller, all the way up.</para>
    ///
    /// <para><b>What that costs if it is left alone.</b> The port's own code then runs on the
    /// library's reader, which is the one thread that has to get back to <c>ReceiveAsync</c> for the
    /// connection to keep working. Anything slow there stalls the connection; anything BLOCKING
    /// there deadlocks it outright, because the connector's disconnect awaits that same read task
    /// (<c>ButtplugWebsocketConnector.DisconnectAsync</c> :75-78). That is not theoretical: it is
    /// how this was found. A captured stack showed one thread running the websocket receive, the
    /// sorter, the send, <c>StartScanningAsync</c>, this class, the sink, and then the test body
    /// itself — which released the sink, which waited for the disconnect, which was waiting for the
    /// read loop that was busy running the test body.</para>
    ///
    /// <para><b><see cref="Task.Yield"/> is what breaks it.</b> It re-schedules the remainder onto
    /// the thread pool, so this method completes on a pool thread and the caller's inline
    /// continuation lands there instead. The reader is handed straight back to the library. The cost
    /// is one thread hop per Buttplug call, against commands a person triggers a few times a second
    /// at most.</para>
    /// </summary>
    private static async Task DetachedAsync(Task libraryCall)
    {
        await libraryCall.ConfigureAwait(false);
        await Task.Yield();
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_client.Connected)
        {
            return;
        }

        // The connector is CONSTRUCTED ON A POOL THREAD, and that is load-bearing rather than
        // stylistic. Its owning dispatcher is captured in a FIELD INITIALIZER, verbatim from the
        // shipped binary (Buttplug 5.0.1, lib/netstandard2.1, ButtplugWebsocketConnector:15):
        //
        //     private readonly SynchronizationContext _owningDispatcher =
        //         SynchronizationContext.Current ?? new SynchronizationContext();
        //
        // — so whichever thread runs `new ButtplugWebsocketConnector(...)` decides it for the life
        // of the connection. At teardown the read loop ends in a finally that posts BLOCKING to that
        // dispatcher (`_owningDispatcher.Send(delegate { Dispose(); }, null)`, :121-127). Capture
        // the Avalonia UI thread's context here and shutdown deadlocks against itself: the UI thread
        // waits for the read loop, the read loop waits for the UI thread, and the app never exits.
        //
        // A pool thread has no ambient context, so the initializer falls through to
        // `new SynchronizationContext()`, whose Send runs the delegate inline on the calling thread.
        // There is then no cross-thread handoff on the teardown path at all.
        //
        // This is a SECOND, independent deadlock from the reader-thread one DetachedAsync fixes.
        // Both are real; neither subsumes the other.
        var connector = await Task.Run(() => new ButtplugWebsocketConnector(new Uri(_serverUrl)), cancellationToken)
            .ConfigureAwait(false);

        await DetachedAsync(_client.ConnectAsync(connector, cancellationToken)).ConfigureAwait(false);

        // Scanning is STARTED and not awaited to completion: it is how a server surfaces devices
        // paired after connect, and nothing here should block on another program finding hardware.
        await DetachedAsync(_client.StartScanningAsync(cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>The device index, as a string. Stable for the life of a server session and the only
    /// identifier the wire actually addresses (<c>OutputCmd.DeviceIndex</c>).</summary>
    public Task<IReadOnlyList<string>> DeviceKeysAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(
            [.. _client.Devices.Select(d => d.Index.ToString(System.Globalization.CultureInfo.InvariantCulture))]);

    public async Task<int> SetVibrateAsync(string deviceKey, double level, CancellationToken cancellationToken)
    {
        var device = Find(deviceKey);
        var command = DeviceOutput.Vibrate.Percent(level);
        var driven = 0;
        foreach (var feature in device.GetFeaturesWithOutput(OutputType.Vibrate))
        {
            await DetachedAsync(feature.RunOutputAsync(command)).ConfigureAwait(false);
            driven++;
        }

        return driven;
    }

    public async Task<int> StopAllAsync()
    {
        var stopped = 0;
        foreach (var device in _client.Devices)
        {
            await DetachedAsync(device.StopAsync()).ConfigureAwait(false);
            stopped++;
        }

        return stopped;
    }

    private ButtplugClientDevice Find(string deviceKey)
    {
        foreach (var device in _client.Devices)
        {
            if (string.Equals(
                    device.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    deviceKey,
                    StringComparison.Ordinal))
            {
                return device;
            }
        }

        throw new KeyNotFoundException($"no Buttplug device is keyed '{deviceKey}'");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // NOT _client.Dispose(). The library's synchronous dispose is, verbatim from the shipped
        // binary (Buttplug 5.0.1, lib/netstandard2.1, ButtplugClient:249-253):
        //
        //     protected virtual void Dispose(bool disposing)
        //     {
        //         _pingTimer?.Dispose();
        //         DisconnectAsync().GetAwaiter().GetResult();
        //     }
        //
        // — a sync-over-async block. DisposeAsync is the same two steps with ConfigureAwait(false)
        // (:261-266), so it is the supported way to do this, and Task.Run keeps the block off any
        // caller's SynchronizationContext.
        //
        // The budget is a safety net for a peer that stops answering mid-close, NOT the mechanism
        // that makes this return. What makes it return is DetachedAsync: without it this call is
        // made from the library's own reader thread, which is the thread the disconnect is waiting
        // for, and no budget would be spent on anything but a deadlock.
        try
        {
            if (!Task.Run(() => _client.DisposeAsync().AsTask()).Wait(DisconnectBudget))
            {
                _log($"haptics: the buttplug disconnect outlasted its {DisconnectBudget.TotalSeconds:0.#}s teardown "
                    + "budget; the socket is left to the OS rather than holding up exit");
            }
        }
        catch (Exception ex)
        {
            _log($"haptics: the buttplug disconnect reported {ex.GetType().Name}");
        }
    }
}
