using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-101 — the parts of Subliminals that are pure enough to pin exactly: the pacing law, the
/// persisted dials and their clamps, the phrase pool, and the card's duration arithmetic.
///
/// <para>Every number here is a place a COPIED template would have been silently wrong, which is
/// why the module was built: the dial counts per minute where the flash's counts per hour, the floor
/// is one second where the flash's is three, the module ships OFF where the flash ships on, and the
/// duration dial is expressed in frames with a floor that makes its shipped default inert.</para>
/// </summary>
public class SubliminalEffectTests
{
    // ---------------------------------------------------------------------------------
    //  the pacing law (SubliminalService.cs:172-187)
    // ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(1, 60.0)]
    [InlineData(5, 12.0)]      // WPF's default dial
    [InlineData(30, 2.0)]      // the dial's ceiling
    public void BaseInterval_IsWpfsSecondsPerMinuteOverTheDial(int perMinute, double expectedSeconds)
    {
        // SubliminalService.cs:177 — `var baseInterval = 60.0 / freq;`. Sixty, not three thousand
        // six hundred: the two modules count against different periods, and a template that shared
        // one constant would have paced subliminals sixty times too slowly with nothing failing.
        Assert.Equal(expectedSeconds, SubliminalSchedule.BaseIntervalSeconds(perMinute), precision: 6);
        Assert.NotEqual(FlashSchedule.BaseIntervalSeconds(perMinute), SubliminalSchedule.BaseIntervalSeconds(perMinute));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void ABadFrequency_HitsWpfsMaxOneGuard_RatherThanDividingByZero(int perMinute)
    {
        // SubliminalService.cs:175 — `Math.Max(1, App.Settings.Current.SubliminalFrequency)`, the
        // same guard the flash schedule has and for the same reason: the function must not depend
        // on its caller's clamp.
        Assert.Equal(
            SubliminalSchedule.SecondsPerMinute,
            SubliminalSchedule.BaseIntervalSeconds(perMinute),
            precision: 6);
    }

    [Fact]
    public void EveryDraw_LandsInWpfsPlusMinusThirtyPercentBand_AndTheBandIsReallyUsed()
    {
        // SubliminalService.cs:179-180: variance = base * 0.3, then base + U(-variance, +variance).
        var random = new Random(20260818);
        var min = double.MaxValue;
        var max = double.MinValue;
        for (var i = 0; i < 5000; i++)
        {
            var seconds = SubliminalSchedule.NextInterval(5, random).TotalSeconds;
            min = Math.Min(min, seconds);
            max = Math.Max(max, seconds);
        }

        // 5/minute: base 12 s, band [8.4, 15.6].
        Assert.InRange(min, 8.4, 15.6);
        Assert.InRange(max, 8.4, 15.6);
        // ...and it is a BAND, not a constant.
        Assert.True(max - min > 6.0, $"the ±30% band collapsed: observed spread {max - min:F2}s over 5000 draws");
    }

    [Fact]
    public void TheOneSecondFloor_IsAppliedAFTERTheVariance_AndIsNotTheFlashsThree()
    {
        // SubliminalService.cs:181 — `interval = Math.Max(1, interval);` is the LAST line, and the
        // constant is ONE. At a frequency whose whole band sits under a second every draw is
        // floored to exactly 1, which is only true if the floor runs after the variance.
        var random = new Random(4242);
        for (var i = 0; i < 500; i++)
        {
            Assert.Equal(
                SubliminalSchedule.MinimumIntervalSeconds,
                SubliminalSchedule.NextInterval(600, random).TotalSeconds,
                precision: 6);
        }

        Assert.Equal(1.0, SubliminalSchedule.MinimumIntervalSeconds);
        Assert.Equal(3.0, FlashSchedule.MinimumIntervalSeconds);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(30)]
    public void TheAdvertisedBounds_ReallyBoundTheDraw(int perMinute)
    {
        // MinimumInterval/MaximumInterval are what every clock-driven test advances by, so if they
        // were not true bounds the whole suite would be quietly timing-dependent.
        var random = new Random(7);
        var low = SubliminalSchedule.MinimumInterval(perMinute);
        var high = SubliminalSchedule.MaximumInterval(perMinute);
        var drawn = Enumerable.Range(0, 2000).Select(_ => SubliminalSchedule.NextInterval(perMinute, random)).ToArray();

        Assert.NotEmpty(drawn);
        Assert.All(drawn, d => Assert.InRange(d, low, high));
    }

    [Fact]
    public void TheSharedArithmetic_IsWhatBothModulesActuallyRunOn()
    {
        // The template's own claim, asserted rather than asserted-about: each module's named facade
        // is exactly the shared law applied to that module's three numbers. If a facade ever grew a
        // second copy of the formula, this is what would catch it.
        Assert.Equal(FlashSchedule.SecondsPerHour, FlashSchedule.Law.SecondsPerUnit);
        Assert.Equal(FlashSchedule.MinimumIntervalSeconds, FlashSchedule.Law.MinimumSeconds);
        Assert.Equal(SubliminalSchedule.SecondsPerMinute, SubliminalSchedule.Law.SecondsPerUnit);
        Assert.Equal(SubliminalSchedule.MinimumIntervalSeconds, SubliminalSchedule.Law.MinimumSeconds);

        Assert.Equal(
            EffectSchedule.NextInterval(FlashSchedule.Law, 10, new Random(11)),
            FlashSchedule.NextInterval(10, new Random(11)));
        Assert.Equal(
            EffectSchedule.NextInterval(SubliminalSchedule.Law, 5, new Random(11)),
            SubliminalSchedule.NextInterval(5, new Random(11)));
    }

    // ---------------------------------------------------------------------------------
    //  the persisted dials
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheDefaults_AreWpfsDefaults_AndTheModuleShipsOFF()
    {
        var preset = new SubliminalPresetDocument();

        // AppSettings.cs:1234 (SubliminalEnabled default FALSE — the OPPOSITE of FlashEnabled at
        // :751, and the reason WPF's StartEngine gates this module's start on the flag at
        // MainWindow.StartStop.cs:186 while calling the flash service unconditionally at :178),
        // :1242 (5 per minute), :1249 (2 frames), :1256 (80 %).
        Assert.False(preset.Enabled);
        Assert.True(new SessionPresetDocument().FlashEnabled);
        Assert.Equal(5, preset.PerMinute);
        Assert.Equal(2, preset.DurationFrames);
        Assert.Equal(80, preset.OpacityPercent);
    }

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(30, 30)]
    [InlineData(31, 30)]
    [InlineData(int.MaxValue, 30)]
    public void PerMinute_ClampsToWpfsOneToThirty(int written, int expected)
    {
        // AppSettings.cs:1246 — `Math.Clamp(value, 1, 30)` in the SETTER, so a hand-edited file is
        // corrected on load rather than driving the scheduler out of range.
        Assert.Equal(expected, new SubliminalPresetDocument { PerMinute = written }.PerMinute);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10, 10)]
    [InlineData(11, 10)]
    public void DurationFrames_ClampsToWpfsOneToTen(int written, int expected)
    {
        // AppSettings.cs:1253 — `Math.Clamp(value, 1, 10)`.
        Assert.Equal(expected, new SubliminalPresetDocument { DurationFrames = written }.DurationFrames);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(9, 10)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    public void Opacity_ClampsToWpfsTenToOneHundred(int written, int expected)
    {
        // AppSettings.cs:1260 — `Math.Clamp(value, 10, 100)`. The FLOOR is ten, not zero: upstream
        // will not let a user configure an invisible subliminal.
        Assert.Equal(expected, new SubliminalPresetDocument { OpacityPercent = written }.OpacityPercent);
    }

    [Fact]
    public async Task AnOutOfRangePresetFile_IsCorrectedOnLoad_AndUnknownMembersSurvive()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp101-preset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, SubliminalPresetDocument.FileName);
        await File.WriteAllTextAsync(
            path,
            """
            {
              "schemaVersion": 1,
              "enabled": true,
              "perMinute": 9999,
              "durationFrames": 0,
              "opacityPercent": 1,
              "phrases": { "ONLY THIS ONE": true, "AND NOT THIS ONE": false },
              "somethingANewerBuildWrote": "keep me"
            }
            """,
            TestContext.Current.CancellationToken);

        var registry = new OperationRegistry();
        var store = new PersistenceStore<SubliminalPresetDocument>(
            registry.OwnerFor("SubliminalPresetTest"), new NullSink(), path,
            SubliminalPresetDocument.CurrentSchemaVersion);
        await store.StartAsync(TestContext.Current.CancellationToken);

        Assert.IsType<LoadOutcome.Loaded>(store.LastLoadOutcome);
        Assert.Equal(SubliminalPresetDocument.MaxPerMinute, store.Current.PerMinute);
        Assert.Equal(SubliminalPresetDocument.MinDurationFrames, store.Current.DurationFrames);
        Assert.Equal(SubliminalPresetDocument.MinOpacityPercent, store.Current.OpacityPercent);

        // A user's own pool replaces the shipped one outright — it is not merged, because merging
        // would resurrect a phrase the user removed (upstream needs a whole RemovedDefaultSubliminals
        // set to undo exactly that mistake, AppSettings.cs:1292-1302).
        Assert.Equal(["ONLY THIS ONE"], store.Current.ActivePhrases());

        // Persistence contract §6: an unknown member written by a newer build round-trips verbatim.
        await store.SaveImmediate();
        var rewritten = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Contains("somethingANewerBuildWrote", rewritten, StringComparison.Ordinal);

        Directory.Delete(dir, recursive: true);
    }

    // ---------------------------------------------------------------------------------
    //  the phrase pool
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheShippedPool_IsWpfsTwentyOnePhrases_AllActive()
    {
        // AppSettings.cs:1263-1285. The pool IS the feature: a subliminal module with a different
        // set of words is a different product, so the list is ported as data and pinned here.
        var preset = new SubliminalPresetDocument();

        Assert.Equal(21, SubliminalPresetDocument.DefaultPhrases.Count);
        Assert.Equal(21, preset.Phrases.Count);
        Assert.Equal(21, preset.ActivePhrases().Count);
        Assert.Equal("BAMBI FREEZE", SubliminalPresetDocument.DefaultPhrases[0]);
        Assert.Equal("THERES NO NEED TO THINK", SubliminalPresetDocument.DefaultPhrases[^1]);
    }

    [Fact]
    public void TheDraw_ReachesEveryActivePhrase_AndNeverAnInactiveOne()
    {
        var preset = new SubliminalPresetDocument
        {
            Phrases = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["KEEP ONE"] = true,
                ["KEEP TWO"] = true,
                ["DROPPED"] = false,
            },
        };
        var pool = new SubliminalPhrasePool(StoreOver(preset), new Random(99));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 400; i++)
        {
            var drawn = pool.Draw();
            Assert.NotNull(drawn);
            seen.Add(drawn!);
        }

        // WPF filters on the pool's own bool before picking (SubliminalService.cs:206-207).
        Assert.Equal(new[] { "KEEP ONE", "KEEP TWO" }.Order(), seen.Order());
        Assert.Equal(2, pool.ActiveCount);
    }

    [Fact]
    public void AnEmptyPool_DrawsNothing_AndNeverThrows()
    {
        // WPF logs "No active subliminal texts" and returns (SubliminalService.cs:209-213). It is
        // an outcome, never an exception on a timer thread.
        var preset = new SubliminalPresetDocument
        {
            Phrases = new Dictionary<string, bool>(StringComparer.Ordinal) { ["OFF"] = false },
        };
        var pool = new SubliminalPhrasePool(StoreOver(preset));

        Assert.Null(pool.Draw());
        Assert.Equal(0, pool.ActiveCount);
    }

    // ---------------------------------------------------------------------------------
    //  the card's duration arithmetic
    // ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(1, 100)]      // 17 ms, floored
    [InlineData(2, 100)]      // WPF's shipped default: 34 ms, floored — the dial is inert here
    [InlineData(5, 100)]      // 85 ms, still floored
    [InlineData(6, 102)]      // 102 ms — the first frame count the dial actually decides
    [InlineData(10, 170)]
    public void TheHold_IsWpfsMaxOneHundredOverSeventeenMillisecondFrames(int frames, int expected)
    {
        // SubliminalService.cs:615-617 — `Math.Max(100, SubliminalDuration * 17)`. The integer 17
        // is the behaviour even though the comment says ~16.6, and the floor means the shipped
        // default of 2 frames shows for 100 ms rather than 34.
        Assert.Equal(expected, SubliminalsEffect.HoldMilliseconds(frames));
    }

    [Fact]
    public void TheCardOccupiesWpfsWholeEnvelope_FadeInPlusHoldPlusFadeOut()
    {
        // SubliminalService.cs:1253-1255: 50 ms in, hold, 50 ms out. The port shows the card at a
        // constant opacity for that whole span instead of ramping (recorded divergence), so the
        // DURATION is the half that must stay exact.
        Assert.Equal(TimeSpan.FromMilliseconds(200), SubliminalsEffect.CardLifetime(2));
        Assert.Equal(TimeSpan.FromMilliseconds(270), SubliminalsEffect.CardLifetime(10));
        Assert.Equal(50, SubliminalSurfacePresenter.FadeInMilliseconds);
        Assert.Equal(50, SubliminalSurfacePresenter.FadeOutMilliseconds);
    }

    private static PersistenceStore<SubliminalPresetDocument> StoreOver(SubliminalPresetDocument document)
    {
        var registry = new OperationRegistry();
        var store = new PersistenceStore<SubliminalPresetDocument>(
            registry.OwnerFor("SubliminalPoolTest-" + Guid.NewGuid().ToString("N")),
            new NullSink(),
            Path.Combine(Path.GetTempPath(), "ccp-sp101-pool-" + Guid.NewGuid().ToString("N") + ".json"),
            SubliminalPresetDocument.CurrentSchemaVersion);
        store.Mutate(p =>
        {
            p.Enabled = document.Enabled;
            p.PerMinute = document.PerMinute;
            p.DurationFrames = document.DurationFrames;
            p.OpacityPercent = document.OpacityPercent;
            p.Phrases = document.Phrases;
        });
        return store;
    }

    private sealed class NullSink : ILogSink
    {
        public void Log(string message)
        {
        }
    }
}
