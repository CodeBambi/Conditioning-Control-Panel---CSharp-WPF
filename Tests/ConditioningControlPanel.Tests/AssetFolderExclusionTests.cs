using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1231: a preset that unticked a folder used to hold only the files that folder had at
/// the time, so a file added later showed up in the preset. The folder is remembered now and new
/// files inherit its state.
/// </summary>
public class AssetFolderExclusionTests
{
    private static HashSet<string> Set(params string[] items) => new(items, StringComparer.OrdinalIgnoreCase);

    [Theory]
    [InlineData("images/a/x.png", "images/a", true)]
    [InlineData("images/a/b/x.png", "images/a", true)]
    [InlineData("IMAGES\\A\\x.png", "images/a", true)]
    [InlineData("images/ab/x.png", "images/a", false)]
    [InlineData("images/a", "images/a", false)]
    [InlineData("videos/a/x.mp4", "images/a", false)]
    public void IsUnderMatchesWholeSegmentsOnly(string path, string folder, bool expected)
        => Assert.Equal(expected, AssetFolderExclusion.IsUnder(path, folder));

    [Fact]
    public void ANewFileInAnUntickedFolderIsExcluded()
    {
        var folders = Set();
        var disabled = Set("images/old.png");
        AssetFolderExclusion.MarkFolder(folders, "images", false);

        var added = AssetFolderExclusion.Expand(disabled, folders, new[] { "images/old.png", "images/new.png", "images/sub/newer.gif", "videos/v.mp4" });

        Assert.Equal(2, added);
        Assert.Contains("images/new.png", disabled);
        Assert.Contains("images/sub/newer.gif", disabled);
        Assert.DoesNotContain("videos/v.mp4", disabled);
    }

    [Fact]
    public void TickingTheFolderAgainForgetsIt()
    {
        var folders = Set();
        AssetFolderExclusion.MarkFolder(folders, "images/a", false);
        AssetFolderExclusion.MarkFolder(folders, "images/a", true);
        Assert.Empty(folders);
    }

    [Fact]
    public void TickingAChildForgetsTheParent()
    {
        var folders = Set();
        AssetFolderExclusion.MarkFolder(folders, "images", false);
        AssetFolderExclusion.MarkFolder(folders, "images/keep", true);
        Assert.Empty(folders);
    }

    [Fact]
    public void UntickingAParentAbsorbsItsChildren()
    {
        var folders = Set();
        AssetFolderExclusion.MarkFolder(folders, "images/a", false);
        AssetFolderExclusion.MarkFolder(folders, "images", false);
        Assert.Equal(new[] { "images" }, folders);
    }

    [Fact]
    public void TickingOneFileBreaksTheFolderRule()
    {
        var folders = Set();
        AssetFolderExclusion.MarkFolder(folders, "images", false);
        AssetFolderExclusion.MarkFolder(folders, "videos", false);
        AssetFolderExclusion.FileEnabled(folders, "images/sub/one.png");
        Assert.Equal(new[] { "videos" }, folders);
    }

    [Fact]
    public void ExpandFromDiskCatchesUpASavedPreset()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-afx-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "images", "sub"));
            File.WriteAllText(Path.Combine(root, "images", "new.png"), "x");
            File.WriteAllText(Path.Combine(root, "images", "sub", "deep.webp"), "x");
            File.WriteAllText(Path.Combine(root, "images", "notes.txt"), "x");

            var disabled = Set();
            var folders = Set("images");
            var added = AssetFolderExclusion.ExpandFromDisk(disabled, folders, root);

            Assert.Equal(2, added);
            Assert.Contains("images/new.png", disabled);
            Assert.Contains("images/sub/deep.webp", disabled);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void NoRememberedFolderMeansNoChange()
    {
        var disabled = Set("images/a.png");
        Assert.Equal(0, AssetFolderExclusion.Expand(disabled, Set(), new[] { "images/b.png" }));
        Assert.Single(disabled);
    }
}
