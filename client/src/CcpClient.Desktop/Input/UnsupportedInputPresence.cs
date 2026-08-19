using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Input;

/// <summary>
/// The honest refusal. Used where this build cannot EARN an input-capture claim.
///
/// <para><b>Why a no-op would be the worst stub in the port, not merely a bad one.</b> A stubbed
/// tray icon is visibly absent. A stubbed overlay is a screen with nothing on it. A stubbed audio
/// backend at least stays silent. A stubbed input capture would put a window on the user's desktop,
/// possibly on top and possibly holding the pointer, that answers NOTHING and can be dismissed by
/// nothing — the module would sit there waiting for a reply that cannot arrive. So this class
/// refuses on every path, never reports <see cref="IsPrompting"/> or
/// <see cref="HoldsTheInput"/> true, never reports <see cref="CanReachAUser"/> true, and answers
/// both observations with their <c>NotAsked</c> value rather than a zeroed record: "we did not ask"
/// and "we asked and nobody was there" are different facts.</para>
///
/// <para><b>What a caller must do with it.</b> Not ask the question at all, and report its module as
/// NOT running. A row whose dot stayed lit over a presence that refuses would be claiming to be
/// waiting for an answer from a user it cannot reach.</para>
/// </summary>
public sealed class UnsupportedInputPresence : IInputPresence
{
    private readonly CapabilityReason _reason;

    public UnsupportedInputPresence(string code, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        _reason = new CapabilityReason(code, detail);
    }

    /// <summary>The refusal this presence was constructed with, verbatim.</summary>
    public CapabilityReason Reason => _reason;

    /// <inheritdoc/>
    public bool IsPrompting => false;

    /// <inheritdoc/>
    public bool HoldsTheInput => false;

    /// <inheritdoc/>
    public bool CanReachAUser => false;

    /// <inheritdoc/>
    public CapabilityState? LastPrompt { get; private set; }

    /// <inheritdoc/>
    public InputCaptureObservation LastObservation => InputCaptureObservation.NotAsked;

    /// <inheritdoc/>
    public CapabilityState Prompt(InputPromptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return LastPrompt = new CapabilityState.Unavailable(_reason);
    }

    /// <inheritdoc/>
    public CapabilityState Update(InputPromptContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new CapabilityState.Unavailable(_reason);
    }

    /// <summary>
    /// Refuses, and the refusal matters: reporting <c>Available</c> from a dismissal that dismissed
    /// nothing would let a caller's teardown pin read green on a build that never asked anything.
    /// </summary>
    public CapabilityState Dismiss() => new CapabilityState.Unavailable(_reason);

    /// <inheritdoc/>
    public InputCaptureObservation Observe() => InputCaptureObservation.NotAsked;

    /// <inheritdoc/>
    public InputStationObservation ObserveStation() => InputStationObservation.NotAsked;

    /// <summary>Nothing to pump: there is no window and no message loop of this presence's own.</summary>
    public int Pump(int maxMessages) => 0;

    public void Dispose()
    {
        // Nothing was ever acquired, and nothing here reports success.
    }
}
