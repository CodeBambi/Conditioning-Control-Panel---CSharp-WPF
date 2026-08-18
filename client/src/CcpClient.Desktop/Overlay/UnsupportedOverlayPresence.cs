using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Overlay;

/// <summary>
/// The honest refusal. Used where this build has no overlay backend that can drive the platform.
///
/// <para><b>What makes it honest rather than a stub.</b> The first attempt's Linux overlay is the
/// stub: <c>CCP.Avalonia.Desktop.Linux/Platform/LinuxOverlaySurface.cs:9-78</c> wraps every
/// operation in a catch that logs and continues — its own doc calls this a "never-throw seam"
/// where "overlay operations degrade to logged no-ops" — and the selector behind it
/// (<c>LinuxOverlayBackendSelector.cs:41-88</c>) is "guaranteed never to throw and never to return
/// null", terminating in a <c>FallbackBackend</c> that "makes zero P/Invokes and always
/// constructs". A Linux box with no usable display server therefore got an overlay that reported
/// success on every call, forever, and drew nothing.</para>
///
/// <para>This class does the opposite in every particular: it returns
/// <see cref="CapabilityState.Unavailable"/> with a code and a detail naming what would have to
/// exist and the manual gate that would settle it, it NEVER reports <see cref="IsPresenting"/>
/// true, and it refuses <see cref="Withdraw"/> and <see cref="SetClickThrough"/> as well as
/// <see cref="Present"/> — there is no path through it that a caller can mistake for a surface on
/// screen.</para>
/// </summary>
public sealed class UnsupportedOverlayPresence : IOverlayPresence
{
    private readonly CapabilityReason _reason;

    public UnsupportedOverlayPresence(string code, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        _reason = new CapabilityReason(code, detail);
    }

    public bool IsPresenting => false;

    /// <summary>The refusal this presence was constructed with, verbatim.</summary>
    public CapabilityReason Reason => _reason;

    public CapabilityState Present(OverlaySurfaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new CapabilityState.Unavailable(_reason);
    }

    public CapabilityState SetClickThrough(bool clickThrough) => new CapabilityState.Unavailable(_reason);

    public CapabilityState Withdraw() => new CapabilityState.Unavailable(_reason);

    public void Dispose()
    {
        // Nothing was ever acquired. Deliberately empty, and deliberately not reporting success
        // anywhere else in this class.
    }
}
