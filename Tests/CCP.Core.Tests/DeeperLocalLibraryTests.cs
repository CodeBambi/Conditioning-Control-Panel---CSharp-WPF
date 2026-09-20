using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Core.Tests;

public class DeeperLocalLibraryTests
{
    [Fact]
    public void Scan_SkipsMalformedButListsParseableValidationInvalidEntries()
    {
        WithTempDirectory(root =>
        {
            WriteEnhancement(root, "valid.ccpenh.json", new Enhancement
            {
                MediaType = MediaTypes.Audio,
                Metadata = new EnhancementMetadata { Name = "Valid", Creator = "author" },
            });
            File.WriteAllText(Path.Combine(root, "broken.ccpenh.json"), "{ not json");
            WriteEnhancement(root, "invalid.ccpenh.json", new Enhancement
            {
                MediaType = MediaTypes.Audio,
                Metadata = new EnhancementMetadata { Name = "Invalid" },
                TimelineItems = { new TimelineItem { Id = "" } },
            });

            var result = DeeperLocalLibrary.Scan(root);

            Assert.False(result.HasError);
            Assert.Equal(2, result.Entries.Count);
            Assert.Contains(result.Entries, entry => entry.Name == "Valid");
            Assert.Contains(result.Entries, entry => entry.Name == "Invalid");
            Assert.Equal(1, result.SkippedCount);
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
    public void SharedFilter_PreservesTagCasingAndDurationDirection()
    {
        var entries = new[]
        {
            Entry("Long", MediaTypes.Video, "Mira", 120, "haptics"),
            Entry("Short", MediaTypes.Video, "Mira", 60, "HAPTICS"),
        };
        var criteria = new EnhancementLibraryFilter.Criteria(
            "mira", EnhancementLibraryFilter.MediaTypeFilter.Video, Haptics: true, Webcam: false);

        Assert.True(EnhancementLibraryFilter.Matches(entries[0], criteria));
        Assert.False(EnhancementLibraryFilter.Matches(entries[1], criteria));
        Assert.Equal(new[] { "Long", "Short" },
            EnhancementLibraryFilter.Sort(entries, EnhancementLibraryFilter.SortMode.Duration, descending: true)
                .Select(entry => entry.Name));
        Assert.Equal(new[] { "Short", "Long" },
            EnhancementLibraryFilter.Sort(entries, EnhancementLibraryFilter.SortMode.Duration, descending: false)
                .Select(entry => entry.Name));
    }

    [Fact]
    public void Scan_PreservesWpfNameFallbackDurationAndTagCasing()
    {
        WithTempDirectory(root =>
        {
            WriteEnhancement(root, "unnamed.ccpenh.json", new Enhancement
            {
                MediaType = MediaTypes.Video,
                Metadata = new EnhancementMetadata { Name = "" },
            });
            WriteEnhancement(root, "tagged.ccpenh.json", new Enhancement
            {
                MediaType = MediaTypes.Video,
                Metadata = new EnhancementMetadata
                {
                    Name = "Tagged",
                    MediaDurationSeconds = 123.5,
                    AutoTags = new() { "HAPTICS" },
                },
            });

            var entries = DeeperLocalLibrary.Scan(root).Entries;
            var unnamed = Assert.Single(entries, entry => entry.FilePath.EndsWith("unnamed.ccpenh.json", StringComparison.OrdinalIgnoreCase));
            var tagged = Assert.Single(entries, entry => entry.Name == "Tagged");
            // WPF uses metadata.Name ?? GetFileNameWithoutExtension; an authored empty name
            // remains empty rather than being replaced with a different suffix-stripping rule.
            Assert.Equal("", unnamed.Name);
            Assert.Equal(0, unnamed.DurationSeconds);
            Assert.Equal(123.5, tagged.DurationSeconds);
            Assert.Equal(new[] { "HAPTICS" }, tagged.AutoTags);
        });
    }

    private static EnhancementLibraryEntry Entry(string name, string type, string creator, double duration, params string[] tags)
        => new()
        {
            FilePath = Path.Combine(Path.GetTempPath(), name + DeeperLocalLibrary.FileSuffix),
            Name = name,
            MediaType = type,
            Creator = creator,
            DurationSeconds = duration,
            AutoTags = tags.ToList(),
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
