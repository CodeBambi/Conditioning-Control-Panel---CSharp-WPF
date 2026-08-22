using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Haptics;

namespace CcpClient.Tests;

/// <summary>
/// The evidence surface for a limb that can never move anything.
///
/// <para><b>IT RECORDS AND IT NEVER TRANSFORMS, and that rule is the whole design.</b> The
/// named hazard: a test double whose <c>Write</c> clamped laundered the very clamp the fact existed
/// to test, so the fact passed while the product's arithmetic was wrong. Nothing here clamps,
/// rounds, quantises, coalesces, de-duplicates, re-orders or drops. The exact
/// <see cref="IReadOnlyList{T}"/> reference the product handed over is what is stored, so a fact
/// asserting a level is asserting the number the product computed and not a number this class
/// produced. <c>HapticLimbTests.TheRecordingSinkRecordsRAWAndTransformsNOTHING</c> executes that
/// claim rather than trusting it.</para>
///
/// <para><b>Why a double is admissible here at all.</b> The product sink refuses everything and no
/// device key exists to address, so on a product path a level is COMPUTED and never sent — which is
/// correct and must stay correct. This double supplies the one thing the product cannot: a device to
/// address. It is never constructed by product code, and nothing in the product may build one
/// (<c>CompositionRoot.EntitlementFactory</c>'s rule, applied to a sink).</para>
/// </summary>
internal sealed class RecordingHapticSink : IHapticSink
{
    private readonly List<Command> _commands = [];
    private readonly object _gate = new();
    private readonly IReadOnlyList<string> _devices;

    public RecordingHapticSink(HapticProviderRoute route = HapticProviderRoute.Buttplug, int devices = 1)
    {
        Route = route;
        _devices = Enumerable.Range(0, devices)
            .Select(i => route.ToString().ToLowerInvariant() + ":"
                + i.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
    }

    /// <inheritdoc/>
    public HapticProviderRoute Route { get; }

    /// <inheritdoc/>
    public CapabilityState? LastOutcome { get; private set; }

    /// <summary>The device keys this double will name when asked. A caller wires them into a limb's
    /// roster; the product's roster is empty on every run.</summary>
    public IReadOnlyList<string> DeviceKeys => _devices;

    /// <summary>How many all-stops arrived.</summary>
    public int StopAlls { get; private set; }

    /// <summary>
    /// Read AT THE INSTANT <see cref="StopAllAsync"/> runs, so a fact can assert what the caller had
    /// already done BY THEN rather than what the end state looks like afterwards.
    ///
    /// <para><b>This exists because an end-state assertion cannot see an ordering.</b> A fact that
    /// only checked <c>Layer == 0</c> after teardown passes identically whether the limb was cleared
    /// before the devices were stopped or after — and "after" re-opens the exact window the stop is
    /// there to close, because a wake firing in between would level-set a device the all-stop had
    /// just silenced. It is the same idiom <c>HapticParticipant</c>'s own <c>sequence</c> /
    /// <c>AllStopSequence</c> pair carries, in the currency this ordering is actually about.</para>
    /// </summary>
    public Func<string>? ObserveAtStopAll { get; set; }

    /// <summary>Whatever <see cref="ObserveAtStopAll"/> answered, or null when nothing was asked.</summary>
    public string? StateAtStopAll { get; private set; }

    /// <summary>Read at the instant <see cref="Dispose"/> runs, for the same reason: the sink must
    /// not still be reachable by a limb that can accept commands.</summary>
    public Func<string>? ObserveAtDispose { get; set; }

    /// <summary>Whatever <see cref="ObserveAtDispose"/> answered, or null when nothing was asked.</summary>
    public string? StateAtDispose { get; private set; }

    /// <summary>Whether the sink has been disposed.</summary>
    public bool Disposed { get; private set; }

    /// <summary>Every level-set, in arrival order, exactly as it was handed over.</summary>
    public IReadOnlyList<Command> Commands
    {
        get
        {
            lock (_gate)
            {
                return _commands.ToArray();
            }
        }
    }

    /// <summary>The first actuator's level from each recorded command, in order. A READ over what was
    /// stored — it computes nothing and changes nothing.</summary>
    public IReadOnlyList<double> Levels => Commands.Select(c => c.Outputs[0].Level.Value).ToArray();

    /// <inheritdoc/>
    public Task<HapticServerObservation> ObserveAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new HapticServerObservation(true, Route, true, true, _devices));

    /// <inheritdoc/>
    public async Task<CapabilityState> ConnectAsync(CancellationToken cancellationToken) =>
        LastOutcome = (await ObserveAsync(cancellationToken)).Classify();

    /// <inheritdoc/>
    public Task<CapabilityState> SetOutputsAsync(
        string deviceKey, IReadOnlyList<HapticOutput> outputs, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            // The reference, not a copy and not a projection. A double that rebuilt this list would
            // be free to "tidy" it, which is that failure in one line.
            _commands.Add(new Command(deviceKey, outputs));
        }

        return Task.FromResult(LastOutcome = new CapabilityState.Available("recorded"));
    }

    /// <inheritdoc/>
    public Task<CapabilityState> StopAllAsync()
    {
        StopAlls++;
        StateAtStopAll ??= ObserveAtStopAll?.Invoke();
        return Task.FromResult(LastOutcome = new CapabilityState.Available("recorded all-stop"));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        StateAtDispose ??= ObserveAtDispose?.Invoke();
        Disposed = true;
    }

    /// <summary>One level-set, verbatim.</summary>
    /// <param name="DeviceKey">The key the caller addressed.</param>
    /// <param name="Outputs">The list the caller handed over, by reference.</param>
    internal sealed record Command(string DeviceKey, IReadOnlyList<HapticOutput> Outputs);
}
