using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Haptics;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Dose (Services/Haptics/LockdownDoseKeeper.cs) - the pure half: which features a round
/// conscripts, how many, and how fast the grace shrinks. The runtime half (engine start/stop,
/// SetWallFeature, recovery file) is WPF-bound and is exercised by the play-test, not here.
/// </summary>
public class LockdownDoseKeeperTests
{
    private static readonly string[] Starter = { "flash", "subliminal", "spiral", "pinkfilter", "bouncingtext", "bubbles" };
    private static readonly string[] Escalation = { "video" };

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    [InlineData(4, 4)]
    [InlineData(9, 4)]
    public void WantedFor_TwoThenOneMorePerRound_CapsAtFour(int round, int expected)
        => Assert.Equal(expected, LockdownDoseKeeper.WantedFor(round));

    [Theory]
    [InlineData(0, 6)]
    [InlineData(1, 4)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(10, 2)]
    public void DoseGrace_ShrinksPerRound_FloorsAtTwoSeconds(int roundsSoFar, int expected)
        => Assert.Equal(expected, LockdownDoseKeeper.DoseGraceFor(roundsSoFar));

    [Fact]
    public void Round1_PicksTwoStarters_WhenTheUserHadNothingOn()
    {
        var picks = LockdownDoseKeeper.PickConscripts(1, Array.Empty<string>(), Starter, Escalation,
            Array.Empty<string>(), new Random(7));

        Assert.Equal(2, picks.Count);
        Assert.All(picks, k => Assert.Contains(k, Starter));
        Assert.Equal(picks.Count, picks.Distinct().Count());
    }

    [Fact]
    public void Round1_TurnsTheUsersOwnFeaturesBackOnFirst()
    {
        // They had flash + bubbles on at activation and switched both off: those come back before
        // anything else is invented for them.
        var picks = LockdownDoseKeeper.PickConscripts(1, new[] { "bubbles", "flash" }, Starter, Escalation,
            Array.Empty<string>(), new Random(3));

        Assert.Equal(2, picks.Count);
        Assert.Contains("flash", picks);
        Assert.Contains("bubbles", picks);
    }

    [Fact]
    public void NeverPicksWhatIsAlreadyOn()
    {
        var on = new[] { "flash", "subliminal" };
        var picks = LockdownDoseKeeper.PickConscripts(2, Array.Empty<string>(), Starter, Escalation, on, new Random(1));

        Assert.Equal(3, picks.Count);
        Assert.DoesNotContain("flash", picks);
        Assert.DoesNotContain("subliminal", picks);
    }

    [Fact]
    public void Round1_NeverReachesTheEscalationPool()
    {
        for (int seed = 0; seed < 50; seed++)
        {
            var picks = LockdownDoseKeeper.PickConscripts(1, Array.Empty<string>(), Starter, Escalation,
                Array.Empty<string>(), new Random(seed));
            Assert.DoesNotContain("video", picks);
        }
    }

    [Fact]
    public void Round2Plus_CanReachTheEscalationPool_WhenStartersRunOut()
    {
        // Everything in the starter pool is already on: the only thing left to add is video.
        var picks = LockdownDoseKeeper.PickConscripts(2, Array.Empty<string>(), Starter, Escalation, Starter, new Random(5));
        Assert.Equal(new[] { "video" }, picks);
    }

    [Fact]
    public void UnknownPreviouslyOnKeys_AreIgnored()
    {
        // A key the catalog does not know (a Tier 2 feature, a typo, a future flag) is never "picked".
        var picks = LockdownDoseKeeper.PickConscripts(1, new[] { "braindrain", "nope" }, Starter, Escalation,
            Array.Empty<string>(), new Random(2));
        Assert.DoesNotContain("braindrain", picks);
        Assert.DoesNotContain("nope", picks);
        Assert.Equal(2, picks.Count);
    }

    [Fact]
    public void NothingLeftToPick_ReturnsEmpty_NotAnException()
    {
        var all = Starter.Concat(Escalation).ToArray();
        var picks = LockdownDoseKeeper.PickConscripts(3, all, Starter, Escalation, all, new Random(0));
        Assert.Empty(picks);
    }

    [Fact]
    public void Deterministic_UnderTheSameSeed()
    {
        var a = LockdownDoseKeeper.PickConscripts(2, new[] { "spiral" }, Starter, Escalation, Array.Empty<string>(), new Random(42));
        var b = LockdownDoseKeeper.PickConscripts(2, new[] { "spiral" }, Starter, Escalation, Array.Empty<string>(), new Random(42));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Catalog_TierZeroIsTheStarterMix_AndEveryKeyIsAWallKey()
    {
        var tier0 = LockdownDoseKeeper.Catalog.Where(f => f.Tier == 0).Select(f => f.Key).ToArray();
        Assert.Equal(Starter.OrderBy(k => k), tier0.OrderBy(k => k));

        // Keys are the wall keys MainWindow.SetWallFeature switches on; a catalog entry it does not
        // know would be a conscription that flips nothing.
        var wallKeys = new HashSet<string> { "flash", "video", "subliminal", "spiral", "pinkfilter", "bubbles",
            "lockcard", "bubblecount", "bouncingtext", "mindwipe", "braindrain" };
        Assert.All(LockdownDoseKeeper.Catalog, f => Assert.Contains(f.Key, wallKeys));
    }

    // =============================================================================================
    //  The census - what counts as "something is running"
    // =============================================================================================

    /// <summary>A room with nothing on: every wall toggle and every off-wall dose switched off.</summary>
    private static AppSettings QuietRoom() => new()
    {
        FlashEnabled = false,
        SubliminalEnabled = false,
        SpiralEnabled = false,
        PinkFilterEnabled = false,
        BouncingTextEnabled = false,
        BubblesEnabled = false,
        MandatoryVideosEnabled = false,
        MindWipeEnabled = false,
        LockCardEnabled = false,
        BubbleCountEnabled = false,
        BrainDrainEnabled = false,
        PopQuizEnabled = false,
        AudioOnlySession = false,
        AutonomyModeEnabled = false,
        AutonomyConsentGiven = false,
        CornerGifOverlays = new List<CornerGifOverlaySetting>(),
    };

    [Fact]
    public void PopQuizAlone_IsNotAnEmptyRoom()
    {
        // StartEngine starts PopQuiz off PopQuizEnabled like any wall feature, so a lockdown running
        // nothing but Pop Quiz used to get a false `starve` tripwire plus a conscription on top of a
        // feature that was genuinely running.
        var s = QuietRoom();
        Assert.True(LockdownDoseKeeper.DoseIsEmpty(s));

        s.PopQuizEnabled = true;
        Assert.False(LockdownDoseKeeper.DoseIsEmpty(s));
        Assert.True(LockdownDoseKeeper.CountsAsOffWallDose(s));
    }

    [Fact]
    public void PopQuiz_CountsButIsNeverConscriptable()
    {
        // It is not a wall card, so MainWindow.SetWallFeature does not know the key: a catalog entry
        // would be a conscription that flips nothing. It lives in the off-wall census instead.
        Assert.DoesNotContain(LockdownDoseKeeper.Catalog,
            f => string.Equals(f.Key, "popquiz", StringComparison.OrdinalIgnoreCase));

        var picks = LockdownDoseKeeper.PickConscripts(3, new[] { "popquiz" }, Starter, Escalation,
            Array.Empty<string>(), new Random(11));
        Assert.DoesNotContain("popquiz", picks);
    }

    [Theory]
    [InlineData("clip.mp4", true)]
    [InlineData("CLIP.MP4", true)]
    [InlineData("a.mov", true)]
    [InlineData("a.avi", true)]
    [InlineData("a.wmv", true)]
    [InlineData("a.mkv", true)]
    [InlineData("a.webm", true)]
    [InlineData("Thumbs.db", false)]
    [InlineData("desktop.ini", false)]
    [InlineData("clip.ccpenh.json", false)]
    [InlineData("cover.jpg", false)]
    [InlineData("notes", false)]
    public void OnlyFilesVideoServiceCouldPlay_CountAsVideoAssets(string name, bool playable)
    {
        // Counting ANY file let round 2 conscript Mandatory Videos over a folder holding nothing but
        // Thumbs.db and enhancement sidecars. The list mirrors VideoService.RefillVideoQueues.
        Assert.Equal(playable, LockdownDoseKeeper.IsPlayableVideoFile(name));
    }

    // =============================================================================================
    //  Source pins - the runtime half is WPF-bound, so these hold the shape the play-test verified
    // =============================================================================================

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string Source(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()));

    [Fact]
    public void Restore_MarshalsBeforeReadingTheWindow_AndOwnsTheRecoveryFile()
    {
        var src = Source("Services", "Haptics", "LockdownDoseKeeper.cs");
        var start = src.IndexOf("private void Restore()", StringComparison.Ordinal);
        Assert.True(start > 0, "Restore() not found");
        var end = src.IndexOf("Crash recovery", start, StringComparison.Ordinal);
        Assert.True(end > start, "end of Restore() not found");
        var body = src[start..end];

        // The thread guard has to come FIRST: MainWindow's getter VerifyAccess-throws off-thread, so
        // reading it above the guard made the marshalling branch unreachable.
        var guard = body.IndexOf("dispatcher.CheckAccess()", StringComparison.Ordinal);
        var read = body.IndexOf("App.MainWindowRef", StringComparison.Ordinal);
        Assert.True(guard > 0 && read > 0 && guard < read,
            "Restore must marshal to the UI thread before it touches MainWindow");

        // The recovery file is the record of what is still switched on: it may only go when the
        // toggles are actually back, never in a finally that runs while the restore is still queued.
        Assert.Contains("if (stillFlipped.Count == 0) DeleteRecoveryFile();", body, StringComparison.Ordinal);
        Assert.Contains("else WriteRecoveryFile();", body, StringComparison.Ordinal);

        var deactivate = src.IndexOf("private void OnDeactivated()", StringComparison.Ordinal);
        var deactivateBody = src[deactivate..start];
        Assert.DoesNotContain("finally { DeleteRecoveryFile(); }", deactivateBody, StringComparison.Ordinal);
    }

    [Fact]
    public void AKeeperStartedEngine_SkipsTheUserOnlySideEffects()
    {
        var engine = Source("MainWindow", "MainWindow.StartStop.cs");
        Assert.Contains("public void StartEngine(bool systemInitiated = false)", engine, StringComparison.Ordinal);
        Assert.Contains("if (!systemInitiated) App.Achievements?.CheckRelapse();", engine, StringComparison.Ordinal);
        Assert.Contains("if (!systemInitiated) settings.TotalSessions++;", engine, StringComparison.Ordinal);
        Assert.Contains("if (!systemInitiated) MaybePromptMandatoryVideoEnhancement();", engine, StringComparison.Ordinal);

        // The duplicated Pop Quiz start block: two identical blocks started the service twice.
        Assert.Equal(1, engine.Split("App.PopQuiz?.Start();").Length - 1);

        var keeper = Source("Services", "Haptics", "LockdownDoseKeeper.cs");
        Assert.Contains("mw.StartEngine(systemInitiated: true);", keeper, StringComparison.Ordinal);
    }

    [Fact]
    public void StandDown_PreservesTheEmptyEdge()
    {
        // A session owning the dose used to rewrite _wasEmpty every tick, consuming the edge: a
        // session ending with everything off never fired `starve` and the room refilled silently.
        var src = Source("Services", "Haptics", "LockdownDoseKeeper.cs");
        var start = src.IndexOf("if (mw.IsSessionFeatureLockActive)", StringComparison.Ordinal);
        Assert.True(start > 0, "stand-down branch not found");
        var body = src.Substring(start, Math.Min(400, src.Length - start));
        Assert.DoesNotContain("_wasEmpty =", body, StringComparison.Ordinal);
    }
}
