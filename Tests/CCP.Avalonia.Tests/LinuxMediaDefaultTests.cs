using System;
using System.IO;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>App.DefaultAssetsPath: Linux default media folder is ~/ccp media (user request).</summary>
public sealed class LinuxMediaDefaultTests
{
    private static string Temp() => Directory.CreateTempSubdirectory("ccp-media-").FullName;

    [Fact]
    public void LinuxDefaultsToCcpMediaUnderHomeAndCreatesIt()
    {
        string home = Temp(), data = Temp();
        var path = App.DefaultAssetsPath(true, home, data);
        Assert.Equal(Path.Combine(home, "ccp media"), path);
        Assert.True(Directory.Exists(Path.Combine(path, "images")));
        Assert.True(Directory.Exists(Path.Combine(path, "videos")));
    }

    [Fact]
    public void WindowsKeepsUserDataAssets()
    {
        string home = Temp(), data = Temp();
        Assert.Equal(Path.Combine(data, "assets"), App.DefaultAssetsPath(false, home, data));
        Assert.False(Directory.Exists(Path.Combine(home, "ccp media")));
    }

    [Fact]
    public void LinuxProfileWithMediaInUserDataAssetsKeepsIt()
    {
        string home = Temp(), data = Temp();
        var legacy = Path.Combine(data, "assets", "images");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "a.png"), "x");
        Assert.Equal(Path.Combine(data, "assets"), App.DefaultAssetsPath(true, home, data));
        Assert.False(Directory.Exists(Path.Combine(home, "ccp media")));
    }

    [Fact]
    public void PersonalFolderGuardAcceptsCcpMedia()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(MainShellWindow.IsPersonalFolderRoot(Path.Combine(profile, "ccp media")));
        Assert.True(MainShellWindow.IsPersonalFolderRoot(profile));
    }
}
