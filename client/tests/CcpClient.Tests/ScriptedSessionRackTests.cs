using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The rack's toolbar as arithmetic: which rows a filter lets through, what order they come out in,
/// and the count line that describes the result (<see cref="ScriptedSessionRack"/>).
///
/// <para><b>Two kinds of fact here, deliberately.</b> The first four are driven by sessions this
/// file builds, so a rule can be stated at values that make its failure unambiguous — a duration
/// tie that only a tie-break can resolve, a needle that appears in a description and nowhere else.
/// The last one is driven by THE FOUR FILES THE APP SHIPS, because a rule that holds on invented
/// data and not on the rack the user will actually see is not the rule the port owes.</para>
///
/// <para>The drawing half — that a real search box on a real page really re-runs this, and that a
/// selected row survives it — is next door in
/// <c>CcpClient.HeadlessTests.SessionRackHeadlessTests</c>. Nothing here mounts a window.</para>
/// </summary>
public class ScriptedSessionRackTests
{
    private static readonly IReadOnlySet<ScriptedSessionDifficulty> AllBands =
        new HashSet<ScriptedSessionDifficulty>(Enum.GetValues<ScriptedSessionDifficulty>());

    private static ScriptedSession Session(
        string name,
        ScriptedSessionDifficulty difficulty,
        int minutes,
        string description = "") => new()
        {
            Id = name.ToLowerInvariant().Replace(' ', '_'),
            Name = name,
            Difficulty = difficulty,
            DurationMinutes = minutes,
            Description = description,
        };

    private static IReadOnlyList<string> Names(IReadOnlyList<ScriptedSession> rows) =>
        [.. rows.Select(row => row.Name)];

    // =====================================================================================
    //  THE FILTERS
    // =====================================================================================

    /// <summary>
    /// Upstream's difficulty dots (<c>MainWindow/MainWindow.SessionIO.cs:289</c>): four independent
    /// toggles, and a row is shown when its own band is still switched on.
    ///
    /// <para>The all-off case is not a curiosity — it is one click away from the three-on case, and
    /// it is the reason <see cref="ScriptedSessionRack.NoMatches"/> exists.</para>
    /// </summary>
    [Fact]
    public void EachBandIsItsOwnSwitch_AndTurningThemAllOffEmptiesTheRack()
    {
        IReadOnlyList<ScriptedSession> all =
        [
            Session("Gentle", ScriptedSessionDifficulty.Easy, 30),
            Session("Middling", ScriptedSessionDifficulty.Medium, 30),
            Session("Rough", ScriptedSessionDifficulty.Hard, 30),
            Session("Brutal", ScriptedSessionDifficulty.Extreme, 30),
        ];

        Assert.Equal(
            ["Gentle", "Middling", "Rough", "Brutal"],
            Names(ScriptedSessionRack.Arrange(all, AllBands, ScriptedSessionSort.Installed, "")));

        var withoutEasy = new HashSet<ScriptedSessionDifficulty>(AllBands);
        withoutEasy.Remove(ScriptedSessionDifficulty.Easy);
        Assert.Equal(
            ["Middling", "Rough", "Brutal"],
            Names(ScriptedSessionRack.Arrange(
                all, withoutEasy, ScriptedSessionSort.Installed, "")));

        Assert.Equal(
            ["Rough"],
            Names(ScriptedSessionRack.Arrange(
                all,
                new HashSet<ScriptedSessionDifficulty> { ScriptedSessionDifficulty.Hard },
                ScriptedSessionSort.Installed,
                "")));

        Assert.Empty(ScriptedSessionRack.Arrange(
            all, new HashSet<ScriptedSessionDifficulty>(), ScriptedSessionSort.Installed, ""));
    }

    /// <summary>
    /// Upstream's search (<c>MainWindow/MainWindow.SessionIO.cs:291-300</c>): the name OR the whole
    /// description, case-insensitively, over a needle that has been trimmed first
    /// (<c>:815-816</c>).
    ///
    /// <para><b>The description needle appears in no name and the name needle in no description</b>,
    /// so neither half of the OR can be dropped without a row going missing here. The trimming case
    /// uses a needle that would match nothing if the spaces were left on it.</para>
    /// </summary>
    [Fact]
    public void TheSearchReadsTheNameAndTheWholeDescription_IgnoringCaseAndSurroundingSpace()
    {
        IReadOnlyList<ScriptedSession> all =
        [
            Session("Morning Drift", ScriptedSessionDifficulty.Easy, 30, "Gentle whispers."),
            Session("Gamer Girl", ScriptedSessionDifficulty.Medium, 45, "Keep playing.\nBorderless windowed."),
        ];

        // A name hit. "morning" is in one name and in neither description.
        Assert.Equal(
            ["Morning Drift"],
            Names(ScriptedSessionRack.Arrange(all, AllBands, ScriptedSessionSort.Installed, "morning")));

        // A description hit, and on the SECOND line of it — the cell only ever shows the first
        // (SessionRackNotices.RowBlurb), so this is the half a first-line-only search would lose.
        Assert.Equal(
            ["Gamer Girl"],
            Names(ScriptedSessionRack.Arrange(all, AllBands, ScriptedSessionSort.Installed, "borderless")));

        // Case, in both directions.
        Assert.Equal(
            ["Gamer Girl"],
            Names(ScriptedSessionRack.Arrange(all, AllBands, ScriptedSessionSort.Installed, "GAMER")));
        Assert.Equal(
            ["Morning Drift"],
            Names(ScriptedSessionRack.Arrange(all, AllBands, ScriptedSessionSort.Installed, "gEnTlE")));

        // Trimmed: " drift " matches, where the untrimmed needle matches nothing at all because no
        // name or description carries a space on both sides of it.
        Assert.Equal(
            ["Morning Drift"],
            Names(ScriptedSessionRack.Arrange(all, AllBands, ScriptedSessionSort.Installed, "  drift  ")));

        // Whitespace alone is not a filter, so the rack stays whole.
        Assert.Equal(2, ScriptedSessionRack.Arrange(all, AllBands, ScriptedSessionSort.Installed, "   ").Count);

        // A miss is empty rather than everything.
        Assert.Empty(ScriptedSessionRack.Arrange(all, AllBands, ScriptedSessionSort.Installed, "zzz"));
    }

    /// <summary>
    /// A <c>.session.json</c> carrying an explicit <c>null</c> name or description deserializes to a
    /// null string, and the rack must not take the app down over one. The row simply does not match
    /// (<see cref="ScriptedSessionRack"/>'s <c>Holds</c>), which is upstream's <c>?? ""</c>
    /// (<c>MainWindow/MainWindow.SessionIO.cs:295-296</c>) reached by another road.
    /// </summary>
    [Fact]
    public void ASessionFileWithNullText_IsARowThatMatchesNothing_NotACrash()
    {
        var broken = ScriptedSession.Parse("""{"id":"x","name":null,"description":null}""");
        Assert.NotNull(broken);

        IReadOnlyList<ScriptedSession> all = [broken, Session("Real", ScriptedSessionDifficulty.Easy, 30)];

        Assert.Equal(2, ScriptedSessionRack.Arrange(all, AllBands, ScriptedSessionSort.Installed, "").Count);
        Assert.Equal(
            ["Real"],
            Names(ScriptedSessionRack.Arrange(all, AllBands, ScriptedSessionSort.Installed, "real")));
    }

    // =====================================================================================
    //  THE ORDERS
    // =====================================================================================

    /// <summary>
    /// Upstream's four orders (<c>MainWindow/MainWindow.SessionIO.cs:320-332</c>), each at values
    /// where its SECOND key decides the answer: two Easy sessions of different lengths, and two
    /// 45-minute sessions of different bands. A sort that read only the band, or only the duration,
    /// or read either of them backwards, answers differently here.
    /// </summary>
    [Fact]
    public void TheFourOrdersAreUpstreams_DownToTheirSecondKey()
    {
        // Installed order is deliberately none of the answers below.
        IReadOnlyList<ScriptedSession> all =
        [
            Session("Long Easy", ScriptedSessionDifficulty.Easy, 45),
            Session("Hard One", ScriptedSessionDifficulty.Hard, 60),
            Session("Short Easy", ScriptedSessionDifficulty.Easy, 30),
            Session("Medium One", ScriptedSessionDifficulty.Medium, 45),
        ];

        Assert.Equal(
            ["Long Easy", "Hard One", "Short Easy", "Medium One"],
            Names(ScriptedSessionRack.Arrange(all, AllBands, ScriptedSessionSort.Installed, "")));

        Assert.Equal(
            ["Hard One", "Long Easy", "Medium One", "Short Easy"],
            Names(ScriptedSessionRack.Arrange(all, AllBands, ScriptedSessionSort.Name, "")));

        // Easiest: band ascending, then the SHORTER of the two Easy ones first.
        Assert.Equal(
            ["Short Easy", "Long Easy", "Medium One", "Hard One"],
            Names(ScriptedSessionRack.Arrange(all, AllBands, ScriptedSessionSort.Easiest, "")));

        // Hardest: band descending, then the LONGER first — upstream's ThenByDescending, which is
        // not a mirror of the line above by accident.
        Assert.Equal(
            ["Hard One", "Medium One", "Long Easy", "Short Easy"],
            Names(ScriptedSessionRack.Arrange(all, AllBands, ScriptedSessionSort.Hardest, "")));

        Assert.Equal(
            ["Short Easy", "Long Easy", "Medium One", "Hard One"],
            Names(ScriptedSessionRack.Arrange(all, AllBands, ScriptedSessionSort.Shortest, "")));
    }

    /// <summary>
    /// Upstream's tie-break: rows that a sort cannot separate come out in the order the rack read
    /// them, "without it a sort on a field half the sessions share (three 45-minute Easy runs) would
    /// shuffle on each repaint" (<c>MainWindow/MainWindow.SessionIO.cs:306-308</c>).
    ///
    /// <para><b>The order is also asserted after a FILTER has removed a row</b>, because that is
    /// where a tie-break keyed off the surviving rows rather than the whole rack would start
    /// disagreeing with itself.</para>
    /// </summary>
    [Fact]
    public void TiedRowsKeepTheOrderTheRackReadThemIn_EvenAfterAFilterHasThinnedIt()
    {
        IReadOnlyList<ScriptedSession> all =
        [
            Session("Third", ScriptedSessionDifficulty.Easy, 45),
            Session("First", ScriptedSessionDifficulty.Medium, 45),
            Session("Second", ScriptedSessionDifficulty.Easy, 45),
            Session("Fourth", ScriptedSessionDifficulty.Easy, 45),
        ];

        // Every Easy row ties on duration, so only the read order can order them.
        Assert.Equal(
            ["Third", "Second", "Fourth", "First"],
            Names(ScriptedSessionRack.Arrange(all, AllBands, ScriptedSessionSort.Easiest, "")));

        var easyOnly = new HashSet<ScriptedSessionDifficulty> { ScriptedSessionDifficulty.Easy };
        Assert.Equal(
            ["Third", "Second", "Fourth"],
            Names(ScriptedSessionRack.Arrange(all, easyOnly, ScriptedSessionSort.Shortest, "")));
    }

    // =====================================================================================
    //  WHAT THE STRIP SAYS
    // =====================================================================================

    /// <summary>
    /// Upstream's count (<c>MainWindow/MainWindow.SessionIO.cs:744-746</c>, <c>en.json:74-75</c>):
    /// the total while nothing is filtered out, "n of m" the moment something is — and a zero is a
    /// filtered state like any other, not a special case.
    /// </summary>
    [Fact]
    public void TheCountLineSaysWhetherAnythingIsHidden()
    {
        Assert.Equal("4 sessions", ScriptedSessionRack.CountLine(4, 4));
        Assert.Equal("2 of 4", ScriptedSessionRack.CountLine(2, 4));
        Assert.Equal("0 of 4", ScriptedSessionRack.CountLine(0, 4));

        // Upstream's own format would read "1 sessions" here; this port pluralises, in the idiom
        // the rack's phase count already uses.
        Assert.Equal("1 session", ScriptedSessionRack.CountLine(1, 1));
        Assert.Equal("1 of 4", ScriptedSessionRack.CountLine(1, 4));
    }

    /// <summary>
    /// The orders this port offers, and the two of upstream's six it refuses. <c>Recent</c> would be
    /// install order under a name that claims recency, and <c>Highest XP</c> would order by a number
    /// the row deliberately does not print — both argued at <see cref="ScriptedSessionSort"/>.
    ///
    /// <para>Pinned as a SET rather than a count, so a member added later is named in the failure
    /// instead of shifting a number: an order that arrives without its refusal being reopened reds
    /// here.</para>
    /// </summary>
    [Fact]
    public void TheOrdersOnOfferAreTheFourUpstreamOnesPlusAnHonestDefault()
    {
        Assert.Equal(
            ["As installed", "Name A-Z", "Easiest first", "Hardest first", "Shortest first"],
            Enum.GetValues<ScriptedSessionSort>().Select(ScriptedSessionRack.SortLabel).ToArray());

        Assert.DoesNotContain(
            Enum.GetValues<ScriptedSessionSort>(),
            sort => ScriptedSessionRack.SortLabel(sort).Contains("XP", StringComparison.Ordinal)
                || ScriptedSessionRack.SortLabel(sort).Contains("Recent", StringComparison.Ordinal));
    }

    // =====================================================================================
    //  AND THE SAME RULES ON THE FOUR FILES THE APP ACTUALLY SHIPS
    // =====================================================================================

    /// <summary>
    /// The rack the user really gets, off the real <c>.session.json</c> files beside the binary
    /// (<see cref="ScriptedSession.ReadBuiltIns"/>) — the same four rows
    /// <c>ScriptedSessionSurfaceTests</c> reads its cells from.
    ///
    /// <para>Every expected order below is a DIFFERENT permutation of the same four rows, so a sort
    /// that quietly did nothing would fail three of them, and the search needle is a word that
    /// appears in exactly one shipped file.</para>
    /// </summary>
    [Fact]
    public void TheFourShippedSessionsFilterSortAndSearchAsTheRackWillDrawThem()
    {
        var all = ScriptedSession.ReadBuiltIns();
        Assert.Equal(4, all.Count);

        // As installed: file-name order (ScriptedSession.ReadFolder), which is what the rack has
        // drawn since it landed.
        Assert.Equal(
            ["The Distant Doll", "Gamer Girl", "Good Girls Don't Cum", "Morning Drift"],
            Names(ScriptedSessionRack.Arrange(all, AllBands, ScriptedSessionSort.Installed, "")));

        Assert.Equal(
            ["Gamer Girl", "Good Girls Don't Cum", "Morning Drift", "The Distant Doll"],
            Names(ScriptedSessionRack.Arrange(all, AllBands, ScriptedSessionSort.Name, "")));

        // Morning Drift (easy, 30) then The Distant Doll (easy, 45) then Gamer Girl (medium, 45)
        // then Good Girls Don't Cum (hard, 60).
        Assert.Equal(
            ["Morning Drift", "The Distant Doll", "Gamer Girl", "Good Girls Don't Cum"],
            Names(ScriptedSessionRack.Arrange(all, AllBands, ScriptedSessionSort.Easiest, "")));

        Assert.Equal(
            ["Good Girls Don't Cum", "Gamer Girl", "The Distant Doll", "Morning Drift"],
            Names(ScriptedSessionRack.Arrange(all, AllBands, ScriptedSessionSort.Hardest, "")));

        // Shortest: 30, then the two 45s in the order the rack read them, then 60.
        Assert.Equal(
            ["Morning Drift", "The Distant Doll", "Gamer Girl", "Good Girls Don't Cum"],
            Names(ScriptedSessionRack.Arrange(all, AllBands, ScriptedSessionSort.Shortest, "")));

        // The one Hard file, and the one Medium file.
        Assert.Equal(
            ["Good Girls Don't Cum"],
            Names(ScriptedSessionRack.Arrange(
                all,
                new HashSet<ScriptedSessionDifficulty> { ScriptedSessionDifficulty.Hard },
                ScriptedSessionSort.Installed,
                "")));

        // No shipped session is Extreme, so that band is a filter with nothing behind it — which is
        // a fact about the content rather than about the filter, and it is the state the rack's
        // empty line answers.
        Assert.Empty(ScriptedSessionRack.Arrange(
            all,
            new HashSet<ScriptedSessionDifficulty> { ScriptedSessionDifficulty.Extreme },
            ScriptedSessionSort.Installed,
            ""));

        // "borderless" is in the Gamer Girl file's description ("Set your game to Borderless Windowed
        // mode for the full experience!") and in no other shipped file at all — a needle that is on
        // no NAME, so a search that only read names would come back empty here.
        Assert.Equal(
            ["Gamer Girl"],
            Names(ScriptedSessionRack.Arrange(all, AllBands, ScriptedSessionSort.Installed, "borderless")));

        // And a search across a band filter is an AND, never an OR.
        Assert.Empty(ScriptedSessionRack.Arrange(
            all,
            new HashSet<ScriptedSessionDifficulty> { ScriptedSessionDifficulty.Hard },
            ScriptedSessionSort.Installed,
            "borderless"));
    }
}
