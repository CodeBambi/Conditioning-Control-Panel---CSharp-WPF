using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// UX restructure Phase 8 — the two tab keys that outlived their views.
///
/// <para><c>ShowTab("progression")</c> and <c>ShowTab("patreon")</c> are API and must keep working
/// forever. Phase 8 deleted both VIEWS (<c>ProgressionTabView</c>, <c>PatreonTabView</c>) while the
/// KEYS stay live as redirects, because:</para>
/// <list type="bullet">
/// <item>54 bark rules per built-in mod carry <c>tab_eq</c> on these keys — a dead key is 54 voiced
/// lines that silently never fire, with nothing in the compiler or the log to say so;</item>
/// <item>TutorialService steps declare <c>RequiresTab = "progression"</c>, and its router maps
/// <c>"patreon"</c> to its own callback. A step whose tab never resolves degrades to an unspotlit
/// card — again, silently.</item>
/// </list>
///
/// <para><b>Why this is a source test.</b> <c>ShowTab</c> is an instance method on
/// <see cref="ConditioningControlPanel.MainWindow"/>, which cannot be constructed off a real app
/// (it builds every service-backed tab in its constructor). The failure being guarded against is
/// textual anyway — a <c>case</c> label deleted along with the view it used to show — so the source
/// is the honest thing to assert against. Same idiom as <c>YouLibraryDoorTests</c>.</para>
/// </summary>
public class Phase8RedirectContractTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string ProductDir => Path.Combine(RepoRoot(), "ConditioningControlPanel");

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { ProductDir }.Concat(parts).ToArray()));

    private static string TabNavigation() => ReadSource("MainWindow", "MainWindow.TabNavigation.cs");

    /// <summary>Every .cs/.xaml under the product project, obj/bin and sibling worktrees excluded.</summary>
    private static IEnumerable<string> ProductSources()
    {
        var skip = new[] { Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                           Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
                           Path.DirectorySeparatorChar + ".claude" + Path.DirectorySeparatorChar,
                           Path.DirectorySeparatorChar + "node_modules" + Path.DirectorySeparatorChar };

        return Directory.EnumerateFiles(ProductDir, "*.*", SearchOption.AllDirectories)
                        .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                                 || f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                        .Where(f => !skip.Any(s => f.Contains(s, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Source with // and /* */ comments and XML comments stripped, so a tombstone note
    /// naming a demolished control can never masquerade as a live reference.</summary>
    private static string StripComments(string text)
    {
        text = Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        text = Regex.Replace(text, @"//[^\n]*", " ");
        text = Regex.Replace(text, @"<!--.*?-->", " ", RegexOptions.Singleline);
        return text;
    }

    // =====================================================================================
    //  1. the keys still resolve, to a LIVE surface
    // =====================================================================================

    [Fact]
    public void ProgressionStillHasACaseAndItLandsOnTheHomeDoor()
    {
        var nav = TabNavigation();

        var m = Regex.Match(nav, @"case ""progression"":(?<body>.*?)break;", RegexOptions.Singleline);
        Assert.True(m.Success, "case \"progression\" is gone from ShowTab — 54 bark rules per mod fire on that key");

        var body = m.Groups["body"].Value;
        // SettingsTab is the Home door's view (the key was never renamed; the VIEW named
        // "SettingsTab" IS Home). Making it Visible is what proves this lands somewhere real.
        Assert.Contains("SettingsTab.Visibility = Visibility.Visible", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ProgressionTab", body, StringComparison.Ordinal);
    }

    [Fact]
    public void PatreonStillRedirectsIntoTheSettingsDoorsAccountSection()
    {
        var nav = TabNavigation();

        var m = Regex.Match(nav, @"if \(tab == ""patreon""\)\s*\{(?<body>.*?)\}", RegexOptions.Singleline);
        Assert.True(m.Success, "the \"patreon\" redirect is gone from ShowTab");
        Assert.Contains("ShowAppInfoPopup", m.Groups["body"].Value, StringComparison.Ordinal);

        // And the destination is a real method somewhere in the product.
        Assert.Contains(ProductSources().Where(f => f.EndsWith(".cs")),
                        f => Regex.IsMatch(File.ReadAllText(f), @"void ShowAppInfoPopup\("));
    }

    [Fact]
    public void NeitherKeyWasQuietlyFoldedIntoTheBarkAliasMap()
    {
        // BarkTabAliases rewrites a key on its way to NotifyTabNavigated. "progression" fires its
        // OWN key (that is what the 54 rules expect); aliasing it would silence every one of them
        // while looking like a tidy-up.
        var aliases = Regex.Match(TabNavigation(),
            @"BarkTabAliases = new\(StringComparer\.OrdinalIgnoreCase\)\s*\{(?<body>.*?)\};",
            RegexOptions.Singleline);

        if (aliases.Success)
        {
            Assert.DoesNotContain("\"progression\"", aliases.Groups["body"].Value, StringComparison.Ordinal);
            Assert.DoesNotContain("\"patreon\"", aliases.Groups["body"].Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheNavIndicatorAndPaletteStillKnowWhereProgressionLives()
    {
        // Three separate maps have to agree or code-driven navigation lands with the active
        // indicator inside a door nobody opened.
        Assert.Contains("\"progression\"", StripComments(ReadSource("MainWindow", "MainWindow.ChromeFx.cs")),
                        StringComparison.Ordinal);
        Assert.Contains("\"progression\"", StripComments(ReadSource("Services", "ChromeFxNav.cs")),
                        StringComparison.Ordinal);
        // The door map: the Home door owns both "settings" and "progression".
        Assert.Matches(new Regex(@"""settings""\s*,\s*""progression"""), StripComments(TabNavigation()));
    }

    [Fact]
    public void TheTutorialRouterStillAnswersToBothKeys()
    {
        var tutorial = StripComments(ReadSource("Services", "TutorialService.cs"));
        Assert.Matches(new Regex(@"""progression""\s*=>"), tutorial);
        Assert.Matches(new Regex(@"""patreon""\s*=>"), tutorial);
    }

    [Fact]
    public void EveryTutorialStepStillRequiresATabKeyShowTabCanHandle()
    {
        // A RequiresTab that ShowTab has no case for is the exact silent failure this phase could
        // have introduced: the overlay just never navigates and the step shows against whatever
        // tab happened to be open.
        var nav = TabNavigation();
        var handled = new HashSet<string>(
            Regex.Matches(nav, @"case ""(?<k>[a-z]+)"":").Select(m => m.Groups["k"].Value),
            StringComparer.Ordinal);

        // The keys ShowTab intercepts before the switch (early returns), plus the door-only keys.
        foreach (var early in Regex.Matches(nav, @"if \(tab == ""(?<k>[a-z]+)""\)").Select(m => m.Groups["k"].Value))
            handled.Add(early);

        var required = new HashSet<string>(
            Regex.Matches(ReadSource("Services", "TutorialService.cs"), @"RequiresTab = ""(?<k>[a-z]+)""")
                 .Select(m => m.Groups["k"].Value),
            StringComparer.Ordinal);

        var orphans = required.Where(k => !handled.Contains(k)).OrderBy(k => k).ToList();
        Assert.True(orphans.Count == 0,
            "tutorial steps require tab keys ShowTab no longer handles: " + string.Join(", ", orphans));

        // Belt and braces: the two Phase 8 keys are actually in the required set, i.e. this test
        // is exercising them rather than passing on an empty intersection.
        Assert.Contains("progression", required);
    }

    // =====================================================================================
    //  2. the demolished surfaces are gone for real
    // =====================================================================================

    public static IEnumerable<object[]> DemolishedTypes => new[]
    {
        new object[] { "ProgressionTabView" },
        new object[] { "PatreonTabView" },
        new object[] { "AttentionCheckSettingsDialog" },
        new object[] { "AttentionCheckFeatureControl" },
        new object[] { "SchedulerRampFeatureControl" },
    };

    [Theory]
    [MemberData(nameof(DemolishedTypes))]
    public void ADemolishedTypeHasNoFilesAndNoLiveReference(string typeName)
    {
        var files = ProductSources().Where(f =>
            Path.GetFileName(f).StartsWith(typeName + ".", StringComparison.Ordinal)).ToList();
        Assert.True(files.Count == 0, $"{typeName} still has source files: " + string.Join(", ", files));

        var live = new List<string>();
        foreach (var f in ProductSources())
        {
            var text = StripComments(File.ReadAllText(f));
            if (Regex.IsMatch(text, @"\b" + Regex.Escape(typeName) + @"\b"))
                live.Add(Path.GetRelativePath(ProductDir, f));
        }

        Assert.True(live.Count == 0,
            $"{typeName} is deleted but still referenced in live (non-comment) code — XAML bindings "
            + "and FindName lookups do not fail the build, they fail at runtime: " + string.Join(", ", live));
    }

    [Theory]
    [InlineData("ProgressionTab")]
    [InlineData("PatreonTab")]
    [InlineData("LegacyDashboardHost")]
    [InlineData("CardSystem")]
    public void ADemolishedContainerIsNoLongerDereferencedAnywhere(string memberName)
    {
        // `CardSystem` is the split ruling: the TILE and its three refs die, but
        // MainWindow.CardSystem_Click stays (the System quick-toggle pill is its only route now),
        // so the pattern deliberately matches `CardSystem` / `.CardSystem` and not `CardSystem_Click`.
        var pattern = new Regex(@"\b" + Regex.Escape(memberName) + @"\b(?!_)");

        var live = new List<string>();
        foreach (var f in ProductSources())
        {
            foreach (var (line, i) in StripComments(File.ReadAllText(f)).Split('\n').Select((l, i) => (l, i + 1)))
                if (pattern.IsMatch(line))
                    live.Add($"{Path.GetRelativePath(ProductDir, f)}:{i}");
        }

        Assert.True(live.Count == 0, $"{memberName} is demolished but still dereferenced: "
                                     + string.Join(", ", live.Take(10)));
    }

    [Fact]
    public void TheSystemQuickTogglePillIsStillWiredToTheHandlerTheTileUsedToShare()
    {
        // The other half of the CardSystem split ruling. Dropping VelvetBtnSystem_Click with the
        // tile would leave the System popup (and its NotifyFeatureOpened("System") bark) with no
        // entry point at all.
        var home = ReadSource("Views", "Tabs", "SettingsTabView.xaml");
        Assert.Contains("x:Name=\"VelvetBtnSystem\"", home, StringComparison.Ordinal);
        Assert.Contains("Click=\"VelvetBtnSystem_Click\"", home, StringComparison.Ordinal);

        Assert.Contains("mw.CardSystem_Click", ReadSource("Views", "Tabs", "SettingsTabView.xaml.cs"),
                        StringComparison.Ordinal);
        Assert.Contains("CardSystem_Click", ReadSource("MainWindow", "MainWindow.Presets.cs"),
                        StringComparison.Ordinal);
    }

    // =====================================================================================
    //  3. the preserved-exactly items (hard rule 7)
    // =====================================================================================

    [Fact]
    public void TheStagedStartupLadderStillHasAllFourRungsInOrder()
    {
        // 3s marquee, 5s update banner, 7s server announcement, 14s intake nudge — the spacing IS
        // the design (the last two share one popup window, and stacking them turns a nudge into an
        // ambush). A rung that loses its delay, or is re-pointed at a different callback, is a
        // regression nothing else would catch.
        var marquee = StripComments(ReadSource("MainWindow", "MainWindow.Marquee.cs"));

        var rungs = new (int Ms, string Callback)[]
        {
            (3000,  "RefreshMarqueeFromSettings"),
            (5000,  "CheckServerUpdateBanner"),
            (7000,  "CheckServerAnnouncement"),
            (14000, "CheckIntakePassNudge"),
        };

        int previous = -1;
        foreach (var (ms, callback) in rungs)
        {
            var m = Regex.Match(marquee,
                @"Task\.Delay\(" + ms + @"\).{0,400}?Dispatcher\.Invoke\(" + Regex.Escape(callback) + @"\)",
                RegexOptions.Singleline);
            Assert.True(m.Success, $"the {ms / 1000}s startup rung ({callback}) is gone or re-pointed");

            Assert.True(m.Index > previous,
                $"the {ms / 1000}s rung moved ahead of an earlier one — the ladder's order is the design");
            previous = m.Index;
        }

        // ...and the one-popup-per-launch slot the last two rungs compete for.
        Assert.Contains("_serverAnnouncementShownThisLaunch", marquee, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLockdownIntroKeepsItsSecretExitForeshadowing()
    {
        // The 5-click secret exit is only ever hinted at here. Losing the line makes the egg
        // undiscoverable rather than secret.
        var popup = ReadSource("Windows", "FeatureIntroPopup.xaml.cs");
        Assert.Contains("the timer keeps a secret", popup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheNewYearNoteLatchIsUntouchedAndStillGuardedByAFileExistsCheck()
    {
        // note_newyear.wav is armed but not yet on disk. PlayNoteClip must keep its File.Exists
        // bail BEFORE it sets the uninterruptible latch, or a missing clip wedges her speech.
        Assert.Contains("NewYearNoteReactionSeen", ReadSource("Models", "AppSettings.cs"), StringComparison.Ordinal);
        Assert.Contains("NewYearNoteReactionSeen", ReadSource("MainWindow", "MainWindow.UiUpdates.cs"),
                        StringComparison.Ordinal);

        var speech = ReadSource("AvatarTube", "AvatarTubeWindow.Speech.cs");
        var body = Regex.Match(speech, @"PlayNoteClip\([^)]*\)\s*\{(?<b>.{0,600})", RegexOptions.Singleline);
        Assert.True(body.Success, "PlayNoteClip is gone");
        Assert.Contains("File.Exists", body.Groups["b"].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAttentionCheckServiceIsStillConstructedForTheBarkHarness()
    {
        // Its two UI files were deleted; the SERVICE must survive, because BarkService wires
        // OnPass/OnFail on it and a null there breaks the harness.
        Assert.Contains("AttentionCheck = new AttentionCheckService()", ReadSource("App.xaml.cs"),
                        StringComparison.Ordinal);
        Assert.Contains("App.AttentionCheck", ReadSource("Services", "Companion", "BarkService.cs"),
                        StringComparison.Ordinal);
    }
}
