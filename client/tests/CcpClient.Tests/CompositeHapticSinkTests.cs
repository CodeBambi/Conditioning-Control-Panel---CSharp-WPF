using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Haptics;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <see cref="CompositeHapticSink"/> — the sink the product actually holds.
///
/// <para><b>Why this file exists.</b> The composite landed with its merge, its prefix routing, its
/// succeed-if-any connect, its no-fallback rule and its per-route stop budget covered by NOTHING: the
/// only thing exercising it was the factory fact that constructs one. Every behaviour below is one an
/// audit would have to take on trust otherwise, and each is written so a deliberately broken
/// composite fails it rather than so a correct one passes.</para>
///
/// <para><b>No socket is opened by anything here.</b> Every route is a fake that answers from a list,
/// so what is under test is the composite's own arithmetic — which is the only part of this seam that
/// is this port's rather than a server's.</para>
/// </summary>
public class CompositeHapticSinkTests
{
    // =====================================================================================
    //  ObserveAsync — the merge, the prefix, and the dedupe that was DELETED
    // =====================================================================================

    /// <summary>
    /// Both enabled routes are asked, and every key comes back carrying the route that named it —
    /// upstream's own <c>provider:id</c> identity shape (<c>HapticContracts.cs:67</c>), in upstream's
    /// preference order (<c>HapticDeviceManager.cs:227-229</c>).
    /// </summary>
    [Fact]
    public async Task OBSERVEAsksEveryEnabledRoute_AndPrefixesEveryKeyWithTheRouteThatNamedIt()
    {
        var routes = new RouteFakes();
        routes.Lovense.Devices.AddRange(["toy-a", "toy-b"]);
        routes.Buttplug.Devices.Add("0");
        using var sink = routes.Composite(HapticProviderRoute.Lovense, HapticProviderRoute.Buttplug);

        var observation = await sink.ObserveAsync(TestContext.Current.CancellationToken);

        // Preference order, prefixed, nothing dropped and nothing renamed.
        Assert.Equal(
            ["lovense:toy-a", "lovense:toy-b", "buttplug:0"],
            observation.DeviceKeys);
        Assert.Equal(3, observation.DeviceCount);
        Assert.True(observation.Asked);
        Assert.True(observation.ServerAnswered);
        Assert.True(observation.ClientAdmitted);
        Assert.Equal(HapticProviderRoute.Lovense, observation.Route);
        Assert.IsType<CapabilityState.Available>(observation.Classify());

        // Each route was asked exactly once. A composite that asked one route twice would be
        // double-counting a server that answers slowly.
        Assert.Equal(1, routes.Lovense.Observes);
        Assert.Equal(1, routes.Buttplug.Observes);
    }

    /// <summary>
    /// <b>Two routes reporting devices whose RAW ids collide yield TWO devices.</b>
    ///
    /// <para>This fact fails against the behaviour it replaced. The composite used to keep ONE
    /// <c>seen</c> set across all routes, keyed on <c>NormalizeIdentity(rawKey)</c> — letters and
    /// digits, lower-cased. Buttplug raw keys are bare indices (<c>"0"</c>, <c>"1"</c>, <c>"2"</c>)
    /// and Lovense raw keys are JSON property names, so a Lovense toy whose id normalised to
    /// <c>"0"</c> silently DELETED the Buttplug device at index 0 — from the very list a user reads
    /// to find their toy, and with nothing downstream able to address it ever again.</para>
    ///
    /// <para>The normaliser was upstream's (<c>HapticDeviceManager.cs:294-301</c>) pointed at the
    /// wrong field: upstream folds on the device NAME <i>because</i> <c>"Buttplug ids are
    /// session-scoped"</c> (<c>:246-247</c>), which is the exact reason an id must not be compared.
    /// The emitted keys here are already route-prefixed and unique, so the fold had no upside.</para>
    /// </summary>
    [Fact]
    public async Task TWORoutesWhoseRAWIdsCOLLIDE_StillYieldTWODevices_BecauseTheKeysAreAlreadyUnique()
    {
        var routes = new RouteFakes();
        routes.Lovense.Devices.Add("0");
        routes.Buttplug.Devices.Add("0");
        using var sink = routes.Composite(HapticProviderRoute.Lovense, HapticProviderRoute.Buttplug);

        var observation = await sink.ObserveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["lovense:0", "buttplug:0"], observation.DeviceKeys);
        Assert.Equal(2, observation.DeviceCount);

        // And BOTH are drivable, which is the half that makes the deletion matter: a dropped key is
        // a device the user can see in their server and never reach from here.
        Assert.IsType<CapabilityState.Available>(await sink.SetOutputsAsync(
            "lovense:0", [new HapticOutput(0, HapticLevel.Of(0.4))], TestContext.Current.CancellationToken));
        Assert.IsType<CapabilityState.Available>(await sink.SetOutputsAsync(
            "buttplug:0", [new HapticOutput(0, HapticLevel.Of(0.4))], TestContext.Current.CancellationToken));
        Assert.Equal(["0"], routes.Lovense.Sent.Select(s => s.Key));
        Assert.Equal(["0"], routes.Buttplug.Sent.Select(s => s.Key));
    }

    /// <summary>
    /// <c>ServerAnswered</c> is ANY and never ALL, and a route that THREW is a route that did not
    /// answer rather than a fault that takes the other one down — upstream's rule in its own words,
    /// <i>"True when ANY connected provider answers — one dead provider must not tear down the
    /// other"</i> (<c>HapticDeviceManager.cs:186-199</c>), and its per-provider catch at
    /// <c>:111-121</c>.
    /// </summary>
    [Fact]
    public async Task AROUTEThatTHREWDidNotAnswer_AndNeverTakesTheOtherRouteDownWithIt()
    {
        var routes = new RouteFakes();
        routes.Lovense.ThrowOnObserve = true;
        routes.Buttplug.Devices.Add("2");
        using var sink = routes.Composite(HapticProviderRoute.Lovense, HapticProviderRoute.Buttplug);

        var observation = await sink.ObserveAsync(TestContext.Current.CancellationToken);

        Assert.True(observation.ServerAnswered);
        Assert.Equal(["buttplug:2"], observation.DeviceKeys);
        // The reported route is the preferred one that ANSWERED, so a command sent down it can
        // actually go somewhere. Reporting Lovense here would name a route that just threw.
        Assert.Equal(HapticProviderRoute.Buttplug, observation.Route);
        Assert.IsType<CapabilityState.Available>(observation.Classify());
    }

    /// <summary>Every enabled route silent is <c>haptic-server-unreachable</c>, never a missing
    /// device: nobody answered, so nothing may be said about a toy.</summary>
    [Fact]
    public async Task EVERYRouteSilentIsAnUNREACHABLEServer_NeverAMissingDevice()
    {
        var routes = new RouteFakes();
        routes.Lovense.ServerAnswers = false;
        routes.Buttplug.ServerAnswers = false;
        using var sink = routes.Composite(HapticProviderRoute.Lovense, HapticProviderRoute.Buttplug);

        var observation = await sink.ObserveAsync(TestContext.Current.CancellationToken);

        Assert.True(observation.Asked);
        Assert.False(observation.ServerAnswered);
        Assert.Empty(observation.DeviceKeys);
        // The preferred ENABLED route when none answered, so the refusal still names a route.
        Assert.Equal(HapticProviderRoute.Lovense, observation.Route);
        var state = Assert.IsType<CapabilityState.Unavailable>(observation.Classify());
        Assert.Equal(HapticReasonCodes.HapticServerUnreachable, state.Reason.Code);
    }

    // =====================================================================================
    //  ConnectAsync — succeed if ANY, and substitute NOTHING
    // =====================================================================================

    /// <summary>
    /// Succeed-if-any, with no fallback — upstream's <c>Task.WhenAll</c> over the enabled set
    /// followed by <c>results.Any(r =&gt; r)</c> (<c>HapticDeviceManager.cs:109-125</c>).
    /// </summary>
    [Fact]
    public async Task CONNECTSucceedsWhenANYRouteDid_AndPutsNOTHINGInThePlaceOfTheOneThatDidNot()
    {
        var routes = new RouteFakes();
        routes.Lovense.ConnectResult = new CapabilityState.Unavailable(new CapabilityReason(
            HapticReasonCodes.HapticServerUnreachable, "no Lovense server"));
        using var sink = routes.Composite(HapticProviderRoute.Lovense, HapticProviderRoute.Buttplug);

        var state = Assert.IsType<CapabilityState.Available>(
            await sink.ConnectAsync(TestContext.Current.CancellationToken));

        Assert.Contains("1 of 2", state.Detail, StringComparison.Ordinal);
        Assert.Contains("Buttplug", state.Detail, StringComparison.Ordinal);
        Assert.Contains("nothing was substituted for it", state.Detail, StringComparison.Ordinal);
        // It confirms a SERVER and says so, because nothing here can know a motor moved.
        Assert.Contains("never that a motor moved", state.Detail, StringComparison.Ordinal);

        // NO FALLBACK, measured: the route that failed was asked ONCE and the route that answered
        // was asked ONCE. A composite that retried the dead route down the live one would drive a
        // device the user did not ask it to drive.
        Assert.Equal(1, routes.Lovense.Connects);
        Assert.Equal(1, routes.Buttplug.Connects);
        Assert.Same(state, sink.LastOutcome);
    }

    /// <summary>
    /// Every route refusing yields the SHARED code when they agree and the PREFERRED route's when
    /// they do not — deterministic either way, and never a summary code nothing produced.
    /// </summary>
    [Fact]
    public async Task WhenEVERYRouteRefuses_TheCodeIsSharedWhenTheyAgreeAndTHEPREFERREDOnesWhenTheyDoNot()
    {
        var agreeing = new RouteFakes();
        agreeing.Lovense.ConnectResult = Refusal(HapticReasonCodes.HapticServerUnreachable, "no Lovense server");
        agreeing.Buttplug.ConnectResult = Refusal(HapticReasonCodes.HapticServerUnreachable, "no Intiface server");
        using var agreed = agreeing.Composite(HapticProviderRoute.Lovense, HapticProviderRoute.Buttplug);

        var shared = Assert.IsType<CapabilityState.Unavailable>(
            await agreed.ConnectAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HapticReasonCodes.HapticServerUnreachable, shared.Reason.Code);
        // Both routes' details survive, so a user is never told about one server when they own the
        // other kind of toy.
        Assert.Contains("[Lovense] no Lovense server", shared.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains("[Buttplug] no Intiface server", shared.Reason.Detail, StringComparison.Ordinal);

        var disagreeing = new RouteFakes();
        disagreeing.Lovense.ConnectResult = Refusal(HapticReasonCodes.HapticServerUnreachable, "no Lovense server");
        disagreeing.Buttplug.ConnectResult = Refusal(HapticReasonCodes.HapticCommandRefused, "Intiface said no");
        using var disagreed = disagreeing.Composite(HapticProviderRoute.Lovense, HapticProviderRoute.Buttplug);

        var preferred = Assert.IsType<CapabilityState.Unavailable>(
            await disagreed.ConnectAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HapticReasonCodes.HapticServerUnreachable, preferred.Reason.Code);
        Assert.NotEqual(HapticReasonCodes.HapticCommandRefused, preferred.Reason.Code);
    }

    // =====================================================================================
    //  SetOutputsAsync — the key names its own route
    // =====================================================================================

    /// <summary>
    /// The owning route is read OFF THE KEY and the route sink is handed the BARE id — upstream keeps
    /// a <c>_ownerByKey</c> map for the same job (<c>HapticDeviceManager.cs:24,202-208</c>).
    /// </summary>
    [Fact]
    public async Task SETOUTPUTSRoutesOnTheKEYSPrefix_AndHandsTheOwningSinkTheBAREId()
    {
        var routes = new RouteFakes();
        using var sink = routes.Composite(HapticProviderRoute.Lovense, HapticProviderRoute.Buttplug);
        var outputs = new[] { new HapticOutput(1, HapticLevel.Of(0.75)) };

        Assert.IsType<CapabilityState.Available>(
            await sink.SetOutputsAsync("buttplug:7", outputs, TestContext.Current.CancellationToken));

        // The prefix is CONSUMED, not passed on: a route sink that received "buttplug:7" would look
        // for a device its own server has never heard of.
        Assert.Equal(["7"], routes.Buttplug.Sent.Select(s => s.Key));
        Assert.Same(outputs, routes.Buttplug.Sent[0].Outputs);
        Assert.Empty(routes.Lovense.Sent);
    }

    /// <summary>
    /// A key naming no route this sink drives is a TYPED refusal and nothing is sent anywhere —
    /// where upstream silently drops the command (<c>HapticDeviceManager.cs:204-206</c> returns
    /// <c>Task.CompletedTask</c>). A silent drop on the path a user believes is driving their toy is
    /// the shape this port refuses everywhere.
    /// </summary>
    [Fact]
    public async Task AKeyNamingNORouteIsATypedRefusal_AndNothingIsSentDownEitherRoute()
    {
        var routes = new RouteFakes();
        using var sink = routes.Composite(HapticProviderRoute.Lovense, HapticProviderRoute.Buttplug);
        var outputs = new[] { new HapticOutput(0, HapticLevel.Of(0.5)) };

        var unknown = Assert.IsType<CapabilityState.Unavailable>(
            await sink.SetOutputsAsync("mock:1", outputs, TestContext.Current.CancellationToken));
        Assert.Equal(HapticReasonCodes.HapticDeviceUnknown, unknown.Reason.Code);
        Assert.Contains("lovense: or buttplug:", unknown.Reason.Detail, StringComparison.Ordinal);

        // An UNPREFIXED key is the same refusal: a bare provider id is meaningful only to the sink
        // that produced it, and guessing a route for it would drive the wrong device.
        var bare = Assert.IsType<CapabilityState.Unavailable>(
            await sink.SetOutputsAsync("7", outputs, TestContext.Current.CancellationToken));
        Assert.Equal(HapticReasonCodes.HapticDeviceUnknown, bare.Reason.Code);

        Assert.Empty(routes.Lovense.Sent);
        Assert.Empty(routes.Buttplug.Sent);
    }

    /// <summary>A key belonging to a route the user has since UN-TICKED is refused rather than
    /// sent: the enable flags are read per operation, so a stale key cannot drive a route the user
    /// has just switched off.</summary>
    [Fact]
    public async Task AKeyForARouteTheUserHasSinceUNTICKED_IsRefusedRatherThanSent()
    {
        var routes = new RouteFakes();
        using var sink = routes.Composite(HapticProviderRoute.Lovense, HapticProviderRoute.Buttplug);
        var outputs = new[] { new HapticOutput(0, HapticLevel.Of(0.5)) };

        Assert.IsType<CapabilityState.Available>(
            await sink.SetOutputsAsync("lovense:x", outputs, TestContext.Current.CancellationToken));

        routes.Enabled.Remove(HapticProviderRoute.Lovense);

        var refused = Assert.IsType<CapabilityState.Unavailable>(
            await sink.SetOutputsAsync("lovense:x", outputs, TestContext.Current.CancellationToken));
        Assert.Equal(HapticReasonCodes.HapticDeviceUnknown, refused.Reason.Code);
        Assert.Contains("which is not enabled", refused.Reason.Detail, StringComparison.Ordinal);
        Assert.Single(routes.Lovense.Sent);
    }

    // =====================================================================================
    //  StopAllAsync — every LIVE route, in parallel, each with its own budget
    // =====================================================================================

    /// <summary>
    /// The all-stop reaches every route this sink BROUGHT UP, including one the user un-ticked after
    /// it was connected. Upstream stops every provider whose <c>IsConnected</c> holds rather than
    /// every provider currently enabled (<c>HapticDeviceManager.cs:151-152</c>), and reading the
    /// enable flags here would be the one place a setting could leave a device running.
    /// </summary>
    [Fact]
    public async Task STOPALLReachesEveryLIVERoute_IncludingOneTheUserHasSinceUNTICKED()
    {
        var routes = new RouteFakes();
        using var sink = routes.Composite(HapticProviderRoute.Lovense, HapticProviderRoute.Buttplug);
        await sink.ConnectAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            [HapticProviderRoute.Lovense, HapticProviderRoute.Buttplug],
            sink.LiveRoutes);

        // The user un-ticks Lovense. Its toy is still holding a level.
        routes.Enabled.Remove(HapticProviderRoute.Lovense);

        var state = Assert.IsType<CapabilityState.Available>(await sink.StopAllAsync());

        Assert.Equal(1, routes.Lovense.StopAlls);
        Assert.Equal(1, routes.Buttplug.StopAlls);
        Assert.Contains("Lovense", state.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>EVERY route's stop is DISPATCHED before any of them is awaited</b>, which is the panic-path
    /// property upstream states in its own words: <i>"a Lovense stop that is waiting on a dead LAN
    /// socket must not hold the Buttplug stop hostage (they used to be awaited one after the
    /// other)"</i> (<c>HapticDeviceManager.cs:143-147</c>).
    ///
    /// <para><b>Proved by a signal rather than by a clock.</b> The Lovense route's stop completes ONLY
    /// when the Buttplug route's stop has been called. Dispatched together, Buttplug's call releases
    /// Lovense's and the whole all-stop completes — with no wall-clock wait anywhere, because the test
    /// awaits a real completion. Awaited one after the other, Lovense's stop can never complete (the
    /// thing that would release it has not been called yet) and the all-stop instead burns the
    /// per-route budget and reports an incomplete stop. The two outcomes are different STATES, not
    /// different durations.</para>
    /// </summary>
    [Fact]
    public async Task EVERYRoutesStopIsDISPATCHEDBeforeANYIsAwaited_SoADeadRouteHoldsNoOtherHostage()
    {
        var routes = new RouteFakes();
        var releasedByTheOtherRoute =
            new TaskCompletionSource<CapabilityState>(TaskCreationOptions.RunContinuationsAsynchronously);
        routes.Lovense.HangStopWith = releasedByTheOtherRoute;
        routes.Buttplug.OnStop = () =>
            releasedByTheOtherRoute.TrySetResult(new CapabilityState.Available("released by the other route"));
        using var sink = routes.Composite(HapticProviderRoute.Lovense, HapticProviderRoute.Buttplug);
        await sink.ConnectAsync(TestContext.Current.CancellationToken);

        var state = Assert.IsType<CapabilityState.Available>(await sink.StopAllAsync());

        Assert.Contains("every route this sink brought up", state.Detail, StringComparison.Ordinal);
        Assert.Equal(1, routes.Lovense.StopAlls);
        Assert.Equal(1, routes.Buttplug.StopAlls);
    }

    /// <summary>
    /// A route that OVERRUNS its budget is reported as an INCOMPLETE stop rather than waited on
    /// forever, and the report names the route and says a device may still be running. The per-route
    /// budget is upstream's <c>DefaultStopTimeout</c> (<c>HapticDeviceManager.cs:140-141</c>).
    ///
    /// <para>The budget here is <see cref="TimeSpan.Zero"/> and the overrunning stop never completes,
    /// so the outcome is decided by which task is already done rather than by elapsed time.</para>
    /// </summary>
    [Fact]
    public async Task ASTOPThatOVERRUNSItsBudgetIsREPORTED_RatherThanWaitedOnForever()
    {
        var routes = new RouteFakes();
        var hung = new TaskCompletionSource<CapabilityState>(TaskCreationOptions.RunContinuationsAsynchronously);
        routes.Lovense.HangStopWith = hung;
        using var sink = routes.Composite(
            [HapticProviderRoute.Lovense, HapticProviderRoute.Buttplug], stopBudget: TimeSpan.Zero);
        await sink.ConnectAsync(TestContext.Current.CancellationToken);

        var state = Assert.IsType<CapabilityState.Unavailable>(await sink.StopAllAsync());

        Assert.Equal(HapticReasonCodes.HapticStopIncomplete, state.Reason.Code);
        Assert.Contains("Lovense overran its stop budget", state.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains("A device may still be running", state.Reason.Detail, StringComparison.Ordinal);
        // The other route was still asked, so an overrun is never a reason to skip the rest.
        Assert.Equal(1, routes.Buttplug.StopAlls);

        hung.SetResult(new CapabilityState.Available("late"));
    }

    /// <summary>
    /// It never reports success for having stopped NOTHING. Upstream returns true for an empty
    /// provider list (<c>HapticDeviceManager.cs:153</c>); this port cannot, because a teardown pin
    /// reading green on a build that stopped nothing is the exact failure the haptic vocabulary
    /// exists to prevent.
    /// </summary>
    [Fact]
    public async Task STOPALLNeverReportsSUCCESSForHavingStoppedNOTHING()
    {
        var routes = new RouteFakes();
        using var sink = routes.Composite(HapticProviderRoute.Lovense, HapticProviderRoute.Buttplug);

        var state = Assert.IsType<CapabilityState.Degraded>(await sink.StopAllAsync());

        Assert.Equal(HapticReasonCodes.HapticNoDevice, state.Reason.Code);
        Assert.Contains("nothing to stop", state.Reason.Detail, StringComparison.Ordinal);
        Assert.Equal(0, routes.Lovense.StopAlls);
        Assert.Equal(0, routes.Buttplug.StopAlls);
    }

    // =====================================================================================
    //  The boundary: nothing ticked contacts nothing, and builds nothing
    // =====================================================================================

    /// <summary>
    /// <b>With no route ticked, the composite refuses every verb BEFORE a wire and never even
    /// constructs a route client.</b>
    ///
    /// <para>The construction count is the part that matters: a client built eagerly is an
    /// <c>HttpClient</c> or a WebSocket connector held for a feature nobody switched on, and the
    /// refusal above it would then be true of the wire and false of the process. Upstream refuses in
    /// exactly this place, twice — the manager returns false before any connect
    /// (<c>HapticDeviceManager.cs:103-107</c>) and the button refuses before calling it at all
    /// (<c>MainWindow/MainWindow.Haptics.cs:653-660</c>).</para>
    /// </summary>
    [Fact]
    public async Task NOROUTETickedRefusesEVERYVerbBeforeAWire_AndBuildsNOCLIENTAtAll()
    {
        var routes = new RouteFakes();
        using var sink = routes.Composite();

        Assert.Equal(HapticProviderRoute.None, sink.Route);
        Assert.Empty(sink.LiveRoutes);

        var connect = Assert.IsType<CapabilityState.Unavailable>(
            await sink.ConnectAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HapticReasonCodes.HapticNoProviderEnabled, connect.Reason.Code);
        Assert.Contains("nothing was CONTACTED either", connect.Reason.Detail, StringComparison.Ordinal);

        var observation = await sink.ObserveAsync(TestContext.Current.CancellationToken);
        Assert.False(observation.Asked);
        Assert.True(observation.ClientAdmitted);
        var classified = Assert.IsType<CapabilityState.Unavailable>(observation.Classify());
        Assert.Equal(HapticReasonCodes.HapticNoProviderEnabled, classified.Reason.Code);

        // NOTHING WAS BUILT. Not a performance point: an eagerly-built client holds a socket for a
        // feature nobody switched on.
        Assert.Equal(0, routes.Built);
        Assert.Equal(0, routes.Lovense.Observes);
        Assert.Equal(0, routes.Buttplug.Observes);
    }

    /// <summary>Disposal releases every route the composite brought up, and one route's failure to
    /// release must not skip the next: the thing being released is a socket to another program, and
    /// leaking it is how a later run finds the port busy.</summary>
    [Fact]
    public async Task DISPOSEReleasesEVERYLiveRoute_EvenWhenTheFirstOneTHROWS()
    {
        var routes = new RouteFakes();
        routes.Lovense.ThrowOnDispose = true;
        var sink = routes.Composite(HapticProviderRoute.Lovense, HapticProviderRoute.Buttplug);
        await sink.ConnectAsync(TestContext.Current.CancellationToken);

        sink.Dispose();

        Assert.True(routes.Lovense.Disposed);
        Assert.True(routes.Buttplug.Disposed);
        Assert.Empty(sink.LiveRoutes);
    }

    private static CapabilityState Refusal(string code, string detail) =>
        new CapabilityState.Unavailable(new CapabilityReason(code, detail));

    /// <summary>The two route sinks and the enabled set, so every fact drives the composite's own
    /// arithmetic rather than a server's.</summary>
    private sealed class RouteFakes
    {
        public RouteSink Lovense { get; } = new(HapticProviderRoute.Lovense);

        public RouteSink Buttplug { get; } = new(HapticProviderRoute.Buttplug);

        public List<HapticProviderRoute> Enabled { get; } = [];

        /// <summary>How many route clients the composite asked to be built. Zero is the claim that a
        /// user who ticked nothing pays for nothing.</summary>
        public int Built { get; private set; }

        public CompositeHapticSink Composite(params HapticProviderRoute[] enabled) =>
            Composite(enabled, stopBudget: null);

        public CompositeHapticSink Composite(
            IReadOnlyList<HapticProviderRoute> enabled, TimeSpan? stopBudget)
        {
            Enabled.AddRange(enabled);
            return new CompositeHapticSink(() => Enabled, Build, stopBudget);
        }

        private IHapticSink Build(HapticProviderRoute route)
        {
            Built++;
            return route == HapticProviderRoute.Lovense ? Lovense : Buttplug;
        }
    }

    private sealed class RouteSink(HapticProviderRoute route) : IHapticSink
    {
        public HapticProviderRoute Route { get; } = route;

        public CapabilityState? LastOutcome { get; private set; }

        public List<string> Devices { get; } = [];

        public List<(string Key, IReadOnlyList<HapticOutput> Outputs)> Sent { get; } = [];

        public bool ServerAnswers { get; set; } = true;

        public bool ThrowOnObserve { get; set; }

        public bool ThrowOnDispose { get; set; }

        public TaskCompletionSource<CapabilityState>? HangStopWith { get; set; }

        /// <summary>Run when this route's stop is CALLED, before its task completes. The signal that
        /// makes "dispatched before awaited" provable without a clock.</summary>
        public Action? OnStop { get; set; }

        public CapabilityState ConnectResult { get; set; } =
            new CapabilityState.Available("the fake server answered");

        public int Connects { get; private set; }

        public int Observes { get; private set; }

        public int StopAlls { get; private set; }

        public bool Disposed { get; private set; }

        public Task<HapticServerObservation> ObserveAsync(CancellationToken cancellationToken)
        {
            Observes++;
            if (ThrowOnObserve)
            {
                throw new InvalidOperationException("this fake route was told to throw");
            }

            return Task.FromResult(new HapticServerObservation(
                Asked: true,
                Route: Route,
                ClientAdmitted: true,
                ServerAnswered: ServerAnswers,
                DeviceKeys: ServerAnswers ? [.. Devices] : []));
        }

        public Task<CapabilityState> ConnectAsync(CancellationToken cancellationToken)
        {
            Connects++;
            return Task.FromResult(LastOutcome = ConnectResult);
        }

        public Task<CapabilityState> SetOutputsAsync(
            string deviceKey, IReadOnlyList<HapticOutput> outputs, CancellationToken cancellationToken)
        {
            Sent.Add((deviceKey, outputs));
            return Task.FromResult(LastOutcome = new CapabilityState.Available("the fake route accepted it"));
        }

        public Task<CapabilityState> StopAllAsync()
        {
            StopAlls++;
            OnStop?.Invoke();
            return HangStopWith?.Task
                ?? Task.FromResult(LastOutcome = new CapabilityState.Available("the fake route stopped"));
        }

        public void Dispose()
        {
            Disposed = true;
            if (ThrowOnDispose)
            {
                throw new InvalidOperationException("this fake route was told to throw on release");
            }
        }
    }
}
