using System;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The Chaster chip on the tab: where it goes, and how short its clock reads.</summary>
public class ChasterWebLinksTests
{
    private static LockSnapshot Lock(string id) =>
        new(id, "Self-lock", null, false, false, false, DateTime.UtcNow);

    [Fact]
    public void ALockOpensItsOwnPage() =>
        Assert.Equal("https://chaster.app/locks/6ab36daef3f717dc1093da95", ChasterWebLinks.For(Lock("6ab36daef3f717dc1093da95")));

    [Theory]
    [InlineData("")]
    [InlineData("../admin")]
    [InlineData("abc?x=1")]
    [InlineData("https://evil.example")]
    public void AnythingButAPlainIdGoesHome(string id) =>
        Assert.Equal(ChasterWebLinks.Home, ChasterWebLinks.For(Lock(id)));

    [Fact]
    public void NoLockGoesHome() => Assert.Equal(ChasterWebLinks.Home, ChasterWebLinks.For(null));

    [Theory]
    [InlineData(3 * 86400 + 4 * 3600 + 59, "3d 4h")]
    [InlineData(4 * 3600 + 12 * 60, "4h 12m")]
    [InlineData(12 * 60 + 30, "12m")]
    [InlineData(-5, "0m")]
    public void ShortClock(int seconds, string expected) =>
        Assert.Equal(expected, ChasterWebLinks.Short(TimeSpan.FromSeconds(seconds)));
}
