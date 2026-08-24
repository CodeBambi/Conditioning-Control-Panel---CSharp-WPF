namespace CcpClient.Desktop.Session;

/// <summary>
/// The session rack's VIEW over the sessions on disk: which rows a filter lets through, what order
/// they come out in, and the lines that describe the result.
///
/// <para>Upstream's rack toolbar (<c>Views/Tabs/PresetsTabView.xaml:788-850</c>) and the three
/// methods behind it — <c>RackAccepts</c> (<c>MainWindow/MainWindow.SessionIO.cs:274-302</c>),
/// <c>SortRackSessions</c> (<c>:310-352</c>) and <c>UpdateRackToolbarCounts</c>
/// (<c>:728-747</c>). Upstream states the rule this file keeps: "every control here is a VIEW over
/// the same list — none of them mutate a session" (<c>Views/Tabs/PresetsTabView.xaml:789-790</c>).
/// Nothing here reads a file, writes one, or touches a running session.</para>
///
/// <para><b>Why a pure function rather than a method on the page.</b> Upstream's rack rebuilds
/// itself from the registry on every toolbar touch, and its filter, its sort and its tie-break are
/// the only part of that a test can reach without a window. Splitting the arithmetic out means the
/// ordering facts are unit facts, and the page keeps one job: draw what this returns.</para>
///
/// <para><b>The labels live here, beside the enum they name</b>, rather than with the rack's other
/// prose in <c>Views/Pages/SessionRackNotices.cs</c>. They are one-to-one with
/// <see cref="ScriptedSessionSort"/>'s members, and a label in another file is a label that rots
/// when a member is added.</para>
/// </summary>
public static class ScriptedSessionRack
{
    /// <summary>Upstream's search watermark (<c>en.json:71</c>). Avalonia's own
    /// <c>TextBox.PlaceholderText</c> carries it, where upstream needs a sibling <c>TextBlock</c>
    /// plus a handler to hide it (<c>Views/Tabs/PresetsTabView.xaml:845-848</c>,
    /// <c>MainWindow/MainWindow.SessionIO.cs:808-811</c>) because WPF has no watermark.</summary>
    public const string SearchWatermark = "Search…";

    /// <summary>The line the rack shows instead of rows when a filter has emptied it — upstream's
    /// (<c>en.json:76</c>, placed by <c>MainWindow/MainWindow.SessionIO.cs:234-259</c>): a line
    /// where the rows would be, never a blank panel.</summary>
    public const string NoMatches = "No sessions match — clear a filter.";

    /// <summary>
    /// Apply the toolbar to the sessions on disk: drop what the filters reject, then order what is
    /// left — upstream's own order of operations, on one line
    /// (<c>MainWindow/MainWindow.SessionIO.cs:228</c>).
    /// </summary>
    /// <param name="all">Every session the rack knows about, in the order it read them. This is
    /// also the TIE-BREAK for every sort, which is upstream's rule and upstream says why: without
    /// it "a sort on a field half the sessions share (three 45-minute Easy runs) would shuffle on
    /// each repaint" (<c>MainWindow/MainWindow.SessionIO.cs:306-308</c>).</param>
    /// <param name="difficulties">The bands the user has left switched on. All four by default; an
    /// empty set is a real state and it empties the rack, which is what <see cref="NoMatches"/> is
    /// for.</param>
    /// <param name="sort">Which order (<c>MainWindow/MainWindow.SessionIO.cs:318-351</c>).</param>
    /// <param name="search">The search box's text. Trimmed here, so a box holding only spaces is
    /// not a filter (<c>MainWindow/MainWindow.SessionIO.cs:815-816</c>).</param>
    public static IReadOnlyList<ScriptedSession> Arrange(
        IReadOnlyList<ScriptedSession> all,
        IReadOnlySet<ScriptedSessionDifficulty> difficulties,
        ScriptedSessionSort sort,
        string search)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(difficulties);

        var needle = (search ?? string.Empty).Trim();
        var rows = all
            .Select((session, index) => (Session: session, Index: index))
            .Where(row => Accepts(row.Session, difficulties, needle))
            .ToList();

        IEnumerable<(ScriptedSession Session, int Index)> ordered = sort switch
        {
            // Upstream's six, minus two — see ScriptedSessionSort for which and why.
            ScriptedSessionSort.Name => rows
                .OrderBy(row => row.Session.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Index),
            ScriptedSessionSort.Easiest => rows
                .OrderBy(row => (int)row.Session.Difficulty)
                .ThenBy(row => row.Session.DurationMinutes)
                .ThenBy(row => row.Index),
            ScriptedSessionSort.Hardest => rows
                .OrderByDescending(row => (int)row.Session.Difficulty)
                .ThenByDescending(row => row.Session.DurationMinutes)
                .ThenBy(row => row.Index),
            ScriptedSessionSort.Shortest => rows
                .OrderBy(row => row.Session.DurationMinutes)
                .ThenBy(row => row.Index),

            // INSTALLED is the incoming order, already filtered — which is what upstream's own
            // tie-break resolves to when nothing has re-ordered the rows.
            _ => rows,
        };

        return [.. ordered.Select(row => row.Session)];
    }

    /// <summary>
    /// Upstream's two filters on one row (<c>MainWindow/MainWindow.SessionIO.cs:289-300</c>): the
    /// difficulty band, then the search.
    ///
    /// <para><b>The search reads the name and the WHOLE description</b>, as upstream's does
    /// (<c>MainWindow/MainWindow.SessionIO.cs:295-299</c>), even though the row shows only the
    /// description's first line — the rest of it is on the row's tooltip, so a hit the cell does
    /// not show is still a hit the user can read. Upstream searches its mode-aware text because its
    /// sessions are re-worded per persona; this port has no persona rewrite for a session file, so
    /// it searches the authored text.</para>
    /// </summary>
    private static bool Accepts(
        ScriptedSession session,
        IReadOnlySet<ScriptedSessionDifficulty> difficulties,
        string needle)
    {
        if (!difficulties.Contains(session.Difficulty))
        {
            return false;
        }

        return needle.Length == 0
            || Holds(session.Name, needle)
            || Holds(session.Description, needle);
    }

    /// <summary>Case-insensitive containment, upstream's <c>OrdinalIgnoreCase</c>
    /// (<c>MainWindow/MainWindow.SessionIO.cs:297-298</c>). Takes a nullable string because a
    /// <c>.session.json</c> carrying an explicit <c>null</c> deserializes to one.</summary>
    private static bool Holds(string? text, string needle) =>
        text is not null && text.Contains(needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The count beside the rack — upstream's <c>TxtRackCount</c>
    /// (<c>MainWindow/MainWindow.SessionIO.cs:744-746</c>, <c>en.json:74-75</c>): the total when
    /// nothing is filtered out, and "n of m" when something is.
    ///
    /// <para>Upstream's unfiltered form is <c>"{0} sessions"</c> and would read "1 sessions" on an
    /// install carrying one file. Pluralised here, in the idiom this rack already uses for its
    /// phase count (<c>Views/Pages/SessionRackNotices.cs:332-335</c>).</para>
    /// </summary>
    public static string CountLine(int shown, int total) => shown == total
        ? total == 1 ? "1 session" : $"{total} sessions"
        : $"{shown} of {total}";

    /// <summary>The sort's face text — upstream's own words for the four orders it shares
    /// (<c>en.json:66-69</c>).</summary>
    public static string SortLabel(ScriptedSessionSort sort) => sort switch
    {
        ScriptedSessionSort.Name => "Name A-Z",
        ScriptedSessionSort.Easiest => "Easiest first",
        ScriptedSessionSort.Hardest => "Hardest first",
        ScriptedSessionSort.Shortest => "Shortest first",
        _ => "As installed",
    };
}

/// <summary>
/// The orders the rack offers — upstream's sort combo
/// (<c>Views/Tabs/PresetsTabView.xaml:820-834</c>) minus two of its six, each refused for a reason
/// rather than quietly dropped.
///
/// <para><b>Upstream's <c>recent</c> is not here, and it is upstream's DEFAULT.</b> It orders by
/// the backing file's last-write time (<c>MainWindow/MainWindow.SessionIO.cs:336-349</c>), which is
/// recency only because upstream's user can create, import and edit sessions — every one of those
/// stamps a file. Nothing in this build writes a <c>.session.json</c>, so every stamp here is the
/// moment the install laid the file down, and "Recent" would be install order wearing a name that
/// claims otherwise. <see cref="Installed"/> is the honest form of the same default, and it is this
/// port's deterministic file-name order (<see cref="ScriptedSession.ReadFolder"/>) rather than
/// upstream's <c>Directory.GetFiles</c> order.</para>
///
/// <para><b>Upstream's <c>xp</c> is not here either</b>
/// (<c>MainWindow/MainWindow.SessionIO.cs:333-334</c>, ordering by
/// <see cref="ScriptedSession.BonusXP"/>). The rack row deliberately does not show upstream's
/// <c>+{0} XP</c> cell, because nothing in this build awards it
/// (<c>Views/Pages/SessionRackNotices.cs:127-130</c>) — and an order over a number the row refuses
/// to print is an order the user cannot see the reason for.</para>
/// </summary>
public enum ScriptedSessionSort
{
    /// <summary>The order the sessions were read in — this build's default, in place of upstream's
    /// <c>recent</c>.</summary>
    Installed,

    /// <summary>By name, case-insensitively (upstream <c>name</c>,
    /// <c>MainWindow/MainWindow.SessionIO.cs:320-322</c>).</summary>
    Name,

    /// <summary>Gentlest band first, then shortest (upstream <c>easiest</c>,
    /// <c>MainWindow/MainWindow.SessionIO.cs:323-326</c>).</summary>
    Easiest,

    /// <summary>Hardest band first, then longest (upstream <c>hardest</c>,
    /// <c>MainWindow/MainWindow.SessionIO.cs:327-330</c>).</summary>
    Hardest,

    /// <summary>By duration (upstream <c>shortest</c>,
    /// <c>MainWindow/MainWindow.SessionIO.cs:331-332</c>).</summary>
    Shortest,
}
