using System;
using System.IO;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The installer's [InstallDelete] sweep empties the bundled audio folders by exact file name when a
/// user upgrades in place onto a modular install, and <c>dirifempty</c> only reclaims a folder the
/// sweep left completely bare. The husk that survives used to satisfy the resolver's bare
/// <c>Directory.Exists</c> probe, win rung 2 of the ladder, and shadow rung 3 — the DOWNLOADED
/// content pack — for good: the pack landed correctly and the companion stayed silent anyway.
///
/// These pin the probe that replaced it. Same lesson as <see cref="ContentMediaFloorTests"/>, applied
/// to the rung that picks which ROOT wins rather than whether a pack needs re-fetching.
/// </summary>
public class CompanionContentProbeTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ccp-probe-" + Guid.NewGuid().ToString("N"));

    private string Dir(string name)
    {
        var p = Path.Combine(_root, name);
        Directory.CreateDirectory(p);
        return p;
    }

    private static CompanionContentCandidate Folder(string path)
        => new(CompanionContentSource.Baseline, path, true);

    private static CompanionContentCandidate File_(string path)
        => new(CompanionContentSource.Baseline, path, false);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    // ---- the husk ----

    [Fact]
    public void EmptyFolderIsNotAHit()
        => Assert.False(ModCompanionContent.Exists(Folder(Dir("empty"))));

    [Fact]
    public void MissingFolderIsNotAHit()
        => Assert.False(ModCompanionContent.Exists(Folder(Path.Combine(_root, "nope"))));

    /// <summary>
    /// bark_rules.json and mantras.json stay in the installer, so a stripped folder can keep a .json
    /// and still hold no voice. That must not count as "the voice is here".
    /// </summary>
    [Fact]
    public void FolderWithOnlyNonMediaIsNotAHit()
    {
        var dir = Dir("jsononly");
        File.WriteAllText(Path.Combine(dir, "bark_rules.json"), "{}");
        Assert.False(ModCompanionContent.Exists(Folder(dir)));
    }

    // ---- the real thing ----

    [Theory]
    [InlineData("clip.mp3")]
    [InlineData("clip.wav")]
    [InlineData("clip.ogg")]
    [InlineData("clip.m4a")]
    [InlineData("portrait.png")]
    [InlineData("PORTRAIT.PNG")]   // NTFS is case-insensitive; the probe must be too
    public void FolderWithOneMediaFileIsAHit(string name)
    {
        var dir = Dir("media-" + Path.GetFileNameWithoutExtension(name) + Path.GetExtension(name).Trim('.'));
        File.WriteAllText(Path.Combine(dir, name), "x");
        Assert.True(ModCompanionContent.Exists(Folder(dir)));
    }

    /// <summary>The portrait trees nest, so the walk has to be recursive.</summary>
    [Fact]
    public void MediaInASubfolderIsAHit()
    {
        var dir = Dir("nested");
        var sub = Path.Combine(dir, "portraits", "0_base");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "pose.png"), "x");
        Assert.True(ModCompanionContent.Exists(Folder(dir)));
    }

    // ---- files are unchanged ----

    [Fact]
    public void FileCandidateStillProbesTheFile()
    {
        var dir = Dir("files");
        var path = Path.Combine(dir, "mantras.json");
        Assert.False(ModCompanionContent.Exists(File_(path)));
        File.WriteAllText(path, "[]");
        Assert.True(ModCompanionContent.Exists(File_(path)));
    }

    [Fact]
    public void BlankPathIsNotAHit()
    {
        Assert.False(ModCompanionContent.Exists(Folder("")));
        Assert.False(ModCompanionContent.Exists(File_("   ")));
    }
}
