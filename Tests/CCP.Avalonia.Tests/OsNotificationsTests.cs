using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>OsNotifications fallback: OS when delivered, else toast only while the window is visible, else drop.</summary>
public sealed class OsNotificationsTests
{
    [Theory]
    [InlineData(true, true, "Os")]
    [InlineData(true, false, "Os")]
    [InlineData(false, true, "Toast")]
    [InlineData(false, false, "Drop")]
    public void FallbackDecision(bool delivered, bool visible, string expected) =>
        Assert.Equal(expected, OsNotifications.Decide(delivered, visible).ToString());

    /// <summary>P74 incident: a test host (CCP_USERDATA_DIR sandbox, TestUserDataProfile) put "Someone just connected
    /// to your remote session." on the user's real desktop. With no Sink installed, a sandbox must not touch D-Bus.</summary>
    [Fact]
    public Task Sandbox_never_reaches_the_session_bus() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Assert.True(SandboxNet.Active);
        var (oldSink, before) = (OsNotifications.Sink, OsNotifications.BusAttempts);
        OsNotifications.Sink = null;
        try
        {
            await OsNotifications.ShowAsync("CCP test", "must not reach the desktop", null);
            Assert.Equal(0u, await OsNotifications.NotifyAsync("CCP test", "must not reach the desktop", false));
            Assert.Equal(before, OsNotifications.BusAttempts);
        }
        finally { OsNotifications.Sink = oldSink; }
    });
}
