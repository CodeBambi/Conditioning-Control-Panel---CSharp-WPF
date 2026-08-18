using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Tray;

/// <summary>
/// A tray presence: an icon in the platform's notification area, and the user's way back to a
/// window that has left the taskbar.
///
/// <para><b>The contract that makes this capability worth having.</b> Every method returns a
/// typed <see cref="CapabilityState"/> (SP-006, <c>runtime-capability-contract.md</c> §1), and
/// <see cref="CapabilityState.Available"/> is returned ONLY after the platform's tray mechanism
/// was really invoked and really confirmed the effect. A backend that cannot place an icon
/// returns <see cref="CapabilityState.Unavailable"/> with a <see cref="TrayReasonCodes"/> code —
/// it never returns a success that means "the method ran". Contract §4 rule 3 in this
/// capability's own words: a no-op keeps the app alive, and it reports Unavailable while doing
/// so.</para>
///
/// <para><b>What a caller does with Unavailable.</b> Fall back to a plain window minimize that
/// keeps the taskbar button — the shape the port already ships and documents at
/// <c>Features/Intake/IntakeHostWindow.axaml.cs:131</c> ("Plain MainWindow minimize (explicitly
/// NOT tray tuck)"), including its record-the-prior-state restore at <c>:134</c>/<c>:148</c>.
/// A caller must NEVER respond to Unavailable by hiding the window anyway: WPF's tuck
/// (<c>Services/Notifications/TrayIconService.cs:145-148</c>) is <c>Hide()</c> PLUS a visible
/// icon, and taking the first half without the second strands the user with a running app that
/// has left the taskbar and has no icon to bring it back.</para>
///
/// <para><b>Thread affinity.</b> A backend may own a native window. Call
/// <see cref="Place"/>/<see cref="Remove"/>/<see cref="IDisposable.Dispose"/> on one thread —
/// the UI thread in the app — and expect <see cref="Activated"/> on that same thread, delivered
/// only while that thread pumps its message queue.</para>
/// </summary>
public interface ITrayPresence : IDisposable
{
    /// <summary>
    /// Places the icon (or updates an already-placed one). <see cref="CapabilityState.Available"/>
    /// means the icon is in the notification area right now and the backend confirmed it with the
    /// platform, not that a call returned.
    /// </summary>
    CapabilityState Place(TrayIconRequest request);

    /// <summary>
    /// Takes the icon back out. <see cref="CapabilityState.Available"/> means the platform
    /// confirmed the icon is gone.
    /// </summary>
    CapabilityState Remove();

    /// <summary>
    /// Installs the menu a right-click on the icon opens (SP-096; WPF builds its equivalent once at
    /// <c>Services/Notifications/TrayIconService.cs:95-110</c>).
    /// <see cref="CapabilityState.Available"/> means the platform really holds a menu with these
    /// entries — a backend must ask the platform BACK for the menu it just built and compare, the
    /// same discipline <see cref="Place"/> obeys with its <c>NIM_MODIFY</c> round trip. It never
    /// means "the append calls returned".
    ///
    /// <para><b>What it still cannot mean.</b> That the menu is drawn, or that a human's
    /// right-click reaches it: on Windows that needs <c>TrackPopupMenu</c>'s modal loop and a real
    /// pointer, which is a headed claim no headless run discharges.</para>
    ///
    /// <para>Independent of <see cref="Place"/> and orderable either way: a menu on no icon is
    /// unreachable but harmless, and an icon with no menu is exactly the SP-093 shape SP-094
    /// refused to tuck onto.</para>
    /// </summary>
    CapabilityState SetMenu(TrayMenu menu);

    /// <summary>
    /// Asks the platform to show a balloon on this presence's icon (WPF
    /// <c>TrayIconService.cs:156</c> / <c>:225</c>). Requires a confirmed-placed icon; without one
    /// there is nothing to attach a balloon to and the answer is
    /// <see cref="CapabilityState.Unavailable"/>.
    ///
    /// <para><b>The honest ceiling on Available here.</b> A backend can confirm that the platform
    /// ACCEPTED the request for an icon it confirms holding. It cannot confirm a balloon was drawn
    /// — Windows suppresses notifications under Focus Assist, quiet hours, a full-screen app and
    /// the user's own per-app notification switch, and reports none of that back. So the detail
    /// says what was confirmed, and visible display stays a headed claim.</para>
    /// </summary>
    CapabilityState ShowNotification(TrayNotification notification);

    /// <summary>True only while an icon this presence placed is confirmed present.</summary>
    bool IsPlaced { get; }

    /// <summary>
    /// The user asked for the window back (left-click or double-click on the icon — WPF admits
    /// both, and single-click deliberately, because "clicking the tray icon does nothing" reads
    /// as the app being gone: <c>TrayIconService.cs:112-119</c>). Never raised by a backend that
    /// has no icon.
    /// </summary>
    event EventHandler? Activated;
}
