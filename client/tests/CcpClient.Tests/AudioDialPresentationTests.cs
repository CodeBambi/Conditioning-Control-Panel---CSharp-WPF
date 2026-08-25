using CcpVerify;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Floor guards over the AUDIO row's two manifest checks (<c>client/tools/verify/checks.json</c>),
/// on <see cref="StudioDialPresentationTests"/>' precedent and for the inverse claim.
///
/// <para><b>The claim these two checks carry is that a running scripted session leaves the audio
/// dials ALONE.</b> <c>audio.json</c> is not one of the eleven documents a run borrows
/// (<c>Session/ScriptedSessionDials.cs</c>'s constructor) and upstream classes volumes as COMFORT
/// rather than dosage, naming audio volume in its own never-lock list
/// (<c>MainWindow/MainWindow.SessionFeatureLock.cs:39-42</c>,
/// <c>Features/SessionLock.cs:21-38</c>). Over-locking is a regression in its own right
/// (<c>:36-38</c>) and it is invisible to every other evidence class here: a greyed dial and a live
/// one are the same control at the same value, and the whole difference is composited pixels.</para>
///
/// <para><b>So the inversion is ACROSS SURFACES rather than across states</b>, which is new on this
/// manifest: both audio states are deliberately the same livery, and what each must fail is
/// <c>studio-dial-locked-track</c> — taken from a capture of a DIFFERENT dial under the SAME
/// running session.</para>
///
/// <para><b>What these are and are not.</b> They are LEXICAL guards over a JSON file on disk. They
/// prove the two checks still exist, still claim the class no headless frame can discharge, still
/// refuse the locked livery, and still keep their fraction floor well under what was measured. They
/// prove nothing about pixels — the captures do that, and the record is
/// <c>artifacts/windows-audio-dial-live.png</c> and <c>-running.png</c>, each 208/884 = 0.235 on
/// its own check, both 0.0000 on <c>studio-dial-locked-track</c> and both 0.0000 on a capture of the
/// whole dashboard, with <c>windows-studio-dial-locked.png</c> scoring 0.0000 on each of theirs
/// while still passing its own at 208/936 = 0.222.</para>
/// </summary>
public class AudioDialPresentationTests
{
    /// <summary>
    /// The lowest fraction MEASURED on a real capture of either state: 208 track pixels of the
    /// 884-pixel band, at scale 1.75. Both states measured identically, which is itself the point of
    /// the pair.
    /// </summary>
    private const double MeasuredFraction = 208.0 / 884.0;

    private static string ManifestPath() =>
        Path.Combine(FindRepoRoot(), "client", "tools", "verify", "checks.json");

    private static IReadOnlyList<ManifestCheck> AudioChecks() =>
        [.. CheckManifest.Load(ManifestPath())
            .Where(c => string.Equals(c.Surface, "audio-dial", StringComparison.Ordinal))];

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repository root not found from the test binary");
    }

    /// <summary>
    /// BOTH states, both <c>presentation-verified</c>, and both expecting the SAME colour — which is
    /// the one place this pair differs in shape from every other on the manifest and is the claim
    /// rather than an oversight. A check demoted to <c>draw-verified</c> would let a headless run
    /// claim a composited pixel it never read.
    /// </summary>
    [Fact]
    public void TheAudioDialIsCheckedInBothStates_AndBothExpectTheLIVELivery()
    {
        var checks = AudioChecks();
        Assert.Equal(
            ["audio-dial/live", "audio-dial/running"],
            checks.Select(c => $"{c.Surface}/{c.State}").Order(StringComparer.Ordinal));

        foreach (var check in checks)
        {
            Assert.Equal(CheckManifest.EvidencePresentation, check.EvidenceClass);

            // Fluent's enabled slider track. The `running` half expecting this is the whole packet:
            // a session is under way in that capture and the dial is still the user's.
            //
            // THE COLOUR IS THE PRODUCT'S ACCENT, NOT THE MACHINE'S. It was 0x0078D4 here until
            // 2026-08-25 — the Windows personalisation blue Fluent used to resolve
            // SystemAccentColor from — and it went stale the day the token layer shadowed Fluent's
            // accent family, silently, because this file only ever checked the manifest against
            // itself. Under the CCP Default theme it is the mod's own AccentColor
            // (WPF Models/BuiltInMods.cs:920), and that is confirmed on a real capture rather than
            // read off a dictionary: the studio dial's live band measured 208 px of #E84393.
            var (r, g, b) = CheckManifest.ParseColor(check.ExpectedColor, $"check '{check.Name}':");
            Assert.Equal((0xE8, 0x43, 0x93), (r, g, b));
        }
    }

    /// <summary>
    /// Neither check can accept the LOCKED livery or the ground behind it. This is what makes the
    /// cross-surface inversion structural rather than a lucky measurement: widen either tolerance
    /// past the separation and the pair stops being able to detect an over-lock at all, and this
    /// reds naming both colours.
    /// </summary>
    [Fact]
    public void NeitherAudioCheckAcceptsTheLockedLiveryOrTheGroundBehindIt()
    {
        (string Name, byte R, byte G, byte B)[] neighbours =
        [
            ("the disabled slider track (Fluent SliderTrackValueFillDisabled)", 0x33, 0x33, 0x33),
            ("the module panel ground (Border.module, MainWindow.axaml:122, PanelBg)", 0x11, 0x11, 0x1A),
        ];

        var checks = AudioChecks();
        Assert.NotEmpty(checks);

        foreach (var check in checks)
        {
            var (r, g, b) = CheckManifest.ParseColor(check.ExpectedColor, $"check '{check.Name}':");
            foreach (var neighbour in neighbours)
            {
                var distance = Math.Max(
                    Math.Abs(neighbour.R - r), Math.Max(Math.Abs(neighbour.G - g), Math.Abs(neighbour.B - b)));
                Assert.True(check.Tolerance < distance,
                    $"check '{check.Name}' expects {check.ExpectedColor} with tolerance {check.Tolerance}, which "
                    + $"also ACCEPTS {neighbour.Name} — they are only {distance} apart on the widest channel. A "
                    + "check that cannot tell a greyed dial from a live one cannot catch an over-lock");
            }
        }
    }

    /// <summary>
    /// <b>The MARGIN RULE, pinned instead of the number.</b> The measured fraction is
    /// <see cref="MeasuredFraction"/> (208 of 884), and it is decided by the slider's own height
    /// against the panel row it sits in — so a theme, a font or a scale change moves it without
    /// moving the claim. A floor set near a value the product moves reds a perfectly good capture,
    /// which this harness has now learned twice (the pop quiz card's ink, and the trainer card's
    /// level bar at 0.10..0.30 against a 0.5949 fill).
    ///
    /// <para>So the rule is: comfortably below the measurement, and comfortably above zero, because
    /// the WRONG livery scores 0.0000 rather than a near miss. Both bounds are asserted; the exact
    /// value between them is not.</para>
    /// </summary>
    [Fact]
    public void TheFractionFloorsSitWellUnderTheMeasurement_AndWellAboveTheWrongLiverysZero()
    {
        // At statement depth 0, because every assertion below is inside the loop: a manifest that
        // lost this surface would otherwise satisfy both bounds by having nothing to check.
        var checks = AudioChecks();
        Assert.Equal(2, checks.Count);

        foreach (var check in checks)
        {
            Assert.True(check.MinPixelFraction <= MeasuredFraction * 0.75,
                $"check '{check.Name}' floors at {check.MinPixelFraction} against a measured "
                + $"{MeasuredFraction:0.0000}. That is too close to a value the product moves for "
                + "reasons that have nothing to do with the lock — the pop quiz ink lesson");

            Assert.True(check.MinPixelFraction >= 0.05,
                $"check '{check.Name}' floors at {check.MinPixelFraction}, which is close enough to "
                + "zero that a band containing almost no track would pass. The wrong livery measures "
                + "0.0000, so there is no reason to go this low");
        }
    }
}
