using System;
using System.IO;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// Guards the shared-asset convention: runtime media lives in the repo-root <c>Assets/</c> tree,
/// never under a head's <c>Resources/</c> folder, so every head links the same bytes.
/// These files are referenced by name from code and JS, where a move is otherwise silent
/// (Assets/web/backroom/shared/sound/music.js TRACKS,
/// ConditioningControlPanel/Services/Launcher/LauncherSfx.cs HoverCue).
/// </summary>
public class SharedAssetLayoutTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Theory]
    [InlineData("Assets/sounds/launcher/hover.wav")]
    [InlineData("Assets/web/backroom/music/velvet-roulette.mp3")]
    [InlineData("Assets/web/backroom/music/midnight-jackpot-alt.mp3")]
    [InlineData("Assets/web/backroom/music/midnight-jackpot.mp3")]
    [InlineData("Assets/web/backroom/music/coin-arpeggio.mp3")]
    [InlineData("Assets/web/backroom/music/neon-jackpot.mp3")]
    public void ReferencedAssetLivesInSharedTree(string relative)
    {
        Assert.True(File.Exists(Path.Combine(RepoRoot(), relative)), relative + " is missing from the shared Assets tree");
    }

    [Theory]
    [InlineData("ConditioningControlPanel/Resources/sounds")]
    [InlineData("ConditioningControlPanel/Resources/web")]
    public void HeadKeepsNoPrivateCopyOfSharedMedia(string relative)
    {
        var dir = Path.Combine(RepoRoot(), relative);
        var stray = Directory.Exists(dir) ? Directory.GetFiles(dir, "*", SearchOption.AllDirectories) : Array.Empty<string>();
        Assert.True(stray.Length == 0, relative + " holds " + stray.Length + " file(s); shared media belongs under Assets/");
    }
}
