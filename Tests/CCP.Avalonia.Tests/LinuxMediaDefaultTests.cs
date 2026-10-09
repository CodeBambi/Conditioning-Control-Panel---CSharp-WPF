using System;
using System.IO;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>App.DefaultAssetsPath: Linux default media folder is ~/ccp media (user request).</summary>
public sealed class LinuxMediaDefaultTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ccp-media-").FullName;
    private string Home => Path.Combine(_root, "home");
    private string Data => Path.Combine(_root, "data");
    private string Media => Path.Combine(Home, "ccp media");
    private string Legacy => Path.Combine(Data, "assets");

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void LinuxDefaultsToCcpMediaUnderHomeAndCreatesIt()
    {
        Assert.Equal(Media, App.DefaultAssetsPath(true, Home, Data, false));
        Assert.True(Directory.Exists(Path.Combine(Media, "images")));
        Assert.True(Directory.Exists(Path.Combine(Media, "videos")));
    }

    [Fact]
    public void LegacyWithOnlyEmptySubfoldersSwitchesToCcpMedia()
    {
        Directory.CreateDirectory(Path.Combine(Legacy, "images"));
        Directory.CreateDirectory(Path.Combine(Legacy, "videos"));
        Assert.Equal(Media, App.DefaultAssetsPath(true, Home, Data, false));
    }

    [Theory]
    [InlineData(false, false, "h")]   // Windows
    [InlineData(true, true, "h")]     // CCP_USERDATA_DIR sandbox
    [InlineData(true, false, "")]     // unknown home
    public void KeepsUserDataAssets(bool isLinux, bool sandboxed, string home)
    {
        var h = home == "" ? "" : Home;
        Assert.Equal(Legacy, App.DefaultAssetsPath(isLinux, h, Data, sandboxed));
        Assert.False(Directory.Exists(Media));
    }

    [Fact]
    public void UncreatableCcpMediaKeepsUserDataAssets()
    {
        File.WriteAllText(Home, "not a folder");   // ~/ccp media cannot be created (read-only/confined home)
        Assert.Equal(Legacy, App.DefaultAssetsPath(true, Home, Data, false));
    }

    [Fact]
    public void LinuxProfileWithMediaInUserDataAssetsKeepsIt()
    {
        Directory.CreateDirectory(Path.Combine(Legacy, "images"));
        File.WriteAllText(Path.Combine(Legacy, "images", "a.png"), "x");
        Assert.Equal(Legacy, App.DefaultAssetsPath(true, Home, Data, false));
        Assert.False(Directory.Exists(Media));
    }

    [Fact]
    public void PersonalFolderGuardAcceptsCcpMedia()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(MainShellWindow.IsPersonalFolderRoot(Path.Combine(profile, "ccp media")));
        Assert.True(MainShellWindow.IsPersonalFolderRoot(profile));
    }
}
