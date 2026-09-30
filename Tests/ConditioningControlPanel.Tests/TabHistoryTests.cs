using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Mouse back / forward between tabs (ccp-bugs #1290).</summary>
public class TabHistoryTests
{
    [Fact]
    public void BackAndForward_WalkTheOrder()
    {
        var h = new TabHistory("settings");
        h.Navigate("quests");
        h.Navigate("appsettings");

        Assert.Equal("quests", h.Back());
        Assert.Equal("settings", h.Back());
        Assert.Null(h.Back());
        Assert.Equal("settings", h.Current);

        Assert.Equal("quests", h.Forward());
        Assert.Equal("appsettings", h.Forward());
        Assert.Null(h.Forward());
    }

    [Fact]
    public void NewNavigation_ClearsForward()
    {
        var h = new TabHistory("settings");
        h.Navigate("quests");
        h.Back();
        Assert.True(h.CanGoForward);

        h.Navigate("play");
        Assert.False(h.CanGoForward);
        Assert.Equal("settings", h.Back());
    }

    [Fact]
    public void SameTab_IsNotAMove()
    {
        var h = new TabHistory("settings");
        h.Navigate("quests");
        h.Back();
        h.Navigate("settings");
        Assert.True(h.CanGoForward);
        Assert.Equal(0, h.BackCount);
    }

    [Fact]
    public void Cap_DropsTheOldest()
    {
        var h = new TabHistory("t0", cap: 3);
        for (var i = 1; i <= 6; i++) h.Navigate("t" + i);
        Assert.Equal(3, h.BackCount);
        Assert.Equal("t5", h.Back());
        Assert.Equal("t4", h.Back());
        Assert.Equal("t3", h.Back());
        Assert.Null(h.Back());
    }

    [Fact]
    public void DefaultCap_IsThirty()
    {
        var h = new TabHistory("start");
        for (var i = 0; i < 50; i++) h.Navigate("t" + i);
        Assert.Equal(30, h.BackCount);
    }

    [Fact]
    public void Keys_AreOpaque()
    {
        var h = new TabHistory();
        h.Navigate("Settings");
        h.Navigate("settings");
        Assert.Equal("Settings", h.Back());
    }

    [Fact]
    public void EmptyKey_IsIgnored()
    {
        var h = new TabHistory("settings");
        h.Navigate("");
        h.Navigate(null);
        Assert.False(h.CanGoBack);
    }
}
