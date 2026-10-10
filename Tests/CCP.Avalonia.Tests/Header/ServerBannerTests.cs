using System;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests.Header;

/// <summary>WPF MainWindow.Marquee.cs CheckServerUpdateBanner / CheckServerAnnouncement rules, ported
/// in MainShellWindow.ServerBanners.cs (the port had neither).</summary>
public sealed class ServerBannerTests
{
    private static readonly Version Current = new(7, 1, 5);

    [Fact]
    public void UpdateBannerLightsOnlyForANewerVersion()
    {
        Assert.NotNull(MainShellWindow.ParseUpdateBanner("{\"enabled\":true,\"version\":\"7.2.0\",\"url\":\"https://x\"}", Current));
        Assert.Null(MainShellWindow.ParseUpdateBanner("{\"enabled\":true,\"version\":\"7.1.5\"}", Current));
        Assert.Null(MainShellWindow.ParseUpdateBanner("{\"enabled\":true,\"version\":\"7.0.5\"}", Current));
        Assert.Null(MainShellWindow.ParseUpdateBanner("{\"enabled\":false,\"version\":\"9.0.0\"}", Current));
        Assert.Null(MainShellWindow.ParseUpdateBanner("{\"enabled\":true}", Current));
        Assert.Null(MainShellWindow.ParseUpdateBanner("not json", Current));
        Assert.Equal("https://x", MainShellWindow.ParseUpdateBanner("{\"enabled\":true,\"version\":\"8.0\",\"url\":\"https://x\"}", Current)!.Url);
    }

    [Fact]
    public void AnnouncementShowsOncePerId()
    {
        const string json = "{\"enabled\":true,\"id\":\"a1\",\"title\":\"T\",\"message\":\"m\",\"image_url\":null,\"link_url\":\"https://l\",\"theme\":\"matrix\"}";
        var r = MainShellWindow.ParseAnnouncement(json, null);
        Assert.NotNull(r);
        Assert.Equal("https://l", r!.LinkUrl);
        Assert.Equal("matrix", r.Theme);
        Assert.Null(MainShellWindow.ParseAnnouncement(json, "a1"));
        Assert.Null(MainShellWindow.ParseAnnouncement("{\"enabled\":true,\"id\":\"a2\"}", null));
        Assert.Null(MainShellWindow.ParseAnnouncement("{\"enabled\":false,\"id\":\"a2\",\"title\":\"T\"}", null));
    }

    [Fact]
    public void InboxSummaryIsOneShortLine()
    {
        Assert.Equal("a b c", MainShellWindow.SummariseForInbox("a\r\nb   c"));
        var s = MainShellWindow.SummariseForInbox(new string('x', 200));
        Assert.Equal(90, s.Length);
        Assert.EndsWith("…", s);
    }

    [Fact]
    public void BuildReportsTheSharedVersion() =>
        Assert.True(MainShellWindow.CurrentAppVersion() >= new Version(7, 1, 5));
}
