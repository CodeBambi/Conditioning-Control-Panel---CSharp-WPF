using System;
using System.IO;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Core.Tests;

// One class so the two static providers are never mutated by parallel tests.
public sealed class CoreHeadProviderTests
{
    [Fact]
    public void EnhancementResolver_library_provider_unset_falls_back_to_not_found_and_set_matches()
    {
        var media = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "clip.mp3");
        try
        {
            EnhancementResolver.LibraryMatchProvider = null;
            Assert.Equal(EnhancementDiscoverySource.None, EnhancementResolver.ResolveForLocalMedia(media).Source);

            EnhancementResolver.LibraryMatchProvider = (_, _) => new EnhancementLibraryEntry { FilePath = "lib.json" };
            var hit = EnhancementResolver.ResolveForLocalMedia(media);
            Assert.Equal(EnhancementDiscoverySource.Library, hit.Source);
            Assert.Equal("lib.json", hit.FilePath);
        }
        finally { EnhancementResolver.LibraryMatchProvider = null; }
    }

    [Fact]
    public void JustDrop_door_provider_is_fail_closed()
    {
        try
        {
            SettingsPaletteIndex.JustDropDoorAvailableProvider = null;
            Assert.False(SettingsPaletteIndex.JustDropDoorAvailable());

            SettingsPaletteIndex.JustDropDoorAvailableProvider = () => throw new InvalidOperationException();
            Assert.False(SettingsPaletteIndex.JustDropDoorAvailable());

            var live = false;
            SettingsPaletteIndex.JustDropDoorAvailableProvider = () => live;
            Assert.False(SettingsPaletteIndex.JustDropDoorAvailable());
            live = true;
            Assert.True(SettingsPaletteIndex.JustDropDoorAvailable());
        }
        finally { SettingsPaletteIndex.JustDropDoorAvailableProvider = null; }
    }
}
