using System;
using System.IO;
using ConditioningControlPanel.Services.Speech;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Finding the offline speech model on disk, and the folder it goes in.
///
/// <para>Four people spent forty-minute support threads on this in one week. Two facts came out of
/// them: the folder does not exist on a fresh install, and the ~128 MB model the README recommended
/// "did not work" while the 40 MB one did. The second is not about size - it is about DEPTH.
/// Windows' "Extract All" defaults to a subfolder named after the zip, so unpacking the bigger
/// model into this folder lands it TWO levels down, and the resolver only looked one. It reported
/// that as no model at all, and the hint said "no speech model installed yet" to someone looking
/// straight at the model they had just put there.</para>
/// </summary>
public class SpeechModelFolderTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ccp-vosk-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>Makes a directory look like a Vosk model: am/ next to conf/.</summary>
    private static string Model(string dir)
    {
        Directory.CreateDirectory(Path.Combine(dir, "am"));
        Directory.CreateDirectory(Path.Combine(dir, "conf"));
        return dir;
    }

    private string Under(params string[] parts)
    {
        var all = new string[parts.Length + 1];
        all[0] = _root;
        parts.CopyTo(all, 1);
        return Path.Combine(all);
    }

    // ---- the three layouts --------------------------------------------------------------

    [Fact]
    public void FilesDroppedStraightIn()
    {
        Model(_root);
        Assert.Equal(_root, SpeechService.ResolveModelDir(_root));
    }

    [Fact]
    public void OneNestedFolderTheWayTheOfficialZipUnpacks()
    {
        var dir = Model(Under("vosk-model-small-en-us-0.15"));
        Assert.Equal(dir, SpeechService.ResolveModelDir(_root));
    }

    /// <summary>The regression this exists for. Extract All, twice the folder, no model found.</summary>
    [Fact]
    public void TheSameFolderTwiceWhichIsWhatExtractAllProduces()
    {
        var dir = Model(Under("vosk-model-en-us-0.22-lgraph", "vosk-model-en-us-0.22-lgraph"));
        Assert.Equal(dir, SpeechService.ResolveModelDir(_root));
    }

    [Fact]
    public void ThreeLevelsDownIsStillNotFound()
    {
        // Deliberate: a bounded search, not a tree walk. The README says to move it up.
        Model(Under("a", "b", "c"));
        Assert.Null(SpeechService.ResolveModelDir(_root));
    }

    [Fact]
    public void AnEmptyFolderIsTheSameAsNoFolder()
    {
        Directory.CreateDirectory(_root);
        Assert.Null(SpeechService.ResolveModelDir(_root));
        Assert.Null(SpeechService.ResolveModelDir(Path.Combine(_root, "not-there")));
    }

    [Fact]
    public void HalfAModelIsNotAModel()
    {
        // am/ with no conf/ is a half-finished unpack, which Vosk would refuse anyway.
        Directory.CreateDirectory(Under("vosk-model-small-en-us-0.15", "am"));
        Assert.Null(SpeechService.ResolveModelDir(_root));
    }

    // ---- ranking ------------------------------------------------------------------------

    /// <summary>
    /// The grammar-capable ranking has to survive the deeper scan: a full model found nested must
    /// not outrank a small one found at the top, or dropping a model in makes speech worse.
    /// </summary>
    [Fact]
    public void GrammarCapableModelsStillWinFromAnyDepth()
    {
        Model(Under("vosk-model-en-us-0.42-gigaspeech"));
        var lgraph = Model(Under("outer", "vosk-model-en-us-0.22-lgraph"));
        Assert.Equal(lgraph, SpeechService.ResolveModelDir(_root));
    }

    [Fact]
    public void SmallBeatsAFullModelToo()
    {
        Model(Under("vosk-model-en-us-0.42-gigaspeech"));
        var small = Model(Under("vosk-model-small-en-us-0.15"));
        Assert.Equal(small, SpeechService.ResolveModelDir(_root));
    }

    // ---- the folder itself ----------------------------------------------------------------

    /// <summary>
    /// The button must never be a dead end: it creates the folder, because on a fresh install
    /// there is nothing there and "put it in Resources\Models\vosk" is the instruction that has
    /// been costing the support threads.
    /// </summary>
    [Fact]
    public void EnsureCreatesTheFolderAndIsHappyToFindIt()
    {
        Assert.False(Directory.Exists(_root));
        Assert.Equal(_root, SpeechModelFolder.Ensure(_root));
        Assert.True(Directory.Exists(_root));

        // Idempotent, and it does not disturb a model already sitting there.
        Model(_root);
        Assert.Equal(_root, SpeechModelFolder.Ensure(_root));
        Assert.Equal(_root, SpeechService.ResolveModelDir(_root));
    }

    [Fact]
    public void EnsureRefusesNonsenseInsteadOfThrowing()
    {
        Assert.Null(SpeechModelFolder.Ensure(null));
        Assert.Null(SpeechModelFolder.Ensure("   "));
    }

    /// <summary>The button and the hints must name the same folder.</summary>
    [Fact]
    public void TheFolderTheButtonOpensIsTheFolderTheAppSearches()
        => Assert.Equal(SpeechService.ModelRoot, SpeechModelFolder.Root);
}
