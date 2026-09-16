using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// "I'm stuck on a window that can't reach the online feed with no way to go back to the default
/// local files mode" (#ask-support, v6.9.5) - and restarting the app, and the PC, did not help.
///
/// <para><b>The trap.</b> <c>#empty</c> is an OPAQUE full-bleed sheet. It shipped at z-index 30,
/// over <c>#chrome</c> (10) - which holds the gear button - and over <c>#options-scrim</c> (20),
/// which is the card the gear opens. So a feed with nothing to show buried the only control that
/// could change what it shows. Online is the one source that can be empty through no fault of the
/// library, the source setting persists, and every relaunch came straight back to the same sheet:
/// a genuine one-way door.</para>
///
/// <para><b>Why a test.</b> The escape is three separate things - a stacking order, a button, and
/// an automatic fallback - and every one of them is invisible on a machine whose online feed
/// works, which is every machine this gets developed on. Assert them against the shipped files,
/// because the bug was never in the logic; it was in a number in a stylesheet.</para>
/// </summary>
public class FypDeadFeedEscapeTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string Read(string file) => File.ReadAllText(
        Path.Combine(RepoRoot(), "Assets", "web", "fyp", file));

    /// <summary>The z-index of the first rule whose selector list contains <paramref name="selector"/>.</summary>
    private static int ZIndexOf(string css, string selector)
    {
        foreach (Match rule in Regex.Matches(css, @"([^{}]+)\{([^}]*)\}"))
        {
            var selectors = rule.Groups[1].Value;
            // Whole-token match: "#empty, #crash" contains "#empty" but "#empty-copy" must not.
            if (!Regex.IsMatch(selectors, @"(^|[\s,>])" + Regex.Escape(selector) + @"($|[\s,{:])")) continue;
            var z = Regex.Match(rule.Groups[2].Value, @"z-index:\s*(-?\d+)");
            if (z.Success) return int.Parse(z.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        Assert.Fail($"no z-index found for {selector}");
        return 0;
    }

    [Fact]
    public void TheEmptySheetNeverCoversTheGearButton()
    {
        var css = Read("fyp.css");
        int empty = ZIndexOf(css, "#empty");
        int chrome = ZIndexOf(css, "#chrome");
        Assert.True(chrome > empty,
            $"#chrome ({chrome}) must paint above #empty ({empty}) - the gear is the way out of an empty feed");
    }

    [Fact]
    public void TheOptionsCardOpensOverTheEmptySheet()
    {
        // Reaching the gear is worth nothing if the card it opens renders underneath the sheet.
        var css = Read("fyp.css");
        int empty = ZIndexOf(css, "#empty");
        int options = ZIndexOf(css, "#options-scrim");
        int consent = ZIndexOf(css, "#consent-scrim");
        Assert.True(options > empty, $"#options-scrim ({options}) must paint above #empty ({empty})");
        Assert.True(consent > options,
            $"#consent-scrim ({consent}) must stay above #options-scrim ({options}) - it is opened FROM the card");
    }

    [Fact]
    public void TheCrashSheetLeavesTheChromeReachableToo()
    {
        var css = Read("fyp.css");
        Assert.True(ZIndexOf(css, "#chrome") > ZIndexOf(css, "#crash"));
    }

    [Fact]
    public void TheEmptyStateOffersAWayBackToLocalFiles()
    {
        Assert.Contains("id=\"btn-use-library\"", Read("index.html"), StringComparison.Ordinal);
        var js = Read("main.js");
        // Wired, and wired to the SAME path as the Library chip so the choice persists.
        Assert.Matches(@"btn-use-library'\)\.addEventListener\('click',\s*\(\)\s*=>\s*requestSourceChange\('library'\)", js);
        // ...and actually revealed on the remote branch of the empty state.
        Assert.Contains("libBtn.classList.remove('hidden')", js, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedTransportFailuresFallBackToTheLibraryBySessionOnly()
    {
        var js = Read("main.js");
        Assert.Contains("const REMOTE_FAIL_FALLBACK", js, StringComparison.Ordinal);
        Assert.Contains("function maybeFallBackToLibrary()", js, StringComparison.Ordinal);
        // The fallback runs from the online-status verdict, which is the only place a transport
        // failure is ever observed.
        Assert.Contains("if (maybeFallBackToLibrary()) break;", js, StringComparison.Ordinal);
        // Session-only: persist:false, so one bad network does not rewrite the user's source.
        Assert.Contains("applySource('library', false)", js, StringComparison.Ordinal);
        // A note on the feed, never a modal.
        Assert.Matches(@"maybeFallBackToLibrary\(\)[\s\S]{0,900}showFeedNote\(", js);
    }

    [Fact]
    public void AFailedGhostTellsTheUserInsteadOfLeavingABlackScreen()
    {
        // The host posts this after un-ghosting itself (FypGhostOverlay.Diagnose refused).
        Assert.Contains("case 'ghost-unavailable':", Read("main.js"), StringComparison.Ordinal);
    }
}
