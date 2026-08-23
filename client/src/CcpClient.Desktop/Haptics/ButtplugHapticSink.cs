using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Haptics;

/// <summary>
/// The Buttplug route: a client of <b>Intiface Central</b> (GUI) or <b>Intiface Engine</b> (CLI), a
/// separate server the user installs and runs. Like the Lovense route this talks to another program
/// on loopback rather than to hardware, so admitting it opens no driver or kernel boundary.
///
/// <para><b>Quantization is NOT ours here, and that is the opposite of the Lovense route.</b>
/// Buttplug maps a percentage onto each feature's own step range. Measured rather than assumed:
/// driving <c>Percent(0.75)</c> against a feature advertising <c>Value:[0,20]</c> put
/// <c>{"Vibrate":{"Value":15}}</c> on the wire. So this sink passes 0..1 through untouched, which is
/// what <see cref="HapticLevel"/>'s own contract says a provider does. Lovense quantizes inside its
/// sink only because its HTTP API has no client library to do it.</para>
///
/// <para><b>Outputs LATCH.</b> A level holds until the next command, so unlike the Lovense LAN mode
/// there is no expiry, no re-assertion and no keep-alive question at all.</para>
///
/// <para><b>The library is reached through <see cref="IButtplugSession"/></b>, a seam thin enough to
/// have no behaviour of its own. What this port owns is which calls happen, in what order, and what
/// a failure becomes; the protocol itself is the library's and is proved end to end against a server
/// speaking the captured wire.</para>
/// </summary>
public sealed class ButtplugHapticSink : IHapticSink
{
    /// <summary>Intiface's loopback default (<c>Services/Haptics/ButtplugProvider.cs:27</c>).</summary>
    public const string DefaultServerUrl = "ws://127.0.0.1:12345";

    /// <summary>The name announced to the server. Visible in Intiface's own UI, so it says what it
    /// is rather than carrying a library default.</summary>
    public const string ClientName = "Conditioning Control Panel";

    private readonly string _serverUrl;
    private readonly Action<string> _log;
    private readonly Func<string, IButtplugSession> _sessionFactory;
    private readonly object _gate = new();

    private IButtplugSession? _session;
    private CapabilityState? _lastOutcome;
    private bool _disposed;

    public ButtplugHapticSink(
        Action<string> log, string? serverUrl = null, Func<string, IButtplugSession>? sessionFactory = null)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _serverUrl = serverUrl ?? DefaultServerUrl;
        _sessionFactory = sessionFactory ?? (url => new RealButtplugSession(url, _log));
    }

    public HapticProviderRoute Route => HapticProviderRoute.Buttplug;

    public CapabilityState? LastOutcome
    {
        get { lock (_gate) { return _lastOutcome; } }
    }

    public async Task<HapticServerObservation> ObserveAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return HapticServerObservation.SinkDisposed;
        }

        var session = Session();
        if (!session.Connected)
        {
            // ASKED and admitted, nobody answered. Not an error state: a user who does not run
            // Intiface is the ordinary case, not a fault.
            return new HapticServerObservation(true, Route, true, false, []);
        }

        var keys = await session.DeviceKeysAsync(cancellationToken).ConfigureAwait(false);
        return new HapticServerObservation(true, Route, true, true, keys);
    }

    public async Task<CapabilityState> ConnectAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return Remember(Disposed("no connection was attempted"));
        }

        var session = Session();
        try
        {
            await session.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log($"haptics: no buttplug server answered on {_serverUrl} ({ex.GetType().Name})");
            return Remember(new CapabilityState.Unavailable(new CapabilityReason(
                HapticReasonCodes.HapticServerUnreachable,
                $"no Buttplug server answered on {_serverUrl} ({ex.GetType().Name}). This route is a client of "
                + "Intiface Central or Intiface Engine, which the user runs separately — nothing was attempted "
                + "against hardware, and this is not \"no device found\"")));
        }

        var keys = await session.DeviceKeysAsync(cancellationToken).ConfigureAwait(false);
        if (keys.Count == 0)
        {
            return Remember(new CapabilityState.Degraded(
                "the client and the server connection both hold: commands would be delivered the moment a device is "
                + "paired in Intiface",
                new CapabilityReason(
                    HapticReasonCodes.HapticNoDevice,
                    $"the Buttplug server on {_serverUrl} answered and reports no connected device")));
        }

        return Remember(new CapabilityState.Available(
            $"the Buttplug server on {_serverUrl} answered with {keys.Count} device(s). Confirms the SERVER, never "
            + "that a motor moved — that is a device gate a human reports"));
    }

    public async Task<CapabilityState> SetOutputsAsync(
        string deviceKey, IReadOnlyList<HapticOutput> outputs, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceKey);
        ArgumentNullException.ThrowIfNull(outputs);
        if (outputs.Count == 0)
        {
            throw new ArgumentException("an empty output list is a caller error, never a silent stop", nameof(outputs));
        }

        if (_disposed)
        {
            return Remember(Disposed($"the '{deviceKey}' command was not attempted"));
        }

        var session = Session();
        if (!session.Connected)
        {
            return Remember(new CapabilityState.Unavailable(new CapabilityReason(
                HapticReasonCodes.HapticServerUnreachable,
                $"no Buttplug server is connected, so the '{deviceKey}' command was not attempted")));
        }

        // The strongest actuator wins, and the level is passed through UNQUANTIZED — the library maps
        // a percentage onto each feature's own step range, so rounding here would quantize twice.
        var level = 0.0;
        for (var i = 0; i < outputs.Count; i++)
        {
            level = Math.Max(level, outputs[i].Level.Value);
        }

        try
        {
            var driven = await session.SetVibrateAsync(deviceKey, level, cancellationToken).ConfigureAwait(false);
            if (driven == 0)
            {
                // A present device with nothing that vibrates is not a refusal and not a success.
                return Remember(new CapabilityState.Degraded(
                    "the device is present and addressable",
                    new CapabilityReason(
                        HapticReasonCodes.HapticNoDevice,
                        $"'{deviceKey}' advertises no feature with a Vibrate output, so the level was not sent")));
            }

            return Remember(new CapabilityState.Available(
                $"the Buttplug server accepted level {level:0.###} for '{deviceKey}' across {driven} vibrate "
                + "feature(s); the output latches until the next command"));
        }
        catch (KeyNotFoundException)
        {
            return Remember(new CapabilityState.Unavailable(new CapabilityReason(
                HapticReasonCodes.HapticDeviceUnknown,
                $"the Buttplug server does not know a device keyed '{deviceKey}'")));
        }
        catch (Exception ex)
        {
            return Remember(new CapabilityState.Unavailable(new CapabilityReason(
                HapticReasonCodes.HapticCommandRefused,
                $"the Buttplug server refused a level {level:0.###} command for '{deviceKey}' "
                + $"({ex.GetType().Name})")));
        }
    }

    /// <summary>
    /// Stop every device the SERVER knows, not merely the ones this sink has driven.
    ///
    /// <para>Deliberately wider than the Lovense route. There this sink is the only thing that can
    /// have started a toy, so its own record is the complete list. Intiface is shared — another
    /// application can be driving the same server — and a teardown that left someone else's command
    /// running because this process never sent it would be exactly the outcome an all-stop exists to
    /// prevent.</para>
    /// </summary>
    public async Task<CapabilityState> StopAllAsync()
    {
        var session = Session();
        if (!session.Connected)
        {
            return Remember(new CapabilityState.Degraded(
                "the stop path itself is live and would have reached every device the server knows",
                new CapabilityReason(
                    HapticReasonCodes.HapticNoDevice,
                    "no Buttplug server is connected, so there was nothing to stop. Reported rather than claimed as a "
                    + "success: \"nothing was running\" and \"everything was stopped\" are different facts")));
        }

        try
        {
            var stopped = await session.StopAllAsync().ConfigureAwait(false);
            return Remember(new CapabilityState.Available(
                $"a stop was accepted for every device the server knows ({stopped})"));
        }
        catch (Exception ex)
        {
            return Remember(new CapabilityState.Unavailable(new CapabilityReason(
                HapticReasonCodes.HapticStopIncomplete,
                $"a stop did not reach every device ({ex.GetType().Name}). A device may still be running")));
        }
    }

    private static CapabilityState Disposed(string what) =>
        new CapabilityState.Unavailable(new CapabilityReason(
            HapticReasonCodes.HapticSinkDisposed, $"this Buttplug sink was disposed, so {what}"));

    private IButtplugSession Session()
    {
        lock (_gate)
        {
            return _session ??= _sessionFactory(_serverUrl);
        }
    }

    private CapabilityState Remember(CapabilityState state)
    {
        lock (_gate)
        {
            _lastOutcome = state;
        }

        return state;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        IButtplugSession? session;
        lock (_gate)
        {
            session = _session;
            _session = null;
        }

        try { session?.Dispose(); }
        catch (Exception ex) { _log($"haptics: buttplug session dispose reported {ex.GetType().Name}"); }
    }
}
