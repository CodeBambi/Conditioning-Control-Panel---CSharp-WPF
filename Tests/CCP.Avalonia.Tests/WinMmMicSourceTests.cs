using System;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>platform#15: the Windows mic source lists devices the WPF way ("System default" first) and
/// resolves the saved mic by name, then index, else the OS default. Never opens a microphone.</summary>
public sealed class WinMmMicSourceTests
{
    [Fact]
    public void ListsSystemDefaultFirstAndResolvesByNameThenIndex()
    {
        var mic = new WinMmMicSource();
        var list = mic.ListDevices();
        Assert.Equal(-1, list[0].Index);
        Assert.Equal("System default", list[0].Name);
        Assert.Equal(WinMmMicSource.DeviceCount + 1, list.Count);
        Assert.Equal(mic.HasDevice, WinMmMicSource.DeviceCount > 0);

        Assert.Equal(0xFFFFFFFFu, WinMmMicSource.ResolveDevice(-1, null));            // OS default
        Assert.Equal(0xFFFFFFFFu, WinMmMicSource.ResolveDevice(999, "no such mic"));  // gone: default
        if (list.Count > 1)
        {
            Assert.Equal(0u, WinMmMicSource.ResolveDevice(-1, list[1].Name));       // the name wins
            Assert.Equal(0u, WinMmMicSource.ResolveDevice(0, null));
        }
        if (!OperatingSystem.IsWindows()) Assert.Single(list);
    }
}
