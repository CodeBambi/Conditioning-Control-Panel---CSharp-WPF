using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// The <b>session feature lock</b>'s one sentence: why a dial the user just tried to drag has gone
/// grey.
///
/// <para>Upstream's whole feature is <c>MainWindow/MainWindow.SessionFeatureLock.cs</c> (531 lines)
/// plus <c>Features/SessionLock.cs</c> (223) and 42 <c>SessionLock.Owned</c> attribute sites. What
/// travels here is the part that is TEXT; the classification travels as a style class on each
/// control (the port of the attached property), and the painting lives on the page.</para>
///
/// <para><b>Never blank while the lock is up</b>, which is upstream's stated rule rather than a
/// nicety: "a greyed-out control with no explanation reads as a bug"
/// (<c>MainWindow.SessionFeatureLock.cs:104-106</c>).</para>
///
/// <para><b>The tooltip half of upstream's lock is deliberately NOT ported</b>, and its own source
/// is why. WPF hides tooltips on disabled controls unless <c>ToolTipService.ShowOnDisabled</c> is
/// set, and <c>Features/SessionLock.cs:134-136</c> records that shipping without it meant "the
/// explanation never appears at all - that was the state of the shipped program lock". Avalonia
/// has no <c>ShowOnDisabled</c>, so a borrowed tooltip here would be exactly the defect upstream
/// describes: an explanation nobody can reach, plus the two tooltip-destroying traps
/// (<c>SessionLock.cs:99-113</c>) taken on for nothing. The explanation is a docked banner instead,
/// which is on screen the whole time the lock is and needs no hover to find.</para>
/// </summary>
public static class SessionLockNotices
{
    /// <summary>
    /// Upstream's generic line, verbatim (<c>en.json:4110</c>, <c>session_lock_reason</c>, reached
    /// from <c>MainWindow.SessionFeatureLock.cs:112</c> for any session that is not a training
    /// program — which, in this port, is every session there is).
    /// </summary>
    public const string GenericReason =
        "You're in a session! Its features and intensity are locked until it ends.";

    /// <summary>
    /// The banner's words for the session that is actually running.
    ///
    /// <para>Upstream branches here to name the TRAINING PROGRAM when there is one, and says why:
    /// "The reason string still names the program when there is one - it is better copy"
    /// (<c>MainWindow.SessionFeatureLock.cs:27</c>). This port has no training programs, so the
    /// thing worth naming is the session, and the sentence is upstream's own no-day template with
    /// the session in the program's place (<c>en.json:3992</c>,
    /// <c>program_lock_reason_no_day</c>: "{0} is running this. Its features and intensity are
    /// locked until the session ends."). A session with no name falls back to
    /// <see cref="GenericReason"/> rather than printing an empty subject.</para>
    /// </summary>
    public static string Reason(ScriptedSession? session) =>
        string.IsNullOrWhiteSpace(session?.Name)
            ? GenericReason
            : $"{session.Name} is running this. Its features and intensity are locked until the "
                + "session ends.";
}
