using System;
using ConditioningControlPanel.Services.JustDrop;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// IB1: the sign-in header rides ONE request in a Just Drop window's life: the start url the host
/// itself navigated to. Asserts are booleans on purpose: a failure never prints the credential.
/// </summary>
public class JustDropHandoffStampTests
{
    private const string Fake = "fake-credential";
    private static readonly Uri Start = new("https://app.cclabs.app/api/auth/desktop-session?unified_id=u1&next=%2Fdashboard%2Fexpress");

    private static bool Carries(JustDropHandoffStamp stamp, string url, WebRequestKind kind = WebRequestKind.Unknown) =>
        stamp.Take(new Uri(url), kind, Fake) == Fake;

    [Fact]
    public void TheStartRequestIsStampedOnce_AndASecondRequestToTheSameUrlIsNot()
    {
        var stamp = new JustDropHandoffStamp(Start);
        Assert.False(stamp.Spent);
        Assert.True(Carries(stamp, Start.AbsoluteUri));
        Assert.True(stamp.Spent);
        Assert.False(Carries(stamp, Start.AbsoluteUri));                          // a page fetch to the same url, later
        Assert.False(Carries(stamp, Start.AbsoluteUri, WebRequestKind.Document)); // even a second document load
    }

    [Fact]
    public void ASubResourceKindIsNeverStamped_AndDoesNotSpendTheStamp()
    {
        var stamp = new JustDropHandoffStamp(Start);
        Assert.False(Carries(stamp, Start.AbsoluteUri, WebRequestKind.SubResource));
        Assert.False(stamp.Spent);
        Assert.True(Carries(stamp, Start.AbsoluteUri, WebRequestKind.Document));
    }

    [Theory]
    [InlineData("https://app.cclabs.app/api/auth/desktop-session")]                                             // the bare path, not the host's own url
    [InlineData("https://app.cclabs.app/api/auth/desktop-session?unified_id=u2&next=%2Fdashboard%2Fexpress")]  // another account
    [InlineData("https://app.cclabs.app/api/auth/desktop-session?unified_id=u1&next=%2F%2Fevil.example")]      // another landing
    [InlineData("https://app.cclabs.app.evil.example/api/auth/desktop-session?unified_id=u1&next=%2Fdashboard%2Fexpress")]
    [InlineData("https://evil.example/api/auth/desktop-session?unified_id=u1&next=%2Fdashboard%2Fexpress")]
    [InlineData("http://app.cclabs.app/api/auth/desktop-session?unified_id=u1&next=%2Fdashboard%2Fexpress")]
    [InlineData("https://app.cclabs.app:8443/api/auth/desktop-session?unified_id=u1&next=%2Fdashboard%2Fexpress")]
    [InlineData("https://user@app.cclabs.app/api/auth/desktop-session?unified_id=u1&next=%2Fdashboard%2Fexpress")]
    [InlineData("https://app.cclabs.app/api/auth/desktop-session/extra?unified_id=u1&next=%2Fdashboard%2Fexpress")]
    [InlineData("https://app.cclabs.app/dashboard/express")]
    public void ALookAlikeIsNotStamped_AndDoesNotSpendTheStamp(string url)
    {
        var stamp = new JustDropHandoffStamp(Start);
        Assert.False(Carries(stamp, url));
        Assert.False(stamp.Spent);
        Assert.True(Carries(stamp, Start.AbsoluteUri));   // the real one still gets it
    }

    [Fact]
    public void TheEngineMayReCaseAnEscape()
    {
        var stamp = new JustDropHandoffStamp(Start);
        Assert.True(Carries(stamp, "https://app.cclabs.app/api/auth/desktop-session?unified_id=u1&next=%2fdashboard%2fexpress"));
    }

    [Fact]
    public void SignedOut_OrASignedOutStartUrl_EarnsNothing()
    {
        var stamp = new JustDropHandoffStamp(Start);
        Assert.True(stamp.Take(Start, WebRequestKind.Document, "") is null);
        Assert.True(stamp.Take(Start, WebRequestKind.Document, null) is null);
        Assert.False(stamp.Spent);

        // Signed out the window opens on the shop itself: no request in its life is the handoff.
        var shop = new JustDropHandoffStamp(new Uri("https://app.cclabs.app/dashboard/express"));
        Assert.False(Carries(shop, Start.AbsoluteUri));
        Assert.False(Carries(shop, "https://app.cclabs.app/dashboard/express"));
        Assert.False(Carries(new JustDropHandoffStamp(null), Start.AbsoluteUri));
    }
}
