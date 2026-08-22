using System.Net.Http;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Haptics;

/// <summary>
/// The Lovense route: an HTTP client of <b>Lovense Connect</b> or <b>Lovense Remote</b>, a separate
/// server the user installs and runs. Nothing here is a driver and nothing here talks to hardware —
/// it talks to another program on loopback, which is the whole reason admitting it is a decision
/// about a dependency rather than about a device.
///
/// <para><b>No haptics package enters the graph for this route.</b> The wire is
/// <c>System.Net.Http</c> and <c>System.Text.Json</c>. The shipping provider's only non-BCL import
/// is its logger (<c>Services/Haptics/LovenseProvider.cs:8</c>), and this port logs through its own
/// sink, so the dependency cost of this file is zero.</para>
///
/// <para><b>Quantization is the provider's, and it is upstream's exactly.</b> The seam carries
/// 0..1; Lovense takes 0..20 and upstream deliberately skips levels 1-2 as imperceptible
/// (<c>LovenseProvider.cs:195-204</c>): at or below 0.05 the answer is a true zero, and above it the
/// range maps onto 3..20. A floor is never substituted for a zero — <i>"slider at 0 = silence, not a
/// floor buzz"</i>.</para>
///
/// <para><b>Two modes, two expiry rules, and that is why this file cares about holding.</b> LAN mode
/// (Lovense Remote) POSTs a Function command carrying <c>timeSec</c>, which the SERVER expires, and
/// its floor is a whole second (<c>:229-236</c>). Connect mode GETs a Vibrate command that expires
/// nothing at all (<c>:242-247</c>). This port's seam carries no duration — an output holds until the
/// next command — so LAN needs its command re-asserted before it lapses. It does NOT need a timer
/// here: <see cref="HapticLimb"/> re-evaluates every live envelope on a 100 ms grid
/// (<c>HapticEnvelope.TickMs</c>) and sends unconditionally at each wake, so a one-second command is
/// refreshed roughly ten times over before it could expire. Delivery is the provider's job by
/// contract, and on this route the caller's own cadence discharges it while anything is playing.</para>
///
/// <para><b>The certificate exception is loopback-only</b>, matching upstream (<c>:41-52</c>): a
/// self-signed certificate is accepted for <c>127.0.0.1</c> and <c>localhost</c> and nowhere else,
/// so admitting this route cannot become a general TLS bypass.</para>
/// </summary>
public sealed class LovenseHapticSink : IHapticSink
{
    /// <summary>Loopback default (<c>LovenseProvider.cs:21</c>). Overridable because upstream
    /// exposes <c>SetUrl</c>; a non-loopback host validates its certificate normally.</summary>
    public const string DefaultBaseUrl = "http://127.0.0.1:20010";

    /// <summary>Upstream's client timeout (<c>:52</c>).</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The whole-second floor the LAN API imposes on its own expiry (<c>:232-233</c>).
    /// Sent as the hold window; the caller's 100 ms cadence refreshes it long before it lapses.</summary>
    public const int LanHoldSeconds = 1;

    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly string _baseUrl;
    private readonly LovenseMode _mode;
    private readonly Action<string> _log;
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _lastSentLevel = new(StringComparer.Ordinal);

    private CapabilityState? _lastOutcome;
    private bool _disposed;

    /// <summary>Which server is answering, and therefore which expiry rule applies.</summary>
    public enum LovenseMode
    {
        /// <summary>Lovense Remote. POSTs a Function command the SERVER expires at a whole-second
        /// floor, so the level must be re-asserted to keep holding.</summary>
        Lan,

        /// <summary>Lovense Connect. GETs a Vibrate command that expires nothing; the device holds
        /// until the next command.</summary>
        Connect,
    }

    public LovenseHapticSink(
        Action<string> log,
        string? baseUrl = null,
        LovenseMode mode = LovenseMode.Lan,
        HttpClient? client = null)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _baseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
        _mode = mode;
        _ownsClient = client is null;
        _client = client ?? BuildLoopbackClient();
    }

    public HapticProviderRoute Route => HapticProviderRoute.Lovense;

    public CapabilityState? LastOutcome
    {
        get { lock (_gate) { return _lastOutcome; } }
    }

    /// <summary>
    /// Upstream's loopback-only certificate rule, verbatim in effect (<c>:41-52</c>). A clean chain
    /// always passes; a broken one passes ONLY for loopback, because that is where a Lovense server
    /// presents a self-signed certificate for its own local API.
    /// </summary>
    private static HttpClient BuildLoopbackClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, _, _, errors) =>
            {
                if (errors == SslPolicyErrors.None)
                {
                    return true;
                }

                var host = message.RequestUri?.Host;
                return string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
                    || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);
            },
        };

        return new HttpClient(handler) { Timeout = RequestTimeout };
    }

    /// <summary>
    /// Upstream's 0..1 to 0..20 map (<c>:195-204</c>). Levels 1 and 2 are unreachable BY DESIGN —
    /// they are imperceptible on the hardware, so spending the bottom of the range on them would
    /// make a quiet setting read as a broken one.
    /// </summary>
    public static int QuantizeLevel(HapticLevel level)
    {
        var intensity = level.Value;
        if (intensity <= 0.05)
        {
            return 0;
        }

        var mapped = 3 + (int)((intensity - 0.05) / 0.95 * 17);
        return Math.Clamp(mapped, 0, 20);
    }

    /// <summary>
    /// The strongest actuator wins. This route addresses ONE toy per command and has no per-actuator
    /// channel — upstream sends a single <c>Vibrate:{i}</c> and holds a single toy id
    /// (<c>:22,:132,:233</c>) — so a multi-actuator request is reduced rather than silently
    /// truncated to the first entry. Max, not average: an envelope's crest is the thing a user set,
    /// and averaging it against a quiet motor would deliver neither level.
    /// </summary>
    public static int QuantizeOutputs(IReadOnlyList<HapticOutput> outputs)
    {
        var strongest = 0;
        for (var i = 0; i < outputs.Count; i++)
        {
            strongest = Math.Max(strongest, QuantizeLevel(outputs[i].Level));
        }

        return strongest;
    }

    public async Task<HapticServerObservation> ObserveAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return HapticServerObservation.NotAsked;
        }

        try
        {
            using var response = await _client
                .GetAsync($"{_baseUrl}/GetToys", cancellationToken)
                .ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // ANSWERED, with no devices. A server that refuses is a different fact from a server
                // that is not running, and collapsing them would report "no toy" for a stopped app.
                _log($"haptics: lovense server answered {(int)response.StatusCode} to a toy enumeration");
                return new HapticServerObservation(true, Route, true, true, []);
            }

            return new HapticServerObservation(true, Route, true, true, ParseToyKeys(body));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // NOT an error state: no Lovense server running is the ordinary case for a user who does
            // not own one. Asked, admitted, and nobody answered.
            _log($"haptics: no lovense server answered on {_baseUrl} ({ex.GetType().Name})");
            return new HapticServerObservation(true, Route, true, false, []);
        }
    }

    /// <summary>
    /// Toy ids out of the server's own answer. Shape-tolerant on purpose: the LAN and Connect
    /// builds do not agree on the envelope, and a parse that demanded one of them would report an
    /// empty device list against a server that is plainly answering.
    /// </summary>
    private static IReadOnlyList<string> ParseToyKeys(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data))
        {
            root = data;
        }

        // Some builds answer with the toy map as a JSON STRING rather than an object.
        if (root.ValueKind == JsonValueKind.String)
        {
            var inner = root.GetString();
            if (string.IsNullOrWhiteSpace(inner))
            {
                return [];
            }

            using var nested = JsonDocument.Parse(inner);
            return [.. nested.RootElement.EnumerateObject().Select(p => p.Name)];
        }

        return root.ValueKind == JsonValueKind.Object
            ? [.. root.EnumerateObject().Select(p => p.Name)]
            : [];
    }

    public async Task<CapabilityState> ConnectAsync(CancellationToken cancellationToken)
    {
        var observation = await ObserveAsync(cancellationToken).ConfigureAwait(false);

        if (!observation.ServerAnswered)
        {
            return Remember(new CapabilityState.Unavailable(new CapabilityReason(
                HapticReasonCodes.HapticServerUnreachable,
                $"no Lovense server answered on {_baseUrl}. This route is a client of Lovense Connect or Lovense "
                + "Remote, which the user runs separately — nothing was attempted against hardware, and this is "
                + "not \"no toy found\"")));
        }

        if (observation.DeviceCount == 0)
        {
            return Remember(new CapabilityState.Degraded(
                "the client and the server connection both hold: commands would be delivered the moment a toy "
                + "is paired",
                new CapabilityReason(
                    HapticReasonCodes.HapticNoDevice,
                    $"the Lovense server on {_baseUrl} answered and reports no connected toy, so there is "
                    + "nothing to drive")));
        }

        return Remember(new CapabilityState.Available(
            $"the Lovense server on {_baseUrl} answered a toy enumeration with {observation.DeviceCount} "
            + $"device(s) in {_mode} mode. Confirms the SERVER, never that a motor moved — that is a device "
            + "gate a human reports"));
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
            return Remember(new CapabilityState.Unavailable(new CapabilityReason(
                HapticReasonCodes.HapticSinkDisposed,
                $"this Lovense sink was disposed, so the '{deviceKey}' command was not attempted")));
        }

        var level = QuantizeOutputs(outputs);

        // THE THROTTLE, AND THE ONE PLACE IT DIVERGES FROM UPSTREAM ON PURPOSE. Upstream rate-limits
        // continuous sends to one per 200 ms and returns early REGARDLESS OF LEVEL
        // (LovenseProvider.cs:207-224), which means a command that lowers or ZEROES the toy can be
        // dropped because an unrelated one went out 190 ms earlier. On a path whose whole job is
        // making a device stop, that is not a rate limit, it is a lost stop. Here a repeat of the
        // SAME level is what gets suppressed, so identical commands still do not spam the server and
        // every CHANGE — above all a change to zero — always reaches the wire.
        lock (_gate)
        {
            if (_lastSentLevel.TryGetValue(deviceKey, out var previous) && previous == level)
            {
                return _lastOutcome ?? new CapabilityState.Available(
                    $"level {level}/20 already stands on '{deviceKey}'; an identical repeat was not re-sent");
            }
        }

        try
        {
            using var response = await SendLevelAsync(deviceKey, level, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Remember(new CapabilityState.Unavailable(new CapabilityReason(
                    HapticReasonCodes.HapticCommandRefused,
                    $"the Lovense server refused a level {level}/20 command for '{deviceKey}' with "
                    + $"{(int)response.StatusCode}")));
            }

            // Remembered only after the server accepted it. A level recorded before the wire would
            // suppress the retry of a command that never landed.
            lock (_gate)
            {
                _lastSentLevel[deviceKey] = level;
            }

            return Remember(new CapabilityState.Available(
                $"the Lovense server accepted level {level}/20 for '{deviceKey}' in {_mode} mode"
                + (_mode == LovenseMode.Lan
                    ? $", holding {LanHoldSeconds}s and re-asserted by the caller's cadence"
                    : ", holding until the next command")));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Remember(new CapabilityState.Unavailable(new CapabilityReason(
                HapticReasonCodes.HapticServerUnreachable,
                $"the Lovense server on {_baseUrl} did not answer a level {level}/20 command for "
                + $"'{deviceKey}' ({ex.GetType().Name})")));
        }
    }

    /// <summary>The two command shapes, each upstream's own (<c>:229-247</c>).</summary>
    private Task<HttpResponseMessage> SendLevelAsync(string deviceKey, int level, CancellationToken cancellationToken)
    {
        if (_mode == LovenseMode.Lan)
        {
            var payload =
                $"{{\"command\":\"Function\",\"action\":\"Vibrate:{level}\",\"timeSec\":{LanHoldSeconds},\"apiVer\":1}}";
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            return _client.PostAsync($"{_baseUrl}/command", content, cancellationToken);
        }

        var url = $"{_baseUrl}/command?command=Vibrate&action=Vibrate&intensity={level}"
            + $"&toy={Uri.EscapeDataString(deviceKey)}";
        return _client.GetAsync(url, cancellationToken);
    }

    /// <summary>
    /// Zero every device this sink has ever driven.
    ///
    /// <para><b>The suppression is bypassed here, deliberately.</b> A stop must reach the wire even
    /// when the sink believes zero already stands: the belief is a memory of a command that was
    /// accepted, not evidence that the toy is quiet now, and a teardown is the one moment where
    /// being wrong about that is unacceptable. The remembered level is cleared FIRST so the send
    /// cannot be suppressed by its own bookkeeping.</para>
    /// </summary>
    public async Task<CapabilityState> StopAllAsync()
    {
        string[] keys;
        lock (_gate)
        {
            keys = [.. _lastSentLevel.Keys];
            _lastSentLevel.Clear();
        }

        if (keys.Length == 0)
        {
            return Remember(new CapabilityState.Degraded(
                "the stop path itself is live and would have zeroed every device this sink had driven",
                new CapabilityReason(
                    HapticReasonCodes.HapticNoDevice,
                    "this Lovense sink never drove a device, so there was nothing to stop. Reported rather than "
                    + "claimed as a success: \"nothing was running\" and \"everything was stopped\" are "
                    + "different facts")));
        }

        var failures = new List<string>();
        foreach (var key in keys)
        {
            try
            {
                using var response = await SendLevelAsync(key, 0, CancellationToken.None).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    failures.Add($"{key}:{(int)response.StatusCode}");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                failures.Add($"{key}:{ex.GetType().Name}");
            }
        }

        return Remember(failures.Count == 0
            ? new CapabilityState.Available(
                $"a zero was accepted for every device this sink drove ({keys.Length})")
            : new CapabilityState.Unavailable(new CapabilityReason(
                HapticReasonCodes.HapticStopIncomplete,
                $"a stop did not reach {failures.Count} of {keys.Length} device(s): {string.Join(", ", failures)}. "
                + "The toy may still be running")));
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
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }
}
