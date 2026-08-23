using CcpClient.Desktop.Session;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The session feature lock's one sentence. Pure text, so it belongs here rather than in the
/// headless project — the same split every other panel's prose already takes.
/// </summary>
public class SessionLockNoticeTests
{
    /// <summary>
    /// Upstream names the thing that is running because it is better copy
    /// (<c>MainWindow/MainWindow.SessionFeatureLock.cs:27</c>), and its template is
    /// <c>program_lock_reason_no_day</c> (<c>en.json:3992</c>). This port has no training programs,
    /// so the subject is the session.
    /// </summary>
    [Fact]
    public void TheReasonNamesTheSessionThatIsRunning()
    {
        var session = new ScriptedSession { Id = "morning_drift", Name = "Morning Drift" };

        Assert.Equal(
            "Morning Drift is running this. Its features and intensity are locked until the session ends.",
            SessionLockNotices.Reason(session));
    }

    /// <summary>
    /// No session, or one with no name, falls back to upstream's generic line verbatim
    /// (<c>en.json:4110</c>) rather than printing a sentence with an empty subject. Upstream's rule
    /// is that the reason is never blank while the lock is up
    /// (<c>MainWindow.SessionFeatureLock.cs:104-106</c>).
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ANamelessSessionFallsBackToUpstreamsGenericLine(string name)
    {
        Assert.Equal(
            "You're in a session! Its features and intensity are locked until it ends.",
            SessionLockNotices.Reason(new ScriptedSession { Id = "x", Name = name }));
        Assert.Equal(SessionLockNotices.GenericReason, SessionLockNotices.Reason(null));
    }
}
