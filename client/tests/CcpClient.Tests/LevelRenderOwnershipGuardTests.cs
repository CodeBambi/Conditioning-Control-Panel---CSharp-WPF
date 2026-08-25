using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The player level has exactly ONE render site in this application, and the shell chrome is not
/// allowed to become a second.
///
/// <para><b>The divergence this pins, stated as the board flagged it.</b> Upstream renders the level
/// in PERMANENT WINDOW CHROME — a pink header chip (<c>MainWindow/MainWindow.xaml:1553</c>, written
/// by <c>MainWindow/MainWindow.UiUpdates.cs:58</c>) and a second <c>LVL</c> readout on the XP bar
/// (<c>MainWindow/MainWindow.xaml:2156</c>, written at <c>:59</c>), which sit at <c>Grid.Row="1"</c>
/// and <c>Grid.Row="3"</c>, ABOVE the content host at <c>Grid.Row="4"</c>
/// (<c>MainWindow/MainWindow.xaml:2290</c>). So a user upstream sees their level on every door
/// without navigating. This port renders it once, on the Trainer Card, behind the Graded Intake
/// door. That is a real behavioural divergence and it is DELIBERATE; this guard is where the reason
/// lives, so the next reader finds an argument rather than an oversight.</para>
///
/// <para><b>Why chrome is the one place this port must not put the level: the refresh path does not
/// exist, and chrome is where its absence is fatal.</b> The level is a PASSIVE read — open the file,
/// parse it, close it (<c>Features/Intake/IntakeLaunch.cs:128</c>) — taken on two deterministic
/// triggers, page mount and gate decision (<c>Views/Pages/IntakePage.axaml.cs:77</c> and
/// <c>:82</c>). Chrome is never unmounted, so on a chrome element the mount trigger fires exactly
/// ONCE per process. There is nothing else to refresh on: the ledger deliberately raises no change
/// event at all (<c>Features/Progression/ProgressionLedger.cs:151</c> — <i>"This ledger raises no
/// event and plays nothing; a grant RETURNS a value and a caller decides what to say"</i>). A chrome
/// chip would therefore show the level as of app launch, permanently, while runs banked XP behind
/// it. The Trainer Card's staleness is bounded, predictable and written down ("navigating away and
/// back, or starting another run, shows the current record"); chrome's would be unbounded, invisible
/// and always on screen. <b>A permanently-visible wrong number is worse than a correct number behind
/// a door</b> — and this port already wrote that rule itself, one file over: a card "that answered
/// from a snapshot taken at startup would show a stale one for the rest of the session"
/// (<c>Features/Intake/IntakeLaunch.cs:105</c>). Chrome IS that snapshot, by construction.</para>
///
/// <para><b>And there is no app-lifetime progression owner to read from.</b> Three modal windows
/// each open their OWN ledger over the same <c>progression.json</c>, each disposed with its window
/// (<c>Features/Dtrh/DtrhHostWindow.axaml.cs:179</c>, <c>Features/Intake/IntakeHostContext.cs:190</c>,
/// <c>Features/Mantra/MantraLaunch.cs:126</c>); the composition root builds none. That is the same
/// defect shape the app-wide audio lift removed — <see cref="AudioOwnershipGuardTests"/>, and a seam
/// one window owns is not a seam the app has. So a live chrome readout is not a chip: it is an
/// app-lifetime ledger participant plus a change signal plus a composition-root change, and only
/// then a render. Polling the file on a timer is not an alternative — it is a wall-clock poll, and
/// it would re-run a load path that quarantines and adopts temp files on a schedule
/// (<c>Features/Progression/TrainerCard.cs:405</c>).</para>
///
/// <para><b>Upstream's chip is not a readout; it is the target of a ceremony this port structurally
/// refuses.</b> It carries a named scale+rotate rig for the level-up pop
/// (<c>MainWindow/MainWindow.xaml:2158-2165</c>) driven with the XP-bar bloom and particle burst
/// (<c>MainWindow/MainWindow.EventFx.cs:232-246</c>), beside a tray balloon, <c>lvup.mp3</c>, an
/// avatar swap and a haptic pattern. None of it is here, and the refusal is structural rather than
/// deferred: this surface never SEES a level-up, because the grant happens inside a modal window
/// with the page unmounted behind it. Porting the chip without the ceremony ports the pixel, not the
/// outcome.</para>
///
/// <para><b>What this port DOES put in permanent chrome, and why the line is principled rather than
/// furniture.</b> The session action bar (<c>Views/MainWindow.axaml:435</c>) is on every page,
/// because a running session changes what every page MEANS and the user must be able to stop it from
/// anywhere. The level changes nothing on any page: upstream deleted feature level gating outright,
/// so it unlocks nothing (<c>Models/AppSettings.cs:5439</c> is <c>return true;</c>). That — not "the
/// rail is not upstream's chrome" — is the reason it does not belong in this shell's chrome. The
/// weaker argument is rejected on purpose: this shell demonstrably HAS a chrome slot, so it could
/// carry one; the honest answer is that it must not until the level can be kept true there.</para>
///
/// <para><b>What it does NOT prove.</b> Nothing visual and nothing rendered. It reads source text,
/// so it cannot see a level reached through a variable, a binding or reflection, and it asserts
/// nothing about what any pixel shows. It closes one named regression route — a silent second
/// projection of the level — and that is the whole claim.</para>
/// </summary>
public class LevelRenderOwnershipGuardTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] SourceParts = ["client", "src", "CcpClient.Desktop"];

    /// <summary>The needle: the passive level projection's own read entry point.</summary>
    private const string LevelReadNeedle = "TrainerCardLevel.Read";

    /// <summary>The one file allowed to take the level read, and why it is.</summary>
    private static readonly string SanctionedReader =
        Path.Combine("Features", "Intake", "IntakeLaunch.cs");

    /// <summary>
    /// The shell's own files. These are the surfaces that are mounted for the whole process, which
    /// is precisely why a level in one of them could never be refreshed.
    /// </summary>
    private static readonly string[] ShellChromeFiles =
    [
        Path.Combine("Views", "MainWindow.axaml"),
        Path.Combine("Views", "MainWindow.axaml.cs"),
        Path.Combine("Views", "MainWindowViewModel.cs"),
    ];

    /// <summary>
    /// The three ways to obtain a real level number. A shell file naming any of them is either
    /// rendering a level or about to.
    /// </summary>
    private static readonly string[] LevelSourceNeedles =
    [
        "TrainerCardLevel",
        "ProgressionLedger",
        "ProgressionDocument",
    ];

    [Fact]
    public void OnlyTheIntakeLaunch_ReadsTheLevelProjection_NoSecondRenderMaySplitTheSource()
    {
        var sourceRoot = Path.Combine([FindRepoRoot(), .. SourceParts]);

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            if (string.Equals(SanctionedReader, relative, StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith(Path.Combine("Features", "Progression"), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (File.ReadAllText(file).Contains(LevelReadNeedle, StringComparison.Ordinal))
            {
                offenders.Add(relative);
            }
        }

        Assert.True(
            offenders.Count == 0,
            "a file outside the sanctioned reader takes the level projection: "
            + string.Join(", ", offenders)
            + ". The level has ONE source and ONE render on purpose — a second projection has to "
            + "reproduce every honesty state the first one carries (Unknown rather than 0, never a "
            + "consoling 1, a null bar fill rather than an empty track) or quietly drop them, and a "
            + "chip that renders Unknown as 'Lvl 1' is a claim about the user. If a second reader is "
            + "genuinely needed, add it here WITH its reason.");

        var sanctioned = File.ReadAllText(Path.Combine(sourceRoot, SanctionedReader));
        Assert.True(
            sanctioned.Contains(LevelReadNeedle, StringComparison.Ordinal),
            $"{SanctionedReader} no longer reads the level projection, and it is supposed to be the "
            + "one site that does. This guard counts readers; if it passes because there are none, "
            + "it is measuring nothing.");
    }

    [Fact]
    public void TheShellChrome_RendersNoLevel_BecauseNothingCouldEverRefreshItThere()
    {
        var sourceRoot = Path.Combine([FindRepoRoot(), .. SourceParts]);

        var offenders = new List<string>();
        foreach (var relative in ShellChromeFiles)
        {
            var text = ReadShellFile(sourceRoot, relative);
            foreach (var needle in LevelSourceNeedles)
            {
                if (text.Contains(needle, StringComparison.Ordinal))
                {
                    offenders.Add($"{relative} names {needle}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "the shell chrome reaches for the player level: "
            + string.Join(", ", offenders)
            + ". This is a DECIDED divergence from upstream, not an oversight — see this class's "
            + "remarks. The blocker is not layout, it is refresh: chrome is never unmounted, the "
            + "level's only triggers are page mount and gate decision, and the ledger raises no "
            + "change event, so a level in chrome would freeze at its app-launch value while runs "
            + "banked XP behind it. Before putting one here, land an app-lifetime progression owner "
            + "with a change signal — the shape Audio/AudioParticipant.cs gave the audio device — "
            + "and give the new surface its own headed gate.");
    }

    /// <summary>Tree-existence plumbing, kept OUT of the [Fact] bodies so no fs-predicate shape
    /// lands in a fact (the vacuous-shape detector surface,
    /// <c>client/tests/CcpClient.Tests/VacuousShapeDetector.cs:225</c>). The existence assert is
    /// still load-bearing: it is what tells a reader the guard is watching a shell that MOVED
    /// rather than one that is clean.</summary>
    private static string ReadShellFile(string sourceRoot, string relative)
    {
        var path = Path.Combine(sourceRoot, relative);
        Assert.True(
            File.Exists(path),
            $"{relative} is missing, so this guard is watching a shell that moved. Re-point it "
            + "at the files that are mounted for the whole process.");
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine([dir.FullName, .. RepoAnchorParts])))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("repository root not found from " + AppContext.BaseDirectory);
    }
}
