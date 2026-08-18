namespace CcpClient.Desktop.Tray;

/// <summary>
/// What the caller wants placed in the notification area. Deliberately tiny: this capability's
/// subject is "an icon the user can click to get the window back", not a menu framework.
///
/// <para>WPF parity anchor: <c>Services/Notifications/TrayIconService.cs:44</c> sets the
/// NotifyIcon's <c>Text</c> to "Conditioning Control Panel" — that is the tooltip the shipping
/// product shows, so it is <see cref="Default"/> here.</para>
/// </summary>
public sealed class TrayIconRequest
{
    /// <summary>
    /// The shell's tooltip budget. <c>NOTIFYICONDATAW.szTip</c> is <c>WCHAR[128]</c>, so 127
    /// characters plus the terminator. Clamped here rather than at the marshalling boundary
    /// because a silently truncated (or marshal-thrown) tooltip is a failure mode the caller
    /// should never have to discover from a native exception.
    /// </summary>
    public const int ToolTipBudget = 127;

    public TrayIconRequest(string toolTip)
    {
        ArgumentNullException.ThrowIfNull(toolTip);
        ToolTip = toolTip.Length > ToolTipBudget ? toolTip[..ToolTipBudget] : toolTip;
        ToolTipWasClamped = toolTip.Length > ToolTipBudget;
    }

    /// <summary>The hover text, already clamped to <see cref="ToolTipBudget"/>.</summary>
    public string ToolTip { get; }

    /// <summary>True when the caller's tooltip did not fit and was cut. Surfaced rather than
    /// swallowed so a caller can see its text was altered.</summary>
    public bool ToolTipWasClamped { get; }

    /// <summary>The product's own tray identity (WPF <c>TrayIconService.cs:44</c>).</summary>
    public static TrayIconRequest Default { get; } = new("Conditioning Control Panel");
}
