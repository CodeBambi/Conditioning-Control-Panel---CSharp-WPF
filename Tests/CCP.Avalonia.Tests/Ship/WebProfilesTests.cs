using System.IO;
using System.Runtime.CompilerServices;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Xunit;

namespace CCP.Avalonia.Tests.Ship;

/// <summary>WebView2 profiles live in UserData under WPF 7.1.5's folder names, so sign-ins survive the
/// upgrade and a mirrored deploy of the install folder cannot wipe them.</summary>
public sealed class WebProfilesTests
{
    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));

    [Theory]
    [InlineData("arcademy", "arcademy")]
    [InlineData("backroom", "backroom")]
    [InlineData("breakout", "backroom")]
    [InlineData("DTRH", "browser_data_dtrh")]
    [InlineData("goon", "browser_data_goon")]
    [InlineData("piecebypiece", "browser_data_piecebypiece")]
    [InlineData("race", "browser_data_race")]
    [InlineData("intake", "browser_data_intake")]
    [InlineData("new-game", "browser_data_new-game")]
    [InlineData("", "browser_data")]
    [InlineData(null, "browser_data")]
    public void AGameKeepsWpfsFolderName(string? id, string expected) =>
        Assert.Equal(expected, WebProfiles.ForGame(id));

    [Fact]
    public void TheNamedSurfacesKeepWpfsFolderNames()
    {
        Assert.Equal("browser_data", WebProfiles.Browser);
        Assert.Equal("browser_data_deeper_editor", WebProfiles.DeeperEditor);
        Assert.Equal("browser_data_deeper_player", WebProfiles.DeeperPlayer);
        Assert.Equal("browser_data_bambicloud", WebProfiles.BambiCloud);
        Assert.Equal("browser_data_spiral", WebProfiles.Spiral);
    }

    [Fact]
    public void EveryProfileIsInsideUserData()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-ud");
        Assert.Equal(Path.Combine(root, "browser_data_goon"), WebProfiles.FolderFor(root, "browser_data_goon"));
        Assert.Equal(Path.Combine(root, "browser_data"), WebProfiles.FolderFor(root, null));
        // A name that tries to leave the folder falls back to the default profile.
        Assert.Equal(Path.Combine(root, "browser_data"), WebProfiles.FolderFor(root, "../elsewhere"));
        Assert.Equal(Path.Combine(root, "browser_data"), WebProfiles.FolderFor(root, "C:/elsewhere"));
        Assert.StartsWith(CorePaths.UserData, WebProfiles.FolderFor("browser_data"));
    }

    [Fact]
    public void OneArgumentStringServesEveryFolder()
    {
        // WebView2 refuses a second environment on a folder started with different switches, so the
        // head has exactly one string and no host adds to it.
        Assert.Contains("--autoplay-policy=no-user-gesture-required", WebHost.WindowsBrowserArguments);
        var views = Path.Combine(RepoRoot(), "CCP.Avalonia");
        foreach (var file in Directory.GetFiles(views, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                || file.EndsWith("WebHost.axaml.cs")) continue;
            Assert.False(File.ReadAllText(file).Contains("AdditionalBrowserArguments ="),
                Path.GetFileName(file) + " sets its own browser arguments");
        }
    }

    [Fact]
    public void TheDeeperViewsNameTheirProfiles()
    {
        var deeper = Path.Combine(RepoRoot(), "CCP.Avalonia", "Views", "Deeper");
        Assert.Contains("Profile=\"" + WebProfiles.DeeperEditor + "\"", File.ReadAllText(Path.Combine(deeper, "DeeperEditorWindow.axaml")));
        Assert.Contains("Profile=\"" + WebProfiles.DeeperPlayer + "\"", File.ReadAllText(Path.Combine(deeper, "EnhancementPlayerWindow.axaml")));
    }
}
