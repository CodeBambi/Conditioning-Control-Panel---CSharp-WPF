using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The audio clip pool, on a real folder.
///
/// <para><b>Why this file exists, and it is a code-review correction.</b> The pool shipped with no
/// facts at all, and the one behaviour it owns is <b>the stated ground of this packet's dot
/// ruling</b>: "dropping a clip in mid-session starts producing sound with no re-arm" is why an empty
/// clip folder takes the SUBLIMINALS answer (<c>Degraded</c> arm, <c>Live</c> dot) rather than the
/// PINK FILTER answer. That sentence is printed to the user in the arm reason
/// (<see cref="AudioCueEffect"/>) and on the panel (<see cref="Views.Pages.AudioPanelNotices"/>), and
/// the module-level fact that names the case runs on a stub whose count is a CONSTANT — it cannot
/// re-read, so it could not have failed if the cache broke. These facts touch the real filesystem
/// for exactly that reason, on the <c>FlashEffectTests</c> precedent.</para>
///
/// <para>Content-free throughout: counts and extensions, never the bytes of anything.</para>
/// </summary>
public class AudioCuePoolTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ccp-sp109-pool-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }

    // =================================================================================
    //  D104 — a missing folder is EMPTY, and is NOT created
    // =================================================================================

    [Fact]
    public void AMissingFolderReadsEmpty_AndTheScanCreatesNOTHINGOnDisk()
    {
        // The assets root exists and is empty; the module's clip folder under it does not exist.
        Directory.CreateDirectory(Assets);
        var pool = new AudioCuePool(Assets, MindWipeEffect.ClipFolderName);

        Assert.Equal(0, pool.ActiveCount);
        Assert.Null(pool.Draw());

        // THE DIVERGENCE, ASSERTED. WPF creates the folder during its scan so the user can find it
        // (MindWipeService.cs:153-157); the port does not, because a pool scan that writes to disk is
        // a side effect in the middle of a read. The panel names the path instead, which is the shape
        // the flash panel already uses. Recorded as D104, and this is the fact behind it.
        //
        // Phrased as "the scan created nothing ANYWHERE under the assets root" rather than "the clip
        // folder does not exist": it is the stronger claim (a pool that created some other directory
        // would still pass the narrow one), and it keeps a filesystem-existence PREDICATE out of a
        // fact body, which the vacuous-shape detector reads as a silencing shape whether or not it
        // is one here.
        Assert.Empty(Directory.EnumerateFileSystemEntries(Assets, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void TheFolderIsUnderTheSameUserMediaRootEveryOtherPoolReads()
    {
        var mindWipe = new AudioCuePool(Assets, MindWipeEffect.ClipFolderName);
        var brainDrain = new AudioCuePool(Assets, BrainDrainEffect.ClipFolderName);

        // Upstream's own folder names (MindWipeService.cs:149, BrainDrainService.cs:75), under the
        // port's one assets root rather than beside the executable.
        Assert.Equal(Path.Combine(Assets, "sounds", "mindwipe"), mindWipe.Folder);
        Assert.Equal(Path.Combine(Assets, "sounds", "braindrain"), brainDrain.Folder);

        // And the two modules never share a pool: one module's clips can never be drawn by the other.
        Assert.NotEqual(mindWipe.Folder, brainDrain.Folder);
    }

    // =================================================================================
    //  the extension filter, and the enumeration depth
    // =================================================================================

    [Fact]
    public void OnlyTheThreeUpstreamExtensionsCount_AndTheMatchIsCaseInsensitive()
    {
        var folder = ClipFolder(MindWipeEffect.ClipFolderName);
        Write(folder, "a.mp3");
        Write(folder, "b.WAV");
        Write(folder, "c.Ogg");
        // Every one of these is ignored by upstream's own Where clause
        // (MindWipeService.cs:162-165, BrainDrainService.cs:87-91).
        Write(folder, "d.flac");
        Write(folder, "e.m4a");
        Write(folder, "notes.txt");
        Write(folder, "cover.png");
        Write(folder, "noextension");

        var pool = new AudioCuePool(Assets, MindWipeEffect.ClipFolderName);

        Assert.Equal(3, pool.ActiveCount);
        for (var i = 0; i < 25; i++)
        {
            var drawn = pool.Draw();
            Assert.NotNull(drawn);
            Assert.Contains(
                Path.GetExtension(drawn), new[] { ".mp3", ".WAV", ".Ogg" }, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void EnumerationIsTOPLEVELONLY_BecauseUpstreamsIs()
    {
        var folder = ClipFolder(MindWipeEffect.ClipFolderName);
        Write(folder, "top.mp3");
        var nested = Path.Combine(folder, "archive");
        Directory.CreateDirectory(nested);
        Write(nested, "buried.mp3");
        Write(nested, "also-buried.wav");

        var pool = new AudioCuePool(Assets, MindWipeEffect.ClipFolderName);

        // `Directory.GetFiles(folder, "*.*")` with no SearchOption is TopDirectoryOnly
        // (MindWipeService.cs:162, BrainDrainService.cs:87). An earlier draft of the port recursed,
        // which would have made a subfolder of clips audible here and silent in the shipping app —
        // a behaviour difference with no upstream evidence behind it. Found by the code review.
        Assert.Equal(1, pool.ActiveCount);
        Assert.Equal(Path.Combine(folder, "top.mp3"), pool.Draw());
    }

    // =================================================================================
    //  THE LATE DROP — the case this packet's dot ruling rests on
    // =================================================================================

    [Fact]
    public void AClipDroppedInAfterAnEmptyRead_IsPickedUpOnTheNextDraw_WithNoReArm()
    {
        var pool = new AudioCuePool(Assets, MindWipeEffect.ClipFolderName);

        // The state a fresh install is in, and the state the arm result calls `audio-no-clip`.
        Assert.Equal(0, pool.ActiveCount);
        Assert.Null(pool.Draw());

        var folder = ClipFolder(MindWipeEffect.ClipFolderName);
        Write(folder, "late.mp3");

        // NOTHING was re-armed, invalidated or restarted between those two lines. This is the whole
        // reason an empty clip folder is `Degraded`/`Live` rather than `Degraded`/`Armed`: a pool is
        // CONTENT and a device is a CHANNEL, and content can arrive mid-session. If the cache stopped
        // re-reading while empty, this fact reds and the dot ruling loses its ground.
        Assert.Equal(1, pool.ActiveCount);
        Assert.Equal(Path.Combine(folder, "late.mp3"), pool.Draw());
    }

    [Fact]
    public void ANonEmptyPoolIsCACHED_AndInvalidateIsWhatRereadsIt()
    {
        var folder = ClipFolder(MindWipeEffect.ClipFolderName);
        Write(folder, "first.mp3");
        var pool = new AudioCuePool(Assets, MindWipeEffect.ClipFolderName);
        Assert.Equal(1, pool.ActiveCount);

        Write(folder, "second.mp3");

        // The OTHER half of the cache rule, and it is the boundary rather than an accident: the
        // enumeration is re-read only while it is EMPTY, so a session does not stat the folder on
        // every ten-second window. Upstream caches harder still — it enumerates once in its
        // constructor and re-reads only on ReloadAudioFiles (MindWipeService.cs:133, :189-192) — so
        // the port's rule is strictly friendlier and this is where it stops being friendly.
        Assert.Equal(1, pool.ActiveCount);

        pool.Invalidate();
        Assert.Equal(2, pool.ActiveCount);
    }

    [Fact]
    public void AnUnreadableFolderIsAnEMPTYPool_NeverAFault()
    {
        // Upstream logs and carries on with an empty array (MindWipeService.cs:179-182). A pool that
        // threw would turn a permissions quirk into a faulted session.
        var folder = ClipFolder(MindWipeEffect.ClipFolderName);
        Write(folder, "ok.mp3");
        var pool = new AudioCuePool(Assets, MindWipeEffect.ClipFolderName);
        Assert.Equal(1, pool.ActiveCount);

        // Replacing the folder with a FILE of the same name is the portable way to make the
        // enumeration fail: EnumerateFiles on a path that is not a directory is an IOException, and
        // it needs no privilege to arrange.
        Directory.Delete(folder, recursive: true);
        File.WriteAllText(folder, "not a directory");
        pool.Invalidate();

        Assert.Equal(0, pool.ActiveCount);
        Assert.Null(pool.Draw());
    }

    [Fact]
    public void TheDrawIsUniformWithReplacement_WhichIsUpstreamsOwnPick()
    {
        var folder = ClipFolder(MindWipeEffect.ClipFolderName);
        Write(folder, "a.mp3");
        Write(folder, "b.mp3");
        Write(folder, "c.mp3");
        var pool = new AudioCuePool(Assets, MindWipeEffect.ClipFolderName, new Random(1109));

        var drawn = new List<string>();
        for (var i = 0; i < 200; i++)
        {
            drawn.Add(pool.Draw()!);
        }

        // `_audioFiles[_random.Next(_audioFiles.Length)]` (MindWipeService.cs:755,
        // BrainDrainService.cs:197) — one independent pick per firing, WITH replacement, so the same
        // clip can legitimately come up twice running and a user hears that.
        Assert.Equal(3, drawn.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(
            Enumerable.Range(0, drawn.Count - 1), i => string.Equals(drawn[i], drawn[i + 1], StringComparison.Ordinal));
    }

    // =================================================================================

    private string Assets => SessionParticipant.AssetsRootFor(_root);

    private string ClipFolder(string module)
    {
        var folder = Path.Combine(Assets, AudioCuePool.SoundsFolderName, module);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void Write(string folder, string name) =>
        File.WriteAllBytes(Path.Combine(folder, name), [0x00, 0x01]);
}
