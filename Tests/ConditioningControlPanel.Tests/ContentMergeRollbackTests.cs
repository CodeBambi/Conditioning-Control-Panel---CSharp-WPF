using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// A pack merge that threw part-way used to leave whatever it had already copied in the live
/// content tree — the debris that makes a broken pack read as "present" and wedges it out of ever
/// being re-fetched. These pin the journal that takes it back out, and above all the constraint
/// that makes the whole thing delicate: every loose-media pack merges into the SHARED
/// content\Resources, so a rollback must never remove a file it did not write.
/// </summary>
public class ContentMergeRollbackTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-665-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Touch(string dir, string relPath, string content = "x")
    {
        var full = Path.Combine(dir, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    // ---- bookkeeping ----

    [Fact]
    public void AFreshJournalHasNothingToUndo()
        => Assert.True(new MergeJournal().IsEmpty);

    [Fact]
    public void RecordingKeepsWriteOrderAndDropsRepeats()
    {
        // The retry pass re-merges over the first attempt's work and re-records the same paths.
        var journal = new MergeJournal();
        journal.RecordFile(@"C:\c\a.mp3");
        journal.RecordFile(@"C:\c\b.mp3");
        journal.RecordFile(@"C:\c\a.mp3");
        journal.RecordFile(@"C:\C\A.MP3");   // NTFS is case-insensitive

        Assert.Equal(new[] { @"C:\c\a.mp3", @"C:\c\b.mp3" }, journal.Files.ToArray());
    }

    [Fact]
    public void FilesDirectoriesAndTreesAreTrackedApart()
    {
        var journal = new MergeJournal();
        journal.RecordFile(@"C:\c\a.mp3");
        journal.RecordDirectory(@"C:\c\sub");
        journal.RecordTree(@"C:\c\whole");

        Assert.False(journal.IsEmpty);
        Assert.Single(journal.Files);
        Assert.Single(journal.Directories);
        Assert.Single(journal.Trees);
    }

    [Fact]
    public void EmptyPathsAreIgnored()
    {
        var journal = new MergeJournal();
        journal.RecordFile("");
        journal.RecordDirectory("");
        journal.RecordTree(null!);

        Assert.True(journal.IsEmpty);
    }

    // ---- the undo ----

    [Fact]
    public void RollbackRemovesExactlyTheFilesTheMergeWrote()
    {
        var root = NewTempDir();
        try
        {
            var stranger = Touch(root, @"Resources\sounds\another-packs-file.mp3");
            var mine = Touch(root, @"Resources\sounds\mine.mp3");

            var journal = new MergeJournal();
            journal.RecordFile(mine);

            Assert.Equal(1, journal.Rollback());
            Assert.False(File.Exists(mine));
            Assert.True(File.Exists(stranger), "rollback took a file that belonged to another pack");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void RollbackRemovesOnlyTheDirectoriesItCreated_AndOnlyOnceEmpty()
    {
        var root = NewTempDir();
        try
        {
            // Shared: predates the merge and keeps a stranger's file. Mine: created by the merge.
            var shared = Path.Combine(root, "Resources");
            var stranger = Touch(root, @"Resources\another.mp3");
            var mineDir = Path.Combine(shared, "mods", "builtin-bambisleep");
            var mine = Touch(root, @"Resources\mods\builtin-bambisleep\voice.mp3");

            var journal = new MergeJournal();
            journal.RecordDirectory(Path.Combine(shared, "mods"));
            journal.RecordDirectory(mineDir);
            journal.RecordFile(mine);

            journal.Rollback();

            Assert.False(Directory.Exists(mineDir));
            Assert.False(Directory.Exists(Path.Combine(shared, "mods")), "the empty parent chain was left behind");
            Assert.True(Directory.Exists(shared), "rollback removed a directory it did not create");
            Assert.True(File.Exists(stranger));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void ADirectoryThatStillHoldsSomeoneElsesFileSurvives()
    {
        var root = NewTempDir();
        try
        {
            var dir = Path.Combine(root, "Resources", "sounds");
            var mine = Touch(root, @"Resources\sounds\mine.mp3");
            var stranger = Touch(root, @"Resources\sounds\theirs.mp3");

            var journal = new MergeJournal();
            journal.RecordDirectory(dir);
            journal.RecordFile(mine);

            journal.Rollback();

            Assert.True(Directory.Exists(dir));
            Assert.True(File.Exists(stranger));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void ATreeCreatedWholesaleComesOutWholesale()
    {
        // The rename fast path: the target did not exist, so nothing in it belongs to anyone else.
        var root = NewTempDir();
        try
        {
            var tree = Path.Combine(root, "packs");
            Touch(root, @"packs\a.ccpmod");
            Touch(root, @"packs\nested\b.mp3");

            var journal = new MergeJournal();
            journal.RecordTree(tree);

            Assert.Equal(2, journal.Rollback());
            Assert.False(Directory.Exists(tree));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void AlreadyGoneEntriesAreNotCountedAndDoNotThrow()
    {
        var root = NewTempDir();
        try
        {
            var journal = new MergeJournal();
            journal.RecordFile(Path.Combine(root, "never-written.mp3"));
            journal.RecordDirectory(Path.Combine(root, "never-made"));
            journal.RecordTree(Path.Combine(root, "never-moved"));

            Assert.Equal(0, journal.Rollback());
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void RollbackIsIdempotent()
    {
        // The generic install catch can reach the cleanup twice; a second pass must be a no-op
        // rather than an exception out of a path that is already handling a failure.
        var root = NewTempDir();
        try
        {
            var mine = Touch(root, @"Resources\mine.mp3");
            var journal = new MergeJournal();
            journal.RecordFile(mine);

            Assert.Equal(1, journal.Rollback());
            Assert.Equal(0, journal.Rollback());
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void DeepChainsUnwindChildBeforeParent()
    {
        // Directories are recorded parents-first; the undo has to walk them the other way or the
        // parent is still non-empty when it is tried.
        var root = NewTempDir();
        try
        {
            var journal = new MergeJournal();
            var path = root;
            foreach (var segment in new[] { "a", "b", "c", "d" })
            {
                path = Path.Combine(path, segment);
                Directory.CreateDirectory(path);
                journal.RecordDirectory(path);
            }
            var leaf = Touch(root, @"a\b\c\d\voice.mp3");
            journal.RecordFile(leaf);

            journal.Rollback();

            Assert.False(Directory.Exists(Path.Combine(root, "a")));
            Assert.True(Directory.Exists(root));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
