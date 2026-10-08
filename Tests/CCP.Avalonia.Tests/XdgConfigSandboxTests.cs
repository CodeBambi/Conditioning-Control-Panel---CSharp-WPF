using System.IO;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The autostart entry every shell build reconciles must resolve inside the test host's
/// throwaway XDG folder, never the developer's ~/.config/autostart.</summary>
public sealed class XdgConfigSandboxTests
{
    [Fact]
    public void AutostartEntryResolvesUnderTheTestSandbox()
    {
        Assert.Null(XdgAutostart.DirectoryOverride);
        var path = Path.GetFullPath(XdgAutostart.EntryPath);
        Assert.StartsWith(Path.GetFullPath(TestXdgConfigSandbox.Root) + Path.DirectorySeparatorChar, path);
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), path);
    }
}
