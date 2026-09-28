using ConditioningControlPanel.Avalonia.Platform;
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
}
