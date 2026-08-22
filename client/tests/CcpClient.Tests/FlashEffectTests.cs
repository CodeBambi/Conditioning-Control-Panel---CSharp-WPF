using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The parts of Flash Images that are pure enough to pin exactly: the pacing law, the
/// persisted dials and their clamps, and the real image pool over a real folder.
///
/// <para>These are the behaviour-visible facts of the ported half. The half that is NOT ported
/// — putting the images on screen above other applications, which needs an always-on-top
/// click-through surface (<c>Services/Flash/FlashService.cs:3615,3667-3668,3862-3868</c>) —
/// has no test here and must not acquire one: a test would be the first thing to make the gap
/// look closed.</para>
/// </summary>
public class FlashEffectTests
{
    // ---------------------------------------------------------------------------------
    //  the pacing law (FlashService.cs:538-563)
    // ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(1, 3600.0)]
    [InlineData(10, 360.0)]     // WPF's default dial
    [InlineData(60, 60.0)]
    [InlineData(180, 20.0)]     // the dial's ceiling
    public void BaseInterval_IsWpfsSecondsPerHourOverTheDial(int perHour, double expectedSeconds)
    {
        // FlashService.cs:550 — `var baseInterval = 3600.0 / baseFreq;`, the whole law of the
        // frequency dial. If this ever drifts, every session in the product paces differently.
        Assert.Equal(expectedSeconds, FlashSchedule.BaseIntervalSeconds(perHour), precision: 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void ABadFrequency_HitsWpfsMaxOneGuard_RatherThanDividingByZero(int perHour)
    {
        // FlashService.cs:549 — `Math.Max(1, settings.FlashFrequency)`. The persisted dial
        // clamps at 1 already; this guard is what stops the function depending on its caller's
        // clamp, and removing it turns a bad caller into a DivideByZero or a negative delay.
        Assert.Equal(FlashSchedule.SecondsPerHour, FlashSchedule.BaseIntervalSeconds(perHour), precision: 6);
    }

    [Fact]
    public void EveryDraw_LandsInWpfsPlusMinusThirtyPercentBand_AndTheBandIsReallyUsed()
    {
        // FlashService.cs:552-554: variance = base * 0.3, then base + U(-variance, +variance).
        var random = new Random(20260818);
        var min = double.MaxValue;
        var max = double.MinValue;
        for (var i = 0; i < 5000; i++)
        {
            var seconds = FlashSchedule.NextInterval(10, random).TotalSeconds;
            min = Math.Min(min, seconds);
            max = Math.Max(max, seconds);
        }

        // 10/hour: base 360s, band [252, 468].
        Assert.InRange(min, 252.0, 468.0);
        Assert.InRange(max, 252.0, 468.0);
        // ...and it is a BAND, not a constant: a fixed interval would pass the bounds check
        // above while destroying the unpredictability the variance exists for.
        Assert.True(max - min > 200.0, $"the ±30% band collapsed: observed spread {max - min:F1}s over 5000 draws");
    }

    [Fact]
    public void TheThreeSecondFloor_IsAppliedAFTERTheVariance_NotBefore()
    {
        // FlashService.cs:555 — `interval = Math.Max(3, interval);` is the LAST line. At a
        // frequency whose whole band sits under 3s every draw is floored to exactly 3, which is
        // only true if the floor runs after the variance: applied first, the variance would
        // push draws back below 3.
        var random = new Random(4242);
        for (var i = 0; i < 500; i++)
        {
            Assert.Equal(
                FlashSchedule.MinimumIntervalSeconds,
                FlashSchedule.NextInterval(18000, random).TotalSeconds,
                precision: 6);
        }

        Assert.Equal(FlashSchedule.MinimumIntervalSeconds, FlashSchedule.MinimumInterval(18000).TotalSeconds, precision: 6);
        Assert.Equal(FlashSchedule.MinimumIntervalSeconds, FlashSchedule.MaximumInterval(18000).TotalSeconds, precision: 6);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(180)]
    public void TheAdvertisedBounds_ReallyBoundTheDraw(int perHour)
    {
        // MinimumInterval/MaximumInterval are what every clock-driven test advances by, so if
        // they were not true bounds the whole suite would be quietly timing-dependent.
        var random = new Random(7);
        var low = FlashSchedule.MinimumInterval(perHour);
        var high = FlashSchedule.MaximumInterval(perHour);
        var drawn = Enumerable.Range(0, 2000).Select(_ => FlashSchedule.NextInterval(perHour, random)).ToArray();

        // The population is asserted before it is swept (loop framing (c)): a sweep over an
        // empty sequence is the vacuous shape that passes while proving nothing.
        Assert.NotEmpty(drawn);
        Assert.All(drawn, d => Assert.InRange(d, low, high));
    }

    // ---------------------------------------------------------------------------------
    //  the persisted dials
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheDefaults_AreWpfsDefaults()
    {
        var preset = new SessionPresetDocument();

        // AppSettings.cs:751 (FlashEnabled default TRUE — a fresh install has flash armed),
        // :763 (10 per hour), :831 (5 images).
        Assert.True(preset.FlashEnabled);
        Assert.Equal(10, preset.FlashesPerHour);
        Assert.Equal(5, preset.ImagesPerFlash);
    }

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(180, 180)]
    [InlineData(181, 180)]
    [InlineData(int.MaxValue, 180)]
    public void FlashesPerHour_ClampsToWpfsOneToOneEighty(int written, int expected)
    {
        // AppSettings.cs:769 — `Math.Clamp(value, 1, 180)` in the SETTER, so a hand-edited file
        // is corrected on load rather than driving the scheduler out of range.
        Assert.Equal(expected, new SessionPresetDocument { FlashesPerHour = written }.FlashesPerHour);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(20, 20)]
    [InlineData(21, 20)]
    public void ImagesPerFlash_ClampsToWpfsOneToTwenty(int written, int expected)
    {
        // AppSettings.cs:835 — `Math.Clamp(value, 1, 20)`.
        Assert.Equal(expected, new SessionPresetDocument { ImagesPerFlash = written }.ImagesPerFlash);
    }

    [Fact]
    public async Task AnOutOfRangePresetFile_IsCorrectedOnLoad_AndUnknownMembersSurvive()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp098-preset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, SessionPresetDocument.FileName);
        await File.WriteAllTextAsync(
            path,
            """
            {
              "schemaVersion": 1,
              "flashEnabled": true,
              "flashesPerHour": 9999,
              "imagesPerFlash": 0,
              "somethingANewerBuildWrote": "keep me"
            }
            """,
            TestContext.Current.CancellationToken);

        var registry = new OperationRegistry();
        var store = new PersistenceStore<SessionPresetDocument>(
            registry.OwnerFor("PresetTest"), new NullLog(), path, SessionPresetDocument.CurrentSchemaVersion);
        await store.StartAsync(TestContext.Current.CancellationToken);

        Assert.IsType<LoadOutcome.Loaded>(store.LastLoadOutcome);
        Assert.Equal(SessionPresetDocument.MaxFlashesPerHour, store.Current.FlashesPerHour);
        Assert.Equal(SessionPresetDocument.MinImagesPerFlash, store.Current.ImagesPerFlash);

        // Persistence contract §6: an unknown member written by a newer build round-trips
        // verbatim rather than being dropped by this build's save.
        await store.SaveImmediate();
        var rewritten = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Contains("somethingANewerBuildWrote", rewritten, StringComparison.Ordinal);
        Assert.Contains("keep me", rewritten, StringComparison.Ordinal);

        Directory.Delete(dir, recursive: true);
    }

    // ---------------------------------------------------------------------------------
    //  the real pool over a real folder
    // ---------------------------------------------------------------------------------

    [Fact]
    public void AMissingFolder_DrawsNothing_AndNeverThrows()
    {
        // WPF's empty pool returns an empty list and the flash shows nothing
        // (FlashService.cs:2589-2593). A first-run user has no folder at all; that must be an
        // outcome, never an exception on a timer thread.
        var pool = new FlashImagePool(Path.Combine(Path.GetTempPath(), "ccp-sp098-absent-" + Guid.NewGuid().ToString("N")));

        Assert.Empty(pool.Draw(5));
        Assert.Empty(pool.Draw(0));
    }

    [Fact]
    public void TheDraw_TakesIndependentPicks_WithReplacement()
    {
        using var folder = new TempAssets("only.png");
        var pool = new FlashImagePool(folder.Root, random: new Random(1));

        var drawn = pool.Draw(5);

        // WPF picks `count` times independently (FlashService.cs:2647-2652), so a one-image
        // pool legitimately fills a whole flash with the same image. Returning ONE would be a
        // slideshow, which is a different feature.
        Assert.Equal(5, drawn.Count);
        Assert.All(drawn, p => Assert.EndsWith("only.png", p, StringComparison.Ordinal));
    }

    [Fact]
    public void TheDraw_ReachesEveryActiveImage_AndOnlyImageFiles()
    {
        using var folder = new TempAssets("a.png", "b.jpg", "c.webp", "notes.txt", "clip.mp4");
        var pool = new FlashImagePool(folder.Root, random: new Random(99));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < 400; i++)
        {
            foreach (var path in pool.Draw(3))
            {
                seen.Add(Path.GetFileName(path));
            }
        }

        Assert.Equal(new[] { "a.png", "b.jpg", "c.webp" }.Order(), seen.Order());
    }

    [Fact]
    public void ADeselectedImage_IsNeverDrawn_ThroughThePortsOneActivePoolSeam()
    {
        using var folder = new TempAssets("keep.png", "dropped.png");
        // Whether a file is in play is decided by DtrhUserMedia.IsAssetActive, the ONE
        // seam every consumer shares — so the same uncheck that hides an image from the descent
        // hides it from a flash. The path is assets-root-relative with '/' separators, which is
        // the normalization that seam ports verbatim.
        var pool = new FlashImagePool(
            folder.Root,
            () => (new[] { "images/dropped.png" }, true),
            new Random(3));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < 200; i++)
        {
            foreach (var path in pool.Draw(4))
            {
                seen.Add(Path.GetFileName(path));
            }
        }

        Assert.Equal(["keep.png"], seen);
    }

    [Fact]
    public void TheWhitelistGateIsHonoured_ADeselectionWithTheGateOffChangesNothing()
    {
        using var folder = new TempAssets("keep.png", "dropped.png");
        // AppSettings.cs:1637's documented contract: the gate turns the whole mechanism on.
        // With it off, a deselection set is inert — which is the shipped default, so getting
        // this backwards would silently empty every user's pool.
        var pool = new FlashImagePool(
            folder.Root,
            () => (new[] { "images/dropped.png" }, false),
            new Random(3));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < 200; i++)
        {
            foreach (var path in pool.Draw(4))
            {
                seen.Add(Path.GetFileName(path));
            }
        }

        Assert.Equal(new[] { "dropped.png", "keep.png" }.Order(), seen.Order());
    }

    [Fact]
    public void AnEmptyFolderIsRecheckedOnEveryDraw_SoImagesDroppedInMidSessionAreFound()
    {
        using var folder = new TempAssets();
        var pool = new FlashImagePool(folder.Root, random: new Random(11));
        Assert.Empty(pool.Draw(3));

        folder.Add("late.png");

        // WPF re-reads only when the pool is empty (FlashService.cs:2573-2578), which is
        // exactly what lets a user fix the most common first-run dead end without restarting.
        Assert.Equal(3, pool.Draw(3).Count);
    }

    private sealed class NullLog : ILogSink
    {
        public void Log(string message)
        {
        }
    }

    /// <summary>A real <c>&lt;root&gt;/images</c> folder with real files in it.</summary>
    private sealed class TempAssets : IDisposable
    {
        public TempAssets(params string[] fileNames)
        {
            Root = Path.Combine(Path.GetTempPath(), "ccp-sp098-pool-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "images"));
            foreach (var name in fileNames)
            {
                Add(name);
            }
        }

        public string Root { get; }

        public void Add(string fileName) =>
            File.WriteAllBytes(Path.Combine(Root, "images", fileName), [0x00]);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
