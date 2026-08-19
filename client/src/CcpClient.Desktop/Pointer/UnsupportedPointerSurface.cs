using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Pointer;

/// <summary>
/// The honest refusal. Used where this build cannot EARN a pointer-target claim.
///
/// <para><b>Why a no-op here is worse than a no-op anywhere else in the port.</b> A stubbed tray
/// icon is visibly absent; a stubbed overlay is a screen with nothing on it; a stubbed audio backend
/// stays silent; a stubbed input capture at least LOOKS wrong to the user, because a card is sitting
/// there. <b>A stubbed pointer surface is invisible in both directions.</b> The module would report
/// targets on screen and count zero pops for ever, and the honest reading of that — "the user is not
/// playing" — is indistinguishable from "the user cannot play". Worse, a half-stub that put a window
/// up without the non-activating style would steal the foreground from whatever the user was really
/// doing, on a click they aimed at something else.</para>
///
/// <para><b>What a caller must do with it.</b> Report the module's interactive half as absent, in
/// plain words, and never light a dot over it.</para>
/// </summary>
public sealed class UnsupportedPointerSurface : IPointerSurface
{
    private readonly CapabilityReason _reason;

    public UnsupportedPointerSurface(string code, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        _reason = new CapabilityReason(code, detail);
    }

    /// <summary>The refusal this surface was constructed with, verbatim.</summary>
    public CapabilityReason Reason => _reason;

    /// <inheritdoc/>
    public Action<PointerPress>? OnPress { get; set; }

    /// <inheritdoc/>
    public bool CanReachAPointer => false;

    /// <inheritdoc/>
    public int OpenTargets => 0;

    /// <inheritdoc/>
    public CapabilityState? LastPlacement { get; private set; }

    /// <inheritdoc/>
    public int MouseActivateQueries => 0;

    /// <inheritdoc/>
    public int MouseActivateRefusals => 0;

    /// <inheritdoc/>
    public int PressesSeen => 0;

    /// <inheritdoc/>
    public PointerStationObservation ObserveStation() => PointerStationObservation.NotAsked;

    /// <inheritdoc/>
    public CapabilityState Open(PointerTargetRequest request, out int target)
    {
        ArgumentNullException.ThrowIfNull(request);
        target = 0;
        return LastPlacement = new CapabilityState.Unavailable(_reason);
    }

    /// <inheritdoc/>
    public CapabilityState Move(int target, PointerBounds bounds) =>
        LastPlacement = new CapabilityState.Unavailable(_reason);

    /// <summary>
    /// Refuses, and the refusal matters: reporting <c>Available</c> from a close that closed nothing
    /// would let a caller's teardown pin read green on a build that never placed anything.
    /// </summary>
    public CapabilityState Close(int target) => new CapabilityState.Unavailable(_reason);

    /// <inheritdoc/>
    public PointerTargetObservation Observe(int target) => PointerTargetObservation.NotAsked;

    /// <summary>Nothing to pump: there is no window and no message loop of this surface's own.</summary>
    public int Pump(int max) => 0;

    public void Dispose()
    {
        // Nothing was ever acquired, and nothing here reports success.
    }
}
