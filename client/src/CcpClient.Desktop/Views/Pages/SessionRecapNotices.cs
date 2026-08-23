using System.Globalization;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// The sentences the <b>Session Complete</b> recap and the <b>Recent Sessions</b> history say —
/// upstream's <c>Windows/SessionCompleteWindow.xaml(.cs)</c> and
/// <c>Windows/SessionLogHistoryWindow.xaml(.cs)</c>.
///
/// <para>Here rather than in the two windows' AXAML for the reason
/// <see cref="SessionRackNotices"/> gives: every string below is a ported contract with a citation,
/// and a sentence here is checked by a unit fact where a sentence in markup needs a mounted
/// window.</para>
///
/// <para><b>One clock format for both windows, and it is the rack's.</b> Upstream formats the same
/// span two different ways in the two windows and one of them is wrong: the recap's duration cell
/// is <c>$"{log.Duration.Minutes:D2}:{log.Duration.Seconds:D2}"</c>
/// (<c>SessionCompleteWindow.xaml.cs:95</c>) — <c>TimeSpan.Minutes</c>, which wraps at 60, so a
/// COMPLETED sixty-minute session (<c>good_girls_dont_cum</c>, the longest shipped one) shows
/// <c>00:00</c>. Its history row does not have the bug (<c>SessionLogHistoryWindow.xaml.cs:83-85</c>
/// branches on <c>TotalHours &gt;= 1</c>). The port uses
/// <see cref="SessionRackNotices.Clock"/> — total minutes, never wrapped — in both, which is the
/// form already on this surface's own countdown (<c>MainWindow/MainWindow.Presets.cs:1752</c>), so
/// the recap's duration and the button the user just pressed agree.</para>
/// </summary>
public static class SessionRecapNotices
{
    /// <summary>Upstream's recap window title (<c>en.json:2783</c>).</summary>
    public const string RecapTitle = "Session Complete";

    /// <summary>Upstream's history window title and its own header line
    /// (<c>en.json:2801</c>, <c>SessionLogHistoryWindow.xaml:5</c>, <c>:52</c>).</summary>
    public const string HistoryTitle = "Recent Sessions";

    /// <summary>Upstream's door button (<c>en.json:2799</c> "📜 Recent",
    /// <c>Views/Tabs/PresetsTabView.xaml:646-651</c>), glyph-stripped as every ported caption on
    /// this shell is (§9 D8).</summary>
    public const string RecentButton = "Recent sessions";

    /// <summary>Upstream's tooltip on that button (<c>en.json:2800</c>). Its promise survives the
    /// port intact — the count of videos and images each run played is exactly what the log
    /// keeps.</summary>
    public const string RecentButtonTooltip =
        "Browse the last 20 sessions and the videos/images each one played";

    /// <summary>Upstream's abort headline (<c>en.json:2792</c>,
    /// <c>SessionCompleteWindow.xaml.cs:87</c>).</summary>
    public const string EndedEarly = "Session Ended Early";

    /// <summary>Upstream's completion headline (<c>en.json:2784</c>).</summary>
    public const string GoodGirl = "Good Girl!";

    /// <summary>Upstream's completion headline for one session and one only
    /// (<c>en.json:2789</c>, <c>SessionCompleteWindow.xaml.cs:80-82</c>).</summary>
    public const string GamerGirlGoodGirl = "GG, Good Girl!";

    /// <summary>Upstream's stat labels (<c>en.json:2785</c>, <c>:958</c>).</summary>
    public const string SessionLabel = "Session";

    /// <inheritdoc cref="SessionLabel"/>
    public const string DurationLabel = "Duration";

    /// <summary>Upstream's media panel header (<c>en.json:2790</c>).</summary>
    public const string MediaPlayedHeader = "Played This Session";

    /// <summary>Upstream's empty-media line (<c>en.json:2791</c>).</summary>
    public const string NoMedia = "No videos or images played this session.";

    /// <summary>Upstream's close button (<c>en.json:2788</c> "💗 Continue"),
    /// glyph-stripped.</summary>
    public const string Continue = "Continue";

    /// <summary>Upstream's empty-history line (<c>en.json:2795</c>).</summary>
    public const string NoHistory = "No session logs yet. Run a session and one will appear here.";

    /// <summary>Upstream's history close button (<c>en.json:902</c>).</summary>
    public const string Close = "Close";

    /// <summary>Upstream's two status words (<c>en.json:2770</c>, <c>:2794</c>).</summary>
    public const string Completed = "Completed";

    /// <inheritdoc cref="Completed"/>
    public const string Aborted = "Aborted";

    /// <summary>Upstream's two media type labels (<c>en.json:2179</c>, <c>:2793</c>).</summary>
    public const string VideoLabel = "VIDEO";

    /// <inheritdoc cref="VideoLabel"/>
    public const string ImageLabel = "IMAGE";

    /// <summary>
    /// <b>The refusal, said where the user reads it.</b> Upstream's media row carries the file's
    /// name and opens its folder when clicked
    /// (<c>Windows/SessionCompleteWindow.xaml.cs:234-236</c>, <c>:173-194</c>). This build records
    /// the KIND and the MINUTE and nothing else — the paths never leave the module that drew them
    /// (<c>Effects/FlashImagesEffect.cs:151-155</c>,
    /// <c>Effects/MandatoryVideoEffect.cs:9-10</c>) — so the row cannot name a file and the click
    /// would have nothing to open. Named here rather than left as a blank column, on §9 D7's rule.
    /// </summary>
    public const string NamesNotRecorded =
        "Which files played is not recorded: this build logs what kind of media appeared and when, "
        + "never a name or a path.";

    /// <summary>
    /// <b>The second refusal.</b> Upstream's recap has a third stat column, <c>+N XP</c>, hidden on
    /// an abort (<c>SessionCompleteWindow.xaml.cs:90</c>, <c>:96</c>), plus a random completion
    /// card image (<c>:22-27</c>, <c>:128-146</c>) and a completion sound (<c>:148-171</c>). None of
    /// the three is here: nothing in this build awards XP (the same refusal
    /// <see cref="SessionRackNotices.RowMeta"/> already carries), and neither the three
    /// <c>Cards/*.png</c> nor <c>lvup.mp3</c> ships in this build's assets — a card border drawn
    /// around a missing image is upstream's own collapsed state (<c>:135-137</c>).
    /// </summary>
    public const string AwardsNotComputed =
        "No XP is awarded for a session in this build, so this recap shows none.";

    /// <summary>
    /// The headline — upstream's, including the one session that gets its own
    /// (<c>SessionCompleteWindow.xaml.cs:78-87</c>).
    /// </summary>
    public static string Headline(ScriptedSessionLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (!log.Completed)
        {
            return EndedEarly;
        }

        return string.Equals(log.SessionId, "gamer_girl", StringComparison.Ordinal)
            ? GamerGirlGoodGirl
            : GoodGirl;
    }

    /// <summary>
    /// The line under it: the icon, the name, and — only when it finished — upstream's
    /// <c>Completed</c> (<c>:83</c>, <c>:88</c>, both trimmed as upstream trims them).
    /// </summary>
    public static string Subtitle(ScriptedSessionLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var head = $"{log.SessionIcon} {log.SessionName}".Trim();
        return log.Completed ? $"{head} {Completed}".Trim() : head;
    }

    /// <summary>Upstream's count cell (<c>en.json:2797</c>, filled at
    /// <c>SessionCompleteWindow.xaml.cs:122-124</c> and again at
    /// <c>SessionLogHistoryWindow.xaml.cs:87-89</c>).</summary>
    public static string MediaCount(ScriptedSessionLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        return string.Create(
            CultureInfo.InvariantCulture, $"{log.VideoCount} videos · {log.ImageCount} images");
    }

    /// <summary>One media row: upstream's time cell then its type cell
    /// (<c>SessionCompleteWindow.xaml:145-160</c>), with upstream's third cell — the file's display
    /// name — refused (see <see cref="NamesNotRecorded"/>).</summary>
    public static string MediaRow(ScriptedMediaEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return $"{SessionRackNotices.Clock(entry.SessionTime)}  {Kind(entry.Kind)}";
    }

    /// <summary>Upstream's two type labels (<c>SessionCompleteWindow.xaml.cs:243-252</c>).</summary>
    public static string Kind(ScriptedMediaKind kind) =>
        kind == ScriptedMediaKind.Video ? VideoLabel : ImageLabel;

    /// <summary>Upstream's status word and its colour, per row
    /// (<c>SessionLogHistoryWindow.xaml.cs:91-100</c>): the same green a completed run gets and the
    /// same orange an aborted one gets.</summary>
    public static string Status(ScriptedSessionLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        return log.Completed ? Completed : Aborted;
    }

    /// <summary>Upstream's status colours (<c>SessionLogHistoryWindow.xaml.cs:94</c>,
    /// <c>:99</c>).</summary>
    public static string StatusColour(ScriptedSessionLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        return log.Completed ? "#FF90EE90" : "#FFFFA500";
    }

    /// <summary>
    /// One history row's detail line — upstream's three cells on one line, in upstream's order and
    /// with upstream's separator (<c>SessionLogHistoryWindow.xaml:100-104</c>).
    ///
    /// <para><b>The time is converted back to LOCAL</b> before a user reads it. Upstream's is local
    /// already because its clock is (<c>SessionLogService.cs:57</c>, <c>DateTime.Now</c>); the
    /// port's clock is UTC (slice 1's recorded divergence, <c>Session/ScriptedClock.cs</c>), so a
    /// run at nine in the evening has to be shown as nine in the evening rather than as whatever
    /// the offset makes of it.</para>
    /// </summary>
    public static string HistoryRow(ScriptedSessionLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var started = log.StartedAt.ToLocalTime().DateTime.ToString("g", CultureInfo.CurrentCulture);
        return $"{started}  ·  {SessionRackNotices.Clock(log.Duration)}  ·  {MediaCount(log)}";
    }

    /// <summary>The history's own count line (<c>en.json:2796</c>,
    /// <c>SessionLogHistoryWindow.xaml.cs:35</c>).</summary>
    public static string HistoryCount(int rows) =>
        string.Create(CultureInfo.InvariantCulture, $"{rows} sessions");

    /// <summary>The history row's title cell: upstream's icon and name
    /// (<c>SessionLogHistoryWindow.xaml:87-89</c>), with the rack's own fallback for a log written
    /// from a session that carried no icon.</summary>
    public static string HistoryTitleFor(ScriptedSessionLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var icon = string.IsNullOrWhiteSpace(log.SessionIcon) ? "\U0001F3AC" : log.SessionIcon;
        return $"{icon} {log.SessionName}".Trim();
    }
}
