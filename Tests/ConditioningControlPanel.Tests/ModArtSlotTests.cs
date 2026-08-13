using System.IO;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Mod-awareness sweep, lane F. Two pieces of decision logic behind the mod editor's new
/// "UI Art" panel and the mod manager's art summary, both deliberately kept free of any
/// Dispatcher so they can be pinned here.
///
/// The extension rule exists because a mod slot key IS the filename inside the package:
/// resources/features/vault.png shadows Resources/features/vault.png byte-for-byte. WPF's
/// decoder sniffs headers and would happily show JPEG bytes named .png, so nothing in the
/// editor used to catch it -- but the catalogue thumbnailer, the mobile client and ordinary
/// image tooling all trust the extension.
/// </summary>
public class ModArtSlotTests
{
    // ── extension gate ───────────────────────────────────────────

    [Fact]
    public void MatchingExtension_IsAccepted()
        => Assert.Null(ModImageSlotRules.DescribeExtensionMismatch("features/vault.png", @"C:\art\my_vault.png"));

    [Fact]
    public void MatchingExtension_IgnoresCase()
        => Assert.Null(ModImageSlotRules.DescribeExtensionMismatch("nav/door_home.png", @"C:\art\DOOR.PNG"));

    [Fact]
    public void JpegBytesForAPngSlot_AreRejected()
    {
        var reason = ModImageSlotRules.DescribeExtensionMismatch("features/vault.png", @"C:\art\vault.jpg");
        Assert.NotNull(reason);
        Assert.Contains(".png", reason);
        Assert.Contains("vault.jpg", reason);
    }

    [Fact]
    public void PngForTheSpiralGifSlot_IsRejected()
        => Assert.NotNull(ModImageSlotRules.DescribeExtensionMismatch("spiral.gif", @"C:\art\spiral.png"));

    [Fact]
    public void JpegAndJpg_AreTheSameFormat()
        => Assert.Null(ModImageSlotRules.DescribeExtensionMismatch("something.jpg", @"C:\art\photo.jpeg"));

    [Fact]
    public void ExtensionlessSlot_AcceptsAnything()
    {
        // The Info panel's "preview" slot has no extension -- it is not packed under a fixed
        // filename, so there is no rule for it to break.
        Assert.Null(ModImageSlotRules.DescribeExtensionMismatch("preview", @"C:\art\thumb.jpg"));
        Assert.Null(ModImageSlotRules.DescribeExtensionMismatch("preview", @"C:\art\thumb.png"));
    }

    [Fact]
    public void ExtensionlessFile_ForAnExtensionSlot_IsRejected()
        => Assert.NotNull(ModImageSlotRules.DescribeExtensionMismatch("features/vault.png", @"C:\art\vault"));

    // ── oversize warning ─────────────────────────────────────────

    [Fact]
    public void FourMegabytesExactly_IsNotOversized()
        => Assert.False(ModImageSlotRules.IsOversized(ModImageSlotRules.WarnFileSizeBytes));

    [Fact]
    public void JustOverFourMegabytes_IsOversized()
        => Assert.True(ModImageSlotRules.IsOversized(ModImageSlotRules.WarnFileSizeBytes + 1));

    [Fact]
    public void ShippedHeroArt_DoesNotTripTheWarning()
        // features/dtrh.png, the largest embedded plate, is ~2.1 MB.
        => Assert.False(ModImageSlotRules.IsOversized(2_190_000));

    [Fact]
    public void SizeIsFormattedInvariantly()
        => Assert.Equal("6.0 MB", ModImageSlotRules.FormatSize(6L * 1024 * 1024));

    // ── manager art summary ──────────────────────────────────────

    [Fact]
    public void MissingResourcesFolder_SummarizesToNull()
        => Assert.Null(ModManagerDialog.SummarizeOverrides(
            Path.Combine(Path.GetTempPath(), "ccp_no_such_mod_" + Path.GetRandomFileName())));

    [Fact]
    public void EmptyResourcesFolder_SummarizesToNull()
    {
        var dir = MakeTempTree();
        try
        {
            Assert.Null(ModManagerDialog.SummarizeOverrides(dir));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void OverridesAreCountedAndGroupedByTopLevelFolder()
    {
        var dir = MakeTempTree();
        try
        {
            Touch(dir, "features", "vault.png");
            Touch(dir, "features", "dtrh.png");
            Touch(dir, "nav", "door_home.png");

            var summary = ModManagerDialog.SummarizeOverrides(dir);

            Assert.Equal("Overrides: 3 files (features 2, nav 1)", summary);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void NestedFilesCountTowardTheirTopLevelFolder()
    {
        var dir = MakeTempTree();
        try
        {
            Touch(dir, Path.Combine("emotes", "set1"), "wink.gif");
            Touch(dir, Path.Combine("emotes", "set2"), "blush.gif");

            Assert.Equal("Overrides: 2 files (emotes 2)", ModManagerDialog.SummarizeOverrides(dir));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void LooseFilesAtTheRootGroupAsRoot()
    {
        var dir = MakeTempTree();
        try
        {
            // lockdown_icon.png really does live at the resource root, not under features/.
            Touch(dir, null, "lockdown_icon.png");

            Assert.Equal("Overrides: 1 file (root 1)", ModManagerDialog.SummarizeOverrides(dir));
        }
        finally { Cleanup(dir); }
    }

    // ── helpers ──────────────────────────────────────────────────

    private static string MakeTempTree()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp_modart_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Touch(string root, string? subFolder, string fileName)
    {
        var folder = subFolder == null ? root : Path.Combine(root, subFolder);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, fileName), "x");
    }

    private static void Cleanup(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* temp */ }
    }
}
