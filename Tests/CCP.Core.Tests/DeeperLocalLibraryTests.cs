using System;
using System.IO;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Core.Tests;

public class DeeperLocalLibraryTests
{
    [Fact]
    public void Scan_SkipsMalformedAndInvalidEntries()
    {
        WithTempDirectory(root =>
        {
            WriteEnhancement(root, "valid.ccpenh.json", new Enhancement
            {
                MediaType = MediaTypes.Audio,
                Metadata = new EnhancementMetadata { Name = "Valid", Creator = "author" },
            });
            File.WriteAllText(Path.Combine(root, "broken.ccpenh.json"), "{ not json");
            WriteEnhancement(root, "invalid.ccpenh.json", new Enhancement { MediaType = "other" });

            var result = DeeperLocalLibrary.Scan(root);

            Assert.False(result.HasError);
            Assert.Single(result.Entries);
            Assert.Equal("Valid", result.Entries[0].Name);
            Assert.Equal(2, result.SkippedCount);
        });
    }

    [Fact]
    public void Scan_UsesCanonicalDirectPathsAndCaseInsensitiveSuffix()
    {
        WithTempDirectory(root =>
        {
            var nested = Directory.CreateDirectory(Path.Combine(root, "nested")).FullName;
            WriteEnhancement(root, "top.CCPENH.JSON", new Enhancement { Metadata = new EnhancementMetadata { Name = "Top" } });
            WriteEnhancement(nested, "nested.ccpenh.json", new Enhancement { Metadata = new EnhancementMetadata { Name = "Nested" } });
            File.WriteAllText(Path.Combine(root, "ignored.json"), "{}");

            var result = DeeperLocalLibrary.Scan(Path.Combine(root, "."));

            Assert.Single(result.Entries);
            Assert.Equal("Top", result.Entries[0].Name);
            Assert.Equal(Path.GetFullPath(Path.Combine(root, "top.CCPENH.JSON")), result.Entries[0].FilePath);
        });
    }

    [Fact]
    public void Scan_MissingFolderIsEmptyAndDoesNotCreateIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-deeper-missing-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = DeeperLocalLibrary.Scan(root);
            Assert.False(result.HasError);
            Assert.Empty(result.Entries);
            Assert.False(Directory.Exists(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Scan_FileInsteadOfFolderReportsError()
    {
        var path = Path.Combine(Path.GetTempPath(), "ccp-deeper-file-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(path, "not a folder");
            var result = DeeperLocalLibrary.Scan(path);
            Assert.True(result.HasError);
            Assert.Empty(result.Entries);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Filter_CombinesSearchTypeAndHardwareTags()
    {
        var entries = new[]
        {
            Entry("Video", MediaTypes.Video, "Mira", "haptics"),
            Entry("Audio", MediaTypes.Audio, "Mira", "webcam"),
            Entry("Other", MediaTypes.Video, "Zed"),
        };

        var criteria = new DeeperLocalLibrary.FilterCriteria(
            "mira", DeeperLocalLibrary.MediaTypeFilter.Video, Haptics: true);

        var filtered = DeeperLocalLibrary.Filter(entries, criteria);

        Assert.Single(filtered);
        Assert.Equal("Video", filtered[0].Name);
        Assert.True(DeeperLocalLibrary.Matches(entries[1], criteria with
        {
            MediaType = DeeperLocalLibrary.MediaTypeFilter.Audio,
            Haptics = false,
            Webcam = true,
        }));
    }

    private static DeeperLocalLibrary.Entry Entry(string name, string type, string creator, params string[] tags)
        => new()
        {
            FilePath = Path.Combine(Path.GetTempPath(), name + DeeperLocalLibrary.FileSuffix),
            Name = name,
            MediaType = type,
            Creator = creator,
            AutoTags = tags,
        };

    private static void WriteEnhancement(string folder, string name, Enhancement enhancement)
        => File.WriteAllText(Path.Combine(folder, name), EnhancementSerializer.Save(enhancement));

    private static void WithTempDirectory(Action<string> test)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ccp-deeper-" + Guid.NewGuid().ToString("N"))).FullName;
        try { test(root); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
