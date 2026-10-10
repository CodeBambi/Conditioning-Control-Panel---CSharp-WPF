using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Providers;
using ConditioningControlPanel.Services.Invites;
using ConditioningControlPanel.Services.Lobby;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The Tonight Board providers lane: the pure halves of Live, Waiting, Resume, Event and Tip,
/// read through the real English strings so a missing key shows up as a raw key in an assert.</summary>
public class BillboardProvidersTests
{
    // =====================================================================================
    //  helpers
    // =====================================================================================

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

    private static readonly Lazy<Dictionary<string, string>> English = new(() => LoadLanguage("en"));

    private static Dictionary<string, string> LoadLanguage(string code)
    {
        var path = Path.Combine(AppDir(), "Localization", "Languages", code + ".json");
        var obj = JObject.Parse(File.ReadAllText(path));
        return obj.Properties().ToDictionary(p => p.Name, p => (string?)p.Value ?? "");
    }

    private static string Loc(string key) => English.Value.TryGetValue(key, out var v) ? v : key;

    private static LobbyRow Open(LobbyGame game, string key, string host, bool friend = false) =>
        new() { Game = game, State = LobbyRowState.Open, Key = key, HostName = host, Friend = friend };

    private static LobbySnapshot Snap(IEnumerable<LobbyRow>? open = null, IEnumerable<LobbyRow>? playing = null,
        IEnumerable<LobbyRow>? friends = null, bool signedIn = true) =>
        new((open ?? Array.Empty<LobbyRow>()).ToList(), (playing ?? Array.Empty<LobbyRow>()).ToList(),
            (friends ?? Array.Empty<LobbyRow>()).ToList(), signedIn);

    private static InviteMine Invites(params InviteSlotState[] states) =>
        new(true, new InviteSnapshot(states.Select((s, i) => new InviteSlot("CODE" + i, s, null, null)).ToList(), null, 0));

    // =====================================================================================
    //  Live
    // =====================================================================================

    [Fact]
    public void Live_signed_out_or_empty_says_nothing()
    {
        Assert.Null(LiveCards.Decide(null, Loc));
        Assert.Null(LiveCards.Decide(LobbySnapshot.Empty, Loc));
        Assert.Null(LiveCards.Decide(Snap(new[] { Open(LobbyGame.Chess, "p_1", "ann") }, signedIn: false), Loc));
        Assert.Null(LiveCards.Decide(Snap(), Loc));
    }

    [Fact]
    public void Live_a_friends_table_comes_first_and_joins_it()
    {
        var card = LiveCards.Decide(Snap(
            open: new[] { Open(LobbyGame.Goon, "ABCD", "stranger") },
            friends: new[] { Open(LobbyGame.Chess, "p_9", "ann", friend: true) }), Loc)!;

        Assert.Equal(LiveCards.CardJoinFriend, card.Id);
        Assert.Equal(BillboardCardKind.Live, card.Kind);
        Assert.Equal("ann is waiting", card.Title);
        Assert.Equal("Chess, and the second seat is yours", card.Line);
        Assert.Equal(BillboardActionKind.Callback, card.Action.Kind);
        Assert.Equal("join:chess:p_9", card.Action.Target);
        Assert.Equal("Join", card.Action.Label);
        Assert.Equal(CardArt.Tables, card.ArtKey);
    }

    [Fact]
    public void Live_one_open_table_joins_it()
    {
        var card = LiveCards.Decide(Snap(open: new[] { Open(LobbyGame.Goon, "ABCD", "bo") }), Loc)!;
        Assert.Equal(LiveCards.CardTables, card.Id);
        Assert.Equal("1 open table", card.Title);
        Assert.Equal("Goon Game, waiting for a second player", card.Line);
        Assert.Equal("join:goon:ABCD", card.Action.Target);
    }

    [Fact]
    public void Live_several_tables_open_the_lobby()
    {
        var card = LiveCards.Decide(Snap(open: new[]
        {
            Open(LobbyGame.Goon, "ABCD", "bo"),
            Open(LobbyGame.Chess, "p_1", "cy"),
            Open(LobbyGame.Chess, "p_2", "di"),
        }), Loc)!;
        Assert.Equal("3 open tables", card.Title);
        Assert.Equal("Chess and Goon Game, each waiting for a second player", card.Line);
        Assert.Equal(BillboardActionKind.Tab, card.Action.Kind);
        Assert.Equal("availablesubjects", card.Action.Target);
        Assert.Equal(3, ((IReadOnlyDictionary<string, int>)card.ArtData!)["count"]);
    }

    [Fact]
    public void Live_game_lists_read_as_a_sentence()
    {
        Assert.Equal("", CardText.JoinAnd(Loc, Array.Empty<string>()));
        Assert.Equal("a", CardText.JoinAnd(Loc, new[] { "a" }));
        Assert.Equal("a and b", CardText.JoinAnd(Loc, new[] { "a", "b" }));
        Assert.Equal("a, b and c", CardText.JoinAnd(Loc, new[] { "a", "b", "c" }));
    }

    [Fact]
    public void Live_a_remote_seat_goes_through_the_lobby_page()
    {
        var card = LiveCards.Decide(Snap(open: new[] { Open(LobbyGame.Remote, "u_1", "eve") }), Loc)!;
        Assert.Equal(BillboardActionKind.Tab, card.Action.Kind);
        Assert.Equal("availablesubjects", card.Action.Target);
    }

    [Fact]
    public void Live_a_friend_in_a_game_with_no_seat_points_at_the_lobby()
    {
        var playing = new LobbyRow { Game = LobbyGame.Chess, State = LobbyRowState.Playing, HostName = "ann", Friend = true };
        var card = LiveCards.Decide(Snap(friends: new[] { playing }), Loc)!;
        Assert.Equal(LiveCards.CardPlaying, card.Id);
        Assert.Equal("ann is in a game", card.Title);
        Assert.Equal("Chess, no seat open yet", card.Line);
        Assert.Equal(BillboardActionKind.Tab, card.Action.Kind);
    }

    [Theory]
    [InlineData("join:chess:p_1", true, LobbyGame.Chess, "p_1")]
    [InlineData("join:goon:AB:CD", true, LobbyGame.Goon, "AB:CD")]
    [InlineData("join:remote:u_1", false, LobbyGame.Chess, "")]
    [InlineData("join:chess:", false, LobbyGame.Chess, "")]
    [InlineData("join:", false, LobbyGame.Chess, "")]
    [InlineData("invites", false, LobbyGame.Chess, "")]
    [InlineData(null, false, LobbyGame.Chess, "")]
    public void Live_targets_read_back_only_what_the_provider_issued(string? target, bool ok, LobbyGame game, string key)
    {
        Assert.Equal(ok, LiveCards.TryParseTarget(target, out var g, out var k));
        Assert.Equal(key, k);
        if (ok) Assert.Equal(game, g);
    }

    // =====================================================================================
    //  Waiting
    // =====================================================================================

    [Fact]
    public void Waiting_quests_count_what_is_left()
    {
        var two = WaitingCards.Quests(3, 1, Loc)!;
        Assert.Equal(WaitingCards.CardQuests, two.Id);
        Assert.Equal(BillboardCardKind.Waiting, two.Kind);
        Assert.Equal("2 quests left today", two.Title);
        Assert.Equal("1 of 3 done so far", two.Line);
        Assert.Equal("quests", two.Action.Target);

        Assert.Equal("One quest left today", WaitingCards.Quests(3, 2, Loc)!.Title);
        Assert.Null(WaitingCards.Quests(3, 3, Loc));
        Assert.Null(WaitingCards.Quests(3, 9, Loc));
        Assert.Null(WaitingCards.Quests(0, 0, Loc));
        Assert.Equal("0 of 3 done so far", WaitingCards.Quests(3, -2, Loc)!.Line);
    }

    [Fact]
    public void Waiting_program_day_until_it_is_done()
    {
        Assert.Null(WaitingCards.Program(null, Loc));
        Assert.Null(WaitingCards.Program(new ProgramToday("Kept", 3, 14, true, true), Loc));
        Assert.Null(WaitingCards.Program(new ProgramToday(" ", 3, 14, false, false), Loc));

        var fresh = WaitingCards.Program(new ProgramToday("Kept", 3, 14, false, false), Loc)!;
        Assert.Equal("Day 3 of Kept", fresh.Title);
        Assert.Equal("today's session is still waiting", fresh.Line);
        Assert.Equal("programs", fresh.Action.Target);
        Assert.Equal(CardArt.Calendar, fresh.ArtKey);

        var tasks = WaitingCards.Program(new ProgramToday("Kept", 3, 14, true, false), Loc)!;
        Assert.Equal("the session is done, a task is still open", tasks.Line);
    }

    [Fact]
    public void Waiting_invite_only_for_subscribers_with_a_code_left()
    {
        var open = Invites(InviteSlotState.Open);
        Assert.Null(WaitingCards.Invite(BillboardTier.Free, open, Loc));
        Assert.Null(WaitingCards.Invite(BillboardTier.Basic, Invites(InviteSlotState.Trying), Loc));
        Assert.Null(WaitingCards.Invite(BillboardTier.Basic, InviteMine.Unreachable, Loc));
        Assert.Null(WaitingCards.Invite(BillboardTier.Prime, null, Loc));

        var card = WaitingCards.Invite(BillboardTier.Prime, open, Loc)!;
        Assert.Equal(WaitingCards.CardInvite, card.Id);
        Assert.Equal(BillboardActionKind.Callback, card.Action.Kind);
        Assert.Equal(WaitingCards.InviteCallback, card.Action.Target);
        Assert.Equal(CardArt.Invite, card.ArtKey);
    }

    [Fact]
    public void Waiting_signals_speak_only_when_the_answer_flips()
    {
        WaitingSignals.ResetForTests();
        int raised = 0;
        WaitingSignals.InvitesChanged += () => raised++;
        try
        {
            WaitingSignals.NoteInvites(InviteMine.Unreachable);
            Assert.Equal(0, raised);
            WaitingSignals.NoteInvites(Invites(InviteSlotState.Open));
            Assert.Equal(1, raised);
            WaitingSignals.NoteInvites(Invites(InviteSlotState.Open, InviteSlotState.Trying));
            Assert.Equal(1, raised);
            WaitingSignals.NoteInvites(Invites(InviteSlotState.Converted));
            Assert.Equal(2, raised);
        }
        finally { WaitingSignals.ResetForTests(); }
    }

    // =====================================================================================
    //  Resume
    // =====================================================================================

    private static readonly DateTime Now = new(2026, 10, 7, 21, 0, 0, DateTimeKind.Local);

    [Fact]
    public void Resume_session_names_it_and_starts_it()
    {
        var card = ResumeCards.Session(new LastSession("deep_pink", "Deep Pink", Now.AddHours(-2), 40.2), Now, false, Loc)!;
        Assert.Equal(ResumeCards.CardSession, card.Id);
        Assert.Equal(BillboardCardKind.Resume, card.Kind);
        Assert.Equal("Deep Pink, 40 min", card.Title);
        Assert.Equal("your last session, earlier today", card.Line);
        Assert.Equal(BillboardActionKind.Callback, card.Action.Kind);
        Assert.Equal("session:deep_pink", card.Action.Target);
        Assert.Equal(CardArt.Spiral, card.ArtKey);

        var shortOne = ResumeCards.Session(new LastSession("x", "Quick", Now.AddHours(-1), 0.4), Now, false, Loc)!;
        Assert.Equal("Quick", shortOne.Title);
    }

    [Fact]
    public void Resume_session_hides_while_running_too_old_or_nameless()
    {
        var last = new LastSession("deep_pink", "Deep Pink", Now.AddDays(-1), 40);
        Assert.Null(ResumeCards.Session(null, Now, false, Loc));
        Assert.Null(ResumeCards.Session(last, Now, true, Loc));
        Assert.Null(ResumeCards.Session(last with { EndedLocal = Now.AddDays(-15) }, Now, false, Loc));
        Assert.Null(ResumeCards.Session(last with { SessionId = "" }, Now, false, Loc));
        Assert.Null(ResumeCards.Session(last with { Name = " " }, Now, false, Loc));
    }

    [Fact]
    public void Resume_when_reads_like_a_person()
    {
        Assert.Equal("your last session, earlier today", ResumeCards.When(Now.AddMinutes(-30), Now, Loc));
        Assert.Equal("your last session, yesterday", ResumeCards.When(Now.Date.AddMinutes(-1), Now, Loc));
        var weekday = ResumeCards.When(Now.AddDays(-3), Now, Loc);
        Assert.StartsWith("your last session, ", weekday);
        Assert.NotEqual("your last session, ", weekday);
        var older = ResumeCards.When(Now.AddDays(-10), Now, Loc);
        Assert.NotEqual(weekday, older);
    }

    [Fact]
    public void Resume_deeper_shows_the_file_name_and_plays_it()
    {
        Assert.Null(ResumeCards.Deeper(null, Loc));
        Assert.Null(ResumeCards.Deeper("  ", Loc));
        var path = @"C:\lib\Deep_Pink.ccpenh.json";
        var card = ResumeCards.Deeper(path, Loc)!;
        Assert.Equal("Deep Pink", card.Title);
        Assert.Equal("your last Deeper file", card.Line);
        Assert.Equal("deeper:" + path, card.Action.Target);
        Assert.Equal(CardArt.Poster, card.ArtKey);
        Assert.Equal("features/deeper.png", card.ArtData);
        Assert.Equal("Other", ResumeCards.DeeperTitle(@"C:\x\Other.json"));
    }

    // =====================================================================================
    //  Event
    // =====================================================================================

    private static readonly DateTime Oct12 = new(2026, 10, 12, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Locktober_needs_the_link_and_october()
    {
        Assert.Null(EventCards.Locktober(false, Oct12, new RaffleReading(6, 3600), Loc));
        Assert.Null(EventCards.Locktober(true, new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc), null, Loc));
        Assert.Null(EventCards.Locktober(true, new DateTime(2026, 11, 1, 0, 0, 1, DateTimeKind.Utc), null, Loc));
    }

    [Fact]
    public void Locktober_without_a_reading_explains_the_raffle()
    {
        var card = EventCards.Locktober(true, Oct12, null, Loc)!;
        Assert.Equal(EventCards.CardLocktober, card.Id);
        Assert.Equal(BillboardCardKind.Event, card.Kind);
        Assert.Equal("Locktober", card.Title);
        Assert.Equal("every day CCP adds time counts toward the raffle", card.Line);
        Assert.Equal("chaster", card.Action.Target);
        Assert.Equal("Open the tab", card.Action.Label);
        Assert.Equal(CardArt.Calendar, card.ArtKey);
    }

    [Fact]
    public void Locktober_counts_days_then_time()
    {
        var six = EventCards.Locktober(true, Oct12, new RaffleReading(6, 3600), Loc)!;
        Assert.Equal("6 of 25 days", six.Title);
        Assert.Equal("19 more counted days to reach the raffle", six.Line);
        var art = (IReadOnlyDictionary<string, int>)six.ArtData!;
        Assert.Equal(6, art["counted"]);
        Assert.Equal(25, art["need"]);
        Assert.Equal(31, art["days"]);
        Assert.Equal(12, art["today"]);

        var oct30 = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal("one more counted day to reach the raffle",
            EventCards.Locktober(true, oct30, new RaffleReading(24, 3600), Loc)!.Line);

        var time = EventCards.Locktober(true, oct30, new RaffleReading(25, 31 * 3600 - 19200), Loc)!;
        Assert.Equal("25 of 25 days", time.Title);
        Assert.Equal("the days are in, 5:20 more on the lock and you are in the draw", time.Line);

        var inDraw = EventCards.Locktober(true, oct30, new RaffleReading(26, 40 * 3600), Loc)!;
        Assert.Equal("In the draw", inDraw.Title);
        Assert.Equal("26 days counted, the ticket number comes on the 1st", inDraw.Line);
    }

    [Fact]
    public void Locktober_out_of_reach_says_so_plainly()
    {
        var oct20 = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        var card = EventCards.Locktober(true, oct20, new RaffleReading(3, 3600), Loc)!;
        Assert.Equal("Locktober", card.Title);
        Assert.Equal("3 days counted. The raffle is out of reach, the lock is not", card.Line);

        // Still reachable on the same day with 13 counted (13 + 12 open = 25).
        Assert.Equal("13 of 25 days", EventCards.Locktober(true, oct20, new RaffleReading(13, 0), Loc)!.Title);
    }

    [Theory]
    [InlineData(19200, "5:20")]
    [InlineData(1, "0:01")]
    [InlineData(0, "0:00")]
    [InlineData(-50, "0:00")]
    [InlineData(36000, "10:00")]
    public void Locktober_clock_is_hours_and_minutes(long seconds, string expected) =>
        Assert.Equal(expected, EventCards.Clock(seconds));

    // =====================================================================================
    //  Tip
    // =====================================================================================

    [Fact]
    public void Tips_are_for_prime_only()
    {
        Assert.Empty(TipCards.Decide(BillboardTier.Free, Oct12, Loc));
        Assert.Empty(TipCards.Decide(BillboardTier.Basic, Oct12, Loc));
        var tips = TipCards.Decide(BillboardTier.Prime, Oct12, Loc);
        Assert.Equal(TipCards.Table.Count, tips.Count);
        Assert.True(tips.Count >= 8);
        Assert.Equal(tips.Count, tips.Select(t => t.Id).Distinct().Count());
        Assert.Equal(Enumerable.Range(0, tips.Count), tips.Select(t => t.Priority));
        Assert.All(tips, t =>
        {
            Assert.Equal(BillboardCardKind.Tip, t.Kind);
            Assert.Equal(BillboardActionKind.Tab, t.Action.Kind);
            Assert.Equal(CardArt.Tip, t.ArtKey);
            Assert.Equal("did you know", t.Eyebrow);
            Assert.False(t.Title.StartsWith("billboard_card_", StringComparison.Ordinal), t.Title);
            Assert.False(t.Line.StartsWith("billboard_card_", StringComparison.Ordinal), t.Line);
        });
    }

    [Fact]
    public void Tips_come_in_table_order_whatever_the_clock_says()
    {
        // The deck turns the tips, one per cycle; the provider never rotates by wall clock.
        var a = TipCards.Decide(BillboardTier.Prime, Oct12, Loc);
        var b = TipCards.Decide(BillboardTier.Prime, Oct12.AddHours(5).AddMinutes(17), Loc);
        Assert.Equal(TipCards.Table.Select(t => TipCards.IdPrefix + t.Id), a.Select(c => c.Id));
        Assert.Equal(a.Select(c => c.Id), b.Select(c => c.Id));
    }

    [Fact]
    public void Every_tip_opens_a_real_page()
    {
        var nav = File.ReadAllText(Path.Combine(AppDir(), "MainWindow", "MainWindow.TabNavigation.cs"));
        foreach (var tip in TipCards.Table)
            Assert.True(nav.Contains("case \"" + tip.Tab + "\"", StringComparison.Ordinal), "no ShowTab case for " + tip.Tab);
        foreach (var tab in new[] { "quests", "programs", "chaster", "availablesubjects", "presets", "deeper" })
            Assert.True(nav.Contains("case \"" + tab + "\"", StringComparison.Ordinal), "no ShowTab case for " + tab);
    }

    // =====================================================================================
    //  the lane as a whole
    // =====================================================================================

    [Fact]
    public void CreateAll_hands_back_the_five_providers()
    {
        var all = BillboardProviders.CreateAll();
        Assert.Equal(new[] { "live", "waiting", "resume", "event", "tip" }, all.Select(p => p.Id));
    }

    [Fact]
    public void Every_card_wears_an_allowed_art_key()
    {
        var cards = new List<BillboardCardSpec?>
        {
            LiveCards.Decide(Snap(open: new[] { Open(LobbyGame.Goon, "ABCD", "bo") }), Loc),
            WaitingCards.Quests(3, 0, Loc),
            WaitingCards.Program(new ProgramToday("Kept", 1, 14, false, false), Loc),
            WaitingCards.Invite(BillboardTier.Basic, Invites(InviteSlotState.Open), Loc),
            ResumeCards.Session(new LastSession("a", "A", Now, 10), Now, false, Loc),
            ResumeCards.Deeper(@"C:\a.ccpenh.json", Loc),
            EventCards.Locktober(true, Oct12, null, Loc),
        };
        cards.AddRange(TipCards.Decide(BillboardTier.Prime, Oct12, Loc));
        Assert.All(cards, c =>
        {
            Assert.NotNull(c);
            Assert.Contains(c!.ArtKey, CardArt.All);
            Assert.Matches("^#[0-9a-f]{6}$", c.AccentHex);
            Assert.True(c.Snoozable);
            Assert.False(string.IsNullOrWhiteSpace(c.Action.Label));
        });
    }

    private static IReadOnlyList<string> KeysUsed()
    {
        var dir = Path.Combine(AppDir(), "Services", "Billboard", "Providers");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(dir, "*.cs"))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"(billboard_card_[a-z0-9_]+)\""))
                if (!m.Groups[1].Value.EndsWith("_", StringComparison.Ordinal)) keys.Add(m.Groups[1].Value);
        foreach (var tip in TipCards.Table)
        {
            keys.Add("billboard_card_tip_" + tip.Id + "_title");
            keys.Add("billboard_card_tip_" + tip.Id + "_line");
        }
        return keys.OrderBy(k => k).ToList();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("pt-BR")]
    [InlineData("ru")]
    [InlineData("zh-CN")]
    public void Every_card_key_is_in_every_language(string code)
    {
        var lang = LoadLanguage(code);
        var keys = KeysUsed();
        Assert.True(keys.Count > 40, "the key scan found too little: " + keys.Count);
        var missing = keys.Where(k => !lang.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, code + " is missing: " + string.Join(", ", missing));
    }

    [Fact]
    public void Card_copy_keeps_the_house_voice()
    {
        foreach (var (key, value) in English.Value.Where(kv => kv.Key.StartsWith("billboard_card_", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain('\u2014', value);
            Assert.DoesNotContain('\u2013', value);
            Assert.DoesNotContain('!', value);
            Assert.False(Regex.IsMatch(value, @"\b(she|her|hers)\b", RegexOptions.IgnoreCase), key + ": " + value);
        }
    }
}
