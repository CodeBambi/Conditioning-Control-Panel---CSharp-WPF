using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Haptics;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The Dose (Services/Haptics/LockdownDoseKeeper.cs) - the pure half: which features a round
/// conscripts, how many, and how fast the grace shrinks. The runtime half (engine start/stop,
/// SetWallFeature, recovery file) runs against a fake LockdownDoseHost at the end of this file.
/// </summary>
[Collection(SessionStatics.Name)]
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
    //  The runtime half, against a fake host (no window, no real engine)
    // =============================================================================================

    [Fact]
    public void AnEmptyLockdownGetsADoseAndAnEngine_AndBothAreGivenBack()
    {
        var s = CoreSettings.Current;
        var quiet = QuietRoom();
        var saved = (s.FlashEnabled, s.SubliminalEnabled, s.SpiralEnabled, s.PinkFilterEnabled, s.BouncingTextEnabled,
            s.BubblesEnabled, s.MandatoryVideosEnabled, s.MindWipeEnabled, s.LockCardEnabled, s.BubbleCountEnabled,
            s.BrainDrainEnabled, s.PopQuizEnabled, s.AudioOnlySession, s.AutonomyModeEnabled, s.CornerGifOverlays,
            s.LockdownDoseKeeperEnabled, s.StrictLockEnabled, s.PanicKeyEnabled);
        var prev = LockdownService.Current;
        var takeover = LockdownDoseKeeper.TakeoverRunningProvider;
        var dir = Directory.CreateTempSubdirectory("ccp-dose-").FullName;
        var recovery = Path.Combine(dir, "lockdown-dose.json");
        var ld = LockdownService.Current = new LockdownService();
        var engine = false;
        var flips = new List<(string Key, bool On)>();
        var barks = new List<(string Names, int Round, bool Engine)>();
        var host = new LockdownDoseHost
        {
            IsEngineRunning = () => engine,
            StartEngine = () => engine = true,
            StopEngine = () => engine = false,
            SetWallFeature = (key, on) =>
            {
                lock (flips) flips.Add((key, on));
                switch (key)
                {
                    case "flash": s.FlashEnabled = on; break;
                    case "subliminal": s.SubliminalEnabled = on; break;
                    case "spiral": s.SpiralEnabled = on; break;
                    case "pinkfilter": s.PinkFilterEnabled = on; break;
                    case "bouncingtext": s.BouncingTextEnabled = on; break;
                    case "bubbles": s.BubblesEnabled = on; break;
                    case "video": s.MandatoryVideosEnabled = on; break;
                }
            },
            Bark = (names, round, started) => { lock (barks) barks.Add((names, round, started)); },
            KickoffDelay = TimeSpan.FromMilliseconds(50),
            RecoveryPath = () => recovery,
            AssetsPath = () => dir,
        };
        using var keeper = new LockdownDoseKeeper(ld, host);
        try
        {
            LockdownDoseKeeper.TakeoverRunningProvider = () => false;
            (s.FlashEnabled, s.SubliminalEnabled, s.SpiralEnabled, s.PinkFilterEnabled, s.BouncingTextEnabled,
                s.BubblesEnabled, s.MandatoryVideosEnabled, s.MindWipeEnabled, s.LockCardEnabled, s.BubbleCountEnabled,
                s.BrainDrainEnabled, s.PopQuizEnabled, s.AudioOnlySession, s.AutonomyModeEnabled, s.CornerGifOverlays) =
                (false, false, false, false, false, false, false, false, false, false, false, false, false, false, quiet.CornerGifOverlays);
            s.LockdownDoseKeeperEnabled = true;
            keeper.Install();
            ld.Activate(TimeSpan.FromMinutes(20));
            Assert.True(keeper.IsArmed);
            for (var t = 0; t < 3000 && !engine; t += 25) System.Threading.Thread.Sleep(25);

            Assert.True(engine);                                   // the engine was started for the user
            Assert.Equal(1, keeper.Round);
            Assert.Equal(2, LockdownDoseKeeper.KeysOn(s).Count);   // round one conscripts two starters
            Assert.True(File.Exists(recovery));                    // a killed lockdown can still give them back
            var bark = Assert.Single(barks);
            Assert.True(bark.Engine && bark.Round == 1 && bark.Names.Contains(" and "));

            ld.Deactivate();
            Assert.False(keeper.IsArmed);
            Assert.Empty(LockdownDoseKeeper.KeysOn(s));            // every borrowed toggle is back off
            Assert.False(engine);                                  // and the engine it started is stopped
            Assert.False(File.Exists(recovery));
        }
        finally
        {
            if (ld.IsActive) ld.Deactivate();
            LockdownService.Current = prev;
            LockdownDoseKeeper.TakeoverRunningProvider = takeover;
            (s.FlashEnabled, s.SubliminalEnabled, s.SpiralEnabled, s.PinkFilterEnabled, s.BouncingTextEnabled,
                s.BubblesEnabled, s.MandatoryVideosEnabled, s.MindWipeEnabled, s.LockCardEnabled, s.BubbleCountEnabled,
                s.BrainDrainEnabled, s.PopQuizEnabled, s.AudioOnlySession, s.AutonomyModeEnabled, s.CornerGifOverlays,
                s.LockdownDoseKeeperEnabled, s.StrictLockEnabled, s.PanicKeyEnabled) = saved;
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void AnInterruptedLockdownsTogglesAreSwitchedBackOffAtTheNextLaunch()
    {
        var s = CoreSettings.Current;
        var saved = (s.FlashEnabled, s.SpiralEnabled);
        var dir = Directory.CreateTempSubdirectory("ccp-dose-").FullName;
        var recovery = Path.Combine(dir, "lockdown-dose.json");
        try
        {
            (s.FlashEnabled, s.SpiralEnabled) = (true, true);
            File.WriteAllText(recovery, "{\"Flipped\":[\"flash\"]}");
            LockdownDoseKeeper.RecoverIfNeeded(recovery);
            Assert.False(s.FlashEnabled);
            Assert.True(s.SpiralEnabled);     // not in the record: the user's own
            Assert.False(File.Exists(recovery));
        }
        finally
        {
            (s.FlashEnabled, s.SpiralEnabled) = saved;
            CoreSettings.SaveImmediate();
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
