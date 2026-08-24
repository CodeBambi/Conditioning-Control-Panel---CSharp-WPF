using System.Text.RegularExpressions;
using CcpClient.Desktop.Features.Progression;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpVerify;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The Trainer Card's RECORD states, and the capture path that earns them.
///
/// <para><b>What this file is NOT.</b> It is not the evidence. The evidence is four real captures
/// of the running shell on a real Windows desktop at scale 1.75, checked by <c>CcpVerify</c>
/// against <c>client/tools/verify/checks.json</c>. Each of the three states passes on its own
/// capture and FAILS BY NAME on both of the others - <c>read</c> reds at 0/3856 on the earned
/// capture and 2039/3374 on the unreadable one, <c>earned</c> reds at 2475/3856 and 2360/3856,
/// <c>unreadable</c> reds at 0/3374 twice - and all seven checks fail on a capture of the whole
/// dashboard. A headless assembly cannot photograph anything and no fact here claims to.</para>
///
/// <para><b>What it IS.</b> The things that rot silently between headed runs. This surface's claim
/// is that WHERE THE INK STOPS on two status lines is the record reaching the screen, so what can
/// rot is: a state whose checks no capture could tell apart from another state's, a tolerance wide
/// enough to let one ink pass for the other, a band that drifted off the line it names, and a
/// seeded record that no longer binds to the state it is named for. Each of those has a fact
/// below, and each is derived from the two files on disk rather than restated here.</para>
///
/// <para><b>A pixel check is evidence about colour and geometry at named offsets.</b> Nothing here
/// or there says the card is legible, well worded, sufficiently contrasted or usable, and no check
/// reads a character. No Linux claim of any kind is made.</para>
/// </summary>
public sealed class TrainerCardRecordPresentationTests : IDisposable
{
    private const string Surface = "trainer-card-record";

    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];

    /// <summary>The card ground every "clear" check names (<c>Border.module</c>,
    /// <c>MainWindow.axaml:122</c>).</summary>
    private static readonly (byte R, byte G, byte B) CardGround = (0x1B, 0x16, 0x22);

    /// <summary>A status line: <c>page-blurb</c>'s <c>#FFE8E0EE</c> at the award template's local
    /// <c>Opacity="0.9"</c>, composited over <see cref="CardGround"/>.</summary>
    private static readonly (byte R, byte G, byte B) StatusInk = (0xD3, 0xCB, 0xD9);

    /// <summary>A row NAME: <c>module-title</c> at no opacity at all.</summary>
    private static readonly (byte R, byte G, byte B) TitleInk = (0xE8, 0xE0, 0xEE);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ccp-trainer-card-record-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best-effort: a temp directory another process still holds is not this test's finding
        }
    }

    [Fact]
    public void TheThreeRecordStatesAreCheckedAndEveryCheckClaimsPresentationVerified()
    {
        // A surface checked in ONE state cannot distinguish anything: the whole bite proof is that
        // another state's real capture fails it, and there has to BE another state for that to
        // mean something. A headed gate is also never dischargeable by a headless frame, so the
        // evidence class is asserted rather than assumed.
        var checks = RecordChecks();
        Assert.All(checks, c => Assert.Equal(CheckManifest.EvidencePresentation, c.EvidenceClass));

        Assert.Equal(
            ["earned", "read", "unreadable"],
            checks.Select(c => c.State).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToArray());

        // Two per state at the very least: one ink and one ground, which is what the uniform-capture
        // fact below rests on.
        foreach (var state in checks.Select(c => c.State).Distinct())
        {
            Assert.True(checks.Count(c => c.State == state) >= 2,
                $"state '{state}' declares fewer than two checks, so one flat colour could satisfy all of them");
        }
    }

    [Fact]
    public void NoUniformCaptureCanPassAnyOfTheThreeRecordStates()
    {
        // The failure this forbids has already happened in this repository: capture-wslg.sh printed
        // CAPTURE PASS over an all-black image. A colour c passes two checks only if EVERY channel
        // is within tolerance of both expected values, which needs |a-b| <= ta+tb on every channel.
        // One channel further apart than that makes the two bands unreachable together.
        foreach (var state in RecordChecks().Select(c => c.State).Distinct())
        {
            var forState = RecordChecks().Where(c => c.State == state).ToArray();
            Assert.True(
                forState.SelectMany(a => forState, (a, b) => (a, b)).Any(pair => Disjoint(pair.a, pair.b)),
                $"every pair of '{state}' checks overlaps on all three channels, so one flat colour satisfies "
                + "the whole state - a capture of an empty rectangle would pass it");

            // And demonstrated, not only derived: the five uniform captures this surface could
            // plausibly be handed. Each must be rejected by at least one check of every state.
            foreach (var (what, colour) in new (string, (byte R, byte G, byte B))[]
                     {
                         ("an all-black capture", ((byte)0, (byte)0, (byte)0)),
                         ("the page ground behind the card (#141018)", ((byte)0x14, (byte)0x10, (byte)0x18)),
                         ("a card that painted its fill and nothing else", CardGround),
                         ("a rectangle of nothing but status ink", StatusInk),
                         ("a rectangle of nothing but title ink", TitleInk),
                     })
            {
                var image = Solid(64, 64, colour);
                Assert.False(
                    forState.All(c => CheckEvaluator.Evaluate(c, image).Passed),
                    $"{what} passes every check of state '{state}'");
            }
        }
    }

    [Fact]
    public void EveryPairOfStatesIsSeparatedAtARegionBOTHOfThemDeclare()
    {
        // THE INVERSION, AS A PROPERTY OF THE MANIFEST RATHER THAN OF THREE PNGs. Two states are
        // only distinguishable if some region they BOTH sample carries expectations that cannot
        // both hold - otherwise a capture of one could satisfy the other's checks and the pair
        // would be two photographs of the same claim.
        //
        // This is why `unreadable` carries an honor-status ink check as well as a top-status one:
        // that check is redundant against the captures (top-status already reds on both siblings)
        // and it is what makes the earned/unreadable separation MECHANICAL rather than measured.
        var checks = RecordChecks();
        var states = checks.Select(c => c.State).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToArray();

        foreach (var left in states)
        {
            foreach (var right in states.Where(s => !string.Equals(s, left, StringComparison.Ordinal)))
            {
                var separated =
                    from a in checks.Where(c => c.State == left)
                    from b in checks.Where(c => c.State == right)
                    where SameRegion(a, b) && Disjoint(a, b)
                    select (a.Name, b.Name);

                Assert.True(separated.Any(),
                    $"states '{left}' and '{right}' share no region on which their expectations disagree, so no "
                    + "capture of either could ever fail the other's checks and the pair proves nothing");
            }
        }
    }

    [Fact]
    public void TheTwoInksStayTwoDifferentClaims_AndTheGroundAcceptsNoOtherShellColour()
    {
        // TOLERANCE IS THE SIZE OF THE DEFECT IT HIDES, and here the defect has a name. A status
        // line composites to #D3CBD9 and a row name is #E8E0EE: 21 per channel apart, which is
        // small for text. At a tolerance of 21 or more the earned state's ROW NAME check would pass
        // on a band of status ink and the two ink bands would stop being different claims.
        var perChannel = Math.Max(
            Math.Abs(StatusInk.R - TitleInk.R),
            Math.Max(Math.Abs(StatusInk.G - TitleInk.G), Math.Abs(StatusInk.B - TitleInk.B)));
        Assert.Equal(21, perChannel);

        foreach (var check in RecordChecks())
        {
            var expected = CheckManifest.ParseColor(check.ExpectedColor, $"check '{check.Name}':");
            if (expected == CardGround)
            {
                // The trainer-card-ground number for the trainer-card-ground reason: the rack's
                // #FF19141F is 3 away on its widest channel, so at the rail door's 24 these checks
                // would pass on a photograph of the Studio rack, and the page ground behind the
                // card (#141018, the window's Background) is 10 away.
                Assert.True(check.Tolerance < 3,
                    $"'{check.Name}' would accept the Studio rack's ground #19141F at tolerance {check.Tolerance}");
                continue;
            }

            Assert.True(expected == StatusInk || expected == TitleInk,
                $"'{check.Name}' expects {check.ExpectedColor}, which is neither of the card's two inks nor its "
                + "ground - a colour nothing on this card paints cannot be evidence about it");
            Assert.True(check.Tolerance < perChannel,
                $"'{check.Name}' has tolerance {check.Tolerance} and the card's two inks are only {perChannel} "
                + "apart, so this check would accept the other ink and the pair would stop being two claims");
        }
    }

    [Fact]
    public void EverySampledBandIsOneTheCaptureScriptProvesAgainstTheMeasuredLayout()
    {
        // A fraction of a capture is only evidence if the thing it names is really at that
        // fraction. capture.ps1 declares each band and REFUSES at capture time when the line it
        // names is not under it - which is how the landed trainer-card surface caught its own ink
        // band walking off the title when the level block landed above it. This holds the manifest
        // to the bands the script proves, so widening one here without moving it there reddens.
        var script = CaptureScript();
        var statusX = Band(script, "statusBandX");
        var tocY = Band(script, "tocStatusBandY");
        var honorY = Band(script, "honorStatusBandY");
        var nameX = Band(script, "rowNameBandX");
        var nameY = Band(script, "rowNameBandY");
        var clearX = Band(script, "clearColumnBandX");
        var clearY = Band(script, "clearColumnBandY");

        var expected = new Dictionary<string, ((double Lo, double Hi) X, (double Lo, double Hi) Y)>(StringComparer.Ordinal)
        {
            ["trainer-card-record-read-top-status-clear"] = (statusX, tocY),
            ["trainer-card-record-read-honor-progress-ink"] = (statusX, honorY),
            ["trainer-card-record-earned-honor-status-clear"] = (statusX, honorY),
            ["trainer-card-record-earned-row-name-ink"] = (nameX, nameY),
            ["trainer-card-record-unreadable-top-status-ink"] = (statusX, tocY),
            ["trainer-card-record-unreadable-honor-status-ink"] = (statusX, honorY),
            ["trainer-card-record-unreadable-clear-column"] = (clearX, clearY),
        };

        var checks = RecordChecks();
        Assert.Equal(
            expected.Keys.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            checks.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());

        foreach (var check in checks)
        {
            var rect = check.Region.Rect!;
            var (x, y) = expected[check.Name];
            Assert.Equal(x.Lo, rect.X, 6);
            Assert.Equal(x.Hi, rect.X + rect.W, 6);
            Assert.Equal(y.Lo, rect.Y, 6);
            Assert.Equal(y.Hi, rect.Y + rect.H, 6);
        }
    }

    [Fact]
    public void EverySeededRecordBindsToTheStateItsCaptureIsNamedFor()
    {
        // THE SEEDS ARE THE SUBJECT OF THIS SURFACE, so nothing may decide what they mean except
        // the card's own reader. Each is lifted out of capture.ps1 and handed to TrainerCard.Read,
        // and what comes back must be the state the manifest names and the sentences the capture
        // gates on - so a seed that stopped producing its state fails HERE, in seconds, instead of
        // on a real desktop twenty minutes later.
        var script = CaptureScript();
        Directory.CreateDirectory(_root);

        foreach (var (state, expected) in new (string State, (TrainerCardRecordState Record, string Note, string Top, string Honor))[]
                 {
                     ("read", (TrainerCardRecordState.Read, string.Empty, TrainerCard.EarnedStatus,
                         $"{TrainerCard.NotEarnedStatus} 1 of {GradedRunAwards.HonorRollCategories} categories cleared at top marks.")),
                     ("earned", (TrainerCardRecordState.Read, string.Empty, TrainerCard.EarnedStatus, TrainerCard.EarnedStatus)),
                     ("unreadable", (TrainerCardRecordState.Unreadable,
                         TrainerCard.UnreadableNoteHead + TrainerCard.UnreadableInvalidJson,
                         TrainerCard.UnknownStatus, TrainerCard.UnknownStatus)),
                 })
        {
            var path = Path.Combine(_root, $"{state}-{GradedRunAwardsDocument.FileName}");
            File.WriteAllText(path, SeedFor(script, state));
            var card = TrainerCard.Read(path);

            Assert.Equal(expected.Record, card.Record);
            Assert.Equal(expected.Note, card.RecordNote);
            Assert.Equal(expected.Top, card.Awards.Single(a => a.Id == GradedRunAwards.TopOfTheClassId).Status);
            Assert.Equal(expected.Honor, card.Awards.Single(a => a.Id == GradedRunAwards.HonorRollId).Status);

            // And the capture gates on exactly those sentences before it reads a pixel. Restating
            // one of them in the script is how a UIA gate quietly stops matching the model.
            Assert.Contains(expected.Top, script, StringComparison.Ordinal);
            Assert.Contains(expected.Honor, script, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AReadRecordAlwaysCarriesAnEarnedRow_WhichIsWhyThereIsNoFourthState()
    {
        // THE PREMISE THE WHOLE SURFACE RESTS ON, and it is the reason the board's "Read" and
        // "earned row" are ONE capture rather than two. A Read record with nothing earned is not a
        // state this build can be in: RecordGradedRun awards top_of_the_class FIRST and
        // UNCONDITIONALLY on a top-marks run, before the category is even looked at
        // (GradedRunAwards.cs:245-248, upstream's GamificationBridge.cs:600), and the file is
        // written ONLY when something was awarded or a category was new (:261-264).
        //
        // So this drives the two paths that could produce an empty record and shows neither does:
        // a below-bar run writes no file at all, and the first run that writes one has already
        // earned a row. If a later producer ever writes an empty record, the `read` capture stops
        // being a photograph of the state it is named for, and this reds first.
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "produced-" + GradedRunAwardsDocument.FileName);
        var store = new PersistenceStore<GradedRunAwardsDocument>(
            new OperationRegistry().OwnerFor("IntakeGradedRunAwards"),
            new NullSink(),
            path,
            GradedRunAwardsDocument.CurrentSchemaVersion);
        await store.StartAsync(TestContext.Current.CancellationToken);
        var awards = new GradedRunAwards(store, _ => { });

        awards.RecordGradedRun(topMarks: false, category: "bambi");
        Assert.False(File.Exists(path), "a run below the bar wrote an award record");
        Assert.Equal(TrainerCardRecordState.NoRunsYet, TrainerCard.Read(path).Record);

        awards.RecordGradedRun(topMarks: true, category: "bambi");
        await store.SaveImmediate();

        var card = TrainerCard.Read(path);
        Assert.Equal(TrainerCardRecordState.Read, card.Record);
        Assert.Equal(
            TrainerCardAwardState.Earned,
            card.Awards.Single(a => a.Id == GradedRunAwards.TopOfTheClassId).State);
    }

    private sealed class NullSink : ILogSink
    {
        public void Log(string message)
        {
            // the store's diagnostics are not this fact's subject
        }
    }

    /// <summary>This surface's checks, read from the manifest on disk and never restated here.</summary>
    private static IReadOnlyList<ManifestCheck> RecordChecks() =>
        [.. CheckManifest.Load(ManifestPath()).Where(c => string.Equals(c.Surface, Surface, StringComparison.Ordinal))];

    private static bool SameRegion(ManifestCheck a, ManifestCheck b)
    {
        var (x, y) = (a.Region.Rect!, b.Region.Rect!);
        return x.X == y.X && x.Y == y.Y && x.W == y.W && x.H == y.H;
    }

    private static bool Disjoint(ManifestCheck a, ManifestCheck b)
    {
        var (ar, ag, ab) = CheckManifest.ParseColor(a.ExpectedColor, $"check '{a.Name}':");
        var (br, bg, bb) = CheckManifest.ParseColor(b.ExpectedColor, $"check '{b.Name}':");
        var reach = a.Tolerance + b.Tolerance;
        return Math.Abs(ar - br) > reach || Math.Abs(ag - bg) > reach || Math.Abs(ab - bb) > reach;
    }

    /// <summary>One <c>$name = @(lo, hi)</c> band out of <c>capture.ps1</c>.</summary>
    private static (double Lo, double Hi) Band(string script, string name)
    {
        var match = Regex.Match(script, @"\$" + name + @" = @\((?<lo>[\d.]+), (?<hi>[\d.]+)\)");
        Assert.True(match.Success,
            $"capture.ps1 no longer declares ${name}, so nothing proves the manifest region it bounds is "
            + "really where the capture says it is");
        return (double.Parse(match.Groups["lo"].Value, System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(match.Groups["hi"].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>The bytes <c>capture.ps1</c> seeds for one state, out of its own switch.</summary>
    private static string SeedFor(string script, string state)
    {
        var start = script.IndexOf("$awardBytes = switch ($State) {", StringComparison.Ordinal);
        Assert.InRange(start, 0, script.Length);
        var end = script.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.InRange(end, start, script.Length);

        var arm = state switch
        {
            "read" or "earned" => $"'{state}'",
            _ => "default",
        };
        var match = Regex.Match(script[start..end], Regex.Escape(arm) + @" \{ '(?<json>[^']*)' \}");
        Assert.True(match.Success,
            $"capture.ps1's seed table no longer has an arm for '{state}', so nothing here is checking the bytes "
            + "that capture is taken over");
        return match.Groups["json"].Value;
    }

    private static string ManifestPath() =>
        Path.Combine(FindRepoRoot(), "client", "tools", "verify", "checks.json");

    private static string CaptureScript() =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "client", "tools", "verify", "capture.ps1"));

    private static DecodedImage Solid(int width, int height, (byte R, byte G, byte B) colour)
    {
        var bgra = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            bgra[i * 4] = colour.B;
            bgra[i * 4 + 1] = colour.G;
            bgra[i * 4 + 2] = colour.R;
            bgra[i * 4 + 3] = 255;
        }

        return new DecodedImage(width, height, bgra);
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine([dir.FullName, .. RepoAnchorParts])))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("the repository root (client/CcpClient.sln) was not found above the test binary");
    }
}
