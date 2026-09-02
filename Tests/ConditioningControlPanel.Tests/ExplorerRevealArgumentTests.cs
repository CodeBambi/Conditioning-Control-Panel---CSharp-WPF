using ConditioningControlPanel.Helpers;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1108: "open in folder" in the media log opened Documents instead of the file's
/// folder, for everyone keeping assets in a path with spaces (D:\Conditioning Control Panel\...).
///
/// The #998 fix passed "/select,&lt;path&gt;" to ProcessStartInfo.ArgumentList as one token so the
/// runtime would quote it correctly. It did: ArgumentList wraps any token containing whitespace in
/// double quotes, and it wraps the whole token, so the command line became
/// explorer.exe "/select,D:\Conditioning Control Panel\..." with the switch INSIDE the quotes.
/// Explorer cannot parse that and falls back to its default folder (Documents on Windows 11)
/// without raising an error. A path with no spaces is never quoted, so it kept working for most
/// people and on the dev's machine, which is why #998 looked fixed.
///
/// These lock the shape of the command line: the switch outside the quotes, the path inside.
/// </summary>
public class ExplorerRevealArgumentTests
{
    [Fact]
    public void SpacedPath_KeepsTheSwitchOutsideTheQuotes()
    {
        var args = ExplorerLauncher.BuildRevealArguments(@"D:\Conditioning Control Panel\images\personal\file.gif");

        Assert.Equal(@"/select,""D:\Conditioning Control Panel\images\personal\file.gif""", args);
        // The exact regression: the whole token quoted as one unit.
        Assert.DoesNotContain(@"""/select", args);
        Assert.StartsWith("/select,\"", args);
    }

    [Fact]
    public void UnspacedPath_IsQuotedToo_SoBothCasesTakeOneCodePath()
    {
        var args = ExplorerLauncher.BuildRevealArguments(@"D:\CCP\images\file.gif");

        // Quoting a path that does not need it is harmless, and it means the spaced path is not a
        // special case that only some machines ever exercise.
        Assert.Equal(@"/select,""D:\CCP\images\file.gif""", args);
    }

    [Fact]
    public void UncPath_SurvivesIntact()
    {
        var args = ExplorerLauncher.BuildRevealArguments(@"\\media-nas\Shared Assets\images\file.gif");

        Assert.Equal(@"/select,""\\media-nas\Shared Assets\images\file.gif""", args);
    }

    [Fact]
    public void FolderArguments_QuoteThePathAndCarryNoSwitch()
    {
        Assert.Equal(@"""D:\Conditioning Control Panel\images""",
            ExplorerLauncher.BuildFolderArguments(@"D:\Conditioning Control Panel\images"));
        Assert.Equal(@"""\\media-nas\Shared Assets""",
            ExplorerLauncher.BuildFolderArguments(@"\\media-nas\Shared Assets"));
    }

    [Theory]
    [InlineData("D:\\weird\"path\\file.gif")]
    [InlineData("\"")]
    public void PathContainingAQuote_IsRefused(string path)
    {
        // Windows filenames cannot hold a double quote, so this is not a real path - and escaping
        // it could only ever produce a command line pointing somewhere other than intended.
        Assert.Null(ExplorerLauncher.BuildRevealArguments(path));
        Assert.Null(ExplorerLauncher.BuildFolderArguments(path));
    }

    [Fact]
    public void EmptyPath_IsRefused()
    {
        Assert.Null(ExplorerLauncher.BuildRevealArguments(""));
        Assert.Null(ExplorerLauncher.BuildFolderArguments("   "));
    }
}
