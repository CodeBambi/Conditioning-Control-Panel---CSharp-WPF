using System.IO;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

// Callers spell relative paths the Windows way; off Windows '\' must fold to '/' or nothing resolves.
// Rooted paths never reach Normalize: Resolve/ResolveDirectory hand them back untouched.
public sealed class ContentLocatorNormalizeTests
{
    [Fact]
    public void Backslashes_fold_to_the_platform_separator()
    {
        var expected = Path.Combine("Resources", "Audio", "backroom", "words");
        Assert.Equal(expected, ContentLocator.Normalize(@"Resources\Audio\backroom\words"));
        Assert.Equal(expected, ContentLocator.Normalize("Resources/Audio/backroom/words"));
        Assert.Equal(expected, ContentLocator.Normalize(@" \Resources\Audio/backroom\words"));
    }

    [Fact]
    public void Rooted_paths_pass_through_untouched()
    {
        var abs = Path.Combine(Path.GetTempPath(), "x", "clip.mp3");
        Assert.Equal(abs, ContentLocator.Resolve(abs));
        Assert.Equal(abs, ContentLocator.ResolveDirectory(abs));
        if (!System.OperatingSystem.IsWindows()) return;
        foreach (var p in new[] { @"\\server\share\clip.mp3", @"\\?\C:\x\clip.mp3", @"C:\x\clip.mp3" })
        {
            Assert.Equal(p, ContentLocator.Resolve(p));
            Assert.Equal(p, ContentLocator.ResolveDirectory(p));
        }
    }
}
