namespace CcpClient.Desktop.Features.Arcademy;

/// <summary>
/// <b>THE DOOR IS SHUT, AND THAT IS THE PORTED FACT.</b> One flag, defaulting closed, standing
/// where upstream's stands: <c>ArcademyHostService.DoorAvailable</c>
/// (<c>ConditioningControlPanel/Services/Arcademy/ArcademyHostService.cs:116</c>) is a
/// <c>static readonly bool = false</c>, its Play card ships <c>Visibility="Collapsed"</c>
/// (<c>Views/Tabs/PlayTabView.xaml:1312</c>) and is re-asserted from that flag on every repaint
/// (<c>MainWindow/MainWindow.PlayTab.cs:106-112</c>), and <c>Launch()</c> refuses on the door
/// BEFORE the T2 bar (<c>:136-141</c>).
///
/// <para><b>Why closed here.</b> Upstream states its own reason at <c>:115-121</c>: Semester 1
/// landed on main ahead of its public reveal and 6.8.4 is an auth/stability patch that must not
/// also ship an unannounced feature — a HIDE, not a lockband, "because a lockband advertises
/// something the account could buy". A port that shipped this surface VISIBLE would release a
/// feature the upstream product is deliberately withholding, so the port withholds it too.
/// Flipping <see cref="Available"/> is the whole reveal, exactly as upstream documents at
/// <c>:122-123</c>; the gates behind it (T2, audio-only) are slice 8's and are untouched by it.</para>
///
/// <para><b><c>static readonly</c>, never <c>const</c></b> — upstream's own remark at
/// <c>:124-125</c>: a <c>const</c> would make the refusal in the launch path compile-time
/// unreachable (CS0162), which in this build is also a warning the gate would fail on.</para>
///
/// <para><b>There is no override seam and that is deliberate.</b> A door a caller can open is
/// not a door. Nothing in this build — no page, no route, no card, no argument, no environment
/// variable — sets this to <c>true</c>.</para>
/// </summary>
public static class ArcademyDoor
{
    /// <summary>Display name for gates, dialogs and the window title
    /// (<c>ArcademyHostService.ProductName</c>, <c>:38</c>).</summary>
    public const string ProductName = "The Arcademy";

    /// <summary>Is there an Arcademy door at all. Port of
    /// <c>ArcademyHostService.DoorAvailable</c> (<c>:116</c>).</summary>
    public static readonly bool Available = false;

    /// <summary>
    /// The refusal, typed. Upstream's own refusal is SILENT (<c>:139-141</c>: a debug log and a
    /// return, because "there is no announced feature to explain a refusal about yet"), so this
    /// carries no user-facing sentence either — <see cref="Reason"/> is transcript text, and
    /// nothing in this build renders it.
    /// </summary>
    /// <param name="Reason">Why the launch opened nothing.</param>
    public sealed record ArcademyDoorRefusal(string Reason);

    /// <summary>The one refusal this build can produce, worded as upstream's log line
    /// (<c>:141</c>).</summary>
    public static readonly ArcademyDoorRefusal Refusal =
        new("the Arcademy door is not open yet (unreleased)");
}
