namespace CcpClient.Desktop.Tray;

/// <summary>
/// A balloon on the tray icon.
///
/// <para><b>The one the port actually sends, and the comment-versus-code trap it comes from.</b>
/// WPF's DTRH tuck is <c>MainWindow.MinimizeToTrayForChaos()</c>, whose XML comment says
/// <i>"No notification (the game window is the focus)"</i>
/// (<c>MainWindow/MainWindow.RemoteControl.cs:1515</c>). The body is
/// <c>_trayIcon?.MinimizeToTray()</c> (<c>:1517</c>), and <c>MinimizeToTray</c> fires a balloon on
/// its FIRST-EVER invocation in the process
/// (<c>Services/Notifications/TrayIconService.cs:150-157</c>, latched by
/// <c>_hasShownFirstMinimizeNotification</c> at <c>:23</c>). So a user whose first minimize of the
/// session is a DTRH launch does get a balloon, the comment notwithstanding. The port reproduces
/// the CODE: <see cref="FirstMinimize"/> carries WPF's exact title, text and 2000 ms timeout, and
/// <c>ShellTray</c> latches it the same way. Recorded as wpf-surface-reachability.md §10 D20 @ 7527243e7.</para>
/// </summary>
public sealed class TrayNotification
{
    /// <summary><c>NOTIFYICONDATAW.szInfoTitle</c> is <c>WCHAR[64]</c>: 63 characters plus the
    /// terminator. Clamped here for the same reason the tooltip is
    /// (<see cref="TrayIconRequest.ToolTipBudget"/>) — a marshalling throw is a worse way to learn
    /// a string was too long than a visible flag on the request.</summary>
    public const int TitleBudget = 63;

    /// <summary><c>NOTIFYICONDATAW.szInfo</c> is <c>WCHAR[256]</c>: 255 characters plus the terminator.</summary>
    public const int MessageBudget = 255;

    public TrayNotification(string title, string message, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(message);
        Title = title.Length > TitleBudget ? title[..TitleBudget] : title;
        Message = message.Length > MessageBudget ? message[..MessageBudget] : message;
        WasClamped = title.Length > TitleBudget || message.Length > MessageBudget;
        Timeout = timeout;
    }

    public string Title { get; }

    public string Message { get; }

    /// <summary>WPF passes 2000 ms (<c>TrayIconService.cs:156</c>). Carried verbatim and passed to
    /// the shell. Vista and later let the accessibility timeout override it, so this is the
    /// REQUEST, never a promise about how long anything is on screen.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>True when the title or the message did not fit and was cut.</summary>
    public bool WasClamped { get; }

    /// <summary>
    /// WPF's first-minimize balloon, verbatim: title, body and the 2000 ms timeout from
    /// <c>Services/Notifications/TrayIconService.cs:155-157</c>.
    /// </summary>
    public static TrayNotification FirstMinimize { get; } = new(
        "Conditioning Control Panel",
        "Running in background. Click the tray icon to restore.",
        TimeSpan.FromMilliseconds(2000));
}
