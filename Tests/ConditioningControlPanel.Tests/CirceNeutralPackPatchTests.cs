using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Migrations;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The in-place patch of the extracted Circe's Lock tree (builtin_mods\builtin-locked\). A user who
/// already holds the mod-locked pack never re-extracts it, so the re-rendered voice lines and art
/// only arrive through <see cref="CirceNeutralPackPatch.Apply"/> copying LockedMod\overrides over
/// the tree and deleting the superseded voice lines (which would otherwise still be enumerated and
/// still print their old text in the bubble).
///
/// Every test works on a throwaway temp folder; the last one checks the real overrides shipped in
/// the repo against the old-to-new map.
/// </summary>
public class CirceNeutralPackPatchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-circe-patch-" + Guid.NewGuid().ToString("N"));
    private readonly string _tree;
    private readonly string _overrides;

    private static readonly string Flashes = CirceNeutralPackPatch.FlashesAudioRelativeDir;
    private static readonly string Features = Path.Combine("resources", "features");

    public CirceNeutralPackPatchTests()
    {
        _tree = Path.Combine(_root, "builtin-locked");
        _overrides = Path.Combine(_root, "overrides");
        Directory.CreateDirectory(Path.Combine(_tree, Flashes));
        Directory.CreateDirectory(Path.Combine(_tree, Features));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp; best effort */ }
    }

    private static void Put(string root, string rel, string content)
    {
        var path = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Read(string root, string rel) => File.ReadAllText(Path.Combine(root, rel));

    // ---- Copy ------------------------------------------------------------------------------

    /// <summary>A replacement the tree does not have yet is copied in, nested folders included.</summary>
    [Fact]
    public void Copies_missing_files()
    {
        Put(_overrides, Path.Combine(Flashes, "GOOD PET.mp3"), "new voice");
        Put(_overrides, Path.Combine(Features, "subliminal.png"), "new art");

        var r = CirceNeutralPackPatch.Apply(_tree, _overrides);

        Assert.Equal(2, r.Copied);
        Assert.Equal(0, r.Failed);
        Assert.Equal("new voice", Read(_tree, Path.Combine(Flashes, "GOOD PET.mp3")));
        Assert.Equal("new art", Read(_tree, Path.Combine(Features, "subliminal.png")));
        Assert.Empty(Directory.GetFiles(_tree, "*.tmp", SearchOption.AllDirectories));
    }

    /// <summary>The four PNGs are replaced in place: a different length means the old art.</summary>
    [Fact]
    public void Overwrites_a_target_of_different_length()
    {
        Put(_tree, Path.Combine(Features, "Phrase_Lock.png"), "old gendered art, longer");
        Put(_overrides, Path.Combine(Features, "Phrase_Lock.png"), "new art");

        var r = CirceNeutralPackPatch.Apply(_tree, _overrides);

        Assert.Equal(1, r.Copied);
        Assert.Equal("new art", Read(_tree, Path.Combine(Features, "Phrase_Lock.png")));
    }

    /// <summary>Same length reads as already patched and is not rewritten (the per-launch cost is a stat).</summary>
    [Fact]
    public void Leaves_a_target_of_the_same_length_alone()
    {
        Put(_tree, Path.Combine(Features, "bouncing_text.png"), "AAAAAAA");
        Put(_overrides, Path.Combine(Features, "bouncing_text.png"), "new art");

        var r = CirceNeutralPackPatch.Apply(_tree, _overrides);

        Assert.Equal(0, r.Copied);
        Assert.False(r.ChangedAnything);
        Assert.Equal("AAAAAAA", Read(_tree, Path.Combine(Features, "bouncing_text.png")));
    }

    // ---- Delete ----------------------------------------------------------------------------

    /// <summary>Every superseded voice line goes once its replacement is in; unrelated lines stay.</summary>
    [Fact]
    public void Deletes_every_old_voice_line_and_keeps_the_rest()
    {
        foreach (var (oldStem, newStem) in CirceNeutralMigration.VoiceLineStemMap)
        {
            Put(_tree, Path.Combine(Flashes, oldStem + ".mp3"), "old");
            Put(_overrides, Path.Combine(Flashes, newStem + ".mp3"), "new");
        }
        Put(_tree, Path.Combine(Flashes, "MINE.mp3"), "unchanged");

        var r = CirceNeutralPackPatch.Apply(_tree, _overrides);

        Assert.Equal(CirceNeutralMigration.VoiceLineStemMap.Count, r.Deleted);
        Assert.Equal(CirceNeutralMigration.VoiceLineStemMap.Count, r.Copied);
        Assert.Equal(0, r.Failed);
        foreach (var name in CirceNeutralPackPatch.ObsoleteVoiceLineFiles)
            Assert.False(File.Exists(Path.Combine(_tree, Flashes, name)), name);
        foreach (var name in CirceNeutralPackPatch.ReplacementVoiceLineFiles)
            Assert.True(File.Exists(Path.Combine(_tree, Flashes, name)), name);
        Assert.True(File.Exists(Path.Combine(_tree, Flashes, "MINE.mp3")));
    }

    /// <summary>An old line whose replacement never landed is kept: a gendered line beats a missing one.</summary>
    [Fact]
    public void Keeps_an_old_voice_line_whose_replacement_is_not_in_the_tree()
    {
        Directory.CreateDirectory(_overrides);
        Put(_tree, Path.Combine(Flashes, "GOOD BOY.mp3"), "old");

        var r = CirceNeutralPackPatch.Apply(_tree, _overrides);

        Assert.Equal(0, r.Deleted);
        Assert.True(File.Exists(Path.Combine(_tree, Flashes, "GOOD BOY.mp3")));
    }

    // ---- Idempotence and no-ops ------------------------------------------------------------

    /// <summary>The call runs on every launch; the second run must find nothing to do.</summary>
    [Fact]
    public void Second_run_is_a_no_op()
    {
        Put(_tree, Path.Combine(Flashes, "Good boy. Pop..mp3"), "old");
        Put(_tree, Path.Combine(Features, "subliminal.png"), "old art, a different length");
        Put(_overrides, Path.Combine(Flashes, "Good pet. Pop..mp3"), "new");
        Put(_overrides, Path.Combine(Features, "subliminal.png"), "new art");

        var first = CirceNeutralPackPatch.Apply(_tree, _overrides);
        var second = CirceNeutralPackPatch.Apply(_tree, _overrides);

        Assert.True(first.ChangedAnything);
        Assert.Equal(2, first.Copied);
        Assert.Equal(1, first.Deleted);
        Assert.False(second.ChangedAnything);
        Assert.Equal(0, second.Copied);
        Assert.Equal(0, second.Deleted);
        Assert.Equal(0, second.Failed);
    }

    /// <summary>A build without the overrides folder touches nothing, including the old lines.</summary>
    [Fact]
    public void Missing_overrides_folder_is_a_no_op()
    {
        Put(_tree, Path.Combine(Flashes, "GOOD BOY.mp3"), "old");
        Put(_tree, Path.Combine(Flashes, "GOOD PET.mp3"), "new");

        var r = CirceNeutralPackPatch.Apply(_tree, Path.Combine(_root, "does-not-exist"));

        Assert.False(r.ChangedAnything);
        Assert.Equal(0, r.Failed);
        Assert.True(File.Exists(Path.Combine(_tree, Flashes, "GOOD BOY.mp3")));
    }

    /// <summary>No extracted tree: nothing is created, or the next launch would adopt an empty one.</summary>
    [Fact]
    public void Missing_tree_is_a_no_op_and_creates_nothing()
    {
        Put(_overrides, Path.Combine(Flashes, "GOOD PET.mp3"), "new");
        var absent = Path.Combine(_root, "never-extracted");

        var r = CirceNeutralPackPatch.Apply(absent, _overrides);

        Assert.False(r.ChangedAnything);
        Assert.False(Directory.Exists(absent));
    }

    /// <summary>A file held open (a playing bark, an AV scan) is counted and skipped, never thrown.</summary>
    [Fact]
    public void Locked_files_never_throw()
    {
        Put(_tree, Path.Combine(Features, "subliminal.png"), "old art, a different length");
        Put(_overrides, Path.Combine(Features, "subliminal.png"), "new art");
        Put(_overrides, Path.Combine(Features, "bouncing_text.png"), "new art");
        Put(_tree, Path.Combine(Flashes, "GOOD BOY.mp3"), "old");
        Put(_tree, Path.Combine(Flashes, "GOOD PET.mp3"), "new");

        CirceNeutralPackPatch.Result r;
        using (new FileStream(Path.Combine(_tree, Features, "subliminal.png"), FileMode.Open, FileAccess.Read, FileShare.None))
        using (new FileStream(Path.Combine(_tree, Flashes, "GOOD BOY.mp3"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            r = CirceNeutralPackPatch.Apply(_tree, _overrides);
        }

        Assert.Equal(2, r.Failed);
        Assert.Equal(1, r.Copied);   // bouncing_text.png still went in
        Assert.Equal("old art, a different length", Read(_tree, Path.Combine(Features, "subliminal.png")));
        Assert.Empty(Directory.GetFiles(_tree, "*.tmp", SearchOption.AllDirectories));

        // Released: the next launch finishes the job.
        var retry = CirceNeutralPackPatch.Apply(_tree, _overrides);
        Assert.Equal(0, retry.Failed);
        Assert.Equal("new art", Read(_tree, Path.Combine(Features, "subliminal.png")));
        Assert.False(File.Exists(Path.Combine(_tree, Flashes, "GOOD BOY.mp3")));
    }

    // ---- The shipped overrides -------------------------------------------------------------

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    /// <summary>
    /// Every old voice line has its replacement in LockedMod\overrides, none of the old names ships
    /// there, and the four replaced PNGs are present. Without this, a missing override would leave its
    /// old line in place forever (the delete waits for the replacement).
    /// </summary>
    [Fact]
    public void Shipped_overrides_cover_every_old_voice_line_and_the_four_pngs()
    {
        var overrides = Path.Combine(RepoRoot(), "ConditioningControlPanel", CirceNeutralPackPatch.OverridesRelativeDir);
        var flashes = Path.Combine(overrides, Flashes);

        Assert.Equal(25, CirceNeutralMigration.VoiceLineStemMap.Count);
        foreach (var name in CirceNeutralPackPatch.ReplacementVoiceLineFiles)
            Assert.True(File.Exists(Path.Combine(flashes, name)), "missing override: " + name);
        foreach (var name in CirceNeutralPackPatch.ObsoleteVoiceLineFiles)
            Assert.False(File.Exists(Path.Combine(flashes, name)), "old name shipped as an override: " + name);

        foreach (var rel in new[]
                 {
                     Path.Combine("resources", "features", "subliminal.png"),
                     Path.Combine("resources", "features", "bouncing_text.png"),
                     Path.Combine("resources", "features", "Phrase_Lock.png"),
                     Path.Combine("resources", "achievements", "Dumb_Bimbo.png"),
                 })
            Assert.True(File.Exists(Path.Combine(overrides, rel)), "missing override: " + rel);

        Assert.Equal(29, Directory.GetFiles(overrides, "*", SearchOption.AllDirectories).Length);
    }
}
