using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Haptics;

/// <summary>
/// Every route the user has switched on, driven together.
///
/// <para><b>Providers are a SET, not a one-of, and that is upstream's shape rather than a
/// generalisation of it.</b> <c>HapticDeviceManager</c> says so in its own class doc — <i>"Providers
/// are connected CONCURRENTLY (the old service had exactly one 'active provider', so a Lovense user
/// could not also drive an Intiface toy)"</i> (<c>Services/Haptics/Core/HapticDeviceManager.cs:10-14</c>)
/// — registers all three unconditionally (<c>:36-38</c>), connects the ENABLED ones with
/// <c>Task.WhenAll</c> and succeeds when any of them did (<c>:109-125</c>). The user's surface is
/// three checkboxes headed <i>"PROVIDERS - enable as many as you like, they connect together"</i>
/// (<c>Views/Tabs/HapticsTabView.xaml:726</c>), never a dropdown.</para>
///
/// <para><b>The enabled set is read PER OPERATION, never captured.</b> Upstream re-evaluates
/// <c>EnabledProviders()</c> at each connect (<c>:102</c>, <c>:91-98</c>), so ticking a box takes
/// effect without rebuilding anything — and in this port it also means the sink can be constructed
/// before the settings file has loaded, which is where the participant builds it.</para>
///
/// <para><b>NO FALLBACK.</b> A route the user did not tick is never substituted for one that failed:
/// upstream a dead provider is simply a <c>false</c> in the results array (<c>:109-125</c>), and the
/// same holds here. One route failing to connect never stops the other from succeeding, and never
/// promotes an untouched route into its place.</para>
///
/// <para><b>Teardown fans out in PARALLEL, and that is the one place the shape is load-bearing
/// rather than tidy.</b> Upstream's words: <i>"This is the panic path: a Lovense stop that is
/// waiting on a dead LAN socket must not hold the Buttplug stop hostage (they used to be awaited one
/// after the other)"</i> (<c>:143-181</c>). Each route also gets its own budget there, and this port
/// keeps that too, because neither route bounds a hung stop from above.</para>
/// </summary>
public sealed class CompositeHapticSink : IHapticSink
{
    /// <summary>
    /// Which route wins when the same device is visible through two of them, in upstream's order and
    /// for upstream's reason (<c>Services/Haptics/Core/HapticDeviceManager.cs:19-21</c>): <i>"When
    /// the same toy is visible through two providers we keep the one with the richer API. Lovense
    /// LAN gives us depth/position/presets/button events; Buttplug does not"</i>. The mock provider
    /// is the third entry upstream and has no route here at all.
    /// </summary>
    public static readonly IReadOnlyList<HapticProviderRoute> Preference =
        [HapticProviderRoute.Lovense, HapticProviderRoute.Buttplug];

    /// <summary>
    /// The per-route budget for an all-stop, verbatim from upstream's
    /// <c>HapticDeviceManager.DefaultStopTimeout</c> (<c>:140-141</c>).
    ///
    /// <para>It is not redundant with each route's own wire timeouts. The Lovense route bounds one
    /// HTTP request (<c>LovenseHapticSink.RequestTimeout</c>) but an all-stop there is one request
    /// per toy, and the Buttplug route's stop awaits the library once per device with nothing above
    /// it bounding the total. This is the bound on the whole route's stop, which is the thing
    /// teardown actually waits on.</para>
    /// </summary>
    public static readonly TimeSpan DefaultStopBudget = TimeSpan.FromSeconds(2);

    /// <summary>What a user is told when they have ticked nothing. It names both routes and the
    /// program each one needs, because upstream's own message names the providers by their user-facing
    /// names (<c>Localization/Languages/en.json:4125</c>: <i>"Tick at least one provider first -
    /// Lovense, Intiface or Mock."</i>) and a refusal that named none would leave a user hunting.</summary>
    public const string NoProviderEnabledDetail =
        "no haptic provider is enabled, so nothing was attempted — and nothing was CONTACTED either: this refusal is "
        + "decided before any socket is opened. Tick at least one route and they connect TOGETHER rather than one "
        + "instead of the other: Lovense (needs Lovense Connect or Lovense Remote running) or Intiface (needs "
        + "Intiface Central or Intiface Engine running). This is NOT \"no device found\" and NOT \"this build has no "
        + "client\": both routes have a client here and neither was switched on.";

    private readonly Func<IReadOnlyList<HapticProviderRoute>> _enabledRoutes;
    private readonly Func<HapticProviderRoute, IHapticSink> _sinkFor;
    private readonly TimeSpan _stopBudget;
    private readonly object _gate = new();
    private readonly Dictionary<HapticProviderRoute, IHapticSink> _live = [];

    private CapabilityState? _lastOutcome;
    private bool _disposed;

    /// <param name="enabledRoutes">
    /// The routes the user has switched on, asked afresh at every operation. Anything outside
    /// <see cref="Preference"/> is ignored rather than refused: the enable flags are a user setting
    /// and a stale one must not fault a teardown.
    /// </param>
    /// <param name="sinkFor">Builds the sink for one route. Called at most once per route, lazily,
    /// so a route nobody enabled never constructs a client.</param>
    /// <param name="stopBudget">Per-route all-stop budget; null takes <see cref="DefaultStopBudget"/>.</param>
    public CompositeHapticSink(
        Func<IReadOnlyList<HapticProviderRoute>> enabledRoutes,
        Func<HapticProviderRoute, IHapticSink> sinkFor,
        TimeSpan? stopBudget = null)
    {
        _enabledRoutes = enabledRoutes ?? throw new ArgumentNullException(nameof(enabledRoutes));
        _sinkFor = sinkFor ?? throw new ArgumentNullException(nameof(sinkFor));
        _stopBudget = stopBudget ?? DefaultStopBudget;
    }

    /// <summary>
    /// The preferred enabled route, or <see cref="HapticProviderRoute.None"/> when the user has
    /// ticked nothing.
    ///
    /// <para><b>The <c>None</c> answer is load-bearing:</b> <c>HapticParticipant</c> returns early on
    /// it before any connect (<c>HapticParticipant.cs:277</c>), which is where "refuse before
    /// touching the wire" actually happens on the product path — the same order upstream keeps, where
    /// the button refuses before the manager is called at all
    /// (<c>MainWindow/MainWindow.Haptics.cs:653-660</c>).</para>
    /// </summary>
    public HapticProviderRoute Route
    {
        get
        {
            var routes = EnabledRoutes();
            return routes.Count == 0 ? HapticProviderRoute.None : routes[0];
        }
    }

    /// <inheritdoc/>
    public CapabilityState? LastOutcome
    {
        get { lock (_gate) { return _lastOutcome; } }
    }

    /// <summary>The routes this sink has actually built a client for, in preference order.
    /// Diagnostic: it is what an all-stop will reach, which is a wider set than the currently
    /// enabled one on purpose — see <see cref="StopAllAsync"/>.</summary>
    public IReadOnlyList<HapticProviderRoute> LiveRoutes
    {
        get
        {
            lock (_gate)
            {
                return [.. Preference.Where(_live.ContainsKey)];
            }
        }
    }

    /// <summary>Upstream's provider key for a route (<c>HapticContracts.cs:67</c>,
    /// <c>DeviceKey =&gt; ProviderKey + ":" + Id</c>; the keys themselves at
    /// <c>HapticDeviceManager.cs:21</c>).</summary>
    public static string RouteKey(HapticProviderRoute route) => route switch
    {
        HapticProviderRoute.Lovense => "lovense",
        HapticProviderRoute.Buttplug => "buttplug",
        _ => "none",
    };

    /// <summary>
    /// Ask every enabled route at once and merge what they name into one list, in preference order,
    /// each key prefixed <c>route:id</c> — upstream's own identity shape
    /// (<c>HapticContracts.cs:67</c>, <c>DeviceKey =&gt; ProviderKey + ":" + Id</c>) and its own
    /// preference ordering (<c>HapticDeviceManager.cs:227-229</c>).
    ///
    /// <para><b>NOTHING IS DE-DUPLICATED HERE, and the absence is the decision.</b> An earlier draft
    /// kept one <c>seen</c> set across all routes, keyed on <c>NormalizeIdentity(rawKey)</c>. That
    /// was upstream's <c>NormalizeName</c> (<c>:294-301</c>, applied at <c>:246-248</c>) pointed at
    /// the wrong field: upstream folds on the device NAME precisely <i>because</i> <c>"Buttplug ids
    /// are session-scoped"</c> (<c>:246-247</c>), so an id is the one thing it will not compare. A
    /// Buttplug raw key is a bare index — <c>"0"</c>, <c>"1"</c>, <c>"2"</c> — and a Lovense raw key
    /// is a JSON property name, so a Lovense toy keyed <c>"0"</c> (or anything normalising to it)
    /// silently DELETED the Buttplug device at index 0 from the very list a user reads to find their
    /// toy, and nothing downstream could ever address it again. The emitted keys are already
    /// route-prefixed and therefore unique, so the fold had no upside and exactly one effect: losing
    /// a real device.</para>
    ///
    /// <para>The consequence, stated rather than hidden: one physical toy visible through both routes
    /// appears TWICE, where upstream would have folded it by name. This seam carries no names — a
    /// <see cref="HapticServerObservation"/> holds keys and nothing else — and two addressable
    /// entries for one toy is a smaller wrong than one unaddressable device.</para>
    ///
    /// <para><b><c>ServerAnswered</c> is ANY, never ALL</b>, which is upstream's liveness rule in as
    /// many words: <i>"True when ANY connected provider answers — one dead provider must not tear
    /// down the other"</i> (<c>:186-199</c>).</para>
    /// </summary>
    public async Task<HapticServerObservation> ObserveAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return HapticServerObservation.SinkDisposed;
        }

        var routes = EnabledRoutes();
        if (routes.Count == 0)
        {
            return NothingTickedObservation();
        }

        var observations = await Task.WhenAll(routes.Select(async route =>
        {
            try
            {
                return (route, observation: await Sink(route).ObserveAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A route that threw while being asked is a route that did not answer, never a fault
                // that takes the other one down with it (upstream catches per provider, :111-121).
                return (route, observation: new HapticServerObservation(true, route, true, false, []));
            }
        })).ConfigureAwait(false);

        var keys = new List<string>();
        foreach (var (route, observation) in observations)
        {
            foreach (var key in observation.DeviceKeys)
            {
                keys.Add(RouteKey(route) + ":" + key);
            }
        }

        var answered = observations.Where(pair => pair.observation.ServerAnswered).ToList();
        return new HapticServerObservation(
            Asked: true,
            // The preferred route that ANSWERED, so the reported route is one a command could
            // actually go down; the preferred enabled route when none answered.
            Route: answered.Count > 0 ? answered[0].route : routes[0],
            ClientAdmitted: observations.Any(pair => pair.observation.ClientAdmitted),
            ServerAnswered: answered.Count > 0,
            DeviceKeys: keys);
    }

    /// <summary>
    /// Connect every enabled route at once, and succeed when ANY of them did — upstream's
    /// <c>Task.WhenAll</c> over the enabled set followed by <c>results.Any(r =&gt; r)</c>
    /// (<c>Services/Haptics/Core/HapticDeviceManager.cs:109-125</c>).
    /// </summary>
    public async Task<CapabilityState> ConnectAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return Remember(Disposed("no connection was attempted"));
        }

        var routes = EnabledRoutes();
        if (routes.Count == 0)
        {
            // BEFORE the wire, exactly as upstream refuses before calling the manager
            // (MainWindow/MainWindow.Haptics.cs:653-660) and as the manager refuses before
            // connecting anything (:103-107).
            return Remember(NothingTicked());
        }

        var results = await Task.WhenAll(routes.Select(async route =>
        {
            try
            {
                return (route, state: await Sink(route).ConnectAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Upstream logs and returns false for a provider that threw (:111-121); one route's
                // fault never becomes the whole connect's exception.
                return (route, state: (CapabilityState)new CapabilityState.Unavailable(new CapabilityReason(
                    HapticReasonCodes.HapticServerUnreachable,
                    $"the {route} route threw while connecting ({ex.GetType().Name})")));
            }
        })).ConfigureAwait(false);

        return Remember(Aggregate(results));
    }

    /// <summary>
    /// Drive one device on the route its key names.
    ///
    /// <para>The key is <c>route:id</c>, so the owning route is read off the key rather than
    /// remembered — upstream keeps a <c>_ownerByKey</c> map for the same job
    /// (<c>HapticDeviceManager.cs:24,202-208</c>). A key whose route is unknown, or whose route the
    /// user has since un-ticked, is a typed refusal here where upstream silently drops the command
    /// (<c>:204-206</c> returns <c>Task.CompletedTask</c>); a silent drop on the path a user believes
    /// is driving their toy is the shape this port refuses everywhere.</para>
    /// </summary>
    public async Task<CapabilityState> SetOutputsAsync(
        string deviceKey, IReadOnlyList<HapticOutput> outputs, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKey);
        ArgumentNullException.ThrowIfNull(outputs);
        if (outputs.Count == 0)
        {
            throw new ArgumentException(
                "an empty output list commands nothing; call StopAllAsync to stop, or send a silent level",
                nameof(outputs));
        }

        if (_disposed)
        {
            return Remember(Disposed($"the '{deviceKey}' command was not attempted"));
        }

        var separator = deviceKey.IndexOf(':', StringComparison.Ordinal);
        var prefix = separator > 0 ? deviceKey[..separator] : string.Empty;
        var route = Preference.FirstOrDefault(
            candidate => string.Equals(RouteKey(candidate), prefix, StringComparison.Ordinal),
            HapticProviderRoute.None);
        if (route == HapticProviderRoute.None)
        {
            return Remember(new CapabilityState.Unavailable(new CapabilityReason(
                HapticReasonCodes.HapticDeviceUnknown,
                $"'{deviceKey}' names no route this sink drives. Keys come from an observation of this sink and are "
                + "shaped 'route:id' (lovense: or buttplug:)")));
        }

        if (!EnabledRoutes().Contains(route))
        {
            return Remember(new CapabilityState.Unavailable(new CapabilityReason(
                HapticReasonCodes.HapticDeviceUnknown,
                $"'{deviceKey}' belongs to the {route} route, which is not enabled, so the command was not sent")));
        }

        return Remember(await Sink(route)
            .SetOutputsAsync(deviceKey[(separator + 1)..], outputs, cancellationToken)
            .ConfigureAwait(false));
    }

    /// <summary>
    /// Stop every route this sink has brought up, IN PARALLEL, each with its own budget.
    ///
    /// <para><b>Every LIVE route, not every ENABLED one.</b> A route the user un-ticked after it was
    /// connected still has a level on a toy, and upstream stops every provider whose
    /// <c>IsConnected</c> holds rather than every provider currently enabled
    /// (<c>Services/Haptics/Core/HapticDeviceManager.cs:151-152</c>). Reading the enable flags here
    /// would be the one place a setting could leave a device running.</para>
    ///
    /// <para><b>Parallel is the behaviour, not the implementation:</b> <i>"a Lovense stop that is
    /// waiting on a dead LAN socket must not hold the Buttplug stop hostage"</i> (<c>:143-147</c>).
    /// Every route's stop is dispatched before any of them is awaited, and a route that overruns its
    /// budget is abandoned with its fault still observed — an unobserved one would surface later as
    /// an <c>UnobservedTaskException</c> on a pool thread (<c>:161-166</c>).</para>
    ///
    /// <para><b>It never reports success for having stopped nothing.</b> Upstream returns true for an
    /// empty provider list (<c>:153</c>); this port cannot, because a teardown pin reading green on a
    /// build that stopped nothing is the exact failure the haptic vocabulary exists to prevent.</para>
    /// </summary>
    public async Task<CapabilityState> StopAllAsync()
    {
        if (_disposed)
        {
            // The disposal, never the empty-route answer: a caller debugging a teardown ordering bug
            // needs to tell "released already" from "nothing was ever brought up".
            return Remember(Disposed("nothing was stopped"));
        }

        List<KeyValuePair<HapticProviderRoute, IHapticSink>> live;
        lock (_gate)
        {
            live = [.. Preference.Where(_live.ContainsKey).Select(route => new KeyValuePair<HapticProviderRoute, IHapticSink>(route, _live[route]))];
        }

        if (live.Count == 0)
        {
            return Remember(new CapabilityState.Degraded(
                "the stop path itself is live and reaches every route this sink has brought up",
                new CapabilityReason(
                    HapticReasonCodes.HapticNoDevice,
                    "no haptic route has been brought up, so there was nothing to stop. Reported rather than claimed "
                    + "as a success: \"nothing was running\" and \"everything was stopped\" are different facts")));
        }

        var results = await Task.WhenAll(live.Select(async pair =>
        {
            try
            {
                // Started HERE, before the first await, so every route's stop is on its way before
                // any of them is waited on. This is the parallelism the panic path needs.
                var stop = pair.Value.StopAllAsync();
                var done = await Task.WhenAny(stop, Task.Delay(_stopBudget)).ConfigureAwait(false);
                if (!ReferenceEquals(done, stop))
                {
                    _ = stop.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
                    return (pair.Key, state: (CapabilityState?)null, note: "overran its stop budget");
                }

                var state = await stop.ConfigureAwait(false);
                return (pair.Key, state: (CapabilityState?)state, note: string.Empty);
            }
            catch (Exception ex)
            {
                return (pair.Key, state: (CapabilityState?)null, note: $"threw {ex.GetType().Name}");
            }
        })).ConfigureAwait(false);

        var failed = results
            .Where(r => r.state is null or CapabilityState.Unavailable or CapabilityState.Faulted)
            .ToList();
        if (failed.Count > 0)
        {
            var named = string.Join(", ", failed.Select(r =>
                $"{r.Key} {(r.note.Length > 0 ? r.note : "refused the stop")}"));
            return Remember(new CapabilityState.Unavailable(new CapabilityReason(
                HapticReasonCodes.HapticStopIncomplete,
                $"a stop did not reach every route ({named}). A device may still be running")));
        }

        var delivered = results.Where(r => r.state is CapabilityState.Available).ToList();
        if (delivered.Count == 0)
        {
            var first = results.Select(r => r.state).OfType<CapabilityState.Degraded>().First();
            return Remember(new CapabilityState.Degraded(
                "every route this sink brought up completed its stop",
                first.Reason));
        }

        return Remember(new CapabilityState.Available(
            $"a stop completed on every route this sink brought up ({string.Join(", ", results.Select(r => r.Key))})"));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        List<IHapticSink> sinks;
        lock (_gate)
        {
            sinks = [.. _live.Values];
            _live.Clear();
        }

        foreach (var sink in sinks)
        {
            // One route's disposal must not skip the next one's: the thing being released is a
            // socket to another program, and leaking it is how a later run finds the port busy.
            try { sink.Dispose(); }
            catch (Exception) { /* nothing above this can act on it, and the rest must still close */ }
        }
    }

    private IReadOnlyList<HapticProviderRoute> EnabledRoutes()
    {
        if (_disposed)
        {
            return [];
        }

        var wanted = _enabledRoutes() ?? [];
        return [.. Preference.Where(wanted.Contains)];
    }

    private IHapticSink Sink(HapticProviderRoute route)
    {
        lock (_gate)
        {
            if (_live.TryGetValue(route, out var existing))
            {
                return existing;
            }

            var created = _sinkFor(route);
            _live[route] = created;
            return created;
        }
    }

    private static CapabilityState Aggregate(
        IReadOnlyList<(HapticProviderRoute route, CapabilityState state)> results)
    {
        var answered = results.Where(r => r.state is CapabilityState.Available).ToList();
        if (answered.Count > 0)
        {
            var silent = results.Where(r => r.state is not CapabilityState.Available).ToList();
            var detail = $"{answered.Count} of {results.Count} enabled haptic route(s) answered "
                + $"({string.Join(", ", answered.Select(r => r.route))})"
                + (silent.Count > 0 ? $"; {string.Join(", ", silent.Select(r => r.route))} did not, and nothing was "
                    + "substituted for it" : string.Empty)
                + ". Confirms the SERVER, never that a motor moved";
            return new CapabilityState.Available(detail);
        }

        var degraded = results.Select(r => r.state).OfType<CapabilityState.Degraded>().FirstOrDefault();
        if (degraded is not null)
        {
            return degraded;
        }

        // Every route refused. The code is the shared one when they agree, and the PREFERRED route's
        // when they do not — deterministic either way, and never a made-up summary code.
        var reasons = results.Select(r => (r.route, reason: ReasonOf(r.state))).ToList();
        var codes = reasons.Select(r => r.reason.Code).Distinct(StringComparer.Ordinal).ToList();
        var code = codes.Count == 1 ? codes[0] : reasons[0].reason.Code;
        return new CapabilityState.Unavailable(new CapabilityReason(
            code,
            string.Join(" ", reasons.Select(r => $"[{r.route}] {r.reason.Detail}"))));
    }

    private static CapabilityReason ReasonOf(CapabilityState state) => state switch
    {
        CapabilityState.Unavailable unavailable => unavailable.Reason,
        CapabilityState.Faulted faulted => faulted.Reason,
        CapabilityState.PermissionRequired permission => permission.Reason,
        CapabilityState.DependencyMissing missing => missing.Reason,
        CapabilityState.Degraded degraded => degraded.Reason,
        _ => new CapabilityReason(
            HapticReasonCodes.HapticServerUnreachable, "the route reported no reason for its refusal"),
    };

    private static CapabilityState NothingTicked() =>
        new CapabilityState.Unavailable(new CapabilityReason(
            HapticReasonCodes.HapticNoProviderEnabled, NoProviderEnabledDetail));

    /// <summary>The observation for a nothing-ticked ask: a client IS admitted, nothing was asked of
    /// any server, and the refusal travels with it so the capability line says what to tick rather
    /// than "nothing has been probed yet".</summary>
    private static HapticServerObservation NothingTickedObservation() =>
        new(false, HapticProviderRoute.None, ClientAdmitted: true, ServerAnswered: false, DeviceKeys: [])
        {
            Refused = new CapabilityReason(HapticReasonCodes.HapticNoProviderEnabled, NoProviderEnabledDetail),
        };

    private static CapabilityState Disposed(string what) =>
        new CapabilityState.Unavailable(new CapabilityReason(
            HapticReasonCodes.HapticSinkDisposed, $"this haptic sink was disposed, so {what}"));

    private CapabilityState Remember(CapabilityState state)
    {
        lock (_gate)
        {
            _lastOutcome = state;
        }

        return state;
    }
}
