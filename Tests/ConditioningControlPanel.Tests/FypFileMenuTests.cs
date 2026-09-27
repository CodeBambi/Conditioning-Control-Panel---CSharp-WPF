using System.IO;
using ConditioningControlPanel.Services.Fyp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Just For You right-click "Open file" / "Show in folder" (ccp-bugs #1290).</summary>
public class FypFileMenuTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ccp-fyp-root");

    [Fact]
    public void LibraryId_ResolvesUnderTheRoot()
    {
        var path = FypFileMenu.ResolveLocal(Root, "videos/sub dir/clip one.mp4");
        Assert.Equal(Path.Combine(Root, "videos", "sub dir", "clip one.mp4"), path);
    }

    [Fact]
    public void OnlineId_HasNoFile()
    {
        Assert.Null(FypFileMenu.ResolveLocal(Root, "scrolller/sissyhypno/abc"));
    }

    [Theory]
    [InlineData("../outside.mp4")]
    [InlineData("videos/../../outside.mp4")]
    [InlineData("C:/Windows/notepad.exe")]
    [InlineData("videos/a\".mp4")]
    [InlineData("")]
    [InlineData(null)]
    public void UnsafeIds_AreRefused(string? id)
    {
        Assert.Null(FypFileMenu.ResolveLocal(Root, id));
    }

    [Fact]
    public void SiblingFolderWithTheSamePrefix_IsRefused()
    {
        // "ccp-fyp-root2" starts with "ccp-fyp-root"; the separator check must catch it.
        Assert.Null(FypFileMenu.ResolveLocal(Root, "../ccp-fyp-root2/x.mp4"));
    }
}
