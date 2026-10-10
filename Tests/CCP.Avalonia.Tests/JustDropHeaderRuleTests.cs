using System;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.JustDrop;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>IB1: the Just Drop window's header rule as the web host calls it. One stamp in the
/// window's life, on the start url the host itself navigated to. Boolean asserts only: a failure
/// never prints the credential.</summary>
public sealed class JustDropHeaderRuleTests
{
    private const string Fake = "fake-credential";
    private static readonly Uri Start = new("https://app.cclabs.app/api/auth/desktop-session?unified_id=u1&next=%2Fdashboard%2Fexpress");

    private static string? UrlRule(Uri uri) => JustDropHostService.AuthHeaderFor(uri, Fake);

    [Fact]
    public void TheWindowStampsItsStartRequestOnce_ThenNeverAgain()
    {
        var rule = GameWindow.HandoffHeaderRule(new JustDropHandoffStamp(Start), UrlRule);

        Assert.True(rule(new Uri("https://app.cclabs.app/dashboard/express")) is null);                     // the shop itself
        Assert.True(rule(new Uri("https://app.cclabs.app/api/auth/desktop-session")) is null);              // a look-alike of the handoff
        Assert.True(rule(new Uri("https://evil.example/api/auth/desktop-session?unified_id=u1&next=%2Fdashboard%2Fexpress")) is null);

        var first = rule(Start);
        Assert.True(first is { } h && h.Name == JustDropHostService.AuthHeaderName && h.Value == Fake);

        Assert.True(rule(Start) is null);   // a fetch, a frame or a reload asking for the same url later
        Assert.True(rule(Start) is null);
    }

    [Fact]
    public void ASignedOutWindowNeverStamps()
    {
        var rule = GameWindow.HandoffHeaderRule(new JustDropHandoffStamp(new Uri("https://app.cclabs.app/dashboard/express")), UrlRule);
        Assert.True(rule(Start) is null);
        Assert.True(rule(new Uri("https://app.cclabs.app/dashboard/express")) is null);
    }
}
