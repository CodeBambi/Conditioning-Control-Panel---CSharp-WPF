using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Haptics;

/// <summary>
/// The honest refusal. Used where this build cannot EARN a haptic claim — which today is
/// everywhere, on every platform.
///
/// <para><b>Why a no-op here is a different kind of wrong from a no-op anywhere else in the
/// port.</b> A stubbed overlay is a screen with nothing on it, a stubbed audio backend is silence, a
/// stubbed pointer target is a click that does nothing. All of those are wrong in ONE direction. A
/// stubbed haptic sink is wrong in both at once: a caller that believed its levels landed would also
/// believe its ALL-STOP landed, and the second belief is the dangerous one. Upstream's shutdown
/// comment is exactly about that asymmetry — <i>"a Lovense level has no server-side watchdog, so a
/// toy we don't countermand keeps running after the app is gone"</i>
/// (<c>ConditioningControlPanel/App.xaml.cs:4401-4404</c>). A sink that reported a successful stop
/// it never performed would be lying about the one operation whose failure a person feels.</para>
///
/// <para><b>So every member refuses, including the stop.</b> <see cref="StopAllAsync"/> does not
/// return <see cref="CapabilityState.Available"/> for having stopped nothing — the same rule
/// <c>Pointer/UnsupportedPointerSurface.Close</c> follows, and for a sharper reason here.</para>
///
/// <para><b>What a caller must do with it.</b> Report the haptic half as ABSENT, in plain words,
/// naming the admitted-provider gap and never a missing device — and never light a dot over it.</para>
/// </summary>
public sealed class UnadmittedHapticSink : IHapticSink
{
    private readonly CapabilityReason _reason;
    private bool _disposed;

    public UnadmittedHapticSink(string code, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        _reason = new CapabilityReason(code, detail);
    }

    /// <summary>The refusal this sink was constructed with, verbatim.</summary>
    public CapabilityReason Reason => _reason;

    /// <inheritdoc/>
    public HapticProviderRoute Route => HapticProviderRoute.None;

    /// <inheritdoc/>
    public CapabilityState? LastOutcome { get; private set; }

    /// <summary>How many times a caller asked this sink to drive something. Diagnostic, and the
    /// evidence behind the participant's claim that it never attempted a connection: a count that
    /// moved would mean the product tried to reach a server it has no client for.</summary>
    public int RefusedCalls { get; private set; }

    /// <inheritdoc/>
    public Task<HapticServerObservation> ObserveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RefusedCalls++;
        // NotAsked, and every field in it is false: nothing was attempted, so nothing may be
        // reported. Classifying this observation yields the admission gap by construction rather
        // than by a second copy of the refusal.
        return Task.FromResult(HapticServerObservation.NotAsked);
    }

    /// <inheritdoc/>
    public Task<CapabilityState> ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RefusedCalls++;
        return Task.FromResult(Refuse());
    }

    /// <inheritdoc/>
    public Task<CapabilityState> SetOutputsAsync(
        string deviceKey, IReadOnlyList<HapticOutput> outputs, CancellationToken cancellationToken)
    {
        // Validated even though nothing is driven: a caller whose bad argument is swallowed by a
        // refusing build discovers it on the day the refusal stops, which is the day a real toy is
        // attached to it.
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKey);
        ArgumentNullException.ThrowIfNull(outputs);
        if (outputs.Count == 0)
        {
            // IHapticSink.SetOutputsAsync says an empty list is a caller error and not a silent
            // no-op, and a contract only one comment believes in is not a contract. It is enforced
            // HERE, in the refusing build, for the same reason the two guards above are: a caller
            // whose mistake is swallowed today discovers it on the day a real device is attached.
            throw new ArgumentException(
                "an empty output list commands nothing; call StopAllAsync to stop, or send a silent level",
                nameof(outputs));
        }

        cancellationToken.ThrowIfCancellationRequested();
        RefusedCalls++;
        return Task.FromResult(Refuse());
    }

    /// <inheritdoc/>
    public Task<CapabilityState> StopAllAsync()
    {
        RefusedCalls++;
        return Task.FromResult(Refuse());
    }

    public void Dispose()
    {
        // Nothing was ever acquired, and nothing here reports success. The flag exists so a use
        // after teardown is answered with the disposal rather than with the admission gap: they are
        // different facts and a caller debugging a teardown ordering bug needs to tell them apart.
        _disposed = true;
    }

    private CapabilityState Refuse() =>
        LastOutcome = _disposed
            ? new CapabilityState.Unavailable(new CapabilityReason(
                HapticReasonCodes.HapticSinkDisposed,
                "this haptic sink was disposed; it holds nothing and will never drive anything again"))
            : new CapabilityState.Unavailable(_reason);
}
