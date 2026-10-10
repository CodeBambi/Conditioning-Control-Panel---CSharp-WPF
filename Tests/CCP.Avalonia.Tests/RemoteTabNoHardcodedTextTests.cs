using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>hunt3 IC10: the Remote tab's last hardcoded English (the second door's dialog title and the
/// two PIN labels) reads from the existing keys tab_remote_control and remote_overlay_pin.</summary>
public sealed class RemoteTabNoHardcodedTextTests
{
    [Fact]
    public void TheRemoteTabCodeBehindCarriesNoEnglishLiterals()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Avalonia", "Views", "Tabs", "RemoteControlTabView.axaml.cs"));
        Assert.DoesNotContain("\"Remote Control\"", text);
        Assert.DoesNotContain("PIN:", text);
        Assert.Contains("Loc.GetF(\"remote_overlay_pin\"", text);
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
