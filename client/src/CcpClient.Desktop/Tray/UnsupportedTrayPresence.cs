using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Tray;

/// <summary>
/// The honest refusal. Used where this build has no tray backend that can drive the platform.
///
/// <para><b>What makes it honest rather than a stub.</b> A stub swallows the call and returns
/// success; this returns <see cref="CapabilityState.Unavailable"/> with a code and a detail
/// naming what would have to exist, and it NEVER reports <see cref="IsPlaced"/> true, never
/// raises <see cref="Activated"/>, and refuses <see cref="Remove"/> as well as
/// <see cref="Place"/> — there is no path through this class that a caller can mistake for a
/// placed icon. That distinction is the whole point: a caller that sees Unavailable falls back
/// to a plain window minimize that keeps the taskbar button
/// (<c>Features/Intake/IntakeHostWindow.axaml.cs:131</c>), and a caller that is lied to hides
/// its window and strands the user.</para>
/// </summary>
public sealed class UnsupportedTrayPresence : ITrayPresence
{
    private readonly CapabilityReason _reason;

    public UnsupportedTrayPresence(string code, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        _reason = new CapabilityReason(code, detail);
    }

    /// <summary>Never raised. Declared because the interface promises it, and an event nobody
    /// invokes is the truthful shape for a presence that has no icon to click.</summary>
    public event EventHandler? Activated
    {
        add { }
        remove { }
    }

    public bool IsPlaced => false;

    /// <summary>The refusal this presence was constructed with, verbatim.</summary>
    public CapabilityReason Reason => _reason;

    public CapabilityState Place(TrayIconRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new CapabilityState.Unavailable(_reason);
    }

    public CapabilityState Remove() => new CapabilityState.Unavailable(_reason);

    /// <summary>
    /// Refuses, and this refusal is load-bearing. A caller places the icon only after the menu is
    /// confirmed, so this refusal is what stops a Linux run from ever reaching a placement — and
    /// therefore what stops it from hiding a window behind an icon that cannot exist.
    /// </summary>
    public CapabilityState SetMenu(TrayMenu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        return new CapabilityState.Unavailable(_reason);
    }

    public CapabilityState ShowNotification(TrayNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return new CapabilityState.Unavailable(_reason);
    }

    public void Dispose()
    {
        // Nothing was ever acquired. Deliberately empty, and deliberately not reporting success
        // anywhere else in this class.
    }
}
